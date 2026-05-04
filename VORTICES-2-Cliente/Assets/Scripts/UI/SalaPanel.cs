using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SimpleFileBrowser;
 
/// <summary>
/// Pasos del flujo de configuración de Sala Environment.
/// </summary>
enum SalaId
{
    RoomConfig      = 0,   // Cantidad de salas y contador de objetos
    BrowsingLocal   = 1,   // Selección de archivos de audio desde el PC
    FileBrowser     = 2,   // Diálogo SimpleFileBrowser (runtime)
    ObjectSelection = 3,   // Selección de objetos que emiten audio
    ImmersionConfig = 4,   // Configuración del AudioManager (AcousticProfiles)
    AudioConfig     = 5,   // Config de SoundEmitter (volumen, distancias, nivel)
    ActivityDirection = 6, // Actividad — clasificación por dirección espacial
    Postload        = 7    // Pantalla final — lanza la sesión
}
 
namespace Vortices
{
    /// <summary>
    /// SalaPanel: Panel de configuración del entorno Sala.
    ///
    /// FLUJO:
    ///   RoomConfig → BrowsingLocal → ObjectSelection →
    ///   ImmersionConfig → AudioConfig → ActivityOne → ActivityTwo → Postload
    /// </summary>
    public class SalaPanel : SpawnPanel
    {
        // ─────────────────────────────────────────────
        //  Referencias a sistemas de la escena
        // ─────────────────────────────────────────────
 
        private RoomGeometry roomGeometry;
        private AudioManager audioManager;
        // ─────────────────────────────────────────────
        //  Estado interno
        // ─────────────────────────────────────────────
 
        // Step 1
        private int minRooms        = 1;
        private int maxRoomsValue   = 50;
        private int totalObjects    = 0;
 
        // Step 3 — selección de objetos
        private Dictionary<string, int>  objectCounts    = new Dictionary<string, int>();
        private Dictionary<string, bool> objectSelected  = new Dictionary<string, bool>();
        private int totalSelectedObjects = 0;
 
        // Step 4 — AudioManager config level activo
        public int configLevel { get; set; } = 4;
 
        // Step 5 — SoundEmitter config
        private float emitterBaseVolume     = 1f;
        private int   emitterMinConfigLevel = 2;
        private float emitterMinDistance    = 1f;
        private float emitterMaxDistance    = 20f;
 
        // Step 6/7 — actividades
        public int activityType { get; set; } = 0;
 
        // ─────────────────────────────────────────────
        //  Inspector — Step 1: Room Config
        // ─────────────────────────────────────────────
 
        [Header("Furniture Config (ScriptableObject)")]
        [Tooltip("Asignar el asset FurniturePlacerConfig para que el contador de objetos funcione en el menu principal.")]
        [SerializeField] private FurniturePlacerConfigSO furniturePlacerConfig;

        [Header("Step 1 - Room Config")]
        [SerializeField] private TMP_InputField minRoomsInput;
        [SerializeField] private TMP_InputField maxRoomsInput;
        [SerializeField] private TextMeshProUGUI totalObjectsText;
 
        // ─────────────────────────────────────────────
        //  Inspector — Step 3: Object Selection
        // ─────────────────────────────────────────────
 
        [Header("Step 3 - Object Selection")]
        [Tooltip("Content del ScrollView donde se generan las filas de objetos.")]
        [SerializeField] private Transform objectListContent;
        [Tooltip("Prefab de una fila de objeto (Toggle + label + count + delete button).")]
        [SerializeField] private GameObject objectRowPrefab;
        [SerializeField] private TextMeshProUGUI objectSelectionStatusText;
 
        // ─────────────────────────────────────────────
        //  Inspector — Step 4: Immersion Config
        // ─────────────────────────────────────────────
 
