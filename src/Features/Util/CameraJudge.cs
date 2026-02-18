using System.Text;
using UnityEngine.SceneManagement;
using UnityVRMod.Core;

namespace UnityVRMod.Features.Util
{
    internal enum CameraJudgement
    {
        Ignore,
        SubCamera,
        MainCamera,
        GUI,
        GUIAndCamera
    }

    internal enum SceneCameraDimension
    {
        Scene2D,
        Scene3D
    }

    internal static class CameraJudge
    {
        private const string MainCameraTag = "MainCamera";
        private const int MaxListedCamerasInSummary = 24;
        private const int MaxListedChildCamerasInHierarchySummary = 12;
        private const int MaxListedScriptsPerObject = 12;
        private static readonly string[] Hybrid2DSceneNames =
        [
            "ADV"
        ];
        private static readonly string[] IgnoreCameraNameTokens =
        [
            "UnityVRMod",
            "OpenVR_",
            "OpenXR_",
            "XrVrCamera",
            "VRGIN",
            "poseUpdater"
        ];

        private static Type _nguiUiCameraType;
        private static bool _nguiUiCameraTypeResolved;

        public static CameraJudgement JudgeCamera(Camera camera)
        {
            if (camera == null)
            {
                return CameraJudgement.Ignore;
            }

            if (ShouldIgnoreByName(camera.name))
            {
                return CameraJudgement.Ignore;
            }

            bool guiInterested = IsGuiInterested(camera);
            if (camera.targetTexture == null)
            {
                if (guiInterested)
                {
                    return CameraJudgement.GUIAndCamera;
                }

                if (camera.CompareTag(MainCameraTag))
                {
                    return CameraJudgement.MainCamera;
                }

                return CameraJudgement.SubCamera;
            }

            return guiInterested ? CameraJudgement.GUI : CameraJudgement.Ignore;
        }

        public static SceneCameraDimension JudgeSceneDimensionByMainCameraTag(
            out int mainTagCameraCount,
            out int mainTagEnabledCameraCount)
        {
            mainTagCameraCount = 0;
            mainTagEnabledCameraCount = 0;
            string sceneName = SceneManager.GetActiveScene().name;

            Camera[] cameras = GetSceneCameras();
            foreach (Camera camera in cameras)
            {
                if (camera == null || !camera.CompareTag(MainCameraTag))
                {
                    continue;
                }

                mainTagCameraCount++;
                if (camera.enabled && camera.gameObject.activeInHierarchy)
                {
                    mainTagEnabledCameraCount++;
                }
            }

            if (IsHybrid2DSceneActive(sceneName))
            {
                return SceneCameraDimension.Scene2D;
            }

            return mainTagEnabledCameraCount > 0
                ? SceneCameraDimension.Scene3D
                : SceneCameraDimension.Scene2D;
        }

