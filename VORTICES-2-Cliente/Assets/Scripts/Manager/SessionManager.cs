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
        public bool   isOnlineSession  = false;
        public string launcherServerIp = "";   // IP del servidor seteada por el Launcher (online)
        // Sala Environment settings
        public int configLevel = 4;
        public int minRooms    = 1;
        public int maxRooms    = 50;
        public int roomSeed    = 0; // Semilla generada por el examinador, distribuida a todos los clients vía SessionData
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
        // Voz por proximidad: true = canal posicional Vivox (volumen según distancia), false = global
        public bool proximityVoice = false;
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
        // Flag: el servidor confirmó la creación de sesión del examinador (success=true)
        private bool sessionCreatedOnServer = false;
 
 
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
        // Referencias a las coroutines del flujo Join (para poder cancelarlas en reintentos)
        private Coroutine joinConnectionCoroutine;
        private Coroutine joinSceneLoadCoroutine;
        // Canal de voz activo (varía si es normal o posicional)
        private string activeVoiceChannelName = null;
        // Coroutine que actualiza posición en canal posicional Vivox
        private Coroutine positionUpdateCoroutine;
 
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
                Debug.LogWarning("[SessionManager] HandleSessionCreatedMessage: success=false — la sesión ya existe o el nombre de ambiente es desconocido. Cambia el nombre de sesión e intenta de nuevo.");
                return;
            }

            sessionCreatedOnServer = true;
            Debug.Log("[SessionManager] HandleSessionCreatedMessage: sesión confirmada por el servidor.");

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
            audioPaths = msg.audioPaths ?? new List<string>();
            minRooms = msg.minRooms > 0 ? msg.minRooms : minRooms;
            maxRooms = msg.maxRooms > 0 ? msg.maxRooms : maxRooms;
            if (msg.configLevel > 0) configLevel = msg.configLevel;
            // Perfil acústico y emitter (el examiner ya los tiene desde AudioSpatialPanel,
            // pero los actualizamos desde el mensaje por consistencia)
            if (msg.hasAcousticOverride)
            {
                hasAcousticOverride = true;
                acousticOverride = new AudioManager.ProfileOverrideData
                {
                    spatialBlend = msg.acousticSpatialBlend,
                    spread       = msg.acousticSpread,
                    dopplerLevel = msg.acousticDopplerLevel,
                    rolloffMode  = msg.acousticRolloffMode,
                    spatialize   = msg.acousticSpatialize
                };
            }
            hasEmitterOverride    = msg.hasEmitterOverride;
            if (msg.emitterBaseVolume > 0)     emitterBaseVolume     = msg.emitterBaseVolume;
            if (msg.emitterMinConfigLevel > 0) emitterMinConfigLevel = msg.emitterMinConfigLevel;
            if (msg.emitterMinDistance > 0)    emitterMinDistance    = msg.emitterMinDistance;
            if (msg.emitterMaxDistance > 0)    emitterMaxDistance    = msg.emitterMaxDistance;

            // Tipos de objeto seleccionados por el examinador y semilla de shuffle.
            // Crítico para que todos los clientes asignen audio a los MISMOS objetos.
            if (msg.selectedObjectTypes != null && msg.selectedObjectTypes.Count > 0)
                selectedObjectTypes = msg.selectedObjectTypes;
            if (msg.roomSeed != 0)
                roomSeed = msg.roomSeed;

            // Sala no usa categorías y su flujo nunca llama Initialize() en CategoryController
            // (sessionName queda null → Path.Combine lanza ArgumentNullException → Mirror desconecta).
            // Para Museum/Circular sí se actualiza la lista normalmente.
            if (environmentName != "Sala" && categoryController != null)
                categoryController.UpdateCategoriesList(msg.categories ?? new List<string>());
        }
 
 
        #region UI Handling
 
        private void Update()
        {
            // EventSystem.current puede ser null en escenas cargadas desde bundle
            // donde el script del EventSystem no coincide con el build actual.
            if (EventSystem.current == null) return;

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

            // AÑADIDO: Detectar tecla M para mutear/desmutear micrófono
            if (Input.GetKeyDown(KeyCode.M))
            {
                ToggleMicrophone();
            }

            // AÑADIDO: Detectar Grip en Oculus Quest 2 para abrir/cerrar chat
            // (Solo compila si OVRPlugin está disponible)
#if OVRPLUGIN_ENABLED
            try
            {
                if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger) || OVRInput.GetDown(OVRInput.Button.SecondaryHandTrigger))
                {
                    NewChatManager chatManager = FindObjectOfType<NewChatManager>();
                    if (chatManager != null)
                    {
                        chatManager.ToggleChat();
                        Debug.Log("[SessionManager] AÑADIDO: Grip detectado en VR - toggling chat.");
                    }
                }
            }
            catch
            {
                // OVRInput error - silenciar
            }
