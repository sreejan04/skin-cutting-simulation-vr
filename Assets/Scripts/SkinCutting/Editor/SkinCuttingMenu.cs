#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SkinCutting.Editor
{
    public static class SkinCuttingMenu
    {
        private const string DemoScenePath = "Assets/Scenes/SkinCuttingDemo.unity";

        [MenuItem("Tools/Skin Cutting/Create Demo Scene")]
        public static void CreateDemoScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            GameObject demoObject = new GameObject("Skin Cutting Demo");
            demoObject.AddComponent<SkinCuttingDemo>();
            Selection.activeGameObject = demoObject;

            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }

            EditorSceneManager.SaveScene(scene, DemoScenePath);
            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/Skin Cutting/Open Demo Scene")]
        public static void OpenDemoScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScenePath) == null)
            {
                CreateDemoScene();
                return;
            }

            EditorSceneManager.OpenScene(DemoScenePath);
        }

        [MenuItem("Tools/Skin Cutting/Run Self Test")]
        public static void RunSelfTest()
        {
            bool passed = SkinCuttingSelfTest.Run();
            if (passed)
            {
                Debug.Log("Skin cutting self test passed.");
            }
            else
            {
                Debug.LogError("Skin cutting self test failed.");
            }
        }
    }

    public static class SkinCuttingSelfTest
    {
        public static bool Run()
        {
            GameObject root = new GameObject("Skin Cutting Self Test");
            StringBuilder report = new StringBuilder();

            try
            {
                SkinMeshData data = root.AddComponent<SkinMeshData>();
                SkinMeshCutter cutter = root.AddComponent<SkinMeshCutter>();
                SkinMeshRefiner refiner = root.AddComponent<SkinMeshRefiner>();

                data.Initialize(3f, 3f, 32, 32);
                int initialTriangles = data.TriangleCount;
                int initialVertices = data.VertexCount;

                SkinRaycastHit hit;
                bool raycastOk = data.Raycast(
                    new Ray(new Vector3(0.13f, 0.17f, 4f), new Vector3(0f, 0f, -1f)),
                    out hit);

                int cutSegments = 0;
                int clippedTriangles = 0;
                cutter.DeferTopology = true;
                for (int i = 0; i < 6; i++)
                {
                    float y = -0.75f + i * 0.3f;
                    int clipped;
                    if (cutter.CutSegment(
                        new Vector3(-1.1f, y, 0f),
                        new Vector3(1.1f, y, 0f),
                        Vector3.forward,
                        Vector3.forward,
                        out clipped))
                    {
                        cutSegments++;
                        clippedTriangles += clipped;
                    }
                }
                cutter.DeferTopology = false;
                data.FinalizeTopology();

                for (int i = 0; i < 20; i++)
                {
                    data.Simulate(1f / 60f);
                }

                int addedByRefinement = refiner.RefineNearSegment(
                    new Vector3(-1f, -0.8f, 0f),
                    new Vector3(1f, 0.8f, 0f),
                    0.6f,
                    2);

                for (int i = 0; i < 12; i++)
                {
                    data.Simulate(1f / 60f);
                }

                int smoothedVertices = data.SmoothBoundary(3, 0.35f);
                for (int i = 0; i < 6; i++)
                {
                    data.Simulate(1f / 60f);
                }

                Vector3[] beforeGrab = new Vector3[data.VertexCount];
                for (int i = 0; i < beforeGrab.Length; i++)
                {
                    beforeGrab[i] = data.Positions[i];
                }

                Vector3 grabPoint = Vector3.zero;
                float bestGrabDistance = float.PositiveInfinity;
                for (int i = 0; i < data.VertexCount; i++)
                {
                    if (data.GetInvMass(i) <= 0f)
                    {
                        continue;
                    }

                    Vector3 p = data.Positions[i];
                    float distance = p.x * p.x + p.y * p.y;
                    if (distance < bestGrabDistance)
                    {
                        bestGrabDistance = distance;
                        grabPoint = p;
                    }
                }

                bool grabStarted = data.BeginGrab(grabPoint, 0.4f);
                float grabDisplacement = 0f;
                if (grabStarted)
                {
                    data.MoveGrab(grabPoint + new Vector3(0f, 0.2f, 0.55f));
                    for (int i = 0; i < 6; i++)
                    {
                        data.Simulate(1f / 60f);
                    }

                    for (int i = 0; i < beforeGrab.Length && i < data.VertexCount; i++)
                    {
                        grabDisplacement = Mathf.Max(
                            grabDisplacement,
                            Vector3.Distance(beforeGrab[i], data.Positions[i]));
                    }
                }

                data.EndGrab();
                bool grabReleased = !data.IsGrabbing;

                bool finite = true;
                for (int i = 0; i < data.VertexCount; i++)
                {
                    Vector3 p = data.Positions[i];
                    if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) ||
                        float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z))
                    {
                        finite = false;
                        break;
                    }
                }

                bool validIndices = true;
                for (int i = 0; i < data.Triangles.Count; i++)
                {
                    if (data.Triangles[i] < 0 || data.Triangles[i] >= data.VertexCount)
                    {
                        validIndices = false;
                        break;
                    }
                }

                bool passed = cutSegments > 0 &&
                              clippedTriangles > 0 &&
                              data.TriangleCount > 0 &&
                              data.VertexCount > initialVertices &&
                              addedByRefinement >= 0 &&
                              smoothedVertices > 0 &&
                              grabStarted &&
                              grabReleased &&
                              grabDisplacement > 0.01f &&
                              finite &&
                              validIndices &&
                              raycastOk;

                report.AppendLine("Skin cutting self test report");
                report.AppendLine("Initial vertices: " + initialVertices);
                report.AppendLine("Initial triangles: " + initialTriangles);
                report.AppendLine("Cut segments: " + cutSegments);
                report.AppendLine("Clipped triangles: " + clippedTriangles);
                report.AppendLine("Vertices after cut+refine: " + data.VertexCount);
                report.AppendLine("Triangles after cut+refine: " + data.TriangleCount);
                report.AppendLine("Added by refinement: " + addedByRefinement);
                report.AppendLine("Edge flips: " + refiner.EdgeFlips);
                report.AppendLine("Smoothed boundary vertices: " + smoothedVertices);
                report.AppendLine("Grab started: " + grabStarted);
                report.AppendLine("Grab displacement: " + grabDisplacement.ToString("0.000"));
                report.AppendLine("Grab released: " + grabReleased);
                report.AppendLine("Constraints: " + data.ConstraintCount);
                report.AppendLine("Finite positions: " + finite);
                report.AppendLine("Valid indices: " + validIndices);
                report.AppendLine("Raycast: " + raycastOk);
                report.AppendLine("Hit distance: " + hit.Distance.ToString("0.000"));
                report.AppendLine("Result: " + (passed ? "PASS" : "FAIL"));

                if (passed)
                {
                    Debug.Log(report.ToString());
                }
                else
                {
                    Debug.LogError(report.ToString());
                }

                return passed;
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
#endif
