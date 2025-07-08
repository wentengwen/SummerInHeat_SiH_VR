using UnityEngine.Rendering;
using UnityVRMod.Config;
using UnityVRMod.Core;
using UnityVRMod.Features.Util;
using UnityVRMod.Features.VRVisualization.OpenVR;

namespace UnityVRMod.Features.VrVisualization
{
    internal class VrCameraSetup_CoreOpenVR : IVrCameraSetup
    {
        private bool _isVrInitialized = false;
        private GameObject _vrRig = null;
        private float _currentAppliedRigScale = 1.0f;

        private Camera _leftVrCamera = null;
        private GameObject _leftVrCameraGO = null;
        private Camera _rightVrCamera = null;
        private GameObject _rightVrCameraGO = null;
        private float _lastCalculatedVerticalOffset;

        private GameObject _currentlyTrackedOriginalCameraGO = null;

        private CVRSystem _hmd = null;
        private CVRCompositor _compositor = null;
        private readonly TrackedDevicePose_t[] _trackedPoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        private readonly TrackedDevicePose_t[] _gamePoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

        private uint _hmdRenderWidth = 0;
        private uint _hmdRenderHeight = 0;
        private RenderTexture _leftEyeTexture = null;
        private RenderTexture _rightEyeTexture = null;
        private ETextureType _vrTextureType = ETextureType.DirectX;
        private static CommandBuffer _flushCommandBuffer;

        public bool IsVrAvailable => _isVrInitialized && _hmd != null && _compositor != null;

        public bool InitializeVr(string applicationKey)
        {
            VRModCore.LogRuntimeDebug($"InitializeVr START. AppKey: {(string.IsNullOrEmpty(applicationKey) ? "N/A" : applicationKey)}");
            if (_isVrInitialized)
            {
                VRModCore.LogWarning("InitializeVr called but already initialized.");
                return true;
            }

            EVRInitError initError = EVRInitError.None;
            try
            {
                _hmd = OpenVR.Init(ref initError, EVRApplicationType.VRApplication_Scene, applicationKey);
                if (initError != EVRInitError.None)
                {
                    VRModCore.LogError($"OpenVR.Init FAILED. Error: {initError} ({OpenVR.GetStringForHmdError(initError)})");
                    TeardownVrInternal(); return false;
                }
                VRModCore.LogRuntimeDebug("OpenVR.Init SUCCESS. CVRSystem instance obtained.");

                _compositor = OpenVR.Compositor;
                if (_compositor == null)
                {
                    VRModCore.LogError("OpenVR.Compositor is null. Cannot proceed.");
                    TeardownVrInternal(); return false;
                }
                VRModCore.LogRuntimeDebug("CVRCompositor instance obtained.");

                if (!_hmd.IsTrackedDeviceConnected(OpenVR.k_unTrackedDeviceIndex_Hmd))
                {
                    VRModCore.LogWarning("HMD is not connected.");
                    TeardownVrInternal(); return false;
                }

                string driverName = GetStringProperty(OpenVR.k_unTrackedDeviceIndex_Hmd, ETrackedDeviceProperty.Prop_TrackingSystemName_String);
                string modelNumber = GetStringProperty(OpenVR.k_unTrackedDeviceIndex_Hmd, ETrackedDeviceProperty.Prop_ModelNumber_String);
                VRModCore.Log($"OpenVR: HMD Initialized: Driver='{driverName}', Model='{modelNumber}'");

                _hmd.GetRecommendedRenderTargetSize(ref _hmdRenderWidth, ref _hmdRenderHeight);
                VRModCore.LogRuntimeDebug($"Initial Recommended RT Size: {_hmdRenderWidth}x{_hmdRenderHeight}");
                if (_hmdRenderWidth == 0 || _hmdRenderHeight == 0)
                {
                    VRModCore.LogWarning("RT Size zero. Using fallback 1024x1024.");
                    _hmdRenderWidth = 1024; _hmdRenderHeight = 1024;
                }

                var graphicsDeviceType = SystemInfo.graphicsDeviceType;
                if (graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 || graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D12)
                    _vrTextureType = ETextureType.DirectX;
                else if (graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore)
                    _vrTextureType = ETextureType.OpenGL;
                else
                {
                    VRModCore.LogError($"Unsupported Graphics Device Type for OpenVR: {graphicsDeviceType}. Failing initialization.");
                    TeardownVrInternal(); return false;
                }

                if (!SetupRenderTargets())
                {
                    VRModCore.LogError("SetupRenderTargets FAILED. Cannot proceed.");
                    TeardownVrInternal(); return false;
                }

                _compositor.SetTrackingSpace(ETrackingUniverseOrigin.TrackingUniverseStanding);
                _isVrInitialized = true;
                VRModCore.Log($"InitializeVr SUCCESS. RT Size: {_leftEyeTexture.width}x{_leftEyeTexture.height}, TextureType: {_vrTextureType}");

                _flushCommandBuffer ??= new CommandBuffer { name = "VRModFlush_OpenVR" };
            }
            catch (Exception e)
            {
                VRModCore.LogError("InitializeVr EXCEPTION:", e);
                TeardownVrInternal(); return false;
            }
            return _isVrInitialized;
        }

