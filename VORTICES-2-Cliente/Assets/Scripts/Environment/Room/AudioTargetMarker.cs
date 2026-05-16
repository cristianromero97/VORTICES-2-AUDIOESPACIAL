using System.Collections.Generic;
using UnityEngine;

namespace Vortices
{
    [DisallowMultipleComponent]
    public class AudioTargetMarker : MonoBehaviour
    {
        // Registro global de todos los markers activos en escena
        public static readonly List<AudioTargetMarker> All = new List<AudioTargetMarker>();

        public int            roomIndex;
        public string         prefabType;
        public string         labelDisplayName; // Nombre mostrado en el label (sin sufijo numérico)
        public string         audioFileName;    // Nombre del archivo sin extensión
        public AudioSource    userAudioSource;
        public FurnitureLabel furnitureLabel;

        private void OnEnable()  { All.Add(this); }
        private void OnDisable() { All.Remove(this); }
    }
}
