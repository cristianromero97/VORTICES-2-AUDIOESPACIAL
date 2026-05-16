using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Vortices
{
    [DisallowMultipleComponent]
    public class FurnitureLabel : MonoBehaviour
    {
        public static readonly Color ColorDefault  = new Color(0.2f, 0.85f, 0.3f, 1f);
        public static readonly Color ColorConflict = new Color(0.9f, 0.15f, 0.1f, 1f);

        // Registro global de todos los labels activos en escena
        public static readonly List<FurnitureLabel> All = new List<FurnitureLabel>();

        // Radio (metros) al que el jugador debe estar para que el label revele el nombre real
        public static float RevealRadius = 1.2f;

        public int roomIndex { get; set; }

        private TextMeshProUGUI labelText;
        private string          labelName;
        private Transform       followTarget;
        private bool            showInFront;
        private float           heightOffset;
        private bool            _isAudioSource;

        // Posición del objeto que este label sigue (usada para cálculo de proximidad)
        public Vector3 FollowPosition => followTarget != null ? followTarget.position : transform.position;

        // Nombre del label (visible al jugador)
        public string labelDisplayName => labelName;

        public void Setup(string name, Transform target, bool inFront = false, float extraHeight = 0f)
        {
            labelName    = name;
            followTarget = target;
            showInFront  = inFront;
            heightOffset = extraHeight;
        }

        private void OnEnable()  { All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        private void Start()
        {
            BuildCanvas();
        }

        private void LateUpdate()
        {
            if (followTarget == null || Camera.main == null) return;
            SnapToTarget();
            FaceCamera();
            UpdateText();
        }

        private void UpdateText()
        {
            if (labelText == null) return;
            Vector3 cam = Camera.main.transform.position;
            Vector3 obj = followTarget.position;
            // Distancia solo en XZ: ignora diferencia de altura entre cámara y origen del mueble
            float dist = Vector2.Distance(new Vector2(cam.x, cam.z), new Vector2(obj.x, obj.z));
            if (dist <= RevealRadius)
            {
                labelText.text  = labelName;
                labelText.color = _isAudioSource ? ColorConflict : ColorDefault;
            }
            else
            {
                labelText.text  = "???";
                labelText.color = ColorDefault;
            }
        }

        public void SetLabelColor(Color color)
        {
            if (labelText != null) labelText.color = color;
        }

        public void ResetColor() => SetLabelColor(ColorDefault);

        // Marca este label como fuente sonora activa (cambia a rojo al acercarse)
        public void MarkAsAudioSource(bool value) => _isAudioSource = value;

        /// <summary>
        /// Resetea todos los labels a verde y luego pone en rojo los que están
        /// dentro de <paramref name="radius"/> metros de algún marker asignado
        /// (el label del propio objeto fuente queda en verde).
        /// </summary>
        public static void ApplyProximityColors(List<AudioTargetMarker> assignedMarkers, float radius)
        {
            foreach (FurnitureLabel lbl in All) lbl.ResetColor();

            foreach (AudioTargetMarker marker in assignedMarkers)
            {
                if (marker == null) continue;
                Vector3 srcPos = marker.transform.position;

                foreach (FurnitureLabel lbl in All)
                {
                    if (lbl == marker.furnitureLabel) continue;
                    if (Vector3.Distance(lbl.FollowPosition, srcPos) <= radius)
                        lbl.SetLabelColor(ColorConflict);
                }
            }
        }

        private void SnapToTarget()
        {
            MeshRenderer[] renderers = followTarget.GetComponentsInChildren<MeshRenderer>();

            if (renderers.Length > 0)
            {
                Bounds combined = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    combined.Encapsulate(renderers[i].bounds);

                Vector3 pos;
                if (showInFront)
                {
                    // Frente del objeto según su forward local (no la cámara)
                    Vector3 fwd = followTarget.forward;
                    fwd.y = 0f;
                    if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
                    fwd.Normalize();

                    float radius = Mathf.Max(combined.extents.x, combined.extents.z);
                    pos = combined.center + fwd * (radius + 0.12f);
                    pos.y = combined.center.y;
                }
                else
                {
                    pos = new Vector3(combined.center.x, combined.max.y + 0.25f + heightOffset, combined.center.z);
                }

                transform.position = pos;
            }
            else
            {
                Vector3 base3 = followTarget.position;
                transform.position = showInFront
                    ? new Vector3(base3.x, base3.y + 0.6f, base3.z + 0.5f)
                    : new Vector3(base3.x, base3.y + 1.2f, base3.z);
            }

            transform.localScale = new Vector3(0.003f, 0.003f, 0.003f);
        }

        private void FaceCamera()
        {
            // Canvas WorldSpace: +Z debe apuntar LEJOS de la cámara para que el texto sea legible
            Vector3 away = transform.position - Camera.main.transform.position;
            if (away.sqrMagnitude < 0.0001f) return;
            float yAngle = Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, yAngle, 0f);
        }

        private void BuildCanvas()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            RectTransform rt = GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(350f, 70f);

            GameObject bgGO = new GameObject("BG", typeof(RectTransform));
            bgGO.transform.SetParent(transform, false);
            bgGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
            StretchFill(bgGO.GetComponent<RectTransform>());

            GameObject txtGO = new GameObject("Text", typeof(RectTransform));
            txtGO.transform.SetParent(transform, false);
            StretchFill(txtGO.GetComponent<RectTransform>());
            labelText              = txtGO.AddComponent<TextMeshProUGUI>();
            labelText.text         = "???";
            labelText.fontSize     = 38f;
            labelText.fontStyle    = FontStyles.Bold;
            labelText.alignment    = TextAlignmentOptions.Center;
            labelText.color        = ColorDefault;
            labelText.overflowMode = TextOverflowModes.Ellipsis;
        }

        private static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }
    }
}
