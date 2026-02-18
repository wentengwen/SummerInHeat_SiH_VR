#if OPENVR_BUILD
using System.Runtime.InteropServices;
using UnityVRMod.Config;
using UnityVRMod.Core;
using UnityVRMod.Features.VRVisualization.OpenVR;

namespace UnityVRMod.Features.VrVisualization
{
    internal sealed class OpenVrUiProjectionPlane
    {
        private const string ProjectionPlaneName = "UnityVRMod_UIProjectionPlane";
        private const string CaptureObjectName = "OpenVR_UIProjectionCapture";
        private const float PlaneDistanceMeters = 1.25f;
        private const float PlaneVerticalOffsetMeters = -0.08f;
        private const float PlaneWidthMeters = 1.6f;
        private const float RightHandPanelForwardOffsetMeters = 0.01f;
        private const float RightHandPanelUpOffsetMeters = 0.10f;
        private const float RightHandPanelRightOffsetMeters = -0.06f;
        private const float NguiProbeIntervalSeconds = 0.75f;
        private const int MaxProjectionTextureDimension = 1920;
        private const float MinPanelScale = 0.35f;
        private const float MaxPanelScale = 3.0f;
        private const string ViewportFrameObjectName = "OpenVR_UIViewportFrame";
        private const float ViewportFrameLineWidth = 0.003f;
        private const float PixelRectLogIntervalSeconds = 1.0f;

        private static readonly Color ViewportFrameColor = new(1.0f, 0.82f, 0.20f, 0.95f);

        private Camera _mainCamera;
        private GameObject _vrRig;
        private GameObject _plane;
        private Material _planeMaterial;
        private RenderTexture _projectionTexture;
        private int _projectionWidth;
        private int _projectionHeight;
        private GameObject _captureObject;
        private NguiCaptureBehaviour _captureBehaviour;
        private bool _isInitialized;
        private float _nextNguiProbeTime;
        private bool _loggedMissingNguiCamera;
        private bool _isPanelAnchoredToRig;
        private bool _wasAButtonPressed;
        private Vector3 _anchoredLocalPos;
        private Quaternion _anchoredLocalRot;
        private float _lastAppliedPanelScale;
        private GameObject _viewportFrameObject;
        private LineRenderer _viewportFrameLine;
        private Material _viewportFrameMaterial;
        private float _nextPixelRectLogTime;
        private Rect _lastLoggedPixelRect;
        private bool _hasLoggedPixelRect;

        public void Initialize(GameObject vrRig, Camera mainCamera)
        {
            Teardown();

            _vrRig = vrRig;
            _mainCamera = mainCamera;
            _projectionWidth = 0;
            _projectionHeight = 0;
            _nextNguiProbeTime = 0f;
            _loggedMissingNguiCamera = false;
            _isPanelAnchoredToRig = false;
            _wasAButtonPressed = false;
            _anchoredLocalPos = Vector3.zero;
            _anchoredLocalRot = Quaternion.identity;
            _lastAppliedPanelScale = -1f;
            _nextPixelRectLogTime = 0f;
            _lastLoggedPixelRect = default;
            _hasLoggedPixelRect = false;

            CreateProjectionTextureIfNeeded(force: true);
            CreateProjectionPlane();
            CreateCaptureBehaviour();

            _isInitialized = _plane != null && _projectionTexture != null && _captureBehaviour != null;
            if (_isInitialized)
            {
                VRModCore.Log("[UI] NGUI projection plane enabled.");
            }
            else
            {
                VRModCore.LogWarning("[UI] Failed to initialize NGUI projection plane.");
            }
        }

        public void Update(Camera mainCamera, CVRSystem hmd, TrackedDevicePose_t[] trackedPoses)
        {
            if (!_isInitialized) return;
            if (mainCamera != null) _mainCamera = mainCamera;

            CreateProjectionTextureIfNeeded(force: false);
            UpdatePanelScaleIfNeeded();
            HandleAnchorToggle(hmd);
            UpdatePlanePose(hmd, trackedPoses);
            UpdatePlaneScale();
            LogPixelRectIfNeeded();
            UpdatePlaneVisibility();
        }

