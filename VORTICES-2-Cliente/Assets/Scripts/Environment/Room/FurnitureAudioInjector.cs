using UnityEngine;
using System.Collections.Generic;
 
/// <summary>
/// FurnitureAudioInjector: Única responsabilidad — agregar o configurar un SoundEmitter
/// en un GameObject de mueble instanciado, según la sala en la que fue colocado.
///
/// Es llamado por <see cref="RoomFurniturePlacer"/> después de instanciar cada prefab.
/// No sabe nada de geometría ni de cómo se generan las salas.
/// </summary>
[DisallowMultipleComponent]
public class FurnitureAudioInjector : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Data classes (públicas para que RoomFurniturePlacer las use en el Inspector)
    // ─────────────────────────────────────────────
 
    [System.Serializable]
    public class RoomAudioOverride
    {
        [Tooltip("ID de sala al que aplica esta configuración (Room_1 => 1, Room_2 => 2, etc.).")]
        [Min(1)] public int roomId = 1;
 
        [Tooltip("Clip a reproducir en esta sala. Si está vacío no se inyecta sonido.")]
        public AudioClip audioClip;
 
        [Tooltip("Volumen base del emisor para esta sala (0–1).")]
        [Range(0f, 1f)] public float baseVolume = 1f;
 
        [Tooltip("Nivel mínimo de inmersión para activar el sonido en esta sala (1–6).")]
        [Range(1, 6)] public int minImmersionLevel = 2;
 
        [Tooltip("Distancia mínima de audio 3D (metros).")]
        [Min(0f)] public float minDistance = 1f;
 
        [Tooltip("Distancia máxima de audio 3D (metros).")]
        [Min(0f)] public float maxDistance = 10f;
 
        [Tooltip("Si está activo, sobreescribe el tipo de emisor con el valor de abajo.")]
        public bool overrideEmitterType = false;
 
        [Tooltip("Tipo de emisor alternativo (solo si overrideEmitterType = true).")]
        public SoundEmitter.SoundEmitterType emitterType = SoundEmitter.SoundEmitterType.Generic;
    }
 
    [System.Serializable]
    public class FurnitureAudioConfig
    {
        [Tooltip("Configuraciones de audio por sala. Cada entrada aplica a un roomId distinto.")]
        public List<RoomAudioOverride> roomAudioOverrides = new List<RoomAudioOverride>();
    }
 
    // ─────────────────────────────────────────────
    //  API pública
    // ─────────────────────────────────────────────
 
    /// <summary>
    /// Inyecta o configura un SoundEmitter en <paramref name="instance"/> según
    /// la configuración de audio correspondiente a <paramref name="roomIndex"/>.
    /// Si no hay override para esa sala, no hace nada.
    /// </summary>
    public void InjectAudio(GameObject instance, FurnitureAudioConfig audioConfig, int roomIndex)
    {
        if (instance == null || audioConfig == null)
        {
            return;
        }
 
        if (!TryGetRoomOverride(audioConfig, roomIndex, out RoomAudioOverride roomOverride))
        {
            return;
        }
 
        if (roomOverride.audioClip == null)
        {
            Debug.LogWarning($"[FurnitureAudioInjector] Room {roomIndex}: el override no tiene AudioClip asignado. " +
                             $"No se inyectará SoundEmitter en '{instance.name}'.", instance);
            return;
        }
 
        SoundEmitter.SoundEmitterType emitterType = roomOverride.overrideEmitterType
            ? roomOverride.emitterType
            : SoundEmitter.SoundEmitterType.Generic;
 
        // Reutilizar SoundEmitter existente en el prefab o agregar uno nuevo
        SoundEmitter emitter = instance.GetComponentInChildren<SoundEmitter>(includeInactive: true);
        if (emitter == null)
        {
            emitter = instance.AddComponent<SoundEmitter>();
        }
 
        emitter.Configure(
            emitterType,
            roomOverride.audioClip,
            roomOverride.baseVolume,
            roomOverride.minImmersionLevel,
            roomOverride.minDistance,
            roomOverride.maxDistance,
            roomIndex);
 
        if (Application.isPlaying && AudioManager.Instance != null)
        {
            AudioManager.Instance.RegisterEmitter(emitter);
        }
    }
 
    // ─────────────────────────────────────────────
    //  Lógica interna
    // ─────────────────────────────────────────────
 
    private bool TryGetRoomOverride(FurnitureAudioConfig config, int roomIndex, out RoomAudioOverride result)
    {
        result = null;
 
        if (config.roomAudioOverrides == null)
        {
            return false;
        }
 
        for (int i = 0; i < config.roomAudioOverrides.Count; i++)
        {
            RoomAudioOverride candidate = config.roomAudioOverrides[i];
            if (candidate != null && candidate.roomId == roomIndex)
            {
                result = candidate;
                return true;
            }
        }
 
        return false;
    }
}