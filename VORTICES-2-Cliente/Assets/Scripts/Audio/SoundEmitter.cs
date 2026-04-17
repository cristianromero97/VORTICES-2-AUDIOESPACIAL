using UnityEngine;

/// <summary>
/// SoundEmitter: Componente genérico de emisión de sonido que se añade a cualquier
/// GameObject de la escena (televisores, radios, objetos ambientales, etc.).
///
/// Se auto-registra en el <see cref="AudioManager"/> al iniciar y respeta el nivel
/// de inmersión auditiva configurado en él. Soporta audio 3D espacial con control
/// de distancia mínima/máxima y efecto Doppler, siguiendo el modelo de MOTIONS P003.
///
/// USO TÍPICO (televisor):
///   1. Añadir este componente al prefab del televisor.
///   2. Asignar el AudioClip (sonido de TV) en el Inspector.
///   3. Elegir EmitterType = Television.
///   4. Ajustar MinImmersionLevel (e.g., 2) y las distancias 3D.
///   5. El AudioManager se encarga del resto automáticamente.
/// </summary>
[RequireComponent(typeof(AudioSource))]
[DisallowMultipleComponent]
public class SoundEmitter : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Tipos de emisor
    // ─────────────────────────────────────────────

    /// <summary>Categoría del emisor de sonido. Útil para filtrado y lógica futura.</summary>
    public enum SoundEmitterType
    {
        Generic,
        Television,
        Radio,
        Ambient,
        NPC,
        Machinery,
        Nature,
        Custom
    }

    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────

    [Header("Identidad")]
    [Tooltip("Categoría del emisor. Permite filtrar emitters por tipo desde el AudioManager.")]
    [SerializeField]
    private SoundEmitterType emitterType = SoundEmitterType.Generic;

    [Tooltip("Nivel de inmersión mínimo para que este emitter esté activo (1–6). " +
             "Por debajo de este nivel el sonido se silencia.")]
    [SerializeField, Range(1, 6)]
    private int minImmersionLevel = 2;

    [Tooltip("ID de sala al que pertenece este emisor (asignado automáticamente por el generador).")]
    [SerializeField]
    private int roomId = -1;

    [Header("Audio")]
    [Tooltip("Clip de audio a reproducir. Si se deja vacío no se reproducirá nada.")]
    [SerializeField]
    private AudioClip audioClip;

    [Tooltip("Volumen base del emisor (0–1). El AudioManager lo escala según el nivel de inmersión.")]
    [SerializeField, Range(0f, 1f)]
    private float baseVolume = 1f;

    [Tooltip("¿El audio se reproduce en bucle?")]
    [SerializeField]
    private bool loop = true;

    [Tooltip("¿Empezar a reproducir automáticamente cuando el nivel de inmersión lo active?")]
    [SerializeField]
    private bool playOnActivate = true;

    [Header("Audio 3D")]
    [Tooltip("Distancia a partir de la cual el audio comienza a atenuarse (en metros).")]
    [SerializeField, Min(0f)]
    private float minDistance = 1f;

    [Tooltip("Distancia máxima a la que el audio es audible (en metros).")]
    [SerializeField, Min(0f)]
    private float maxDistance = 12f;

    // ─────────────────────────────────────────────
    //  Estado interno
    // ─────────────────────────────────────────────
    private AudioSource audioSource;
    private bool isActive = false;

    // ─────────────────────────────────────────────
    //  Propiedades públicas
    // ─────────────────────────────────────────────

    /// <summary>Categoría de este emisor.</summary>
    public SoundEmitterType EmitterType => emitterType;

    /// <summary>Nivel de inmersión mínimo para que el emisor esté activo.</summary>
    public int MinImmersionLevel => minImmersionLevel;

    /// <summary>Indica si el emisor está actualmente activo (sonando).</summary>
    public bool IsActive => isActive;

    /// <summary>ID de sala asociado a este emisor. -1 si no está asociado.</summary>
    public int RoomId => roomId;

    // ─────────────────────────────────────────────
    //  Unity
    // ─────────────────────────────────────────────
    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        ConfigureAudioSourceDefaults();
    }

    private void Start()
    {
        // Si el AudioManager existe, se registra; de lo contrario advierte.
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.RegisterEmitter(this);
        }
        else
        {
            Debug.LogWarning($"[SoundEmitter] '{name}': No se encontró AudioManager en la escena. " +
                             "El emitter no será controlado por niveles de inmersión.", this);
        }
    }

    private void OnDestroy()
    {
        AudioManager.Instance?.UnregisterEmitter(this);
    }

    // ─────────────────────────────────────────────
    //  API pública (llamada por AudioManager)
    // ─────────────────────────────────────────────

    /// <summary>
    /// Activa el emisor y aplica la configuración del nivel de inmersión actual.
    /// Llamado por <see cref="AudioManager"/> cuando el nivel alcanza <see cref="MinImmersionLevel"/>.
    /// </summary>
    public void Activate(AudioManager.ImmersionLevelConfig config)
    {
        if (audioClip == null)
        {
            return;
        }

        isActive = true;

        // Aplicar configuración de nivel de inmersión
        audioSource.volume      = baseVolume * config.globalVolume;
        audioSource.spatialBlend = config.spatialBlend;
        audioSource.dopplerLevel = config.dopplerLevel;
        audioSource.rolloffMode  = config.rolloffMode;
        audioSource.minDistance  = minDistance;
        audioSource.maxDistance  = maxDistance;

        if (playOnActivate && !audioSource.isPlaying)
        {
            audioSource.Play();
        }
    }

    /// <summary>
    /// Desactiva el emisor y detiene la reproducción.
    /// Llamado por <see cref="AudioManager"/> cuando el nivel está por debajo del mínimo.
    /// </summary>
    public void Deactivate()
    {
        isActive = false;

        if (audioSource.isPlaying)
        {
            audioSource.Stop();
        }
    }

    /// <summary>
    /// Permite cambiar el clip de audio en tiempo de ejecución (útil para lógica dinámica,
    /// por ejemplo, cambiar el canal de TV). Solo funciona si el emitter está activo.
    /// </summary>
    public void SetClip(AudioClip newClip, bool restartPlayback = true)
    {
        audioClip = newClip;
        audioSource.clip = newClip;

        if (restartPlayback && isActive && playOnActivate)
        {
            audioSource.Stop();
            audioSource.Play();
        }
    }

    /// <summary>
    /// Configura el emisor en tiempo de ejecucion o en el momento del spawn.
    /// Llamado por CorridorRoomsGenerator cuando inyecta el componente en un mueble instanciado.
    /// </summary>
    public void Configure(
        SoundEmitterType type,
        AudioClip clip,
        float volume,
        int immersionLevel,
        float minDist,
        float maxDist,
        int assignedRoomId = -1)
    {
        emitterType       = type;
        audioClip         = clip;
        baseVolume        = Mathf.Clamp01(volume);
        minImmersionLevel = Mathf.Clamp(immersionLevel, 1, 6);
        minDistance       = Mathf.Max(0f, minDist);
        maxDistance       = Mathf.Max(0f, maxDist);
        roomId            = assignedRoomId;

        if (audioSource != null)
        {
            ConfigureAudioSourceDefaults();
        }
    }

    /// <summary>
    /// Modifica el volumen base del emisor en tiempo de ejecucion.
    /// El volumen real resultante sigue siendo escalado por el nivel de inmersion.
    /// </summary>
    public void SetBaseVolume(float volume)
    {
        baseVolume = Mathf.Clamp01(volume);

        // Refrescar el volumen real si el AudioManager tiene config disponible
        if (AudioManager.Instance != null)
        {
            var config = AudioManager.Instance.GetLevelConfig(AudioManager.Instance.CurrentImmersionLevel);
            if (config != null && isActive)
            {
                audioSource.volume = baseVolume * config.globalVolume;
            }
        }
    }

    // ─────────────────────────────────────────────
    //  Inicialización del AudioSource
    // ─────────────────────────────────────────────
    private void ConfigureAudioSourceDefaults()
    {
        audioSource.clip        = audioClip;
        audioSource.loop        = loop;
        audioSource.volume      = baseVolume;
        audioSource.playOnAwake = false;    // El AudioManager decide cuándo reproducir
        audioSource.spatialBlend = 0f;      // Empieza en 2D; el nivel de inmersión lo ajusta
        audioSource.minDistance  = minDistance;
        audioSource.maxDistance  = maxDistance;
        audioSource.Stop();
    }

    // ─────────────────────────────────────────────
    //  Gizmos de editor
    // ─────────────────────────────────────────────
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Esfera de distancia máxima (semi-transparente)
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.12f);
        Gizmos.DrawSphere(transform.position, maxDistance);

        // Borde de distancia máxima
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, maxDistance);

        // Distancia mínima (zona de audio completo)
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, minDistance);

        // Etiqueta con info del emisor
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * (maxDistance * 0.15f + 0.5f),
            $"[{emitterType}] min={minDistance}m  max={maxDistance}m\nMinLevel={minImmersionLevel}");
    }
#endif
}
