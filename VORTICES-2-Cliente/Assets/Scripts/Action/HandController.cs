using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using Vortices;

public class HandController : MonoBehaviour
{
    // Other references
    public Element selectElement;
    private RighthandTools righthandTools;

    // Input
    [SerializeField] InputActionProperty aPress;
    [SerializeField] InputActionProperty bPress;

    private bool _prevSecondaryButton = false;

    private void Start()
    {
        righthandTools = FindObjectOfType<RighthandTools>();

        if (bPress.action != null)
        {
            bPress.action.Enable();
            bPress.action.started += OnBPress;
        }

        if (righthandTools != null && aPress.action != null)
        {
            aPress.action.Enable();
            aPress.action.started += SelectElement;
        }
    }

    private void Update()
    {
        // Fallback XR hardware polling cuando bPress no está asignado en el Inspector
        if (bPress.action == null)
            CheckBButtonHardware();
    }

    private void CheckBButtonHardware()
    {
        var devices = new List<UnityEngine.XR.InputDevice>();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
            UnityEngine.XR.InputDeviceCharacteristics.Right | UnityEngine.XR.InputDeviceCharacteristics.Controller, devices);

        if (devices.Count == 0) return;

        devices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool pressed);
        if (pressed && !_prevSecondaryButton)
            OpenChat();
        _prevSecondaryButton = pressed;
    }

    private void OnDisable()
    {
        if (bPress.action != null)
            bPress.action.started -= OnBPress;

        if (righthandTools != null && aPress.action != null)
            aPress.action.started -= SelectElement;
    }

    private void OnBPress(InputAction.CallbackContext context) => OpenChat();

    #region Controller Actions

    public void SelectElement(InputAction.CallbackContext context)
    {
        if (selectElement != null)
            selectElement.GetComponent<Element>().SelectElement();
    }

    private void OpenChat()
    {
        if (righthandTools != null)
            righthandTools.OnChatToggleChanged(true);
        else
        {
            NewChatManager chatManager = FindObjectOfType<NewChatManager>(true);
            chatManager?.ToggleChat();
        }
    }

    #endregion
}
