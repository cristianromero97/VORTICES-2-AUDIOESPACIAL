using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;
using TMPro;
using SimpleFileBrowser;

// ══════════════════════════════════════════════════════════════
//  ENFOQUE ACTUAL: Options tiene un botón "Audio Espacial" que
//  navega al AudioSpatialPanel (via MainMenuPanel.ChangeVisibleComponent).
//  El código comentado abajo fue el intento de integrar todo
//  directamente en Options — se conserva por si se necesita después.
// ══════════════════════════════════════════════════════════════

namespace Vortices
{
    // ORIGINAL — NO borrar
    public enum FrameLimit
    {
        noLimit = 0,
        limit30 = 30,
        limit60 = 60,
    }

    public class OptionsPanel : MonoBehaviour
    {
        // ─────────────────────────────────────────────
        //  ORIGINAL — campos de configuración de app
        // ─────────────────────────────────────────────

        public AudioMixer audioMixer;
        Resolution[] resolutions;
        float[] refreshRates;

        public TMP_Dropdown resolutionDropdown;
        public TMP_Dropdown refreshRateDropdown;
        public TMP_Dropdown qualitySettingsDropdown;

        // ─────────────────────────────────────────────
        //  COMENTADO — integración Audio Espacial directa en Options
        //  (se dejó en espera; el enfoque actual usa AudioSpatialPanel separado)
        // ─────────────────────────────────────────────

        /*
        [Header("Audio Espacial")]
        [SerializeField] private TextMeshProUGUI selectedFilesLabel;
        [SerializeField] private TMP_Dropdown    configLevelDropdown;
        [SerializeField] private Slider          baseVolumeSlider;
        [SerializeField] private TextMeshProUGUI baseVolumeText;
        [SerializeField] private TMP_InputField  minDistanceInput;
        [SerializeField] private TMP_InputField  maxDistanceInput;

        private List<string> _audioPaths = new List<string>();
        private int   _configLevel = 4;
        private float _baseVolume  = 1f;
        private float _minDistance = 1f;
        private float _maxDistance = 12f;

        private static readonly HashSet<string> AudioExtensions = new HashSet<string>
            { ".mp3", ".mp4", ".wav", ".ogg", ".m4a" };
        */

        // ─────────────────────────────────────────────
        //  Unity lifecycle
        // ─────────────────────────────────────────────

        private void Start()
        {
            SetupAppSettings();
        }

        // ─────────────────────────────────────────────
        //  ORIGINAL — configuración de app
        // ─────────────────────────────────────────────

        private void SetupAppSettings()
        {
            resolutions = Screen.resolutions;
            resolutionDropdown.ClearOptions();

            List<string> options = new List<string>();
            int currentResolutionIndex = 0;
            for (int i = 0; i < resolutions.Length; i++)
            {
                string option = resolutions[i].width + " x " + resolutions[i].height;
                options.Add(option);
                if (resolutions[i].width == Screen.currentResolution.width &&
                    resolutions[i].height == Screen.currentResolution.height)
                    currentResolutionIndex = i;
            }
            resolutionDropdown.AddOptions(options);
            resolutionDropdown.value = currentResolutionIndex;
            resolutionDropdown.RefreshShownValue();

            qualitySettingsDropdown.value = QualitySettings.GetQualityLevel();
            qualitySettingsDropdown.RefreshShownValue();

            refreshRateDropdown.ClearOptions();
            Unity.XR.Oculus.Performance.TryGetAvailableDisplayRefreshRates(out refreshRates);

            List<string> rateOptions = new List<string>();
            int currentRateIndex = 0;
            if (refreshRates.Length > 0)
            {
                for (int i = 0; i < refreshRates.Length; i++)
                {
                    rateOptions.Add(refreshRates[i].ToString());
                    Unity.XR.Oculus.Performance.TryGetDisplayRefreshRate(out float currentRate);
                    if (refreshRates[i] == currentRate) currentRateIndex = i;
                }
                refreshRateDropdown.AddOptions(rateOptions);
                refreshRateDropdown.value = currentRateIndex;
                refreshRateDropdown.RefreshShownValue();
            }
            else
            {
                rateOptions.Add("Screen Rate");
                refreshRateDropdown.AddOptions(rateOptions);
                refreshRateDropdown.RefreshShownValue();
            }
        }

        public void SetResolution(int resolutionIndex)
        {
            Resolution resolution = resolutions[resolutionIndex];
            Screen.SetResolution(resolution.width, resolution.height, true);
        }

        public void SetRefreshRate(int refreshIndex)
        {
            Unity.XR.Oculus.Performance.TrySetDisplayRefreshRate(refreshRates[refreshIndex]);
        }

        public void SetVolume(float volume)
        {
            audioMixer.SetFloat("Volume", volume);
        }

        public void SetQuality(int qualityIndex)
        {
            QualitySettings.SetQualityLevel(qualityIndex);
        }

        // ─────────────────────────────────────────────
        //  NUEVO — Navegación hacia Audio Espacial
        // ─────────────────────────────────────────────

        // Referencia al Spatial Audio Panel (hermano en la jerarquía)
        [SerializeField] private GameObject audioSpatialPanel;

        // Llamado desde el botón "Spatial Audio" dentro de Options.
        // Se desactiva a sí mismo para ocultar Language/Resolution/etc,
        // y activa el Spatial Audio Panel en su lugar.
        public void OpenAudioSpatialConfig()
        {
            if (audioSpatialPanel != null)
                audioSpatialPanel.SetActive(true);
            gameObject.SetActive(false);
        }

        // Llamado cuando el contenedor padre de Options se reactiva (via OptionsContainerReset).
        // Garantiza que Options Panel sea visible y Spatial Audio Panel esté oculto.
        public void ResetToDefault()
        {
            gameObject.SetActive(true);
            if (audioSpatialPanel != null) audioSpatialPanel.SetActive(false);
        }

        // ─────────────────────────────────────────────
        //  COMENTADO — métodos de Audio Espacial integrado
        //  (conservado por si se retoma este enfoque)
        // ─────────────────────────────────────────────

        /*
        private void SetupAudioEspacialDefaults() { ... }
        public void OpenFileBrowser() { ... }
        private void CollectAudioPaths(string[] paths) { ... }
        private void UpdateSelectedFilesLabel() { ... }
        public void OnBaseVolumeChanged(float value) { ... }
        public void SaveAudioEspacialConfig() { ... }
        private static void FixDropdownForVR(TMP_Dropdown dropdown) { ... }
        */
    }
}
