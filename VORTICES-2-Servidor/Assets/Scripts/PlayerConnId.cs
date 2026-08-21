using UnityEngine;
using Mirror;

/// <summary>
/// Sincroniza el connectionId del dueño de este player prefab a todos los clientes.
/// El servidor asigna <see cref="connId"/> tras spawnear el objeto;
/// el SyncVar lo propaga automáticamente y el hook renombra el GameObject para que
/// SalaNetworkHandler.RpcUpdatePlayerPosition lo encuentre por nombre.
/// </summary>
public class PlayerConnId : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnConnIdChanged))]
    public int connId = -1;

    // QUITAR EN CASO DE VOLVER A LA VERSION ANTERIOR (eliminar también VivoxOcclusionManager.cs)
    [SyncVar]
    public int vivoxUserId = -1;

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        var sm = Vortices.SessionManager.instance;
        if (sm != null) CmdSetVivoxUserId(sm.userId);
    }

    [Command]
    private void CmdSetVivoxUserId(int id) => vivoxUserId = id;

    private void OnConnIdChanged(int _, int newId)
    {
        if (newId < 0) return;
        gameObject.name = $"Player [connId={newId}]";
        Debug.Log($"[PlayerConnId] Player renombrado a 'Player [connId={newId}]'");

        // Para el jugador LOCAL en VR: ocultar mesh propio pero conservar sombra.
        //   • ShadowsOnly: el renderer no dibuja el mesh en la cámara local,
        //     pero sigue proyectando sombras → el jugador ve su propia sombra en el suelo.
        //   • Solo se activa cuando hay un dispositivo XR real conectado;
        //     en el Editor sin headset el blob queda visible para facilitar pruebas.
        //   • Otros clientes no se ven afectados (cada uno tiene su propia instancia).
        // En VR real (build con headset): ocultar mesh propio, conservar sombra.
        // En el Editor: dejar visible para facilitar pruebas.
        if (isLocalPlayer)
        {
            if (!Application.isEditor)
            {
                foreach (var r in GetComponentsInChildren<Renderer>())
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                Debug.Log($"[PlayerConnId] Blob local (connId={newId}) → ShadowsOnly (build VR).");
            }
            else
            {
                Debug.Log($"[PlayerConnId] Blob local (connId={newId}) → visible (Editor).");
            }
        }
    }
}
