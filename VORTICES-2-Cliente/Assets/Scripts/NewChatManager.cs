using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Mirror;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Vortices;


public class NewChatManager : NetworkBehaviour
{
    [Header("Chat UI Components")]
    public GameObject chatCanvas;
    public ScrollRect scrollRect;
    public TMP_InputField chatInputField;
    public TMP_Text chatDisplay;
    public Button sendButton;

    // AÑADIDO: Singleton estático para asegurar que solo existe una instancia
    private static NewChatManager _instance;

    private void Awake()
    {
        // AÑADIDO: Singleton — si ya existe una instancia, destruir esta ANTES de Start
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning("[NewChatManager] AÑADIDO: Ya existe una instancia de ChatCanvas, destruyendo duplicada en Awake.");
            Destroy(gameObject);
            return;
        }
        _instance = this;
        Debug.Log("[NewChatManager] AÑADIDO: Instancia singleton registrada en Awake.");

        // AÑADIDO: Chat solo en Sala - destruir en otros entornos
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (!sceneName.Contains("Sala"))
        {
            Debug.Log($"[NewChatManager] AÑADIDO: Chat no habilitado en {sceneName}. Destruyendo.");
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {

        // AÑADIDO: Solo desactivar los elementos visuales si no es servidor, NO el Canvas completo
        // (si desactivamos el Canvas, Update() deja de ejecutarse y F2 no funciona)
        if (scrollRect != null) scrollRect.gameObject.SetActive(false);
        if (chatInputField != null) chatInputField.gameObject.SetActive(false);
        if (chatDisplay != null) chatDisplay.gameObject.SetActive(false);
        if (sendButton != null) sendButton.gameObject.SetActive(false);

        Debug.Log($"[NewChatManager] AÑADIDO: Inicializado en: {gameObject.name}. Es servidor: {isServer}");

        // Asegurar que el ChatCanvas no se destruya al cambiar de escena
        DontDestroyOnLoad(gameObject);

        // AÑADIDO: Asignar Main Camera al Canvas para que sea interactivo en VR
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera)
        {
            if (Camera.main != null)
            {
                canvas.worldCamera = Camera.main;
                Debug.Log("[NewChatManager] AÑADIDO: Main Camera asignada al Canvas inmediatamente.");
            }
            else
            {
                // Si Camera.main no está disponible aún, esperar un frame y reintentar
                StartCoroutine(AssignCameraWhenReady(canvas));
            }
        }

        // Vincular el botón de enviar
        sendButton.onClick.AddListener(OnSendButtonPressed);

        // AÑADIDO: Vincular Enter key en el InputField para enviar
        if (chatInputField != null)
        {
            chatInputField.onSubmit.AddListener((_) => OnSendButtonPressed());
            Debug.Log("[NewChatManager] AÑADIDO: Enter key vinculado al InputField.");
        }
    }

    // AÑADIDO: Esperar a que Main Camera esté disponible y asignarlo al Canvas
    private IEnumerator AssignCameraWhenReady(Canvas canvas)
    {
        int maxAttempts = 30; // 3 segundos máximo
        int attempts = 0;
        while (Camera.main == null && attempts < maxAttempts)
        {
            yield return new WaitForSeconds(0.1f);
            attempts++;
        }

        if (Camera.main != null)
        {
            canvas.worldCamera = Camera.main;
            Debug.Log("[NewChatManager] AÑADIDO: Main Camera asignada al Canvas después de esperar.");
        }
        else
        {
            Debug.LogWarning("[NewChatManager] AÑADIDO: No se pudo encontrar Main Camera después de 3 segundos.");
        }
    }

    // F2 (teclado) abre/cierra el chat — en VR usar botón B (HandController)
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F2))
            ToggleChat();
    }


    public void ToggleChat()
    {
        bool newState = false;
        if (chatInputField != null)
        {
            newState = !chatInputField.gameObject.activeSelf;
            chatInputField.gameObject.SetActive(newState);

            if (newState)
            {
                // Reposicionar frente al jugador al abrir
                Camera cam = Camera.main;
                if (cam != null)
                {
                    chatCanvas.transform.position = cam.transform.position + cam.transform.forward * 1.5f;
                    chatCanvas.transform.rotation = Quaternion.LookRotation(cam.transform.forward);
                }

                chatInputField.ActivateInputField();
                chatInputField.Select();
            }
            else
            {
                chatInputField.DeactivateInputField();
                EventSystem.current.SetSelectedGameObject(null);
            }
        }
        if (scrollRect != null) scrollRect.gameObject.SetActive(newState);
        if (chatDisplay != null) chatDisplay.gameObject.SetActive(newState);
        if (sendButton != null) sendButton.gameObject.SetActive(newState);
    }

    // Llamado cuando se presiona el botón de enviar
    public void OnSendButtonPressed()
    {
        if (chatInputField == null || string.IsNullOrEmpty(chatInputField.text))
        {
            Debug.LogWarning("[NewChatManager] No se puede enviar un mensaje vacío.");
            return;
        }

        string message = chatInputField.text;

        // Obtener el jugador local
        GameObject playerObject = NetworkClient.localPlayer?.gameObject;
        if (playerObject == null)
        {
            Debug.LogError("[NewChatManager] Objeto jugador local no encontrado.");
            return;
        }

        // Obtener el controlador de chat del jugador
        PlayerChatController playerChatController = playerObject.GetComponent<PlayerChatController>();
        if (playerChatController == null)
        {
            Debug.LogError("[NewChatManager] PlayerChatController no encontrado en el jugador.");
            return;
        }

        // Obtener el userId desde el SessionManager
        string userId = FindObjectOfType<SessionManager>()?.userId.ToString();
        if (string.IsNullOrEmpty(userId))
        {
            Debug.LogError("[NewChatManager] UserID no configurado.");
            return;
        }

        // Enviar el mensaje al servidor
        Debug.Log($"[NewChatManager] Enviando mensaje: {message} de userId: {userId}");
        playerChatController.CmdSendMessageToChat(userId, message);

        // Limpiar el campo de texto
        chatInputField.text = "";
    }




    [ClientRpc]
    public void RpcReceiveMessage(string userId, string message)
    {
        Debug.Log($"[NewChatManager] Mensaje recibido de {userId}: {message}");

        if (chatDisplay != null)
        {
            chatDisplay.text += $"Usuario {userId}: {message}\n";
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }
        else
        {
            Debug.LogError("[NewChatManager] chatDisplay no está asignado.");
        }
    }


}
