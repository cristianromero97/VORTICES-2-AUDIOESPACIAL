using UnityEngine;

namespace Vortices
{
    [DisallowMultipleComponent]
    public class AudioTargetMarker : MonoBehaviour
    {
        public int         roomIndex;
        public string      prefabType;
        public string      audioFileName;  // Nombre del archivo sin extensión — asignado por SessionManager
        public AudioSource userAudioSource; // AudioSource dedicado para el audio de actividad
    }
}
