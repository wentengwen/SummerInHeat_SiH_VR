using System.Runtime.InteropServices;
using UnityEngine.SceneManagement;
using UnityVRMod.Config;
using UnityVRMod.Core;
using UnityVRMod.Features.Util;

namespace UnityVRMod.Features.Shared
{
    /// <summary>
    /// A shared, static API for other features to interact with the active VR system,
    /// regardless of whether it's our injected rig or a game's native implementation.
    /// </summary>
    public static class SharedVrApi
    {
        private static bool _nativeVrChecked = false;
        private static bool _isNativeOpenXr = false;
        private static bool _isNativeOpenVr = false;

        /// <summary>
        /// Finds and returns the primary VR camera component.
        /// This method correctly handles four conditions:
        /// 1. Our injected VR rig is active.
        /// 2. A native (game-provided) OpenXR environment is detected.
        /// 3. A native (game-provided) OpenVR environment is detected.
        /// 4. No VR environment is active.
        /// </summary>
        /// <returns>The active VR Camera component, or null if none is found.</returns>
        public static Camera GetCurrentVrCamera()
        {
            // Case 1: Our VR injection is enabled.
            if (ConfigManager.EnableVrInjection.Value)
            {
                var vizManager = VRModCore.VrVisualizationFeature;
                if (vizManager != null)
                {
                    var injectedCam = vizManager.VrCameraForUIParenting;
                    if (injectedCam != null) return injectedCam;
                }

                VRModCore.LogError("EnableVrInjection is true, but the VR Visualization Feature is not available or has no active camera. This may indicate a mod initialization failure.");
                return null;
            }

            // Cases 2 & 3: Our injection is disabled, check for a native VR environment.
            // Priority 1: Check for user-defined camera overrides.
            var overrideCamera = FindCameraByOverride();
            if (overrideCamera != null)
            {
                VRModCore.LogRuntimeDebug($"Found native VR camera via AssertedCameraOverrides: '{overrideCamera.name}'");
                return overrideCamera;
            }

            // Priority 2: Check for native VR DLLs and then use heuristics.
            if (!_nativeVrChecked)
            {
                _isNativeOpenXr = NativeMethods.GetModuleHandle("openxr_loader.dll") != IntPtr.Zero;
                _isNativeOpenVr = NativeMethods.GetModuleHandle("openvr_api.dll") != IntPtr.Zero;
                _nativeVrChecked = true;
                if (_isNativeOpenXr || _isNativeOpenVr)
                    VRModCore.LogRuntimeDebug($"Native VR check complete: OpenXR active = {_isNativeOpenXr}, OpenVR active = {_isNativeOpenVr}");
            }

            if (_isNativeOpenXr || _isNativeOpenVr)
            {
                // Heuristic 1: Check Camera.main first.
                var mainCam = Camera.main;
                if (mainCam != null && mainCam.enabled && IsLikelyVrCamera(mainCam))
                {
                    VRModCore.LogRuntimeDebug("Found likely native VR camera via Camera.main.");
                    return mainCam;
                }

                // Heuristic 2 & 3: Iterate all cameras and check components and names.
                foreach (var cam in Camera.allCameras)
                {
                    if (cam != null && cam.enabled && IsLikelyVrCamera(cam))
                    {
                        VRModCore.LogRuntimeDebug($"Found likely native VR camera via heuristic search: '{cam.name}'.");
                        return cam;
                    }
                }
            }

            // Case 4: No VR environment.
            return null;
        }

        private static Camera FindCameraByOverride()
        {
            var identifiers = CameraIdentifierHelper.Parse(ConfigManager.AssertedCameraOverrides.Value);
            if (identifiers.Count == 0) return null;

            string currentSceneName = SceneManager.GetActiveScene().name;

            foreach (var id in identifiers)
            {
                if (string.IsNullOrEmpty(id.Scene) || id.Scene.Equals(currentSceneName, StringComparison.OrdinalIgnoreCase))
                {
                    GameObject targetGO = GameObject.Find(id.Path);
                    if (targetGO != null)
                    {
                        var cam = targetGO.GetComponent<Camera>();
                        if (cam != null && cam.enabled)
                        {
                            return cam;
                        }
                    }
                }
            }
            return null;
        }

        private static bool IsLikelyVrCamera(Camera cam)
        {
            // Heuristic 2: Check for TrackedPoseDriver via its string name.
            if (cam.GetComponent("UnityEngine.XR.TrackedPoseDriver") != null)
            {
                VRModCore.LogSpammyDebug($"Camera '{cam.name}' has TrackedPoseDriver component.");
                return true;
            }

            // Heuristic 3: Check by name of the camera or its parent.
            string camName = cam.name.ToLowerInvariant();
            string parentName = cam.transform.parent?.name.ToLowerInvariant() ?? "";
            string[] vrKeywords = ["vr", "head", "hmd", "stereo", "eye", "ovr"];

            foreach (string keyword in vrKeywords)
            {
                if (camName.Contains(keyword) || parentName.Contains(keyword))
                {
                    VRModCore.LogSpammyDebug($"Camera '{cam.name}' or its parent has VR keyword '{keyword}'.");
                    return true;
                }
            }

            return false;
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            internal static extern IntPtr GetModuleHandle(string lpModuleName);
        }
    }
}