        public static bool IsHybrid2DSceneActive(string sceneName = null)
        {
            string resolvedSceneName = sceneName;
            if (string.IsNullOrWhiteSpace(resolvedSceneName))
            {
                resolvedSceneName = SceneManager.GetActiveScene().name;
            }

            for (int i = 0; i < Hybrid2DSceneNames.Length; i++)
            {
                if (string.Equals(Hybrid2DSceneNames[i], resolvedSceneName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static string BuildStateSignature()
        {
            Camera[] cameras = GetSceneCameras();
            SceneCameraDimension sceneDimension = JudgeSceneDimensionByMainCameraTag(
                out int mainTagCount,
                out int mainTagEnabledCount);

            var sb = new StringBuilder(256);
            sb.Append(SceneManager.GetActiveScene().name);
            sb.Append('|');
            sb.Append((int)sceneDimension);
            sb.Append('|');
            sb.Append(mainTagCount);
            sb.Append('|');
            sb.Append(mainTagEnabledCount);

            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (camera == null) continue;

                sb.Append('|');
                sb.Append(camera.GetInstanceID());
                sb.Append(':');
                sb.Append((int)JudgeCamera(camera));
                sb.Append(':');
                sb.Append(camera.enabled ? 1 : 0);
                sb.Append(':');
                sb.Append(camera.gameObject.activeInHierarchy ? 1 : 0);
            }

            return sb.ToString();
        }

        public static string BuildDebugSummary()
        {
            Camera[] cameras = GetSceneCameras();
            SceneCameraDimension sceneDimension = JudgeSceneDimensionByMainCameraTag(
                out int mainTagCount,
                out int mainTagEnabledCount);

            var sb = new StringBuilder(1024);
            sb.Append("[CameraJudge] Scene='");
            sb.Append(SceneManager.GetActiveScene().name);
            sb.Append("' Dimension=");
            sb.Append(sceneDimension);
            if (IsHybrid2DSceneActive())
            {
                sb.Append("(Hybrid2D)");
            }
            sb.Append(" MainTag(total=");
            sb.Append(mainTagCount);
            sb.Append(", enabled=");
            sb.Append(mainTagEnabledCount);
            sb.Append(") Cameras=");
            sb.Append(cameras.Length);

            int listed = 0;
            Array.Sort(cameras, CompareCamerasByDepthThenName);
            for (int i = 0; i < cameras.Length && listed < MaxListedCamerasInSummary; i++)
            {
                Camera camera = cameras[i];
                if (camera == null) continue;

                listed++;
                bool isMainTag = camera.CompareTag(MainCameraTag);
                bool isEnabled = camera.enabled && camera.gameObject.activeInHierarchy;
                bool hasTargetTexture = camera.targetTexture != null;
                bool isUiCamera = IsGuiInterested(camera);
                CameraJudgement judgement = JudgeCamera(camera);

                sb.Append(" | ");
                sb.Append(camera.name);
                sb.Append("=>");
                sb.Append(judgement);
                sb.Append(" depth=");
                sb.Append(camera.depth.ToString("F1"));
                sb.Append(" mainTag=");
                sb.Append(isMainTag ? 1 : 0);
                sb.Append(" enabled=");
                sb.Append(isEnabled ? 1 : 0);
                sb.Append(" rt=");
                sb.Append(hasTargetTexture ? 1 : 0);
                sb.Append(" ui=");
                sb.Append(isUiCamera ? 1 : 0);
            }

            if (cameras.Length > MaxListedCamerasInSummary)
            {
                sb.Append(" | ...");
            }

            return sb.ToString();
        }

        public static string BuildMainCameraHierarchyDebugSummary(Camera mainCamera = null)
        {
            Camera resolvedMainCamera = mainCamera ?? Camera.main;
            if (resolvedMainCamera == null)
            {
                return "[CameraJudge][MainCamHierarchy] No main camera resolved (Camera.main is null).";
            }

            var sb = new StringBuilder(1024);
            sb.Append("[CameraJudge][MainCamHierarchy] Scene='");
            sb.Append(SceneManager.GetActiveScene().name);
            sb.Append("' Main=");
            AppendCameraInfo(sb, resolvedMainCamera);
            sb.Append(" MainScripts=");
            AppendScriptList(sb, resolvedMainCamera.gameObject);

            Camera[] hierarchyCameras = resolvedMainCamera.GetComponentsInChildren<Camera>(true);
            int childCameraCount = 0;
            for (int i = 0; i < hierarchyCameras.Length; i++)
            {
                Camera childCamera = hierarchyCameras[i];
                if (childCamera == null || childCamera == resolvedMainCamera) continue;
                childCameraCount++;
            }

            sb.Append(" ChildCameras=");
            sb.Append(childCameraCount);

            int listedChildCameras = 0;
            for (int i = 0; i < hierarchyCameras.Length && listedChildCameras < MaxListedChildCamerasInHierarchySummary; i++)
            {
                Camera childCamera = hierarchyCameras[i];
                if (childCamera == null || childCamera == resolvedMainCamera) continue;

                listedChildCameras++;
                sb.Append(" | Child#");
                sb.Append(listedChildCameras);
                sb.Append(" path=");
                sb.Append(BuildRelativePath(resolvedMainCamera.transform, childCamera.transform));
                sb.Append(" cam=");
                AppendCameraInfo(sb, childCamera);
                sb.Append(" scripts=");
                AppendScriptList(sb, childCamera.gameObject);
            }

            if (childCameraCount > MaxListedChildCamerasInHierarchySummary)
            {
                sb.Append(" | ...");
            }

            return sb.ToString();
        }

        private static void AppendCameraInfo(StringBuilder sb, Camera camera)
        {
            if (camera == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append(camera.name);
            sb.Append("{enabled=");
            sb.Append(camera.enabled && camera.gameObject.activeInHierarchy ? 1 : 0);
            sb.Append(", depth=");
            sb.Append(camera.depth.ToString("F1"));
            sb.Append(", clear=");
            sb.Append(camera.clearFlags);
            sb.Append(", mask=");
            sb.Append(camera.cullingMask);
            sb.Append(", rt=");
            sb.Append(camera.targetTexture != null ? camera.targetTexture.name : "null");
            sb.Append(", tag=");
            sb.Append(camera.tag);
            sb.Append('}');
        }

        private static void AppendScriptList(StringBuilder sb, GameObject gameObject)
        {
            if (gameObject == null)
            {
                sb.Append("none");
                return;
            }

            MonoBehaviour[] scripts = gameObject.GetComponents<MonoBehaviour>();
            if (scripts == null || scripts.Length == 0)
            {
                sb.Append("none");
                return;
            }

            int listed = 0;
            for (int i = 0; i < scripts.Length; i++)
            {
                if (listed > 0)
                {
                    sb.Append(',');
                }

                if (listed >= MaxListedScriptsPerObject)
                {
                    sb.Append("...");
                    return;
                }

                MonoBehaviour script = scripts[i];
                if (script == null)
                {
                    sb.Append("MissingScript");
                }
                else
                {
                    sb.Append(script.GetType().Name);
                }

                listed++;
            }

            if (listed == 0)
            {
                sb.Append("none");
            }
        }

        private static string BuildRelativePath(Transform root, Transform target)
        {
            if (target == null)
            {
                return "(null)";
            }

            if (root == null || target == root)
            {
                return target.name;
            }

            var segments = new List<string>();
            Transform current = target;
            int guard = 0;
            while (current != null && guard < 128)
            {
                segments.Add(current.name);
                if (current == root)
                {
                    segments.Reverse();
                    return string.Join("/", segments);
                }

                current = current.parent;
                guard++;
            }

            return target.name;
        }

        private static int CompareCamerasByDepthThenName(Camera left, Camera right)
        {
            if (left == null && right == null) return 0;
            if (left == null) return 1;
            if (right == null) return -1;

            int depthCompare = right.depth.CompareTo(left.depth);
            if (depthCompare != 0) return depthCompare;
            return string.Compare(left.name, right.name, StringComparison.Ordinal);
        }

        private static bool ShouldIgnoreByName(string cameraName)
        {
            if (string.IsNullOrEmpty(cameraName)) return false;

            for (int i = 0; i < IgnoreCameraNameTokens.Length; i++)
            {
                if (cameraName.IndexOf(IgnoreCameraNameTokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsGuiInterested(Camera camera)
        {
            Type nguiType = ResolveNguiUiCameraType();
            if (nguiType == null) return false;
            return camera.GetComponent(nguiType) != null;
        }

        private static Type ResolveNguiUiCameraType()
        {
            if (_nguiUiCameraTypeResolved) return _nguiUiCameraType;

            _nguiUiCameraTypeResolved = true;
            _nguiUiCameraType = Type.GetType("UICamera", false);
            if (_nguiUiCameraType != null) return _nguiUiCameraType;

            _nguiUiCameraType = Type.GetType("UICamera, Assembly-CSharp", false);
            if (_nguiUiCameraType != null) return _nguiUiCameraType;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType("UICamera", false);
                if (type != null)
                {
                    _nguiUiCameraType = type;
                    break;
                }
            }

            return _nguiUiCameraType;
        }

        private static Camera[] GetSceneCameras()
        {
            Camera[] allCameras = Resources.FindObjectsOfTypeAll<Camera>();
            var sceneCameras = new List<Camera>(allCameras.Length);
            for (int i = 0; i < allCameras.Length; i++)
            {
                Camera camera = allCameras[i];
                if (camera == null) continue;
                if (!camera.gameObject.scene.IsValid()) continue;

                HideFlags hideFlags = camera.hideFlags;
                if ((hideFlags & HideFlags.HideAndDontSave) != 0) continue;
                if ((hideFlags & HideFlags.DontSave) != 0) continue;
                if ((hideFlags & HideFlags.NotEditable) != 0) continue;

                sceneCameras.Add(camera);
            }

            return sceneCameras.ToArray();
        }
    }
}
