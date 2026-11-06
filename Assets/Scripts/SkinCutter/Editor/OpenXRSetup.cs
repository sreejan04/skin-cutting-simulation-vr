using UnityEditor;
using UnityEditor.XR.OpenXR;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;
using UnityEngine.XR.OpenXR.Features.OculusQuestSupport;

namespace SkinCutter.EditorTools
{
    /// <summary>
    /// One-click OpenXR bootstrap for the skin-cutter demo. Ensures XR Plug-in
    /// Management initializes the OpenXR loader on Android (and Standalone), and
    /// enables the controller/quest features needed by the VR grab/cut demo.
    /// </summary>
    public static class OpenXRSetup
    {
        private const string MenuPath = "Tools/Skin Cutter/Setup OpenXR";

        [MenuItem(MenuPath)]
        public static void SetupOpenXR()
        {
            var buildTargetGroups = new[]
            {
                BuildTargetGroup.Android,
                BuildTargetGroup.Standalone
            };

            foreach (var group in buildTargetGroups)
            {
                EnableLoader(group);
                ConfigureOpenXR(group);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SkinCutter] OpenXR configured for Android + Standalone. " +
                      "Verify under Edit > Project Settings > XR Plug-in Management.");
        }

        private static void EnableLoader(BuildTargetGroup group)
        {
            UnityEngine.XR.Management.XRGeneralSettings xr =
                UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget
                    .XRGeneralSettingsForBuildTarget(group);

            if (xr == null) return;

            xr.InitManagerOnStart = true;

            var manager = xr.Manager;
            if (manager != null)
            {
                manager.automaticLoading = true;
                manager.automaticRunning = true;

                bool hasOpenXR = false;
                foreach (var loader in manager.activeLoaders)
                {
                    if (loader is UnityEngine.XR.OpenXR.OpenXRLoader)
                    {
                        hasOpenXR = true;
                        break;
                    }
                }

                if (!hasOpenXR)
                {
                    Debug.LogWarning($"[SkinCutter] OpenXR loader not assigned for {group}. " +
                                     "Enable it under Edit > Project Settings > XR Plug-in Management.");
                }
            }
        }

        private static void ConfigureOpenXR(BuildTargetGroup group)
        {
            FeatureHelpers.RefreshFeatures(group);

            var settings = UnityEngine.XR.OpenXR.OpenXRSettings
                .GetSettingsForBuildTargetGroup(group);
            if (settings == null)
            {
                Debug.LogWarning($"[SkinCutter] No OpenXR settings for {group}; skipping feature config.");
                return;
            }

            var touchProfile = settings.GetFeature<OculusTouchControllerProfile>();
            if (touchProfile != null) touchProfile.enabled = true;

            var metaQuest = settings.GetFeature<MetaQuestFeature>();
            if (metaQuest != null) metaQuest.enabled = true;

#pragma warning disable CS0618
            var legacyQuest = settings.GetFeature<OculusQuestFeature>();
            if (legacyQuest != null) legacyQuest.enabled = false;
#pragma warning restore CS0618

            settings.renderMode = UnityEngine.XR.OpenXR.OpenXRSettings.RenderMode.SinglePassInstanced;
            EditorUtility.SetDirty(settings);
        }
    }
}
