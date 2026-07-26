using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Mirror;
using Vortices;

public class ServerSessionManager : NetworkBehaviour
{
    // Diccionario para almacenar las sesiones activas
    private Dictionary<string, SessionData> activeSessions = new Dictionary<string, SessionData>();

    private static ServerSessionManager _instance;

    [SerializeField]
    public GameObject museumBaseNetworkPrefab;

    [SerializeField]
    public GameObject CircularNetworkPrefab;

    [SerializeField]
    public GameObject SalaNetworkHandlerPrefab;

    // AÑADIDO: Prefab del ChatCanvas para spawnearlo en sesiones Sala online
    [SerializeField]
    public GameObject ChatCanvasPrefab;

    public static ServerSessionManager Instance
    {
        get
        {
            if (_instance == null)
            {
                Debug.LogError("ServerSessionManager no est� en la escena.");
            }
            return _instance;
        }
    }

    private void Awake()
    {   
        if (_instance != null)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

    }

    private void Start()
    {
        // Iniciar la rutina para monitorear conexiones activas
        StartCoroutine(MonitorClientConnections());
    }

    // Contador de ciclos consecutivos sin clientes autenticados.
    // Grace period amplio (5 min) para cubrir:
    //   - Carga de bundle de escena (puede tardar 10-60 s)
    //   - KCP timeout transitorio durante el cambio de escena
    //   - El joining client tarda en abrir y conectar
    private int noAuthClientCycles = 0;
    private const int NoAuthCyclesBeforeClear = 60; // 60 × 5 s = 5 min de gracia

    private IEnumerator MonitorClientConnections()
    {
        while (true)
        {
            yield return new WaitForSeconds(5f); // Revisa cada 5 segundos

            bool hayClientesAutenticados = NetworkServer.connections.Count > 0
                && NetworkServer.connections.Any(c => c.Value.isAuthenticated);

            if (!hayClientesAutenticados)
            {
                noAuthClientCycles++;
                // Solo loguear cada 12 ciclos (1 min) para no saturar el log
                if (noAuthClientCycles % 12 == 1 || noAuthClientCycles >= NoAuthCyclesBeforeClear)
                    Debug.Log($"[Servidor] MonitorClientConnections: sin clientes autenticados ({noAuthClientCycles}/{NoAuthCyclesBeforeClear}). Sesiones activas: {activeSessions.Count}");
                if (noAuthClientCycles >= NoAuthCyclesBeforeClear)
                {
                    noAuthClientCycles = 0;
                    HandleAllClientsDisconnected();
                }
            }
            else
            {
                // Hay al menos un cliente autenticado → resetear contador
                if (noAuthClientCycles > 0)
                    Debug.Log($"[Servidor] MonitorClientConnections: cliente autenticado (re)conectado. Reset contador (estaba en {noAuthClientCycles}).");
                noAuthClientCycles = 0;
            }
        }
    }


    public override void OnStartServer()
    {
        base.OnStartServer();
        Debug.Log("Servidor iniciado y listo para recibir conexiones.");

        // Registrar handlers
        NetworkServer.RegisterHandler<CreateSessionMessage>(HandleCreateSessionMessage);
        NetworkServer.RegisterHandler<RequestActiveSessionMessage>(HandleRequestActiveSessionMessage);
        NetworkServer.RegisterHandler<SonidosSyncMessage>(HandleSonidosSync);

    }


    #region Sincronización SonidosPanel

    private void HandleSonidosSync(NetworkConnectionToClient conn, SonidosSyncMessage msg)
    {
        // Retransmitir a todos los clientes EXCEPTO el emisor.
        // El emisor ya ejecutó la acción localmente (SonidosPanel.OnEmitirClicked).
        // Si también recibiera el relay de vuelta escucharía el audio dos veces.
        int relayed = 0;
        foreach (NetworkConnection c in NetworkServer.connections.Values)
        {
            if (c != conn)
            {
                c.Send(msg);
                relayed++;
            }
        }
        Debug.Log($"[Servidor] SonidosSync retransmitido a {relayed} cliente(s) (excluido emisor connId={conn.connectionId}): audioIndex={msg.audioIndex}");
    }

