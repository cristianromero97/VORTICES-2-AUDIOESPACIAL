using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Vivox;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.XR;
#if AUTH_PACKAGE_PRESENT
using Unity.Services.Authentication;
#endif

public class VivoxVoiceManager : MonoBehaviour
{
    public const string LobbyChannelName = "VoRTIcESVoiceChat";

    private static VivoxVoiceManager _instance;

    [SerializeField] private string _key;
    [SerializeField] private string _issuer;
    [SerializeField] private string _domain;
    [SerializeField] private string _server;

    public static VivoxVoiceManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<VivoxVoiceManager>();

                if (_instance == null)
                {
                    GameObject singletonObject = new GameObject();
                    _instance = singletonObject.AddComponent<VivoxVoiceManager>();
                    singletonObject.name = typeof(VivoxVoiceManager).ToString() + " (Singleton)";
                }
            }
            DontDestroyOnLoad(_instance.gameObject);
            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance != this && _instance != null)
        {
            Destroy(this);
        }

        InitializeVivoxService();
    }

    private async void InitializeVivoxService()
    {
        var options = new InitializationOptions();
        if (!string.IsNullOrEmpty(_server) && !string.IsNullOrEmpty(_domain) && !string.IsNullOrEmpty(_issuer) && !string.IsNullOrEmpty(_key))
        {
            options.SetVivoxCredentials(_server, _domain, _issuer, _key);
        }

        try
        {
            await UnityServices.InitializeAsync(options);
            Debug.Log("Unity Services and Vivox initialized.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to initialize Unity Services or Vivox: {ex.Message}");
        }
    }

    public async Task LoginAsync(string playerName)
    {
        if (!UnityServices.State.Equals(ServicesInitializationState.Initialized))
        {
            Debug.LogError("Unity Services not initialized.");
            return;
        }

#if AUTH_PACKAGE_PRESENT
        try
        {
            AuthenticationService.Instance.SwitchProfile(playerName);
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log($"Logged into Unity Authentication as {playerName}.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Unity Authentication failed: {ex.Message}");
        }
#endif

        try
        {
            await VivoxService.Instance.LoginAsync(new LoginOptions { DisplayName = playerName }); // QUITAR EN CASO DE VOLVER A LA VERSION ANTERIOR: reemplazar por → await VivoxService.Instance.LoginAsync();
            Debug.Log($"Logged into Vivox as {playerName}.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Vivox login failed: {ex.Message}");
        }
    }

    public async Task JoinChannelAsync(string channelName)
    {
        try
        {
            await VivoxService.Instance.JoinGroupChannelAsync(channelName, ChatCapability.AudioOnly);
            Debug.Log($"Joined Vivox channel: {channelName}.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to join Vivox channel: {ex.Message}");
        }
    }

    /// <summary>
    /// Une al usuario a un canal posicional 3D de Vivox.
    /// El volumen se atenúa según la distancia entre jugadores.
    /// Después de unirse, llamar periódicamente a UpdatePosition3D con la posición de la cámara.
    /// </summary>
    /// <param name="channelName">Nombre del canal posicional.</param>
    /// <param name="audibleDistance">Distancia máxima de escucha (unidades de Unity). Por defecto 25.</param>
    /// <param name="conversationalDistance">Distancia a la que se escucha al 100%. Por defecto 2.</param>
    public async Task JoinPositionalChannelAsync(string channelName, int audibleDistance = 14, int conversationalDistance = 4)
    {
        try
        {
            var props = new Channel3DProperties(audibleDistance, conversationalDistance, 1.0f, AudioFadeModel.LinearByDistance);
            await VivoxService.Instance.JoinPositionalChannelAsync(channelName, ChatCapability.AudioOnly, props);
            Debug.Log($"[VivoxVoiceManager] Joined positional channel: {channelName} (audibleDist={audibleDistance}, convDist={conversationalDistance}).");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VivoxVoiceManager] Failed to join positional channel '{channelName}': {ex.Message}");
        }
    }

    /// <summary>
    /// Actualiza la posición del oyente local en el canal posicional.
    /// Debe llamarse periódicamente (ej. cada 0.1 s) mientras el canal posicional esté activo.
    /// speakerPos y listenerPos son típicamente la misma posición (la cámara/cabeza del jugador).
    /// </summary>
    public void UpdatePosition3D(string channelName, Vector3 speakerPos, Vector3 listenerPos, Vector3 atOrient, Vector3 upOrient)
    {
        try
        {
            VivoxService.Instance.Set3DPosition(speakerPos, listenerPos, atOrient, upOrient, channelName);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VivoxVoiceManager] Failed to update 3D position in '{channelName}': {ex.Message}");
        }
    }

    public async Task LeaveChannelAsync(string channelName)
    {
        try
        {
            await VivoxService.Instance.LeaveChannelAsync(channelName);
            Debug.Log($"Left Vivox channel: {channelName}.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to leave Vivox channel: {ex.Message}");
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            await VivoxService.Instance.LogoutAsync();
            Debug.Log("Logged out of Vivox.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to logout of Vivox: {ex.Message}");
        }
    }

    public async Task EnsureVivoxInitialized()
    {
        Debug.Log("[VoiceChat] Inicializando Vivox Service...");

        var options = new InitializationOptions();
        if (CheckManualCredentials())
        {
            options.SetVivoxCredentials(_server, _domain, _issuer, _key);
        }

        await UnityServices.InitializeAsync(options);
        await VivoxService.Instance.InitializeAsync();

        Debug.Log("[VoiceChat] Vivox Service inicializado correctamente.");
    }

    bool CheckManualCredentials()
    {
        return !(string.IsNullOrEmpty(_issuer) && string.IsNullOrEmpty(_domain) && string.IsNullOrEmpty(_server));
    }

    // ─────────────────────────────────────────────
    //  Mute de micrófono
    // ─────────────────────────────────────────────

    private bool _isMuted = false;
    private bool _prevPrimaryLeft = false;
    public bool IsMuted => _isMuted;

    public bool IsLocalSpeaking
    {
        get
        {
            try
            {
                if (VivoxService.Instance?.ActiveChannels == null) return false;
                foreach (var participants in VivoxService.Instance.ActiveChannels.Values)
                    foreach (var p in participants)
                        if (p.IsSelf) return p.SpeechDetected;
            }
            catch { /* Vivox not yet connected */ }
            return false;
        }
    }

    private void Update()
    {
        CheckMuteInput();
    }

    private void CheckMuteInput()
    {
        // Teclado: Ctrl+M
        if (Input.GetKeyDown(KeyCode.M) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
        {
            ToggleMute();
            return;
        }

        // VR: botón X (primaryButton controlador izquierdo)
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, devices);

        if (devices.Count == 0) return;

        devices[0].TryGetFeatureValue(CommonUsages.primaryButton, out bool pressed);
        if (pressed && !_prevPrimaryLeft) ToggleMute();
        _prevPrimaryLeft = pressed;
    }

    public void ToggleMute()
    {
        if (_isMuted) UnmuteMicrophone();
        else MuteMicrophone();
    }

    public void MuteMicrophone()
    {
        try
        {
            VivoxService.Instance.MuteInputDevice();
            _isMuted = true;
            Debug.Log("[VivoxVoiceManager] Micrófono MUTEADO");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VivoxVoiceManager] Error al mutear micrófono: {ex.Message}");
        }
    }

    public void UnmuteMicrophone()
    {
        try
        {
            VivoxService.Instance.UnmuteInputDevice();
            _isMuted = false;
            Debug.Log("[VivoxVoiceManager] Micrófono ACTIVADO");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VivoxVoiceManager] Error al desmutear micrófono: {ex.Message}");
        }
    }

}