        public void Teardown()
        {
            _isInitialized = false;

            if (_captureObject != null) UnityEngine.Object.Destroy(_captureObject);
            _captureObject = null;
            _captureBehaviour = null;

            if (_plane != null) UnityEngine.Object.Destroy(_plane);
            _plane = null;

            if (_viewportFrameObject != null) UnityEngine.Object.Destroy(_viewportFrameObject);
            _viewportFrameObject = null;

            if (_planeMaterial != null) UnityEngine.Object.Destroy(_planeMaterial);
            _planeMaterial = null;
            if (_viewportFrameMaterial != null) UnityEngine.Object.Destroy(_viewportFrameMaterial);
            _viewportFrameMaterial = null;
            _viewportFrameLine = null;

            if (_projectionTexture != null)
            {
                _projectionTexture.Release();
                UnityEngine.Object.Destroy(_projectionTexture);
                _projectionTexture = null;
            }

            _vrRig = null;
            _mainCamera = null;
            _projectionWidth = 0;
            _projectionHeight = 0;
            _nextNguiProbeTime = 0f;
            _loggedMissingNguiCamera = false;
            _isPanelAnchoredToRig = false;
            _wasAButtonPressed = false;
            _anchoredLocalPos = Vector3.zero;
            _anchoredLocalRot = Quaternion.identity;
            _lastAppliedPanelScale = -1f;
            _nextPixelRectLogTime = 0f;
            _lastLoggedPixelRect = default;
            _hasLoggedPixelRect = false;
        }

        private void CreateProjectionPlane()
        {
            if (_vrRig == null || _projectionTexture == null) return;

            _plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _plane.name = ProjectionPlaneName;
            _plane.transform.SetParent(_vrRig.transform, false);
            _plane.layer = _vrRig.layer;

            object collider = _plane.GetComponent("Collider");
            if (collider != null) UnityEngine.Object.Destroy(collider as UnityEngine.Object);

            var renderer = _plane.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            Shader shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            _planeMaterial = new Material(shader);
            _planeMaterial.mainTexture = _projectionTexture;
            renderer.sharedMaterial = _planeMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            CreateViewportFrameIfNeeded();

            UpdatePlanePose(null, null);
            UpdatePlaneScale();
        }

        private void CreateCaptureBehaviour()
        {
            if (_projectionTexture == null) return;

            _captureObject = new GameObject(CaptureObjectName);
            if (_vrRig != null) _captureObject.transform.SetParent(_vrRig.transform, false);

            _captureBehaviour = _captureObject.AddComponent<NguiCaptureBehaviour>();
            _captureBehaviour.SetTarget(_projectionTexture);
            _captureBehaviour.enabled = true;
        }

        private void CreateProjectionTextureIfNeeded(bool force)
        {
            int targetWidth = Mathf.Clamp(Screen.width, 640, MaxProjectionTextureDimension);
            int targetHeight = Mathf.Clamp(Screen.height, 360, MaxProjectionTextureDimension);
            if (!force && targetWidth == _projectionWidth && targetHeight == _projectionHeight && _projectionTexture != null)
            {
                return;
            }

            if (_projectionTexture != null)
            {
                _projectionTexture.Release();
                UnityEngine.Object.Destroy(_projectionTexture);
                _projectionTexture = null;
            }

            _projectionTexture = new RenderTexture(targetWidth, targetHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "OpenVR_UIProjectionTexture",
                useMipMap = false,
                autoGenerateMips = false
            };
            _projectionTexture.Create();

            _projectionWidth = targetWidth;
            _projectionHeight = targetHeight;

            if (_planeMaterial != null) _planeMaterial.mainTexture = _projectionTexture;
            if (_captureBehaviour != null) _captureBehaviour.SetTarget(_projectionTexture);

            UpdatePlaneScale();
        }

