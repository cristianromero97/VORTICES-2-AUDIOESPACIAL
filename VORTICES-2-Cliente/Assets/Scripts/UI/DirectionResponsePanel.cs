using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace Vortices
{
    /// <summary>
    /// DirectionResponsePanel: Se adjunta a un GO DENTRO del Canvas del VRPanel_Sonidos.
    /// Flujo: Selección (lista de direcciones) → Confirmar → Completado (Repetir / Finalizar).
    /// </summary>
    public class DirectionResponsePanel : MonoBehaviour
    {
        // ─────────────────────────────────────────────
        //  Estado
        // ─────────────────────────────────────────────
        private enum PanelState { Selecting, ConfirmAnswer, Completed }

        private PanelState        _state = PanelState.Selecting;
        private string            _selectedDirection;
        private AudioTargetMarker _currentMarker;
        private SonidosPanel      _sonidosPanel;
        private List<string>      _activeDirections;
        private bool              _isVisible;

        // ─────────────────────────────────────────────
        //  UI refs
        // ─────────────────────────────────────────────
        private CanvasGroup       _group;
        private TextMeshProUGUI   _titleText;

        private GameObject        _selectionSection;
        private Transform         _buttonContainer;

        private GameObject        _confirmSection;
        private TextMeshProUGUI   _confirmText;
        private Button            _yesBtn;
        private Button            _noBtn;

        private GameObject        _completedSection;
        private Button            _repeatBtn;
        private Button            _finalizeBtn;

        // ─────────────────────────────────────────────
        //  Unity lifecycle
        // ─────────────────────────────────────────────
        private void Awake()
        {
            BuildUI();
            SetVisible(false);
        }

        private void Update()
        {
            if (!_isVisible || _state != PanelState.Selecting || _activeDirections == null) return;
            for (int i = 0; i < _activeDirections.Count && i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    OnDirectionSelected(_activeDirections[i]);
                    break;
                }
            }
        }

        // ─────────────────────────────────────────────
        //  API pública
        // ─────────────────────────────────────────────
        public void Show(AudioTargetMarker marker, SonidosPanel returnPanel)
        {
            // Si el panel ya está activo, ignorar — el usuario debe Repetir o Finalizar primero
            if (_isVisible) return;

            _currentMarker     = marker;
            _sonidosPanel      = returnPanel;
            _selectedDirection = null;

            if (_titleText != null && marker != null && !string.IsNullOrEmpty(marker.audioFileName))
                _titleText.text = $"\"{marker.audioFileName}\"";

            BuildDirectionButtons();
            EnterState(PanelState.Selecting);
            SetVisible(true);
        }

        // ─────────────────────────────────────────────
        //  Máquina de estados
        // ─────────────────────────────────────────────
        private void EnterState(PanelState s)
        {
            _state = s;
            _selectionSection.SetActive(s == PanelState.Selecting);
            _confirmSection.SetActive(s == PanelState.ConfirmAnswer);
            _completedSection.SetActive(s == PanelState.Completed);

            switch (s)
            {
                case PanelState.ConfirmAnswer:
                    _confirmText.text = $"¿Estás seguro que el sonido viene de: {_selectedDirection}?";
                    Select(_yesBtn.gameObject);
                    break;
                case PanelState.Completed:
                    Select(_repeatBtn.gameObject);
                    break;
            }
        }

        // ─────────────────────────────────────────────
        //  Botones de dirección
        // ─────────────────────────────────────────────
        private void BuildDirectionButtons()
        {
            if (_buttonContainer == null) return;
            foreach (Transform child in _buttonContainer)
                Destroy(child.gameObject);

            List<string> directions = SessionManager.instance?.selectedDirections;
            if (directions == null || directions.Count == 0)
                directions = new List<string> { "Izquierda", "Derecha", "Arriba", "Abajo", "No sé" };

            _activeDirections = directions;

            foreach (string dir in directions)
            {
                string captured = dir;
                GameObject rowGO = MakeRect("Row_" + dir, _buttonContainer);
                rowGO.AddComponent<LayoutElement>().preferredHeight = 90f;
                rowGO.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.15f, 0.9f);

                Button btn = rowGO.AddComponent<Button>();
                btn.transition = Selectable.Transition.ColorTint;
                ColorBlock cb = btn.colors;
                cb.normalColor      = new Color(0.1f,  0.1f,  0.15f, 0.9f);
                cb.highlightedColor = new Color(0.18f, 0.38f, 0.72f, 1f);
                cb.pressedColor     = new Color(0.08f, 0.22f, 0.5f,  1f);
                cb.selectedColor    = cb.highlightedColor;
                btn.colors = cb;

                HorizontalLayoutGroup hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
                hlg.padding                = new RectOffset(20, 20, 10, 10);
                hlg.spacing                = 20f;
                hlg.childAlignment         = TextAnchor.MiddleLeft;
                hlg.childForceExpandWidth  = false;
                hlg.childForceExpandHeight = true;
                hlg.childControlWidth      = true;
                hlg.childControlHeight     = true;

                GameObject box = MakeRect("Checkbox", rowGO.transform);
                box.AddComponent<LayoutElement>().preferredWidth = 55f;
                box.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.28f, 1f);
                MakeText("Check", box.transform, "", 50f, FontStyles.Bold,
                          TextAlignmentOptions.Center, new Color(0.3f, 0.9f, 0.4f), stretch: true);

                TextMeshProUGUI label = MakeText("Label", rowGO.transform, dir, 48f, FontStyles.Normal,
                          TextAlignmentOptions.Left, Color.white, stretch: false);
                label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

                btn.onClick.AddListener(() => OnDirectionSelected(captured));
            }
        }

        // ─────────────────────────────────────────────
        //  Callbacks
        // ─────────────────────────────────────────────
        private void OnDirectionSelected(string direction)
        {
            _selectedDirection = direction;
            EnterState(PanelState.ConfirmAnswer);
        }

        private void OnYes()  => EnterState(PanelState.Completed);

        private void OnNo()   => EnterState(PanelState.Selecting);

        private void OnRepeat()
        {
            // Cierra el panel y espera el próximo trigger (SonidosPanel → proximidad)
            // NO llama OnDirectionResponded: SonidosPanel queda libre para volver a emitir
            SetVisible(false);
        }

        private void OnFinalize()
        {
            LogResponse(_selectedDirection);
            SetVisible(false);
            _sonidosPanel?.OnDirectionResponded();
        }

        // ─────────────────────────────────────────────
        //  Visibilidad / selección
        // ─────────────────────────────────────────────
        private static void Select(GameObject go)
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(go);
        }

        private void SetVisible(bool visible)
        {
            if (_group == null) return;
            _isVisible           = visible;
            _group.alpha         = visible ? 1f : 0f;
            _group.interactable  = visible;
            _group.blocksRaycasts = visible;
            if (!visible) Select(null);
        }

        // ─────────────────────────────────────────────
        //  Validación: derecha = sala impar, izquierda = sala par
        // ─────────────────────────────────────────────
        private static bool IsCorrectAnswer(string direction, int roomIndex)
        {
            bool isRight = direction.Equals("derecha",   System.StringComparison.OrdinalIgnoreCase)
                        || direction.Equals("right",     System.StringComparison.OrdinalIgnoreCase);
            bool isLeft  = direction.Equals("izquierda", System.StringComparison.OrdinalIgnoreCase)
                        || direction.Equals("left",      System.StringComparison.OrdinalIgnoreCase);

            if (isRight) return roomIndex % 2 == 1;
            if (isLeft)  return roomIndex % 2 == 0;
            return false;
        }

        // ─────────────────────────────────────────────
        //  CSV
        // ─────────────────────────────────────────────
        private void LogResponse(string direction)
        {
            string sessionName = SessionManager.instance?.sessionName ?? "unknown";
            int    userId      = SessionManager.instance?.userId      ?? -1;
            string soundFile   = _currentMarker?.audioFileName        ?? "unknown";
            string prefabType  = _currentMarker?.prefabType           ?? "unknown";
            int    roomIndex   = _currentMarker?.roomIndex            ?? -1;
            bool   correct     = IsCorrectAnswer(direction, roomIndex);
            string timestamp   = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            string entry = $"{sessionName},{userId},{soundFile},{prefabType},{roomIndex},{direction},{correct},{timestamp}";
            Debug.Log($"[DirectionResponse] {entry}");

            try
            {
                string folder = Path.Combine(Application.dataPath, "Results",
                                             sessionName, userId.ToString());
                Directory.CreateDirectory(folder);
                string path  = Path.Combine(folder, "Sala Direction Responses.csv");
                bool exists  = File.Exists(path);
                using (StreamWriter sw = File.AppendText(path))
                {
                    if (!exists)
                        sw.WriteLine("sessionName,userId,soundFile,prefabType,roomIndex,direction,correct,timestamp");
                    sw.WriteLine(entry);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[DirectionResponse] Error al guardar CSV: {e.Message}");
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
            AnchorRect(header, 0f, 0.82f, 1f, 1f);
            MakeText("HText", header.transform, "Tutorial Activity", 70f,
                      FontStyles.Bold, TextAlignmentOptions.Center, Color.white, stretch: true);

            // Subtítulo (nombre del audio)
            GameObject sub = MakeRect("SubTitle", panel.transform);
            AnchorRect(sub, 0f, 0.73f, 1f, 0.82f);
            _titleText = MakeText("TitleText", sub.transform, "", 50f, FontStyles.Italic,
                                   TextAlignmentOptions.Center,
                                   new Color(0.85f, 0.85f, 0.85f), stretch: true);

            // ── Sección selección ─────────────────────
            _selectionSection = MakeRect("SelectionSection", panel.transform);
            AnchorRect(_selectionSection, 0f, 0f, 1f, 0.73f);

            GameObject qArea = MakeRect("QuestionArea", _selectionSection.transform);
            AnchorRect(qArea, 0f, 0.88f, 1f, 1f);
            var qTxt = MakeText("QText", qArea.transform,
                         "¿De qué dirección proviene el sonido?", 55f,
                         FontStyles.Normal, TextAlignmentOptions.Center,
                         new Color(0.75f, 0.85f, 1f), stretch: true);
            qTxt.overflowMode = TextOverflowModes.Overflow;

            GameObject btnArea = MakeRect("BtnArea", _selectionSection.transform);
            AnchorRect(btnArea, 0.05f, 0.04f, 0.95f, 0.87f);
            VerticalLayoutGroup vlg = btnArea.AddComponent<VerticalLayoutGroup>();
            vlg.spacing                = 20f;
            vlg.padding                = new RectOffset(10, 10, 10, 10);
            vlg.childAlignment         = TextAnchor.UpperCenter;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            _buttonContainer = btnArea.transform;

            // ── Sección confirmación ──────────────────
            _confirmSection = MakeRect("ConfirmSection", panel.transform);
            AnchorRect(_confirmSection, 0.05f, 0f, 0.95f, 0.73f);

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

            // ── Sección completado ────────────────────
            _completedSection = MakeRect("CompletedSection", panel.transform);
            AnchorRect(_completedSection, 0.05f, 0f, 0.95f, 0.73f);

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

            // Todas las secciones ocultas al inicio
            _selectionSection.SetActive(false);
            _confirmSection.SetActive(false);
            _completedSection.SetActive(false);
        }

        // ─────────────────────────────────────────────
        //  Helpers UI
        // ─────────────────────────────────────────────
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
