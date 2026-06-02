using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using System.Threading.Tasks;

using System.Linq;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using static System.Net.Mime.MediaTypeNames;
using static UnityEngine.Rendering.DebugUI;
 
namespace Vortices
{
    public class SessionManager : MonoBehaviour
    {
 
        // Static instance
        public static SessionManager instance;
 
        // Settings
        public string sessionName;
        public int userId;
        public string environmentName;
        public bool isOnlineSession = false;
        // Sala Environment settings
        public int configLevel = 4;
        public int minRooms    = 1;
        public int maxRooms    = 50;
        public List<string> selectedObjectTypes = new List<string>();
        public bool hasAcousticOverride = false;
        public AudioManager.ProfileOverrideData acousticOverride;
        public bool hasRoomFilter = false;
        public bool roomFilterAll = true;
        public List<int> roomFilterIds = new List<int>();
        public List<string> selectedDirections = new List<string>();
        // Step 5 — emitter global override
        public bool  hasEmitterOverride    = false;
        public float emitterBaseVolume     = 1f;
        public int   emitterMinConfigLevel = 2;
        public float emitterMinDistance    = 1f;
        public float emitterMaxDistance    = 12f;
        // Radio (metros) para marcar labels de objetos cercanos como conflicto (rojo)
        public float proximityLabelRadius  = 1.5f;
        // Environment Settings
        public string displayMode;
        public string browsingMode;
        public bool volumetric;
        public Vector3Int dimension;
        public List<string> elementPaths;
        public List<string> audioPaths;
        // Session Manager settings
        public float initializeTime = 2.0f;
        private bool sessionDataReceived = false;
 
 
        // Controllers
        [SerializeField] private SceneTransitionManager actualTransitionManager;
        [SerializeField] public CategoryController categoryController;
        [SerializeField] public ElementCategoryController elementCategoryController;
        [SerializeField] public SpawnController spawnController;
        [SerializeField] public AddonsController addonsController;
        private CategorySelector categorySelector;
        [SerializeField] public LoggingController loggingController;
        public InputController inputController;
        [SerializeField] public LocalizationController localizationController;
        [SerializeField] public ErrorController errorController;
        public RighthandTools righthandTools;
 
        // Coroutine
        public bool sessionLaunchRunning;
 
        public GameObject currentlySelected;
        public GameObject lastSelected;
        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
 
        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
 
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            lastSelected = null; // Reset lastSelected when a new scene is loaded
        }
 
