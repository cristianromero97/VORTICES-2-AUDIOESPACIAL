using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace Vortices
{
    /// <summary>
    /// SourceIdentificationPanel: Se adjunta a un GO DENTRO del Canvas de un VRPanel.
    /// No crea Canvas propio — usa el Canvas padre del VRPanel.
    ///
    /// Flujo: Q1 (sala, lista) → Confirmar sala → Q2 (objeto, lista) → Confirmar objeto → Completado
    ///
    /// Navegación por teclado:
    ///   Q1 lista → Flechas Arr/Abajo entre salas, Enter para seleccionar.
    ///   Confirmar → Flechas Izq/Der entre Sí y No, Enter para confirmar.
    ///   Q2 lista → Flechas Arr/Abajo entre botones, Enter para seleccionar.
    ///   Completado → Flechas Izq/Der entre Repetir y Finalizar, Enter.
    /// </summary>
    public class SourceIdentificationPanel : MonoBehaviour
    {
        // ─────────────────────────────────────────────
        //  Inspector
        // ─────────────────────────────────────────────
        [Header("Actividad")]
        [Tooltip("Radio XZ en metros para detectar que el jugador se acercó a un emisor.")]
        [SerializeField] private float proximityRadius = 1.5f;

        [Header("Editor / Debug")]
        [Tooltip("Tecla para simular acercamiento al primer emisor disponible.")]
        [SerializeField] private KeyCode simulateProximityKey = KeyCode.G;

        // ─────────────────────────────────────────────
        //  Estado
        // ─────────────────────────────────────────────
        private enum PanelState { Idle, Q1Room, ConfirmRoom, Q2Object, ConfirmObject, Completed, Results }

        public static SourceIdentificationPanel instance;

        private PanelState        _state = PanelState.Idle;
        private AudioTargetMarker _currentMarker;
        private AudioTargetMarker _activeMarker;   // marker cuyo "Emitir" fue presionado en SonidosPanel
        private string            _roomAnswer;
        private string            _objectAnswer;
        private readonly HashSet<AudioTargetMarker> _answered = new HashSet<AudioTargetMarker>();

        // ─────────────────────────────────────────────
        //  UI refs
        // ─────────────────────────────────────────────
        private CanvasGroup     _group;
        private TextMeshProUGUI _subtitleText;

        // Referencia al teclado virtual de la mano
        private HandKeyboard _handKeyboard;

        // Q1 — lista de salas (modo actual)
        private GameObject    _q1Section;
        private ScrollRect    _roomScrollRect;
        private RectTransform _roomContentRT;
        private Transform     _roomListContent;

        // Q1 — campo de texto (comentado — descomentar para volver al teclado virtual)
        // private TMP_InputField _roomInputField;
        // private Button         _q1ContinueBtn;

        // Q2 — lista de objetos
        private GameObject     _objectSection;
        private ScrollRect     _objectScrollRect;
        private Transform      _objectListContent;
        private RectTransform  _objectContentRT;

        private GameObject      _confirmSection;
        private TextMeshProUGUI _confirmText;
        private Button          _yesBtn;
        private Button          _noBtn;

        private GameObject _completedSection;
        private Button     _repeatBtn;
        private Button     _finalizeBtn;

        private GameObject      _resultsSection;
        private TextMeshProUGUI _roomResultText;
        private TextMeshProUGUI _objectResultText;
        private Button          _closeBtn;

        // Datos calculados al Finalizar
        private bool   _resultCorrectRoom;
        private bool   _resultCorrectObj;
        private string _resultActualRoom;
        private string _resultActualObject;

        // ─────────────────────────────────────────────
        //  Unity lifecycle
        // ─────────────────────────────────────────────
        private void Awake()
        {
            instance = this;
            BuildUI();
            SetVisible(false);

            _handKeyboard = FindObjectOfType<HandKeyboard>();
            if (_handKeyboard == null)
                Debug.LogWarning("[SourceID] HandKeyboard no encontrado en la escena");
        }

        private void Start()
        {
            // Camera.main puede ser null en Awake con XR Origin — se asigna en Start
            Camera cam = Camera.main;
            if (cam == null) cam = FindObjectOfType<Camera>();
            if (cam != null)
            {
                foreach (Canvas c in GetComponentsInParent<Canvas>(true))
                {
                    if (c.renderMode == RenderMode.WorldSpace && c.worldCamera == null)
                        c.worldCamera = cam;
                }
            }
            else
            {
                Debug.LogWarning("[SourceID] No se encontró ninguna cámara para asignar al Canvas.");
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(simulateProximityKey))
                TryTriggerProximity(force: true);

            if (_state == PanelState.Idle)
                TryTriggerProximity(force: false);

            // Mouse scroll wheel → scroll the active list (bypasses raycaster entirely)
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.001f)
            {
                ScrollRect active = _state == PanelState.Q1Room   ? _roomScrollRect :
                                    _state == PanelState.Q2Object  ? _objectScrollRect : null;
                if (active != null)
                    active.verticalNormalizedPosition = Mathf.Clamp01(
                        active.verticalNormalizedPosition + wheel * 0.08f);
            }
        }

        // ─────────────────────────────────────────────
        //  Detección de proximidad
        // ─────────────────────────────────────────────
        // API pública: SonidosPanel llama esto al presionar/silenciar Emitir
        public void SetActiveMarker(AudioTargetMarker marker) => _activeMarker = marker;

        private void TryTriggerProximity(bool force)
        {
            if (_activeMarker == null) return;
            if (_activeMarker.userAudioSource == null) return;
            if (_answered.Contains(_activeMarker)) return;
            if (Camera.main == null) return;

            Vector3 cam  = Camera.main.transform.position;
            Vector3 obj  = _activeMarker.transform.position;
            float   dist = Vector2.Distance(
                new Vector2(cam.x, cam.z),
                new Vector2(obj.x, obj.z));

            if (force || dist <= proximityRadius)
                Trigger(_activeMarker);
        }

        private void Trigger(AudioTargetMarker marker)
        {
            _currentMarker = marker;
            if (_subtitleText != null && !string.IsNullOrEmpty(marker.audioFileName))
                _subtitleText.text = $"\"{marker.audioFileName}\"";
            EnterState(PanelState.Q1Room);
            SetVisible(true);
        }

        // ─────────────────────────────────────────────
        //  Máquina de estados
        // ─────────────────────────────────────────────
        private void EnterState(PanelState s)
        {
            _state = s;

            _q1Section.SetActive(s == PanelState.Q1Room);
            _objectSection.SetActive(s == PanelState.Q2Object);
            _confirmSection.SetActive(s == PanelState.ConfirmRoom || s == PanelState.ConfirmObject);
            _completedSection.SetActive(s == PanelState.Completed);
            _resultsSection.SetActive(s == PanelState.Results);

            switch (s)
            {
                case PanelState.Q1Room:
                    // -- Versión teclado virtual (descomentar para restaurar) --
                    // _roomInputField.text = string.Empty;
                    // _roomInputField.ActivateInputField();
                    // Select(_roomInputField.gameObject);
                    StartCoroutine(BuildRoomButtonsNextFrame());
                    break;

                case PanelState.ConfirmRoom:
                    _confirmText.text = $"¿Estás seguro que el sonido proviene de la sala {_roomAnswer}?";
                    Select(_yesBtn.gameObject);
                    break;

                case PanelState.Q2Object:
                    StartCoroutine(BuildObjectButtonsNextFrame());
                    break;

                case PanelState.ConfirmObject:
                    _confirmText.text = $"¿Estás seguro que el sonido proviene de: {_objectAnswer}?";
                    Select(_yesBtn.gameObject);
                    break;

                case PanelState.Completed:
                    Select(_repeatBtn.gameObject);
                    break;

                case PanelState.Results:
                    PopulateResults();
                    Select(_closeBtn.gameObject);
                    break;
            }
        }

        // ─────────────────────────────────────────────
        //  Q1 — lista de números de sala
        // ─────────────────────────────────────────────
        private IEnumerator BuildRoomButtonsNextFrame()
        {
            for (int i = _roomListContent.childCount - 1; i >= 0; i--)
                DestroyImmediate(_roomListContent.GetChild(i).gameObject);

            yield return null;

            // Recopilar índices únicos de sala desde los FurnitureLabel activos
            var roomSet = new SortedSet<int>();
            foreach (FurnitureLabel lbl in FurnitureLabel.All)
            {
                if (lbl != null) roomSet.Add(lbl.roomIndex);
            }

            var buttons = new List<Button>();

            foreach (int ri in roomSet)
            {
                int capturedRoom = ri;

                GameObject rowGO = MakeRect("Row_Sala" + ri, _roomListContent);
                rowGO.AddComponent<LayoutElement>().preferredHeight = 120f;
                rowGO.AddComponent<Image>().color = new Color(0.12f, 0.2f, 0.45f, 0.95f);

                Button btn = rowGO.AddComponent<Button>();
                ColorBlock cb = btn.colors;
                cb.normalColor      = new Color(0.12f, 0.2f,  0.45f, 0.95f);
                cb.highlightedColor = new Color(0.25f, 0.45f, 0.8f,  1f);
                cb.pressedColor     = new Color(0.08f, 0.15f, 0.35f, 1f);
                cb.selectedColor    = new Color(0.25f, 0.45f, 0.8f,  1f);
                btn.colors = cb;

                MakeText("Label", rowGO.transform, "Sala " + ri, 56f,
                          FontStyles.Bold, TextAlignmentOptions.Center,
                          Color.white, stretch: true);

                btn.onClick.AddListener(() => OnRoomSelected(capturedRoom));
                buttons.Add(btn);
            }

            const float buttonHeight = 120f;
            const float spacing      = 15f;
            const float padding      = 16f;
            float totalHeight = (buttons.Count * buttonHeight)
                              + (Mathf.Max(0, buttons.Count - 1) * spacing)
                              + padding;

            _roomContentRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, totalHeight);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_roomContentRT);

            // Navegación vertical + auto-scroll al seleccionar
            for (int i = 0; i < buttons.Count; i++)
            {
                Navigation nav = new Navigation { mode = Navigation.Mode.Explicit };
                if (i > 0)                  nav.selectOnUp   = buttons[i - 1];
                if (i < buttons.Count - 1)  nav.selectOnDown = buttons[i + 1];
                buttons[i].navigation = nav;

                ScrollOnSelect sos = buttons[i].gameObject.AddComponent<ScrollOnSelect>();
                sos.scrollRect = _roomScrollRect;
            }

            if (buttons.Count > 0)
                Select(buttons[0].gameObject);

            Debug.Log($"[SourceID] Salas disponibles: {buttons.Count}");
        }

        private void OnRoomSelected(int room)
        {
            _roomAnswer = room.ToString();
            EnterState(PanelState.ConfirmRoom);
        }

        // -- Versión teclado virtual (descomentar para restaurar) --
        // private void OnQ1Continue()
        // {
        //     string answer = _roomInputField.text.Trim();
        //     if (string.IsNullOrEmpty(answer)) return;
        //     _roomAnswer = answer;
        //     EnterState(PanelState.ConfirmRoom);
        // }

        // ─────────────────────────────────────────────
        //  Q2 — lista de objetos
        // ─────────────────────────────────────────────
        private IEnumerator BuildObjectButtonsNextFrame()
        {
            for (int i = _objectListContent.childCount - 1; i >= 0; i--)
                DestroyImmediate(_objectListContent.GetChild(i).gameObject);

            yield return null;

            var buttons = new List<Button>();

            foreach (FurnitureLabel lbl in FurnitureLabel.All)
            {
                if (lbl == null || lbl.roomIndex != _currentMarker.roomIndex) continue;

                string captured = lbl.labelDisplayName ?? "Unknown";

                GameObject rowGO = MakeRect("Row_" + captured, _objectListContent);
                rowGO.AddComponent<LayoutElement>().preferredHeight = 120f;
                rowGO.AddComponent<Image>().color = new Color(0.12f, 0.2f, 0.45f, 0.95f);

                Button btn = rowGO.AddComponent<Button>();
                ColorBlock cb = btn.colors;
                cb.normalColor      = new Color(0.12f, 0.2f,  0.45f, 0.95f);
                cb.highlightedColor = new Color(0.25f, 0.45f, 0.8f,  1f);
                cb.pressedColor     = new Color(0.08f, 0.15f, 0.35f, 1f);
                cb.selectedColor    = new Color(0.25f, 0.45f, 0.8f,  1f);
                btn.colors = cb;

                MakeText("Label", rowGO.transform, captured, 56f,
                          FontStyles.Bold, TextAlignmentOptions.Center,
                          Color.white, stretch: true);

                btn.onClick.AddListener(() => OnObjectSelected(captured));
                buttons.Add(btn);
            }

            const float buttonHeight = 120f;
            const float spacing      = 15f;
            const float padding      = 16f;
            float totalHeight = (buttons.Count * buttonHeight)
                              + (Mathf.Max(0, buttons.Count - 1) * spacing)
                              + padding;

            _objectContentRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, totalHeight);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_objectContentRT);
            if (_objectContentRT.parent != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_objectContentRT.parent as RectTransform);

            Debug.Log($"[SourceID] Objetos mostrados: {buttons.Count} | room: {_currentMarker.roomIndex}");

            for (int i = 0; i < buttons.Count; i++)
            {
                Navigation nav = new Navigation { mode = Navigation.Mode.Explicit };
                if (i > 0)                  nav.selectOnUp   = buttons[i - 1];
                if (i < buttons.Count - 1)  nav.selectOnDown = buttons[i + 1];
                buttons[i].navigation = nav;

                ScrollOnSelect sos = buttons[i].gameObject.AddComponent<ScrollOnSelect>();
                sos.scrollRect = _objectScrollRect;
            }

            if (buttons.Count > 0)
                Select(buttons[0].gameObject);
        }

        // ─────────────────────────────────────────────
        //  Callbacks
        // ─────────────────────────────────────────────
        private void OnObjectSelected(string objectLabel)
        {
            _objectAnswer = objectLabel;
            EnterState(PanelState.ConfirmObject);
        }

        private void OnYes()
        {
            if (_state == PanelState.ConfirmRoom)
                EnterState(PanelState.Q2Object);
            else if (_state == PanelState.ConfirmObject)
                Finish();
        }

        private void OnNo()
        {
            if (_state == PanelState.ConfirmRoom)
                EnterState(PanelState.Q1Room);
            else if (_state == PanelState.ConfirmObject)
                EnterState(PanelState.Q2Object);
        }

        private void Finish() => EnterState(PanelState.Completed);

        private void OnRepeat()
        {
            _answered.Clear();
            _activeMarker = null;   // debe presionar Emitir en SonidosPanel para reactivar
            SetVisible(false);
            _state = PanelState.Idle;
            Debug.Log("[SourceID] Actividad repetida — _answered limpiado, esperando nuevo trigger");
        }

        private void OnFinalize()
        {
            _answered.Add(_currentMarker);
            _activeMarker = null;
            ComputeResults();
            LogResponse();
            EnterState(PanelState.Results);
            Debug.Log("[SourceID] Actividad finalizada — mostrando resultados");
        }

        private void OnClose()
        {
            _answered.Remove(_currentMarker);
            SetVisible(false);
            _state = PanelState.Idle;
            Debug.Log("[SourceID] Resultados cerrados");
        }

        // ─────────────────────────────────────────────
        //  Visibilidad
        // ─────────────────────────────────────────────
        private static void Select(GameObject go)
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(go);
        }

        private void SetVisible(bool visible)
        {
            if (_group == null) return;
            _group.alpha          = visible ? 1f : 0f;
            _group.interactable   = visible;
            _group.blocksRaycasts = visible;
            if (!visible) Select(null);
        }

        // ─────────────────────────────────────────────
        //  Resultados
        // ─────────────────────────────────────────────
        private void ComputeResults()
        {
            string actualObject = _currentMarker?.labelDisplayName ?? _currentMarker?.prefabType ?? "?";
            int    actualRoom   = _currentMarker?.roomIndex ?? -1;

            _resultCorrectRoom   = int.TryParse(_roomAnswer, out int rInt) && rInt == actualRoom;
            _resultCorrectObj    = string.Equals(_objectAnswer?.Trim(), actualObject.Trim(),
                                                 System.StringComparison.OrdinalIgnoreCase);
            _resultActualRoom    = actualRoom.ToString();
            _resultActualObject  = actualObject;
        }

        private void PopulateResults()
        {
            Color green = new Color(0.2f, 0.9f, 0.3f);
            Color red   = new Color(1f,   0.3f, 0.3f);

            if (_resultCorrectRoom)
            {
                _roomResultText.text  = $"Sala: Sala {_roomAnswer}  ✓";
                _roomResultText.color = green;
            }
            else
            {
                _roomResultText.text  = $"Sala: Sala {_roomAnswer}  ✗\n(era Sala {_resultActualRoom})";
                _roomResultText.color = red;
            }

            if (_resultCorrectObj)
            {
                _objectResultText.text  = $"Objeto: {_objectAnswer}  ✓";
                _objectResultText.color = green;
            }
            else
            {
                _objectResultText.text  = $"Objeto: {_objectAnswer}  ✗\n(era {_resultActualObject})";
                _objectResultText.color = red;
            }
        }

        // ─────────────────────────────────────────────
        //  CSV
        // ─────────────────────────────────────────────
        private void LogResponse()
        {
            if (_currentMarker == null) return;

            string sessionName  = SessionManager.instance?.sessionName ?? "unknown";
            int    userId       = SessionManager.instance?.userId      ?? -1;
            string soundFile    = _currentMarker.audioFileName         ?? "unknown";
            string actualObject = _currentMarker.labelDisplayName      ?? _currentMarker.prefabType ?? "unknown";
            int    actualRoom   = _currentMarker.roomIndex;
            string timestamp    = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            bool correctRoom = int.TryParse(_roomAnswer, out int rInt) && rInt == actualRoom;
            bool correctObj  = string.Equals(_objectAnswer.Trim(), actualObject.Trim(),
                                             System.StringComparison.OrdinalIgnoreCase);

            string entry = $"{sessionName},{userId},{soundFile},{actualObject},{actualRoom}," +
                           $"{_roomAnswer},{_objectAnswer},{correctRoom},{correctObj},{timestamp}";

            Debug.Log($"[SourceIdentification] {entry}");

            try
            {
                string folder = Path.Combine(Application.dataPath, "Results",
                                             sessionName, userId.ToString());
                Directory.CreateDirectory(folder);
                string path   = Path.Combine(folder, "Source Identification.csv");
                bool   exists = File.Exists(path);
                using (StreamWriter sw = File.AppendText(path))
                {
                    if (!exists)
                        sw.WriteLine("sessionName,userId,soundFile,actualObject,actualRoom," +
                                     "answeredRoom,answeredObject,correctRoom,correctObject,timestamp");
                    sw.WriteLine(entry);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SourceIdentification] Error al guardar CSV: {e.Message}");
            }
        }

        // ─────────────────────────────────────────────
        //  Construcción de UI
        // ─────────────────────────────────────────────
        private void BuildUI()
        {
            _group = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            RectTransform rt = GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            MakeImage("Overlay", transform, new Color(0f, 0f, 0f, 0.75f), stretch: true);

            // Panel central
            GameObject panel = MakeRect("Panel", transform);
            RectTransform panelRT = panel.GetComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0.1f, 0.05f);
            panelRT.anchorMax = new Vector2(0.9f, 0.95f);
            panelRT.offsetMin = Vector2.zero;
            panelRT.offsetMax = Vector2.zero;
            MakeImage("PanelBG", panel.transform, new Color(0.06f, 0.06f, 0.1f, 0.97f), stretch: true);

            // Header
            GameObject header = MakeImage("Header", panel.transform,
                                           new Color(0.1f, 0.2f, 0.52f, 1f), stretch: false);
            AnchorRect(header, 0f, 0.84f, 1f, 1f);
            MakeText("HText", header.transform, "Source Identification", 65f,
                      FontStyles.Bold, TextAlignmentOptions.Center, Color.white, stretch: true);

            // Subtítulo
            GameObject sub = MakeRect("SubTitle", panel.transform);
            AnchorRect(sub, 0f, 0.75f, 1f, 0.84f);
            _subtitleText = MakeText("SubText", sub.transform, "", 44f, FontStyles.Italic,
                                      TextAlignmentOptions.Center,
                                      new Color(0.85f, 0.85f, 0.85f), stretch: true);

            // ── Q1: sala — lista scrolleable ──────────────────
            _q1Section = MakeRect("Q1Section", panel.transform);
            AnchorRect(_q1Section, 0.05f, 0.08f, 0.95f, 0.75f);

            GameObject qRoomArea = MakeRect("QArea", _q1Section.transform);
            AnchorRect(qRoomArea, 0f, 0.88f, 1f, 1f);
            var qRoomTxt = MakeText("QText", qRoomArea.transform,
                     "¿En qué sala identificaste el sonido?", 52f,
                     FontStyles.Normal, TextAlignmentOptions.Center,
                     new Color(0.75f, 0.85f, 1f), stretch: true);
            qRoomTxt.overflowMode = TextOverflowModes.Overflow;

            // -- Versión teclado virtual (descomentar para restaurar) --
            // GameObject fieldArea = MakeRect("FieldArea", _q1Section.transform);
            // AnchorRect(fieldArea, 0.15f, 0.38f, 0.85f, 0.65f);
            // _roomInputField = BuildInputField(fieldArea.transform, "Número de sala...");
            // _roomInputField.onSubmit.AddListener(_ => OnQ1Continue());
            // if (_handKeyboard != null)
            // {
            //     _roomInputField.onSelect.AddListener(_ => _handKeyboard.SetInputField(_roomInputField));
            //     _roomInputField.onDeselect.AddListener(_ => _handKeyboard.RemoveInputField());
            // }
            // GameObject btnCont = MakeRect("BtnArea", _q1Section.transform);
            // AnchorRect(btnCont, 0.2f, 0.06f, 0.8f, 0.32f);
            // _q1ContinueBtn = BuildButton(btnCont.transform, "Continuar", new Color(0.12f, 0.45f, 0.85f, 1f));
            // _q1ContinueBtn.onClick.AddListener(OnQ1Continue);
            // Navigation inputNav = new Navigation { mode = Navigation.Mode.Explicit };
            // inputNav.selectOnDown = _q1ContinueBtn;
            // _roomInputField.navigation = inputNav;
            // Navigation contNav = new Navigation { mode = Navigation.Mode.Explicit };
            // contNav.selectOnUp = _roomInputField;
            // _q1ContinueBtn.navigation = contNav;

            // Botones de scroll arriba/abajo
            GameObject roomScrollUp = MakeRect("ScrollUp", _q1Section.transform);
            AnchorRect(roomScrollUp, 0.35f, 0.80f, 0.65f, 0.88f);
            Button roomUpBtn = BuildButton(roomScrollUp.transform, "▲", new Color(0.2f, 0.3f, 0.6f, 0.9f));
            roomUpBtn.onClick.AddListener(() => { if (_roomScrollRect != null) _roomScrollRect.verticalNormalizedPosition = Mathf.Clamp01(_roomScrollRect.verticalNormalizedPosition + 0.15f); });

            GameObject roomScrollDown = MakeRect("ScrollDown", _q1Section.transform);
            AnchorRect(roomScrollDown, 0.35f, 0f, 0.65f, 0.08f);
            Button roomDownBtn = BuildButton(roomScrollDown.transform, "▼", new Color(0.2f, 0.3f, 0.6f, 0.9f));
            roomDownBtn.onClick.AddListener(() => { if (_roomScrollRect != null) _roomScrollRect.verticalNormalizedPosition = Mathf.Clamp01(_roomScrollRect.verticalNormalizedPosition - 0.15f); });

            // Lista scrolleable de salas
            GameObject roomScrollGO = MakeRect("RoomScrollView", _q1Section.transform);
            AnchorRect(roomScrollGO, 0f, 0.08f, 1f, 0.80f);

            _roomScrollRect = roomScrollGO.AddComponent<ScrollRect>();
            ScrollRect roomScroll = _roomScrollRect;
            roomScroll.horizontal        = false;
            roomScroll.vertical          = true;
            roomScroll.scrollSensitivity = 30f;

            GameObject roomViewport = MakeRect("Viewport", roomScrollGO.transform);
            StretchFill(roomViewport);
            Image roomViewportImg = roomViewport.AddComponent<Image>();
            roomViewportImg.color = Color.white;
            roomViewport.AddComponent<Mask>().showMaskGraphic = false;
            roomScroll.viewport = roomViewport.GetComponent<RectTransform>();

            GameObject roomContent = MakeRect("Content", roomViewport.transform);
            _roomContentRT = roomContent.GetComponent<RectTransform>();
            _roomContentRT.anchorMin = new Vector2(0f, 1f);
            _roomContentRT.anchorMax = new Vector2(1f, 1f);
            _roomContentRT.pivot     = new Vector2(0.5f, 1f);
            _roomContentRT.offsetMin = Vector2.zero;
            _roomContentRT.offsetMax = Vector2.zero;

            roomContent.AddComponent<Image>().color = Color.clear;

            VerticalLayoutGroup roomVlg = roomContent.AddComponent<VerticalLayoutGroup>();
            roomVlg.spacing                = 15f;
            roomVlg.padding                = new RectOffset(8, 8, 8, 8);
            roomVlg.childAlignment         = TextAnchor.UpperCenter;
            roomVlg.childForceExpandWidth  = true;
            roomVlg.childForceExpandHeight = false;
            roomVlg.childControlWidth      = true;
            roomVlg.childControlHeight     = true;

            roomScroll.content = _roomContentRT;
            _roomListContent   = roomContent.transform;

            // ── Q2: objeto (lista scrolleable) ───────────────
            _objectSection = MakeRect("ObjectSection", panel.transform);
            AnchorRect(_objectSection, 0.05f, 0.08f, 0.95f, 0.75f);

            GameObject qObjArea = MakeRect("QArea", _objectSection.transform);
            AnchorRect(qObjArea, 0f, 0.83f, 1f, 1f);
            var qObjTxt = MakeText("QText", qObjArea.transform,
                     "¿Puedes identificar el objeto de la sala?", 52f,
                     FontStyles.Normal, TextAlignmentOptions.Center,
                     new Color(0.75f, 0.85f, 1f), stretch: true);
            qObjTxt.overflowMode = TextOverflowModes.Overflow;

            // Botones de scroll arriba/abajo para objetos
            GameObject objScrollUp = MakeRect("ScrollUp", _objectSection.transform);
            AnchorRect(objScrollUp, 0.35f, 0.75f, 0.65f, 0.83f);
            Button objUpBtn = BuildButton(objScrollUp.transform, "▲", new Color(0.2f, 0.3f, 0.6f, 0.9f));
            objUpBtn.onClick.AddListener(() => { if (_objectScrollRect != null) _objectScrollRect.verticalNormalizedPosition = Mathf.Clamp01(_objectScrollRect.verticalNormalizedPosition + 0.15f); });

            GameObject objScrollDown = MakeRect("ScrollDown", _objectSection.transform);
            AnchorRect(objScrollDown, 0.35f, 0f, 0.65f, 0.08f);
            Button objDownBtn = BuildButton(objScrollDown.transform, "▼", new Color(0.2f, 0.3f, 0.6f, 0.9f));
            objDownBtn.onClick.AddListener(() => { if (_objectScrollRect != null) _objectScrollRect.verticalNormalizedPosition = Mathf.Clamp01(_objectScrollRect.verticalNormalizedPosition - 0.15f); });

            GameObject scrollGO = MakeRect("ScrollView", _objectSection.transform);
            AnchorRect(scrollGO, 0f, 0.08f, 1f, 0.75f);

            _objectScrollRect = scrollGO.AddComponent<ScrollRect>();
            ScrollRect scroll = _objectScrollRect;
            scroll.horizontal        = false;
            scroll.vertical          = true;
            scroll.scrollSensitivity = 30f;

            GameObject viewport = MakeRect("Viewport", scrollGO.transform);
            StretchFill(viewport);
            Image viewportImg = viewport.AddComponent<Image>();
            viewportImg.color = Color.white;
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            scroll.viewport = viewport.GetComponent<RectTransform>();

            GameObject content = MakeRect("Content", viewport.transform);
            _objectContentRT = content.GetComponent<RectTransform>();
            _objectContentRT.anchorMin = new Vector2(0f, 1f);
            _objectContentRT.anchorMax = new Vector2(1f, 1f);
            _objectContentRT.pivot     = new Vector2(0.5f, 1f);
            _objectContentRT.offsetMin = Vector2.zero;
            _objectContentRT.offsetMax = Vector2.zero;

            content.AddComponent<Image>().color = Color.clear;

            VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing                = 15f;
            vlg.padding                = new RectOffset(8, 8, 8, 8);
            vlg.childAlignment         = TextAnchor.UpperCenter;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;

            scroll.content     = _objectContentRT;
            _objectListContent = content.transform;

            // ── Confirmación ──────────────────────────────────
            _confirmSection = MakeRect("ConfirmSection", panel.transform);
            AnchorRect(_confirmSection, 0.05f, 0.08f, 0.95f, 0.75f);

            GameObject confirmTextArea = MakeRect("ConfirmArea", _confirmSection.transform);
            AnchorRect(confirmTextArea, 0f, 0.45f, 1f, 1f);
            _confirmText = MakeText("CText", confirmTextArea.transform, "", 52f, FontStyles.Normal,
                                     TextAlignmentOptions.Center, new Color(0.75f, 0.85f, 1f),
                                     stretch: true);
            _confirmText.overflowMode = TextOverflowModes.Overflow;

            GameObject yesArea = MakeRect("YesArea", _confirmSection.transform);
            AnchorRect(yesArea, 0.08f, 0.05f, 0.44f, 0.38f);
            _yesBtn = BuildButton(yesArea.transform, "Sí", new Color(0.1f, 0.55f, 0.18f, 1f));
            _yesBtn.onClick.AddListener(OnYes);

            GameObject noArea = MakeRect("NoArea", _confirmSection.transform);
            AnchorRect(noArea, 0.56f, 0.05f, 0.92f, 0.38f);
            _noBtn = BuildButton(noArea.transform, "No", new Color(0.7f, 0.12f, 0.1f, 1f));
            _noBtn.onClick.AddListener(OnNo);

            _yesBtn.navigation = new Navigation
                { mode = Navigation.Mode.Explicit, selectOnRight = _noBtn };
            _noBtn.navigation  = new Navigation
                { mode = Navigation.Mode.Explicit, selectOnLeft  = _yesBtn };

            // ── Completado ────────────────────────────────────
            _completedSection = MakeRect("CompletedSection", panel.transform);
            AnchorRect(_completedSection, 0.05f, 0.08f, 0.95f, 0.75f);

            GameObject doneArea = MakeRect("DoneArea", _completedSection.transform);
            AnchorRect(doneArea, 0f, 0.45f, 1f, 1f);
            var doneTxt = MakeText("DoneText", doneArea.transform,
                     "¡Actividad completada!\n¿Deseas repetirla?", 55f,
                     FontStyles.Bold, TextAlignmentOptions.Center, Color.white, stretch: true);
            doneTxt.overflowMode = TextOverflowModes.Overflow;

            GameObject repArea = MakeRect("RepeatArea", _completedSection.transform);
            AnchorRect(repArea, 0.05f, 0.05f, 0.45f, 0.4f);
            _repeatBtn = BuildButton(repArea.transform, "Repetir", new Color(0.1f, 0.55f, 0.18f, 1f));
            _repeatBtn.onClick.AddListener(OnRepeat);

            GameObject finArea = MakeRect("FinalizeArea", _completedSection.transform);
            AnchorRect(finArea, 0.55f, 0.05f, 0.95f, 0.4f);
            _finalizeBtn = BuildButton(finArea.transform, "Finalizar", new Color(0.45f, 0.12f, 0.6f, 1f));
            _finalizeBtn.onClick.AddListener(OnFinalize);

            _repeatBtn.navigation  = new Navigation
                { mode = Navigation.Mode.Explicit, selectOnRight = _finalizeBtn };
            _finalizeBtn.navigation = new Navigation
                { mode = Navigation.Mode.Explicit, selectOnLeft  = _repeatBtn };

            // ── Resultados ────────────────────────────────────
            _resultsSection = MakeRect("ResultsSection", panel.transform);
            AnchorRect(_resultsSection, 0.05f, 0.08f, 0.95f, 0.75f);

            GameObject resTitleArea = MakeRect("TitleArea", _resultsSection.transform);
            AnchorRect(resTitleArea, 0f, 0.82f, 1f, 1f);
            MakeText("TitleText", resTitleArea.transform, "Resultados", 58f,
                      FontStyles.Bold, TextAlignmentOptions.Center, Color.white, stretch: true);

            GameObject roomResArea = MakeRect("RoomResult", _resultsSection.transform);
            AnchorRect(roomResArea, 0.04f, 0.52f, 0.96f, 0.80f);
            MakeImage("RoomBG", roomResArea.transform, new Color(0.08f, 0.08f, 0.14f, 0.9f), stretch: true);
            _roomResultText = MakeText("RoomText", roomResArea.transform, "", 50f,
                                        FontStyles.Bold, TextAlignmentOptions.Center,
                                        Color.white, stretch: true);
            _roomResultText.overflowMode = TextOverflowModes.Overflow;

            GameObject objResArea = MakeRect("ObjResult", _resultsSection.transform);
            AnchorRect(objResArea, 0.04f, 0.22f, 0.96f, 0.50f);
            MakeImage("ObjBG", objResArea.transform, new Color(0.08f, 0.08f, 0.14f, 0.9f), stretch: true);
            _objectResultText = MakeText("ObjText", objResArea.transform, "", 50f,
                                          FontStyles.Bold, TextAlignmentOptions.Center,
                                          Color.white, stretch: true);
            _objectResultText.overflowMode = TextOverflowModes.Overflow;

            GameObject closeArea = MakeRect("CloseArea", _resultsSection.transform);
            AnchorRect(closeArea, 0.25f, 0.03f, 0.75f, 0.19f);
            _closeBtn = BuildButton(closeArea.transform, "Cerrar", new Color(0.1f, 0.2f, 0.52f, 1f));
            _closeBtn.onClick.AddListener(OnClose);

            // Todas las secciones ocultas al inicio
            _q1Section.SetActive(false);
            _objectSection.SetActive(false);
            _confirmSection.SetActive(false);
            _completedSection.SetActive(false);
            _resultsSection.SetActive(false);
        }

        // ─────────────────────────────────────────────
        //  Helpers UI
        // ─────────────────────────────────────────────

        // -- Versión teclado virtual (descomentar para restaurar) --
        // private TMP_InputField BuildInputField(Transform parent, string placeholderText)
        // {
        //     GameObject go = MakeRect("InputField", parent);
        //     StretchFill(go);
        //     go.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f, 1f);
        //     TMP_InputField field = go.AddComponent<TMP_InputField>();
        //     GameObject textArea = MakeRect("Text Area", go.transform);
        //     StretchFill(textArea);
        //     RectTransform taRT = textArea.GetComponent<RectTransform>();
        //     taRT.offsetMin = new Vector2(12f, 6f);
        //     taRT.offsetMax = new Vector2(-12f, -6f);
        //     GameObject phGO = MakeRect("Placeholder", textArea.transform);
        //     StretchFill(phGO);
        //     TextMeshProUGUI ph = phGO.AddComponent<TextMeshProUGUI>();
        //     ph.text = placeholderText; ph.fontSize = 46f;
        //     ph.fontStyle = FontStyles.Italic;
        //     ph.color = new Color(0.5f, 0.5f, 0.5f, 0.8f);
        //     ph.alignment = TextAlignmentOptions.MidlineLeft;
        //     GameObject txtGO = MakeRect("Text", textArea.transform);
        //     StretchFill(txtGO);
        //     TextMeshProUGUI txt = txtGO.AddComponent<TextMeshProUGUI>();
        //     txt.fontSize = 46f; txt.color = Color.white;
        //     txt.alignment = TextAlignmentOptions.MidlineLeft;
        //     field.textViewport   = textArea.GetComponent<RectTransform>();
        //     field.textComponent  = txt;
        //     field.placeholder    = ph;
        //     field.lineType       = TMP_InputField.LineType.SingleLine;
        //     field.characterLimit = 10;
        //     return field;
        // }

        private Button BuildButton(Transform parent, string label, Color bgColor)
        {
            GameObject go = MakeRect("Btn_" + label, parent);
            StretchFill(go);
            go.AddComponent<Image>().color = bgColor;

            Button btn = go.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor      = bgColor;
            cb.highlightedColor = Color.Lerp(bgColor, Color.white, 0.28f);
            cb.pressedColor     = Color.Lerp(bgColor, Color.black, 0.28f);
            cb.selectedColor    = Color.Lerp(bgColor, Color.white, 0.28f);
            btn.colors = cb;

            MakeText("BtnLabel", go.transform, label, 52f,
                      FontStyles.Bold, TextAlignmentOptions.Center, Color.white, stretch: true);
            return btn;
        }

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

    }
}

