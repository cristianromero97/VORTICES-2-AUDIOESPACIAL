using Mirror;

/// <summary>
/// Mensaje de sincronización del SonidosPanel entre clientes.
/// El cliente emisor lo manda al servidor; el servidor lo retransmite a TODOS.
/// audioIndex >= 0 → emitir ese audio. audioIndex = -1 → silenciar el actual.
/// </summary>
public struct SonidosSyncMessage : NetworkMessage
{
    public int audioIndex;
}