        private void UpdatePlanePose(CVRSystem hmd, TrackedDevicePose_t[] trackedPoses)
        {
            if (_plane == null) return;

            if (_isPanelAnchoredToRig && _vrRig != null)
            {
                Vector3 anchoredWorldPos = _vrRig.transform.TransformPoint(_anchoredLocalPos);
                Quaternion anchoredWorldRot = _vrRig.transform.rotation * _anchoredLocalRot;

#if MONO
                _plane.transform.position = anchoredWorldPos;
                _plane.transform.rotation = anchoredWorldRot;
#elif CPP
                _plane.transform.SetPositionAndRotation(anchoredWorldPos, anchoredWorldRot);
#endif
                return;
            }

            if (TryGetRightControllerPose(hmd, trackedPoses, out Vector3 rightHandWorldPos, out Quaternion rightHandWorldRot))
            {
                Vector3 localOffset = new(RightHandPanelRightOffsetMeters, RightHandPanelUpOffsetMeters, RightHandPanelForwardOffsetMeters);
                Vector3 worldPos = rightHandWorldPos + (rightHandWorldRot * localOffset);
                Vector3 forward = rightHandWorldRot * Vector3.forward;
                Vector3 up = rightHandWorldRot * Vector3.up;
                Quaternion worldRot = Quaternion.LookRotation(forward, up);

#if MONO
                _plane.transform.position = worldPos;
                _plane.transform.rotation = worldRot;
#elif CPP
                _plane.transform.SetPositionAndRotation(worldPos, worldRot);
#endif
                return;
            }

            Transform referenceTransform = _mainCamera != null ? _mainCamera.transform : _vrRig?.transform;
            if (referenceTransform == null) return;

            Vector3 fallbackForward = referenceTransform.forward;
            Vector3 fallbackUp = referenceTransform.up;
            if (fallbackForward.sqrMagnitude < 0.001f) fallbackForward = Vector3.forward;
            if (fallbackUp.sqrMagnitude < 0.001f) fallbackUp = Vector3.up;

            Vector3 fallbackWorldPos = referenceTransform.position + fallbackForward.normalized * PlaneDistanceMeters + fallbackUp.normalized * PlaneVerticalOffsetMeters;
            Quaternion fallbackWorldRot = Quaternion.LookRotation(fallbackForward.normalized, fallbackUp.normalized);

#if MONO
            _plane.transform.position = fallbackWorldPos;
            _plane.transform.rotation = fallbackWorldRot;
#elif CPP
            _plane.transform.SetPositionAndRotation(fallbackWorldPos, fallbackWorldRot);
#endif
        }

        private bool TryGetRightControllerPose(CVRSystem hmd, TrackedDevicePose_t[] trackedPoses, out Vector3 worldPos, out Quaternion worldRot)
        {
            worldPos = default;
            worldRot = default;

            if (_vrRig == null || hmd == null || trackedPoses == null) return false;

            uint deviceIndex = hmd.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);
            if (deviceIndex == OpenVR.k_unTrackedDeviceIndexInvalid || deviceIndex >= trackedPoses.Length) return false;
            if (!hmd.IsTrackedDeviceConnected(deviceIndex) || !trackedPoses[deviceIndex].bPoseIsValid) return false;

            HmdMatrix34_t pose = trackedPoses[deviceIndex].mDeviceToAbsoluteTracking;
            Vector3 localPos = GetPositionFromHmdMatrix(pose);
            Quaternion localRot = GetRotationFromHmdMatrix(pose);

            worldPos = _vrRig.transform.TransformPoint(localPos);
            worldRot = _vrRig.transform.rotation * localRot;
            return true;
        }

        private void UpdatePlaneScale()
        {
            if (_plane == null) return;

            Rect viewport = GetPrimaryNguiViewportNormalized();
            float baseWidth = PlaneWidthMeters * GetPanelScaleMultiplier();
            float baseHeight = baseWidth / Mathf.Max(0.1f, _projectionHeight > 0 ? (float)_projectionWidth / _projectionHeight : (16f / 9f));
            float width = Mathf.Max(0.02f, baseWidth * Mathf.Max(0.01f, viewport.width));
            float height = Mathf.Max(0.02f, baseHeight * Mathf.Max(0.01f, viewport.height));
            _plane.transform.localScale = new Vector3(width, height, 1f);

            if (_viewportFrameLine != null)
            {
                // Frame always follows panel bounds in local space.
                _viewportFrameLine.SetPosition(0, new Vector3(-0.5f, -0.5f, 0.002f));
                _viewportFrameLine.SetPosition(1, new Vector3(-0.5f, 0.5f, 0.002f));
                _viewportFrameLine.SetPosition(2, new Vector3(0.5f, 0.5f, 0.002f));
                _viewportFrameLine.SetPosition(3, new Vector3(0.5f, -0.5f, 0.002f));
                _viewportFrameLine.SetPosition(4, new Vector3(-0.5f, -0.5f, 0.002f));
            }
        }

