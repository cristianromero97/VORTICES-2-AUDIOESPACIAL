using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;

namespace Vortices
{
    /// <summary>
    /// SonidosPanel: Se adjunta a un GameObject DENTRO del Canvas de un VRPanel existente.
    /// No crea su propio Canvas — usa el Canvas padre (WorldSpace del VRPanel).
    /// Llena todo el espacio del Canvas con la lista de audios y botones de emisión.
    /// </summary>
    public class SonidosPanel : MonoBehaviour
    {
        [Header("Actividad")]
        [Tooltip("Radio en metros para detectar que el jugador se acercó al emisor activo.")]
        [SerializeField] private float proximityRadius = 2.5f;

        [Tooltip("Panel de respuesta de dirección.")]
        [SerializeField] private DirectionResponsePanel directionPanel;

        [Header("Editor / Debug")]
        [Tooltip("Tecla para simular acercamiento al emisor activo (útil sin VR).")]
        [SerializeField] private KeyCode simulateProximityKey = KeyCode.F;

        // ─────────────────────────────────────────────
        //  Estado runtime
        // ─────────────────────────────────────────────
        private AudioSource       currentSrc;
        private AudioTargetMarker currentMarker;
        private bool              proximityTriggered;
        private bool              initialized;
        private int               _selectedIndex;

        // ─────────────────────────────────────────────
        //  UI runtime
        // ─────────────────────────────────────────────
        private CanvasGroup   panelGroup;
        private ScrollRect    _sonidosScrollRect;
        private RectTransform _sonidosContentRT;
        private Transform     rowContainer;
        private readonly List<SoundRow> rows = new List<SoundRow>();

        public static SonidosPanel instance;

        // Colores de fila
        private static readonly Color RowNormal   = new Color(0.13f, 0.13f, 0.18f, 0.9f);
        private static readonly Color RowSelected = new Color(0.18f, 0.32f, 0.56f, 0.95f);

        // ─────────────────────────────────────────────
        //  Inner data
        // ─────────────────────────────────────────────
        private class SoundRow
        {
            public AudioTargetMarker marker;
            public AudioSource       src;
            public Button            button;
            public TextMeshProUGUI   buttonLabel;
            public Image             rowImage;
        }

        // ─────────────────────────────────────────────
        //  Unity lifecycle
        // ─────────────────────────────────────────────
        private void Awake()
        {
            instance = this;
            BuildUI();
            SetVisible(false);
            // Registrar handler de sincronización (se reemplaza si hay instancias previas)
            NetworkClient.RegisterHandler<SonidosSyncMessage>(OnSonidosSyncReceived);
        }

        private void OnDestroy()
        {
            NetworkClient.UnregisterHandler<SonidosSyncMessage>();
        }

        private void Update()
        {
            if (!initialized) return;

            // Mouse scroll wheel para la lista de sonidos
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.001f && _sonidosScrollRect != null && !proximityTriggered)
                _sonidosScrollRect.verticalNormalizedPosition = Mathf.Clamp01(
                    _sonidosScrollRect.verticalNormalizedPosition + wheel * 0.08f);

            // Navegación por teclado (solo cuando el panel de direcciones NO está activo)
            if (!proximityTriggered && rows.Count > 0)
            {
                if (Input.GetKeyDown(KeyCode.Alpha2))          // 2 → siguiente
                {
                    _selectedIndex = Mathf.Min(_selectedIndex + 1, rows.Count - 1);
                    UpdateRowHighlight();
                }
                else if (Input.GetKeyDown(KeyCode.Alpha0))     // 0 → anterior
                {
                    _selectedIndex = Mathf.Max(_selectedIndex - 1, 0);
                    UpdateRowHighlight();
                }
                else if (Input.GetKeyDown(KeyCode.Alpha1))     // 1 → emitir/silenciar seleccionado
                {
                    OnEmitirClicked(rows[_selectedIndex]);
                }
            }

            if (Input.GetKeyDown(simulateProximityKey) && currentSrc != null && !proximityTriggered)
                TriggerProximity();

            if (currentSrc == null || proximityTriggered || Camera.main == null) return;

            float dist = Vector3.Distance(currentSrc.transform.position,
                                          Camera.main.transform.position);
            if (dist <= proximityRadius)
                TriggerProximity();
        }

        // ─────────────────────────────────────────────
        //  API pública
        // ─────────────────────────────────────────────

