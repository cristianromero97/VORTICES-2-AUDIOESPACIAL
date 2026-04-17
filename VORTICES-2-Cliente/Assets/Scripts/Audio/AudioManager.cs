using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Audio;

/// <summary>
/// AudioManager: Controla los niveles de inmersión auditiva según el modelo definido en el
/// Prototipo P003 de MOTIONS. Gestiona todos los SoundEmitters registrados en la escena.
///
/// NIVELES DE INMERSIÓN AUDITIVA (basados en MOTIONS P003):
///   Nivel 0 — Sin audio (silencio total)
///   Nivel 1 — Mono, 22050 Hz, sin 3D
///   Nivel 2 — Mono, 32000 Hz, sin 3D
///   Nivel 3 — Stereo, 44100 Hz, 3D parcial
///   Nivel 4 — Stereo, 48000 Hz, 3D completo
///   Nivel 5 — Stereo, 88200 Hz, 3D completo + Doppler
///   Nivel 6 — Stereo, 96000 Hz, 3D completo + Doppler máximo
///
/// NOTA sobre Mono/Stereo: Unity no permite cambiar el modo Stereo de un AudioClip en
/// tiempo de ejecución. Para aprovechar esa configuración, los clips deben importarse
/// con el ajuste correcto en sus Import Settings (Force To Mono activado o desactivado).
/// El AudioManager comunica esta intención a través del campo <c>stereo</c> de cada nivel,
/// que puede usarse para lógica adicional (e.g., elegir un clip alternativo).
/// </summary>
public class AudioManager : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Singleton
    // ─────────────────────────────────────────────
    public static AudioManager Instance { get; private set; }

    // ─────────────────────────────────────────────
    //  Configuración de un nivel de inmersión
    // ─────────────────────────────────────────────
    [System.Serializable]
    public class ImmersionLevelConfig
    {
        [Tooltip("Nombre descriptivo del nivel (solo referencia)")]
        public string levelName = "Nivel";

        [Tooltip("Modo Stereo (true) o Mono (false). Informativo: ver comentario de clase.")]
        public bool stereo = false;

        [Tooltip("Volumen global aplicado a todos los emitters activos en este nivel.")]
        [Range(0f, 1f)]
        public float globalVolume = 1f;

        [Tooltip("Factor de mezcla 3D (0 = 2D plano, 1 = 3D espacial completo).")]
        [Range(0f, 1f)]
        public float spatialBlend = 0f;

        [Tooltip("Multiplicador del efecto Doppler (0 = desactivado).")]
        [Range(0f, 5f)]
        public float dopplerLevel = 0f;

        [Tooltip("Tipo de curva de rolloff para atenuación con la distancia.")]
        public AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;
    }

    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────
    [Header("Nivel de Inmersión Auditiva")]
    [Tooltip("Nivel activo al iniciar la escena (0 = sin audio).")]
    [SerializeField, Range(0, 6)]
    private int initialImmersionLevel = 0;

    [Tooltip("Configuraciones de cada nivel de inmersión. El índice 0 = Nivel 1, índice 1 = Nivel 2, etc.")]
    [SerializeField]
    private List<ImmersionLevelConfig> immersionLevels = new List<ImmersionLevelConfig>();

    [Header("Comportamiento")]
    [Tooltip("Si está activo, busca y registra todos los SoundEmitters en la escena al Start.")]
    [SerializeField]
    private bool autoDiscoverEmitters = true;

    [Tooltip("Si está activo, este GameObject no se destruye al cargar una nueva escena.")]
    [SerializeField]
    private bool persistAcrossScenes = false;

    [Header("Clips de Compatibilidad")]
    [Tooltip("Clip usado por MuseumElement al seleccionar un objeto.")]
    [SerializeField] private AudioClip objectSelectedClip;

    [Tooltip("Clip usado por MuseumElement al deseleccionar un objeto.")]
    [SerializeField] private AudioClip objectDeselectedClip;

    [Tooltip("Mixer opcional para enrutar estos one-shots de compatibilidad.")]
    [SerializeField] private AudioMixerGroup outputMixerGroup;

    [SerializeField, Min(0.01f)] private float oneShotMinDistance = 0.5f;
    [SerializeField, Min(0.01f)] private float oneShotMaxDistance = 12f;

    [Header("Filtro por Sala (Opcional)")]
    [Tooltip("Si está activo, solo sonarán emitters cuyos RoomId estén en la lista permitida.")]
    [SerializeField] private bool filterEmittersByRoomId = false;

    [Tooltip("IDs de sala permitidos cuando el filtro está activo (por ejemplo: 1, 3, 7).")]
    [SerializeField] private List<int> enabledRoomIds = new List<int>();

    // ─────────────────────────────────────────────
    //  Estado interno
    // ─────────────────────────────────────────────
    private readonly List<SoundEmitter> registeredEmitters = new List<SoundEmitter>();
    private int currentLevel = 0;

    /// <summary>Nivel de inmersión auditiva actualmente aplicado (0–6).</summary>
    public int CurrentImmersionLevel => currentLevel;

    // ─────────────────────────────────────────────
    //  Unity
    // ─────────────────────────────────────────────
    private void Awake()
    {
        // Patrón Singleton
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[AudioManager] Ya existe una instancia. Destruyendo duplicado.", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (persistAcrossScenes)
        {
            DontDestroyOnLoad(gameObject);
        }

        EnsureDefaultLevels();
    }

    private void Start()
    {
        if (autoDiscoverEmitters)
        {
            DiscoverAndRegisterAll();
        }

        SetImmersionLevel(initialImmersionLevel);
    }

    // ─────────────────────────────────────────────
    //  API pública
    // ─────────────────────────────────────────────

    /// <summary>
    /// Cambia el nivel de inmersión auditiva y lo aplica a todos los emitters registrados.
    /// </summary>
    /// <param name="level">0 = silencio total; 1–6 = niveles crecientes de calidad.</param>
    public void SetImmersionLevel(int level)
    {
        int clamped = NormalizeLevel(level);
        if (clamped == currentLevel)
        {
            return;
        }

        currentLevel = clamped;
        ApplyLevel(currentLevel);
        Debug.Log($"[AudioManager] Nivel de inmersión auditiva cambiado a: {currentLevel}" +
                  (currentLevel > 0 ? $" ({immersionLevels[currentLevel - 1].levelName})" : " (Silencio)"));
    }

    /// <summary>
    /// Registra un SoundEmitter para que el AudioManager pueda gestionarlo.
    /// Llamado automáticamente por cada SoundEmitter en su Start().
    /// </summary>
    public void RegisterEmitter(SoundEmitter emitter)
    {
        if (emitter == null || registeredEmitters.Contains(emitter))
        {
            return;
        }

        registeredEmitters.Add(emitter);
        ApplyConfigToEmitter(emitter, currentLevel);
    }

    /// <summary>
    /// Desregistra un SoundEmitter (llamado en OnDestroy del emitter).
    /// </summary>
    public void UnregisterEmitter(SoundEmitter emitter)
    {
        registeredEmitters.Remove(emitter);
    }

    /// <summary>
    /// Devuelve la configuración de un nivel específico (null si nivel = 0).
    /// </summary>
    public ImmersionLevelConfig GetLevelConfig(int level)
    {
        int normalized = NormalizeLevel(level);
        if (normalized <= 0)
        {
            return null;
        }

        return immersionLevels[normalized - 1];
    }

    /// <summary>
    /// Método de compatibilidad para llamadas existentes desde MuseumElement.
    /// </summary>
    public void PlayObjectSelectedSound(Vector3 position)
    {
        PlayCompatibilityOneShot(objectSelectedClip, position);
    }

    /// <summary>
    /// Método de compatibilidad para llamadas existentes desde MuseumElement.
    /// </summary>
    public void PlayObjectDeselectedSound(Vector3 position)
    {
        PlayCompatibilityOneShot(objectDeselectedClip, position);
    }

    // ─────────────────────────────────────────────
    //  Lógica interna
    // ─────────────────────────────────────────────

    private void ApplyLevel(int level)
    {
        currentLevel = NormalizeLevel(level);

        // Limpiar emitters destruidos
        registeredEmitters.RemoveAll(e => e == null);

        foreach (SoundEmitter emitter in registeredEmitters)
        {
            ApplyConfigToEmitter(emitter, currentLevel);
        }
    }

    private void ApplyConfigToEmitter(SoundEmitter emitter, int level)
    {
        int normalized = NormalizeLevel(level);

        // Nivel 0 → silencio total, todos los emitters apagados
        if (normalized <= 0)
        {
            emitter.Deactivate();
            return;
        }

        ImmersionLevelConfig config = immersionLevels[normalized - 1];

        // Un emitter solo está activo si el nivel alcanzó su umbral mínimo
        bool shouldBeActive = normalized >= emitter.MinImmersionLevel;

        if (shouldBeActive && !IsRoomEnabledForEmitter(emitter))
        {
            shouldBeActive = false;
        }

        if (shouldBeActive)
        {
            emitter.Activate(config);
        }
        else
        {
            emitter.Deactivate();
        }
    }

    private int NormalizeLevel(int level)
    {
        int maxAvailableLevel = immersionLevels != null ? immersionLevels.Count : 0;
        return Mathf.Clamp(level, 0, maxAvailableLevel);
    }

    private bool IsRoomEnabledForEmitter(SoundEmitter emitter)
    {
        if (!filterEmittersByRoomId)
        {
            return true;
        }

        if (emitter == null)
        {
            return false;
        }

        int emitterRoomId = emitter.RoomId;
        if (emitterRoomId <= 0)
        {
            return false;
        }

        return enabledRoomIds != null && enabledRoomIds.Contains(emitterRoomId);
    }

    private void DiscoverAndRegisterAll()
    {
        SoundEmitter[] found = FindObjectsOfType<SoundEmitter>(includeInactive: true);
        foreach (SoundEmitter emitter in found)
        {
            RegisterEmitter(emitter);
        }

        Debug.Log($"[AudioManager] Emitters descubiertos: {found.Length}");
    }

    private void PlayCompatibilityOneShot(AudioClip clip, Vector3 position)
    {
        if (clip == null)
        {
            return;
        }

        if (currentLevel <= 0)
        {
            return;
        }

        ImmersionLevelConfig config = GetLevelConfig(currentLevel);
        if (config == null)
        {
            return;
        }

        GameObject oneShotObject = new GameObject($"{clip.name}_OneShot");
        oneShotObject.transform.position = position;

        AudioSource source = oneShotObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.outputAudioMixerGroup = outputMixerGroup;
        source.playOnAwake = false;
        source.loop = false;
        source.volume = config.globalVolume;
        source.spatialBlend = config.spatialBlend;
        source.dopplerLevel = config.dopplerLevel;
        source.rolloffMode = config.rolloffMode;
        source.minDistance = oneShotMinDistance;
        source.maxDistance = oneShotMaxDistance;

        source.Play();
        Destroy(oneShotObject, clip.length + 0.1f);
    }

    /// <summary>
    /// Popula la lista con los 6 niveles por defecto si está vacía,
    /// siguiendo el modelo del Prototipo P003 de MOTIONS.
    /// </summary>
    private void EnsureDefaultLevels()
    {
        if (immersionLevels.Count > 0)
        {
            return;
        }

        immersionLevels.Add(new ImmersionLevelConfig
        {
            levelName    = "Nivel 1 · Mono 22050 Hz",
            stereo       = false,
            globalVolume = 0.6f,
            spatialBlend = 0f,
            dopplerLevel = 0f,
            rolloffMode  = AudioRolloffMode.Linear
        });
        immersionLevels.Add(new ImmersionLevelConfig
        {
            levelName    = "Nivel 2 · Mono 32000 Hz",
            stereo       = false,
            globalVolume = 0.7f,
            spatialBlend = 0f,
            dopplerLevel = 0f,
            rolloffMode  = AudioRolloffMode.Linear
        });
        immersionLevels.Add(new ImmersionLevelConfig
        {
            levelName    = "Nivel 3 · Stereo 44100 Hz",
            stereo       = true,
            globalVolume = 0.8f,
            spatialBlend = 0.4f,
            dopplerLevel = 0f,
            rolloffMode  = AudioRolloffMode.Logarithmic
        });
        immersionLevels.Add(new ImmersionLevelConfig
        {
            levelName    = "Nivel 4 · Stereo 48000 Hz 3D",
            stereo       = true,
            globalVolume = 0.9f,
            spatialBlend = 1f,
            dopplerLevel = 0f,
            rolloffMode  = AudioRolloffMode.Logarithmic
        });
        immersionLevels.Add(new ImmersionLevelConfig
        {
            levelName    = "Nivel 5 · Stereo 88200 Hz 3D + Doppler",
            stereo       = true,
            globalVolume = 1f,
            spatialBlend = 1f,
            dopplerLevel = 1f,
            rolloffMode  = AudioRolloffMode.Logarithmic
        });
        immersionLevels.Add(new ImmersionLevelConfig
        {
            levelName    = "Nivel 6 · Stereo 96000 Hz 3D + Doppler máx",
            stereo       = true,
            globalVolume = 1f,
            spatialBlend = 1f,
            dopplerLevel = 3f,
            rolloffMode  = AudioRolloffMode.Logarithmic
        });
    }

#if UNITY_EDITOR
    // ─────────────────────────────────────────────
    //  Herramientas de edición
    // ─────────────────────────────────────────────
    [ContextMenu("Aplicar nivel actual (Editor)")]
    private void EditorApplyCurrentLevel()
    {
        DiscoverAndRegisterAll();
        ApplyLevel(initialImmersionLevel);
        Debug.Log($"[AudioManager] Nivel {initialImmersionLevel} aplicado desde el Editor.");
    }

    [ContextMenu("Listar emitters registrados")]
    private void EditorListEmitters()
    {
        Debug.Log($"[AudioManager] {registeredEmitters.Count} emitters registrados:");
        foreach (var e in registeredEmitters)
        {
            if (e != null)
            {
                Debug.Log($"  → {e.name}  tipo={e.EmitterType}  minLevel={e.MinImmersionLevel}");
            }
        }
    }
#endif
}