        private void CreateViewportFrameIfNeeded()
        {
            if (_plane == null || _viewportFrameObject != null) return;

            _viewportFrameObject = new GameObject(ViewportFrameObjectName);
            _viewportFrameObject.transform.SetParent(_plane.transform, false);
            _viewportFrameLine = _viewportFrameObject.AddComponent<LineRenderer>();
            _viewportFrameLine.useWorldSpace = false;
            _viewportFrameLine.loop = false;
            _viewportFrameLine.positionCount = 5;
            _viewportFrameLine.startWidth = ViewportFrameLineWidth;
            _viewportFrameLine.endWidth = ViewportFrameLineWidth;
            _viewportFrameLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _viewportFrameLine.receiveShadows = false;
            _viewportFrameLine.numCapVertices = 4;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            _viewportFrameMaterial = new Material(shader);
            _viewportFrameMaterial.color = ViewportFrameColor;
            _viewportFrameLine.material = _viewportFrameMaterial;
        }

        private void LogPixelRectIfNeeded()
        {
            if (Time.time < _nextPixelRectLogTime) return;
            _nextPixelRectLogTime = Time.time + PixelRectLogIntervalSeconds;
            if (!TryGetPrimaryNguiPixelRect(out Rect pixelRect)) return;

            if (_hasLoggedPixelRect &&
                Mathf.Abs(pixelRect.x - _lastLoggedPixelRect.x) < 0.5f &&
                Mathf.Abs(pixelRect.y - _lastLoggedPixelRect.y) < 0.5f &&
                Mathf.Abs(pixelRect.width - _lastLoggedPixelRect.width) < 0.5f &&
                Mathf.Abs(pixelRect.height - _lastLoggedPixelRect.height) < 0.5f)
            {
                return;
            }

            _lastLoggedPixelRect = pixelRect;
            _hasLoggedPixelRect = true;

            float normW = Screen.width > 0 ? pixelRect.width / Screen.width : 1f;
            float normH = Screen.height > 0 ? pixelRect.height / Screen.height : 1f;
            VRModCore.Log($"[UI] UICamera.pixelRect px=({pixelRect.x:F0},{pixelRect.y:F0},{pixelRect.width:F0},{pixelRect.height:F0}) norm=({normW:F3},{normH:F3})");
        }

        private void UpdatePanelScaleIfNeeded()
        {
            float targetScale = GetPanelScaleMultiplier();
            if (Mathf.Abs(targetScale - _lastAppliedPanelScale) <= 0.0001f) return;

            _lastAppliedPanelScale = targetScale;
            UpdatePlaneScale();
            VRModCore.Log($"[UI] Panel scale set to {targetScale:F2}x");
        }

        private float GetPanelScaleMultiplier()
        {
            float configuredScale = ConfigManager.OpenVR_UiPanelScale?.Value ?? 1.0f;
            return Mathf.Clamp(configuredScale, MinPanelScale, MaxPanelScale);
        }

