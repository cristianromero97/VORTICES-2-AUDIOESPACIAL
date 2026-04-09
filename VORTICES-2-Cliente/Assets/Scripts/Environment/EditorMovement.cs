using UnityEngine;

namespace Vortices
{
    public class EditorMovement : MonoBehaviour
    {
        public float moveSpeed = 5f;
        public float lookSpeed = 2f;

        private float rotY = 0f;
        private CharacterController cc;

        private void Start()
        {
#if UNITY_EDITOR
            // El CharacterController está en el XR Origin (padre del padre)
            cc = GetComponentInParent<CharacterController>();
#endif
        }

        private void Update()
        {
#if UNITY_EDITOR
            if (transform.parent == null)
            {
                return;
            }

            if (Input.GetMouseButton(1))
            {
                rotY += Input.GetAxis("Mouse X") * lookSpeed;
                transform.parent.rotation = Quaternion.Euler(0f, rotY, 0f);
            }

            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            Vector3 forward = transform.parent.forward;
            Vector3 right = transform.parent.right;
            Vector3 move = (forward * v + right * h) * moveSpeed;

            if (cc != null)
            {
                if (!cc.isGrounded)
                {
                    move.y = Physics.gravity.y;
                }
                else
                {
                    move.y = -0.5f;
                }

                cc.Move(move * Time.deltaTime);
            }
#endif
        }
    }
}
