using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Audio;
 
namespace Vortices
{
     
    /// <summary>
    /// AudioManager: Controla los niveles de configuración auditiva según el modelo definido en el
    /// Prototipo P003 de MOTIONS. Gestiona todos los SoundEmitters registrados en la escena.
    ///
    /// NIVELES DE INMERSIÓN AUDITIVA (basados en MOTIONS P003):
    ///   Nivel 0 — Sin audio (silencio total)
    ///   Nivel 1 — Mono, 22050 Hz, sin 3D
    ///   Nivel 2 — Mono, 32000 Hz, sin 3D
    ///   Nivel 3 — Stereo, 44100 Hz, 3D parcial
    ///   Nivel 4 — Stereo, 48000 Hz, 3D completo
    ///   Nivel 5 — Stereo, 88200 Hz, 3D + Doppler + HRTF
    ///   Nivel 6 — Stereo, 96000 Hz, 3D + Doppler máximo + HRTF
    ///
    /// HRTF (Head-Related Transfer Function):
    ///   Activar spatialize = true en niveles 5 y 6 habilita el Oculus Spatializer,
    ///   que modela cómo el oído y la cabeza afectan la percepción direccional del sonido.
    ///   Requiere que el plugin "Oculus Spatializer" esté instalado en el proyecto.
    ///
    /// NOTA sobre Mono/Stereo: Unity no permite cambiar el modo Stereo de un AudioClip en
    /// tiempo de ejecución. Para aprovechar esa configuración, los clips deben importarse
    /// con el ajuste correcto en sus Import Settings (Force To Mono activado o desactivado).
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        // ─────────────────────────────────────────────
        //  Singleton
        // ─────────────────────────────────────────────
        public static AudioManager Instance { get; private set; }
     
        // ─────────────────────────────────────────────
        //  Configuración de un nivel de configuración
        // ─────────────────────────────────────────────
        [System.Serializable]
        public class AcousticProfile
        {
            [Tooltip("Nombre descriptivo del nivel (solo referencia).")]
            public string levelName = "Nivel";
     
            [Tooltip("Modo Stereo (true) o Mono (false). Informativo: ver comentario de clase.")]
            public bool stereo = false;
     
            [Tooltip("Volumen global aplicado a todos los emitters activos en este nivel.")]
            [Range(0f, 1f)]
            public float globalVolume = 1f;
     
            [Tooltip("Factor de mezcla 3D (0 = 2D plano, 1 = 3D espacial completo).")]
            [Range(0f, 1f)]
            public float spatialBlend = 0f;
     
                [Tooltip("Apertura del campo estéreo.\n" +
                     "0   = muy direccional (el sonido se oye solo por el oído más cercano a la fuente).\n" +
                     "180 = semi-omnidireccional.\n" +
                     "360 = igual en ambos oídos sin importar la posición.")]
            [Range(0f, 360f)]
            public float spread = 0f;
     
            [Tooltip("Multiplicador del efecto Doppler (0 = desactivado).")]
            [Range(0f, 5f)]
            public float dopplerLevel = 0f;
     
            [Tooltip("Tipo de curva de rolloff para atenuación con la distancia.")]
            public AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;
     
                [Tooltip("Activa el Oculus Spatializer (HRTF) para percepción direccional precisa.\n" +
                     "Requiere el plugin 'Oculus Spatializer' instalado en el proyecto.\n" +
                     "Recomendado solo en niveles altos (5–6) por su coste computacional.")]
            public bool spatialize = false;
     
                [Tooltip("Curva de rolloff personalizada. Solo se usa cuando rolloffMode = Custom.\n" +
                     "Eje X = distancia normalizada (0 = minDistance, 1 = maxDistance).\n" +
                     "Eje Y = volumen (0 = silencio total, 1 = volumen completo).")]
            public AnimationCurve customRolloffCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        }
     
        // ─────────────────────────────────────────────
        //  Inspector
        // ─────────────────────────────────────────────
        [Header("Nivel de Configuración Acústica")]
        [Tooltip("Nivel activo al iniciar la escena (0 = sin audio).")]
        [SerializeField, Range(0, 6)]
        private int initialConfigLevel = 0;
     
        [Tooltip("Configuraciones de cada nivel de configuración. Índice 0 = Nivel 1, índice 1 = Nivel 2, etc.")]
        [SerializeField]
        public  List<AcousticProfile> acousticProfiles = new List<AcousticProfile>();
     
