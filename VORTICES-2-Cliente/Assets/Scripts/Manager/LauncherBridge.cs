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
        // Flag estática: evita que LauncherBridge vuelva a correr cuando
        // el usuario finaliza la sesión y regresa al Main Menu.
        // Se resetea solo al reiniciar la aplicación (nuevo lanzamiento desde el Launcher).
        private static bool hasAutoLaunched = false;

        private IEnumerator Start()
        {
            // Si ya se hizo un auto-launch en esta ejecución, no interferir con
            // el flujo normal de retorno al Main Menu.
            if (hasAutoLaunched) yield break;

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

                bool isJoinMode = config.isOnlineSession && (config.audioPaths == null || config.audioPaths.Count == 0);

                hasAutoLaunched = true;
                foreach (Canvas c in FindObjectsOfType<Canvas>()) c.enabled = false;
                foreach (Camera cam in FindObjectsOfType<Camera>()) cam.enabled = false;

                if (isJoinMode)
                    ApplyJoinSessionConfig(sm, config);
                else
                    ApplySalaConfig(sm, config);
            }
            else if (config.targetEnvironment == "Museum" || config.targetEnvironment == "Circular")
            {
                if (string.IsNullOrEmpty(config.sessionName))
                {
                    Debug.LogWarning($"[LauncherBridge] targetEnvironment={config.targetEnvironment} pero sessionName vacío — flujo normal.");
                    yield break;
                }

                // REVISAR Y BORRAR — detección antigua: solo miraba audioPaths, ignoraba elementPaths
                // bool isJoinMode = config.isOnlineSession && (config.audioPaths == null || config.audioPaths.Count == 0);

                // (fix añadido) JOIN = online sin audio Y sin elementos; examinador Museum puede no tener audio pero sí elementos
                bool isJoinMode = config.isOnlineSession
                    && (config.audioPaths  == null || config.audioPaths.Count  == 0)
                    && (config.elementPaths == null || config.elementPaths.Count == 0);

                hasAutoLaunched = true;
                foreach (Canvas c in FindObjectsOfType<Canvas>()) c.enabled = false;
                foreach (Camera cam in FindObjectsOfType<Camera>()) cam.enabled = false;

                if (isJoinMode)
                    ApplyJoinMuseumConfig(sm, config);
                else
                    ApplyMuseumConfig(sm, config);
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
            sm.browsingMode    = config.isOnlineSession ? "Online" : "Local";
            sm.displayMode     = "Sala";
            sm.isOnlineSession = config.isOnlineSession;

            // SceneTransitionManager necesita que AddonsController.currentEnvironmentObject
            // esté seteado — sin esto la escena no carga (sceneName queda vacío).
            AddonsController addons = AddonsController.instance;
            if (addons != null)
            {
                int count = addons.environmentObjects != null ? addons.environmentObjects.Count : -1;
                Debug.Log($"[LauncherBridge] environmentObjects.Count = {count}");

                bool found = false;
                if (addons.environmentObjects != null)
                {
                    foreach (EnvironmentObject envObj in addons.environmentObjects)
                    {
                        string name = envObj != null ? envObj.environmentName : "(null)";
                        Debug.Log($"[LauncherBridge] EnvironmentObject encontrado: '{name}'");
                        if (envObj != null && envObj.environmentName == "Sala Environment")
                        {
                            addons.currentEnvironmentObject = envObj;
                            found = true;
                            Debug.Log("[LauncherBridge] currentEnvironmentObject seteado a 'Sala Environment'.");
                            break;
                        }
                    }
                }
                if (!found)
                {
                    // Sala Environment está compilada en el build (Build Settings) pero no fue
                    // registrada en environmentObjects. La creamos como built-in aquí.
                    EnvironmentObject salaEnv = new EnvironmentObject();
                    salaEnv.environmentName = "Sala Environment";
                    salaEnv.isBuiltIn       = true;
                    addons.currentEnvironmentObject = salaEnv;
                    Debug.Log("[LauncherBridge] EnvironmentObject built-in creado para 'Sala Environment'.");
                }
            }
            else
            {
                Debug.LogWarning("[LauncherBridge] AddonsController.instance es null.");
            }

            sm.minRooms = Mathf.Max(1, config.minRooms);
            sm.maxRooms = Mathf.Max(sm.minRooms, config.maxRooms);

            sm.elementPaths = config.audioPaths != null
                ? new List<string>(config.audioPaths)
                : new List<string>();

            // audioPaths es lo que usa AssignUserAudioCoroutine para cargar los clips
            sm.audioPaths = config.audioPaths != null
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

            if (config.isOnlineSession)
            {
                sm.isOnlineSession  = true;
                sm.launcherServerIp = config.serverIpAddress;
            }

            // Voz por proximidad: true = canal posicional Vivox, false = canal global
            sm.proximityVoice = config.proximityVoice;

            Debug.Log($"[LauncherBridge] Audio config inyectada (configLevel={sm.configLevel}, {sm.audioPaths.Count} archivos, online={config.isOnlineSession}, proximityVoice={config.proximityVoice}).");
        }

        private void ApplyMuseumConfig(SessionManager sm, LauncherSessionConfig config)
        {
            string envShort = config.targetEnvironment; // "Museum" 
            string envFull  = envShort + " Environment";

            sm.sessionName     = config.sessionName;
            sm.userId          = config.userId;
            sm.environmentName = envShort;
            sm.browsingMode    = config.isOnlineSession ? "Online" : "Local";
            sm.displayMode     = envShort;
            sm.isOnlineSession = config.isOnlineSession;

            sm.elementPaths = config.elementPaths != null
                ? new List<string>(config.elementPaths)
                : new List<string>();

            sm.selectedObjectTypes = new List<string>();

            AddonsController addons = AddonsController.instance;
            if (addons != null)
            {
                // FIX: en auto-launch nunca se pasa por SessionController.LoadEnvironments(),
                // así que environmentObjects está vacío. Cargamos los addons aquí igual que
                // hace HandleActiveSessionResponse para el cliente que se une.
                if (addons.environmentObjects == null || addons.environmentObjects.Count == 0)
                    addons.LoadAddonObjects();

                bool found = false;
                if (addons.environmentObjects != null)
                {
                    foreach (EnvironmentObject envObj in addons.environmentObjects)
                    {
                        if (envObj != null && envObj.environmentName == envFull)
                        {
                            addons.currentEnvironmentObject = envObj;
                            found = true;
                            break;
                        }
                    }
                }

                // Si el addon estaba desactivado en addons.json, forzarlo y recargar.
                if (!found && addons.allAddonsData != null)
                {
                    foreach (Addon addon in addons.allAddonsData)
                    {
                        if (addon.addonType == "Environment" && addon.addonName == envFull)
                        {
                            Debug.Log($"[LauncherBridge] Addon '{addon.addonName}' desactivado → habilitando para auto-launch.");
                            addon.enabled = true;
                            break;
                        }
                    }
                    addons.LoadAddonObjects();
                    if (addons.environmentObjects != null)
                    {
                        foreach (EnvironmentObject envObj in addons.environmentObjects)
                        {
                            if (envObj != null && envObj.environmentName == envFull)
                            {
                                addons.currentEnvironmentObject = envObj;
                                found = true;
                                break;
                            }
                        }
                    }
                }

                if (!found)
                    Debug.LogError($"[LauncherBridge] No se encontró '{envFull}' en addons. Verifica que el bundle esté en Addons/Environment/.");
            }

            if (config.categories != null && config.categories.Count > 0)
                PreWriteCategories(config.sessionName, config.userId, config.categories);

            ApplyAudioConfig(sm, config);

            Debug.Log($"[LauncherBridge] {envShort} auto-launch para '{config.sessionName}'.");
            sm.LaunchSession();
        }

        private void PreWriteCategories(string sessionName, int userId, List<string> cats)
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath, "Session categories.json");

            CategoryController.CategorySaveData saveData;
            if (System.IO.File.Exists(path))
            {
                try
                {
                    saveData = JsonUtility.FromJson<CategoryController.CategorySaveData>(
                        System.IO.File.ReadAllText(path));
                    saveData.allSessionCategory ??= new List<CategoryController.SessionCategory>();
                }
                catch { saveData = new CategoryController.CategorySaveData { allSessionCategory = new List<CategoryController.SessionCategory>() }; }
            }
            else
            {
                saveData = new CategoryController.CategorySaveData { allSessionCategory = new List<CategoryController.SessionCategory>() };
            }

            saveData.allSessionCategory.RemoveAll(s => s.sessionName == sessionName && s.userId == userId);

            saveData.allSessionCategory.Add(new CategoryController.SessionCategory
            {
                sessionName          = sessionName,
                userId               = userId,
                categoriesList       = new List<string>(cats),
                selectedCategoriesList = new List<string>()
            });

            System.IO.File.WriteAllText(path, JsonUtility.ToJson(saveData));
            Debug.Log($"[LauncherBridge] Categorías pre-escritas para '{sessionName}': {string.Join(", ", cats)}");
        }

        private void ApplyJoinMuseumConfig(SessionManager sm, LauncherSessionConfig config)
        {
            string envShort = config.targetEnvironment; // "Museum" o "Circular"
            string envFull  = envShort + " Environment";

            sm.sessionName      = config.sessionName;
            sm.userId           = config.userId;
            sm.environmentName  = envShort;
            sm.isOnlineSession  = true;
            sm.launcherServerIp = config.serverIpAddress;
            sm.browsingMode     = "Online";
            sm.displayMode      = envShort;
            sm.proximityVoice   = config.proximityVoice;

            AddonsController addons = AddonsController.instance;
            if (addons != null)
            {
                // FIX: mismo problema que ApplyMuseumConfig — environmentObjects vacío en auto-launch.
                if (addons.environmentObjects == null || addons.environmentObjects.Count == 0)
                    addons.LoadAddonObjects();

                bool found = false;
                if (addons.environmentObjects != null)
                {
                    foreach (EnvironmentObject envObj in addons.environmentObjects)
                    {
                        if (envObj != null && envObj.environmentName == envFull)
                        {
                            addons.currentEnvironmentObject = envObj;
                            found = true;
                            break;
                        }
                    }
                }

                if (!found && addons.allAddonsData != null)
                {
                    foreach (Addon addon in addons.allAddonsData)
                    {
                        if (addon.addonType == "Environment" && addon.addonName == envFull)
                        {
                            Debug.Log($"[LauncherBridge] Addon '{addon.addonName}' desactivado → habilitando para JOIN auto-launch.");
                            addon.enabled = true;
                            break;
                        }
                    }
                    addons.LoadAddonObjects();
                    if (addons.environmentObjects != null)
                    {
                        foreach (EnvironmentObject envObj in addons.environmentObjects)
                        {
                            if (envObj != null && envObj.environmentName == envFull)
                            {
                                addons.currentEnvironmentObject = envObj;
                                found = true;
                                break;
                            }
                        }
                    }
                }

                if (!found)
                    Debug.LogError($"[LauncherBridge] No se encontró '{envFull}' en addons (JOIN). Verifica que el bundle esté en Addons/Environment/.");
            }

            Debug.Log($"[LauncherBridge] {envShort} JOIN para '{config.sessionName}', IP={config.serverIpAddress}.");
            sm.JoinSession(config.serverIpAddress);
        }

        private void ApplyJoinSessionConfig(SessionManager sm, LauncherSessionConfig config)
        {
            // AÑADIDO: Setear datos mínimos necesarios para JOIN
            sm.sessionName     = config.sessionName;
            sm.userId          = config.userId;
            sm.environmentName = "Sala";
            sm.isOnlineSession = true;
            sm.launcherServerIp = config.serverIpAddress;
            sm.browsingMode = "Online";
            sm.displayMode  = "Sala";
            sm.minRooms     = config.minRooms;
            sm.maxRooms     = config.maxRooms;

            // Voz por proximidad: también aplica al participante que se une
            sm.proximityVoice = config.proximityVoice;

            Debug.Log($"[LauncherBridge]: Configuración JOIN aplicada (sessionName='{config.sessionName}', userId={config.userId}, online=true, serverIp={config.serverIpAddress}, proximityVoice={config.proximityVoice}).");

            // AÑADIDO: Asegurar que AddonsController esté listo (mismo proceso que en ApplySalaConfig)
            AddonsController addons = AddonsController.instance;
            if (addons != null)
            {
                bool found = false;
                if (addons.environmentObjects != null)
                {
                    foreach (EnvironmentObject envObj in addons.environmentObjects)
                    {
                        if (envObj != null && envObj.environmentName == "Sala Environment")
                        {
                            addons.currentEnvironmentObject = envObj;
                            found = true;
                            Debug.Log("[LauncherBridge] AÑADIDO: currentEnvironmentObject seteado a 'Sala Environment' (JOIN mode).");
                            break;
                        }
                    }
                }
                if (!found)
                {
                    EnvironmentObject salaEnv = new EnvironmentObject();
                    salaEnv.environmentName = "Sala Environment";
                    salaEnv.isBuiltIn       = true;
                    addons.currentEnvironmentObject = salaEnv;
                    Debug.Log("[LauncherBridge] AÑADIDO: EnvironmentObject built-in creado para 'Sala Environment' (JOIN mode).");
                }
            }

            // AÑADIDO: Llamar JoinSession en lugar de LaunchSession
            Debug.Log("[LauncherBridge] AÑADIDO: Llamando sm.JoinSession() para unirse a sesión existente.");
            sm.JoinSession(config.serverIpAddress);
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
        public List<string> audioPaths    = new List<string>();

        // Step 2b - element paths (Museum/Circular)
        public List<string> elementPaths  = new List<string>();

        // Categorías (Museum/Circular)
        public List<string> categories    = new List<string>();

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

        // Online
        public bool   isOnlineSession = false;
        public string serverIpAddress = "";

        // Voz por proximidad: true = canal posicional Vivox (volumen según distancia), false = canal global
        public bool proximityVoice = false;
    }
}