// Componente auxiliar: auto-scrollea el ScrollRect cuando este botón es seleccionado.
// Implementa solo ISelectHandler para NO interferir con los eventos de drag/scroll del ScrollRect.
public class ScrollOnSelect : MonoBehaviour, UnityEngine.EventSystems.ISelectHandler
{
    public ScrollRect scrollRect;

    public void OnSelect(UnityEngine.EventSystems.BaseEventData eventData)
    {
        if (scrollRect == null) return;
        Canvas.ForceUpdateCanvases();

        RectTransform contentRT  = scrollRect.content;
        RectTransform viewportRT = scrollRect.viewport;
        RectTransform itemRT     = GetComponent<RectTransform>();

        float contentHeight  = contentRT.rect.height;
        float viewportHeight = viewportRT.rect.height;
        if (contentHeight <= viewportHeight) return;

        float scrollableRange = contentHeight - viewportHeight;
        float itemLocalY      = contentRT.InverseTransformPoint(itemRT.position).y;
        float itemTop         = -itemLocalY;
        float itemBottom      = itemTop + itemRT.rect.height;

        float currentTop    = (1f - scrollRect.verticalNormalizedPosition) * scrollableRange;
        float currentBottom = currentTop + viewportHeight;

        float newTop = currentTop;
        if (itemTop < currentTop)
            newTop = itemTop;
        else if (itemBottom > currentBottom)
            newTop = itemBottom - viewportHeight;

        newTop = Mathf.Clamp(newTop, 0f, scrollableRange);
        scrollRect.verticalNormalizedPosition = 1f - (newTop / scrollableRange);
    }
}