        public void Initialize(List<(AudioTargetMarker marker, AudioSource src)> sounds)
        {
            ClearRows();
            foreach ((AudioTargetMarker marker, AudioSource src) in sounds)
                BuildRow(marker, src);

            Canvas.ForceUpdateCanvases();
            if (_sonidosContentRT != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_sonidosContentRT);
            if (_sonidosScrollRect != null)
                _sonidosScrollRect.verticalNormalizedPosition = 1f;

            initialized = true;
        }

        public void Show()
        {
            SetVisible(true);
        }

        public void MuteCurrentAudio() => StopCurrent();

        public void OnDirectionResponded()
        {
            StopCurrent();
            SetVisible(true);
        }

        [ContextMenu("Mostrar panel de prueba")]
        private void TestShow()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[SonidosPanel] Iniciar Play Mode primero para probar el panel.");
                return;
            }

            ClearRows();

            for (int i = 1; i <= 3; i++)
            {
                GameObject go = new GameObject($"TestMarker_{i}");
                AudioTargetMarker marker = go.AddComponent<AudioTargetMarker>();
                marker.roomIndex     = i;
                marker.prefabType    = "TestObj";
                marker.audioFileName = $"audio_prueba_{i}";

                GameObject srcGO = new GameObject("UserAudio");
                srcGO.transform.SetParent(go.transform, false);
                AudioSource src = srcGO.AddComponent<AudioSource>();
                src.playOnAwake = false;

                marker.userAudioSource = src;
                BuildRow(marker, src);
            }

            initialized = true;
            SetVisible(true);
            Debug.Log("[SonidosPanel] Panel de prueba mostrado. Usa F para simular proximidad.");
        }

        // ─────────────────────────────────────────────
        //  Lógica interna
        // ─────────────────────────────────────────────
        private void TriggerProximity()
        {
            if (directionPanel != null)
            {
                proximityTriggered = true;
                directionPanel.Show(currentMarker, this);
            }
            // Si no hay DirectionResponsePanel (ej: Museum), no se bloquea la navegación
        }

        private void OnEmitirClicked(SoundRow row)
        {
            // Online: ejecutar la acción localmente (sin esperar relay de vuelta)
            // y notificar al servidor para que retransmita a los DEMÁS clientes.
            // El servidor excluye al emisor → nadie escucha doble.
            if (NetworkClient.isConnected &&
                SessionManager.instance != null && SessionManager.instance.isOnlineSession)
            {
                if (currentSrc == row.src)
                {
                    // Toggle: ya estaba reproduciendo esta fila → silenciar
                    StopCurrent();
                    NetworkClient.Send(new SonidosSyncMessage { audioIndex = -1 });
                }
                else
                {
                    // Nueva fila: tocar localmente + avisar a los demás
                    StopCurrent();
                    PlayRow(row);
                    NetworkClient.Send(new SonidosSyncMessage { audioIndex = rows.IndexOf(row) });
                }
            }
            else
            {
                // Offline: acción local directa
                if (currentSrc == row.src)
                    StopCurrent();
                else
                {
                    StopCurrent();
                    PlayRow(row);
                }
            }
        }

        /// <summary>Recibe el mensaje retransmitido por el servidor y ejecuta la acción localmente.</summary>
        private void OnSonidosSyncReceived(SonidosSyncMessage msg)
        {
            if (msg.audioIndex < 0)
            {
                StopCurrent();
            }
            else if (msg.audioIndex < rows.Count)
            {
                StopCurrent();
                PlayRow(rows[msg.audioIndex]);
            }
            else
            {
                Debug.LogWarning($"[SonidosPanel] SonidosSync: índice {msg.audioIndex} fuera de rango " +
                                 $"(rows.Count={rows.Count}). ¿Panel aún no inicializado?");
            }
        }

        private void PlayRow(SoundRow row)
        {
            currentSrc         = row.src;
            currentMarker      = row.marker;
            proximityTriggered = false;

            row.src.time = 0f;
            row.src.Play();

            row.buttonLabel.text   = "Silenciar";
            row.button.image.color = new Color(0.72f, 0.26f, 0.1f, 1f);

            SourceIdentificationPanel.instance?.SetActiveMarker(row.marker);
        }

        private void StopCurrent()
        {
            if (currentSrc == null) return;

            currentSrc.Stop();
            currentSrc.time = 0f;

            foreach (SoundRow r in rows)
            {
                if (r.src != currentSrc) continue;
                r.buttonLabel.text   = "Emitir";
                r.button.image.color = new Color(0.15f, 0.55f, 0.2f, 1f);
                break;
            }

            currentSrc         = null;
            currentMarker      = null;
            proximityTriggered = false;

            SourceIdentificationPanel.instance?.SetActiveMarker(null);
        }

        private void ClearRows()
        {
            if (rowContainer != null)
                foreach (Transform child in rowContainer)
                    Destroy(child.gameObject);
            rows.Clear();
            currentSrc     = null;
            currentMarker  = null;
            _selectedIndex = 0;
        }

        private void UpdateRowHighlight()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].rowImage == null) continue;
                rows[i].rowImage.color = i == _selectedIndex ? RowSelected : RowNormal;
            }
        }

        private void SetVisible(bool visible)
        {
            if (panelGroup == null) return;
            panelGroup.alpha          = visible ? 1f : 0f;
            panelGroup.interactable   = visible;
            panelGroup.blocksRaycasts = visible;
        }

        // ─────────────────────────────────────────────
        //  Construcción de UI (sin Canvas propio — usa el Canvas padre del VRPanel)
        // ─────────────────────────────────────────────
        private void BuildUI()
        {
            // Este GO es hijo del Canvas del VRPanel. Solo necesita CanvasGroup + RectTransform.
            panelGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            // Llenar todo el espacio disponible del Canvas padre
            RectTransform rt = GetComponent<RectTransform>();
            if (rt == null) rt = gameObject.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // Fondo
            MakeImage("BG", transform, new Color(0.07f, 0.07f, 0.12f, 0.96f), stretch: true);

            // Header
            GameObject header = MakeImage("Header", transform, new Color(0.1f, 0.2f, 0.52f, 1f), stretch: false);
            AnchorRect(header, 0f, 0.88f, 1f, 1f);
            MakeText("HeaderText", header.transform, "Sonidos", 80f, FontStyles.Bold,
                      TextAlignmentOptions.Center, Color.white, stretch: true);

            // Hint tecla de prueba
            GameObject hint = MakeRect("Hint", transform);
            AnchorRect(hint, 0f, 0.84f, 1f, 0.88f);
            MakeText("HintText", hint.transform,
                      $"[0] anterior  [1] emitir  [2] siguiente  [{simulateProximityKey}] acercamiento", 36f,
                      FontStyles.Italic, TextAlignmentOptions.Center,
                      new Color(0.5f, 0.5f, 0.6f), stretch: true);

            // ▲/▼ botones de scroll
            GameObject sndScrollUp = MakeRect("ScrollUp", transform);
            AnchorRect(sndScrollUp, 0.2f, 0.79f, 0.8f, 0.84f);
            Button sndUpBtn = BuildScrollButton(sndScrollUp.transform, "▲");
            sndUpBtn.onClick.AddListener(() => { if (_sonidosScrollRect != null) _sonidosScrollRect.verticalNormalizedPosition = Mathf.Clamp01(_sonidosScrollRect.verticalNormalizedPosition + 0.2f); });

            GameObject sndScrollDown = MakeRect("ScrollDown", transform);
            AnchorRect(sndScrollDown, 0.2f, 0f, 0.8f, 0.05f);
            Button sndDownBtn = BuildScrollButton(sndScrollDown.transform, "▼");
            sndDownBtn.onClick.AddListener(() => { if (_sonidosScrollRect != null) _sonidosScrollRect.verticalNormalizedPosition = Mathf.Clamp01(_sonidosScrollRect.verticalNormalizedPosition - 0.2f); });

            // ScrollRect para la lista de sonidos
            GameObject sndScrollGO = MakeRect("SonidosScrollView", transform);
            AnchorRect(sndScrollGO, 0f, 0.05f, 1f, 0.79f);
            sndScrollGO.GetComponent<RectTransform>().offsetMin = new Vector2(6f, 0f);
            sndScrollGO.GetComponent<RectTransform>().offsetMax = new Vector2(-6f, 0f);

            _sonidosScrollRect = sndScrollGO.AddComponent<ScrollRect>();
            _sonidosScrollRect.horizontal        = false;
            _sonidosScrollRect.vertical          = true;
            _sonidosScrollRect.scrollSensitivity = 30f;

            GameObject sndViewport = MakeRect("Viewport", sndScrollGO.transform);
            StretchFill(sndViewport);
            sndViewport.AddComponent<Image>().color = Color.white;
            sndViewport.AddComponent<Mask>().showMaskGraphic = false;
            _sonidosScrollRect.viewport = sndViewport.GetComponent<RectTransform>();

            GameObject sndContent = MakeRect("Content", sndViewport.transform);
            _sonidosContentRT = sndContent.GetComponent<RectTransform>();
            _sonidosContentRT.anchorMin = new Vector2(0f, 1f);
            _sonidosContentRT.anchorMax = new Vector2(1f, 1f);
            _sonidosContentRT.pivot     = new Vector2(0.5f, 1f);
            _sonidosContentRT.offsetMin = Vector2.zero;
            _sonidosContentRT.offsetMax = Vector2.zero;
            sndContent.AddComponent<Image>().color = Color.clear;

            VerticalLayoutGroup vlg = sndContent.AddComponent<VerticalLayoutGroup>();
            vlg.spacing                = 10f;
            vlg.padding                = new RectOffset(8, 8, 8, 8);
            vlg.childAlignment         = TextAnchor.UpperCenter;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;

            ContentSizeFitter sndCsf = sndContent.AddComponent<ContentSizeFitter>();
            sndCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _sonidosScrollRect.content = _sonidosContentRT;
            rowContainer = sndContent.transform;
        }

        private void BuildRow(AudioTargetMarker marker, AudioSource src)
        {
            GameObject rowGO = MakeRect($"Row_{marker.roomIndex}", rowContainer);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 120f;
            Image rowImg = rowGO.AddComponent<Image>();
            rowImg.color = RowNormal;

            HorizontalLayoutGroup hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
            hlg.padding                = new RectOffset(15, 15, 8, 8);
            hlg.spacing                = 16f;
            hlg.childAlignment         = TextAnchor.MiddleLeft;
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth  = false;
            hlg.childControlHeight     = true;
            hlg.childControlWidth      = true;

            string label = string.IsNullOrEmpty(marker.audioFileName)
                ? $"Sala {marker.roomIndex}"
                : marker.audioFileName;

            TextMeshProUGUI nameLabel = MakeText("Name", rowGO.transform, label, 50f,
                                                  FontStyles.Normal, TextAlignmentOptions.Left,
                                                  Color.white, stretch: false);
            nameLabel.overflowMode = TextOverflowModes.Ellipsis;
            nameLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            SoundRow row = new SoundRow { marker = marker, src = src, rowImage = rowImg };
            GameObject btnGO = MakeRect("EmitirBtn", rowGO.transform);
            Image btnImg = btnGO.AddComponent<Image>();
            btnImg.color = new Color(0.15f, 0.55f, 0.2f, 1f);

            Button btn = btnGO.AddComponent<Button>();
            btn.transition = Selectable.Transition.None; // cambios de color manuales sin interferencia

            LayoutElement btnLE = btnGO.AddComponent<LayoutElement>();
            btnLE.minWidth = btnLE.preferredWidth = 180f;

            row.buttonLabel = MakeText("BtnText", btnGO.transform, "Emitir", 50f,
                                       FontStyles.Bold, TextAlignmentOptions.Center,
                                       Color.white, stretch: true);
            row.button = btn;
            rows.Add(row);

            SoundRow captured = row;
            int rowIndex = rows.Count - 1;
            btn.onClick.AddListener(() =>
            {
                _selectedIndex = rowIndex;
                UpdateRowHighlight();
                OnEmitirClicked(captured);
            });

            UpdateRowHighlight();
        }

        // ─────────────────────────────────────────────
        //  Helpers UI
        // ─────────────────────────────────────────────
        private static GameObject MakeRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject MakeImage(string name, Transform parent, Color color, bool stretch)
        {
            GameObject go = MakeRect(name, parent);
            go.AddComponent<Image>().color = color;
            if (stretch) StretchFill(go);
            return go;
        }

        private static TextMeshProUGUI MakeText(string name, Transform parent, string text,
                                                 float size, FontStyles style,
                                                 TextAlignmentOptions align, Color color, bool stretch)
        {
            GameObject go = MakeRect(name, parent);
            if (stretch) StretchFill(go);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.fontStyle = style;
            tmp.alignment = align; tmp.color = color;
            return tmp;
        }

        private static void StretchFill(GameObject go)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private static void AnchorRect(GameObject go, float xMin, float yMin, float xMax, float yMax)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(xMin, yMin); rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private static Button BuildScrollButton(Transform parent, string label)
        {
            Color bg = new Color(0.2f, 0.3f, 0.6f, 0.9f);
            GameObject go = MakeRect("Btn_" + label, parent);
            StretchFill(go);
            go.AddComponent<Image>().color = bg;
            Button btn = go.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor      = bg;
            cb.highlightedColor = Color.Lerp(bg, Color.white, 0.28f);
            cb.pressedColor     = Color.Lerp(bg, Color.black, 0.28f);
            cb.selectedColor    = cb.highlightedColor;
            btn.colors = cb;
            MakeText("BtnLabel", go.transform, label, 52f,
                      FontStyles.Bold, TextAlignmentOptions.Center, Color.white, stretch: true);
            return btn;
        }
    }
}