        [Header("Comportamiento")]
        [Tooltip("Si está activo, busca y registra todos los SoundEmitters en la escena al Start.")]
        [SerializeField]
        private bool autoDiscoverEmitters = true;
     
        [Tooltip("Si está activo, este GameObject no se destruye al cargar una nueva escena.")]
        [SerializeField]
        private bool persistAcrossScenes = false;
     
        [Header("Clips de Compatibilidad (MuseumElement)")]
        [SerializeField] private AudioClip objectSelectedClip;
        [SerializeField] private AudioClip objectDeselectedClip;
        [SerializeField] private AudioMixerGroup outputMixerGroup;
        [SerializeField, Min(0.01f)] private float oneShotMinDistance = 0.5f;
        [SerializeField, Min(0.01f)] private float oneShotMaxDistance = 12f;
     
        // ─────────────────────────────────────────────
        //  Estado interno
        // ─────────────────────────────────────────────
        private readonly List<SoundEmitter> registeredEmitters = new List<SoundEmitter>();
        private int currentLevel = 0;
        private AudioRoomFilter roomFilter;
     
        /// <summary>Nivel de configuración auditiva actualmente aplicado (0–6).</summary>
        public int CurrentConfigLevel => currentLevel;
     
        // ─────────────────────────────────────────────
        //  Unity
        // ─────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[AudioManager] Ya existe una instancia. Destruyendo duplicado.", this);
                Destroy(gameObject);
                return;
            }
     
            Instance = this;
     
            if (persistAcrossScenes)
                DontDestroyOnLoad(gameObject);

            roomFilter = GetComponent<AudioRoomFilter>();
            EnsureDefaultLevels();
        }
     
        private void Start()
        {
            if (autoDiscoverEmitters)
                DiscoverAndRegisterAll();
     
            SetConfigLevel(initialConfigLevel);
        }
     
        // ─────────────────────────────────────────────
        //  API pública
        // ─────────────────────────────────────────────
     
        /// <summary>Cambia el nivel de configuración y lo aplica a todos los emitters registrados.</summary>
        public void SetConfigLevel(int level)
        {
            int clamped = NormalizeLevel(level);
            if (clamped == currentLevel) return;
     
            currentLevel = clamped;
            ApplyLevel(currentLevel);
            Debug.Log($"[AudioManager] Nivel → {currentLevel}" +
                      (currentLevel > 0 ? $" ({acousticProfiles[currentLevel - 1].levelName})" : " (Silencio)"));
        }
     
        /// <summary>Registra un SoundEmitter. Llamado automáticamente por cada emitter en Start().</summary>
        public void RegisterEmitter(SoundEmitter emitter)
        {
            if (emitter == null || registeredEmitters.Contains(emitter)) return;
     
            registeredEmitters.Add(emitter);
            ApplyConfigToEmitter(emitter, currentLevel);
        }
     
        /// <summary>Desregistra un SoundEmitter. Llamado en OnDestroy del emitter.</summary>
        public void UnregisterEmitter(SoundEmitter emitter)
        {
            registeredEmitters.Remove(emitter);
        }
     
        /// <summary>Devuelve el primer SoundEmitter registrado con el soundId indicado. Null si no existe.</summary>
        public SoundEmitter GetEmitterById(string soundId)
        {
            if (string.IsNullOrWhiteSpace(soundId)) return null;
     
            foreach (SoundEmitter emitter in registeredEmitters)
            {
                if (emitter != null && emitter.SoundId == soundId)
                    return emitter;
            }
     
            return null;
        }
     
        /// <summary>Devuelve todos los SoundEmitters registrados con el soundId indicado.</summary>
        public List<SoundEmitter> GetAllEmittersById(string soundId)
        {
            List<SoundEmitter> result = new List<SoundEmitter>();
            if (string.IsNullOrWhiteSpace(soundId)) return result;
     
            foreach (SoundEmitter emitter in registeredEmitters)
            {
                if (emitter != null && emitter.SoundId == soundId)
                    result.Add(emitter);
            }
     
            return result;
        }
     
        /// <summary>Desactiva todos los emitters con el soundId indicado.</summary>
        public void DeactivateEmitterById(string soundId)
        {
            foreach (SoundEmitter emitter in GetAllEmittersById(soundId))
                emitter.Deactivate();
        }
    
        /// <summary>Devuelve la configuración del nivel indicado (0 devuelve null).</summary>
        public AcousticProfile GetAcousticProfile(int level)
        {
            int normalized = NormalizeLevel(level);
            if (normalized <= 0) return null;
            return acousticProfiles[normalized - 1];
        }

        /// <summary>Aplica valores personalizados al perfil del nivel indicado y reaaplica el nivel actual.</summary>
        public void ApplyProfileOverride(int level, ProfileOverrideData ovr)
        {
            var profile = GetAcousticProfile(level);
            if (profile == null) return;
            profile.spatialBlend = ovr.spatialBlend;
            profile.spread       = ovr.spread;
            profile.dopplerLevel = ovr.dopplerLevel;
            profile.rolloffMode  = (AudioRolloffMode)ovr.rolloffMode;
            profile.spatialize   = ovr.spatialize;
            ApplyLevel(currentLevel);
        }

        [System.Serializable]
        public struct ProfileOverrideData
        {
            public float spatialBlend;
            public float spread;
            public float dopplerLevel;
            public int   rolloffMode;
            public bool  spatialize;
        }
     
        /// <summary>Compatibilidad con MuseumElement.</summary>
        public void PlayObjectSelectedSound(Vector3 position)   => PlayCompatibilityOneShot(objectSelectedClip, position);
     
        /// <summary>Compatibilidad con MuseumElement.</summary>
        public void PlayObjectDeselectedSound(Vector3 position) => PlayCompatibilityOneShot(objectDeselectedClip, position);
     
        // ─────────────────────────────────────────────
        //  Lógica interna
        // ─────────────────────────────────────────────
     
        private void ApplyLevel(int level)
        {
            currentLevel = NormalizeLevel(level);
            registeredEmitters.RemoveAll(e => e == null);
     
            foreach (SoundEmitter emitter in registeredEmitters)
                ApplyConfigToEmitter(emitter, currentLevel);
        }
     
        private void ApplyConfigToEmitter(SoundEmitter emitter, int level)
        {
            int normalized = NormalizeLevel(level);
     
            if (normalized <= 0)
            {
                emitter.Deactivate();
                return;
            }
     
            AcousticProfile config = acousticProfiles[normalized - 1];
            bool shouldBeActive = normalized >= emitter.MinConfigLevel && IsRoomEnabledForEmitter(emitter);
     
            if (shouldBeActive)
                emitter.Activate(config);
            else
                emitter.Deactivate();
        }
     
        private int NormalizeLevel(int level)
        {
            int max = acousticProfiles != null ? acousticProfiles.Count : 0;
            return Mathf.Clamp(level, 0, max);
        }
     
        private bool IsRoomEnabledForEmitter(SoundEmitter emitter)
        {
            if (roomFilter == null) return true;
            return roomFilter.IsRoomEnabled(emitter);
        }

        /// <summary>Habilita todas las salas de una vez. Delega en AudioRoomFilter si está presente.</summary>
        public void EnableAllRooms()
        {
            roomFilter?.EnableAllRooms();
            ApplyLevel(currentLevel);
        }

        /// <summary>Habilita solo las salas indicadas. Delega en AudioRoomFilter si está presente.</summary>
        public void SetEnabledRooms(List<int> roomIds)
        {
            roomFilter?.SetEnabledRooms(roomIds);
            ApplyLevel(currentLevel);
        }

        /// <summary>Activa o desactiva el filtro por sala. Delega en AudioRoomFilter si está presente.</summary>
        public void SetRoomFilterEnabled(bool enabled)
        {
            roomFilter?.SetRoomFilterEnabled(enabled);
            ApplyLevel(currentLevel);
        }

        /// <summary>Agrega una sala a la lista de habilitadas en runtime.</summary>
        public void EnableRoom(int roomId)
        {
            roomFilter?.EnableRoom(roomId);
            ApplyLevel(currentLevel);
        }

        /// <summary>Quita una sala de la lista de habilitadas en runtime.</summary>
        public void DisableRoom(int roomId)
        {
            roomFilter?.DisableRoom(roomId);
            ApplyLevel(currentLevel);
        }
     
        private void DiscoverAndRegisterAll()
        {
            SoundEmitter[] found = FindObjectsOfType<SoundEmitter>(includeInactive: true);
            foreach (SoundEmitter emitter in found)
                RegisterEmitter(emitter);
     
            Debug.Log($"[AudioManager] Emitters descubiertos: {found.Length}");
        }
     
        private void PlayCompatibilityOneShot(AudioClip clip, Vector3 position)
        {
            if (clip == null || currentLevel <= 0) return;
     
            AcousticProfile config = GetAcousticProfile(currentLevel);
            if (config == null) return;
     
            GameObject obj = new GameObject($"{clip.name}_OneShot");
            obj.transform.position = position;
     
            AudioSource src = obj.AddComponent<AudioSource>();
            src.clip              = clip;
            src.outputAudioMixerGroup = outputMixerGroup;
            src.playOnAwake       = false;
            src.loop              = false;
            src.volume            = config.globalVolume;
            src.spatialBlend      = config.spatialBlend;
            src.spread            = config.spread;
            src.dopplerLevel      = config.dopplerLevel;
            src.rolloffMode       = config.rolloffMode;
            src.spatialize        = config.spatialize;
            src.minDistance       = oneShotMinDistance;
            src.maxDistance       = oneShotMaxDistance;
     
            src.Play();
            Destroy(obj, clip.length + 0.1f);
        }
     
        /// <summary>
        /// Popula la lista con los 6 niveles por defecto si está vacía,
        /// siguiendo el modelo del Prototipo P003 de MOTIONS.
        /// spread: 0 = muy direccional (ideal niveles altos); 180 = menos direccional.
        /// spatialize: true activa HRTF en niveles 5 y 6.
        /// </summary>
        private void EnsureDefaultLevels()
        {
            if (acousticProfiles.Count > 0) return;
     
            acousticProfiles.Add(new AcousticProfile
            {
                levelName    = "Nivel 1 · Mono 22050 Hz",
                stereo       = false,
                globalVolume = 0.6f,
                spatialBlend = 0f,
                spread       = 180f,
                dopplerLevel = 0f,
                rolloffMode  = AudioRolloffMode.Linear,
                spatialize   = false
            });
            acousticProfiles.Add(new AcousticProfile
            {
                levelName    = "Nivel 2 · Mono 32000 Hz",
                stereo       = false,
                globalVolume = 0.7f,
                spatialBlend = 0f,
                spread       = 180f,
                dopplerLevel = 0f,
                rolloffMode  = AudioRolloffMode.Linear,
                spatialize   = false
            });
            acousticProfiles.Add(new AcousticProfile
            {
                levelName    = "Nivel 3 · Stereo 44100 Hz",
                stereo       = true,
                globalVolume = 0.8f,
                spatialBlend = 0.4f,
                spread       = 90f,
                dopplerLevel = 0f,
                rolloffMode  = AudioRolloffMode.Logarithmic,
                spatialize   = false
            });
            acousticProfiles.Add(new AcousticProfile
            {
                levelName    = "Nivel 4 · Stereo 48000 Hz 3D",
                stereo       = true,
                globalVolume = 0.9f,
                spatialBlend = 1f,
                spread       = 30f,
                dopplerLevel = 0f,
                rolloffMode  = AudioRolloffMode.Logarithmic,
                spatialize   = false
            });
            acousticProfiles.Add(new AcousticProfile
            {
                levelName    = "Nivel 5 · Stereo 88200 Hz 3D + Doppler + HRTF",
                stereo       = true,
                globalVolume = 1f,
                spatialBlend = 1f,
                spread       = 0f,
                dopplerLevel = 1f,
                rolloffMode  = AudioRolloffMode.Logarithmic,
                spatialize   = true
            });
            acousticProfiles.Add(new AcousticProfile
            {
                levelName    = "Nivel 6 · Stereo 96000 Hz 3D + Doppler máx + HRTF",
                stereo       = true,
                globalVolume = 1f,
                spatialBlend = 1f,
                spread       = 0f,
                dopplerLevel = 3f,
                rolloffMode  = AudioRolloffMode.Logarithmic,
                spatialize   = true
            });
        }
     
    #if UNITY_EDITOR
        [ContextMenu("Aplicar nivel actual (Editor)")]
        private void EditorApplyCurrentLevel()
        {
            DiscoverAndRegisterAll();
            ApplyLevel(initialConfigLevel);
            Debug.Log($"[AudioManager] Nivel {initialConfigLevel} aplicado desde el Editor.");
        }
     
        [ContextMenu("Listar emitters registrados")]
        private void EditorListEmitters()
        {
            Debug.Log($"[AudioManager] {registeredEmitters.Count} emitters registrados:");
            foreach (var e in registeredEmitters)
            {
                if (e != null)
                    Debug.Log($"  → {e.name}  tipo={e.EmitterType}  minLevel={e.MinConfigLevel}");
            }
        }
    #endif
    }
}