        [Header("Step 4 - Immersion Config")]
        [SerializeField] private TMP_Dropdown configLevelDropdown;
        [SerializeField] private Slider       spatialBlendSlider;
        [SerializeField] private Slider       spreadSlider;
        [SerializeField] private Slider       dopplerSlider;
        [SerializeField] private TMP_Dropdown rolloffModeDropdown;
        [SerializeField] private Toggle       spatializeToggle;
 
        // ─────────────────────────────────────────────
        //  Inspector — Step 5: Audio Config
        // ─────────────────────────────────────────────
 
        [Header("Step 5 - Audio Config")]
        [SerializeField] private Slider          baseVolumeSlider;
        [SerializeField] private TextMeshProUGUI baseVolumeText;
        [SerializeField] private TMP_InputField  minConfigLevelInput;
        [SerializeField] private TMP_InputField  minDistanceInput;
        [SerializeField] private TMP_InputField  maxDistanceInput;
 
        // ─────────────────────────────────────────────
        //  Inspector — Step 6: Activity Direction
        // ─────────────────────────────────────────────
 
        [Header("Step 6 - Activity Direction")]
        [Tooltip("Content del ScrollView donde se generan las filas de dirección.")]
        [SerializeField] private Transform directionListContent;
        [SerializeField] private TextMeshProUGUI directionSelectionStatusText;
 
        // ─────────────────────────────────────────────
        //  Unity
        // ─────────────────────────────────────────────
 
        private void OnEnable()
        {
            sessionManager = GameObject.Find("SessionManager")?.GetComponent<SessionManager>();
            roomGeometry   = GameObject.FindObjectOfType<RoomGeometry>(true);
            audioManager   = GameObject.FindObjectOfType<AudioManager>(true);

            // Asegurar que el Step 0 (RoomConfig) sea el único visible al entrar
            if (uiComponents != null && uiComponents.Count > 0)
            {
                for (int i = 0; i < uiComponents.Count; i++)
                {
                    if (uiComponents[i] == null) continue;
                    bool isFirst = (i == 0);
                    uiComponents[i].SetActive(isFirst);
                    CanvasGroup cg = uiComponents[i].GetComponent<CanvasGroup>();
                    if (cg != null) { cg.alpha = isFirst ? 1f : 0f; cg.blocksRaycasts = isFirst; }
                }
                actualComponentId = 0;
            }

            if (minRoomsInput != null) minRoomsInput.onValueChanged.AddListener(_ => OnRoomConfigChanged());
            if (maxRoomsInput != null) maxRoomsInput.onValueChanged.AddListener(_ => OnRoomConfigChanged());

            SetupStep4Defaults();
            SetupStep5Defaults();
            OnRoomConfigChanged();
        }
 
        // ─────────────────────────────────────────────
        //  Step 1 — Room Config
        // ─────────────────────────────────────────────
 
        /// <summary>
        /// Llamado cuando el examinador cambia los campos de min/max rooms.
        /// Actualiza el contador de objetos totales.
        /// </summary>
        public void OnRoomConfigChanged()
        {
            bool minParsed = int.TryParse(minRoomsInput?.text, out int min) && min > 0;
            bool maxParsed = int.TryParse(maxRoomsInput?.text, out int max) && max > 0;

            if (minParsed)
                minRooms = min;
            else
                minRooms = 0;

            if (maxParsed)
                maxRoomsValue = max;
            else
                maxRoomsValue = 0;

            if (minRooms > 0 && maxRoomsValue > 0)
            {
                if (roomGeometry != null)
                {
                    roomGeometry.SetRoomConfig(minRooms, maxRoomsValue);
                    totalObjects = roomGeometry.GetTotalObjectCount();
                }
                else if (furniturePlacerConfig != null)
                {
                    totalObjects = furniturePlacerConfig.CalculateTotalObjects(minRooms);
                }
            }
            else
            {
                totalObjects = 0;
            }

            if (totalObjectsText != null)
                totalObjectsText.text = $"{totalObjects} objects have been found";

            BlockButton((int)SalaId.RoomConfig);
        }
 
