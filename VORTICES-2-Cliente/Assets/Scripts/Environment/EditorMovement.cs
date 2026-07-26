using UnityEngine;

namespace Vortices
{
    public class EditorMovement : MonoBehaviour
    {
        public float moveSpeed = 5f;
        public float lookSpeed = 2f;

        // rotY se inicializa desde la rotación real del padre en Start()
        // para evitar giros bruscos al hacer click derecho por primera vez.
        private float rotY = 0f;
        private CharacterController cc;

        private void Start()
        {
#if UNITY_EDITOR
            // El CharacterController está en el XR Origin (padre del padre)
            cc = GetComponentInParent<CharacterController>();

            // Leer rotación actual del padre para que WASD apunte en la dirección correcta
            if (transform.parent != null)
                rotY = transform.parent.eulerAngles.y;
#endif
        }

        private void Update()
        {
#if UNITY_EDITOR
            if (transform.parent == null) return;

            // Click derecho + ratón → girar
            if (Input.GetMouseButton(1))
            {
                rotY += Input.GetAxis("Mouse X") * lookSpeed;
                transform.parent.rotation = Quaternion.Euler(0f, rotY, 0f);
            }

            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            Vector3 forward = transform.parent.forward;
            Vector3 right   = transform.parent.right;
            Vector3 move    = (forward * v + right * h) * moveSpeed;

            if (cc != null)
            {
                // Solo aplicar un leve empuje hacia abajo cuando estamos en suelo.
                // NO aplicar Physics.gravity cuando isGrounded==false: en asset bundles
                // el suelo puede no tener collider registrado → isGrounded siempre false
                // → caída libre infinita en el Editor.
                move.y = cc.isGrounded ? -0.5f : 0f;
                cc.Move(move * Time.deltaTime);
            }
            else
            {
                // Sin CharacterController: movimiento libre directo (flythrough)
                transform.parent.position += move * Time.deltaTime;
            }
#endif
        }
    }
}
