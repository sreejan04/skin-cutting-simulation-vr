using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;

namespace SkinCutter.Live
{
    public class SkinCutterVRDemo : MonoBehaviour
    {
        private const int Rows = 30;
        private const int Cols = 30;
        private const float Width = 4f;
        private const float Height = 4f;
        private const float BoxSize = 3f;
        private const int BoxSubdiv = 12;

        private static readonly Color BackgroundColor = new Color32(0x1a, 0x1a, 0x2e, 255);
        private static readonly Color SkinColor = new Color32(0xe8, 0xb8, 0x9a, 255);

        private const int SolverIters = 14;
        private const int SolverItersFast = 8;
        private const float SolverHz = 60f;

        private bool showBox;
        private float normalRecalcTimer;
        private float fpsLogTimer;
        private int fpsFrames;

        private LiveSkinTopology topo;
        private LiveSkinMesh skinMesh;
        private LiveDeformationSystem deformation;
        private LiveInteractionManager interaction;
        private SkinCutterXRRig xrRig;
        private OrbitCamera orbitCam;
        private Camera mainCam;
        private Transform skinRoot;
        private Material skinMaterial;
        private TextMesh modeText;
        private LiveToolMode lastMode = (LiveToolMode)(-1);
        private LiveGrabStyle lastStyle = (LiveGrabStyle)(-1);
        private float indicatorTimer;
        private int indicatorFrames;

        private void Start()
        {
            SetupCamera();
            SetupLights();
            skinMaterial = CreateSkinMaterial();
            InitCommon(false);
            CreateModeIndicator();
            SetupXR();
        }

        private void Update()
        {
            HandleShortcuts();
            UpdateModeIndicator();

            if (interaction == null || deformation == null || skinMesh == null) return;

            if (interaction.IsDragging && interaction.mode == LiveToolMode.Grab)
            {
                bool vr = xrRig != null && xrRig.IsVrActive;

                float dt = Time.deltaTime;
                float iters;
                if (vr)
                {
                    double rem = Screen.currentResolution.refreshRateRatio.value > 90.0 ? 120.0 : 90.0;
                    iters = dt >= 1.0f / (float)rem ? SolverItersFast : SolverIters;
                }
                else
                {
                    iters = dt >= 1f / SolverHz ? SolverItersFast : SolverIters;
                }

                deformation.Step((int)iters);

                normalRecalcTimer += Time.deltaTime;
                bool recalcEveryFrame = normalRecalcTimer > 0.1f;
                skinMesh.Upload(false, recalcEveryFrame);
                if (recalcEveryFrame) normalRecalcTimer = 0f;

                fpsLogTimer += Time.deltaTime;
                fpsFrames++;
                if (fpsLogTimer >= 2f && vr)
                {
                    Debug.Log($"[SkinCutterLive] FPS={fpsFrames / fpsLogTimer:F0} frameMs={Time.deltaTime * 1000f:F1} iters={iters}");
                    fpsLogTimer = 0f;
                    fpsFrames = 0;
                }
            }
        }

