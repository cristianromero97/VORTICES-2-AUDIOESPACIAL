using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SimpleFileBrowser;

/// <summary>
/// Pasos del panel de configuración de audio espacial.
/// </summary>
enum AudioSpatialId
{
    BrowsingLocal   = 0,   // Selección de archivos de audio
    FileBrowser     = 1,   // Diálogo SimpleFileBrowser (runtime)
    ImmersionConfig = 2,   // Nivel de inmersión (configLevel 1–6) + sliders acústicos
    AudioConfig     = 3,   // Volumen, minConfigLevel, distancias min/max
}

namespace Vortices
{
    /// <summary>
    /// Panel de configuración de Audio Espacial accesible desde el Main Menu (Options).
    /// Permite cargar archivos de audio y configurar los parámetros acústicos
    /// antes de lanzar cualquier entorno (Museum, Circular, etc.).
    ///
    /// FLUJO:
    ///   BrowsingLocal → ImmersionConfig → AudioConfig → vuelve a Options
    ///
    /// Step 2 y Step 3 replican exactamente los Steps 4 y 5 de SalaPanel,
    /// con la única excepción de que no hay AudioManager en la escena del Main Menu,
    /// por lo que siempre se usan los valores por defecto (DefaultLevelNames, etc.).
    /// Room Filter tampoco aplica aquí (es específico de Sala Environment).
    /// </summary>
    public class AudioSpatialPanel : SpawnPanel
    {
        // ─────────────────────────────────────────────
        //  Valores por defecto de los 6 niveles
        //  (igual que SalaPanel / AudioManager.EnsureDefaultLevels)
        // ─────────────────────────────────────────────

        private static readonly string[] DefaultLevelNames = {
            "Nivel 1 · Mono 22050 Hz",
            "Nivel 2 · Mono 32000 Hz",
            "Nivel 3 · Stereo 44100 Hz",
            "Nivel 4 · Stereo 48000 Hz 3D",
            "Nivel 5 · Stereo 88200 Hz 3D + Doppler + HRTF",
            "Nivel 6 · Stereo 96000 Hz 3D + Doppler máx + HRTF"
        };
        private static readonly float[] DefaultSpatialBlend = { 0f,   0f,   0.4f, 1f,  1f,  1f   };
        private static readonly float[] DefaultSpread       = { 180f, 180f, 90f,  30f, 0f,  0f   };
        private static readonly float[] DefaultDoppler      = { 0f,   0f,   0f,   0f,  1f,  3f   };
        private static readonly int[]   DefaultRolloff      = { 1,    1,    0,    0,   2,   2    }; // 0=Logarithmic,1=Linear,2=Custom
        private static readonly bool[]  DefaultSpatialize   = { false,false,false,false,true,true };

        // ─────────────────────────────────────────────
        //  Inspector — Step 2: Immersion Config
        //  (igual que SalaPanel Step 4)
        // ─────────────────────────────────────────────

        [Header("Step 2 - Immersion Config")]
        [SerializeField] private TMP_Dropdown    configLevelDropdown;
        [SerializeField] private Slider          spatialBlendSlider;
        [SerializeField] private Slider          spreadSlider;
        [SerializeField] private Slider          dopplerSlider;
        [SerializeField] private TMP_Dropdown    rolloffModeDropdown;
        [SerializeField] private Toggle          spatializeToggle;
        [SerializeField] private TextMeshProUGUI spatialBlendText;
        [SerializeField] private TextMeshProUGUI spreadText;
        [SerializeField] private TextMeshProUGUI dopplerText;

        // ─────────────────────────────────────────────
        //  Inspector — Step 3: Audio Config
        //  (igual que SalaPanel Step 5, sin Room Filter)
        // ─────────────────────────────────────────────

        [Header("Step 3 - Audio Config")]
        [SerializeField] private Slider          baseVolumeSlider;
        [SerializeField] private TextMeshProUGUI baseVolumeText;
        [SerializeField] private TMP_InputField  minConfigLevelInput;
        [SerializeField] private TMP_InputField  minDistanceInput;
        [SerializeField] private TMP_InputField  maxDistanceInput;

        // ─────────────────────────────────────────────
        //  Inspector — Navegación
        // ─────────────────────────────────────────────

        [Header("Navigation")]
        [SerializeField] private GameObject optionsPanel;

        // ─────────────────────────────────────────────
        //  Estado interno
        // ─────────────────────────────────────────────

