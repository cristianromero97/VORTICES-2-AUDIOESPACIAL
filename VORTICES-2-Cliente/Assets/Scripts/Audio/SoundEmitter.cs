using UnityEngine;
 
/// <summary>
/// SoundEmitter: Componente de emisión de sonido 3D gestionado por el sistema de inmersión.
///
/// Su configuración (clip, volumen, distancias, sala) siempre viene de <see cref="FurnitureAudioInjector"/>
/// a través de <see cref="Configure"/>. Los únicos campos visibles en el Inspector son
/// los de comportamiento de reproducción, que sí pueden variar por prefab.
///
/// El ciclo de vida es:
///   FurnitureAudioInjector.InjectAudio() → Configure() → AudioManager.RegisterEmitter()
///   AudioManager → Activate() / Deactivate() según nivel de inmersión
/// </summary>
[RequireComponent(typeof(AudioSource))]
[DisallowMultipleComponent]
public class SoundEmitter : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Tipos de emisor (público para FurnitureAudioInjector)
    // ─────────────────────────────────────────────
 
    public enum SoundEmitterType
    {
        Generic,
        Appliance,   // electrodomésticos: TV, radio, lavadora, microondas, etc.
        Ambient,     // ambiente de sala: viento, lluvia, ruido de fondo
        Machinery,   // máquinas industriales, generadores
        Nature,      // pájaros, agua, viento exterior
        NPC          // personajes, voces
    }
 
    // ─────────────────────────────────────────────
    //  Inspector — solo comportamiento de reproducción
    // ─────────────────────────────────────────────
 
    [Header("Reproducción")]
    [Tooltip("¿El audio se reproduce en bucle?")]
    [SerializeField] private bool loop = true;
 
    [Tooltip("¿Reproducir automáticamente cuando el nivel de inmersión lo active?")]
    [SerializeField] private bool playOnActivate = true;
 
    [Header("Espacialización")]
    [Tooltip("Garantiza atenuación por distancia incluso cuando el nivel de inmersión tiene spatialBlend bajo.")]
    [SerializeField] private bool enforceDistanceAttenuation = true;
 
    [Tooltip("SpatialBlend mínimo aplicado cuando enforceDistanceAttenuation está activo.")]
    [SerializeField, Range(0f, 1f)] private float minimumSpatialBlend = 0.1f;
 
    // ─────────────────────────────────────────────
    //  Estado interno — seteado por Configure()
    // ─────────────────────────────────────────────
 
    private AudioSource      audioSource;
    private bool             isActive;
 
    private SoundEmitterType emitterType       = SoundEmitterType.Generic;
    private AudioClip        audioClip;
    private float            baseVolume        = 1f;
    private int              minImmersionLevel = 2;
    private float            minDistance       = 1f;
    private float            maxDistance       = 12f;
    private int              roomId            = -1;
 
    // ─────────────────────────────────────────────
    //  Propiedades públicas (leídas por AudioManager)
    // ─────────────────────────────────────────────
 
    public SoundEmitterType EmitterType       => emitterType;
    public int              MinImmersionLevel => minImmersionLevel;
    public bool             IsActive          => isActive;
    public int              RoomId            => roomId;
 
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
        {
            AudioManager.Instance.RegisterEmitter(this);
        }
        else
        {
            Debug.LogWarning($"[SoundEmitter] '{name}': AudioManager no encontrado. " +
                             "El emitter no será controlado por niveles de inmersión.", this);
        }
    }
 
    private void OnDestroy()
    {
        AudioManager.Instance?.UnregisterEmitter(this);
    }
 
    // ─────────────────────────────────────────────
    //  API — llamada por AudioManager
    // ─────────────────────────────────────────────
 
    public void Activate(AudioManager.ImmersionLevelConfig config)
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
 
        if (playOnActivate && !audioSource.isPlaying)
            audioSource.Play();
    }
 
    public void Deactivate()
    {
        isActive = false;
 
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
        int              immersionLevel,
        float            minDist,
        float            maxDist,
        int              assignedRoomId = -1)
    {
        emitterType       = type;
        audioClip         = clip;
        baseVolume        = Mathf.Clamp01(volume);
        minImmersionLevel = Mathf.Clamp(immersionLevel, 1, 6);
        minDistance       = Mathf.Max(0f, minDist);
        maxDistance       = Mathf.Max(minDistance + 0.01f, maxDist);
        roomId            = assignedRoomId;
 
        if (audioSource != null)
            ApplyDefaultsToAudioSource();
    }
 
    // ─────────────────────────────────────────────
    //  API — utilidades en runtime
    // ─────────────────────────────────────────────
 
    /// <summary>Cambia el clip en runtime (ej: cambiar canal de TV).</summary>
    public void SetClip(AudioClip newClip, bool restartPlayback = true)
    {
        audioClip        = newClip;
        audioSource.clip = newClip;
 
        if (restartPlayback && isActive && playOnActivate)
        {
            audioSource.Stop();
            audioSource.Play();
        }
    }
 
    /// <summary>Cambia el volumen base en runtime. El AudioManager re-escala automáticamente.</summary>
    public void SetBaseVolume(float volume)
    {
        baseVolume = Mathf.Clamp01(volume);
 
        if (!isActive || AudioManager.Instance == null) return;
 
        var config = AudioManager.Instance.GetLevelConfig(AudioManager.Instance.CurrentImmersionLevel);
        if (config != null)
            audioSource.volume = baseVolume * config.globalVolume;
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
        audioSource.spatialBlend = 0f;    // AudioManager lo ajusta en Activate()
        audioSource.spread       = 0f;    // AudioManager lo ajusta en Activate()
        audioSource.spatialize   = false; // AudioManager lo ajusta en Activate()
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
 
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * (maxDistance * 0.15f + 0.5f),
            $"[{emitterType}] min={minDistance}m  max={maxDistance}m  minLevel={minImmersionLevel}");
    }
#endif
}