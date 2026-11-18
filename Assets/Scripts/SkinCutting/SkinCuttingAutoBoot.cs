using UnityEngine;
using UnityEngine.SceneManagement;

namespace SkinCutting
{
    public static class SkinCuttingAutoBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Object.FindFirstObjectByType<SkinCuttingDemo>() != null)
            {
                return;
            }

            string sceneName = SceneManager.GetActiveScene().name;
            if (!string.IsNullOrEmpty(sceneName) &&
                sceneName != "SampleScene" &&
                sceneName != "SkinCuttingDemo")
            {
                return;
            }

            GameObject demoObject = new GameObject("Skin Cutting Demo");
            demoObject.AddComponent<SkinCuttingDemo>();
        }
    }
}