        private int   _configLevel         = 4;
        private float _baseVolume          = 1f;
        private int   _emitterMinConfigLevel = 2;
        private float _minDistance         = 1f;
        private float _maxDistance         = 12f;

        private HandKeyboard _handKeyboard;
        private Coroutine    _removeKeyboardCoroutine;

        // ─────────────────────────────────────────────
        //  Unity lifecycle
        // ─────────────────────────────────────────────

        private void OnEnable()
        {
            sessionManager = GameObject.Find("SessionManager")?.GetComponent<SessionManager>();

            // Mostrar solo el Step 0 al entrar (loop inverso para evitar bug de activación)
            if (uiComponents != null)
            {
                for (int i = uiComponents.Count - 1; i >= 0; i--)
                {
                    if (uiComponents[i] == null) continue;
                    bool active = (i == 0);
                    uiComponents[i].SetActive(active);
                    CanvasGroup cg = uiComponents[i].GetComponent<CanvasGroup>();
                    if (cg != null) { cg.alpha = active ? 1f : 0f; cg.blocksRaycasts = active; }
                }
                actualComponentId = 0;
            }

            SetupImmersionDefaults();
            SetupAudioConfigDefaults();
            FixDropdownForVR(configLevelDropdown);
            FixDropdownForVR(rolloffModeDropdown);

            _handKeyboard = GameObject.Find("Keyboard Canvas")?.GetComponent<HandKeyboard>();
            WireKeyboard(minConfigLevelInput);
            WireKeyboard(minDistanceInput);
            WireKeyboard(maxDistanceInput);
        }

        private void OnDisable()
        {
            // Si el usuario navegó fuera con el browser abierto, forzar cierre
            if (FileBrowser.IsOpen)
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
                GameObject browserCanvas = GameObject.Find("SimpleFileBrowserCanvas(Clone)");
                if (browserCanvas != null) Destroy(browserCanvas);
                // Volver al step de browsing para que el estado sea consistente
                ChangeVisibleComponent((int)AudioSpatialId.BrowsingLocal);
            }

            if (_removeKeyboardCoroutine != null) StopCoroutine(_removeKeyboardCoroutine);
            _handKeyboard?.RemoveInputField();
            ClearKeyboard(minConfigLevelInput);
            ClearKeyboard(minDistanceInput);
            ClearKeyboard(maxDistanceInput);
            _handKeyboard = null;
        }

        // ─────────────────────────────────────────────
        //  Step 0 — File Browser
        // ─────────────────────────────────────────────

        public void OpenFileBrowserLocal()
        {
            AddBrowserToComponents();
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            FileBrowser.SetFilters(true,
                new FileBrowser.Filter("Audio", ".mp3", ".mp4", ".wav", ".ogg"));

            FileBrowser.ShowLoadDialog(
                paths =>
                {
                    Cursor.visible = false;
                    Cursor.lockState = CursorLockMode.Locked;
                    optionFilePath.ClearPaths();
                    optionFilePath.GetFilePaths(paths);
                    optionFilePath.SetUIText();
                    RemoveBrowserFromComponents();
                    ChangeVisibleComponent((int)AudioSpatialId.BrowsingLocal);
                },
                () =>
                {
                    Cursor.visible = false;
                    Cursor.lockState = CursorLockMode.Locked;
                    RemoveBrowserFromComponents();
                    ChangeVisibleComponent((int)AudioSpatialId.BrowsingLocal);
                },
                FileBrowser.PickMode.FilesAndFolders,
                true,
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                null, "Select audio files", "Select");
        }

        public void AddBrowserToComponents()
        {
            GameObject browserCanvas = GameObject.Find("SimpleFileBrowserCanvas(Clone)");
            if (browserCanvas != null && uiComponents != null &&
                uiComponents.Count > (int)AudioSpatialId.FileBrowser)
            {
                uiComponents[(int)AudioSpatialId.FileBrowser] = browserCanvas;
                FileBrowser fb = browserCanvas.GetComponent<FileBrowser>();
                if (fb != null) fb.SetAsPersistent(false);
            }
        }

        public void RemoveBrowserFromComponents()
        {
            if (uiComponents == null || uiComponents.Count <= (int)AudioSpatialId.FileBrowser) return;
            uiComponents[(int)AudioSpatialId.FileBrowser]?.GetComponent<FileBrowser>()?.SetAsPersistent(true);
        }

        // ─────────────────────────────────────────────
        //  Step 2 — Immersion Config
        //  (replica SalaPanel.SetupStep4Defaults, sin AudioManager)
        // ─────────────────────────────────────────────