        private string GetStringProperty(uint deviceIndex, ETrackedDeviceProperty prop)
        {
            var error = ETrackedPropertyError.TrackedProp_Success;
            uint capacity = _hmd.GetStringTrackedDeviceProperty(deviceIndex, prop, null, 0, ref error);
            if (capacity == 0) return "";
            var buffer = new System.Text.StringBuilder((int)capacity);
            _hmd.GetStringTrackedDeviceProperty(deviceIndex, prop, buffer, capacity, ref error);
            return (error == ETrackedPropertyError.TrackedProp_Success) ? buffer.ToString() : error.ToString();
        }

        public void SetupCameraRig(Camera mainCamera)
        {
            if (mainCamera == null)
            {
                VRModCore.LogError("SetupCameraRig FAILED: mainCamera is null.");
                return;
            }
            VRModCore.LogRuntimeDebug($"SetupCameraRig for camera '{mainCamera.name}'.");

            if (_vrRig != null) TeardownCameraRig();
            if (!IsVrAvailable) { VRModCore.LogError("SetupCameraRig FAILED: VR system not available/initialized."); return; }

            _vrRig = new GameObject("UnityVRMod_VRRig_OpenVR");
            _currentlyTrackedOriginalCameraGO = mainCamera.gameObject;

            Vector3 targetPosition = mainCamera.transform.position;
            Quaternion targetRotation = Quaternion.Euler(0, mainCamera.transform.eulerAngles.y, 0);

            var poseOverrides = PoseParser.Parse(ConfigManager.ScenePoseOverrides.Value);
            string currentSceneName = mainCamera.gameObject.scene.name;

            if (poseOverrides.TryGetValue(currentSceneName, out PoseOverride poseOverride))
            {
                VRModCore.Log($"Applying pose override for scene '{currentSceneName}'");
                var p = poseOverride.Position;
                var r = poseOverride.Rotation;
                var originalPos = mainCamera.transform.position;
                var originalRot = mainCamera.transform.eulerAngles;

                Vector3 finalPos = new(float.IsNaN(p.x) ? originalPos.x : p.x, float.IsNaN(p.y) ? originalPos.y : p.y, float.IsNaN(p.z) ? originalPos.z : p.z);
                Vector3 finalRot = new(float.IsNaN(r.x) ? originalRot.x : r.x, float.IsNaN(r.y) ? originalRot.y : r.y, float.IsNaN(r.z) ? originalRot.z : r.z);
                targetPosition = finalPos;
                targetRotation = Quaternion.Euler(finalRot);
            }
            _vrRig.transform.SetPositionAndRotation(targetPosition, targetRotation);

            _currentAppliedRigScale = 1.0f / Mathf.Max(0.01f, ConfigManager.VrWorldScale.Value);
            _vrRig.transform.localScale = new Vector3(_currentAppliedRigScale, _currentAppliedRigScale, _currentAppliedRigScale);

            _leftVrCameraGO = new GameObject("OpenVR_VRCamera_Left");
            _leftVrCameraGO.transform.SetParent(_vrRig.transform, false);
            _leftVrCamera = _leftVrCameraGO.AddComponent<Camera>();
            ConfigureVrCamera(_leftVrCamera, mainCamera, "Left");
            if (_leftVrCameraGO.GetComponent<AudioListener>() == null) _leftVrCameraGO.AddComponent<AudioListener>();

            _rightVrCameraGO = new GameObject("OpenVR_VRCamera_Right");
            _rightVrCameraGO.transform.SetParent(_vrRig.transform, false);
            _rightVrCamera = _rightVrCameraGO.AddComponent<Camera>();
            ConfigureVrCamera(_rightVrCamera, mainCamera, "Right");

            _lastCalculatedVerticalOffset = 0f;
            UpdateVerticalOffset();

            VRModCore.Log("OpenVR: VR Camera Rig setup complete.");
        }

