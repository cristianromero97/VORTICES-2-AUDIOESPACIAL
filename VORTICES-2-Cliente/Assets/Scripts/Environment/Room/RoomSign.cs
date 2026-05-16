using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Vortices
{
    /// <summary>
    /// Cartel de número de sala colocado sobre el vano de la puerta, orientado hacia el corredor.
    /// Se instancia desde RoomGeometry al construir cada sala.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoomSign : MonoBehaviour
    {
        public void Setup(int roomNumber, int side)
        {
            // Canvas WorldSpace: +Z apunta LEJOS del corredor (mismo convenio que FurnitureLabel)
            // side= 1 (sala derecha): +Z → +X → rotation (0, 90, 0)
            // side=-1 (sala izquierda): +Z → -X → rotation (0,-90, 0)
            transform.localRotation = Quaternion.Euler(0f, side * 90f, 0f);

            // sizeDelta 200×100 × 0.008 = 1.6 m × 0.8 m en mundo
            transform.localScale = Vector3.one * 0.008f;

            BuildCanvas(roomNumber);
        }

        private void BuildCanvas(int roomNumber)
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            RectTransform rt = GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(200f, 100f);

            // Fondo oscuro semitransparente
            GameObject bgGO = new GameObject("BG", typeof(RectTransform));
            bgGO.transform.SetParent(transform, false);
            bgGO.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.12f, 0.92f);
            StretchFill(bgGO.GetComponent<RectTransform>());

            // Etiqueta "SALA" en la franja superior
            MakeText("SubLabel", transform, "ROOM", 26f, FontStyles.Normal,
                     TextAlignmentOptions.Center, new Color(0.65f, 0.65f, 0.78f),
                     new Vector2(0f, 0.52f), Vector2.one);

            // Número grande en la franja inferior
            MakeText("Number", transform, roomNumber.ToString(), 70f, FontStyles.Bold,
                     TextAlignmentOptions.Center, Color.white,
                     Vector2.zero, new Vector2(1f, 0.58f));
        }

        private static void MakeText(string goName, Transform parent, string text,
                                     float size, FontStyles style, TextAlignmentOptions align,
                                     Color color, Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject go = new GameObject(goName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.fontStyle = style;
            tmp.alignment = align; tmp.color = color;
        }

        private static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }
    }
}
