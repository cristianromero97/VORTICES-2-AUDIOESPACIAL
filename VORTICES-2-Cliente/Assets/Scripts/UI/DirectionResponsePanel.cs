using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Vortices
{
    /// <summary>
    /// DirectionResponsePanel: Se adjunta a un GO DENTRO del Canvas del VRPanel_Sonidos,
    /// como hermano del SonidosUI. No crea Canvas propio — usa el Canvas padre del VRPanel.
    /// Se superpone sobre SonidosUI usando CanvasGroup (alpha 0/1).
    /// </summary>
    public class DirectionResponsePanel : MonoBehaviour
    {
        // ─────────────────────────────────────────────
        //  Estado runtime
        // ─────────────────────────────────────────────
        private AudioTargetMarker currentMarker;
        private SonidosPanel      sonidosPanel;
        private Transform         buttonContainer;
        private TextMeshProUGUI   titleText;
        private CanvasGroup       group;
        private List<string>      activeDirections;
        private bool              isVisible;

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
            if (!isVisible || activeDirections == null) return;
            for (int i = 0; i < activeDirections.Count && i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    OnDirectionSelected(activeDirections[i]);
                    break;
                }
            }
        }

        // ─────────────────────────────────────────────
        //  API pública
        // ─────────────────────────────────────────────

        public void Show(AudioTargetMarker marker, SonidosPanel returnPanel)
        {
            currentMarker = marker;
            sonidosPanel  = returnPanel;

            if (titleText != null && marker != null && !string.IsNullOrEmpty(marker.audioFileName))
                titleText.text = $"\"{marker.audioFileName}\"";

            BuildDirectionButtons();
            SetVisible(true);
        }

        // ─────────────────────────────────────────────
        //  Botones de dirección
        // ─────────────────────────────────────────────
        private void BuildDirectionButtons()
        {
            if (buttonContainer == null) return;
            foreach (Transform child in buttonContainer)
                Destroy(child.gameObject);

            List<string> directions = SessionManager.instance?.selectedDirections;
            if (directions == null || directions.Count == 0)
                directions = new List<string> { "Izquierda", "Derecha", "Arriba", "Abajo", "No sé" };

            activeDirections = directions;

            foreach (string dir in directions)
            {
                string captured = dir;
                GameObject rowGO = MakeRect("Row_" + dir, buttonContainer);
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
                hlg.padding               = new RectOffset(20, 20, 10, 10);
                hlg.spacing               = 20f;
                hlg.childAlignment        = TextAnchor.MiddleLeft;
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = true;
                hlg.childControlWidth     = true;
                hlg.childControlHeight    = true;

                // Checkbox cuadrado
                GameObject box = MakeRect("Checkbox", rowGO.transform);
                box.AddComponent<LayoutElement>().preferredWidth = 55f;
                box.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.28f, 1f);
                MakeText("Check", box.transform, "", 50f, FontStyles.Bold,
                          TextAlignmentOptions.Center, new Color(0.3f, 0.9f, 0.4f), stretch: true);

                // Etiqueta de dirección
                TextMeshProUGUI label = MakeText("Label", rowGO.transform, dir, 48f, FontStyles.Normal,
                          TextAlignmentOptions.Left, Color.white, stretch: false);
                label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

                btn.onClick.AddListener(() => OnDirectionSelected(captured));
            }
        }

        private void OnDirectionSelected(string direction)
        {
            LogResponse(direction);
            SetVisible(false);
            sonidosPanel?.OnDirectionResponded();
        }

        private void SetVisible(bool visible)
        {
            if (group == null) return;
            isVisible            = visible;
            group.alpha          = visible ? 1f : 0f;
            group.interactable   = visible;
            group.blocksRaycasts = visible;
        }

        // ─────────────────────────────────────────────
        //  Registro en CSV
        // ─────────────────────────────────────────────
        private void LogResponse(string direction)
        {
            string sessionName = SessionManager.instance?.sessionName  ?? "unknown";
            int    userId      = SessionManager.instance?.userId       ?? -1;
            string soundFile   = currentMarker?.audioFileName          ?? "unknown";
            string prefabType  = currentMarker?.prefabType             ?? "unknown";
            int    roomIndex   = currentMarker?.roomIndex              ?? -1;
            string timestamp   = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            string entry = $"{sessionName},{userId},{soundFile},{prefabType},{roomIndex},{direction},{timestamp}";
            Debug.Log($"[DirectionResponse] {entry}");

            try
            {
                // Guardar junto a los otros resultados: Assets/Results/{sessionName}/{userId}/
                string folder = Path.Combine(Application.dataPath, "Results",
                                             sessionName, userId.ToString());
                Directory.CreateDirectory(folder);
                string path   = Path.Combine(folder, "Sala Direction Responses.csv");
                bool   exists = File.Exists(path);
                using (StreamWriter sw = File.AppendText(path))
                {
                    if (!exists)
                        sw.WriteLine("sessionName,userId,soundFile,prefabType,roomIndex,direction,timestamp");
                    sw.WriteLine(entry);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[DirectionResponse] Error al guardar CSV: {e.Message}");
            }
        }

        // ─────────────────────────────────────────────
        //  Construcción de UI (sin Canvas propio — usa el Canvas padre del VRPanel)
        // ─────────────────────────────────────────────
        private void BuildUI()
        {
            group = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            RectTransform rt = GetComponent<RectTransform>();
            if (rt == null) rt = gameObject.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // Fondo oscuro semi-transparente
            MakeImage("Overlay", transform, new Color(0f, 0f, 0f, 0.75f), stretch: true);

            // Panel central (80% del ancho, 90% del alto)
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

            // Subtítulo
            GameObject sub = MakeRect("SubTitle", panel.transform);
            AnchorRect(sub, 0f, 0.73f, 1f, 0.82f);
            titleText = MakeText("TitleText", sub.transform, "", 50f, FontStyles.Italic,
                                  TextAlignmentOptions.Center,
                                  new Color(0.85f, 0.85f, 0.85f), stretch: true);

            // Pregunta
            GameObject qArea = MakeRect("QuestionArea", panel.transform);
            AnchorRect(qArea, 0f, 0.64f, 1f, 0.73f);
            MakeText("QText", qArea.transform, "Which ear did you hear it with?", 55f,
                      FontStyles.Normal, TextAlignmentOptions.Center,
                      new Color(0.75f, 0.85f, 1f), stretch: true);

            // Contenedor de botones
            GameObject btnArea = MakeRect("BtnArea", panel.transform);
            AnchorRect(btnArea, 0.05f, 0.04f, 0.95f, 0.64f);

            VerticalLayoutGroup vlg = btnArea.AddComponent<VerticalLayoutGroup>();
            vlg.spacing                = 20f;
            vlg.padding                = new RectOffset(10, 10, 10, 10);
            vlg.childAlignment         = TextAnchor.UpperCenter;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;

            buttonContainer = btnArea.transform;
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
    }
}
