using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Vortices
{
    /// <summary>
    /// Lee parameters.json escrito por el Interaction Launcher.
    /// - Si targetEnvironment == "Sala": popula todo y lanza la sesión automáticamente.
    /// - Si targetEnvironment == "": solo inyecta la config de audio en SessionManager
    ///   y deja que el usuario navegue el menú VR normalmente.
    /// Si el archivo no existe, no hace nada y el flujo normal continúa.
    /// </summary>
    public class LauncherBridge : MonoBehaviour
    {
        private IEnumerator Start()
        {
            // Esperar un frame para que SessionManager.Start() termine y asigne su instancia
            yield return null;

            string configPath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath),
                "parameters.json"
            );

            if (!File.Exists(configPath))
                yield break;

            LauncherSessionConfig config = null;
            try
            {
                string json = File.ReadAllText(configPath);
                config = JsonUtility.FromJson<LauncherSessionConfig>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LauncherBridge] Error leyendo parameters.json: {e.Message}");
                yield break;
            }

            if (config == null)
            {
                Debug.LogWarning("[LauncherBridge] parameters.json inválido — flujo normal.");
                yield break;
            }

            SessionManager sm = SessionManager.instance;
            if (sm == null)
            {
                Debug.LogError("[LauncherBridge] SessionManager.instance es null.");
                yield break;
            }

            if (config.targetEnvironment == "Sala")
            {
                if (string.IsNullOrEmpty(config.sessionName))
                {
                    Debug.LogWarning("[LauncherBridge] targetEnvironment=Sala pero sessionName vacío — flujo normal.");
                    yield break;
                }
                ApplySalaConfig(sm, config);
            }
            else
            {
                ApplyAudioConfig(sm, config);
            }
        }

        // Popula todo y lanza la sesión directamente (sin menús VR)
        private void ApplySalaConfig(SessionManager sm, LauncherSessionConfig config)
        {
            sm.sessionName     = config.sessionName;
            sm.userId          = config.userId;
            sm.environmentName = "Sala";
            sm.browsingMode    = "Local";
            sm.displayMode     = "Sala";
            sm.isOnlineSession = false;

            sm.minRooms = Mathf.Max(1, config.minRooms);
            sm.maxRooms = Mathf.Max(sm.minRooms, config.maxRooms);

            sm.elementPaths = config.audioPaths != null
                ? new List<string>(config.audioPaths)
                : new List<string>();

            sm.selectedDirections = config.selectedDirections != null
                ? new List<string>(config.selectedDirections)
                : new List<string>();

            sm.selectedObjectTypes = new List<string>();

            sm.hasRoomFilter = config.hasRoomFilter;
            sm.roomFilterAll = config.roomFilterAll;
            sm.roomFilterIds = config.roomFilterIds != null
                ? new List<int>(config.roomFilterIds)
                : new List<int>();

            ApplyAudioConfig(sm, config);

            Debug.Log($"[LauncherBridge] Sala auto-launch para '{config.sessionName}'.");
            sm.LaunchSession();
        }

        // Solo inyecta config de audio — el usuario navega el menú VR normalmente
        private void ApplyAudioConfig(SessionManager sm, LauncherSessionConfig config)
        {
            sm.configLevel = Mathf.Clamp(config.configLevel, 1, 6);

            sm.hasAcousticOverride = config.hasAcousticOverride;
            if (config.hasAcousticOverride)
                sm.acousticOverride = config.acousticOverride;

            sm.hasEmitterOverride    = true;
            sm.emitterBaseVolume     = config.emitterBaseVolume > 0f ? config.emitterBaseVolume : 1f;
            sm.emitterMinConfigLevel = Mathf.Clamp(config.emitterMinConfigLevel, 1, 6);
            sm.emitterMinDistance    = Mathf.Max(0f, config.emitterMinDistance);
            sm.emitterMaxDistance    = Mathf.Max(sm.emitterMinDistance + 0.01f, config.emitterMaxDistance);

            sm.audioPaths = config.audioPaths != null
                ? new List<string>(config.audioPaths)
                : new List<string>();

            Debug.Log($"[LauncherBridge] Audio config inyectada (configLevel={sm.configLevel}, {sm.audioPaths.Count} archivos).");
        }
    }

    // ─── Estructura del parameters.json ───────────────────────────────────────

    [Serializable]
    public class LauncherSessionConfig
    {
        // Modo de lanzamiento: "Sala" = auto-launch, "" = solo pre-carga audio
        public string targetEnvironment = "";

        // Básico (solo requerido si targetEnvironment == "Sala")
        public string sessionName = "";
        public int    userId      = 0;

        // Step 1 (solo Sala)
        public int minRooms = 1;
        public int maxRooms = 10;

        // Step 2 - audio paths
        public List<string> audioPaths = new List<string>();

        // Step 4 - inmersión (aplica a todos los entornos)
        public int  configLevel         = 4;
        public bool hasAcousticOverride = false;
        public AudioManager.ProfileOverrideData acousticOverride;

        // Step 5 - audio config (aplica a todos los entornos)
        public float emitterBaseVolume     = 1f;
        public int   emitterMinConfigLevel = 2;
        public float emitterMinDistance    = 1f;
        public float emitterMaxDistance    = 12f;

        // Step 5 - filtro de salas (solo Sala)
        public bool      hasRoomFilter = false;
        public bool      roomFilterAll = true;
        public List<int> roomFilterIds = new List<int>();

        // Step 6 - direcciones (solo Sala)
        public List<string> selectedDirections = new List<string>();
    }
}
