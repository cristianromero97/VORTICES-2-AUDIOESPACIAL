using UnityEngine;
using System.Collections;
 
namespace Vortices
{
    /// <summary>
    /// SoundEmitter: Componente de emisión de sonido 3D gestionado por el sistema de configuración acústica.
    ///
    /// MODOS DE REPRODUCCIÓN:
    ///   loop = true  → bucle infinito (comportamiento original).
    ///   loop = false + playCount = 0 → se reproduce una sola vez.
    ///   loop = false + playCount > 1 → se reproduce exactamente N veces y luego se detiene.
    ///
    /// La configuración (clip, volumen, distancias, sala) siempre viene de
    /// <see cref="FurnitureAudioInjector"/> a través de <see cref="Configure"/>.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    [DisallowMultipleComponent]
    public class SoundEmitter : MonoBehaviour
    {
        // ─────────────────────────────────────────────
        //  Tipos de emisor
        // ─────────────────────────────────────────────
 
        public enum SoundEmitterType
        {
            Generic,
            Appliance,   // electrodomésticos: TV, radio, lavadora, etc.
            Ambient,     // ambiente de sala: viento, lluvia, ruido de fondo
            Machinery,   // máquinas industriales, generadores
            Nature,      // pájaros, agua, viento exterior
            NPC          // personajes, voces
        }
 
        // ─────────────────────────────────────────────
        //  Inspector — comportamiento de reproducción
        // ─────────────────────────────────────────────
 
        [Header("Reproducción")]
        [Tooltip("Si está activo, el audio se repite indefinidamente ignorando Play Count.")]
        [SerializeField] private bool loop = true;
 
        [Tooltip("Número de veces que se reproduce el clip antes de detenerse.\n" +
                 "Solo aplica cuando Loop está desactivado.\n" +
                 "0 = una sola vez, 2 = dos veces, etc.")]
        [SerializeField, Min(0)] private int playCount = 0;
 
        [Tooltip("¿Reproducir automáticamente cuando el nivel de configuración lo active?")]
        [SerializeField] private bool playOnActivate = true;
 
        [Header("Espacialización")]
        [Tooltip("Garantiza atenuación por distancia incluso en niveles con spatialBlend bajo.")]
        [SerializeField] private bool enforceDistanceAttenuation = true;
 
        [Tooltip("SpatialBlend mínimo cuando enforceDistanceAttenuation está activo.")]
        [SerializeField, Range(0f, 1f)] private float minimumSpatialBlend = 0.1f;
 
        // ─────────────────────────────────────────────
        //  Estado interno — seteado por Configure()
        // ─────────────────────────────────────────────
 
        private AudioSource      audioSource;
        private bool             isActive;
        private Coroutine        playCountCoroutine;
 
        private SoundEmitterType emitterType       = SoundEmitterType.Generic;
        private AudioClip        audioClip;
        private float            baseVolume        = 1f;
        private int              minConfigLevel = 2;
        private float            minDistance       = 1f;
        private float            maxDistance       = 12f;
        private int              roomId            = -1;
        private string           soundId           = "default";
 
        // ─────────────────────────────────────────────
        //  Propiedades públicas
        // ─────────────────────────────────────────────
 
        public SoundEmitterType EmitterType       => emitterType;
        public int              MinConfigLevel => minConfigLevel;
        public bool             IsActive          => isActive;
        public int              RoomId            => roomId;
        public string           SoundId           => soundId;
 
        // ─────────────────────────────────────────────
        //  Unity lifecycle
        // ─────────────────────────────────────────────
 
        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            ApplyDefaultsToAudioSource();
        }
 
        private void Start()
        {
            if (AudioManager.Instance != null)
                AudioManager.Instance.RegisterEmitter(this);
            else
                Debug.LogWarning($"[SoundEmitter] '{name}': AudioManager no encontrado.", this);
        }
 
        private void OnDestroy()
        {
            StopPlayCountCoroutine();
            AudioManager.Instance?.UnregisterEmitter(this);
        }
 
        // ─────────────────────────────────────────────
        //  API — llamada por AudioManager
        // ─────────────────────────────────────────────
 
        public void Activate(AudioManager.AcousticProfile config)
        {
            if (audioClip == null) return;
 
            isActive = true;
 
            float blend = Mathf.Clamp01(config.spatialBlend);
            if (enforceDistanceAttenuation)
                blend = Mathf.Max(blend, minimumSpatialBlend);
 
            audioSource.volume       = baseVolume * config.globalVolume;
            audioSource.spatialBlend = blend;
            audioSource.spread       = config.spread;
            audioSource.dopplerLevel = config.dopplerLevel;
            audioSource.rolloffMode  = config.rolloffMode;
            audioSource.minDistance  = minDistance;
            audioSource.maxDistance  = maxDistance;
            audioSource.spatialize   = config.spatialize;
 
        if (config.rolloffMode == AudioRolloffMode.Custom && config.customRolloffCurve != null && config.customRolloffCurve.length > 0) 
        audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, config.customRolloffCurve);

            if (!playOnActivate) return;
 
