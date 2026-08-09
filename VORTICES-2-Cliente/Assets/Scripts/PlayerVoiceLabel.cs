using Mirror;
using TMPro;
using UnityEngine;

public class PlayerVoiceLabel : NetworkBehaviour
{
    public enum VoiceState : byte { Silent, Speaking, Muted }

    [SyncVar(hook = nameof(OnStateChanged))]
    private VoiceState _state = VoiceState.Silent;

    [SerializeField] private TextMeshPro label;

    private float _checkTimer;
    private const float CheckInterval = 0.1f;

    private void Awake()
    {
        if (label == null)
            BuildLabel();
    }

    private void BuildLabel()
    {
        var textGO = new GameObject("VoiceLabel");
        textGO.transform.SetParent(transform);
        textGO.transform.localPosition = new Vector3(0f, 1.3f, 0f);
        textGO.transform.localScale = Vector3.one;

        label = textGO.AddComponent<TextMeshPro>();
        label.text = "hablando";
        label.fontSize = 1.5f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.green;
        textGO.SetActive(false);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyState(_state);
    }

    private void Update()
    {
        BillboardLabel();

        if (!isLocalPlayer) return;

        _checkTimer += Time.deltaTime;
        if (_checkTimer < CheckInterval) return;
        _checkTimer = 0f;

        VoiceState newState = ComputeState();
        if (newState != _state)
            CmdSetVoiceState(newState);
    }

    private void BillboardLabel()
    {
        if (label == null) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 dir = label.transform.position - cam.transform.position;
        if (dir.sqrMagnitude > 0.001f)
            label.transform.rotation = Quaternion.LookRotation(dir);
    }

    private VoiceState ComputeState()
    {
        if (VivoxVoiceManager.Instance == null) return VoiceState.Silent;
        if (VivoxVoiceManager.Instance.IsMuted) return VoiceState.Muted;
        if (VivoxVoiceManager.Instance.IsLocalSpeaking) return VoiceState.Speaking;
        return VoiceState.Silent;
    }

    [Command]
    private void CmdSetVoiceState(VoiceState state) => _state = state;

    private void OnStateChanged(VoiceState _, VoiceState newState) => ApplyState(newState);

    private void ApplyState(VoiceState state)
    {
        if (label == null) return;
        switch (state)
        {
            case VoiceState.Silent:
                label.gameObject.SetActive(false);
                break;
            case VoiceState.Speaking:
                label.gameObject.SetActive(true);
                label.text = "hablando";
                label.color = Color.green;
                break;
            case VoiceState.Muted:
                label.gameObject.SetActive(true);
                label.text = "silenciado";
                label.color = Color.red;
                break;
        }
    }
}
