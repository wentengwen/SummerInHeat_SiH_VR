using UnityVRMod.Config;
using UniverseLib.Input; // Using UniverseLib's InputManager for universal input

#pragma warning disable IDE0130
namespace UnityVRMod.Core
#pragma warning restore IDE0130
{
    public static class VRModKeybind
    {
        public static void Update()
        {
            if (ConfigManager.ToggleSafeModeKey != null && InputManager.GetKeyDown(ConfigManager.ToggleSafeModeKey.Value))
            {
                VRModCore.LogRuntimeDebug("Toggle Safe Mode key pressed!");
                if (VRModCore.VrVisualizationFeature != null)
                {
                    VRModCore.VrVisualizationFeature.ToggleUserSafeMode();
                }
                else
                {
                    VRModCore.LogWarning("VrVisualizationManager (VrVisFeature) is null. Cannot toggle safe mode.");
                }
            }

            // REMINDER: Add other mod-specific keybind checks here if needed in the future.
        }
    }
}