        private void HandleShortcuts()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.vKey.wasPressedThisFrame && interaction != null) interaction.SetMode(LiveToolMode.Viewport);
            if (kb.gKey.wasPressedThisFrame && interaction != null) interaction.SetMode(LiveToolMode.Grab);
            if (kb.kKey.wasPressedThisFrame && interaction != null) interaction.SetMode(LiveToolMode.Knife);
            if (kb.bKey.wasPressedThisFrame) ToggleShape();
            if (kb.rKey.wasPressedThisFrame) ResetDemo();
        }

        private void SetupCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
                cam.depth = -1f;
            }

            mainCam = cam;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BackgroundColor;
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 40f;
            cam.transform.position = new Vector3(0f, 0f, 6f);
            cam.transform.rotation = Quaternion.identity;

            orbitCam = cam.GetComponent<OrbitCamera>();
            if (orbitCam == null) orbitCam = cam.gameObject.AddComponent<OrbitCamera>();
        }

        private void SetupXR()
        {
            if (xrRig != null) return;
            xrRig = gameObject.AddComponent<SkinCutterXRRig>();
            xrRig.Initialize(interaction, mainCam);
            xrRig.OnToggleShape = ToggleShape;
            xrRig.OnReset = ResetDemo;
        }

        private void CreateModeIndicator()
        {
            var go = new GameObject("ModeIndicator");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 2.35f, 0.05f);
            go.transform.localRotation = Quaternion.identity;

            modeText = go.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            modeText.font = font;
            modeText.fontSize = 64;
            modeText.characterSize = 0.05f;
            modeText.anchor = TextAnchor.MiddleCenter;
            modeText.alignment = TextAlignment.Center;
            modeText.color = Color.white;
            modeText.text = "";
        }

        private void UpdateModeIndicator()
        {
            if (modeText == null || interaction == null) return;

            indicatorTimer += Time.unscaledDeltaTime;
            indicatorFrames++;

            bool changed = interaction.mode != lastMode || interaction.grabStyle != lastStyle;
            if (!changed && indicatorTimer < 0.25f) return;

            float fps = indicatorTimer > 0f ? indicatorFrames / indicatorTimer : 0f;
            indicatorTimer = 0f;
            indicatorFrames = 0;
            lastMode = interaction.mode;
            lastStyle = interaction.grabStyle;
            modeText.text = $"Mode: {interaction.mode}   Style: {interaction.grabStyle}   FPS: {fps:F0}\n" +
                            "A:Grab  B:Knife  X:View  Y:Style  LStick:Shape  Grip:Toggle";
        }

        private static void SetupLights()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.25f, 0.25f, 0.375f, 1f) * 0.6f;

            CreateDirectionalLight("Key Light", new Vector3(5f, 5f, 5f), Color.white, 1.5f, false);
            CreateDirectionalLight("Fill Light", new Vector3(-3f, -2f, 4f), new Color32(0xff, 0xcc, 0xaa, 255), 0.5f, false);
            CreateDirectionalLight("Back Light", new Vector3(-2f, -3f, -4f), new Color32(0x88, 0xaa, 0xff, 255), 0.4f, false);
        }

        private static void CreateDirectionalLight(string name, Vector3 fromPosition, Color color, float intensity, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.position = fromPosition;
            go.transform.LookAt(Vector3.zero);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        }

        private static Material CreateSkinMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");

            var mat = new Material(shader) { name = "SkinMaterial" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", SkinColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", SkinColor);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.5f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            return mat;
        }

        private void ClearCurrent()
        {
            if (interaction != null) interaction.ResetState();
            if (skinMesh != null) skinMesh.Destroy();
            skinMesh = null;
            deformation = null;
            topo = null;
        }

        private void InitCommon(bool box)
        {
            ClearCurrent();

            if (skinRoot == null) skinRoot = new GameObject("SkinRoot").transform;

            var source = new SkinTopology();
            if (box) source.InitBox(BoxSize, BoxSubdiv);
            else source.InitGrid(Rows, Cols, Width, Height);

            topo = new LiveSkinTopology(source);
            skinMesh = new LiveSkinMesh(topo, skinRoot, skinMaterial);
            deformation = new LiveDeformationSystem(topo);

            if (interaction == null)
                interaction = gameObject.AddComponent<LiveInteractionManager>();

            interaction.Initialize(mainCam, orbitCam, topo, skinMesh, deformation);

            if (xrRig != null) xrRig.Initialize(interaction, mainCam);
        }

        public void InitPlane() => InitCommon(false);

        public void InitBox() => InitCommon(true);

        public void ToggleShape()
        {
            showBox = !showBox;
            InitCommon(showBox);
        }

        public void ResetDemo()
        {
            InitCommon(showBox);
        }
    }
}
