using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SkinCutter.Live.EditorTools
{
    public static class CreateVRScene
    {
        [MenuItem("Tools/Skin Cutter/Create Live VR Scene")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("SkinCutterVRDemo");
            go.AddComponent<SkinCutterVRDemo>();

            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/SkinCutterVR.unity");
            AssetDatabase.Refresh();
        }
    }
}
