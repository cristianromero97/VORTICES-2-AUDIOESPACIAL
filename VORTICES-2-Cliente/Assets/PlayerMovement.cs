using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using UnityEngine.XR;
using Vortices;

public class PlayerMovement : NetworkBehaviour
{
    public Transform cameraTransform;
    private Transform xrOriginTransform;

    private void Start()
    {
        if (!isLocalPlayer)
        {
            return; // Solo el jugador local actualiza su propia posición
        }

        StartCoroutine(WaitForMuseumBaseAndCamera());
    }

    private void Update()
    {
        if (!isLocalPlayer || cameraTransform == null)
        {
            return;
        }

        // Mueve el PlayerPrefab a la posición de la cámara
        transform.position = cameraTransform.position;

        // Asegura que el Cube esté en la posición correcta dentro del PlayerPrefab
        Transform cubeTransform = transform.Find("Cube"); // Asegúrate de que el nombre es correcto
        if (cubeTransform != null)
        {
            cubeTransform.localPosition = Vector3.zero; // Asegura que el Cube esté alineado
        }
    }


    IEnumerator WaitForMuseumBaseAndCamera()
    {
        Debug.Log("[PlayerMovement] Esperando entorno y cámara...");

        // En Sala no existe MuseumBase → detectar entorno primero y saltarse esa espera.
        // En Museum/Circular sí existe MuseumBase y hay que esperar a que cargue.
        SessionManager sm = null;
        float smTimeout = 15f;
        while (sm == null && smTimeout > 0f)
        {
            smTimeout -= Time.deltaTime;
            yield return null;
            sm = SessionManager.instance;
        }

        bool esSala = sm != null &&
                      (sm.environmentName == "Sala" || sm.environmentName == "Sala Environment");

        if (!esSala)
        {
            Debug.Log("[PlayerMovement] Esperando a que MuseumBase cargue...");
            MuseumBase localMuseumBase = null;
            while (localMuseumBase == null)
            {
                yield return null;
                localMuseumBase = FindObjectOfType<MuseumBase>();
            }
            Debug.Log("[PlayerMovement] MuseumBase encontrado.");
        }
        else
        {
            Debug.Log("[PlayerMovement] Sala Environment — saltando espera de MuseumBase.");
        }

        while (cameraTransform == null)
        {
            cameraTransform = Camera.main?.transform;
            yield return null;
        }
        Debug.Log($"[PlayerMovement] Cámara vinculada: {cameraTransform.name}");
    }

}

