using System.Collections;
using UnityEngine;
using Mirror;

/// <summary>
/// Sincroniza las posiciones de los jugadores en Sala Environment.
/// Spawneado por el servidor al crear una sesión de Sala.
/// Cada cliente envía su posición (cabeza/cámara VR) al servidor,
/// que la distribuye a todos para que el prefab de jugador (blob)
/// se mueva al lugar real de cada participante.
/// </summary>
public class SalaNetworkHandler : NetworkBehaviour
{
    public static SalaNetworkHandler Instance { get; private set; }

    // Frecuencia de envío de posición: 20 actualizaciones por segundo
    private const float PositionUpdateInterval = 0.05f;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        Debug.Log("[SalaNetworkHandler] OnStartClient — DontDestroyOnLoad activado.");
        DontDestroyOnLoad(gameObject);
        StartCoroutine(WaitForSalaEnvironment());
    }

    // ─── Sincronización de posición de jugadores ───────────────────────────────

    /// <summary>
    /// Llamado por el cliente local para enviar su posición al servidor.
    /// El servidor la reenvía a todos los clientes vía RPC.
    /// </summary>
    public void SendLocalPlayerPosition(int connId, Vector3 position, Quaternion rotation)
    {
        if (isServer)
            RpcUpdatePlayerPosition(connId, position, rotation);
        else
            CmdUpdatePlayerPosition(connId, position, rotation);
    }

    [Command(requiresAuthority = false)]
    public void CmdUpdatePlayerPosition(int connId, Vector3 position, Quaternion rotation)
    {
        RpcUpdatePlayerPosition(connId, position, rotation);
    }

    [ClientRpc]
    void RpcUpdatePlayerPosition(int connId, Vector3 position, Quaternion rotation)
    {
        string playerName = $"Player [connId={connId}]";
        GameObject player = GameObject.Find(playerName);
        if (player == null)
        {
            Debug.LogWarning($"[SalaNetworkHandler] Prefab de jugador '{playerName}' no encontrado en escena.");
            return;
        }

        // Saltar el blob del jugador LOCAL: PlayerMovement.Update() ya lo mueve
        // siguiendo Camera.main cada frame. Si el RPC también lo mueve, los dos
        // compiten y pueden causar movimiento errático / volar a través del suelo.
        NetworkIdentity ni = player.GetComponent<NetworkIdentity>();
        if (ni != null && ni.isLocalPlayer) return;

        player.transform.position = position;
        player.transform.rotation = rotation;
    }

    // ─── Espera de escena y bucle de broadcasting ──────────────────────────────

    private IEnumerator WaitForSalaEnvironment()
    {
        Debug.Log("[SalaNetworkHandler] Esperando a que cargue Sala Environment...");

        // Esperar hasta que SessionManager confirme que estamos en Sala (timeout 120 s)
        Vortices.SessionManager sm = null;
        float timeout = 120f;
        while (timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
            sm = Vortices.SessionManager.instance;
            if (sm != null
                && (sm.environmentName == "Sala Environment" || sm.environmentName == "Sala"))
                break;
        }

        if (sm == null)
        {
            Debug.LogWarning("[SalaNetworkHandler] SessionManager no encontrado en 120 s. Sin tracking de posición.");
            yield break;
        }

        // Esperar hasta encontrar XR Origin en la escena (timeout 60 s)
        GameObject xrOrigin = null;
        timeout = 60f;
        while (xrOrigin == null && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
            xrOrigin = GameObject.Find("XR Origin");
        }

        if (xrOrigin == null)
        {
            Debug.LogWarning("[SalaNetworkHandler] 'XR Origin' no encontrado en Sala Environment. Sin tracking de posición.");
            yield break;
        }

        // Esperar a que AddPlayer() haya spawneado el blob local y el SyncVar connId
        // esté asignado. NetworkClient.connection.connectionId es siempre 0 en el cliente;
        // el connId real (asignado por el servidor) vive en PlayerConnId.connId del local player.
        Debug.Log("[SalaNetworkHandler] Esperando blob del jugador local (AddPlayer + SyncVar connId)...");
        int myConnId = -1;
        float connTimeout = 30f;
        while (myConnId < 0 && connTimeout > 0f)
        {
            connTimeout -= Time.deltaTime;
            yield return null;
            if (NetworkClient.localPlayer != null)
            {
                var cid = NetworkClient.localPlayer.GetComponent<PlayerConnId>();
                if (cid != null && cid.connId >= 0)
                    myConnId = cid.connId;
            }
        }

        if (myConnId < 0)
        {
            Debug.LogWarning("[SalaNetworkHandler] connId del jugador local no disponible en 30 s. Sin tracking de posición.");
            yield break;
        }

        Debug.Log($"[SalaNetworkHandler] XR Origin encontrado. connId local={myConnId}. Iniciando broadcast de posición.");

        // Bucle de envío continuo: se detiene cuando se pierde la conexión
        // o cuando Unity destruye este objeto (Mirror destroy → coroutine stops)
        while (NetworkClient.isConnected)
        {
            // Usar XR Origin (posición al nivel del suelo) para el blob.
            // Camera.main está a la altura de la cabeza → el blob quedaría flotando.
            SendLocalPlayerPosition(myConnId, xrOrigin.transform.position, xrOrigin.transform.rotation);
            yield return new WaitForSeconds(PositionUpdateInterval);
        }

        Debug.Log("[SalaNetworkHandler] Conexión perdida. Deteniendo broadcast de posición.");
    }
}
