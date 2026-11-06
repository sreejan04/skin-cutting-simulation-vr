using UnityEditor;
using UnityEngine;

namespace SkinCutter.EditorTools
{
    public static class SkinCutterMenu
    {
        [MenuItem("Tools/Skin Cutter/Create Demo In Scene")]
        public static void CreateDemoInScene()
        {
            var existing = Object.FindFirstObjectByType<SkinCutterDemo>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);
                return;
            }

            var go = new GameObject("SkinCutterDemo");
            Undo.RegisterCreatedObjectUndo(go, "Create Skin Cutter Demo");
            go.AddComponent<SkinCutterDemo>();
            Selection.activeGameObject = go;
        }
    }
}
