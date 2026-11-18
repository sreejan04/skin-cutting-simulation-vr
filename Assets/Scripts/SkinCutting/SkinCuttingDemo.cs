using UnityEngine;
using UnityEngine.Rendering;

namespace SkinCutting
{
    [DisallowMultipleComponent]
    public sealed class SkinCuttingDemo : MonoBehaviour
    {
        public enum ToolMode
        {
            Knife,
            Grab,
            View
        }

        public ToolMode Mode = ToolMode.Knife;
        public int GridResolution = 36;
        public float SkinWidth = 4f;
        public float SkinHeight = 4f;
        public float CutGap = 0.007f;
        public float CutDepth = 0.0025f;
        public float BladeRadius = 0.09f;
        public float CutStep = 0.05f;
        public float GrabRadius = 0.35f;
        public bool EnableRefinement = true;
        public bool PreRefineCuts = true;
        public int RefinementLevels = 2;
        public float RefinementTargetEdge = 0.035f;
        public int RefinementMaxTriangles = 9000;
        public int BoundarySmoothIterations = 4;
        public float BoundarySmoothStrength = 0.4f;
        public bool ShowHelp = true;
        public bool SimulationPaused;

        private SkinMeshData data;
        private SkinMeshCutter cutter;
        private SkinMeshRefiner refiner;
        private Camera cameraComponent;
        private SkinOrbitCamera orbitCamera;
        private bool built;
        private bool strokeActive;
        private bool grabActive;
        private bool strokeMadeCut;
        private Vector3 lastLocalPoint;
        private Vector3 lastLocalNormal;
        private Vector3 smoothedLocalNormal;
        private Vector3 strokeStart;
        private Vector3 strokeEnd;
        private Plane grabPlane;
        private float framesPerSecond;
        private int lastStrokeCuts;
        private Texture2D panelTexture;
        private GUIStyle panelStyle;
        private GUIStyle titleStyle;
        private GUIStyle labelStyle;
        private GUIStyle smallStyle;
        private static readonly Rect HelpPanelRect = new Rect(18f, 18f, 390f, 350f);

        private void Awake()
        {
            Application.targetFrameRate = 60;
            BuildDemo();
        }

        private void OnDestroy()
        {
            if (panelTexture != null)
            {
                Destroy(panelTexture);
            }
        }

        private void BuildDemo()
        {
            if (built)
            {
                return;
            }

            built = true;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.085f, 0.075f, 0.085f);
            RenderSettings.fog = false;

            GameObject skin = new GameObject("Skin Sheet");
            skin.transform.SetParent(transform, false);

            data = skin.AddComponent<SkinMeshData>();
            cutter = skin.AddComponent<SkinMeshCutter>();
            refiner = skin.AddComponent<SkinMeshRefiner>();

            cutter.CutGap = CutGap;
            cutter.CutDepth = CutDepth;
            cutter.BladeRadius = BladeRadius;
            cutter.MinSegmentLength = 0.004f;
            refiner.RefinementEnabled = EnableRefinement;
            refiner.MaxLevels = Mathf.Max(1, RefinementLevels);
            refiner.TargetEdgeLength = RefinementTargetEdge > 0f
                ? RefinementTargetEdge
                : (SkinWidth / Mathf.Max(2, GridResolution)) * 0.42f;
            refiner.MaxTriangles = RefinementMaxTriangles;

            data.Initialize(SkinWidth, SkinHeight, GridResolution, GridResolution);
            data.Gravity = -2f;
            data.Substeps = 1;
            data.SolverIterations = 14;
            data.BendIterations = 7;
            data.StretchStiffness = 1f;
            data.BendStiffness = 1f;
            data.Damping = 0.96f;
            data.AttachmentStrength = 0.18f;
            data.MaxStretchRatio = 1.05f;
            data.StrainLimitIterations = 2;
            data.MaxSimulationSteps = 2;
            data.GrabSolverIterations = 14;
            data.GrabBendIterations = 7;
            data.GrabResponse = 35f;
            data.GrabHandleRadiusScale = 0.45f;
            data.GrabSoftRadiusScale = 4f;
            data.GrabMaxSoftRadius = 1.75f;
            data.GrabTetherReduction = 1f;
            data.GrabStiffness = 500f;
            data.GrabMaxSpeed = 15f;
            data.GrabMaxStretchRatio = 1.04f;
            data.GrabStrainLimitIterations = 6;

