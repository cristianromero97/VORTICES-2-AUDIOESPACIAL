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
        private enum PanelState { Selecting, ConfirmAnswer, Completed, Results }

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
        private RectTransform     _dirContentRT;
        private ScrollRect        _directionScrollRect;
        // private Button            _muteBtn;
        // private TextMeshProUGUI   _muteBtnLabel;

        private GameObject        _confirmSection;
        private TextMeshProUGUI   _confirmText;
        private Button            _yesBtn;
        private Button            _noBtn;

        private GameObject        _completedSection;
        private Button            _repeatBtn;
        private Button            _finalizeBtn;

        private GameObject        _resultsSection;
        private TextMeshProUGUI   _answerResultText;
        private TextMeshProUGUI   _explanationText;
        private Button            _closeBtn;

        // Datos calculados al Finalizar
        private bool   _resultCorrect;
        private string _resultCorrectDirection;
        private int    _resultRoomIndex;

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
            if (!_isVisible) return;

            if (_state == PanelState.Selecting)
            {
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.001f && _directionScrollRect != null)
                    _directionScrollRect.verticalNormalizedPosition = Mathf.Clamp01(
                        _directionScrollRect.verticalNormalizedPosition + wheel * 0.08f);
            }

            if (_state != PanelState.Selecting || _activeDirections == null) return;
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
            Canvas.ForceUpdateCanvases();
            if (_dirContentRT != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_dirContentRT);
            if (_directionScrollRect != null)
                _directionScrollRect.verticalNormalizedPosition = 1f;
            // if (_muteBtn != null) { _muteBtn.interactable = true; }
            // if (_muteBtnLabel != null) _muteBtnLabel.text = "Silenciar audio";
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
            _resultsSection.SetActive(s == PanelState.Results);

            switch (s)
            {
                case PanelState.ConfirmAnswer:
                    _confirmText.text = $"¿Estás seguro que el sonido viene de: {_selectedDirection}?";
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

        // private void OnMuteClicked()
        // {
        //     _sonidosPanel?.MuteCurrentAudio();
        //     if (_muteBtn != null) _muteBtn.interactable = false;
        //     if (_muteBtnLabel != null) _muteBtnLabel.text = "Audio silenciado";
        // }

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
            ComputeDirectionResult();
            LogResponse(_selectedDirection);
            EnterState(PanelState.Results);
        }

        private void OnClose()
        {
            SetVisible(false);
            _sonidosPanel?.OnDirectionResponded();
        }

        private void ComputeDirectionResult()
        {
            _resultRoomIndex       = _currentMarker?.roomIndex ?? -1;
            _resultCorrectDirection = (_resultRoomIndex % 2 == 1) ? "DERECHA" : "IZQUIERDA";
            _resultCorrect         = IsCorrectAnswer(_selectedDirection, _resultRoomIndex);
        }

        private void PopulateResults()
        {
            Color green = new Color(0.2f, 0.9f, 0.3f);
            Color red   = new Color(1f,   0.3f, 0.3f);

            string dirLower = _resultCorrectDirection.ToLower();

            if (_resultCorrect)
            {
                _answerResultText.text  = $"Tu respuesta:  {_selectedDirection}\n✓ ¡Correcto!";
                _answerResultText.color = green;
                _explanationText.text   = $"El sonido provenía de la Sala {_resultRoomIndex}, " +
                                          $"ubicada a la {_resultCorrectDirection} del corredor.";
            }
            else
            {
                _answerResultText.text  = $"Tu respuesta:  {_selectedDirection}\n✗  Incorrecto";
                _answerResultText.color = red;
                _explanationText.text   = $"El sonido provenía de la Sala {_resultRoomIndex}, " +
                                          $"ubicada a la {_resultCorrectDirection} del corredor, " +
                                          $"no a la {_selectedDirection.ToLower()}.";
            }
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

            // Overlay oscurece visualmente pero no intercepta clicks (SonidosPanel sigue accesible)
            GameObject overlay = MakeImage("Overlay", transform, new Color(0f, 0f, 0f, 0.75f), stretch: true);
            overlay.GetComponent<Image>().raycastTarget = false;

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

            // // Botón silenciar audio (descomentar si SonidosPanel vuelve a bloquearse)
            // GameObject muteArea = MakeRect("MuteArea", _selectionSection.transform);
            // AnchorRect(muteArea, 0.1f, 0.84f, 0.9f, 0.91f);
            // _muteBtn = BuildButton(muteArea.transform, "Silenciar audio", new Color(0.6f, 0.18f, 0.1f, 1f));
            // _muteBtnLabel = _muteBtn.GetComponentInChildren<TextMeshProUGUI>();
            // _muteBtn.onClick.AddListener(OnMuteClicked);

            // ▲/▼ botones de scroll
            GameObject dirScrollUp = MakeRect("ScrollUp", _selectionSection.transform);
            AnchorRect(dirScrollUp, 0.2f, 0.81f, 0.8f, 0.88f);
            Button dirUpBtn = BuildButton(dirScrollUp.transform, "▲", new Color(0.2f, 0.3f, 0.6f, 0.9f));
            dirUpBtn.onClick.AddListener(() => { if (_directionScrollRect != null) _directionScrollRect.verticalNormalizedPosition = Mathf.Clamp01(_directionScrollRect.verticalNormalizedPosition + 0.2f); });

            GameObject dirScrollDown = MakeRect("ScrollDown", _selectionSection.transform);
            AnchorRect(dirScrollDown, 0.2f, 0.01f, 0.8f, 0.08f);
            Button dirDownBtn = BuildButton(dirScrollDown.transform, "▼", new Color(0.2f, 0.3f, 0.6f, 0.9f));
            dirDownBtn.onClick.AddListener(() => { if (_directionScrollRect != null) _directionScrollRect.verticalNormalizedPosition = Mathf.Clamp01(_directionScrollRect.verticalNormalizedPosition - 0.2f); });

            // ScrollRect para la lista de direcciones
            GameObject dirScrollGO = MakeRect("DirScrollView", _selectionSection.transform);
            AnchorRect(dirScrollGO, 0.03f, 0.08f, 0.97f, 0.81f);

            _directionScrollRect = dirScrollGO.AddComponent<ScrollRect>();
            _directionScrollRect.horizontal        = false;
            _directionScrollRect.vertical          = true;
            _directionScrollRect.scrollSensitivity = 30f;

            GameObject dirViewport = MakeRect("Viewport", dirScrollGO.transform);
            StretchFill(dirViewport);
            dirViewport.AddComponent<Image>().color = Color.white;
            dirViewport.AddComponent<Mask>().showMaskGraphic = false;
            _directionScrollRect.viewport = dirViewport.GetComponent<RectTransform>();

            GameObject dirContent = MakeRect("Content", dirViewport.transform);
            _dirContentRT = dirContent.GetComponent<RectTransform>();
            _dirContentRT.anchorMin = new Vector2(0f, 1f);
            _dirContentRT.anchorMax = new Vector2(1f, 1f);
            _dirContentRT.pivot     = new Vector2(0.5f, 1f);
            _dirContentRT.offsetMin = Vector2.zero;
            _dirContentRT.offsetMax = Vector2.zero;
            dirContent.AddComponent<Image>().color = Color.clear;

            VerticalLayoutGroup vlg = dirContent.AddComponent<VerticalLayoutGroup>();
            vlg.spacing                = 15f;
            vlg.padding                = new RectOffset(10, 10, 8, 8);
            vlg.childAlignment         = TextAnchor.UpperCenter;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;

            ContentSizeFitter dirCsf = dirContent.AddComponent<ContentSizeFitter>();
            dirCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _directionScrollRect.content = _dirContentRT;
            _buttonContainer = dirContent.transform;

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

            // ── Sección resultados ────────────────────
            _resultsSection = MakeRect("ResultsSection", panel.transform);
            AnchorRect(_resultsSection, 0.05f, 0f, 0.95f, 0.73f);

            GameObject resTitleArea = MakeRect("TitleArea", _resultsSection.transform);
            AnchorRect(resTitleArea, 0f, 0.85f, 1f, 1f);
            MakeText("TitleText", resTitleArea.transform, "Resultado", 58f,
                      FontStyles.Bold, TextAlignmentOptions.Center, Color.white, stretch: true);

            GameObject answerArea = MakeRect("AnswerResult", _resultsSection.transform);
            AnchorRect(answerArea, 0.04f, 0.55f, 0.96f, 0.83f);
            MakeImage("AnswerBG", answerArea.transform, new Color(0.08f, 0.08f, 0.14f, 0.9f), stretch: true);
            _answerResultText = MakeText("AnswerText", answerArea.transform, "", 48f,
                                          FontStyles.Bold, TextAlignmentOptions.Center,
                                          Color.white, stretch: true);
            _answerResultText.overflowMode = TextOverflowModes.Overflow;

            GameObject explanationArea = MakeRect("Explanation", _resultsSection.transform);
            AnchorRect(explanationArea, 0.04f, 0.22f, 0.96f, 0.53f);
            MakeImage("ExplBG", explanationArea.transform, new Color(0.08f, 0.08f, 0.14f, 0.9f), stretch: true);
            _explanationText = MakeText("ExplText", explanationArea.transform, "", 46f,
                                         FontStyles.Normal, TextAlignmentOptions.Center,
                                         new Color(0.85f, 0.85f, 0.85f), stretch: true);
            _explanationText.overflowMode = TextOverflowModes.Overflow;

            GameObject closeArea = MakeRect("CloseArea", _resultsSection.transform);
            AnchorRect(closeArea, 0.25f, 0.03f, 0.75f, 0.19f);
            _closeBtn = BuildButton(closeArea.transform, "Cerrar", new Color(0.1f, 0.2f, 0.52f, 1f));
            _closeBtn.onClick.AddListener(OnClose);

            // Todas las secciones ocultas al inicio
            _selectionSection.SetActive(false);
            _confirmSection.SetActive(false);
            _completedSection.SetActive(false);
            _resultsSection.SetActive(false);
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
