using UnityEngine.SceneManagement;
using UnityVRMod.Config;
using UnityVRMod.Core;

namespace UnityVRMod.Features.Util
{
    /// <summary>
    /// A utility class responsible for finding the primary game camera that the VR rig should follow.
    /// </summary>
    public static class CameraFinder
    {
        private static Camera _cachedCamera = null;

        /// <summary>
        /// Invalidates the cached camera, forcing a new search on the next call to FindGameCamera.
        /// </summary>
        public static void InvalidateCache()
        {
            VRModCore.LogRuntimeDebug("CameraFinder cache invalidated.");
            _cachedCamera = null;
        }

        /// <summary>
        /// Finds the most appropriate game camera for VR injection.
        /// It prioritizes user-defined overrides and falls back to Camera.main.
        /// It uses a cache to avoid expensive lookups every frame.
        /// </summary>
        /// <returns>The found Camera component, or null if no suitable camera is found.</returns>
        public static Camera FindGameCamera()
        {
            // Return the cached camera if it's still valid (not destroyed and is enabled).
            if (_cachedCamera != null && _cachedCamera.enabled)
            {
                return _cachedCamera;
            }

            // If cache is invalid, find a new camera.
            _cachedCamera = FindGameCameraInternal();
            return _cachedCamera;
        }

        private static Camera FindGameCameraInternal()
        {
            // Priority 1: Check for user-defined camera overrides.
            var identifiers = CameraIdentifierHelper.Parse(ConfigManager.AssertedCameraOverrides.Value);
            if (identifiers.Count > 0)
            {
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
                                VRModCore.Log($"Found game camera via AssertedCameraOverrides: '{id.Path}' in scene '{currentSceneName}'");
                                return cam;
                            }
                        }
                    }
                }
            }

            // Priority 2: Fallback to the default Camera.main.
            var mainCam = Camera.main;
            if (mainCam != null)
            {
                VRModCore.Log("Found game camera via fallback to Camera.main.");
                return mainCam;
            }

            return null;
        }
    }
}