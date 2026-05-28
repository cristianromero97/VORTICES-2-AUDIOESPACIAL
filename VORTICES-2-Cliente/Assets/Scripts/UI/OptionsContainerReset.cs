using UnityEngine;

namespace Vortices
{
    // Colocado en el contenedor "Options" (hijo directo de Option Screen).
    // Cada vez que Options se activa (al presionar el toggle de Options en el menú),
    // resetea el estado interno: muestra Options Panel y oculta Spatial Audio Panel.
    public class OptionsContainerReset : MonoBehaviour
    {
        [SerializeField] private OptionsPanel optionsPanel;

        private void OnEnable()
        {
            if (optionsPanel != null)
                optionsPanel.ResetToDefault();
        }
    }
}