        private void ConfigureVrCamera(Camera vrCam, Camera mainCamRef, string eyeName)
        {
            vrCam.stereoTargetEye = StereoTargetEyeMask.None;
            vrCam.enabled = false;

            vrCam.clearFlags = mainCamRef.clearFlags;
            vrCam.backgroundColor = mainCamRef.backgroundColor;
            vrCam.cullingMask = mainCamRef.cullingMask;
            vrCam.renderingPath = mainCamRef.renderingPath;
            vrCam.allowHDR = mainCamRef.allowHDR;
            vrCam.allowMSAA = mainCamRef.allowMSAA;

            float nearClipPlaneUserValue = ConfigManager.VrCameraNearClipPlane.Value;
            vrCam.nearClipPlane = Mathf.Max(0.001f, nearClipPlaneUserValue * _currentAppliedRigScale);
            vrCam.farClipPlane = mainCamRef.farClipPlane * _currentAppliedRigScale;

            VRModCore.LogRuntimeDebug($"  {eyeName} Cam Clips Updated: Near={vrCam.nearClipPlane:F4}, Far={vrCam.farClipPlane:F2}");
        }

        public void UpdatePoses()
        {
            if (!IsVrAvailable || _vrRig == null) return;

            try
            {
                int delay = ConfigManager.OpenVR_WaitGetPosesDelayMs.Value;
                if (delay > 0) System.Threading.Thread.Sleep(delay);

                var compositorError = _compositor.WaitGetPoses(_trackedPoses, _gamePoses);
                if (compositorError != EVRCompositorError.None)
                {
                    VRModCore.LogError($"WaitGetPoses FAILED. Error: {compositorError}");
                    return;
                }

                if (!_trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd].bPoseIsValid)
                {
                    VRModCore.LogWarning("UpdatePoses: HMD pose NOT valid. Rendering from origin to re-establish session.");
                }

                RenderEye(EVREye.Eye_Left);
                RenderEye(EVREye.Eye_Right);
            }
            catch (Exception e)
            {
                VRModCore.LogError("UpdatePoses EXCEPTION:", e);
            }
        }

        private void RenderEye(EVREye eye)
        {
            Camera vrCam = (eye == EVREye.Eye_Left) ? _leftVrCamera : _rightVrCamera;
            RenderTexture targetTexture = (eye == EVREye.Eye_Left) ? _leftEyeTexture : _rightEyeTexture;
            if (vrCam == null || targetTexture == null) return;

            if (_trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd].bPoseIsValid)
            {
                HmdMatrix34_t hmdPose = _trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd].mDeviceToAbsoluteTracking;
                HmdMatrix34_t eyeToHeadMatrix = _hmd.GetEyeToHeadTransform(eye);

