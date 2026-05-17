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

        // Step 5 — Room Filter
        private bool roomFilterEnabled = false;
        private int  roomFilterMode    = 0; // 0=Todas, 1=Pares, 2=Impares
 
        // Step 6 — direcciones de actividad
        private Dictionary<string, bool> directionSelected = new Dictionary<string, bool>();

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
        [SerializeField] private TMP_Dropdown    rolloffModeDropdown;
        [SerializeField] private Toggle          spatializeToggle;
        [SerializeField] private TextMeshProUGUI spatialBlendText;
        [SerializeField] private TextMeshProUGUI spreadText;
        [SerializeField] private TextMeshProUGUI dopplerText;
 
        // ─────────────────────────────────────────────
        //  Inspector — Step 5: Audio Config
        // ─────────────────────────────────────────────
 
        [Header("Step 5 - Audio Config")]
        [SerializeField] private Slider          baseVolumeSlider;
        [SerializeField] private TextMeshProUGUI baseVolumeText;
        [SerializeField] private TMP_InputField  minConfigLevelInput;
        [SerializeField] private TMP_InputField  minDistanceInput;
        [SerializeField] private TMP_InputField  maxDistanceInput;

        [Header("Step 5 - Room Filter")]
        [SerializeField] private Toggle     roomFilterToggle;
        [SerializeField] private GameObject roomFilterModeContainer;
        [SerializeField] private Toggle     allRoomsToggle;
        [SerializeField] private Toggle     evenRoomsToggle;
        [SerializeField] private Toggle     oddRoomsToggle;
 
        // ─────────────────────────────────────────────
        //  Inspector — Step 6: Activity Direction
        // ─────────────────────────────────────────────
 
        [Header("Step 6 - Activity Direction")]
        [Tooltip("Content del ScrollView donde se generan las filas de dirección.")]
        [SerializeField] private Transform directionListContent;
        [SerializeField] private TextMeshProUGUI directionSelectionStatusText;
        [SerializeField] private GameObject directionRowPrefab;
        [SerializeField] private TMP_InputField newDirectionInput;
 
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
            SetupStep5RoomFilter();
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
            Debug.Log("[SalaPanel] LoadObjectSelectionUI llamado");

            if (objectListContent == null)
            {
                Debug.LogError("[SalaPanel] objectListContent es NULL");
                return;
            }
            if (objectRowPrefab == null)
            {
                Debug.LogError("[SalaPanel] objectRowPrefab es NULL");
                return;
            }
            if (roomGeometry == null && furniturePlacerConfig == null)
            {
                Debug.LogError("[SalaPanel] roomGeometry Y furniturePlacerConfig son NULL");
                return;
            }

            objectCounts = roomGeometry != null
                ? roomGeometry.GetObjectCountsByType()
                : furniturePlacerConfig.CalculateObjectCounts(minRooms);

            Debug.Log($"[SalaPanel] Objetos encontrados: {objectCounts.Count} tipos");
            foreach (var pair in objectCounts)
                Debug.Log($"  - {pair.Key}: {pair.Value}");

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

                Debug.Log($"[SalaPanel] Fila creada para {prefabName}");

                // Capturar para el closure
                string capturedName = prefabName;
                Image rowBackground = row.GetComponent<Image>();
                if (toggle != null)
                    toggle.onValueChanged.AddListener(isOn =>
                    {
                        objectSelected[capturedName] = isOn;
                        if (rowBackground != null)
                            rowBackground.color = isOn
                                ? new Color(0.369f, 0.812f, 0.812f, 0.3f)  // teal semitransparente
                                : new Color(1f, 1f, 1f, 0.05f);            // blanco casi transparente
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
 
        // Valores por defecto de los 6 niveles (mismos que AudioManager.EnsureDefaultLevels)
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
        private static readonly int[]   DefaultRolloff      = { 1,    1,    0,    2,   2,   2    }; // 0=Logarithmic,1=Linear,2=Custom
        private static readonly bool[]  DefaultSpatialize   = { false,false,false,false,true,true };

        private void SetupStep4Defaults()
        {
            if (configLevelDropdown != null)
            {
                configLevelDropdown.ClearOptions();
                var options = new List<string>();
                if (audioManager != null)
                {
                    for (int i = 1; i <= audioManager.acousticProfiles.Count; i++)
                        options.Add($"Level {i} — {audioManager.acousticProfiles[i - 1].levelName}");
                }
                else
                {
                    for (int i = 0; i < DefaultLevelNames.Length; i++)
                        options.Add($"Level {i + 1} — {DefaultLevelNames[i]}");
                }
                configLevelDropdown.AddOptions(options);
                configLevelDropdown.value = configLevel - 1;
                configLevelDropdown.onValueChanged.AddListener(v =>
                {
                    configLevel = v + 1;
                    RefreshImmersionSliders();
                });
            }

            RefreshImmersionSliders();

            if (spatialBlendSlider != null) spatialBlendSlider.onValueChanged.AddListener(v => UpdateSpatialBlendText(v));
            if (spreadSlider       != null) spreadSlider.onValueChanged.AddListener(v => UpdateSpreadText(v));
            if (dopplerSlider      != null) dopplerSlider.onValueChanged.AddListener(v => UpdateDopplerText(v));
        }

        private void RefreshImmersionSliders()
        {
            if (configLevel < 1) return;
            int idx = Mathf.Clamp(configLevel - 1, 0, DefaultLevelNames.Length - 1);

            float spatialBlend, spread, doppler;
            bool  spatialize;
            int   rolloff;

            if (audioManager != null)
            {
                var profile = audioManager.GetAcousticProfile(configLevel);
                if (profile == null) return;
                spatialBlend = profile.spatialBlend;
                spread       = profile.spread;
                doppler      = profile.dopplerLevel;
                spatialize   = profile.spatialize;
                rolloff      = (int)profile.rolloffMode;
            }
            else
            {
                spatialBlend = DefaultSpatialBlend[idx];
                spread       = DefaultSpread[idx];
                doppler      = DefaultDoppler[idx];
                spatialize   = DefaultSpatialize[idx];
                rolloff      = DefaultRolloff[idx];
            }

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
            int idx = Mathf.Clamp(configLevel - 1, 0, DefaultLevelNames.Length - 1);
            return new AudioManager.ProfileOverrideData
            {
                spatialBlend = spatialBlendSlider  != null ? spatialBlendSlider.value  : DefaultSpatialBlend[idx],
                spread       = spreadSlider        != null ? spreadSlider.value        : DefaultSpread[idx],
                dopplerLevel = dopplerSlider       != null ? dopplerSlider.value       : DefaultDoppler[idx],
                rolloffMode  = rolloffModeDropdown != null ? rolloffModeDropdown.value : DefaultRolloff[idx],
                spatialize   = spatializeToggle    != null ? spatializeToggle.isOn     : DefaultSpatialize[idx],
            };
        }

        public void ApplyImmersionChanges()
        {
            if (audioManager == null) return;
            var profile = audioManager.GetAcousticProfile(configLevel);
            if (profile == null) return;
            var ovr = ReadSliderValues();
            profile.spatialBlend = ovr.spatialBlend;
            profile.spread       = ovr.spread;
            profile.dopplerLevel = ovr.dopplerLevel;
            profile.rolloffMode  = (AudioRolloffMode)ovr.rolloffMode;
            profile.spatialize   = ovr.spatialize;
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
 
        private void SetupStep5RoomFilter()
        {
            if (roomFilterModeContainer != null)
                roomFilterModeContainer.SetActive(roomFilterEnabled);

            if (roomFilterToggle != null)
            {
                roomFilterToggle.isOn = roomFilterEnabled;
                roomFilterToggle.onValueChanged.AddListener(enabled =>
                {
                    roomFilterEnabled = enabled;
                    if (roomFilterModeContainer != null)
                        roomFilterModeContainer.SetActive(enabled);
                });
            }

            if (allRoomsToggle  != null) allRoomsToggle.onValueChanged.AddListener(isOn  => { if (isOn) roomFilterMode = 0; });
            if (evenRoomsToggle != null) evenRoomsToggle.onValueChanged.AddListener(isOn => { if (isOn) roomFilterMode = 1; });
            if (oddRoomsToggle  != null) oddRoomsToggle.onValueChanged.AddListener(isOn  => { if (isOn) roomFilterMode = 2; });

            // Estado inicial: Todas seleccionado
            if (allRoomsToggle != null) allRoomsToggle.isOn = true;
        }

        private List<int> GetRoomFilterIds()
        {
            var ids = new List<int>();
            for (int i = 1; i <= maxRoomsValue; i++)
            {
                bool isEven = (i % 2 == 0);
                if (roomFilterMode == 1 && isEven)  ids.Add(i);
                if (roomFilterMode == 2 && !isEven) ids.Add(i);
            }
            return ids;
        }

        public void UpdateBaseVolumeText(float value)
        {
            emitterBaseVolume = value;
            if (baseVolumeText != null)
                baseVolumeText.text = $"Config Volume  {value:F2}";
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
        //  Step 6 — Activity Direction
        // ─────────────────────────────────────────────

        public void OnAddDirectionClicked()
        {
            if (newDirectionInput == null) return;
            string dirName = newDirectionInput.text.Trim();
            if (string.IsNullOrEmpty(dirName)) return;
            if (directionSelected.ContainsKey(dirName)) return;

            AddDirectionRow(dirName);
            newDirectionInput.text = "";
            UpdateDirectionSelectionStatus();
        }

        private void AddDirectionRow(string directionName)
        {
            if (directionListContent == null || directionRowPrefab == null) return;

            directionSelected[directionName] = false;

            GameObject row      = Instantiate(directionRowPrefab, directionListContent);
            Toggle     toggle   = row.GetComponentInChildren<Toggle>();
            TextMeshProUGUI label   = row.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI counter = row.transform.Find("Counter")?.GetComponent<TextMeshProUGUI>();
            Button deleteBtn    = row.transform.Find("DeleteButton")?.GetComponent<Button>();

            if (label   != null) label.text = directionName;
            if (counter != null) counter.gameObject.SetActive(false);

            string capturedName = directionName;
            Image rowBackground = row.GetComponent<Image>();

            if (toggle != null)
                toggle.onValueChanged.AddListener(isOn =>
                {
                    directionSelected[capturedName] = isOn;
                    if (rowBackground != null)
                        rowBackground.color = isOn
                            ? new Color(0.369f, 0.812f, 0.812f, 0.3f)
                            : new Color(1f, 1f, 1f, 0.05f);
                    UpdateDirectionSelectionStatus();
                });

            if (deleteBtn != null)
                deleteBtn.onClick.AddListener(() =>
                {
                    directionSelected.Remove(capturedName);
                    Destroy(row);
                    UpdateDirectionSelectionStatus();
                });
        }

        private void UpdateDirectionSelectionStatus()
        {
            int selected = 0;
            foreach (var pair in directionSelected)
                if (pair.Value) selected++;

            if (directionSelectionStatusText != null)
                directionSelectionStatusText.text = selected > 0
                    ? $"{selected} direction(s) selected — ready!"
                    : "Select at least one direction to continue.";

            BlockButton((int)SalaId.ActivityDirection);
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
                    int selectedDirs = 0;
                    foreach (var pair in directionSelected)
                        if (pair.Value) selectedDirs++;
                    hasToBlock = selectedDirs == 0;
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
 
            // Aplicar cambios del AudioManager (si está presente en escena)
            ApplyImmersionChanges();
            if (audioManager != null)
                audioManager.SetConfigLevel(configLevel);

            // Step 1 — Room Config
            sessionManager.minRooms = minRooms;
            sessionManager.maxRooms = maxRoomsValue;

            // Step 3 — Object Selection
            var selectedTypes = new List<string>();
            foreach (var pair in objectSelected)
                if (pair.Value) selectedTypes.Add(pair.Key);
            sessionManager.selectedObjectTypes = selectedTypes;

            // Archivos de audio
            sessionManager.environmentName = "Sala";
            sessionManager.browsingMode    = "Local";
            sessionManager.elementPaths    = optionFilePath.filePaths;
            sessionManager.configLevel     = configLevel;
            sessionManager.displayMode     = "Sala";

            // Guardar override del perfil acústico para aplicarlo en Sala Environment
            sessionManager.hasAcousticOverride = true;
            sessionManager.acousticOverride    = ReadSliderValues();

            // Step 5 — Audio Config (emitter global override)
            OnAudioConfigChanged();
            sessionManager.hasEmitterOverride    = true;
            sessionManager.emitterBaseVolume     = emitterBaseVolume;
            sessionManager.emitterMinConfigLevel = emitterMinConfigLevel;
            sessionManager.emitterMinDistance    = emitterMinDistance;
            sessionManager.emitterMaxDistance    = emitterMaxDistance;

            // Guardar direcciones seleccionadas
            var selectedDirList = new List<string>();
            foreach (var pair in directionSelected)
                if (pair.Value) selectedDirList.Add(pair.Key);
            sessionManager.selectedDirections = selectedDirList;

            // Guardar filtro por sala
            sessionManager.hasRoomFilter  = roomFilterEnabled;
            sessionManager.roomFilterAll  = (roomFilterMode == 0);
            sessionManager.roomFilterIds  = roomFilterEnabled && roomFilterMode != 0
                ? GetRoomFilterIds()
                : new List<int>();

            sessionManager.LaunchSession();
        }
    }
}