            StopPlayCountCoroutine();
 
            if (loop)
            {
                // Bucle infinito
                audioSource.loop = true;
                if (!audioSource.isPlaying)
                    audioSource.Play();
            }
            else
            {
                // N reproducciones (playCount 0 = una vez, 1 = una vez, 2 = dos veces, etc.)
                audioSource.loop = false;
                int times = Mathf.Max(1, playCount == 0 ? 1 : playCount);
                playCountCoroutine = StartCoroutine(PlayNTimes(times));
            }
        }
 
        public void Deactivate()
        {
            isActive = false;
            StopPlayCountCoroutine();
 
            if (audioSource.isPlaying)
                audioSource.Stop();
        }
 
        // ─────────────────────────────────────────────
        //  API — llamada por FurnitureAudioInjector
        // ─────────────────────────────────────────────
 
        public void Configure(
            SoundEmitterType type,
            AudioClip        clip,
            float            volume,
            int              configLevel,
            float            minDist,
            float            maxDist,
            int              assignedRoomId  = -1,
            string           assignedSoundId = "default",
            bool             assignedLoop    = true,
            int              assignedPlayCount = 0)
        {
            emitterType       = type;
            audioClip         = clip;
            baseVolume        = Mathf.Clamp01(volume);
            minConfigLevel = Mathf.Clamp(configLevel, 1, 6);
            minDistance       = Mathf.Max(0f, minDist);
            maxDistance       = Mathf.Max(minDistance + 0.01f, maxDist);
            roomId            = assignedRoomId;
            soundId           = string.IsNullOrWhiteSpace(assignedSoundId) ? "default" : assignedSoundId;
            loop              = assignedLoop;
            playCount         = Mathf.Max(0, assignedPlayCount);
 
            if (audioSource != null)
                ApplyDefaultsToAudioSource();
        }
 
        // ─────────────────────────────────────────────
        //  API — utilidades en runtime
        // ─────────────────────────────────────────────
 
        /// <summary>Detiene la reproducción y resetea el tiempo al inicio. Usado por SonidosPanel.</summary>
        public void ForceStop()
        {
            Deactivate();
            if (audioSource != null) audioSource.time = 0f;
        }

        /// <summary>Cambia el clip en runtime (ej: cambiar canal de TV).</summary>
        public void SetClip(AudioClip newClip, bool restartPlayback = true)
        {
            audioClip        = newClip;
            audioSource.clip = newClip;
 
            if (restartPlayback && isActive && playOnActivate)
            {
                StopPlayCountCoroutine();
                audioSource.Stop();
                audioSource.Play();
            }
        }
 
        /// <summary>Cambia el volumen base en runtime.</summary>
        public void SetBaseVolume(float volume)
        {
            baseVolume = Mathf.Clamp01(volume);
            if (!isActive || AudioManager.Instance == null) return;
 
            var config = AudioManager.Instance.GetAcousticProfile(AudioManager.Instance.CurrentConfigLevel);
            if (config != null)
                audioSource.volume = baseVolume * config.globalVolume;
        }
 
        // ─────────────────────────────────────────────
        //  Corrutina de N reproducciones
        // ─────────────────────────────────────────────
 
        private IEnumerator PlayNTimes(int times)
        {
            for (int i = 0; i < times; i++)
            {
                audioSource.Play();
 
                // Esperar a que termine la reproducción actual
                yield return new WaitUntil(() => !audioSource.isPlaying);
 
                // Si fue desactivado externamente, salir
                if (!isActive) yield break;
            }
 
            // Terminadas las N reproducciones — el emitter queda activo pero en silencio
            // (el AudioManager lo puede reactivar si sube/baja el nivel de configuración)
            isActive = false;
            playCountCoroutine = null;
        }
 
        private void StopPlayCountCoroutine()
        {
            if (playCountCoroutine != null)
            {
                StopCoroutine(playCountCoroutine);
                playCountCoroutine = null;
            }
        }
 
        // ─────────────────────────────────────────────
        //  Privado
        // ─────────────────────────────────────────────
 
        private void ApplyDefaultsToAudioSource()
        {
            audioSource.clip         = audioClip;
            audioSource.loop         = loop;
            audioSource.volume       = baseVolume;
            audioSource.playOnAwake  = false;
            audioSource.spatialBlend = 0f;
            audioSource.spread       = 0f;
            audioSource.spatialize   = false;
            audioSource.minDistance  = minDistance;
            audioSource.maxDistance  = maxDistance;
            audioSource.Stop();
        }
 
        // ─────────────────────────────────────────────
        //  Gizmos
        // ─────────────────────────────────────────────
 
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.12f);
            Gizmos.DrawSphere(transform.position, maxDistance);
 
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, maxDistance);
 
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, minDistance);
 
            string playMode = loop ? "loop" : (playCount <= 1 ? "x1" : $"x{playCount}");
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * (maxDistance * 0.15f + 0.5f),
                $"[{emitterType}] {playMode}  min={minDistance}m  max={maxDistance}m  minLevel={minConfigLevel}");
        }
#endif
    }
}