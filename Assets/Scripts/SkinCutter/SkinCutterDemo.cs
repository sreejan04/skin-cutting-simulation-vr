using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace SkinCutter
{
    public class SkinCutterDemo : MonoBehaviour
    {
        private const int Rows = 30;
        private const int Cols = 30;
        private const float Width = 4f;
        private const float Height = 4f;
        private const float BoxSize = 3f;
        private const int BoxSubdiv = 12;

        private static readonly Color BackgroundColor = new Color32(0x1a, 0x1a, 0x2e, 255);
        private static readonly Color SkinColor = new Color32(0xe8, 0xb8, 0x9a, 255);
        private static readonly Color ActiveBg = new Color32(0xe8, 0xb8, 0x9a, 255);
        private static readonly Color InactiveBg = new Color32(0x2a, 0x2a, 0x3e, 255);
        private static readonly Color ActiveText = new Color32(0x11, 0x11, 0x11, 255);
        private static readonly Color InactiveText = new Color32(0xcc, 0xcc, 0xcc, 255);

        private bool showBox;

        private const float SolverHz = 60f;
        private const int SolverIters = 35;
        private const int SolverItersFast = 12;
        private bool recalcEveryFrame = true;
        private float normalRecalcTimer;
        private float fpsLogTimer;
        private int fpsFrames;

        private SkinTopology topo;
        private SkinMesh skinMesh;
        private DeformationSystem deformation;
        private InteractionManager interaction;
        private OrbitCamera orbitCam;
        private SkinCutterVR vrInput;
        private Camera mainCam;
        private Transform skinRoot;
        private Material skinMaterial;

        private Button viewportBtn;
        private Button grabBtn;
        private Button knifeBtn;
        private Button shapeBtn;

        public bool IsBox => showBox;

        private void Start()
        {
            SetupCamera();
            SetupLights();
            SetupEventSystem();
            skinMaterial = CreateSkinMaterial();
            BuildUI();
            InitPlane();
            SetupVR();
        }

        private void Update()
        {
            HandleShortcuts();

            if (interaction == null || deformation == null || skinMesh == null) return;

            if (interaction.IsDragging && interaction.mode == ToolMode.Grab)
            {
                bool vr = vrInput != null && vrInput.IsVrActive;

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
                recalcEveryFrame = normalRecalcTimer > 0.1f;
                skinMesh.UpdatePositions(topo, recalcEveryFrame);
                if (recalcEveryFrame) normalRecalcTimer = 0f;

                fpsLogTimer += Time.deltaTime;
                fpsFrames++;
                if (fpsLogTimer >= 2f && vr)
                {
                    Debug.Log($"[SkinCutter] FPS={fpsFrames / fpsLogTimer:F0} frameMs={Time.deltaTime * 1000f:F1} iters={iters}");
                    fpsLogTimer = 0f;
                    fpsFrames = 0;
                }
            }
        }

        private void HandleShortcuts()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.vKey.wasPressedThisFrame && interaction != null) interaction.SetMode(ToolMode.Viewport);
            if (kb.gKey.wasPressedThisFrame && interaction != null) interaction.SetMode(ToolMode.Grab);
            if (kb.kKey.wasPressedThisFrame && interaction != null) interaction.SetMode(ToolMode.Knife);
            if (kb.bKey.wasPressedThisFrame) ToggleShape();
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
            cam.farClipPlane = 20f;
            cam.transform.position = new Vector3(0f, 0f, 6f);
            cam.transform.rotation = Quaternion.identity;

            orbitCam = cam.GetComponent<OrbitCamera>();
            if (orbitCam == null) orbitCam = cam.gameObject.AddComponent<OrbitCamera>();
        }

        private void SetupVR()
        {
            if (vrInput != null) return;
            vrInput = gameObject.AddComponent<SkinCutterVR>();
            vrInput.Initialize(interaction, topo, skinMesh, deformation, mainCam);
        }

        private static void SetupLights()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.25f, 0.25f, 0.375f, 1f) * 0.6f;

            CreateDirectionalLight("Key Light", new Vector3(5f, 5f, 5f), Color.white, 1.5f, true);
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

        private static void SetupEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();
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
            return mat;
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("SkinCutterCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);

            var panel = new GameObject("Toolbar", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.SetParent(canvasGo.transform, false);
            panelRect.anchorMin = new Vector2(0.5f, 1f);
            panelRect.anchorMax = new Vector2(0.5f, 1f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.anchoredPosition = new Vector2(0f, -16f);

            var layout = panel.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.padding = new RectOffset(12, 12, 8, 8);
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

            viewportBtn = MakeButton(panelRect, "Viewport", () =>
            {
                if (interaction != null) interaction.SetMode(ToolMode.Viewport);
            });
            grabBtn = MakeButton(panelRect, "Grab", () =>
            {
                if (interaction != null) interaction.SetMode(ToolMode.Grab);
            });
            knifeBtn = MakeButton(panelRect, "Knife", () =>
            {
                if (interaction != null) interaction.SetMode(ToolMode.Knife);
            });
            shapeBtn = MakeButton(panelRect, "Box", ToggleShape);

            var infoGo = new GameObject("Info", typeof(RectTransform));
            var infoRect = infoGo.GetComponent<RectTransform>();
            infoRect.SetParent(canvasGo.transform, false);
            infoRect.anchorMin = new Vector2(0.5f, 0f);
            infoRect.anchorMax = new Vector2(0.5f, 0f);
            infoRect.pivot = new Vector2(0.5f, 0f);
            infoRect.anchoredPosition = new Vector2(0f, 16f);
            infoRect.sizeDelta = new Vector2(900f, 40f);

            var text = infoGo.AddComponent<Text>();
            text.font = GetUiFont();
            text.fontSize = 14;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color32(0x88, 0x88, 0x88, 255);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.text = "Viewport: LMB orbit / RMB pan | Knife: LMB drag to cut | Grab: LMB drag to pull | Keys: V/G/K tools, B toggles box";

            RefreshButtons();
        }

        private static Font GetUiFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }

        private Button MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(130f, 42f);

            var image = go.GetComponent<Image>();
            image.color = InactiveBg;

            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);

            var textGo = new GameObject("Text", typeof(RectTransform));
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.SetParent(rect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            var text = textGo.AddComponent<Text>();
            text.font = GetUiFont();
            text.fontSize = 16;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = InactiveText;
            text.text = label;

            return button;
        }

        private void RefreshButtons()
        {
            SetButton(viewportBtn, "Viewport", interaction != null && interaction.mode == ToolMode.Viewport);
            SetButton(grabBtn, "Grab", interaction != null && interaction.mode == ToolMode.Grab);
            SetButton(knifeBtn, "Knife", interaction != null && interaction.mode == ToolMode.Knife);
            SetButton(shapeBtn, showBox ? "Plane" : "Box", false);
        }

        private static void SetButton(Button button, string label, bool active)
        {
            if (button == null) return;
            button.image.color = active ? ActiveBg : InactiveBg;
            var text = button.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = label;
                text.color = active ? ActiveText : InactiveText;
            }
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

            topo = new SkinTopology();
            if (box)
            {
                topo.InitBox(BoxSize, BoxSubdiv);
                skinMesh = new SkinMesh(topo, skinRoot, skinMaterial, ShapeType.Box);
                deformation = new DeformationSystem(topo);
                deformation.PinCorners(0, 0);
            }
            else
            {
                topo.InitGrid(Rows, Cols, Width, Height);
                skinMesh = new SkinMesh(topo, skinRoot, skinMaterial, ShapeType.Plane);
                deformation = new DeformationSystem(topo);
                deformation.PinCorners(Rows, Cols);
            }

            if (interaction == null)
            {
                interaction = gameObject.AddComponent<InteractionManager>();
                interaction.OnModeChange += _ => RefreshButtons();
            }

            interaction.Initialize(Camera.main, orbitCam, topo, skinMesh, deformation);
            if (vrInput != null) vrInput.Initialize(interaction, topo, skinMesh, deformation, mainCam);
            RefreshButtons();
        }

        public void InitPlane() => InitCommon(false);

        public void InitBox() => InitCommon(true);

        public void ToggleShape()
        {
            showBox = !showBox;
            InitCommon(showBox);
        }
    }
}
