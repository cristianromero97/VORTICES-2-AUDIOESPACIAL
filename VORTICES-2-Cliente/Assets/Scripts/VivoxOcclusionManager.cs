using UnityEngine;
using Unity.Services.Vivox;

/// <summary>
/// Aplica oclusión de audio de voz Vivox mediante raycasts.
/// Cuando hay geometría entre el jugador local y un jugador remoto,
/// reduce el volumen local de ese participante en el canal posicional.
/// Se instancia desde SessionManager al unirse al canal posicional.
///
/// SETUP: crear un Layer llamado "AudioOccluder" en Unity (Edit → Project Settings → Tags and Layers)
/// y asignarlo a los objetos que deben ocluir audio (paredes, divisores, etc.).
/// Mesas, sillas y props pequeños NO deben tener ese layer — no ocluirán.
/// </summary>
public class VivoxOcclusionManager : MonoBehaviour
{
    private const float CheckInterval = 0.3f;
    private const int OccludedVolume = -30; // rango -50..50, default 0
    private const int ClearVolume = 0;
    private const string OccluderLayerName = "AudioOccluder";

    private float _timer;
    private string _channelName;
    private int _occluderMask;

    public void Init(string positionalChannelName)
    {
        _channelName = positionalChannelName;
        _occluderMask = LayerMask.GetMask(OccluderLayerName);

        if (_occluderMask == 0)
            Debug.LogWarning("[VivoxOcclusion] Layer 'AudioOccluder' no encontrado. " +
                "Créalo en Edit → Project Settings → Tags and Layers y asígnalo a las paredes.");
    }

    private void Update()
    {
        _timer += Time.deltaTime;
        if (_timer < CheckInterval) return;
        _timer = 0f;
        CheckOcclusion();
    }

    private void CheckOcclusion()
    {
        if (string.IsNullOrEmpty(_channelName)) return;
        if (VivoxService.Instance?.ActiveChannels == null) return;
        if (!VivoxService.Instance.ActiveChannels.ContainsKey(_channelName)) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        var participants = VivoxService.Instance.ActiveChannels[_channelName];
        var remotePlayers = FindObjectsOfType<PlayerConnId>();

        foreach (var player in remotePlayers)
        {
            if (player.isLocalPlayer || player.vivoxUserId < 0) continue;

            VivoxParticipant match = FindParticipant(participants, player.vivoxUserId);
            if (match == null) continue;

            bool occluded = _occluderMask != 0 &&
                IsOccluded(cam.transform.position, player.transform.position);
            match.SetLocalVolume(occluded ? OccludedVolume : ClearVolume);
        }
    }

    private static VivoxParticipant FindParticipant(
        System.Collections.Generic.IReadOnlyCollection<VivoxParticipant> participants, int userId)
    {
        string target = userId.ToString();
        foreach (var p in participants)
        {
            if (!p.IsSelf && p.DisplayName == target)
                return p;
        }
        return null;
    }

    private bool IsOccluded(Vector3 from, Vector3 to)
    {
        if (_occluderMask == 0) return false; // layer no configurado → sin oclusión
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        // Solo pega contra objetos en el layer AudioOccluder — mesas y props no ocluyen
        return Physics.Raycast(from, dir.normalized, dist - 0.5f,
            _occluderMask, QueryTriggerInteraction.Ignore);
    }
}
