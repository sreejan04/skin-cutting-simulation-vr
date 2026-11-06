using System;
using UnityEngine;
using UnityEngine.XR;

namespace SkinCutter.Live
{
    public class SkinCutterXRRig : MonoBehaviour
    {
        public LiveInteractionManager interaction;
        public Camera desktopCamera;
        public float viewDistance = 2.2f;
        public float viewHeight = 1.0f;
        public float locomotionSpeed = 1.2f;

        public Action OnToggleShape;
        public Action OnReset;

        public bool IsVrActive => vrActive;

        private Transform rig;
        private Transform head;
        private Transform leftController;
        private Transform rightController;
        private bool rigBuilt;
        private bool vrActive;
        private bool grabbed;
        private bool cutting;
        private Vector3 grabOffset;

        private bool prevPrimaryRight;
        private bool prevSecondaryRight;
        private bool prevPrimaryLeft;
        private bool prevSecondaryLeft;
        private bool prevStickClickLeft;
        private bool prevGripRight;

        public void Initialize(LiveInteractionManager interactionRef, Camera desktopCam)
        {
            interaction = interactionRef;
            desktopCamera = desktopCam;
            grabbed = false;
            cutting = false;
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
                if (interaction != null) interaction.SetMode(LiveToolMode.Grab);

                QualitySettings.vSyncCount = 0;
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

            if (interaction == null) return;

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
                new Vector3(0f, viewHeight, viewDistance),
                Quaternion.Euler(0f, 180f, 0f));

            var headGo = new GameObject("XR Head");
            head = headGo.transform;
            head.SetParent(rig, false);
            var cam = headGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 40f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.102f, 0.102f, 0.18f, 1f);
            cam.allowMSAA = false;

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
            bool pr = Held(XRNode.RightHand, CommonUsages.primaryButton);
            bool sr = Held(XRNode.RightHand, CommonUsages.secondaryButton);
            bool pl = Held(XRNode.LeftHand, CommonUsages.primaryButton);
            bool sl = Held(XRNode.LeftHand, CommonUsages.secondaryButton);
            bool cl = Held(XRNode.LeftHand, CommonUsages.primary2DAxisClick);
            bool gr = Held(XRNode.RightHand, CommonUsages.gripButton);

            if (pr && !prevPrimaryRight)
            {
                interaction.SetMode(LiveToolMode.Grab);
                Pulse(XRNode.RightHand, 0.3f, 0.05f);
                Debug.Log("[SkinCutter] Mode: Grab");
            }
            if (sr && !prevSecondaryRight)
            {
                interaction.SetMode(LiveToolMode.Knife);
                Pulse(XRNode.RightHand, 0.5f, 0.05f);
                Debug.Log("[SkinCutter] Mode: Knife");
            }
            if (pl && !prevPrimaryLeft)
            {
                interaction.SetMode(LiveToolMode.Viewport);
                Pulse(XRNode.LeftHand, 0.3f, 0.05f);
                Debug.Log("[SkinCutter] Mode: Viewport");
            }
            if (sl && !prevSecondaryLeft)
            {
                interaction.CycleGrabStyle();
                Pulse(XRNode.LeftHand, 0.3f, 0.05f);
                Debug.Log("[SkinCutter] Grab style: " + interaction.grabStyle);
            }
            if (cl && !prevStickClickLeft)
            {
                OnToggleShape?.Invoke();
                Pulse(XRNode.LeftHand, 0.4f, 0.05f);
                Debug.Log("[SkinCutter] Toggle shape");
            }
            if (gr && !prevGripRight)
            {
                LiveToolMode next = interaction.mode == LiveToolMode.Knife
                    ? LiveToolMode.Grab
                    : LiveToolMode.Knife;
                interaction.SetMode(next);
                Pulse(XRNode.RightHand, 0.5f, 0.05f);
                Debug.Log("[SkinCutter] Mode: " + next);
            }

            prevPrimaryRight = pr;
            prevSecondaryRight = sr;
            prevPrimaryLeft = pl;
            prevSecondaryLeft = sl;
            prevStickClickLeft = cl;
            prevGripRight = gr;
        }

        private static bool Held(XRNode node, InputFeatureUsage<bool> usage)
        {
            var dev = InputDevices.GetDeviceAtXRNode(node);
            return dev.isValid && dev.TryGetFeatureValue(usage, out bool v) && v;
        }

        private static bool IsTriggerHeld(XRNode node)
        {
            var dev = InputDevices.GetDeviceAtXRNode(node);
            if (!dev.isValid) return false;
            if (dev.TryGetFeatureValue(CommonUsages.trigger, out float v) && v > 0.5f) return true;
            return dev.TryGetFeatureValue(CommonUsages.triggerButton, out bool pressed) && pressed;
        }

        private static void Pulse(XRNode node, float amplitude, float duration)
        {
            var dev = InputDevices.GetDeviceAtXRNode(node);
            if (dev.isValid) dev.SendHapticImpulse(0u, amplitude, duration);
        }

        private void HandleRightControllerInteraction()
        {
            if (rightController == null) return;

            Vector3 origin = rightController.position;
            Vector3 dir = rightController.forward;
            bool triggerHeld = IsTriggerHeld(XRNode.RightHand);

            if (interaction.mode == LiveToolMode.Grab)
            {
                if (interaction.grabStyle == LiveGrabStyle.Ray)
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
                    if (triggerHeld && !grabbed)
                    {
                        if (interaction.TryGetSurfacePoint(origin, dir, 0.12f, out Vector3 p))
                        {
                            grabbed = interaction.TryBeginGrab(p, true);
                            if (grabbed)
                            {
                                grabOffset = origin - p;
                                Pulse(XRNode.RightHand, 0.4f, 0.05f);
                            }
                        }
                    }
                    else if (grabbed)
                    {
                        interaction.UpdateGrab(origin - grabOffset);
                    }
                }

                if (grabbed && !triggerHeld)
                {
                    interaction.EndGrab();
                    grabbed = false;
                }
            }
            else if (interaction.mode == LiveToolMode.Knife)
            {
                if (triggerHeld || cutting)
                {
                    Vector3 probe = origin + dir * 0.08f;
                    bool cutHit = interaction.TryGetCutPoint(probe, dir, 0.15f, out Vector3 point);
                    if (triggerHeld && !cutting)
                    {
                        if (cutHit)
                        {
                            cutting = interaction.BeginCut(point);
                            if (cutting) Pulse(XRNode.RightHand, 0.6f, 0.03f);
                        }
                    }
                    else if (cutting && triggerHeld)
                    {
                        if (cutHit) interaction.UpdateCut(point);
                    }
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