                Vector3 headPos = GetPositionFromHmdMatrix(hmdPose);
                Quaternion headRot = GetRotationFromHmdMatrix(hmdPose);
                Vector3 eyeOffset = GetPositionFromHmdMatrix(eyeToHeadMatrix);

#if MONO
                vrCam.transform.localPosition = headPos + (headRot * eyeOffset);
                vrCam.transform.localRotation = headRot;
#elif CPP
                vrCam.transform.SetLocalPositionAndRotation(headPos + (headRot * eyeOffset), headRot);
#endif
            }
            else
            {
                VRModCore.LogWarning("Rendering eye with last known valid pose because current pose is invalid.");
            }

            float l = 0, r = 0, t = 0, b = 0;
            _hmd.GetProjectionRaw(eye, ref l, ref r, ref t, ref b);

            vrCam.projectionMatrix = CreateUnityProjectionMatrixFromOpenVRFrustum(
                l * vrCam.nearClipPlane, r * vrCam.nearClipPlane,
                t * vrCam.nearClipPlane, b * vrCam.nearClipPlane,
                vrCam.nearClipPlane, vrCam.farClipPlane);

            vrCam.targetTexture = targetTexture;

            bool originalInvertCulling = GL.invertCulling;
            GL.invertCulling = true;
            vrCam.Render();
            GL.invertCulling = originalInvertCulling;

            vrCam.targetTexture = null;

            Graphics.ExecuteCommandBuffer(_flushCommandBuffer);

            Texture_t eyeTexture = new() { handle = targetTexture.GetNativeTexturePtr(), eType = _vrTextureType, eColorSpace = EColorSpace.Auto };
            VRTextureBounds_t textureBounds = new() { uMin = 0f, vMin = 0f, uMax = 1f, vMax = 1f };
            var submitError = _compositor.Submit(eye, ref eyeTexture, ref textureBounds, EVRSubmitFlags.Submit_Default);

            if (submitError != EVRCompositorError.None)
            {
                VRModCore.LogError($"Submit FAILED for eye {eye}. Error: {submitError}");
            }
        }

        private static Matrix4x4 CreateUnityProjectionMatrixFromOpenVRFrustum(float left, float right, float top, float bottom, float nearClip, float farClip)
        {
            Matrix4x4 m = new();
            m[0, 0] = (2.0f * nearClip) / (right - left);
            m[0, 2] = (right + left) / (right - left);
            m[1, 1] = (2.0f * nearClip) / (top - bottom);
            m[1, 2] = (top + bottom) / (top - bottom);
            m[2, 2] = -(farClip + nearClip) / (farClip - nearClip);
            m[2, 3] = -(2.0f * farClip * nearClip) / (farClip - nearClip);
            m[3, 2] = -1.0f;
            return m;
        }

        private static Vector3 GetPositionFromHmdMatrix(HmdMatrix34_t matrix)
        {
            return new Vector3(matrix.m3, matrix.m7, -matrix.m11);
        }

        private static Quaternion GetRotationFromHmdMatrix(HmdMatrix34_t matrix)
        {
            Quaternion q = new()
            {
                w = Mathf.Sqrt(Mathf.Max(0, 1 + matrix.m0 + matrix.m5 + matrix.m10)) / 2,
                x = Mathf.Sqrt(Mathf.Max(0, 1 + matrix.m0 - matrix.m5 - matrix.m10)) / 2,
                y = Mathf.Sqrt(Mathf.Max(0, 1 - matrix.m0 + matrix.m5 - matrix.m10)) / 2,
                z = Mathf.Sqrt(Mathf.Max(0, 1 - matrix.m0 - matrix.m5 + matrix.m10)) / 2
            };
            q.x *= Mathf.Sign(q.x * (matrix.m9 - matrix.m6));
            q.y *= Mathf.Sign(q.y * (matrix.m2 - matrix.m8));
            q.z *= Mathf.Sign(q.z * (matrix.m4 - matrix.m1));
            return new Quaternion(-q.x, -q.y, q.z, q.w);
        }

        public void TeardownCameraRig()
        {
            VRModCore.LogRuntimeDebug("Tearing down VR camera rig.");
            ReleaseRenderTargets();
            if (_vrRig != null) UnityEngine.Object.Destroy(_vrRig);
            _vrRig = null;
            _leftVrCamera = null; _leftVrCameraGO = null;
            _rightVrCamera = null; _rightVrCameraGO = null;
            _currentlyTrackedOriginalCameraGO = null;
        }

        private void TeardownVrInternal()
        {
            VRModCore.LogRuntimeDebug("Tearing down OpenVR system.");
            TeardownCameraRig();
            if (_hmd != null) OpenVR.Shutdown();
            _hmd = null;
            _compositor = null;
            _isVrInitialized = false;
        }

        public void TeardownVr() { TeardownVrInternal(); }

        private void ReleaseRenderTargets()
        {
            if (_leftEyeTexture != null) { _leftEyeTexture.Release(); UnityEngine.Object.Destroy(_leftEyeTexture); _leftEyeTexture = null; }
            if (_rightEyeTexture != null) { _rightEyeTexture.Release(); UnityEngine.Object.Destroy(_rightEyeTexture); _rightEyeTexture = null; }
        }

        private bool SetupRenderTargets()
        {
            uint width = _hmdRenderWidth;
            uint height = _hmdRenderHeight;
            int maxDim = ConfigManager.OpenVR_MaxRenderTargetDimension.Value;
            if (maxDim > 0)
            {
                width = Math.Min(width, (uint)maxDim);
                height = Math.Min(height, (uint)maxDim);
            }
            ReleaseRenderTargets();
            _leftEyeTexture = new RenderTexture((int)width, (int)height, 24, RenderTextureFormat.ARGB32);
            _rightEyeTexture = new RenderTexture((int)width, (int)height, 24, RenderTextureFormat.ARGB32);
            return _leftEyeTexture.Create() && _rightEyeTexture.Create();
        }

        public VrCameraRig GetVrCameraGameObjects() => new() { LeftEye = _leftVrCameraGO, RightEye = _rightVrCameraGO };

        public void SetCameraNearClip(float newNearClipBaseValue)
        {
            if (_vrRig == null) return;
            Camera mainCamera = null;
            if (_currentlyTrackedOriginalCameraGO != null)
                mainCamera = _currentlyTrackedOriginalCameraGO.GetComponent<Camera>();
            if (_leftVrCamera != null && mainCamera != null) ConfigureVrCamera(_leftVrCamera, mainCamera, "Left");
            if (_rightVrCamera != null && mainCamera != null) ConfigureVrCamera(_rightVrCamera, mainCamera, "Right");
        }

        private void UpdateVerticalOffset()
        {
            if (_vrRig == null) return;
            float newTotalOffset = ConfigManager.VrUserEyeHeightOffset.Value * _currentAppliedRigScale;
            float delta = newTotalOffset - _lastCalculatedVerticalOffset;
            _vrRig.transform.position += new Vector3(0, delta, 0);
            _lastCalculatedVerticalOffset = newTotalOffset;
        }

        public void SetWorldScale(float newWorldScale, Camera mainCameraRef)
        {
            if (_vrRig == null) return;
            _currentAppliedRigScale = 1.0f / Mathf.Max(0.01f, newWorldScale);
            _vrRig.transform.localScale = new Vector3(_currentAppliedRigScale, _currentAppliedRigScale, _currentAppliedRigScale);

            Camera mainCam = mainCameraRef;
            if (mainCam == null && _currentlyTrackedOriginalCameraGO != null)
                mainCam = _currentlyTrackedOriginalCameraGO.GetComponent<Camera>();

            if (_leftVrCamera != null) ConfigureVrCamera(_leftVrCamera, mainCam, "Left");
            if (_rightVrCamera != null) ConfigureVrCamera(_rightVrCamera, mainCam, "Right");

            UpdateVerticalOffset();
        }

        public void SetUserEyeHeightOffset(float newOffset)
        {
            UpdateVerticalOffset();
        }
    }
}