        // ─────────────────────────────────────────────
        //  Step 2 — File Browser
        // ─────────────────────────────────────────────
 
        public void OpenFileBrowserLocal()
        {
            AddBrowserToComponents();

            FileBrowser.SetFilters(true,
                new FileBrowser.Filter("Audio", ".mp3", ".mp4", ".wav", ".ogg"));

            FileBrowser.ShowLoadDialog(
                paths =>
                {
                    // Limitar a totalObjects archivos máximo
                    List<string> limited = new List<string>(paths);
                    if (limited.Count > totalObjects)
                    {
                        limited = limited.GetRange(0, totalObjects);
                        if (!alertCoroutineRunning)
                            StartCoroutine(SetAlert($"Only {totalObjects} files were loaded (object limit)."));
                    }

                    optionFilePath.ClearPaths();
                    optionFilePath.GetFilePaths(limited.ToArray());
                    optionFilePath.SetUIText();
                    RemoveBrowserFromComponents();
                    ChangeVisibleComponent((int)SalaId.BrowsingLocal);
                },
                () => {
                    RemoveBrowserFromComponents();
                    ChangeVisibleComponent((int)SalaId.BrowsingLocal);
                },
                FileBrowser.PickMode.FilesAndFolders,
                true,
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                null, "Select audio files", "Select");
        }
 
        public void AddBrowserToComponents()
        {
            GameObject browserCanvas = GameObject.Find("SimpleFileBrowserCanvas(Clone)");
            if (browserCanvas != null && uiComponents != null && uiComponents.Count > (int)SalaId.FileBrowser)
            {
                uiComponents[(int)SalaId.FileBrowser] = browserCanvas;
                var fileBrowser = browserCanvas.GetComponent<FileBrowser>();
                if (fileBrowser != null)
                    fileBrowser.SetAsPersistent(false);
            }
        }
 
        public void RemoveBrowserFromComponents()
        {
            uiComponents[(int)SalaId.FileBrowser].GetComponent<FileBrowser>().SetAsPersistent(true);
        }
 
        // ─────────────────────────────────────────────
        //  Step 3 — Object Selection
        // ─────────────────────────────────────────────
 
        /// <summary>
        /// Llama a RoomGeometry para obtener los tipos de objetos y genera las filas en la UI.
        /// Se llama al entrar al Step 3.
        /// </summary>
        public void LoadObjectSelectionUI()
        {
            if (objectListContent == null || objectRowPrefab == null) return;
            if (roomGeometry == null && furniturePlacerConfig == null) return;

            objectCounts = roomGeometry != null
                ? roomGeometry.GetObjectCountsByType()
                : furniturePlacerConfig.CalculateObjectCounts(minRooms);
            objectSelected = new Dictionary<string, bool>();
 
            // Limpiar filas anteriores
            foreach (Transform child in objectListContent)
                Destroy(child.gameObject);
 
            foreach (var pair in objectCounts)
            {
                string prefabName = pair.Key;
                int    count      = pair.Value;
                objectSelected[prefabName] = false;
 
                GameObject row      = Instantiate(objectRowPrefab, objectListContent);
                Toggle     toggle   = row.GetComponentInChildren<Toggle>();
                TextMeshProUGUI label  = row.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
                TextMeshProUGUI counter = row.transform.Find("Counter")?.GetComponent<TextMeshProUGUI>();
                Button deleteBtn    = row.transform.Find("DeleteButton")?.GetComponent<Button>();
 
                if (label   != null) label.text   = prefabName;
                if (counter != null) counter.text = $"({count})";
 
                // Capturar para el closure
                string capturedName = prefabName;
                if (toggle != null)
                    toggle.onValueChanged.AddListener(isOn =>
                    {
                        objectSelected[capturedName] = isOn;
                        UpdateObjectSelectionStatus();
                    });
 
                if (deleteBtn != null)
                    deleteBtn.onClick.AddListener(() =>
                    {
                        objectSelected.Remove(capturedName);
                        objectCounts.Remove(capturedName);
                        Destroy(row);
                        UpdateObjectSelectionStatus();
                    });
            }
 
            UpdateObjectSelectionStatus();
        }
 
