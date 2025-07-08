using UnityVRMod.Core;
using UnityVRMod.Features.VrVisualization;

namespace UnityVRMod.Config
{
    public static class ConfigManager
    {
        internal static readonly Dictionary<string, IConfigElement> ConfigElements = [];
        internal static readonly Dictionary<string, IConfigElement> InternalConfigs = [];
        public static ConfigHandler Handler { get; private set; }
        internal static InternalConfigHandler InternalHandler { get; private set; }

        // -------- CONFIG ELEMENT DEFINITIONS --------
        // --- Internal Settings (Require Restart) ---
        public static ConfigElement<float> Startup_Delay_Time;
        public static ConfigElement<bool> Disable_EventSystem_Override;
        public static ConfigElement<string> Reflection_Signature_Blacklist;
        public static ConfigElement<bool> EnableVrInjection;
        public static ConfigElement<string> VrApplicationKey;
        public static ConfigElement<bool> SafeModeStartsActive;

        // --- VR Rendering Settings ---
        public static ConfigElement<float> VrCameraNearClipPlane;
        public static ConfigElement<float> VrWorldScale;
        public static ConfigElement<float> VrUserEyeHeightOffset;
        public static ConfigElement<string> ScenePoseOverrides;

        // --- Backend-Specific Stability Settings ---
#if OPENVR_BUILD
        public static ConfigElement<int> OpenVR_WaitGetPosesDelayMs;
        public static ConfigElement<int> OpenVR_MaxRenderTargetDimension;
#endif

        // --- General Settings ---
        public static ConfigElement<bool> Force_Unlock_Mouse;
        public static ConfigElement<KeyCode> ToggleSafeModeKey;
        public static ConfigElement<SafeModeLevel> ActiveSafeModeLevel;
        public static ConfigElement<bool> EnableAutomaticSafeMode;
        public static ConfigElement<float> AutomaticSafeModeDurationSecs;
        public static ConfigElement<bool> EnableRuntimeDebugLogging;
        public static ConfigElement<string> AssertedCameraOverrides;
        // -------- END OF CONFIG ELEMENT DEFINITIONS --------

        internal static void Init(ConfigHandler mainHandler)
        {
            Handler = mainHandler;
            Handler.Init();

            InternalHandler = new InternalConfigHandler();
            InternalHandler.Init();

            CreateConfigElements();

            VRModCore.LogRuntimeDebug("Loading main configuration...");
            Handler.LoadConfig();
            VRModCore.LogRuntimeDebug("Loading internal configuration...");
            InternalHandler.LoadConfig();
            VRModCore.Log("Configuration initialized and loaded.");
        }

        internal static void RegisterConfigElement<T>(ConfigElement<T> configElement)
        {
            if (!configElement.IsInternal)
            {
                Handler.RegisterConfigElement(configElement);
                ConfigElements.Add(configElement.Name, configElement);
            }
            else
            {
                InternalHandler.RegisterConfigElement(configElement);
                InternalConfigs.Add(configElement.Name, configElement);
            }
        }

