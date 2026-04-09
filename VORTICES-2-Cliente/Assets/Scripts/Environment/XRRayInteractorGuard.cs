using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[DefaultExecutionOrder(-1000)]
public class XRRayInteractorGuard : MonoBehaviour
{
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private FieldInfo samplePointsField;
    private FieldInfo sampleFrequencyField;
    private bool hasWarned;

    private void Awake()
    {
        Type rayType = typeof(XRRayInteractor);
        samplePointsField = rayType.GetField("m_SamplePoints", PrivateInstance);
        sampleFrequencyField = rayType.GetField("m_SampleFrequency", PrivateInstance);
        RepairAllInteractors();
    }

    private void OnEnable()
    {
        RepairAllInteractors();
    }

    private void Start()
    {
        StartCoroutine(RepairAfterFrame());
    }

    private IEnumerator RepairAfterFrame()
    {
        yield return null;
        RepairAllInteractors();
    }

    [ContextMenu("Repair XR Ray Interactors")]
    public void RepairAllInteractors()
    {
        if (samplePointsField == null)
        {
            if (!hasWarned)
            {
                hasWarned = true;
                Debug.LogWarning("[XRRayInteractorGuard] No se pudo acceder a m_SamplePoints. Revisa versión de XR Interaction Toolkit.");
            }
            return;
        }

        XRRayInteractor[] interactors = FindObjectsOfType<XRRayInteractor>(true);
        for (int i = 0; i < interactors.Length; i++)
        {
            XRRayInteractor interactor = interactors[i];
            if (interactor == null)
            {
                continue;
            }

            EnsureRayOrigin(interactor);
            EnsureSamplePoints(interactor);
        }
    }

    private void EnsureRayOrigin(XRRayInteractor interactor)
    {
        if (interactor.rayOriginTransform != null)
        {
            return;
        }

        GameObject rayOrigin = new GameObject("[Guard] Ray Origin");
        rayOrigin.transform.SetParent(interactor.transform, false);

        Transform attach = interactor.attachTransform;
        if (attach != null)
        {
            rayOrigin.transform.SetPositionAndRotation(attach.position, attach.rotation);
        }

        interactor.rayOriginTransform = rayOrigin.transform;
    }

    private void EnsureSamplePoints(XRRayInteractor interactor)
    {
        object current = samplePointsField.GetValue(interactor);
        if (current != null)
        {
            return;
        }

        int capacity = 2;
        if (sampleFrequencyField != null)
        {
            object freqValue = sampleFrequencyField.GetValue(interactor);
            if (freqValue is int freq && freq > 2)
            {
                capacity = freq;
            }
        }

        Type listType = samplePointsField.FieldType;
        object listInstance = Activator.CreateInstance(listType, capacity);
        samplePointsField.SetValue(interactor, listInstance);

        Debug.LogWarning("[XRRayInteractorGuard] Se reparo XRRayInteractor sin sample points: " + interactor.name, interactor);
    }
}