        private void SetupImmersionDefaults()
        {
            if (configLevelDropdown != null)
            {
                configLevelDropdown.ClearOptions();
                var options = new List<string>();
                for (int i = 0; i < DefaultLevelNames.Length; i++)
                    options.Add($"Level {i + 1} — {DefaultLevelNames[i]}");
                configLevelDropdown.AddOptions(options);
                configLevelDropdown.value = Mathf.Clamp(_configLevel - 1, 0, DefaultLevelNames.Length - 1);
                configLevelDropdown.onValueChanged.RemoveAllListeners();
                configLevelDropdown.onValueChanged.AddListener(v =>
                {
                    _configLevel = v + 1;
                    RefreshImmersionSliders();
                });
            }

            // Configurar rangos correctos de cada slider
            if (spatialBlendSlider != null) { spatialBlendSlider.minValue = 0f; spatialBlendSlider.maxValue = 1f;   }
            if (spreadSlider       != null) { spreadSlider.minValue       = 0f; spreadSlider.maxValue       = 360f; }
            if (dopplerSlider      != null) { dopplerSlider.minValue      = 0f; dopplerSlider.maxValue      = 5f;   }

            RefreshImmersionSliders();

            if (spatialBlendSlider != null) spatialBlendSlider.onValueChanged.AddListener(UpdateSpatialBlendText);
            if (spreadSlider       != null) spreadSlider.onValueChanged.AddListener(UpdateSpreadText);
            if (dopplerSlider      != null) dopplerSlider.onValueChanged.AddListener(UpdateDopplerText);
        }

        private void RefreshImmersionSliders()
        {
            int idx = Mathf.Clamp(_configLevel - 1, 0, DefaultLevelNames.Length - 1);

            float spatialBlend = DefaultSpatialBlend[idx];
            float spread       = DefaultSpread[idx];
            float doppler      = DefaultDoppler[idx];
            bool  spatialize   = DefaultSpatialize[idx];
            int   rolloff      = DefaultRolloff[idx];

            if (spatialBlendSlider  != null) spatialBlendSlider.value  = spatialBlend;
            if (spreadSlider        != null) spreadSlider.value        = spread;
            if (dopplerSlider       != null) dopplerSlider.value       = doppler;
            if (spatializeToggle    != null) spatializeToggle.isOn     = spatialize;
            if (rolloffModeDropdown != null) rolloffModeDropdown.value = rolloff;

            UpdateSpatialBlendText(spatialBlend);
            UpdateSpreadText(spread);
            UpdateDopplerText(doppler);
        }

        private AudioManager.ProfileOverrideData ReadSliderValues()
        {
            int idx = Mathf.Clamp(_configLevel - 1, 0, DefaultLevelNames.Length - 1);
            return new AudioManager.ProfileOverrideData
            {
                spatialBlend = spatialBlendSlider  != null ? spatialBlendSlider.value  : DefaultSpatialBlend[idx],
                spread       = spreadSlider        != null ? spreadSlider.value        : DefaultSpread[idx],
                dopplerLevel = dopplerSlider       != null ? dopplerSlider.value       : DefaultDoppler[idx],
                rolloffMode  = rolloffModeDropdown != null ? rolloffModeDropdown.value : DefaultRolloff[idx],
                spatialize   = spatializeToggle    != null ? spatializeToggle.isOn     : DefaultSpatialize[idx],
            };
        }

        public void UpdateSpatialBlendText(float value)
        {
            if (spatialBlendText != null) spatialBlendText.text = $"Spatial Blend  {value:F2}";
        }

        public void UpdateSpreadText(float value)
        {
            if (spreadText != null) spreadText.text = $"Spread  {value:F0}";
        }

        public void UpdateDopplerText(float value)
        {
            if (dopplerText != null) dopplerText.text = $"Doppler Level  {value:F2}";
        }

        public void OnNextFromImmersion()
        {
            ChangeVisibleComponent((int)AudioSpatialId.AudioConfig);
        }

        // ─────────────────────────────────────────────
        //  Step 3 — Audio Config
        //  (replica SalaPanel.SetupStep5Defaults, sin Room Filter)
        // ─────────────────────────────────────────────

