using UnityEngine;
using UnityEngine.XR;

namespace SkinCutter
{
    /// <summary>
    /// Minimal OpenXR-aware VR front-end. Builds a head/controller rig from tracked
    /// poses (UnityEngine.XR.InputTracking) and drives the shared InteractionManager
    /// operations so the skin can be cut and grabbed in VR without any XR Toolkit
    /// dependency. On desktop (XRSettings disabled) it is inert and the mouse path
    /// in InteractionManager continues to work.
    /// </summary>
    public class SkinCutterVR : MonoBehaviour
    {
        public InteractionManager interaction;
        public SkinTopology topology;
        public SkinMesh skinMesh;
        public DeformationSystem deformation;
        public Camera desktopCamera;
        public float viewDistance = 2.2f;
        public float viewHeight = 1.0f;
        public float locomotionSpeed = 1.2f;

        private Transform rig;
        private Transform head;
        private Transform leftController;
        private Transform rightController;
        private bool rigBuilt;
        public bool IsVrActive => vrActive;
        private bool vrActive;
        private bool grabbed;
        private bool cutting;
        private Vector3 grabOffset;

        public void Initialize(InteractionManager interactionRef, SkinTopology topologyRef,
            SkinMesh meshRef, DeformationSystem deformationRef, Camera desktopCam)
        {
            interaction = interactionRef;
            topology = topologyRef;
            skinMesh = meshRef;
            deformation = deformationRef;
            desktopCamera = desktopCam;
        }

        private void Update()
        {
            bool nowActive = XRSettings.enabled;

            if (nowActive && !vrActive)
            {
                vrActive = true;
                if (!rigBuilt) BuildRig();
                if (desktopCamera != null) desktopCamera.enabled = false;
                var orbit = FindFirstObjectByType<OrbitCamera>();
                if (orbit != null) orbit.enabled = false;
                if (interaction != null) interaction.SetMode(ToolMode.Grab);

                QualitySettings.vSyncCount = 1;
                Application.targetFrameRate = 90;
            }

            if (!nowActive)
            {
                if (vrActive)
                {
                    vrActive = false;
                    if (desktopCamera != null) desktopCamera.enabled = true;
                }
                if (cutting) { interaction?.EndCut(); cutting = false; }
                if (grabbed) { interaction?.EndGrab(); grabbed = false; }
                return;
            }

            UpdatePoses(head, XRNode.Head);
            UpdatePoses(leftController, XRNode.LeftHand);
            UpdatePoses(rightController, XRNode.RightHand);

            if (interaction == null || topology == null) return;

            HandleLocomotion();
            HandleControllerButtons();
            HandleRightControllerInteraction();
        }

        private void BuildRig()
        {
            if (head != null) return;

            var rigGo = new GameObject("XR Rig");
            rig = rigGo.transform;
            rig.SetPositionAndRotation(
                new Vector3(0f, viewHeight, -viewDistance),
                Quaternion.identity);

            var headGo = new GameObject("XR Head");
            head = headGo.transform;
            head.SetParent(rig, false);
            var cam = headGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 40f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.102f, 0.102f, 0.18f, 1f);
            cam.allowMSAA = true;

            leftController = new GameObject("Left Controller").transform;
            rightController = new GameObject("Right Controller").transform;
            leftController.SetParent(rig, false);
            rightController.SetParent(rig, false);

            AddControllerGizmo(leftController);
            AddControllerGizmo(rightController);

            rigBuilt = true;
        }

        private static void AddControllerGizmo(Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Gizmo";
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(0.02f, 0.05f, 0.02f);
            go.transform.localPosition = Vector3.zero;
            var col = go.GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }

        private static void UpdatePoses(Transform t, XRNode node)
        {
            if (t == null) return;
            var dev = InputDevices.GetDeviceAtXRNode(node);
            if (!dev.isValid) return;
            if (dev.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos))
                t.localPosition = pos;
            if (dev.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot))
                t.localRotation = rot;
        }

        private void HandleLocomotion()
        {
            if (rig == null) return;
            var dev = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!dev.isValid) return;
            if (!dev.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis)) return;
            if (axis.sqrMagnitude < 0.05f) return;

            float speed = locomotionSpeed * Time.deltaTime;
            Vector3 move = (rig.right * axis.x + rig.forward * axis.y) * speed;
            rig.position += move;
        }

        private void HandleControllerButtons()
        {
            if (PressedThisFrame(XRNode.RightHand, CommonUsages.primaryButton))
                interaction.SetMode(ToolMode.Grab);
            if (PressedThisFrame(XRNode.RightHand, CommonUsages.secondaryButton))
                interaction.SetMode(ToolMode.Knife);
            if (PressedThisFrame(XRNode.LeftHand, CommonUsages.primaryButton))
                interaction.SetMode(ToolMode.Viewport);
            if (PressedThisFrame(XRNode.LeftHand, CommonUsages.secondaryButton))
                interaction.CycleGrabStyle();
            if (PressedThisFrame(XRNode.LeftHand, CommonUsages.primary2DAxisClick))
                GetComponent<SkinCutterDemo>()?.ToggleShape();
        }

        private bool PressedThisFrame(XRNode node, InputFeatureUsage<bool> usage)
        {
            var dev = InputDevices.GetDeviceAtXRNode(node);
            return dev.isValid && dev.TryGetFeatureValue(usage, out bool pressed) && pressed;
        }

        private static bool IsTriggerHeld(XRNode node)
        {
            var dev = InputDevices.GetDeviceAtXRNode(node);
            return dev.isValid && dev.TryGetFeatureValue(CommonUsages.trigger, out float v) && v > 0.5f;
        }

        private void HandleRightControllerInteraction()
        {
            Vector3 origin = rightController.position;
            Vector3 dir = rightController.forward;
            bool triggerHeld = IsTriggerHeld(XRNode.RightHand);

            if (interaction.mode == ToolMode.Grab)
            {
                if (interaction.grabStyle == GrabStyle.Ray)
                {
                    Ray ray = new Ray(origin, dir);
                    if (triggerHeld && !grabbed)
                    {
                        if (interaction.RaycastMesh(ray, out Vector3 p))
                            grabbed = interaction.TryBeginGrab(p, false);
                    }
                    else if (grabbed)
                    {
                        if (interaction.RaycastMesh(ray, out Vector3 p))
                            interaction.UpdateGrab(p);
                    }
                }
                else
                {
                    Vector3 tip = rightController.position + rightController.forward * 0.05f;
                    Ray tipRay = new Ray(rightController.position, rightController.forward);
                    if (triggerHeld && !grabbed)
                    {
                        if (interaction.RaycastMesh(tipRay, out Vector3 p))
                        {
                            grabbed = interaction.TryBeginGrab(p, true);
                            if (grabbed) grabOffset = rightController.position - p;
                        }
                    }
                    else if (grabbed)
                    {
                        interaction.UpdateGrab(rightController.position - grabOffset);
                    }
                }

                if (grabbed && !triggerHeld)
                {
                    interaction.EndGrab();
                    grabbed = false;
                }
            }
            else if (interaction.mode == ToolMode.Knife)
            {
                Ray ray = new Ray(origin, dir);
                bool cutHit = interaction.RaycastMesh(ray, out Vector3 point);
                if (triggerHeld && !cutting)
                {
                    if (cutHit) cutting = interaction.TryBeginCut(point);
                }
                else if (cutting && triggerHeld)
                {
                    if (cutHit) interaction.UpdateCut(point);
                }

                if (cutting && !triggerHeld)
                {
                    interaction.EndCut();
                    cutting = false;
                }
            }
        }
    }
}