        private void UpdateObjectSelectionStatus()
        {
            totalSelectedObjects = 0;
            foreach (var pair in objectSelected)
                if (pair.Value && objectCounts.ContainsKey(pair.Key))
                    totalSelectedObjects += objectCounts[pair.Key];
 
            int audioCount = optionFilePath.filePaths != null ? optionFilePath.filePaths.Count : 0;
 
            if (objectSelectionStatusText != null)
            {
                if (totalSelectedObjects < audioCount)
                    objectSelectionStatusText.text = "Please, more objects need to be selected!";
                else
                    objectSelectionStatusText.text = $"{totalSelectedObjects} objects selected — ready!";
            }
 
            BlockButton((int)SalaId.ObjectSelection);
        }
 
        // ─────────────────────────────────────────────
        //  Step 4 — Immersion Config
        // ─────────────────────────────────────────────
 
        private void SetupStep4Defaults()
        {
            if (audioManager == null) return;
 
            if (configLevelDropdown != null)
            {
                configLevelDropdown.ClearOptions();
                List<string> options = new List<string>();
                for (int i = 1; i <= audioManager.acousticProfiles.Count; i++)
                    options.Add($"Level {i} — {audioManager.acousticProfiles[i - 1].levelName}");
                configLevelDropdown.AddOptions(options);
                configLevelDropdown.value = configLevel - 1;
                configLevelDropdown.onValueChanged.AddListener(v =>
                {
                    configLevel = v + 1;
                    RefreshImmersionSliders();
                });
            }
 
            RefreshImmersionSliders();
        }
 
        private void RefreshImmersionSliders()
        {
            if (audioManager == null || configLevel < 1) return;
            var profile = audioManager.GetAcousticProfile(configLevel);
            if (profile == null) return;
 
            if (spatialBlendSlider != null) spatialBlendSlider.value = profile.spatialBlend;
            if (spreadSlider       != null) spreadSlider.value       = profile.spread;
            if (dopplerSlider      != null) dopplerSlider.value      = profile.dopplerLevel;
            if (spatializeToggle   != null) spatializeToggle.isOn    = profile.spatialize;
            if (rolloffModeDropdown != null)
                rolloffModeDropdown.value = (int)profile.rolloffMode;
        }
 
        /// <summary>Aplica los cambios de los sliders al AcousticProfile activo.</summary>
        public void ApplyImmersionChanges()
        {
            if (audioManager == null) return;
            var profile = audioManager.GetAcousticProfile(configLevel);
            if (profile == null) return;
 
            if (spatialBlendSlider  != null) profile.spatialBlend  = spatialBlendSlider.value;
            if (spreadSlider        != null) profile.spread        = spreadSlider.value;
            if (dopplerSlider       != null) profile.dopplerLevel  = dopplerSlider.value;
            if (spatializeToggle    != null) profile.spatialize    = spatializeToggle.isOn;
            if (rolloffModeDropdown != null) profile.rolloffMode   = (AudioRolloffMode)rolloffModeDropdown.value;
        }
 
        // ─────────────────────────────────────────────
        //  Step 5 — Audio Config
        // ─────────────────────────────────────────────
 
        private void SetupStep5Defaults()
        {
            if (baseVolumeSlider != null)
            {
                baseVolumeSlider.minValue     = 0f;
                baseVolumeSlider.maxValue     = 1f;
                baseVolumeSlider.value        = emitterBaseVolume;
                UpdateBaseVolumeText(emitterBaseVolume);
                baseVolumeSlider.onValueChanged.AddListener(v => UpdateBaseVolumeText(v));
            }
 
            if (minConfigLevelInput != null) minConfigLevelInput.text = emitterMinConfigLevel.ToString();
            if (minDistanceInput    != null) minDistanceInput.text    = emitterMinDistance.ToString();
            if (maxDistanceInput    != null) maxDistanceInput.text    = emitterMaxDistance.ToString();
        }
 
