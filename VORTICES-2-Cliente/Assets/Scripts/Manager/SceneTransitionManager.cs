using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vuplex.WebView;
using UnityEditor;
using UnityEngine.InputSystem.XInput;

namespace Vortices
{
    public class SceneTransitionManager : MonoBehaviour
    {
        public FadeScreen fadeScreen;
        private float blackScreenDuration = 2.0f;
        public string sceneName;

        public bool returnToMain;

        public static SceneTransitionManager instance;

        private void Start()
        {
            // Instance initializing
            if (instance != null)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            #if UNITY_EDITOR
            UnityEditor.EditorApplication.quitting += OnQuitting;
            #endif
        }

        public void GoToScene()
        {
            StartCoroutine(GoToSceneRoutine());
        }

        public IEnumerator GoToSceneRoutine()
        {
            // Limpiar EventSystems duplicados antes de cambiar de escena.
            // SimpleFileBrowser (selector de archivos en Options) deja un EventSystem extra
            // que convive con el del nuevo ambiente → warning infinito + freeze.
            var allEventSystems = FindObjectsOfType<UnityEngine.EventSystems.EventSystem>();
            if (allEventSystems.Length > 1)
            {
                Debug.LogWarning($"[SceneTransitionManager] {allEventSystems.Length} EventSystems encontrados. Eliminando duplicados.");
                // Mantener solo el primero; destruir el resto
                for (int i = 1; i < allEventSystems.Length; i++)
                    Destroy(allEventSystems[i].gameObject);
            }

            fadeScreen = GameObject.FindObjectOfType<FadeScreen>();
            fadeScreen.FadeOut();

            float timer = 0;
            while(timer <= fadeScreen.fadeDuration)
            {
                timer += Time.deltaTime;
                yield return null;
            }
            //Launch new scene
            string sceneName = "";
            EnvironmentObject currentEnvironment = AddonsController.instance.currentEnvironmentObject;
            if (!returnToMain)
            {
                // Usar carga ASYNC del bundle de escena para no bloquear el hilo principal.
                // La versión síncrona (LoadEnvironmentScene) puede tardar >60 s en hardware lento
                // y provoca el timeout KCP de Mirror → desconexión del cliente que se une.
                if (currentEnvironment == null)
                {
                    Debug.LogError("[SceneTransitionManager] currentEnvironmentObject es null. No se puede cargar la escena.");
                    yield break;
                }
                yield return StartCoroutine(AddonsController.instance.LoadEnvironmentSceneAsync());
                sceneName = currentEnvironment.environmentName;
            }
            else
            {
                sceneName = "Main Menu";
            }

            Debug.Log($"[DEBUG] Intentando cargar la escena: '{sceneName}'");

            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("[ERROR] El nombre de la escena está vacío. Verifica que environmentName esté correctamente asignado.");
                yield break;
            }

            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            operation.allowSceneActivation = false;

            timer = 0;
            while(timer <= blackScreenDuration && !operation.isDone)
            {
                timer += Time.deltaTime;
                yield return null;
            }

            operation.allowSceneActivation = true;
            if (sceneName != "Main Menu" && currentEnvironment?.sceneBundle != null)
            {
                currentEnvironment.sceneBundle.Unload(false);
            }

        }



        public IEnumerator FadeScreenOut()
        {
            yield return StartCoroutine(fadeScreen.FadeRoutine(0, 1));
        }

        // Handle application exit
        /*private async void OnApplicationQuit()
        {
            await StandaloneWebView.TerminateBrowserProcess();
        }*/


        private void OnQuitting()
        {
            /*StartCoroutine(ClearWebData());*/
            AddonsController.instance.ClearEnvironment();
        }

        /*private IEnumerator ClearWebData()
        {
            yield return StartCoroutine(StandaloneWebView.TerminateBrowserProcess().AsIEnumerator());
        }*/


    }
    }
