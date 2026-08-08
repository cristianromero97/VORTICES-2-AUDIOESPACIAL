using Mirror;
using UnityEngine;

namespace Vortices
{
    public class DebugHUD : MonoBehaviour
    {
        private float timer;
        private bool _visible = true;
        private string _hudText = "";
        private const float UpdateInterval = 0.5f;

        private GUIStyle _style;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateInstance()
        {
            var go = new GameObject("DebugHUD");
            go.AddComponent<DebugHUD>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
                _visible = !_visible;

            if (!_visible) return;

            timer += Time.deltaTime;
            if (timer < UpdateInterval) return;
            timer = 0f;

            float fps = 1f / Time.deltaTime;

            if (NetworkClient.isConnected)
            {
                float pingMs = (float)(NetworkTime.rtt * 1000.0);
                _hudText = $"FPS: {fps:F0}  |  Ping: {pingMs:F0} ms";
            }
            else
            {
                _hudText = $"FPS: {fps:F0}";
            }
        }

        private void OnGUI()
        {
            if (!_visible) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label);
                _style.fontSize = 20;
                _style.fontStyle = FontStyle.Bold;
                _style.normal.textColor = Color.white;
                _style.alignment = TextAnchor.UpperRight;
            }

            float w = 300f;
            float h = 30f;
            GUI.Label(new Rect(Screen.width - w - 10f, 10f, w, h), _hudText, _style);
        }
    }
}
