using UnityEngine;
using System.Collections.Generic;
 
namespace Vortices
{
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
    
            [Tooltip("Identificador único del sonido. Usado por el examinador para referenciar este emisor en runtime.")]
            public string soundId = "default";
    
            [Tooltip("Clip a reproducir en esta sala. Si está vacío no se inyecta sonido.")]
            public AudioClip audioClip;
    
            [Tooltip("Volumen base del emisor para esta sala (0–1).")]
            [Range(0f, 1f)] public float baseVolume = 1f;
    
            [Tooltip("Nivel mínimo de configuración para activar el sonido en esta sala (1–6).")]
            [Range(1, 6)] public int minConfigLevel = 2;
    
            [Tooltip("Distancia mínima de audio 3D (metros).")]
            [Min(0f)] public float minDistance = 1f;
    
            [Tooltip("Distancia máxima de audio 3D (metros).")]
            [Min(0f)] public float maxDistance = 10f;
    
            [Tooltip("Si está activo, sobreescribe el tipo de emisor con el valor de abajo.")]
            public bool overrideEmitterType = false;
    
            [Tooltip("Tipo de emisor alternativo (solo si overrideEmitterType = true).")]
            public SoundEmitter.SoundEmitterType emitterType = SoundEmitter.SoundEmitterType.Generic;
 
            [Tooltip("Si está activo, el audio se repite indefinidamente ignorando Play Count.")]
            public bool loop = true;
 
            [Tooltip("Número de veces que se reproduce el clip antes de detenerse.\n" +
                     "Solo aplica cuando Loop está desactivado.\n" +
                     "0 = una sola vez, 2 = dos veces, etc.")]
            [Min(0)] public int playCount = 0;
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

            // Aplicar override global de Step 5 (emitter config) si está configurado
            float finalVolume      = roomOverride.baseVolume;
            int   finalMinLevel    = roomOverride.minConfigLevel;
            float finalMinDistance = roomOverride.minDistance;
            float finalMaxDistance = roomOverride.maxDistance;
            if (SessionManager.instance != null && SessionManager.instance.hasEmitterOverride)
            {
                finalVolume      = SessionManager.instance.emitterBaseVolume;
                finalMinLevel    = SessionManager.instance.emitterMinConfigLevel;
                finalMinDistance = SessionManager.instance.emitterMinDistance;
                finalMaxDistance = SessionManager.instance.emitterMaxDistance;
            }

            emitter.Configure(
                emitterType,
                roomOverride.audioClip,
                finalVolume,
                finalMinLevel,
                finalMinDistance,
                finalMaxDistance,
                roomIndex,
                roomOverride.soundId,
                roomOverride.loop,
                roomOverride.playCount);
    
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
}