        private Rect GetPrimaryNguiViewportNormalized()
        {
            if (Screen.width <= 0 || Screen.height <= 0) return new Rect(0f, 0f, 1f, 1f);
            if (!TryGetPrimaryNguiPixelRect(out Rect pixelRect)) return new Rect(0f, 0f, 1f, 1f);

            float xMin = Mathf.Clamp(pixelRect.xMin / Screen.width, 0f, 1f);
            float yMin = Mathf.Clamp(pixelRect.yMin / Screen.height, 0f, 1f);
            float xMax = Mathf.Clamp(pixelRect.xMax / Screen.width, 0f, 1f);
            float yMax = Mathf.Clamp(pixelRect.yMax / Screen.height, 0f, 1f);
            float w = Mathf.Max(0f, xMax - xMin);
            float h = Mathf.Max(0f, yMax - yMin);
            if (w < 0.01f || h < 0.01f) return new Rect(0f, 0f, 1f, 1f);

            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private void HandleAnchorToggle(CVRSystem hmd)
        {
            bool isPressedNow = false;
            if (TryReadRightControllerState(hmd, out VRControllerState_t rightState))
            {
                isPressedNow = IsButtonPressed(rightState.ulButtonPressed, EVRButtonId.k_EButton_A);
            }

            bool justPressed = isPressedNow && !_wasAButtonPressed;
            _wasAButtonPressed = isPressedNow;
            if (!justPressed) return;

            _isPanelAnchoredToRig = !_isPanelAnchoredToRig;
            if (_isPanelAnchoredToRig)
            {
                CaptureAnchoredPoseFromCurrent();
                VRModCore.Log("[UI] Panel mode: Anchored (follows rig movement, not controller).");
            }
            else
            {
                VRModCore.Log("[UI] Panel mode: Right-hand follow.");
            }
        }

        private void CaptureAnchoredPoseFromCurrent()
        {
            if (_vrRig == null || _plane == null) return;

            _anchoredLocalPos = _vrRig.transform.InverseTransformPoint(_plane.transform.position);
            _anchoredLocalRot = Quaternion.Inverse(_vrRig.transform.rotation) * _plane.transform.rotation;
        }

        private void UpdatePlaneVisibility()
        {
            if (_plane == null) return;
            if (Time.time < _nextNguiProbeTime) return;

            _nextNguiProbeTime = Time.time + NguiProbeIntervalSeconds;

            bool hasNguiCamera = HasActiveNguiCamera();
            if (!hasNguiCamera)
            {
                if (!_loggedMissingNguiCamera)
                {
                    _loggedMissingNguiCamera = true;
                    VRModCore.LogWarning("[UI][NGUI] No active UICamera found. Projection plane hidden.");
                }
            }
            else
            {
                _loggedMissingNguiCamera = false;
            }

            if (_plane.activeSelf != hasNguiCamera)
            {
                _plane.SetActive(hasNguiCamera);
            }
        }

        private static bool HasActiveNguiCamera()
        {
            Type nguiUiCameraType = ResolveTypeAnyAssembly("UICamera");
            if (nguiUiCameraType == null) return false;

            foreach (Camera cam in Camera.allCameras)
            {
                if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy) continue;
                if (cam.GetComponent(nguiUiCameraType) != null) return true;
            }

            return false;
        }

        private static bool TryGetPrimaryNguiPixelRect(out Rect pixelRect)
        {
            pixelRect = new Rect(0f, 0f, Screen.width, Screen.height);
            Type nguiUiCameraType = ResolveTypeAnyAssembly("UICamera");
            if (nguiUiCameraType == null) return false;

            Camera bestCamera = null;
            float bestDepth = float.MinValue;
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy) continue;
                if (cam.GetComponent(nguiUiCameraType) == null) continue;