        private void SetupAudioConfigDefaults()
        {
            if (baseVolumeSlider != null)
            {
                baseVolumeSlider.minValue = 0f;
                baseVolumeSlider.maxValue = 1f;
                baseVolumeSlider.value    = _baseVolume;
                UpdateBaseVolumeText(_baseVolume);
                baseVolumeSlider.onValueChanged.RemoveAllListeners();
                baseVolumeSlider.onValueChanged.AddListener(UpdateBaseVolumeText);
            }

            if (minConfigLevelInput != null) minConfigLevelInput.text = _emitterMinConfigLevel.ToString();
            if (minDistanceInput    != null) minDistanceInput.text    = _minDistance.ToString("F1");
            if (maxDistanceInput    != null) maxDistanceInput.text    = _maxDistance.ToString("F1");
        }

        public void UpdateBaseVolumeText(float value)
        {
            _baseVolume = value;
            if (baseVolumeText != null)
                baseVolumeText.text = $"Config Volume  {value:F2}";
        }

        private void ReadAudioConfigValues()
        {
            if (int.TryParse(minConfigLevelInput?.text, out int lvl))
                _emitterMinConfigLevel = Mathf.Clamp(lvl, 1, 6);
            if (float.TryParse(minDistanceInput?.text, out float minD))
                _minDistance = Mathf.Max(0f, minD);
            if (float.TryParse(maxDistanceInput?.text, out float maxD))
                _maxDistance = Mathf.Max(_minDistance + 0.01f, maxD);
        }

        public void OnConfirm()
        {
            if (optionFilePath == null || optionFilePath.filePaths == null || optionFilePath.filePaths.Count == 0)
            {
                if (!alertCoroutineRunning)
                    StartCoroutine(SetAlert("Please select at least one audio file."));
                return;
            }

            ReadAudioConfigValues();

            if (sessionManager == null)
            {
                Debug.LogError("[AudioSpatialPanel] SessionManager no encontrado.");
                return;
            }

            sessionManager.audioPaths = new List<string>(optionFilePath.filePaths);
            sessionManager.configLevel = _configLevel;

            // Guardar override del perfil acústico (igual que SalaPanel.SendDataToSessionManager)
            sessionManager.hasAcousticOverride = true;
            sessionManager.acousticOverride    = ReadSliderValues();

            // Guardar config del emitter (igual que SalaPanel.SendDataToSessionManager)
            sessionManager.hasEmitterOverride    = true;
            sessionManager.emitterBaseVolume     = _baseVolume;
            sessionManager.emitterMinConfigLevel = _emitterMinConfigLevel;
            sessionManager.emitterMinDistance    = _minDistance;
            sessionManager.emitterMaxDistance    = _maxDistance;

            Debug.Log($"[AudioSpatialPanel] Config guardada: {sessionManager.audioPaths.Count} archivos, " +
                      $"nivel={_configLevel}, vol={_baseVolume:F2}, minLvl={_emitterMinConfigLevel}, " +
                      $"dist={_minDistance}–{_maxDistance}");

            // Volver a Options Panel
            gameObject.SetActive(false);
            if (optionsPanel != null) optionsPanel.SetActive(true);
        }

        // ─────────────────────────────────────────────
        //  SpawnPanel — requerido
        // ─────────────────────────────────────────────

        public override void BlockButton(int componentId) { }

        // ─────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────

        private static void FixDropdownForVR(TMP_Dropdown dropdown)
        {
            if (dropdown == null || dropdown.template == null) return;
            Canvas c = dropdown.template.GetComponent<Canvas>();
            if (c == null) c = dropdown.template.gameObject.AddComponent<Canvas>();
            c.overrideSorting = true;
            c.sortingOrder    = 30;
        }

        private void WireKeyboard(TMP_InputField field)
        {
            if (field == null || _handKeyboard == null) return;
            HandKeyboard kb = _handKeyboard;
            field.onSelect.AddListener(_ =>
            {
                if (_removeKeyboardCoroutine != null) StopCoroutine(_removeKeyboardCoroutine);
                kb.SetInputField(field);
            });
            field.onDeselect.AddListener(_ =>
            {
                _removeKeyboardCoroutine = StartCoroutine(DelayedRemoveKeyboard(kb));
            });
        }

        private IEnumerator DelayedRemoveKeyboard(HandKeyboard kb)
        {
            yield return null; // esperar un frame
            kb.RemoveInputField();
            _removeKeyboardCoroutine = null;
        }

        private void ClearKeyboard(TMP_InputField field)
        {
            if (field == null) return;
            field.onSelect.RemoveAllListeners();
            field.onDeselect.RemoveAllListeners();
        }
    }
}