            MeshRenderer skinRenderer = skin.GetComponent<MeshRenderer>();
            if (skinRenderer != null)
            {
                skinRenderer.sharedMaterials = new[] { CreateSkinMaterial(), CreateInnerFleshMaterial() };
                skinRenderer.shadowCastingMode = ShadowCastingMode.On;
                skinRenderer.receiveShadows = true;
            }

            CreateBackingPlane();
            SetupCamera();
            SetupLighting();
        }

        private void SetupCamera()
        {
            cameraComponent = Camera.main;
            if (cameraComponent == null)
            {
                cameraComponent = Object.FindFirstObjectByType<Camera>();
            }
            if (cameraComponent == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                cameraComponent = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            cameraComponent.clearFlags = CameraClearFlags.SolidColor;
            cameraComponent.backgroundColor = new Color(0.012f, 0.014f, 0.02f, 1f);
            cameraComponent.fieldOfView = 42f;
            cameraComponent.nearClipPlane = 0.03f;
            cameraComponent.farClipPlane = 80f;

            orbitCamera = cameraComponent.GetComponent<SkinOrbitCamera>();
            if (orbitCamera == null)
            {
                orbitCamera = cameraComponent.gameObject.AddComponent<SkinOrbitCamera>();
            }

            orbitCamera.Target = Vector3.zero;
            orbitCamera.ResetView();
        }

        private void SetupLighting()
        {
            Light keyLight = Object.FindFirstObjectByType<Light>();
            if (keyLight == null)
            {
                GameObject lightObject = new GameObject("Skin Key Light");
                lightObject.transform.SetParent(transform, false);
                keyLight = lightObject.AddComponent<Light>();
                keyLight.type = LightType.Directional;
            }

            keyLight.transform.rotation = Quaternion.Euler(34f, -28f, 0f);
            keyLight.color = new Color(1f, 0.91f, 0.84f);
            keyLight.intensity = 1.35f;

            GameObject rimObject = new GameObject("Skin Rim Light");
            rimObject.transform.SetParent(transform, false);
            rimObject.transform.position = new Vector3(-2.2f, 1.8f, 3.2f);
            Light rim = rimObject.AddComponent<Light>();
            rim.type = LightType.Point;
            rim.range = 9f;
            rim.intensity = 7f;
            rim.color = new Color(0.9f, 0.42f, 0.36f);
            rim.shadows = LightShadows.None;
        }

        private void CreateBackingPlane()
        {
            GameObject backing = new GameObject("Cut Backing");
            backing.transform.SetParent(transform, false);

            float width = SkinWidth + 0.9f;
            float height = SkinHeight + 0.9f;
            float z = -0.035f;
            Vector3[] vertices =
            {
                new Vector3(-width * 0.5f, -height * 0.5f, z),
                new Vector3(width * 0.5f, -height * 0.5f, z),
                new Vector3(width * 0.5f, height * 0.5f, z),
                new Vector3(-width * 0.5f, height * 0.5f, z)
            };
            Vector2[] uv =
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            int[] triangles = { 0, 1, 2, 0, 2, 3 };

            Mesh mesh = new Mesh();
            mesh.name = "CutBackingMesh";
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            MeshFilter filter = backing.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = backing.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateUnlitMaterial(new Color(0.22f, 0.012f, 0.018f), "Cut Backing Material");
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private Material CreateSkinMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null)
            {
                shader = Shader.Find("Diffuse");
            }

            Material material = new Material(shader);
            material.name = "Procedural Skin";
            Texture2D texture = CreateSkinTexture(512);
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
            }
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
            }
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", Color.white);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.44f);
            }
            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", 0.44f);
            }
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0f);
            }
            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", 0f);
            }
            material.doubleSidedGI = true;
            return material;
        }

        private Material CreateInnerFleshMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null)
            {
                shader = Shader.Find("Diffuse");
            }

            Material material = new Material(shader);
            material.name = "Procedural Inner Flesh";
            Texture2D texture = CreateInnerFleshTexture(128);
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
            }
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
            }
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", Color.white);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.24f);
            }
            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", 0.24f);
            }
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0f);
            }
            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", 0f);
            }
            material.doubleSidedGI = true;
            return material;
        }

        private Texture2D CreateInnerFleshTexture(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            texture.name = "Procedural Inner Flesh Texture";
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Bilinear;
            texture.anisoLevel = 4;

            Color[] pixels = new Color[size * size];
            Color fat = new Color(0.93f, 0.82f, 0.70f);
            Color flesh = new Color(0.72f, 0.30f, 0.27f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size;
                    float v = y / (float)size;
                    float veins = Mathf.Sin(u * 38f + Mathf.Sin(v * 17f) * 2.4f) *
                                  Mathf.Sin(v * 31f + Mathf.Sin(u * 13f) * 1.8f);
                    float blend = Mathf.SmoothStep(0.08f, 0.34f, Mathf.Abs(veins));
                    pixels[y * size + x] = Color.Lerp(fat, flesh, blend);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true, false);
            return texture;
        }

        private Material CreateUnlitMaterial(Color color, string materialName)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            Material material = new Material(shader);
            material.name = materialName;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
            return material;
        }

        private Texture2D CreateSkinTexture(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            texture.name = "Procedural Skin Texture";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.anisoLevel = 4;

            Color[] pixels = new Color[size * size];
            Color lightSkin = new Color(0.88f, 0.58f, 0.47f);
            Color deepSkin = new Color(0.67f, 0.34f, 0.28f);
            Color woundTint = new Color(0.62f, 0.13f, 0.14f);

            for (int y = 0; y < size; y++)
            {
                float v = y / (float)(size - 1);
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)(size - 1);
                    float broad = Mathf.PerlinNoise(u * 5.5f + 1.7f, v * 5.5f + 8.2f);
                    float veins = Mathf.PerlinNoise(u * 13f + 4.3f, v * 3.7f + 2.1f);
                    float pores = Mathf.PerlinNoise(u * 96f + 11f, v * 96f + 23f);

                    Color color = Color.Lerp(lightSkin, deepSkin, Mathf.Clamp01(broad * 0.72f + veins * 0.28f));
                    color = Color.Lerp(color, woundTint, Mathf.Max(0f, veins - 0.7f) * 0.2f);

                    float poreDarkening = Mathf.SmoothStep(0.62f, 0.9f, pores) * 0.16f;
                    color *= 1f - poreDarkening;

                    float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    color = Color.Lerp(new Color(0.48f, 0.16f, 0.14f), color, Mathf.SmoothStep(0f, 0.09f, edge));
                    color.a = 1f;

                    pixels[y * size + x] = color;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true, false);
            return texture;
        }

        private void Update()
        {
            if (data == null)
            {
                return;
            }

            float instantaneousFps = 1f / Mathf.Max(1e-4f, Time.unscaledDeltaTime);
            framesPerSecond = Mathf.Lerp(framesPerSecond <= 0f ? instantaneousFps : framesPerSecond,
                instantaneousFps, 0.08f);

            HandleShortcuts();

            bool modifierGrab = Input.GetKey(KeyCode.LeftShift) ||
                                Input.GetKey(KeyCode.RightShift);
            bool wantGrab = modifierGrab || Mode == ToolMode.Grab;
            bool pointerOverUI = PointerOverUI();

            if (wantGrab && strokeActive)
            {
                EndStroke();
            }
            if (!wantGrab && grabActive)
            {
                EndGrab();
            }

            if (wantGrab)
            {
                if (Input.GetMouseButtonDown(0) && !pointerOverUI)
                {
                    BeginGrab();
                }
                if (grabActive)
                {
                    if (Input.GetMouseButton(0))
                    {
                        ContinueGrab();
                    }
                    else
                    {
                        EndGrab();
                    }
                }
            }
            else if (Mode == ToolMode.Knife)
            {
                if (Input.GetMouseButtonDown(0) && !pointerOverUI)
                {
                    BeginStroke();
                }
                if (strokeActive)
                {
                    if (Input.GetMouseButton(0))
                    {
                        ContinueStroke();
                    }
                    else
                    {
                        EndStroke();
                    }
                }
            }

            if (!SimulationPaused)
            {
                data.Simulate(Time.deltaTime);
            }
        }

        private void HandleShortcuts()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            {
                SetMode(ToolMode.Knife);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            {
                SetMode(ToolMode.Grab);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
            {
                SetMode(ToolMode.View);
            }

            if (Input.GetKeyDown(KeyCode.G))
            {
                SetMode(ToolMode.Grab);
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                ResetSimulation();
            }

            if (Input.GetKeyDown(KeyCode.H))
            {
                ShowHelp = !ShowHelp;
            }

            if (Input.GetKeyDown(KeyCode.P))
            {
                SimulationPaused = !SimulationPaused;
            }
        }

        private void SetMode(ToolMode nextMode)
        {
            if (Mode == nextMode)
            {
                return;
            }

            EndStroke();
            EndGrab();
            Mode = nextMode;
        }

        private bool PointerOverUI()
        {
            if (!ShowHelp)
            {
                return false;
            }

            Vector2 mouse = Input.mousePosition;
            return HelpPanelRect.Contains(new Vector2(mouse.x, Screen.height - mouse.y));
        }

        private bool RaycastSkin(out SkinRaycastHit hit)
        {
            hit = default(SkinRaycastHit);
            if (cameraComponent == null || data == null)
            {
                return false;
            }

            Ray ray = cameraComponent.ScreenPointToRay(Input.mousePosition);
            return data.Raycast(ray, out hit);
        }

        private void BeginStroke()
        {
            EndGrab();

            SkinRaycastHit hit;
            if (!RaycastSkin(out hit))
            {
                return;
            }

            strokeActive = true;
            strokeMadeCut = false;
            lastStrokeCuts = 0;
            lastLocalPoint = hit.LocalPoint;
            lastLocalNormal = hit.LocalNormal;
            smoothedLocalNormal = hit.LocalNormal;
            strokeStart = hit.LocalPoint;
            strokeEnd = hit.LocalPoint;

            if (PreRefineCuts && EnableRefinement && refiner != null)
            {
                float radius = Mathf.Max(0.1f, BladeRadius * 1.8f);
                refiner.RefineNearSegment(
                    hit.LocalPoint,
                    hit.LocalPoint,
                    radius,
                    1,
                    false);
            }
        }

        private void ContinueStroke()
        {
            SkinRaycastHit hit;
            if (!RaycastSkin(out hit))
            {
                return;
            }

            Vector3 currentPoint = hit.LocalPoint;
            Vector3 currentNormal = Vector3.Lerp(
                smoothedLocalNormal,
                hit.LocalNormal,
                0.35f);
            if (currentNormal.sqrMagnitude < 1e-8f)
            {
                currentNormal = hit.LocalNormal;
            }
            currentNormal.Normalize();
            smoothedLocalNormal = currentNormal;

            float distance = Vector3.Distance(lastLocalPoint, currentPoint);
            if (distance < 1e-5f)
            {
                return;
            }

            float stepLength = Mathf.Max(0.002f, CutStep);
            int stepCount = Mathf.Clamp(Mathf.CeilToInt(distance / stepLength), 1, 256);

            Vector3 previousPoint = lastLocalPoint;
            Vector3 previousNormal = lastLocalNormal;
            bool topologyChanged = false;

            cutter.DeferTopology = true;
            if (refiner != null)
            {
                refiner.DeferTopology = true;
            }

            try
            {
                if (PreRefineCuts && EnableRefinement && refiner != null)
                {
                    float radius = Mathf.Max(0.1f, BladeRadius * 1.8f);
                    if (refiner.RefineNearSegment(
                            lastLocalPoint, currentPoint, radius, 1, false) > 0)
                    {
                        topologyChanged = true;
                    }
                }

                for (int i = 0; i < stepCount; i++)
                {
                    float t = (i + 1) / (float)stepCount;
                    Vector3 nextPoint = Vector3.Lerp(lastLocalPoint, currentPoint, t);
                    Vector3 nextNormal = Vector3.Lerp(lastLocalNormal, currentNormal, t);
                    if (nextNormal.sqrMagnitude < 1e-8f)
                    {
                        nextNormal = currentNormal;
                    }
                    nextNormal.Normalize();

                    int clipped;
                    if (cutter.CutSegment(previousPoint, nextPoint, previousNormal, nextNormal, out clipped))
                    {
                        strokeMadeCut = true;
                        topologyChanged = true;
                        lastStrokeCuts += clipped;
                    }

                    previousPoint = nextPoint;
                    previousNormal = nextNormal;
                }
            }
            finally
            {
                cutter.DeferTopology = false;
                if (refiner != null)
                {
                    refiner.DeferTopology = false;
                }
            }

            if (topologyChanged)
            {
                data.FinalizeTopology();
            }

            lastLocalPoint = currentPoint;
            lastLocalNormal = currentNormal;
            strokeEnd = currentPoint;
        }

        private void EndStroke()
        {
            if (!strokeActive)
            {
                return;
            }

            strokeActive = false;
            if (!strokeMadeCut)
            {
                return;
            }

            if (EnableRefinement && refiner != null)
            {
                float refineRadius = Mathf.Max(0.12f, BladeRadius * 2.4f);
                refiner.RefineNearSegment(strokeStart, strokeEnd, refineRadius, RefinementLevels);
            }

            if (data != null && BoundarySmoothIterations > 0)
            {
                data.SmoothBoundary(BoundarySmoothIterations, BoundarySmoothStrength);
            }
        }

        private void BeginGrab()
        {
            SkinRaycastHit hit;
            if (!RaycastSkin(out hit))
            {
                return;
            }

            if (data.BeginGrab(hit.LocalPoint, GrabRadius, hit.TriangleIndex))
            {
                grabActive = true;
                grabPlane = new Plane(cameraComponent.transform.forward, hit.WorldPoint);
            }
        }

        private void ContinueGrab()
        {
            if (!grabActive || cameraComponent == null)
            {
                return;
            }

            Ray ray = cameraComponent.ScreenPointToRay(Input.mousePosition);
            float enter;
            if (grabPlane.Raycast(ray, out enter))
            {
                Vector3 worldPoint = ray.GetPoint(enter);
                data.MoveGrab(data.transform.InverseTransformPoint(worldPoint));
            }
        }

        private void EndGrab()
        {
            if (!grabActive)
            {
                return;
            }

            grabActive = false;
            data.EndGrab();
        }

        private void ResetSimulation()
        {
            EndStroke();
            EndGrab();
            if (data != null)
            {
                data.BuildFlatSheet();
            }
            if (cutter != null)
            {
                cutter.ResetStats();
            }
            if (refiner != null)
            {
                refiner.ResetStats();
            }
            if (orbitCamera != null)
            {
                orbitCamera.ResetView();
            }

            strokeMadeCut = false;
            lastStrokeCuts = 0;
            SimulationPaused = false;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                EndStroke();
                EndGrab();
            }
        }

        private void EnsureGuiStyles()
        {
            if (panelStyle != null)
            {
                return;
            }

            panelTexture = new Texture2D(1, 1);
            panelTexture.SetPixel(0, 0, new Color(0.025f, 0.03f, 0.04f, 0.9f));
            panelTexture.Apply();

            panelStyle = new GUIStyle(GUI.skin.box);
            panelStyle.normal.background = panelTexture;
            panelStyle.border = new RectOffset(1, 1, 1, 1);
            panelStyle.padding = new RectOffset(14, 14, 12, 12);

            titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 17;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.normal.textColor = new Color(1f, 0.82f, 0.74f);

            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 13;
            labelStyle.normal.textColor = new Color(0.92f, 0.92f, 0.94f);

            smallStyle = new GUIStyle(GUI.skin.label);
            smallStyle.fontSize = 11;
            smallStyle.normal.textColor = new Color(0.72f, 0.75f, 0.8f);
        }

        private void OnGUI()
        {
            if (data == null)
            {
                return;
            }

            EnsureGuiStyles();

            if (ShowHelp)
            {
                GUILayout.BeginArea(HelpPanelRect, panelStyle);
                GUILayout.Label("PBD Skin Cutting", titleStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Mode == ToolMode.Knife ? "Knife *" : "Knife"))
                {
                    SetMode(ToolMode.Knife);
                }
                if (GUILayout.Button(Mode == ToolMode.Grab ? "Grab *" : "Grab"))
                {
                    SetMode(ToolMode.Grab);
                }
                if (GUILayout.Button(Mode == ToolMode.View ? "View *" : "View"))
                {
                    SetMode(ToolMode.View);
                }
                GUILayout.EndHorizontal();
                string grabState = data.IsGrabbing ? "  [Pulling]" : string.Empty;
                GUILayout.Label(
                    "Mode: " + Mode + grabState +
                    (SimulationPaused ? "  [Paused]" : string.Empty),
                    labelStyle);
                GUILayout.Space(6f);
                GUILayout.Label("Left drag: cut skin", labelStyle);
                GUILayout.Label("Shift + left drag: grab skin", labelStyle);
                GUILayout.Label("Grab mode: left drag pulls skin", labelStyle);
                GUILayout.Label("Right drag: orbit camera", labelStyle);
                GUILayout.Label("Mouse wheel: zoom", labelStyle);
                GUILayout.Label("1 Knife   2/G Grab   3 View", smallStyle);
                GUILayout.Label("R reset   P pause   H hide help", smallStyle);
                GUILayout.Space(8f);
                GUILayout.Label(
                    "Vertices " + data.VertexCount +
                    "   Triangles " + data.TriangleCount, smallStyle);
                GUILayout.Label(
                    "Constraints " + data.ConstraintCount +
                    "   Cuts " + cutter.CutOperations, smallStyle);
                GUILayout.Label(
                    "Cut length " + cutter.TotalCutLength.ToString("0.00") +
                    "   Refine " + refiner.RefinementPasses +
                    " / Flip " + refiner.EdgeFlips, smallStyle);
                GUILayout.Label(
                    "Last stroke triangles " + lastStrokeCuts +
                    "   FPS " + framesPerSecond.ToString("0"), smallStyle);
                GUILayout.EndArea();
            }
            else
            {
                GUILayout.BeginArea(new Rect(18f, 18f, 270f, 38f), panelStyle);
                GUILayout.Label("PBD Skin Cutting  |  " + Mode + "  |  H help", smallStyle);
                GUILayout.EndArea();
            }

            if (Mode == ToolMode.Knife && Event.current.type == EventType.Repaint)
            {
                Vector2 mouse = Input.mousePosition;
                float x = mouse.x;
                float y = Screen.height - mouse.y;
                GUI.color = new Color(1f, 0.82f, 0.7f, 0.85f);
                GUI.DrawTexture(new Rect(x - 7f, y - 0.5f, 14f, 1f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(x - 0.5f, y - 7f, 1f, 14f), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
        }
    }
}


