using UnityEngine;

namespace SkinCutting
{
    [DisallowMultipleComponent]
    public sealed class SkinOrbitCamera : MonoBehaviour
    {
        public Vector3 Target = Vector3.zero;
        public float Distance = 6.2f;
        public float Yaw = 0f;
        public float Pitch = 8f;
        public float MinDistance = 2.2f;
        public float MaxDistance = 11f;
        public float MinPitch = -65f;
        public float MaxPitch = 65f;
        public float RotateSpeed = 4f;
        public float ZoomSpeed = 0.9f;

        public void ResetView()
        {
            Yaw = 0f;
            Pitch = 8f;
            Distance = 6.2f;
            Apply();
        }

        private void LateUpdate()
        {
            if (Input.GetMouseButton(1))
            {
                Yaw += Input.GetAxis("Mouse X") * RotateSpeed;
                Pitch -= Input.GetAxis("Mouse Y") * RotateSpeed;
                Pitch = Mathf.Clamp(Pitch, MinPitch, MaxPitch);
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.001f)
            {
                Distance = Mathf.Clamp(Distance - scroll * ZoomSpeed, MinDistance, MaxDistance);
            }

            Apply();
        }

        public void Apply()
        {
            Quaternion rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, Distance);
            Vector3 position = Target + offset;
            transform.position = position;
            transform.rotation = Quaternion.LookRotation(Target - position, Vector3.up);
        }
    }
}