        private void Start()
        {
            if (instance != null)
            {
                Destroy(gameObject);
                return;
            }
 
            categoryController = GameObject.FindObjectOfType<CategoryController>(true);
            elementCategoryController = GameObject.FindObjectOfType<ElementCategoryController>(true);
            inputController = GameObject.FindObjectOfType<InputController>(true);
 
            errorController.Initialize();
            inputController.Initialize();
            addonsController.Initialize();
            localizationController.Initialize();
 
            // Registrar handlers en el cliente
            NetworkClient.RegisterHandler<SessionCreatedMessage>(HandleSessionCreatedMessage);
            NetworkClient.RegisterHandler<ActiveSessionResponseMessage>(HandleActiveSessionResponse);
 
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
 
        private void HandleSessionCreatedMessage(SessionCreatedMessage msg)
        {
            if (!msg.success)
            {
                return;
            }
 
            sessionName = msg.sessionName;
            userId = msg.userId;
 
            if (msg.environmentName == "Circular Environment")
            {
                environmentName = "Circular";
            }
            else if (msg.environmentName == "Museum Environment")
            {
                environmentName = "Museum";
            }
            else if (msg.environmentName == "Sala Environment")
            {
                environmentName = "Sala";
            }
            else
            {
                environmentName = msg.environmentName;
            }
            isOnlineSession = msg.isOnlineSession;
            displayMode = msg.displayMode;
            browsingMode = msg.browsingMode;
            volumetric = msg.volumetric;
            dimension = msg.dimension;
            elementPaths = msg.elementPaths;
 
            categoryController.UpdateCategoriesList(msg.categories);
        }
 
 
        #region UI Handling
 
        private void Update()
        {
            if (EventSystem.current.currentSelectedGameObject != null)
            {
                currentlySelected = EventSystem.current.currentSelectedGameObject;
            }
            else
            {
                currentlySelected = null;
            }
            if (EventSystem.current.currentSelectedGameObject == null || !EventSystem.current.currentSelectedGameObject.activeInHierarchy)
            {
                FindAndSetNextSelectable();
            }
            else
            {
                lastSelected = EventSystem.current.currentSelectedGameObject;
            }
        }
 
        private void FindAndSetNextSelectable()
        {
            GameObject canvasObject = GameObject.Find("Canvas"); // Find the GameObject named "Canvas"
 
            if (canvasObject == null)
            {
                Debug.LogError("No GameObject named 'Canvas' found in the scene.");
                return;
            }
 
            Canvas canvasComponent = canvasObject.GetComponent<Canvas>(); // Get the Canvas component from the GameObject
 
            if (canvasComponent == null)
            {
                Debug.LogError("No Canvas component found on the 'Canvas' GameObject.");
                return;
            }
 
            Selectable[] selectables = canvasComponent.GetComponentsInChildren<Selectable>();
 
            if (selectables.Length == 0)
            {
                return; // No selectables in the canvas
            }
 
            int startIndex = 0;
            if (lastSelected != null)
            {
                int lastIndex = System.Array.FindIndex(selectables, selectable => selectable.gameObject == lastSelected);
                if (lastIndex != -1)
                {
                    startIndex = (lastIndex + 1) % selectables.Length;
                }
            }
 
            for (int i = 0; i < selectables.Length; i++)
            {
                int index = (startIndex + i) % selectables.Length;
                if (selectables[index].gameObject.activeInHierarchy && selectables[index].interactable)
                {
                    EventSystem.current.SetSelectedGameObject(selectables[index].gameObject);
                    lastSelected = selectables[index].gameObject;
                    break;
                }
            }
        }
 
        #endregion
 
        #region Data Operations
        public void LaunchSession()
        {
            if (!sessionLaunchRunning)
            {
                StartCoroutine(LaunchSessionCoroutine(sessionName, userId, environmentName));
            }
        }
 
        public IEnumerator LaunchSessionCoroutine(string sessionName, int userId, string environmentName)
        {
            sessionLaunchRunning = true;
 
            GameObject keyboard = GameObject.Find("Keyboard Canvas");
            if (keyboard != null)
            {
                HandKeyboard handKeyboard = keyboard.GetComponent<HandKeyboard>();
                if (handKeyboard != null)
                    handKeyboard.RemoveInputField();
            }
 
 
            // Switch to environment scene
            actualTransitionManager = GameObject.Find("TransitionManager").GetComponent<SceneTransitionManager>();
            actualTransitionManager.returnToMain = false;
 
            if (isOnlineSession)
                {
                    if (!NetworkClient.isConnected)
                    {
                        NetworkManager.singleton.networkAddress = "127.0.0.1"; // 134.65.228.226 Oracle
                        NetworkManager.singleton.StartClient();
 
                        float timeout = 10f;
                        while (!NetworkClient.isConnected && timeout > 0f)
                        {
                            timeout -= Time.deltaTime;
                            yield return null;
                        }
 
                        if (!NetworkClient.isConnected)
                        {
                            Debug.LogError("[SessionManager] No se pudo conectar al servidor.");
                            sessionLaunchRunning = false;
                            yield break;
                        }
                    }
 
                    SessionData sessionData = new SessionData
                    {
                        sessionName = sessionName,
                        userId = userId,
                        environmentName = environmentName,
                        elementPaths = elementPaths,
                        categories = categoryController.GetCategories(),
                        browsingMode = "Online"
                    };
 
                    yield return StartCoroutine(SendSessionDataToServer(sessionData));
 
                    
                    if (environmentName == "Circular")
                    {
                        environmentName = "Circular Environment";
                    }
                    else if (environmentName == "Museum")
                    {
                        environmentName = "Museum Environment";
                    }
                    else if (environmentName == "Sala")
                    {
                        environmentName = "Sala Environment";
                    }
 
                    yield return StartCoroutine(actualTransitionManager.GoToSceneRoutine());
            
                    yield return new WaitForSeconds(initializeTime);
 
 
 
                    //  AHORA BUSCAMOS TODOS LOS OBJETOS NECESARIOS 
                    actualTransitionManager = GameObject.FindObjectOfType<SceneTransitionManager>(true);
                    categoryController = GameObject.FindObjectOfType<CategoryController>(true);
                    elementCategoryController = GameObject.FindObjectOfType<ElementCategoryController>(true);
                    loggingController = GameObject.FindObjectOfType<LoggingController>(true);
                    spawnController = GameObject.FindObjectOfType<SpawnController>(true);
                    righthandTools = GameObject.FindObjectOfType<RighthandTools>(true);
 
                    elementCategoryController.Initialize();
                    loggingController.Initialize();
                    spawnController.Initialize();
                    inputController.RestartInputs();
 
                    sessionLaunchRunning = false;
 
                    StartCoroutine(ConnectToVoiceChatCoroutine(userId));
                    Debug.Log("Se envió la data al servidor.");
                    yield break;
                }
 
            Debug.Log("Empeze GoTo Offline");
            yield return StartCoroutine(actualTransitionManager.GoToSceneRoutine());
            
            // Un frame para que la escena termine de activarse (Start() de todos los objetos)
            yield return null;

            Debug.Log("Sali GoTo Offline");

            // Para Sala: configurar y regenerar AHORA, mientras la pantalla aún está negra
            // (FadeIn acaba de empezar — el jugador no verá el corredor inicial incorrecto)
            if (environmentName == "Sala")
            {
                AudioManager audioManager = GameObject.FindObjectOfType<AudioManager>(true);
                if (audioManager != null)
                {
                    audioManager.SetConfigLevel(configLevel);
                    if (hasAcousticOverride)
                        audioManager.ApplyProfileOverride(configLevel, acousticOverride);
                    if (hasRoomFilter)
                    {
                        audioManager.SetRoomFilterEnabled(true);
                        if (roomFilterAll)
                            audioManager.EnableAllRooms();
                        else
                            audioManager.SetEnabledRooms(roomFilterIds);
                    }
                }
                else
                    Debug.LogWarning("[SessionManager] AudioManager no encontrado en Sala Environment.");

                RoomGeometry roomGeometry = GameObject.FindObjectOfType<RoomGeometry>(true);
                if (roomGeometry != null)
                {
                    roomGeometry.SetRoomConfig(minRooms, maxRooms);
                    roomGeometry.RegenerateScenario();
                }
                else
                    Debug.LogWarning("[SessionManager] RoomGeometry no encontrado en Sala Environment.");
            }

            yield return new WaitForSeconds(initializeTime);
 
            // When done, configure controllers of the scene
            Debug.Log("Busco Offline");
            actualTransitionManager = GameObject.FindObjectOfType<SceneTransitionManager>(true);
            categoryController = GameObject.FindObjectOfType<CategoryController>(true);
            elementCategoryController = GameObject.FindObjectOfType<ElementCategoryController>(true);
            loggingController = GameObject.FindObjectOfType<LoggingController>(true);
            spawnController = GameObject.FindObjectOfType<SpawnController>(true);
            righthandTools = GameObject.FindObjectOfType<RighthandTools>(true);

            if (elementCategoryController != null) elementCategoryController.Initialize();
            else Debug.LogWarning("[SessionManager] elementCategoryController no encontrado en escena.");
            if (loggingController != null) loggingController.Initialize();
            else Debug.LogWarning("[SessionManager] loggingController no encontrado en escena.");
            if (spawnController != null) spawnController.Initialize();
            else Debug.LogWarning("[SessionManager] spawnController no encontrado en escena.");
 
            // Sala: AudioManager y RoomGeometry ya fueron configurados antes del WaitForSeconds.
            // Solo queda asignar el audio de usuario a los objetos marcados.
            if (environmentName == "Sala")
            {
                yield return StartCoroutine(AssignUserAudioCoroutine());
            }
            else if (environmentName == "Museum" || environmentName == "Circular")
            {
                // Aplicar configLevel y override acústico del AudioSpatialPanel (Options)
                AudioManager audioManager = GameObject.FindObjectOfType<AudioManager>(true);
                if (audioManager != null)
                {
                    audioManager.SetConfigLevel(configLevel);
                    if (hasAcousticOverride)
                        audioManager.ApplyProfileOverride(configLevel, acousticOverride);
                }
                else
                    Debug.LogWarning($"[SessionManager] AudioManager no encontrado en {environmentName} Environment.");

                // Asignar los archivos de audio configurados en AudioSpatialPanel
                if (audioPaths != null && audioPaths.Count > 0)
                    yield return StartCoroutine(AssignUserAudioCoroutine(audioPaths));
                else
                    Debug.LogWarning($"[SessionManager] audioPaths vacío — no se asignará audio en {environmentName}.");
            }
 
            inputController.RestartInputs();
 
            sessionLaunchRunning = false;
            Debug.Log("Termino Offline");
        }
 
        public IEnumerator SendSessionDataToServer(SessionData sessionData)
        {
            if (!NetworkClient.isConnected)
            {
                Debug.LogError("El cliente no est� conectado al servidor. No se puede enviar la sesi�n.");
                yield break;
            }
 
            Debug.Log("Enviando datos de sesi�n al servidor...");
            NetworkClient.Send(new CreateSessionMessage
            {
                sessionName = sessionName,
                userId = userId,
                environmentName = environmentName,
                isOnlineSession = isOnlineSession,
                displayMode = displayMode,
                browsingMode = browsingMode,
                volumetric = volumetric,
                dimension = dimension,
                categories = categoryController.GetCategories(),
                elementPaths = elementPaths
            });
 
            yield return new WaitForSeconds(1.0f);
        }
 
        public IEnumerator StopSessionCoroutine()
        {
            sessionLaunchRunning = true;
 
            if (isOnlineSession)
                yield return StartCoroutine(DisconnectFromVivoxCoroutine());
 
            if (NetworkClient.isConnected)
            {
                Debug.Log("El cliente est� conectado a una sesi�n. Desconectando...");
                NetworkClient.Disconnect();
 
                // Esperar a que el cliente se desconecte
                float timeout = 5f; // Tiempo m�ximo para desconectar
                while (NetworkClient.isConnected && timeout > 0f)
                {
                    timeout -= Time.deltaTime;
                    yield return null;
                }
 
                if (NetworkClient.isConnected)
                {
                    Debug.LogError("No se pudo desconectar del servidor dentro del tiempo de espera.");
                    yield break;
                }
                else
                {
                    Debug.Log("Desconectado del servidor con �xito.");
                }
            }
            else
            {
                Debug.Log("El cliente no est� conectado a ninguna sesi�n.");
            }
 
            actualTransitionManager = GameObject.Find("TransitionManager").GetComponent<SceneTransitionManager>();
            righthandTools = GameObject.FindObjectOfType<RighthandTools>(true);
 
            if (actualTransitionManager == null)
            {
                Debug.LogError("No se encontro el actualTransitionManager");
            }
            else
            {
                Debug.Log("Se encontro el actualTransitionManager");
            }
 
            if (righthandTools == null)
            {
                Debug.LogError("righthandTools no est� asignado. Verifica su inicializaci�n.");
                yield break; // Salimos del coroutine para evitar m�s errores
            }
            else
            {
                Debug.Log("righthandTools est� asignado correctamente.");
            }
 
            Fade toolsFader = righthandTools.GetComponent<Fade>();
            yield return StartCoroutine(toolsFader.FadeOutCoroutine());
 
            actualTransitionManager.returnToMain = true;
            yield return StartCoroutine(actualTransitionManager.GoToSceneRoutine());
 
            yield return new WaitForSeconds(initializeTime);
 
            categorySelector = GameObject.FindObjectOfType<CategorySelector>(true);
 
            if(categorySelector == null)
            {
                Debug.LogError("No se encontro el categorySelector");
            }
            else
            {
                Debug.Log("categoriSelector encontrado");
            }
 
            ResetSessionManager();
 
            sessionLaunchRunning = false;
        }
 
        private void ResetSessionManager()
        {
            Debug.Log("Reiniciando SessionManager...");
 
            // Restablecer valores del SessionManager
            sessionName = string.Empty;
            userId = 0;
            environmentName = string.Empty;
            configLevel = 4;
            displayMode = null;
            volumetric = false;
            dimension = Vector3Int.zero;
            elementPaths?.Clear();
            audioPaths?.Clear();
            categoryController.UpdateCategoriesList(null);
            isOnlineSession = false;
            browsingMode = string.Empty;
 
            // Reiniciar controladores auxiliares si es necesario
            categoryController?.Initialize();
            elementCategoryController?.Initialize();
            loggingController?.Initialize();
 
            Debug.Log("SessionManager reiniciado con �xito.");
        }
 
        public void JoinSession(string ipAddress)
        {
            if (!string.IsNullOrEmpty(ipAddress))
            {
                NetworkManager.singleton.networkAddress = ipAddress;
                NetworkManager.singleton.StartClient();
                StartCoroutine(WaitForConnectionAndJoinSession()); 
 
                isOnlineSession = true;
 
                StartCoroutine(WaitForSessionDataAndLoadScene());
            }
            else
            {
                Debug.LogWarning("La dirección IP está vacía.");
            }
        }
 
 
        private IEnumerator JoinSessionRoutine()
        {
            Debug.Log("[DEBUG] Iniciando carga de escena para sesión online...");
        
            // Cambiar a la escena correspondiente
            yield return StartCoroutine(actualTransitionManager.GoToSceneRoutine());
 
            Debug.Log("[DEBUG] Esperando tiempo de inicialización...");
            yield return new WaitForSeconds(initializeTime);
 
            //  AHORA BUSCAMOS TODOS LOS OBJETOS NECESARIOS 
            Debug.Log("[DEBUG] Buscando y configurando controladores en la escena...");
 
            actualTransitionManager = GameObject.FindObjectOfType<SceneTransitionManager>(true);
            categoryController = GameObject.FindObjectOfType<CategoryController>(true);
            elementCategoryController = GameObject.FindObjectOfType<ElementCategoryController>(true);
            loggingController = GameObject.FindObjectOfType<LoggingController>(true);
            spawnController = GameObject.FindObjectOfType<SpawnController>(true);
            righthandTools = GameObject.FindObjectOfType<RighthandTools>(true);
 
            elementCategoryController.Initialize();
            loggingController.Initialize();
            spawnController.Initialize();
            inputController.RestartInputs();
 
            sessionLaunchRunning = false;
 
            // Conectar al canal de voz
            Debug.Log("[DEBUG] Conectando al canal de voz...");
            StartCoroutine(ConnectToVoiceChatCoroutine(userId));
        }
 
 
        private IEnumerator WaitForConnectionAndJoinSession()
        {
            float timeout = 10f;
            float elapsedTime = 0f;
 
            while (!NetworkClient.isConnected && elapsedTime < timeout)
            {
                elapsedTime += Time.deltaTime;
                yield return null;
            }
 
            if (NetworkClient.isConnected)
            {
                NetworkClient.Send(new RequestActiveSessionMessage());
            }
 
        }
 
        private void HandleActiveSessionResponse(ActiveSessionResponseMessage msg)
        {
            if (!msg.success)
            {
                return;
            }
 
            if (msg.sessionData.categories == null)
            {
                msg.sessionData.categories = new List<string>();
            }
 
            if (msg.sessionData.elementPaths == null)
            {
                msg.sessionData.elementPaths = new List<string>(); 
            }
 
            sessionName = msg.sessionData.sessionName;
 
            if (msg.sessionData.environmentName == "Circular Environment")
            {
                environmentName = "Circular";
            }
            else if (msg.sessionData.environmentName == "Museum Environment")
            {
                environmentName = "Museum";
            }
            else if (msg.sessionData.environmentName == "Sala Environment")
            {
                environmentName = "Sala";
            }
            else
            {
                environmentName = msg.sessionData.environmentName;
            }
 
            browsingMode = msg.sessionData.browsingMode;
            displayMode = msg.sessionData.displayMode;
            volumetric = msg.sessionData.volumetric;
            dimension = msg.sessionData.dimension;
 
            foreach (string path in msg.sessionData.elementPaths)
            {
                Debug.Log($"  - {path}");
            }
            categoryController.Initialize();
            //  Asignamos los `elementPaths` al SessionManager
            elementPaths = new List<string>(msg.sessionData.elementPaths);
 
            //  Actualizamos el AddonsController con el environment correcto
            AddonsController addonsController = AddonsController.instance;
            if (addonsController != null)
            {
                foreach (var envObject in addonsController.environmentObjects)
                {
                    if (envObject.environmentName == msg.sessionData.environmentName)
                    {
                        addonsController.currentEnvironmentObject = envObject;
                        break;
                    }
                }
            }
 
            //  Sincronizar categorías en CategoryController
            if (categoryController != null)
            {
                categoryController.UpdateCategoriesList(msg.sessionData.categories);
 
                //  Ahora sincronizamos las categorías seleccionadas
                if (msg.sessionData.categories.Count > 0)
                {
                    categoryController.UpdateSelectedCategoriesList(msg.sessionData.categories);
                }
            }
 
 
            //  Confirmamos que los datos han sido recibidos
            sessionDataReceived = true;
        }
 
 
 
 
        private IEnumerator WaitForSessionDataAndLoadScene()
        {
 
            float timeout = 10f;
            while (!sessionDataReceived && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
 
            if (!sessionDataReceived)
            {
                yield break;
            }
 
            StartCoroutine(JoinSessionRoutine());
        }
 
 
 
 
        private IEnumerator AssignUserAudioCoroutine(List<string> paths = null)
        {
            if (paths == null) paths = elementPaths;

            if (paths == null || paths.Count == 0)
            {
                Debug.LogWarning("[SessionManager] AssignUserAudio: lista de paths vacía, no se asigna audio de usuario.");
                yield break;
            }

            // Esperar un frame para que Destroy() del escenario anterior se procese
            // (generateOnStart agrega markers al escenario inicial; RegenerateScenario lo destruye con rename,
            // pero el Destroy es diferido — sin yield return null, FindObjectsOfType los devuelve igual)
            yield return null;

            AudioTargetMarker[] allMarkers = GameObject.FindObjectsOfType<AudioTargetMarker>(true);

            // Filtrar markers de objetos ya destruidos (del escenario previo pendiente de Destroy)
            List<AudioTargetMarker> markers = new List<AudioTargetMarker>();
            foreach (AudioTargetMarker m in allMarkers)
                if (m != null && m.gameObject != null) markers.Add(m);

            // Filtrar por tipo de objeto si el examinador eligió tipos específicos en el Step 3
            // (selectedObjectTypes vacío = todos los tipos son elegibles)
            if (selectedObjectTypes != null && selectedObjectTypes.Count > 0)
            {
                List<AudioTargetMarker> filtered = new List<AudioTargetMarker>();
                foreach (AudioTargetMarker m in markers)
                    if (selectedObjectTypes.Contains(m.prefabType)) filtered.Add(m);

                if (filtered.Count > 0)
                    markers = filtered;
                else
                    Debug.LogWarning($"[SessionManager] AssignUserAudio: ningún marker coincide con los tipos seleccionados {string.Join(", ", selectedObjectTypes)} — usando todos.");
            }

            if (AudioManager.Instance == null)
                Debug.LogWarning("[SessionManager] AssignUserAudio: AudioManager.Instance es null — se usará fallback (Custom rolloff, sin perfil acústico).");

            if (markers.Count == 0)
            {
                Debug.LogWarning("[SessionManager] AssignUserAudio: no se encontraron AudioTargetMarker válidos en la escena.");
                yield break;
            }

            // Shuffle aleatorio de los markers (Fisher-Yates) — distinta asignación cada sesión
            for (int i = markers.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                AudioTargetMarker tmp = markers[i];
                markers[i] = markers[j];
                markers[j] = tmp;
            }

            // Asignación 1:1 — cada archivo a un objeto diferente, sin repetir
            int assignCount = Mathf.Min(paths.Count, markers.Count);
            Debug.Log($"[SessionManager] AssignUserAudio: {paths.Count} archivo(s), {markers.Count} objeto(s) elegibles → asignando {assignCount}.");

            var assignedSounds = new List<(AudioTargetMarker marker, AudioSource src)>();

            for (int i = 0; i < assignCount; i++)
            {
                AudioTargetMarker marker = markers[i];
                string path = paths[i];

                AudioClip clip = null;
                yield return StartCoroutine(LoadAudioClipCoroutine(path, loaded => clip = loaded));

                if (marker == null)
                {
                    Debug.LogWarning($"[SessionManager] AssignUserAudio: marker destruido durante carga de '{path}'.");
                    continue;
                }

                if (clip == null)
                {
                    Debug.LogWarning($"[SessionManager] AssignUserAudio: no se pudo cargar '{path}' (Room {marker.roomIndex}).");
                    continue;
                }

                // Crear AudioSource hijo dedicado — completamente separado del sistema AudioManager/SoundEmitter.
                // Usar SoundEmitter causaba auto-registro → AudioManager lo activaba automáticamente.
                GameObject audioGO = new GameObject("UserAudio");
                audioGO.transform.SetParent(marker.transform, false);
                AudioSource src = audioGO.AddComponent<AudioSource>();
                src.clip        = clip;
                src.playOnAwake = false;
                src.loop        = true;
                src.minDistance = emitterMinDistance;
                src.maxDistance = emitterMaxDistance;

                // Aplicar perfil acústico del Step 4 (mismo que usan los SoundEmitters del ambiente)
                AudioManager.AcousticProfile profile = AudioManager.Instance?.GetAcousticProfile(configLevel);
                if (profile != null)
                {
                    src.spatialBlend = profile.spatialBlend;
                    src.spread       = profile.spread;
                    src.dopplerLevel = profile.dopplerLevel;
                    src.spatialize   = profile.spatialize;
                    src.volume       = emitterBaseVolume * profile.globalVolume;

                    if (profile.rolloffMode == AudioRolloffMode.Custom && profile.customRolloffCurve != null)
                    {
                        src.rolloffMode = AudioRolloffMode.Custom;
                        src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, profile.customRolloffCurve);
                    }
                    else if (profile.rolloffMode == AudioRolloffMode.Custom)
                    {
                        // Custom sin curva definida → usar Logarithmic para evitar volumen 0
                        src.rolloffMode = AudioRolloffMode.Logarithmic;
                        Debug.LogWarning($"[SessionManager] Perfil '{profile.levelName}' usa Custom rolloff sin curva → usando Logarithmic.");
                    }
                    else
                    {
                        src.rolloffMode = profile.rolloffMode;
                    }
                }
                else
                {
                    // Fallback si no hay AudioManager activo (ej: test directo en Sala sin pasar por menú)
                    src.spatialBlend = 1f;
                    src.rolloffMode  = AudioRolloffMode.Logarithmic;
                    src.volume       = emitterBaseVolume;
                }

                src.Stop();

                marker.audioFileName   = System.IO.Path.GetFileNameWithoutExtension(path);
                marker.userAudioSource = src;
                marker.furnitureLabel?.MarkAsAudioSource(true);

                assignedSounds.Add((marker, src));
                Debug.Log($"[SessionManager] AssignUserAudio: Room {marker.roomIndex} ({marker.prefabType}) ← {System.IO.Path.GetFileName(path)}");
                Debug.Log($"[SessionManager] AudioSource config → pos={marker.transform.position:F1}, vol={src.volume:F2}, spatialBlend={src.spatialBlend:F2}, minDist={src.minDistance}, rolloff={src.rolloffMode}, perfil={(AudioManager.Instance?.GetAcousticProfile(configLevel)?.levelName ?? "FALLBACK")}");
            }

            if (markers.Count > assignCount)
                Debug.Log($"[SessionManager] AssignUserAudio: {markers.Count - assignCount} objeto(s) sin audio (menos archivos que objetos).");

            // Colorear labels: rojo para objetos cercanos a una fuente sonora asignada
            // List<AudioTargetMarker> assignedMarkers = new List<AudioTargetMarker>();
            // foreach ((AudioTargetMarker m, AudioSource _) in assignedSounds) assignedMarkers.Add(m);
            // FurnitureLabel.ApplyProximityColors(assignedMarkers, proximityLabelRadius);

            // Inicializar SonidosPanel con los sonidos asignados exitosamente
            SonidosPanel sonidosPanel = GameObject.FindObjectOfType<SonidosPanel>(true);
            if (sonidosPanel != null)
                sonidosPanel.Initialize(assignedSounds);
            else
                Debug.LogWarning($"[SessionManager] SonidosPanel no encontrado en la escena {environmentName}.");

            Debug.Log($"[SessionManager] Todos los audios fueron asignados ({assignCount}). Listo para iniciar la experiencia.");

            InfoPanel infoPanel = GameObject.FindObjectOfType<InfoPanel>(true);
            if (infoPanel != null)
                infoPanel.NotifyAudioReady();
            else
                Debug.LogWarning("[SessionManager] InfoPanel no encontrado — el botón Start no será habilitado.");
        }

        private IEnumerator LoadAudioClipCoroutine(string path, System.Action<AudioClip> onLoaded)
        {
            string uri = "file:///" + path.Replace("\\", "/");
            AudioType audioType = GetAudioTypeFromPath(path);

            using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
            {
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[SessionManager] Error cargando audio '{path}': {req.error}");
                    onLoaded(null);
                }
                else
                {
                    onLoaded(DownloadHandlerAudioClip.GetContent(req));
                }
            }
        }

