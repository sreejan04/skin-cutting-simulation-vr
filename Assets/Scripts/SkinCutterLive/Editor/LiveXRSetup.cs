using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;
using UnityEngine.XR.OpenXR.Features.OculusQuestSupport;

namespace SkinCutter.Live.EditorTools
{
    public static class LiveXRSetup
    {
        private const string MenuPath = "Tools/Skin Cutter/Setup Live VR (Quest 3S)";

        [MenuItem(MenuPath)]
        public static void SetupLiveVR()
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
            Debug.Log("[SkinCutterLive] OpenXR configured for Quest 3S (Touch Plus) on Android + Standalone.");
        }

        private static void EnableLoader(BuildTargetGroup group)
        {
            UnityEngine.XR.Management.XRGeneralSettings xr =
                UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget
                    .XRGeneralSettingsForBuildTarget(group);

            if (xr == null) return;

            xr.InitManagerOnStart = true;

            var manager = xr.Manager;
            if (manager == null) return;

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
                Debug.LogWarning($"[SkinCutterLive] OpenXR loader not assigned for {group}. " +
                                 "Enable it under Edit > Project Settings > XR Plug-in Management.");
            }
        }

        private static void ConfigureOpenXR(BuildTargetGroup group)
        {
            FeatureHelpers.RefreshFeatures(group);

            var settings = UnityEngine.XR.OpenXR.OpenXRSettings
                .GetSettingsForBuildTargetGroup(group);
            if (settings == null)
            {
                Debug.LogWarning($"[SkinCutterLive] No OpenXR settings for {group}; skipping feature config.");
                return;
            }

            var oculusTouch = settings.GetFeature<OculusTouchControllerProfile>();
            if (oculusTouch != null) oculusTouch.enabled = true;

            var touchPlus = settings.GetFeature<MetaQuestTouchPlusControllerProfile>();
            if (touchPlus != null) touchPlus.enabled = true;

            var touchPro = settings.GetFeature<MetaQuestTouchProControllerProfile>();
            if (touchPro != null) touchPro.enabled = true;

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
