using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SimpleFileBrowser;
using System.IO;

/// <summary>
/// Pasos del flujo de configuración de Sala Environment (modo Local únicamente).
/// Cada valor corresponde a un índice en la lista uiComponents de SpawnPanel.
/// </summary>
enum SalaId
{
    BrowsingLocal   = 0,   // Selector de archivos de audio locales (.mp3 / .mp4 / .wav / .ogg)
    FileBrowser     = 1,   // El diálogo de SimpleFileBrowser (instanciado en runtime)
    AcousticConfig  = 2,   // Nivel de configuración acústica (1–6)
    ActivityConfig  = 3,   // Tipo de actividad del participante
    Postload        = 4    // Pantalla final — lanza la sesión
}

namespace Vortices
{
    /// <summary>
    /// SalaPanel: Panel de configuración del entorno Sala (corredor con salas laterales y audio espacial).
    /// Solo soporta modo Local — el examinador selecciona archivos de audio desde su PC.
    ///
    /// FLUJO:
    ///   BrowsingLocal → AcousticConfig → ActivityConfig → Postload
    ///
    /// ACTIVIDADES DISPONIBLES:
    ///   0 — Categorizar audios
    ///   1 — Encontrar la dirección del sonido
    ///   2 — Identificar la sala de origen del sonido
    /// </summary>
    public class SalaPanel : SpawnPanel
    {
        // ─────────────────────────────────────────────
        //  Propiedades de la sesión
        // ─────────────────────────────────────────────

        public int configLevel  { get; set; }
        public int activityType { get; set; }

        // ─────────────────────────────────────────────
        //  Referencias de UI
        // ─────────────────────────────────────────────

        [Header("Acoustic Config Panel")]
        [SerializeField] private Slider acousticLevelSlider;
        [SerializeField] private TextMeshProUGUI acousticLevelText;

        [Header("Activity Config Panel")]
        [SerializeField] private Toggle activityCategorizeToggle;
        [SerializeField] private Toggle activityDirectionToggle;
        [SerializeField] private Toggle activityIdentifyToggle;

        // ─────────────────────────────────────────────
        //  Unity
        // ─────────────────────────────────────────────

        private void OnEnable()
        {
            sessionManager = GameObject.Find("SessionManager")?.GetComponent<SessionManager>();

            configLevel  = 4;
            activityType = 0;

            if (acousticLevelSlider != null)
            {
                acousticLevelSlider.minValue     = 1;
                acousticLevelSlider.maxValue     = 6;
                acousticLevelSlider.wholeNumbers = true;
                acousticLevelSlider.value        = configLevel;
                UpdateAcousticLevelText((int)acousticLevelSlider.value);
                acousticLevelSlider.onValueChanged.AddListener(v => UpdateAcousticLevelText((int)v));
            }
        }

        // ─────────────────────────────────────────────
        //  Callbacks de UI
        // ─────────────────────────────────────────────

        public void UpdateAcousticLevelText(int level)
        {
            configLevel = level;
            if (acousticLevelText != null)
                acousticLevelText.text = $"Nivel {level}";
        }

        public void SetActivityType(int type)
        {
            activityType = type;
            BlockButton((int)SalaId.ActivityConfig);
        }

        public void OpenFileBrowserLocal()
        {
            FileBrowser.SetFilters(true,
                new FileBrowser.Filter("Audio", ".mp3", ".mp4", ".wav", ".ogg"));

            FileBrowser.ShowLoadDialog(
                paths =>
                {
                    optionFilePath.ClearPaths();
                    optionFilePath.GetFilePaths(paths);
                    optionFilePath.SetUIText();
                    ChangeVisibleComponent((int)SalaId.BrowsingLocal);
                },
                () => ChangeVisibleComponent((int)SalaId.BrowsingLocal),
                FileBrowser.PickMode.FilesAndFolders,
                true,
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                null, "Seleccionar audios", "Seleccionar");
        }

        public void AddBrowserToComponents()
        {
            uiComponents[(int)SalaId.FileBrowser] = GameObject.Find("SimpleFileBrowserCanvas(Clone)");
            FileBrowser fileBrowser = uiComponents[(int)SalaId.FileBrowser].GetComponent<FileBrowser>();
            fileBrowser.SetAsPersistent(false);
        }

        public void RemoveBrowserFromComponents()
        {
            FileBrowser fileBrowser = uiComponents[(int)SalaId.FileBrowser].GetComponent<FileBrowser>();
            fileBrowser.SetAsPersistent(true);
        }

        // ─────────────────────────────────────────────
        //  BlockButton
        // ─────────────────────────────────────────────

        public override void BlockButton(int componentId)
        {
            bool hasToBlock = true;

            switch (componentId)
            {
                case (int)SalaId.BrowsingLocal:
                    if (optionFilePath.filePaths != null && optionFilePath.filePaths.Count > 0)
                        hasToBlock = false;
                    else if (optionFilePath.filePaths != null && optionFilePath.filePaths.Count == 0)
                        if (!alertCoroutineRunning)
                            StartCoroutine(SetAlert("No se encontraron archivos de audio compatibles."));
                    break;

                case (int)SalaId.AcousticConfig:
                    hasToBlock = false;
                    break;

                case (int)SalaId.ActivityConfig:
                    if ((activityCategorizeToggle != null && activityCategorizeToggle.isOn) ||
                        (activityDirectionToggle  != null && activityDirectionToggle.isOn)  ||
                        (activityIdentifyToggle   != null && activityIdentifyToggle.isOn))
                        hasToBlock = false;
                    break;
            }

            if (componentId != (int)SalaId.FileBrowser &&
                componentId != (int)SalaId.Postload)
            {
                Button nextButton = uiComponents[componentId].transform.Find("Footer").GetComponentInChildren<Button>();
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

            sessionManager.browsingMode = "Local";
            sessionManager.elementPaths = optionFilePath.filePaths;
            sessionManager.configLevel  = configLevel;

            switch (activityType)
            {
                case 0: sessionManager.displayMode = "Categorize"; break;
                case 1: sessionManager.displayMode = "Direction";  break;
                case 2: sessionManager.displayMode = "Identify";   break;
                default: sessionManager.displayMode = "Categorize"; break;
            }

            sessionManager.LaunchSession();
        }
    }
}
