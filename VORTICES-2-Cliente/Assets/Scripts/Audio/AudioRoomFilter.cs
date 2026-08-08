using UnityEngine;
using System.Collections.Generic;

namespace Vortices
{
    /// <summary>
    /// AudioRoomFilter: Componente opcional que restringe qué salas suenan en el AudioManager.
    /// Añadir este componente al mismo GameObject que AudioManager activa el filtro.
    /// Si el componente no existe, AudioManager trata todas las salas como habilitadas.
    /// </summary>
    public class AudioRoomFilter : MonoBehaviour
    {
        [Tooltip("Si está activo, solo sonarán emitters cuyos RoomId estén en la lista permitida.")]
        [SerializeField] private bool filterEmittersByRoomId = false;

        [Tooltip("Habilita todas las salas de una vez, ignorando la lista de IDs. " +
                 "Útil cuando hay muchas salas y no se quieren cargar una por una.")]
        [SerializeField] private bool enableAllRooms = true;

        [Tooltip("IDs de sala permitidos cuando el filtro está activo y enableAllRooms = false.")]
        [SerializeField] private List<int> enabledRoomIds = new List<int>();

        // ─────────────────────────────────────────────
        //  Consulta
        // ─────────────────────────────────────────────

        public bool IsRoomEnabled(SoundEmitter emitter)
        {
            if (!filterEmittersByRoomId) return true;
            if (emitter == null) return false;
            if (enableAllRooms) return true;
            int id = emitter.RoomId;
            return id > 0 && enabledRoomIds != null && enabledRoomIds.Contains(id);
        }

        // ─────────────────────────────────────────────
        //  API — llamada desde AudioManager (delegación)
        // ─────────────────────────────────────────────

        public void SetRoomFilterEnabled(bool enabled)
        {
            filterEmittersByRoomId = enabled;
        }

        public void EnableAllRooms()
        {
            enableAllRooms = true;
        }

        public void SetEnabledRooms(List<int> roomIds)
        {
            enableAllRooms = false;
            enabledRoomIds = roomIds ?? new List<int>();
        }

        public void EnableRoom(int roomId)
        {
            enableAllRooms = false;
            if (!enabledRoomIds.Contains(roomId))
                enabledRoomIds.Add(roomId);
        }

        public void DisableRoom(int roomId)
        {
            enableAllRooms = false;
            enabledRoomIds.Remove(roomId);
        }
    }
}