        public void UpdateBaseVolumeText(float value)
        {
            emitterBaseVolume = value;
            if (baseVolumeText != null)
                baseVolumeText.text = $"{value:F2}";
        }
 
        public void OnAudioConfigChanged()
        {
            if (int.TryParse(minConfigLevelInput?.text, out int lvl))
                emitterMinConfigLevel = Mathf.Clamp(lvl, 1, 6);
            if (float.TryParse(minDistanceInput?.text, out float minD))
                emitterMinDistance = Mathf.Max(0f, minD);
            if (float.TryParse(maxDistanceInput?.text, out float maxD))
                emitterMaxDistance = Mathf.Max(emitterMinDistance + 0.01f, maxD);
        }
 
        // ─────────────────────────────────────────────
        //  BlockButton
        // ─────────────────────────────────────────────
 
        public override void BlockButton(int componentId)
        {
            bool hasToBlock = true;

            switch (componentId)
            {
                case (int)SalaId.RoomConfig:
                    // Bloquear si campos están vacíos o si min > max
                    if (minRooms > 0 && maxRoomsValue >= minRooms)
                        hasToBlock = false;
                    break;
 
                case (int)SalaId.BrowsingLocal:
                    if (optionFilePath.filePaths != null && optionFilePath.filePaths.Count > 0)
                        hasToBlock = false;
                    else if (optionFilePath.filePaths != null && optionFilePath.filePaths.Count == 0)
                        if (!alertCoroutineRunning)
                            StartCoroutine(SetAlert("No compatible audio files found."));
                    break;
 
                case (int)SalaId.ObjectSelection:
                    int audioCount = optionFilePath.filePaths != null ? optionFilePath.filePaths.Count : 0;
                    if (totalSelectedObjects >= audioCount && audioCount > 0)
                        hasToBlock = false;
                    break;
 
                case (int)SalaId.ImmersionConfig:
                case (int)SalaId.AudioConfig:
                    // Siempre desbloqueado — tienen valores por defecto válidos
                    hasToBlock = false;
                    break;
 
                case (int)SalaId.ActivityDirection:
                    // El Category Selection maneja su propia lógica — siempre habilitado
                    hasToBlock = false;
                    break;
            }
 
            if (componentId != (int)SalaId.FileBrowser &&
                componentId != (int)SalaId.Postload)
            {
                Button nextButton = uiComponents[componentId].transform.Find("Footer").GetComponentInChildren<Button>();
                if (nextButton != null)
                    nextButton.interactable = !hasToBlock;
            }
        }
 
        // ─────────────────────────────────────────────
        //  Envío de datos al SessionManager
        // ─────────────────────────────────────────────
 
        public void SendDataToSessionManager()
        {
            if (sessionManager == null)
            {
                Debug.LogError("[SalaPanel] SessionManager no encontrado.");
                return;
            }
 
            // Aplicar config de sala antes de lanzar
            if (roomGeometry != null)
                roomGeometry.SetRoomConfig(minRooms, maxRoomsValue);
 
            // Aplicar cambios del AudioManager
            ApplyImmersionChanges();
            if (audioManager != null)
                audioManager.SetConfigLevel(configLevel);
 
            // Archivos de audio
            sessionManager.browsingMode = "Local";
            sessionManager.elementPaths = optionFilePath.filePaths;
            sessionManager.configLevel  = configLevel;
            sessionManager.displayMode  = "Sala";
 
            // Guardar config de SoundEmitter en SessionManager si tiene esos campos
            // (se aplican en LaunchSession al iniciar la escena)
 
            sessionManager.LaunchSession();
        }
    }
}