        private AudioType GetAudioTypeFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return AudioType.UNKNOWN;
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".mp3"  => AudioType.MPEG,
                ".wav"  => AudioType.WAV,
                ".ogg"  => AudioType.OGGVORBIS,
                ".aiff" => AudioType.AIFF,
                ".aif"  => AudioType.AIFF,
                _       => AudioType.UNKNOWN,
            };
        }

        private IEnumerator ConnectToVoiceChatCoroutine(int userId)
        {
            bool loginSuccess = false;
            bool channelJoinSuccess = false;
 
            // Intentar iniciar sesión en Vivox
            VivoxVoiceManager.Instance.LoginAsync(userId.ToString()).ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    loginSuccess = true;
                }
            });
 
            // Esperar que el login termine
            yield return new WaitUntil(() => loginSuccess);
 
            // Intentar unirse al canal de voz
            VivoxVoiceManager.Instance.JoinChannelAsync("VoRTIcESVoiceChat").ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    channelJoinSuccess = true;
                }
            });
 
            // Esperar que se conecte al canal
            yield return new WaitUntil(() => channelJoinSuccess);
        }
 
        private IEnumerator DisconnectFromVivoxCoroutine()
        {
            Debug.Log("[VoiceChat] Iniciando desconexión de Vivox...");
 
            if (VivoxVoiceManager.Instance != null)
            {
                // Salir del canal de voz
                if (VivoxVoiceManager.LobbyChannelName != null)
                {
                    Task leaveChannelTask = VivoxVoiceManager.Instance.LeaveChannelAsync(VivoxVoiceManager.LobbyChannelName);
                    yield return new WaitUntil(() => leaveChannelTask.IsCompleted);
 
                    if (leaveChannelTask.Exception != null)
                    {
                        Debug.LogError($"Error al salir del canal de voz: {leaveChannelTask.Exception.Message}");
                    }
                    else
                    {
                        Debug.Log("[VoiceChat] Desconectado del canal de voz.");
                    }
                }
 
                // Cerrar sesión de Vivox
                Task logoutTask = VivoxVoiceManager.Instance.LogoutAsync();
                yield return new WaitUntil(() => logoutTask.IsCompleted);
 
                if (logoutTask.Exception != null)
                {
                    Debug.LogError($"Error al cerrar sesión en Vivox: {logoutTask.Exception.Message}");
                }
                else
                {
                    Debug.Log("[VoiceChat] Sesión en Vivox cerrada.");
                }
            }
            else
            {
                Debug.LogError("[VoiceChat] VivoxVoiceManager.Instance es NULL. No se pudo desconectar.");
            }
        }
 
 
        #endregion
    }
}