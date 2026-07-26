using UnityEngine;
using Mirror;

/// <summary>
/// Versión servidor de SalaNetworkHandler.
/// El servidor solo retransmite las posiciones vía Cmd/Rpc.
/// El tracking y el bucle de envío viven en la versión cliente (OnStartClient).
/// </summary>
public class SalaNetworkHandler : NetworkBehaviour
{
    public static SalaNetworkHandler Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ─── Sincronización de posición de jugadores ───────────────────────────────

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
            Debug.LogWarning($"[SalaNetworkHandler] Prefab de jugador '{playerName}' no encontrado.");
            return;
        }

        // Saltar el blob del jugador LOCAL: PlayerMovement.Update() ya lo mueve
        // siguiendo Camera.main. Si el RPC también lo mueve, compiten y causan
        // movimiento errático / volar a través del suelo.
        NetworkIdentity ni = player.GetComponent<NetworkIdentity>();
        if (ni != null && ni.isLocalPlayer) return;

        player.transform.position = position;
        player.transform.rotation = rotation;
    }
}