#endif
        }

        private void FindAndSetNextSelectable()
        {
            GameObject canvasObject = GameObject.Find("Canvas"); // Find the GameObject named "Canvas"

            if (canvasObject == null)
            {
                // En Museum/Sala/Circular no hay un Canvas llamado "Canvas" — evitar spam de errores.
                return;
            }

            Canvas canvasComponent = canvasObject.GetComponent<Canvas>(); // Get the Canvas component from the GameObject

            if (canvasComponent == null)
            {
                // Silenciar: el Canvas puede estar mal en escenas de bundle stale.
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

        // AÑADIDO: Variable para rastrear estado de micrófono
        private bool isMicrophoneMuted = false;

        // AÑADIDO: Entornos con chat habilitado
        // Agregar/quitar entornos aquí para habilitar/deshabilitar chat
        private static readonly List<string> ENVIRONMENTS_WITH_CHAT = new List<string> { "Sala", "Sala Environment" };
        // Para habilitar chat en Museum: ENVIRONMENTS_WITH_CHAT.Add("Museum"); ENVIRONMENTS_WITH_CHAT.Add("Museum Environment");
        // Para habilitar chat en Circular: ENVIRONMENTS_WITH_CHAT.Add("Circular"); ENVIRONMENTS_WITH_CHAT.Add("Circular Environment");

        // AÑADIDO: Limpiar ChatCanvas si el entorno no tiene chat habilitado
        private void CleanupChatIfNotEnabled()
        {
            if (!ENVIRONMENTS_WITH_CHAT.Contains(environmentName))
            {
                NewChatManager chatManager = FindObjectOfType<NewChatManager>();
                if (chatManager != null)
                {
                    Debug.Log($"[SessionManager] AÑADIDO: Destruyendo ChatCanvas (entorno {environmentName} no tiene chat habilitado)");
                    Destroy(chatManager.gameObject);
                }
            }
        }

        private void ToggleMicrophone()
        {
            if (VivoxVoiceManager.Instance != null)
                VivoxVoiceManager.Instance.ToggleMute();
            isMicrophoneMuted = VivoxVoiceManager.Instance?.IsMuted ?? false;
        }

        // AÑADIDO: Inicializar Vivox para la sesión actual
        private async void InitializeVivoxForSession()
        {
            if (VivoxVoiceManager.Instance == null)
            {
                Debug.LogError("[SessionManager] AÑADIDO: VivoxVoiceManager no encontrado.");
                return;
            }

            try
            {
                // Login a Vivox con el userId
                await VivoxVoiceManager.Instance.LoginAsync(userId.ToString());

                // Unirse al canal de voz de la sesión (genérico para cualquier entorno)
                string voiceChannelName = $"{environmentName}_{sessionName}";
                await VivoxVoiceManager.Instance.JoinChannelAsync(voiceChannelName);

                Debug.Log($"[SessionManager] AÑADIDO: Vivox inicializado - Usuario: {userId}, Canal: {voiceChannelName}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SessionManager] AÑADIDO: Error inicializando Vivox - {ex.Message}");
            }
        }

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

            Debug.Log($"[LaunchSessionCoroutine] INICIO — isOnlineSession={isOnlineSession}, env='{environmentName}', NetworkClient.isConnected={NetworkClient.isConnected}");

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
                        // Usar IP del Launcher si está definida (online LAN), si no localhost
                        string targetIp = !string.IsNullOrEmpty(launcherServerIp)
                            ? launcherServerIp
                            : "127.0.0.1"; // fallback local (modo offline/debug)
                        NetworkManager.singleton.networkAddress = targetIp;
                        Debug.Log($"[SessionManager] Conectando a Mirror server en {targetIp}...");
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
 
                    // Generar semilla de shuffle/posición de audio antes de enviar al servidor.
                    // El joining client recibirá la misma semilla → mismo shuffle Y mismas posiciones
                    // de AudioTargetMarkers dinámicos (Museum/Circular usan GetRandomFloorPosition con ella).
                    roomSeed = UnityEngine.Random.Range(1, int.MaxValue);

                    SessionData sessionData = new SessionData
                    {
                        sessionName = sessionName,
                        userId = userId,
                        environmentName = environmentName,
                        elementPaths = elementPaths,
                        audioPaths = audioPaths ?? new List<string>(),
                        categories = categoryController.GetCategories(),
                        browsingMode = "Online",
                        minRooms = minRooms,
                        maxRooms = maxRooms,
                        roomSeed = roomSeed,
                        configLevel = configLevel
                    };

                    yield return StartCoroutine(SendSessionDataToServer(sessionData));

                    // Conectar a voz (original)
                    if (sessionCreatedOnServer)
                    {
                        StartCoroutine(ConnectToVoiceChatCoroutine(userId));
                    }

                    // Si el servidor no confirmó la sesión, NO cargar la escena.
                    // Esto evita que el examinador entre al entorno sin que el servidor tenga la sesión registrada.
                    if (!sessionCreatedOnServer)
                    {
                        Debug.LogError("[LaunchSessionCoroutine] El servidor no confirmó la sesión. Abortando carga de escena online. " +
                                       "Posibles causas: (1) nombre de sesión ya existe en el servidor, (2) servidor no accesible, " +
                                       "(3) nombre de ambiente no reconocido. Reinicia el servidor o usa otro nombre de sesión.");
                        sessionLaunchRunning = false;
                        // Hacer FadeIn para que el usuario no quede con pantalla negra
                        var tsErr = SceneTransitionManager.instance;
                        if (tsErr?.fadeScreen != null) tsErr.fadeScreen.FadeIn();
                        yield break;
                    }

                    // Convertir el nombre del ambiente ANTES de GoToSceneRoutine y capturarlo
                    // en una variable LOCAL para que HandleSessionCreatedMessage (que puede llegar
                    // asíncronamente del servidor) no sobreescriba el valor mientras la coroutine sigue.
                    if (environmentName == "Circular")      environmentName = "Circular Environment";
                    else if (environmentName == "Museum")   environmentName = "Museum Environment";
                    else if (environmentName == "Sala")     environmentName = "Sala Environment";
                    string targetEnvironment = environmentName; // ← variable local inmune a race condition

                    yield return StartCoroutine(actualTransitionManager.GoToSceneRoutine());

                    // Un frame para que Start() de los objetos de la escena se ejecute
                    yield return null;

                    // COMENTADO: Limpiar ChatCanvas - causa conflicto con EventSystem
                    // CleanupChatIfNotEnabled();

                    // Ready(): permite que el servidor envíe objetos spawneados (SalaNetworkHandler,
                    // blobs de otros jugadores). Como OnClientConnect ya no auto-llama Ready(),
                    // esta es la primera vez que se llama → el cliente estará not-ready aquí.
                    if (NetworkClient.isConnected && !NetworkClient.ready)
                    {
                        Debug.Log("[LaunchSessionCoroutine] Llamando NetworkClient.Ready().");
                        NetworkClient.Ready();
                    }
                    // AddPlayer(): crea el blob del examinador en la escena ya cargada (Sala Environment).
                    // Llamarlo DESPUÉS de Ready() y en la escena correcta evita el bug donde el blob
                    // se creaba en Main Menu y se destruía al cambiar de escena (Single mode).
                    yield return null; // un frame para que Ready() se procese en el servidor
                    if (NetworkClient.isConnected && NetworkClient.ready && NetworkClient.localPlayer == null)
                    {
                        Debug.Log("[LaunchSessionCoroutine] Llamando NetworkClient.AddPlayer() — creando blob del examinador en la escena actual.");
                        NetworkClient.AddPlayer();
                    }
                    Debug.Log($"[LaunchSessionCoroutine] Escena cargada. NetworkClient.isConnected={NetworkClient.isConnected}, ready={NetworkClient.ready}, localPlayer={NetworkClient.localPlayer?.name ?? "null"}");

                    // Limpiar EventSystems duplicados tras cargar el bundle de la escena
                    var allES = FindObjectsOfType<UnityEngine.EventSystems.EventSystem>();
                    if (allES.Length > 1)
                    {
                        Debug.LogWarning($"[LaunchSessionCoroutine] {allES.Length} EventSystems detectados. Eliminando duplicados.");
                        for (int i = 1; i < allES.Length; i++)
                            Destroy(allES[i].gameObject);
                    }

                    // Sala Environment online — configurar AudioManager y RoomGeometry
                    // igual que el path offline para que el examiner vea el layout correcto
                    if (targetEnvironment == "Sala Environment")
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
                            Debug.LogWarning("[SessionManager] AudioManager no encontrado en Sala Environment (online examiner).");

                        RoomGeometry roomGeometry = GameObject.FindObjectOfType<RoomGeometry>(true);
                        if (roomGeometry != null)
                        {
                            roomGeometry.SetRoomConfig(minRooms, maxRooms);
                            roomGeometry.RegenerateScenario();
                        }
                        else
                            Debug.LogWarning("[SessionManager] RoomGeometry no encontrado en Sala Environment (online examiner).");
                    }

                    yield return new WaitForSeconds(initializeTime);

                    //  AHORA BUSCAMOS TODOS LOS OBJETOS NECESARIOS
                    actualTransitionManager = GameObject.FindObjectOfType<SceneTransitionManager>(true);
                    categoryController = GameObject.FindObjectOfType<CategoryController>(true);
                    elementCategoryController = GameObject.FindObjectOfType<ElementCategoryController>(true);
                    loggingController = GameObject.FindObjectOfType<LoggingController>(true);
                    spawnController = GameObject.FindObjectOfType<SpawnController>(true);
                    righthandTools = GameObject.FindObjectOfType<RighthandTools>(true);

                    // Null-check: Sala Environment no tiene ElementCategoryController/LoggingController/SpawnController
                    if (elementCategoryController != null) elementCategoryController.Initialize();
                    else Debug.LogWarning("[SessionManager] elementCategoryController no encontrado (normal en Sala Environment).");
                    if (loggingController != null) loggingController.Initialize();
                    else Debug.LogWarning("[SessionManager] loggingController no encontrado (normal en Sala Environment).");
                    if (spawnController != null) spawnController.Initialize();
                    else Debug.LogWarning("[SessionManager] spawnController no encontrado (normal en Sala Environment).");
                    inputController.RestartInputs();

                    // Sala online: asignar audio a los markers.
                    // - Launcher: audioPaths contiene URLs http:// (AudioHttpServer)
                    // - VR menú:  SalaPanel setea elementPaths (rutas locales), audioPaths queda vacío
                    //   → usar elementPaths como fallback igual que hace el path offline.
                    if (targetEnvironment == "Sala Environment")
                    {
                        List<string> salaAudio = (audioPaths != null && audioPaths.Count > 0)
                            ? audioPaths
                            : elementPaths;
                        if (salaAudio != null && salaAudio.Count > 0)
                            yield return StartCoroutine(AssignUserAudioCoroutine(salaAudio, roomSeed));
                        else
                            Debug.LogWarning("[SessionManager] Sin rutas de audio para Sala (online). Verifica que hayas cargado archivos.");
                    }

                    // Museum / Circular online: configurar AudioManager + asignar audio de Options.
                    // Replica el bloque equivalente del path offline.
                    else if (targetEnvironment == "Museum Environment" || targetEnvironment == "Circular Environment")
                    {
                        AudioManager audioManager = GameObject.FindObjectOfType<AudioManager>(true);
                        if (audioManager != null)
                        {
                            audioManager.SetConfigLevel(configLevel);
                            if (hasAcousticOverride)
                                audioManager.ApplyProfileOverride(configLevel, acousticOverride);
                        }
                        else
                            Debug.LogWarning($"[SessionManager] AudioManager no encontrado en {targetEnvironment} (online).");

                        if (audioPaths != null && audioPaths.Count > 0)
                            yield return StartCoroutine(AssignUserAudioCoroutine(audioPaths, roomSeed));
                        else
                            Debug.LogWarning($"[SessionManager] audioPaths vacío en {targetEnvironment} (online). Carga audios desde Options.");
                    }

                    sessionLaunchRunning = false;

                    // Conectar a voz (original)
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
                Debug.LogError("[SessionManager] SendSessionDataToServer: cliente NO conectado al servidor. Verifica que el servidor esté corriendo y la IP sea correcta.");
                yield break;
            }

            sessionCreatedOnServer = false; // reset antes de enviar
            Debug.Log($"[SessionManager] SendSessionDataToServer: enviando CreateSessionMessage (session='{sessionName}', env='{environmentName}', isOnline={isOnlineSession})...");
            // Para Sala online: SalaPanel setea elementPaths (rutas locales), audioPaths queda vacío.
            // Usar elementPaths como fallback SOLO para Sala.
            // Para Museum/Circular: audioPaths vacío = sin audio (válido). NO usar elementPaths como
            // fallback porque son image URLs, no rutas de audio → el joining client intentaría
            // cargarlas como AudioClip y bloquearía JoinSessionRoutine sin motivo.
            bool _isSalaEnv = environmentName == "Sala" || environmentName == "Sala Environment";
            List<string> audioPathsToSend = (audioPaths != null && audioPaths.Count > 0)
                ? audioPaths
                : (_isSalaEnv ? (elementPaths ?? new List<string>()) : new List<string>());

            NetworkClient.Send(new CreateSessionMessage
            {
                sessionName = sessionName,
                userId = userId,
                environmentName = environmentName,
                isOnlineSession = isOnlineSession,
                displayMode = displayMode,
                browsingMode = "Online",   // siempre "Online" en el path online — SalaPanel setea "Local" pero eso es solo para el modo offline
                volumetric = volumetric,
                dimension = dimension,
                categories = categoryController.GetCategories(),
                elementPaths = elementPaths,
                audioPaths = audioPathsToSend,
                minRooms = minRooms,
                maxRooms = maxRooms,
                roomSeed = roomSeed,
                configLevel           = configLevel,
                hasAcousticOverride   = hasAcousticOverride,
                acousticSpatialBlend  = acousticOverride.spatialBlend,
                acousticSpread        = acousticOverride.spread,
                acousticDopplerLevel  = acousticOverride.dopplerLevel,
                acousticRolloffMode   = acousticOverride.rolloffMode,
                acousticSpatialize    = acousticOverride.spatialize,
                hasEmitterOverride    = hasEmitterOverride,
                emitterBaseVolume     = emitterBaseVolume,
                emitterMinConfigLevel = emitterMinConfigLevel,
                emitterMinDistance    = emitterMinDistance,
                emitterMaxDistance    = emitterMaxDistance,
                selectedObjectTypes   = selectedObjectTypes ?? new List<string>(),
                selectedDirections    = selectedDirections  ?? new List<string>()
            });

            // Esperar hasta 5 s a que el servidor confirme la sesión (HandleSessionCreatedMessage pone sessionCreatedOnServer = true)
            float waitConfirm = 5f;
            while (!sessionCreatedOnServer && waitConfirm > 0f)
            {
                waitConfirm -= Time.deltaTime;
                yield return null;
            }

            if (!sessionCreatedOnServer)
                Debug.LogError("[SessionManager] SendSessionDataToServer: el servidor NO confirmó la sesión en 5 s. Puede que el nombre ya exista, la conexión se perdió, o el servidor no reconoció el mensaje.");
            else
                Debug.Log("[SessionManager] SendSessionDataToServer: servidor confirmó la sesión.");
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
            sessionDataReceived = false;      // Reset para que JoinSession pueda volver a esperar datos frescos
            sessionCreatedOnServer = false;   // Reset para que LaunchSession pueda volver a esperar confirmación del servidor
            proximityVoice = false;
            activeVoiceChannelName = null;
            // La positionUpdateCoroutine ya fue detenida en DisconnectFromVivoxCoroutine
            positionUpdateCoroutine = null;

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
                // Cancelar coroutines de un intento previo que todavía estén corriendo
                if (joinConnectionCoroutine != null) { StopCoroutine(joinConnectionCoroutine); joinConnectionCoroutine = null; }
                if (joinSceneLoadCoroutine  != null) { StopCoroutine(joinSceneLoadCoroutine);  joinSceneLoadCoroutine  = null; }

                // Resetear flag para que WaitForSessionDataAndLoadScene espere datos frescos
                sessionDataReceived = false;
                isOnlineSession     = true;

                // Si el cliente Mirror ya está activo (reintento sin reiniciar la app) no relanzarlo.
                // "Client already started" silencia la solicitud y deja la conexión previa activa.
                if (!NetworkClient.active)
                {
                    NetworkManager.singleton.networkAddress = ipAddress;
                    NetworkManager.singleton.StartClient();
                    Debug.Log($"[SessionManager] JoinSession: StartClient en {ipAddress}");
                }
                else
                {
                    Debug.Log("[SessionManager] JoinSession: cliente Mirror ya activo — reutilizando conexión existente.");
                }

                joinConnectionCoroutine = StartCoroutine(WaitForConnectionAndJoinSession());
                joinSceneLoadCoroutine  = StartCoroutine(WaitForSessionDataAndLoadScene());
            }
            else
            {
                Debug.LogWarning("La dirección IP está vacía.");
            }
        }
 
 
        private IEnumerator JoinSessionRoutine()
        {
            Debug.Log("[DEBUG] Iniciando carga de escena para sesión online...");

            // Asegurar que actualTransitionManager esté disponible antes de usarlo
            // (JoinSession no pasa por LaunchSessionCoroutine, hay que buscarlo aquí)
            if (actualTransitionManager == null)
                actualTransitionManager = GameObject.Find("TransitionManager")?.GetComponent<SceneTransitionManager>();

            if (actualTransitionManager == null)
            {
                Debug.LogError("[SessionManager] TransitionManager no encontrado en JoinSessionRoutine. Abortando.");
                yield break;
            }

            // HandleActiveSessionResponse setea environmentName en forma corta ("Sala", "Museum", "Circular").
            // Normalizar a nombre completo para que los checks de abajo funcionen.
            if (environmentName == "Sala")           environmentName = "Sala Environment";
            else if (environmentName == "Museum")    environmentName = "Museum Environment";
            else if (environmentName == "Circular")  environmentName = "Circular Environment";

            // Cambiar a la escena correspondiente
            yield return StartCoroutine(actualTransitionManager.GoToSceneRoutine());

            // Un frame para que Start() de los objetos de la escena se ejecute
            yield return null;

            // COMENTADO: Limpiar ChatCanvas - causa conflicto con EventSystem
            // CleanupChatIfNotEnabled();

            // La escena del bundle puede traer su propio EventSystem → quedan 2 → input se congela.
            // Eliminar duplicados ahora que la escena ya cargó.
            var allEventSystems = FindObjectsOfType<UnityEngine.EventSystems.EventSystem>();
            if (allEventSystems.Length > 1)
            {
                Debug.LogWarning($"[JoinSessionRoutine] {allEventSystems.Length} EventSystems detectados tras cargar escena. Eliminando duplicados.");
                for (int i = 1; i < allEventSystems.Length; i++)
                    Destroy(allEventSystems[i].gameObject);
            }

            // Revelar la escena: si el FadeScreen es hijo del SceneTransitionManager (DontDestroyOnLoad)
            // persiste entre escenas y quedó negro tras GoToSceneRoutine→FadeOut. Llamar FadeIn aquí.
            // Si fue destruido al cambiar de escena, el FadeScreen de la nueva escena ya llamó FadeIn
            // desde su propio Start() — en ese caso fadeScreen es null y no hacemos nada.
            {
                var ts = SceneTransitionManager.instance;
                if (ts != null && ts.fadeScreen != null)
                {
                    Debug.Log("[JoinSessionRoutine] FadeScreen persistente detectado → llamando FadeIn para revelar la escena.");
                    ts.fadeScreen.FadeIn();
                }
            }

            // Señalizar a Mirror que el cliente está listo en la nueva escena.
            // LoadSceneMode.Single destruye los objetos spawneados localmente; Ready() hace que el servidor
            // reenvíe los SpawnMessages para que el cliente los recree en la nueva escena.
            // IMPORTANTE: si el bundle de escena tardó >60 s en cargarse, la conexión KCP puede haberse
            // cortado por timeout. En ese caso isConnected es false y el player prefab nunca se re-spawneará
            // → sin cámara → pantalla negra. La solución principal es la carga ASYNC del bundle (async fix
            // en GoToSceneRoutine). Este bloque loguea el estado para facilitar diagnóstico.
            if (NetworkClient.isConnected)
            {
                Debug.Log("[JoinSessionRoutine] Llamando NetworkClient.Ready() para resincronizar objetos Mirror.");
                NetworkClient.Ready();

                // AddPlayer(): crea el blob del participante en la escena ya cargada.
                // Como OnClientConnect ya no auto-llama AddPlayer(), hay que pedirlo aquí.
                yield return null; // un frame para que el servidor procese Ready()
                if (NetworkClient.ready && NetworkClient.localPlayer == null)
                {
                    Debug.Log("[JoinSessionRoutine] Llamando NetworkClient.AddPlayer() — creando blob del participante.");
                    NetworkClient.AddPlayer();
                }
            }
            else
            {
                Debug.LogError("[JoinSessionRoutine] La conexión Mirror se perdió durante la carga de escena (probable timeout KCP). " +
                               "El player prefab no será spawneado → la escena puede verse en negro. " +
                               "Revisa que la carga del bundle de escena sea async y que el tiempo total sea <60 s.");
                // Revelar la escena de todas formas (puede estar negra pero al menos el usuario ve algo)
                var ts2 = SceneTransitionManager.instance;
                if (ts2?.fadeScreen != null) ts2.fadeScreen.FadeIn();
                sessionLaunchRunning = false;
                yield break;
            }

            // Sala Environment — el joining client genera el mismo layout que el examiner
            // (RoomGeometry es determinista; mismo minRooms = mismo layout)
            if (environmentName == "Sala Environment")
            {
                AudioManager audioManager = GameObject.FindObjectOfType<AudioManager>(true);
                if (audioManager != null)
                {
                    audioManager.SetConfigLevel(configLevel);
                    if (hasAcousticOverride)
                        audioManager.ApplyProfileOverride(configLevel, acousticOverride);
                }
                else
                    Debug.LogWarning("[SessionManager] AudioManager no encontrado en Sala Environment (joining client).");

                RoomGeometry roomGeometry = GameObject.FindObjectOfType<RoomGeometry>(true);
                if (roomGeometry != null)
                {
                    roomGeometry.SetRoomConfig(minRooms, maxRooms);
                    roomGeometry.RegenerateScenario();
                    Debug.Log($"[SessionManager] Layout Sala generado para joining client: {minRooms} salas.");
                }
                else
                    Debug.LogWarning("[SessionManager] RoomGeometry no encontrado en Sala Environment (joining client).");
            }

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

            // Null-check: Sala Environment no tiene ElementCategoryController/LoggingController/SpawnController
            if (elementCategoryController != null) elementCategoryController.Initialize();
            else Debug.LogWarning("[SessionManager] elementCategoryController no encontrado (normal en Sala Environment).");
            if (loggingController != null) loggingController.Initialize();
            else Debug.LogWarning("[SessionManager] loggingController no encontrado (normal en Sala Environment).");
            if (spawnController != null) spawnController.Initialize();
            else Debug.LogWarning("[SessionManager] spawnController no encontrado (normal en Sala Environment).");
            inputController.RestartInputs();

            // Sala online: asignar audio a los markers con la misma semilla que el examinador
            // (roomSeed se recibió en HandleActiveSessionResponse → mismo shuffle → misma asignación)
            if (environmentName == "Sala Environment" && audioPaths != null && audioPaths.Count > 0)
                yield return StartCoroutine(AssignUserAudioCoroutine(audioPaths, roomSeed));

            // Museum / Circular online: el joining client también debe escuchar el audio espacial.
            // audioPaths fue recibido del servidor via HandleActiveSessionResponse (mismo valor que el examiner).
            else if ((environmentName == "Museum Environment" || environmentName == "Circular Environment")
                     && audioPaths != null && audioPaths.Count > 0)
            {
                AudioManager audioManager = GameObject.FindObjectOfType<AudioManager>(true);
                if (audioManager != null)
                {
                    audioManager.SetConfigLevel(configLevel);
                    if (hasAcousticOverride)
                        audioManager.ApplyProfileOverride(configLevel, acousticOverride);
                }
                else
                    Debug.LogWarning($"[SessionManager] AudioManager no encontrado en {environmentName} (joining client).");

                yield return StartCoroutine(AssignUserAudioCoroutine(audioPaths, roomSeed));
            }
            else if (audioPaths == null || audioPaths.Count == 0)
                Debug.LogWarning($"[SessionManager] audioPaths vacío para joining client en {environmentName}. No se asigna audio.");

            sessionLaunchRunning = false;

            // Conectar al canal de voz (original)
            Debug.Log("[DEBUG] Conectando al canal de voz...");
            StartCoroutine(ConnectToVoiceChatCoroutine(userId));
        }


        private IEnumerator WaitForConnectionAndJoinSession()
        {
            // Esperar conexión (hasta 10 s)
            float connTimeout = 10f;
            float elapsed = 0f;
            while (!NetworkClient.isConnected && elapsed < connTimeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (!NetworkClient.isConnected)
            {
                Debug.LogError("[SessionManager] WaitForConnectionAndJoinSession: no se pudo conectar al servidor en 10 s.");
                yield break;
            }

            // Reintentar RequestActiveSessionMessage cada 2 s.
            // 150 reintentos × 2 s = 300 s (5 min): el examinador puede tardar en configurar
            // los steps de Sala (selección de salas, archivos de audio, perfiles, etc.)
            // antes de lanzar la sesión.
            int maxRetries = 150;
            int retryCount = 0;
            while (!sessionDataReceived && NetworkClient.isConnected && retryCount < maxRetries)
            {
                // Loguear solo cada 5 reintentos (cada 10 s) para no saturar el log
                if (retryCount % 5 == 0)
                    Debug.Log($"[SessionManager] RequestActiveSessionMessage — intento {retryCount + 1}/{maxRetries} (esperando sesión activa)...");
                NetworkClient.Send(new RequestActiveSessionMessage());
                retryCount++;
                yield return new WaitForSeconds(2f);
            }

            if (!sessionDataReceived)
                Debug.LogWarning("[SessionManager] WaitForConnectionAndJoinSession: agotados los reintentos sin recibir datos de sesión.");

            joinConnectionCoroutine = null;
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

            if (msg.sessionData.audioPaths == null)
            {
                msg.sessionData.audioPaths = new List<string>();
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
            // Null-check: el joining client puede no tener categoryController asignado aún.
            // Sin él la excepción era silenciosa y sessionDataReceived nunca se seteaba → pantalla negra.
            if (categoryController != null)
                categoryController.Initialize();
            else
                Debug.LogWarning("[SessionManager] HandleActiveSessionResponse: categoryController es null, se omite Initialize().");
            //  Asignamos los `elementPaths` al SessionManager
            elementPaths = new List<string>(msg.sessionData.elementPaths);
            //  Asignamos los `audioPaths` (pueden ser URLs http:// en modo online)
            audioPaths = new List<string>(msg.sessionData.audioPaths);
            //  Sala: sincronizar layout (RoomGeometry es determinista con el mismo minRooms)
            if (msg.sessionData.minRooms > 0) minRooms = msg.sessionData.minRooms;
            if (msg.sessionData.maxRooms > 0) maxRooms = msg.sessionData.maxRooms;
            if (msg.sessionData.roomSeed  > 0) roomSeed = msg.sessionData.roomSeed;
            // Tipos de objeto seleccionados por el examinador (vacío = todos elegibles).
            // Crítico: AssignUserAudioCoroutine usa este valor para filtrar markers;
            // si no se lee aquí el participante asigna audio a objetos distintos al examinador.
            if (msg.sessionData.selectedObjectTypes != null && msg.sessionData.selectedObjectTypes.Count > 0)
                selectedObjectTypes = msg.sessionData.selectedObjectTypes;
            if (msg.sessionData.selectedDirections != null && msg.sessionData.selectedDirections.Count > 0)
                selectedDirections = msg.sessionData.selectedDirections;
            //  Audio: sincronizar toda la configuración del examinador
            if (msg.sessionData.configLevel > 0) configLevel = msg.sessionData.configLevel;
            if (msg.sessionData.hasAcousticOverride)
            {
                hasAcousticOverride = true;
                acousticOverride = new AudioManager.ProfileOverrideData
                {
                    spatialBlend = msg.sessionData.acousticSpatialBlend,
                    spread       = msg.sessionData.acousticSpread,
                    dopplerLevel = msg.sessionData.acousticDopplerLevel,
                    rolloffMode  = msg.sessionData.acousticRolloffMode,
                    spatialize   = msg.sessionData.acousticSpatialize
                };
            }
            hasEmitterOverride    = msg.sessionData.hasEmitterOverride;
            if (msg.sessionData.emitterBaseVolume > 0)     emitterBaseVolume     = msg.sessionData.emitterBaseVolume;
            if (msg.sessionData.emitterMinConfigLevel > 0) emitterMinConfigLevel = msg.sessionData.emitterMinConfigLevel;
            if (msg.sessionData.emitterMinDistance > 0)    emitterMinDistance    = msg.sessionData.emitterMinDistance;
            if (msg.sessionData.emitterMaxDistance > 0)    emitterMaxDistance    = msg.sessionData.emitterMaxDistance;

            //  Actualizamos el AddonsController con el environment correcto
            AddonsController addonsController = AddonsController.instance;
            if (addonsController != null)
            {
                // El participante que se une puede no haber pasado por Options,
                // por lo que environmentObjects puede estar vacío. Cargarlo ahora si es necesario.
                if (addonsController.environmentObjects == null || addonsController.environmentObjects.Count == 0)
                {
                    Debug.Log("[SessionManager] environmentObjects vacío — cargando addons automáticamente para joining client.");
                    addonsController.LoadAddonObjects();
                }

                bool envFound = false;
                foreach (var envObject in addonsController.environmentObjects)
                {
                    if (envObject.environmentName == msg.sessionData.environmentName)
                    {
                        addonsController.currentEnvironmentObject = envObject;
                        envFound = true;
                        break;
                    }
                }

                // Si el addon no se encontró (p.ej. estaba disabled=false en addons.json para el
                // joining client que nunca pasó por Options), intentar habilitarlo y recargar.
                if (!envFound && addonsController.allAddonsData != null)
                {
                    foreach (Addon addon in addonsController.allAddonsData)
                    {
                        if (addon.addonType == "Environment" && addon.addonName == msg.sessionData.environmentName)
                        {
                            Debug.Log($"[SessionManager] Addon '{addon.addonName}' estaba desactivado. Habilitando para joining client...");
                            addon.enabled = true;
                            break;
                        }
                    }
                    addonsController.LoadAddonObjects();
                    foreach (var envObject in addonsController.environmentObjects)
                    {
                        if (envObject.environmentName == msg.sessionData.environmentName)
                        {
                            addonsController.currentEnvironmentObject = envObject;
                            envFound = true;
                            break;
                        }
                    }
                }

                if (!envFound)
                    Debug.LogError($"[SessionManager] El entorno '{msg.sessionData.environmentName}' no está disponible en este cliente. " +
                                   "Verifica que los bundles estén en la carpeta Addons/Environment.");
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
            // 300 s (5 min): el examinador puede tardar en configurar los steps de Sala
            // (selección de salas, archivos de audio, perfiles acústicos, etc.) antes de lanzar.
            // WaitForConnectionAndJoinSession reintenta RequestActiveSessionMessage cada 2 s mientras tanto.
            float timeout = 300f;
            while (!sessionDataReceived && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            if (!sessionDataReceived)
            {
                Debug.LogError("[SessionManager] Timeout (300 s / 5 min) esperando datos de la sesión activa. " +
                               "Verifica que el examinador haya creado la sesión y esté conectado al servidor.");
                // Revelar la escena actual para que el usuario no quede en pantalla negra.
                var tsF = SceneTransitionManager.instance;
                if (tsF?.fadeScreen != null) tsF.fadeScreen.FadeIn();
                joinSceneLoadCoroutine = null;
                yield break;
            }

            // Limpiar referencia antes de cargar la escena
            joinSceneLoadCoroutine = null;
            StartCoroutine(JoinSessionRoutine());
        }
 
 
 
 
        /// <param name="paths">Rutas de audio a asignar. Si null, usa elementPaths.</param>
        /// <param name="shuffleSeed">
        /// Semilla para el shuffle determinista (System.Random, no afecta UnityEngine.Random).
        /// -1 = aleatorio (modo offline o Museum/Circular donde no hay cross-client sync).
        /// En Sala online el examinador genera la semilla y la distribuye a todos los clients.
        /// </param>
        private IEnumerator AssignUserAudioCoroutine(List<string> paths = null, int shuffleSeed = -1)
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

            // Codigo para debug: listar todos los markers encontrados
            // Crear RNG determinista aquí — se usa tanto para posiciones de markers dinámicos
            // como para el shuffle posterior. shuffleSeed >= 0 → sesión online con semilla del examinador.
            // Esto garantiza que examinador y participantes generen los MISMOS markers en las MISMAS posiciones.
            System.Random rng = shuffleSeed >= 0
                ? new System.Random(shuffleSeed)
                : new System.Random();

            // Genera AudioTargetMarkers dinámicos para Museum/Circular si hay menos markers que audios.
            // Requiere un BoxCollider con tag "MuseumBounds" en la escena que defina el área válida.
            if ((environmentName == "Museum" || environmentName == "Museum Environment" ||
                 environmentName == "Circular" || environmentName == "Circular Environment") && markers.Count < paths.Count)
            {
                BoxCollider bounds = GameObject.FindGameObjectWithTag("MuseumBounds")?.GetComponent<BoxCollider>();
                if (bounds == null)
                    Debug.LogWarning("[SessionManager] Museum dynamic markers: no se encontró BoxCollider con tag 'MuseumBounds'.");

                int toGenerate = paths.Count - markers.Count;
                for (int i = 0; i < toGenerate; i++)
                {
                    // Usar rng sembrado para que todos los clientes generen las mismas posiciones
                    Vector3 pos = GetRandomFloorPosition(bounds, rng);
                    GameObject go = new GameObject($"DynamicAudioMarker_{i}");
                    AudioTargetMarker newMarker = go.AddComponent<AudioTargetMarker>();
                    go.transform.position = pos;
                    markers.Add(newMarker);
                }
                Debug.Log($"[SessionManager] Museum: generados {toGenerate} AudioTargetMarker dinámicos (total: {markers.Count}).");
            }

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

            // Shuffle Fisher-Yates con el mismo rng ya creado arriba (determinista si shuffleSeed >= 0).

            for (int i = markers.Count - 1; i > 0; i--)
            {
                int j = rng.Next(0, i + 1);
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

        // Nuevo metodo auxiliar para obtener una posición aleatoria válida en el piso dentro de unos límites definidos por un BoxCollider.
        private Vector3 GetRandomFloorPosition(BoxCollider bounds, System.Random rng = null)
        {
            if (bounds == null) return Vector3.zero;
            Bounds b = bounds.bounds;
            float x, z;
            if (rng != null)
            {
                // RNG sembrado → posición determinista → misma en todos los clientes
                x = (float)(rng.NextDouble() * (b.max.x - b.min.x) + b.min.x);
                z = (float)(rng.NextDouble() * (b.max.z - b.min.z) + b.min.z);
            }
            else
            {
                x = UnityEngine.Random.Range(b.min.x, b.max.x);
                z = UnityEngine.Random.Range(b.min.z, b.max.z);
            }
            // Usar b.min.y como piso del área — evita raycast que golpea el techo del BoxCollider
            return new Vector3(x, b.min.y + 0.1f, z);
        }

        private IEnumerator LoadAudioClipCoroutine(string path, System.Action<AudioClip> onLoaded)
        {
            // Soporta rutas locales (file:///) Y URLs HTTP servidas por el Launcher online
            string uri = (path.StartsWith("http://") || path.StartsWith("https://"))
                ? path
                : "file:///" + path.Replace("\\", "/");
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
            // Guard: si Vivox no está inicializado, salir sin crashear
            if (VivoxVoiceManager.Instance == null)
            {
                Debug.LogWarning("[VoiceChat] VivoxVoiceManager.Instance es NULL — Vivox no disponible, saltando conexión de voz.");
                yield break;
            }

            bool loginSuccess = false;
            bool loginDone    = false;

            // Intentar iniciar sesión en Vivox (con try-catch: LoginAsync puede lanzar internamente
            // si Unity Services no está inicializado, aunque Instance no sea null)
            try
            {
                VivoxVoiceManager.Instance.LoginAsync(userId.ToString()).ContinueWith(task =>
                {
                    loginSuccess = task.IsCompletedSuccessfully;
                    if (!loginSuccess)
                        Debug.LogWarning($"[VoiceChat] Login en Vivox falló: {task.Exception?.GetBaseException()?.Message}");
                    loginDone = true;
                });
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VoiceChat] LoginAsync lanzó excepción: {ex.Message}. Saltando conexión de voz.");
                yield break;
            }

            // Esperar login con timeout de 10 s (evita WaitUntil infinito si el task nunca termina)
            float timeout = 10f;
            while (!loginDone && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            if (!loginSuccess)
            {
                Debug.LogWarning("[VoiceChat] No se pudo hacer login en Vivox (timeout o error). Saltando conexión de voz.");
                yield break;
            }

            // Elegir canal según modo de voz
            // - Normal (global):     canal de grupo estándar, todos se escuchan igual
            // - Proximidad (3D):     canal posicional, volumen según distancia al hablante
            bool channelJoinSuccess = false;
            bool channelJoinDone    = false;

            if (proximityVoice)
            {
                // Canal posicional — nombre diferente para no mezclar usuarios en modos distintos
                activeVoiceChannelName = "VoRTIcESVoiceChat_Positional";
                Debug.Log($"[VoiceChat] Modo proximidad activo → uniendo a canal posicional '{activeVoiceChannelName}'.");

                try
                {
                    VivoxVoiceManager.Instance.JoinPositionalChannelAsync(activeVoiceChannelName).ContinueWith(task =>
                    {
                        channelJoinSuccess = task.IsCompletedSuccessfully;
                        if (!channelJoinSuccess)
                            Debug.LogWarning($"[VoiceChat] Error al unirse al canal posicional Vivox: {task.Exception?.GetBaseException()?.Message}");
                        channelJoinDone = true;
                    });
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[VoiceChat] JoinPositionalChannelAsync lanzó excepción: {ex.Message}. Sin canal de voz.");
                    yield break;
                }
            }
            else
            {
                // Canal de grupo estándar (comportamiento original)
                activeVoiceChannelName = "VoRTIcESVoiceChat";
                Debug.Log($"[VoiceChat] Modo global → uniendo a canal de grupo '{activeVoiceChannelName}'.");

                try
                {
                    VivoxVoiceManager.Instance.JoinChannelAsync(activeVoiceChannelName).ContinueWith(task =>
                    {
                        channelJoinSuccess = task.IsCompletedSuccessfully;
                        if (!channelJoinSuccess)
                            Debug.LogWarning($"[VoiceChat] Error al unirse al canal Vivox: {task.Exception?.GetBaseException()?.Message}");
                        channelJoinDone = true;
                    });
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[VoiceChat] JoinChannelAsync lanzó excepción: {ex.Message}. Sin canal de voz.");
                    yield break;
                }
            }

            // Esperar unión al canal con timeout de 10 s
            timeout = 10f;
            while (!channelJoinDone && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            if (!channelJoinSuccess)
            {
                Debug.LogWarning("[VoiceChat] No se pudo unir al canal Vivox (timeout o error).");
            }
            else if (proximityVoice)
            {
                // Iniciar la coroutine que envía la posición del jugador a Vivox cada 0.1 s
                if (positionUpdateCoroutine != null) StopCoroutine(positionUpdateCoroutine);
                positionUpdateCoroutine = StartCoroutine(UpdateVivoxPositionCoroutine(activeVoiceChannelName));
                Debug.Log("[VoiceChat] Canal posicional unido. Actualizador de posición 3D iniciado.");

                // QUITAR EN CASO DE VOLVER A LA VERSION ANTERIOR
                var occlusionGO = new GameObject("VivoxOcclusionManager");
                DontDestroyOnLoad(occlusionGO);
                var occlusion = occlusionGO.AddComponent<VivoxOcclusionManager>();
                occlusion.Init(activeVoiceChannelName);
            }
        }

        /// <summary>
        /// Envía la posición y orientación de la cámara principal a Vivox cada 0.1 s.
        /// Vivox usa estos datos para atenuar el volumen de los otros usuarios según distancia.
        /// Solo activo cuando proximityVoice = true.
        /// </summary>
        private IEnumerator UpdateVivoxPositionCoroutine(string channelName)
        {
            var wait = new WaitForSeconds(0.1f);
            while (true)
            {
                yield return wait;

                if (VivoxVoiceManager.Instance == null) yield break;

                Camera cam = Camera.main;
                if (cam == null) continue; // La cámara puede no estar lista todavía

                Vector3 pos = cam.transform.position;
                Vector3 fwd = cam.transform.forward;
                Vector3 up  = cam.transform.up;

                // speakerPos == listenerPos: la "boca" y los "oídos" están en la misma posición (la cabeza del jugador)
                VivoxVoiceManager.Instance.UpdatePosition3D(channelName, pos, pos, fwd, up);
            }
        }
 
        private IEnumerator DisconnectFromVivoxCoroutine()
        {
            Debug.Log("[VoiceChat] Iniciando desconexión de Vivox...");

            // Detener actualizador de posición si estaba activo (canal posicional)
            if (positionUpdateCoroutine != null)
            {
                StopCoroutine(positionUpdateCoroutine);
                positionUpdateCoroutine = null;
                Debug.Log("[VoiceChat] Actualizador de posición 3D detenido.");
            }

            if (VivoxVoiceManager.Instance != null)
            {
                // Salir del canal activo (normal o posicional según el modo usado)
                string channelToLeave = !string.IsNullOrEmpty(activeVoiceChannelName)
                    ? activeVoiceChannelName
                    : VivoxVoiceManager.LobbyChannelName;

                if (channelToLeave != null)
                {
                    Task leaveChannelTask = VivoxVoiceManager.Instance.LeaveChannelAsync(channelToLeave);
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