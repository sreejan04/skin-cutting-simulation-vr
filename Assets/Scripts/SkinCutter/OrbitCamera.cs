using UnityEngine;
using UnityEngine.InputSystem;

namespace SkinCutter
{
    public class OrbitCamera : MonoBehaviour
    {
        public Vector3 target = Vector3.zero;
        public float distance = 6f;
        public float minDistance = 1f;
        public float maxDistance = 15f;
        [Range(10f, 89f)] public float maxPolarDegrees = 72f;

        private float yaw;
        private float pitch;
        private bool toolModeActive;

        public void SetToolModeActive(bool active)
        {
            toolModeActive = active;
        }

        private void LateUpdate()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            bool rotateHeld = toolModeActive ? mouse.rightButton.isPressed : mouse.leftButton.isPressed;
            bool panHeld = !toolModeActive && mouse.rightButton.isPressed;
            Vector2 delta = mouse.delta.ReadValue();
            Vector2 scroll = mouse.scroll.ReadValue();

            if (rotateHeld)
            {
                yaw += delta.x * 0.25f;
                pitch = Mathf.Clamp(pitch + delta.y * 0.25f, -89f, maxPolarDegrees);
            }

            if (panHeld)
            {
                float worldPerPixel = distance * 0.00125f;
                target -= transform.right * (delta.x * worldPerPixel);
                target -= transform.up * (delta.y * worldPerPixel);
            }

            if (scroll.y != 0f)
            {
                distance = Mathf.Clamp(distance * (scroll.y > 0f ? 0.9f : 1.1f), minDistance, maxDistance);
            }
            else if (mouse.middleButton.isPressed && Mathf.Abs(delta.y) > 0f)
            {
                distance = Mathf.Clamp(distance * (1f - delta.y * 0.005f), minDistance, maxDistance);
            }

            Apply();
        }

        private void Apply()
        {
            float yawRad = yaw * Mathf.Deg2Rad;
            float pitchRad = pitch * Mathf.Deg2Rad;

            var offset = new Vector3(
                Mathf.Sin(yawRad) * Mathf.Cos(pitchRad),
                Mathf.Sin(pitchRad),
                Mathf.Cos(yawRad) * Mathf.Cos(pitchRad)) * distance;

            transform.position = target + offset;
            transform.LookAt(target);
        }
    }
}
