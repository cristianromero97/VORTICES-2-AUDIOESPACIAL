using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class CustomNetworkManager : NetworkManager
{

    public GameObject chatCanvasPrefab;

    public override void Awake()
    {
        base.Awake();
        Debug.Log("CustomNetworkManager - Awake");
    }

    public override void Start()
    {
        base.Start();
        Debug.Log("CustomNetworkManager - Start");

        // Auto-iniciar el servidor para que acepte conexiones desde el menú principal.
        // Así el Launcher puede lanzar el cliente sin que el usuario del servidor
        // tenga que crear una sesión manualmente primero.
        if (!NetworkServer.active && !NetworkClient.isConnected)
        {
            Debug.Log("[CustomNetworkManager] Auto-iniciando servidor Mirror...");
            StartServer();
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        if (chatCanvasPrefab != null)
        {
            // Crear el ChatCanvas
            GameObject chatInstance = Instantiate(chatCanvasPrefab);
            
            // Activar el ChatCanvas en el servidor
            chatInstance.SetActive(true);
            
            // Marcarlo como persistente entre escenas
            DontDestroyOnLoad(chatInstance);
            
            // Sincronizar con los clientes
            NetworkServer.Spawn(chatInstance);
            Debug.Log("[ChatCanvas] ChatCanvas global creado, activado y marcado como persistente.");
        }
        else
        {
            Debug.LogError("[ChatCanvas] ChatCanvasPrefab no está asignado.");
        }
    }



    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        Debug.Log($"[OnServerAddPlayer] Añadiendo jugador con connId: {conn.connectionId}...");

        // Instanciar y añadir el jugador (todos los entornos)
        // NOTA Sala Environment: no tiene NetworkManager Start Positions en el Inspector
        // → GetStartPosition() devuelve null → el prefab aparece en (0,0,0) = inicio del corredor.
        // El jugador VR se mueve con el XR rig (posición independiente del prefab), pero
        // el prefab sirve como indicador visual de presencia de cada cliente conectado.
        // Para tracking de posición VR real se necesita un SalaNetworkHandler (trabajo futuro).
        Transform startPos = GetStartPosition();
        GameObject player = startPos != null
            ? Instantiate(playerPrefab, startPos.position, startPos.rotation)
            : Instantiate(playerPrefab);

        player.name = $"Player [connId={conn.connectionId}]";
        Debug.Log("[OnServerAddPlayer] Jugador instanciado: " + player.name);

        NetworkServer.AddPlayerForConnection(conn, player);

        // Sincronizar connId a todos los clientes vía SyncVar para que puedan
        // renombrar el objeto y SalaNetworkHandler lo encuentre por nombre.
        PlayerConnId connIdComp = player.GetComponent<PlayerConnId>();
        if (connIdComp != null)
            connIdComp.connId = conn.connectionId;
        else
            Debug.LogWarning("[OnServerAddPlayer] PlayerConnId no encontrado en el player prefab. Agrega el componente al prefab.");
        Debug.Log("[OnServerAddPlayer] Jugador añadido a la conexión del cliente.");

        // Buscar el ChatCanvas global
        GameObject chatCanvas = GameObject.FindWithTag("ChatCanvas");
        if (chatCanvas != null)
           Debug.Log("[ChatCanvas] Se encontró el ChatCanvas en el servidor.");
        else
            Debug.LogError("[ChatCanvas] No se encontró el ChatCanvas en el servidor.");
    }





}