                if (bestCamera == null || cam.depth > bestDepth)
                {
                    bestCamera = cam;
                    bestDepth = cam.depth;
                }
            }

            if (bestCamera == null) return false;
            pixelRect = bestCamera.pixelRect;
            return true;
        }

        private static bool TryReadRightControllerState(CVRSystem hmd, out VRControllerState_t state)
        {
            state = new VRControllerState_t();
            if (hmd == null) return false;

            uint deviceIndex = hmd.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);
            if (deviceIndex == OpenVR.k_unTrackedDeviceIndexInvalid || !hmd.IsTrackedDeviceConnected(deviceIndex))
            {
                return false;
            }

            return hmd.GetControllerState(deviceIndex, ref state, (uint)Marshal.SizeOf(typeof(VRControllerState_t)));
        }

        private static bool IsButtonPressed(ulong buttonMask, EVRButtonId buttonId)
        {
            int buttonBit = (int)buttonId;
            if (buttonBit < 0 || buttonBit > 63) return false;
            ulong bit = 1UL << buttonBit;
            return (buttonMask & bit) != 0;
        }

        private static Type ResolveTypeAnyAssembly(string fullTypeName)
        {
            Type type = Type.GetType(fullTypeName, false);
            if (type != null) return type;

            type = Type.GetType($"{fullTypeName}, Assembly-CSharp", false);
            if (type != null) return type;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(fullTypeName, false);
                if (type != null) return type;
            }

            return null;
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

        private sealed class NguiCaptureBehaviour : MonoBehaviour
        {
            private const float SourceRefreshIntervalSeconds = 0.75f;

            private RenderTexture _target;
            private Camera _captureCamera;
            private Camera _sourceCamera;
            private Type _nguiUiCameraType;
            private float _nextSourceRefreshTime;
            private bool _bindingsResolved;
            private bool _bindingsFailed;
            private bool _loggedMissingSource;

            internal void SetTarget(RenderTexture target)
            {
                _target = target;
                if (_captureCamera != null) _captureCamera.targetTexture = _target;
            }

            private void LateUpdate()
            {
                if (_target == null || !_target.IsCreated()) return;
                if (!EnsureBindings()) return;

                EnsureCaptureCamera();
                RefreshSourceCameraIfNeeded();

                if (_captureCamera == null || _sourceCamera == null)
                {
                    ClearTarget();
                    return;
                }

                RenderFromSourceCamera();
            }

            private void OnDestroy()
            {
                if (_captureCamera != null) UnityEngine.Object.Destroy(_captureCamera.gameObject);
                _captureCamera = null;
                _sourceCamera = null;
            }

            private bool EnsureBindings()
            {
                if (_bindingsResolved) return true;
                if (_bindingsFailed) return false;

                _nguiUiCameraType = ResolveTypeAnyAssembly("UICamera");
                if (_nguiUiCameraType == null)
                {
                    _bindingsFailed = true;
                    VRModCore.LogWarning("[UI][NGUI] UICamera type not found. NGUI projection capture disabled.");
                    return false;
                }

                _bindingsResolved = true;
                return true;
            }

            private void EnsureCaptureCamera()
            {
                if (_captureCamera != null) return;

                GameObject cameraObject = new("OpenVR_UICaptureCamera");
                cameraObject.transform.SetParent(transform, false);
                _captureCamera = cameraObject.AddComponent<Camera>();
                _captureCamera.enabled = false;
                _captureCamera.stereoTargetEye = StereoTargetEyeMask.None;
                _captureCamera.targetTexture = _target;
                _captureCamera.clearFlags = CameraClearFlags.SolidColor;
                _captureCamera.backgroundColor = Color.clear;
                _captureCamera.allowHDR = false;
                _captureCamera.allowMSAA = false;
            }

            private void RefreshSourceCameraIfNeeded()
            {
                if (_sourceCamera != null && _sourceCamera.enabled && _sourceCamera.gameObject.activeInHierarchy && Time.time < _nextSourceRefreshTime)
                {
                    return;
                }

                _nextSourceRefreshTime = Time.time + SourceRefreshIntervalSeconds;

                Camera best = null;
                float bestDepth = float.MinValue;
                foreach (Camera cam in Camera.allCameras)
                {
                    if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy) continue;
                    if (cam.GetComponent(_nguiUiCameraType) == null) continue;

                    if (best == null || cam.depth >= bestDepth)
                    {
                        best = cam;
                        bestDepth = cam.depth;
                    }
                }

                _sourceCamera = best;
                if (_sourceCamera == null)
                {
                    if (!_loggedMissingSource)
                    {
                        _loggedMissingSource = true;
                        VRModCore.LogWarning("[UI][NGUI] No active UICamera available for projection capture.");
                    }
                }
                else
                {
                    _loggedMissingSource = false;
                }
            }

            private void RenderFromSourceCamera()
            {
                if (_captureCamera == null || _sourceCamera == null || _target == null) return;

                _captureCamera.CopyFrom(_sourceCamera);
                _captureCamera.enabled = false;
                _captureCamera.stereoTargetEye = StereoTargetEyeMask.None;
                _captureCamera.targetTexture = _target;
                _captureCamera.clearFlags = CameraClearFlags.SolidColor;
                _captureCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _captureCamera.allowHDR = false;
                _captureCamera.allowMSAA = false;

                int uiLayer = LayerMask.NameToLayer("UI");
                if (uiLayer >= 0)
                {
                    int uiMask = 1 << uiLayer;
                    if ((_sourceCamera.cullingMask & uiMask) != 0)
                    {
                        _captureCamera.cullingMask = uiMask;
                    }
                }

                _captureCamera.Render();
            }

            private void ClearTarget()
            {
                if (_target == null) return;

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = _target;
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = previous;
            }
        }
    }
}
#endif