        private static void CreateConfigElements()
        {
            VRModCore.Log("Creating configuration elements...");

            // --- Internal Settings (Require Restart) ---
            Startup_Delay_Time = new ConfigElement<float>("Startup Delay Time",
                "The delay (in seconds) before the mod fully initializes after game start.", 1.0f, isInternal: true);

            Disable_EventSystem_Override = new ConfigElement<bool>("Disable EventSystem Override",
                "If true, the mod will not override the game's EventSystem.", false, isInternal: true);

            Reflection_Signature_Blacklist = new ConfigElement<string>("Member Signature Blacklist",
                "Prevents the mod from reflecting on specific class members. Separate with ';'. Ex: 'UnityEngine.Camera.main;'", "", isInternal: true);

            EnableVrInjection = new ConfigElement<bool>("Enable VR Injection",
                "Master switch for all VR-related functionality. Change requires restart.", true, isInternal: true);

            VrApplicationKey = new ConfigElement<string>("VR Application Key",
                "The application key used when initializing OpenVR/OpenXR. Change requires restart.", "unityvrmod.default.key", isInternal: true);

            SafeModeStartsActive = new ConfigElement<bool>("Safe Mode Starts Active",
                "If true, VR rendering is disabled when the mod first loads.", true, isInternal: true);

            // --- VR Rendering Settings ---
            VrCameraNearClipPlane = new ConfigElement<float>("VR Camera Near Clip",
                "The closest distance (in meters) that the VR cameras can see. This value is scaled by World Scale.", 0.01f);
            VrCameraNearClipPlane.OnValueChanged += value => VRModCore.VrVisualizationFeature?.LiveUpdateCameraNearClip(value);

            VrWorldScale = new ConfigElement<float>("VR World Scale",
                "Adjusts the perceived size of the world. >1 makes the world feel larger; <1 makes it feel smaller.", 1.0f);
            VrWorldScale.OnValueChanged += value => VRModCore.VrVisualizationFeature?.LiveUpdateWorldScale(value);

            VrUserEyeHeightOffset = new ConfigElement<float>("User Eye Height Offset",
                "How much taller (+) or shorter (-) you want to feel in the virtual world (in meters). This value is scaled by World Scale.", 0.0f);
            VrUserEyeHeightOffset.OnValueChanged += value => VRModCore.VrVisualizationFeature?.LiveUpdateUserEyeHeightOffset(value);

            ScenePoseOverrides = new ConfigElement<string>("Scene-Specific Pose Overrides",
                "Defines a starting position and, optionally, rotation for the VR rig in specific scenes. Format: 'SceneName|X Y Z|Pitch Yaw Roll;'. Use '~' to keep a game's original value for any axis.", "");
            
            // --- Backend-Specific Stability Settings ---
#if OPENVR_BUILD
            OpenVR_WaitGetPosesDelayMs = new ConfigElement<int>("OpenVR WaitGetPoses Delay (ms)",
                "[OpenVR ONLY] A small delay (in milliseconds) before retrieving headset poses. Helps prevent crashes in some games.", 2);

            OpenVR_MaxRenderTargetDimension = new ConfigElement<int>("OpenVR Max Render Target Dimension",
                "[OpenVR ONLY] The maximum width or height for the VR eye textures. 0 uses the default recommended by SteamVR.", 0);
#endif

            // --- General Settings ---
            Force_Unlock_Mouse = new ConfigElement<bool>("Force Unlock Mouse",
               "Forces the mouse cursor to be visible and unlocked when any mod UI is open.", true);
            Force_Unlock_Mouse.OnValueChanged += value => UniverseLib.Config.ConfigManager.Force_Unlock_Mouse = value;

            ToggleSafeModeKey = new ConfigElement<KeyCode>("Toggle Safe Mode Keybind",
                "The key used to toggle VR rendering on and off.", KeyCode.F11);

            ActiveSafeModeLevel = new ConfigElement<SafeModeLevel>("Safe Mode Level",
                "Defines the behavior of the Safe Mode toggle. Fast is quickest. RigReinit is safer but slower. FullVrReinit is the most aggressive and safest option for delicate games.", SafeModeLevel.RigReinitOnToggle);

            EnableAutomaticSafeMode = new ConfigElement<bool>("Enable Automatic Safe Mode",
                "If true, VR rendering will be temporarily disabled during scene loads or when the game's main camera changes.", true);

            AutomaticSafeModeDurationSecs = new ConfigElement<float>("Automatic Safe Mode Duration",
                "The time (in seconds) that automatic safe mode will remain active.", 1.0f);

            EnableRuntimeDebugLogging = new ConfigElement<bool>("Enable Runtime Debug Logging",
                "Enables detailed, non-spammy debug messages to be printed to the console.", false);

            AssertedCameraOverrides = new ConfigElement<string>("Asserted Camera Overrides",
                "Manual overrides for camera detection if heuristics fail. Format: 'SceneName|GameObjectPath;GameObjectPath2'. Use full hierarchy or just the name. An empty scene name applies the override to all scenes.",
                "");

            VRModCore.Log($"Finished creating {ConfigElements.Count + InternalConfigs.Count} config elements.");
        }

        public static void SaveAll()
        {
            VRModCore.LogRuntimeDebug("Saving main configuration...");
            Handler?.SaveConfig();
            VRModCore.LogRuntimeDebug("Saving internal configuration...");
            InternalHandler?.SaveConfig();
        }

        public static void LoadAll()
        {
            VRModCore.LogRuntimeDebug("Reloading main configuration...");
            Handler?.LoadConfig();
            VRModCore.LogRuntimeDebug("Reloading internal configuration...");
            InternalHandler?.LoadConfig();
        }
    }
}