    #endregion

    #region Manejo de Sesiones

    private void HandleCreateSessionMessage(NetworkConnectionToClient conn, CreateSessionMessage msg)
    {
        // Envolvemos TODO en try-catch: una excepción no atrapada hace que Mirror desconecte
        // al examinador (connId=0), lo que luego dispara MonitorClientConnections → borra la sesión.
        try
        {
            Debug.Log($"[Servidor] HandleCreateSessionMessage RECIBIDO — session='{msg.sessionName}', env='{msg.environmentName}', connId={conn.connectionId}");

            // Limpiar TODAS las sesiones previas al crear una nueva.
            // Razones:
            //   1. Sesiones huérfanas del grace period (mismo o distinto nombre) interferirían con
            //      el joining client: activeSessions.Values.First() devolvería la vieja en vez de la nueva.
            //   2. Un CreateSession nuevo indica que el examinador reinició la sesión desde cero.
            // También destruir handlers de entornos anteriores (ej: MuseumBaseNetworkHandler de una
            // sesión Museum anterior cuando ahora se crea Sala, y viceversa).
            if (activeSessions.Count > 0)
            {
                Debug.LogWarning($"[Servidor] CreateSession '{msg.sessionName}': limpiando {activeSessions.Count} sesión(es) previa(s) y sus handlers.");
                activeSessions.Clear();
                DestroyAllNetworkHandlers();
            }

            if (msg.environmentName == "Museum")
            {
                msg.environmentName = "Museum Environment";
            }
            else if (msg.environmentName == "Circular")
            {
                msg.environmentName = "Circular Environment";
            }
            else if (msg.environmentName == "Sala")
            {
                msg.environmentName = "Sala Environment";
            }
            else
            {
                Debug.LogError($"Nombre de escena desconocido: '{msg.environmentName}'");
                conn.Send(new SessionCreatedMessage
                {
                    success          = false,
                    sessionName      = msg.sessionName      ?? "",
                    environmentName  = msg.environmentName  ?? "",
                    displayMode      = msg.displayMode      ?? "",
                    browsingMode     = msg.browsingMode     ?? "",
                    categories       = msg.categories       ?? new List<string>(),
                    elementPaths     = msg.elementPaths     ?? new List<string>(),
                    audioPaths       = msg.audioPaths       ?? new List<string>()
                });
                return;
            }

            var sessionData = new SessionData
            {
                sessionName           = msg.sessionName      ?? "",
                userId                = msg.userId,
                environmentName       = msg.environmentName  ?? "",
                isOnlineSession       = msg.isOnlineSession,
                displayMode           = msg.displayMode      ?? "",
                browsingMode          = msg.browsingMode     ?? "",
                volumetric            = msg.volumetric,
                dimension             = msg.dimension,
                categories            = msg.categories       ?? new List<string>(),
                elementPaths          = msg.elementPaths     ?? new List<string>(),
                audioPaths            = msg.audioPaths            ?? new List<string>(),
                minRooms              = msg.minRooms,
                maxRooms              = msg.maxRooms,
                roomSeed              = msg.roomSeed,
                selectedObjectTypes   = msg.selectedObjectTypes   ?? new List<string>(),
                selectedDirections    = msg.selectedDirections    ?? new List<string>(), // (fix añadido)
                configLevel           = msg.configLevel,
                hasAcousticOverride   = msg.hasAcousticOverride,
                acousticSpatialBlend  = msg.acousticSpatialBlend,
                acousticSpread        = msg.acousticSpread,
                acousticDopplerLevel  = msg.acousticDopplerLevel,
                acousticRolloffMode   = msg.acousticRolloffMode,
                acousticSpatialize    = msg.acousticSpatialize,
                hasEmitterOverride    = msg.hasEmitterOverride,
                emitterBaseVolume     = msg.emitterBaseVolume,
                emitterMinConfigLevel = msg.emitterMinConfigLevel,
                emitterMinDistance    = msg.emitterMinDistance,
                emitterMaxDistance    = msg.emitterMaxDistance
            };
            activeSessions[sessionData.sessionName] = sessionData;
            noAuthClientCycles = 0; // Resetear contador — la sesión recién nació, dar gracia completa
            Debug.Log($"[Servidor] Sesión '{sessionData.sessionName}' ({sessionData.environmentName}) registrada. Total sesiones: {activeSessions.Count}");

            conn.Send(new SessionCreatedMessage
            {
                success               = true,
                sessionName           = sessionData.sessionName,
                userId                = sessionData.userId,
                environmentName       = sessionData.environmentName,
                isOnlineSession       = sessionData.isOnlineSession,
                displayMode           = sessionData.displayMode,
                browsingMode          = sessionData.browsingMode,
                volumetric            = sessionData.volumetric,
                dimension             = sessionData.dimension,
                categories            = sessionData.categories,
                elementPaths          = sessionData.elementPaths,
                audioPaths            = sessionData.audioPaths,
                minRooms              = sessionData.minRooms,
                maxRooms              = sessionData.maxRooms,
                roomSeed              = sessionData.roomSeed,
                selectedObjectTypes   = sessionData.selectedObjectTypes ?? new List<string>(),
                configLevel           = sessionData.configLevel,
                hasAcousticOverride   = sessionData.hasAcousticOverride,
                acousticSpatialBlend  = sessionData.acousticSpatialBlend,
                acousticSpread        = sessionData.acousticSpread,
                acousticDopplerLevel  = sessionData.acousticDopplerLevel,
                acousticRolloffMode   = sessionData.acousticRolloffMode,
                acousticSpatialize    = sessionData.acousticSpatialize,
                hasEmitterOverride    = sessionData.hasEmitterOverride,
                emitterBaseVolume     = sessionData.emitterBaseVolume,
                emitterMinConfigLevel = sessionData.emitterMinConfigLevel,
                emitterMinDistance    = sessionData.emitterMinDistance,
                emitterMaxDistance    = sessionData.emitterMaxDistance
            });

            Debug.Log("[Servidor] Inicializando NetworkHandler...");

            if (msg.environmentName == "Museum Environment")
            {
                Debug.Log("[Servidor] Creando MuseumBaseNetworkHandler para sincronización.");
                GameObject museumBaseNetwork = Instantiate(museumBaseNetworkPrefab);
                NetworkServer.Spawn(museumBaseNetwork);
                Debug.Log($"[Servidor] MuseumBaseNetworkHandler spawneado con Net ID: {museumBaseNetwork.GetComponent<NetworkIdentity>().netId}");
            }
            else if (msg.environmentName == "Circular Environment")
            {
                Debug.Log("[Servidor] Creando CircularNetworkHandler para sincronización.");
                GameObject circularNetwork = Instantiate(CircularNetworkPrefab);
                NetworkServer.Spawn(circularNetwork);
                Debug.Log($"[Servidor] CircularNetworkHandler spawneado con Net ID: {circularNetwork.GetComponent<NetworkIdentity>().netId}");
            }
            else if (msg.environmentName == "Sala Environment")
            {
                // El layout de Sala se sincroniza por seed en cada cliente.
                // SalaNetworkHandler se encarga del tracking de posición de los jugadores.
                if (SalaNetworkHandlerPrefab != null)
                {
                    Debug.Log("[Servidor] Creando SalaNetworkHandler para tracking de jugadores.");
                    GameObject salaNetwork = Instantiate(SalaNetworkHandlerPrefab);
                    NetworkServer.Spawn(salaNetwork);
                    Debug.Log($"[Servidor] SalaNetworkHandler spawneado con Net ID: {salaNetwork.GetComponent<NetworkIdentity>().netId}");
                }
                else
                {
                    Debug.LogWarning("[Servidor] SalaNetworkHandlerPrefab no asignado en ServerSessionManager. Sin tracking de posición de jugadores.");
                }

                // AÑADIDO: Spawnear ChatCanvas para chat de texto en Sala online
                if (ChatCanvasPrefab != null)
                {
                    Debug.Log("[Servidor] AÑADIDO: Spawneando ChatCanvas para sesión Sala online.");
                    GameObject chatCanvas = Instantiate(ChatCanvasPrefab);
                    NetworkServer.Spawn(chatCanvas);
                    Debug.Log($"[Servidor] AÑADIDO: ChatCanvas spawneado con Net ID: {chatCanvas.GetComponent<NetworkIdentity>().netId}");
                }
                else
                {
                    Debug.LogWarning("[Servidor] AÑADIDO: ChatCanvasPrefab no asignado. Chat no disponible en Sala.");
                }
            }
            else
            {
                Debug.LogWarning("[Servidor] No se encontró un NetworkHandler válido para esta escena.");
            }
        }
        catch (System.Exception ex)
        {
            // Capturar la excepción para que Mirror NO desconecte al examinador.
            // Sin este catch, Mirror desconecta connId → MonitorClientConnections borra la sesión
            // → el cliente que se une llega y no encuentra nada.
            Debug.LogError($"[Servidor] EXCEPCIÓN en HandleCreateSessionMessage — {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }



    private void HandleRequestActiveSessionMessage(NetworkConnectionToClient conn, RequestActiveSessionMessage msg)
    {
        Debug.Log($"Servidor recibi� RequestActiveSessionMessage del cliente {conn.connectionId}.");

        if (activeSessions.Count == 0)
        {
            Debug.LogWarning("No hay sesiones activas.");
            Debug.Log("[Server] Enviando mensaje: ActiveSessionResponseMessage");


            conn.Send(new ActiveSessionResponseMessage { success = false });
            return;
        }

        var sessionData = activeSessions.Values.First();
        Debug.Log($"Enviando datos de la sesi�n activa al cliente {conn.connectionId}: {sessionData.sessionName}");
        Debug.Log("[Server] Enviando mensaje: ActiveSessionResponseMessage");


        conn.Send(new ActiveSessionResponseMessage
        {
            success = true,
            sessionData = sessionData
        });
        Debug.Log($"Mensaje enviado al cliente {conn.connectionId}: {sessionData.sessionName}, {sessionData.environmentName}, {string.Join(", ", sessionData.categories)}, {sessionData.dimension}");
    }

    // Comando para unirse a una sesi�n existente
    [Command]
    public void CmdJoinSession(NetworkConnectionToClient conn)
    {
        if (activeSessions.Count == 0)
        {
            Debug.LogWarning("No hay sesiones activas en este momento.");
            Debug.Log("[Server] Enviando mensaje: SessionCreatedMessage");


            conn.Send(new SessionCreatedMessage { success = false });
            return;
        }

        // Selecciona la primera sesi�n activa (puedes cambiar esto si necesitas algo m�s espec�fico)
        var sessionData = activeSessions.Values.First();

        Debug.Log($"Cliente {conn.connectionId} unido a la sesi�n '{sessionData.sessionName}'.");

        Debug.Log("[Server] Enviando mensaje: SessionCreatedMessage");


        // Enviar los datos de la sesión al cliente que se une
        conn.Send(new SessionCreatedMessage
        {
            success               = true,
            sessionName           = sessionData.sessionName,
            userId                = sessionData.userId,
            environmentName       = sessionData.environmentName,
            isOnlineSession       = sessionData.isOnlineSession,
            displayMode           = sessionData.displayMode,
            browsingMode          = sessionData.browsingMode,
            volumetric            = sessionData.volumetric,
            dimension             = sessionData.dimension,
            categories            = sessionData.categories,
            elementPaths          = sessionData.elementPaths,
            audioPaths            = sessionData.audioPaths,
            minRooms              = sessionData.minRooms,
            maxRooms              = sessionData.maxRooms,
            roomSeed              = sessionData.roomSeed,
            selectedObjectTypes   = sessionData.selectedObjectTypes ?? new List<string>(),
            configLevel           = sessionData.configLevel,
            hasAcousticOverride   = sessionData.hasAcousticOverride,
            acousticSpatialBlend  = sessionData.acousticSpatialBlend,
            acousticSpread        = sessionData.acousticSpread,
            acousticDopplerLevel  = sessionData.acousticDopplerLevel,
            acousticRolloffMode   = sessionData.acousticRolloffMode,
            acousticSpatialize    = sessionData.acousticSpatialize,
            hasEmitterOverride    = sessionData.hasEmitterOverride,
            emitterBaseVolume     = sessionData.emitterBaseVolume,
            emitterMinConfigLevel = sessionData.emitterMinConfigLevel,
            emitterMinDistance    = sessionData.emitterMinDistance,
            emitterMaxDistance    = sessionData.emitterMaxDistance
        });
    }

    public void HandleAllClientsDisconnected()
    {
        // Siempre destruir los handlers (el bug anterior era que solo se destruían
        // si activeSessions.Count > 0 — dejando handlers huérfanos si la sesión
        // ya había sido limpiada pero el handler no).
        bool hadSessions = activeSessions.Count > 0;
        if (hadSessions)
        {
            Debug.Log("[Servidor] HandleAllClientsDisconnected: eliminando sesiones activas.");
            activeSessions.Clear();
            Debug.Log("[Servidor] Todas las sesiones han sido eliminadas.");
        }

        // Destruir handlers siempre (huérfanos o no)
        DestroyAllNetworkHandlers();
    }

    private void DestroyAllNetworkHandlers()
    {
        Debug.Log("Buscando y eliminando todos los NetworkHandlers activos...");

        // Buscar y destruir todos los MuseumBaseNetworkHandler
        foreach (var handler in FindObjectsOfType<MuseumBaseNetworkHandler>())
        {
            Debug.Log($"Eliminando MuseumBaseNetworkHandler con Net ID: {handler.GetComponent<NetworkIdentity>().netId}");
            NetworkServer.Destroy(handler.gameObject);
        }

        // Buscar y destruir todos los CircularNetworkHandler
        foreach (var handler in FindObjectsOfType<CircularNetworkHandler>())
        {
            Debug.Log($"Eliminando CircularNetworkHandler con Net ID: {handler.GetComponent<NetworkIdentity>().netId}");
            NetworkServer.Destroy(handler.gameObject);
        }

        // Buscar y destruir todos los SalaNetworkHandler
        foreach (var handler in FindObjectsOfType<SalaNetworkHandler>())
        {
            Debug.Log($"Eliminando SalaNetworkHandler con Net ID: {handler.GetComponent<NetworkIdentity>().netId}");
            NetworkServer.Destroy(handler.gameObject);
        }
    }

    /// <summary>
    /// Devuelve el nombre del entorno de la primera sesión activa, o "" si no hay sesiones.
    /// Usado por CustomNetworkManager para decidir si spawnear el playerPrefab en OnServerAddPlayer.
    /// </summary>
    public string GetFirstSessionEnvironment()
    {
        foreach (var kv in activeSessions)
            return kv.Value.environmentName;
        return "";
    }

    #endregion

    #region Sincronizaci�n con el Cliente

    // Notificar al cliente que la sesi�n fue creada exitosamente
    [TargetRpc]
    private void TargetNotifySessionCreated(NetworkConnection target, SessionData sessionData)
    {
        if (sessionData.categories == null)
        {
            sessionData.categories = new List<string>(); // Asegurarse de que no sea nulo
        }

        Debug.Log($"Sesi�n '{sessionData.sessionName}' creada en el cliente.");
        Debug.Log($"Datos enviados: Nombre: {sessionData.sessionName}, Usuario ID: {sessionData.userId}, Entorno: {sessionData.environmentName}, Categor�as: {string.Join(", ", sessionData.categories)}");
    }


    // Notificar al cliente que se uni� exitosamente a una sesi�n
    [TargetRpc]
    private void TargetNotifySessionJoined(NetworkConnection target, SessionData sessionData)
    {
        Debug.Log($"Cliente unido a la sesi�n '{sessionData.sessionName}'.");
        // Aqu� puedes sincronizar datos de la sesi�n con el cliente.
    }

    // Notificar al cliente de un error
    [TargetRpc]
    private void TargetNotifyError(NetworkConnection target, string errorMessage)
    {
        Debug.LogError($"Error enviado al cliente: {errorMessage}");
        // Aqu� puedes mostrar un mensaje de error en el cliente.
    }

    #endregion
}
