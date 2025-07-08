using System.Runtime.InteropServices;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityVRMod.Config;
using UnityVRMod.Core;
using UnityVRMod.Features.Util;
using UnityVRMod.Features.VRVisualization.OpenXR;

namespace UnityVRMod.Features.VrVisualization
{
    internal class VrCameraSetup_CoreOpenXR : IVrCameraSetup
    {
        private ulong _xrInstance = OpenXRConstants.XR_NULL_HANDLE;
        private ulong _xrSystemId = OpenXRConstants.XR_NULL_SYSTEM_ID;
        private ulong _xrSession = OpenXRConstants.XR_NULL_HANDLE;

        public bool IsVrAvailable { get; private set; } = false;

        private GameObject _vrRig = null;
        private float _currentAppliedRigScale = 1.0f;

        private XrFrameState _xrFrameState;
        private XrSessionState _currentSessionState = XrSessionState.XR_SESSION_STATE_UNKNOWN;
        private readonly XrViewConfigurationType _viewConfigType = XrViewConfigurationType.XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO;
        private readonly List<XrViewConfigurationView> _viewConfigViews = [];

        private readonly List<long> _supportedSwapchainFormats = [];
        private long _selectedSwapchainFormat = 0;
        private readonly List<ulong> _eyeSwapchains = [];
        private readonly List<List<IntPtr>> _eyeSwapchainImages = [];
        private ulong _appSpace = OpenXRConstants.XR_NULL_HANDLE;
        private bool _isSessionRunning = false;
        private XrReferenceSpaceType _appSpaceType = XrReferenceSpaceType.XR_REFERENCE_SPACE_TYPE_LOCAL;
        private XrView[] _locatedViews;
        private XrViewState _locatedViewState;

        private Camera _leftVrCamera = null;
        private GameObject _leftVrCameraGO = null;
        private Camera _rightVrCamera = null;
        private GameObject _rightVrCameraGO = null;

        private RenderTexture _leftEyeIntermediateRT = null;
        private RenderTexture _rightEyeIntermediateRT = null;

        private IntPtr _d3d11Device = IntPtr.Zero;

        private XrCompositionLayerProjectionView[] _projectionLayerViews;
        private IntPtr _pProjectionLayerViews = IntPtr.Zero;
        private XrCompositionLayerProjection _projectionLayer;
        private IntPtr _pProjectionLayer = IntPtr.Zero;
        private IntPtr _pLayersForSubmit = IntPtr.Zero;

        private CameraClearFlags _mainCameraClearFlags;
        private Color _mainCameraBackgroundColor;
        private int _mainCameraCullingMask;

        private GameObject _currentlyTrackedOriginalCameraGO = null;
        private float _lastCalculatedVerticalOffset;
        private static CommandBuffer _flushCommandBuffer;

        private readonly List<List<IntPtr>> _eyeSwapchainSRVs = [];

        public bool InitializeVr(string applicationKey)
        {
            VRModCore.Log("Attempting to initialize OpenXR via P/Invoke...");

            try
            {
                if (!OpenXRNativeLoader.LoadOpenXRLibrary())
                    throw new Exception("Failed to load OpenXR native library (openxr_loader.dll).");

                if (!OpenXRAPI.InitializeCoreFunctions(OpenXRNativeLoader.xrGetInstanceProcAddr_ptr_delegate))
                    throw new Exception("Failed to initialize core OpenXR functions.");

                var appInfo = new XrApplicationInfo
                {
                    applicationName = "UnityVRMod",
                    applicationVersion = 1,
                    engineName = "Unity",
                    engineVersion = 1,
                    apiVersion = OpenXRConstants.XR_API_VERSION_1_1
                };

                string[] requestedExtensions = [OpenXRConstants.XR_KHR_D3D11_ENABLE_EXTENSION_NAME];
                IntPtr pRequestedExtensions = MarshallStringUtils.MarshalStringArrayToAnsi(requestedExtensions);

                var instanceCreateInfo = new XrInstanceCreateInfo
                {
                    type = XrStructureType.XR_TYPE_INSTANCE_CREATE_INFO,
                    applicationInfo = appInfo,
                    enabledExtensionCount = (uint)requestedExtensions.Length,
                    enabledExtensionNames = pRequestedExtensions
                };

                OpenXRHelper.CheckResult(OpenXRAPI.xrCreateInstance(in instanceCreateInfo, out _xrInstance), "xrCreateInstance");
                MarshallStringUtils.FreeMarshalledStringArray(pRequestedExtensions, requestedExtensions.Length);
                if (_xrInstance == OpenXRConstants.XR_NULL_HANDLE) throw new Exception("xrCreateInstance returned a null handle.");

                VRModCore.Log($"OpenXR Instance created. Handle: {_xrInstance}");

                if (!OpenXRAPI.InitializeInstanceFunctions(_xrInstance))
                    throw new Exception("Failed to initialize instance-specific OpenXR functions.");

                _d3d11Device = NativeBridge.GetD3D11DevicePointer(Texture2D.whiteTexture);
                if (_d3d11Device == IntPtr.Zero)
                    throw new Exception("Failed to get D3D11 device pointer.");

                NativeBridge.SetDevicePointerFromCSharp(_d3d11Device);

                var systemGetInfo = new XrSystemGetInfo { type = XrStructureType.XR_TYPE_SYSTEM_GET_INFO, formFactor = XrFormFactor.XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY };
                OpenXRHelper.CheckResult(OpenXRAPI.xrGetSystem(_xrInstance, in systemGetInfo, out _xrSystemId), "xrGetSystem");
                if (_xrSystemId == OpenXRConstants.XR_NULL_SYSTEM_ID) throw new Exception("xrGetSystem failed for HMD.");
                VRModCore.Log($"OpenXR SystemId obtained: {_xrSystemId}");

                var d3d11GraphicsRequirements = new XrGraphicsRequirementsD3D11KHR { type = XrStructureType.XR_TYPE_GRAPHICS_REQUIREMENTS_D3D11_KHR };
                OpenXRHelper.CheckResult(OpenXRAPI.xrGetD3D11GraphicsRequirementsKHR(_xrInstance, _xrSystemId, out d3d11GraphicsRequirements), "xrGetD3D11GraphicsRequirementsKHR");

                var graphicsBinding = new XrGraphicsBindingD3D11KHR { type = XrStructureType.XR_TYPE_GRAPHICS_BINDING_D3D11_KHR, device = _d3d11Device };
                IntPtr pGraphicsBinding = Marshal.AllocHGlobal(Marshal.SizeOf(graphicsBinding));
                Marshal.StructureToPtr(graphicsBinding, pGraphicsBinding, false);
                var sessionCreateInfo = new XrSessionCreateInfo { type = XrStructureType.XR_TYPE_SESSION_CREATE_INFO, next = pGraphicsBinding, systemId = _xrSystemId };
                OpenXRHelper.CheckResult(OpenXRAPI.xrCreateSession(_xrInstance, in sessionCreateInfo, out _xrSession), "xrCreateSession");
                Marshal.FreeHGlobal(pGraphicsBinding);
                if (_xrSession == OpenXRConstants.XR_NULL_HANDLE) throw new Exception("xrCreateSession returned a null handle.");
                VRModCore.Log($"OpenXR Session created. Handle: {_xrSession}");

                var sessionBeginInfo = new XrSessionBeginInfo { type = XrStructureType.XR_TYPE_SESSION_BEGIN_INFO, primaryViewConfigurationType = _viewConfigType };
                OpenXRHelper.CheckResult(OpenXRAPI.xrBeginSession(_xrSession, in sessionBeginInfo), "xrBeginSession");
                _currentSessionState = XrSessionState.XR_SESSION_STATE_READY;
                _isSessionRunning = true;
                VRModCore.Log("OpenXR Session begun.");

                var refSpaceCreateInfo = new XrReferenceSpaceCreateInfo { type = XrStructureType.XR_TYPE_REFERENCE_SPACE_CREATE_INFO, referenceSpaceType = XrReferenceSpaceType.XR_REFERENCE_SPACE_TYPE_STAGE, poseInReferenceSpace = new XrPosef { orientation = new XrQuaternionf { w = 1f } } };
                if (OpenXRAPI.xrCreateReferenceSpace(_xrSession, in refSpaceCreateInfo, out _appSpace) < 0)
                {
                    VRModCore.LogWarning("Failed to create STAGE space. Trying LOCAL.");
                    _appSpaceType = XrReferenceSpaceType.XR_REFERENCE_SPACE_TYPE_LOCAL;
                    refSpaceCreateInfo.referenceSpaceType = _appSpaceType;
                    OpenXRHelper.CheckResult(OpenXRAPI.xrCreateReferenceSpace(_xrSession, in refSpaceCreateInfo, out _appSpace), "xrCreateReferenceSpace_Fallback");
                }
                else
                {
                    _appSpaceType = XrReferenceSpaceType.XR_REFERENCE_SPACE_TYPE_STAGE;
                }
                if (_appSpace == OpenXRConstants.XR_NULL_HANDLE) throw new Exception("Failed to create any reference space.");
                VRModCore.Log($"Created {_appSpaceType} space.");

                InitializeViews();
                InitializeSwapchains();

                if (_viewConfigViews.Count > 0)
                {
                    _pProjectionLayerViews = Marshal.AllocHGlobal(Marshal.SizeOf<XrCompositionLayerProjectionView>() * _viewConfigViews.Count);
                    _projectionLayer = new XrCompositionLayerProjection { type = XrStructureType.XR_TYPE_COMPOSITION_LAYER_PROJECTION };
                    _pProjectionLayer = Marshal.AllocHGlobal(Marshal.SizeOf<XrCompositionLayerProjection>());
                    _pLayersForSubmit = Marshal.AllocHGlobal(Marshal.SizeOf<IntPtr>() * 1);
                    Marshal.WriteIntPtr(_pLayersForSubmit, _pProjectionLayer);
                }

                _flushCommandBuffer ??= new CommandBuffer
                    {
                        name = "VRModFlush"
                    };

                VRModCore.Log("OpenXR fully initialized.");
                IsVrAvailable = true;
                return true;
            }
            catch (Exception ex)
            {
                VRModCore.LogError("Exception during OpenXR initialization:", ex);
                TeardownVr();
                return false;
            }
        }

        private void InitializeViews()
        {
            OpenXRAPI.xrEnumerateViewConfigurations(_xrInstance, _xrSystemId, 0, out uint viewConfigCountOutput, IntPtr.Zero);
            if (viewConfigCountOutput == 0) throw new Exception("No view configurations available.");

            OpenXRAPI.xrEnumerateViewConfigurationViews(_xrInstance, _xrSystemId, _viewConfigType, 0, out uint viewCountOutput, IntPtr.Zero);
            if (viewCountOutput == 0) throw new Exception("No views for primary stereo config.");

            _viewConfigViews.Clear();
            IntPtr pViewStructs = Marshal.AllocHGlobal((int)(viewCountOutput * Marshal.SizeOf<XrViewConfigurationView>()));
            try
            {
                for (int i = 0; i < viewCountOutput; ++i) Marshal.StructureToPtr(new XrViewConfigurationView { type = XrStructureType.XR_TYPE_VIEW_CONFIGURATION_VIEW }, pViewStructs + (i * Marshal.SizeOf<XrViewConfigurationView>()), false);
                OpenXRAPI.xrEnumerateViewConfigurationViews(_xrInstance, _xrSystemId, _viewConfigType, viewCountOutput, out viewCountOutput, pViewStructs);
                for (int i = 0; i < viewCountOutput; ++i) _viewConfigViews.Add(Marshal.PtrToStructure<XrViewConfigurationView>(pViewStructs + (i * Marshal.SizeOf<XrViewConfigurationView>())));
            }
            finally { Marshal.FreeHGlobal(pViewStructs); }

            if (_viewConfigViews.Count > 0)
            {
                _locatedViews = new XrView[_viewConfigViews.Count];
                for (int i = 0; i < _locatedViews.Length; i++) _locatedViews[i].type = XrStructureType.XR_TYPE_VIEW;

                _projectionLayerViews = new XrCompositionLayerProjectionView[_viewConfigViews.Count];
                for (int i = 0; i < _projectionLayerViews.Length; i++) _projectionLayerViews[i].type = XrStructureType.XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW;
            }
            VRModCore.LogRuntimeDebug("View configurations enumerated.");
        }

        private void InitializeSwapchains()
        {
            OpenXRAPI.xrEnumerateSwapchainFormats(_xrSession, 0, out uint formatCount, null);
            if (formatCount == 0) throw new Exception("No swapchain formats.");
            long[] formatsArray = new long[formatCount];
            OpenXRAPI.xrEnumerateSwapchainFormats(_xrSession, formatCount, out formatCount, formatsArray);
            _supportedSwapchainFormats.Clear();
            _supportedSwapchainFormats.AddRange(formatsArray);

            long DXGI_FORMAT_B8G8R8A8_UNORM_SRGB = 91;
            if (_supportedSwapchainFormats.Contains(DXGI_FORMAT_B8G8R8A8_UNORM_SRGB))
                _selectedSwapchainFormat = DXGI_FORMAT_B8G8R8A8_UNORM_SRGB;
            else if (_supportedSwapchainFormats.Count > 0)
                _selectedSwapchainFormat = _supportedSwapchainFormats[0];
            else
                throw new Exception("No suitable swapchain format found.");

            VRModCore.Log($"Selected Swapchain Format (DXGI): {_selectedSwapchainFormat}");

            _eyeSwapchains.Clear();
            _eyeSwapchainImages.Clear();
            _eyeSwapchainSRVs.Clear();

            for (int i = 0; i < _viewConfigViews.Count; i++)
            {
                var view = _viewConfigViews[i];
                var swapchainCreateInfo = new XrSwapchainCreateInfo
                {
                    type = XrStructureType.XR_TYPE_SWAPCHAIN_CREATE_INFO,
                    usageFlags = XrSwapchainUsageFlags.XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT | XrSwapchainUsageFlags.XR_SWAPCHAIN_USAGE_SAMPLED_BIT,
                    format = _selectedSwapchainFormat,
                    sampleCount = view.recommendedSwapchainSampleCount,
                    width = view.recommendedImageRectWidth,
                    height = view.recommendedImageRectHeight,
                    faceCount = 1,
                    arraySize = 1,
                    mipCount = 1
                };
                OpenXRHelper.CheckResult(OpenXRAPI.xrCreateSwapchain(_xrSession, in swapchainCreateInfo, out ulong scHandle), "xrCreateSwapchain");
                _eyeSwapchains.Add(scHandle);

                OpenXRAPI.xrEnumerateSwapchainImages(scHandle, 0, out uint imgCount, IntPtr.Zero);
                IntPtr scImagesPtr = Marshal.AllocHGlobal((int)(imgCount * Marshal.SizeOf<XrSwapchainImageD3D11KHR>()));
                List<IntPtr> currentEyeTexList = [];
                List<IntPtr> currentEyeSrvList = [];
                try
                {
                    for (int j = 0; j < imgCount; ++j) Marshal.StructureToPtr(new XrSwapchainImageD3D11KHR { type = XrStructureType.XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR }, scImagesPtr + (j * Marshal.SizeOf<XrSwapchainImageD3D11KHR>()), false);
                    OpenXRAPI.xrEnumerateSwapchainImages(scHandle, imgCount, out imgCount, scImagesPtr);
                    for (int j = 0; j < imgCount; j++)
                    {
                        var swapchainImage = Marshal.PtrToStructure<XrSwapchainImageD3D11KHR>(scImagesPtr + (j * Marshal.SizeOf<XrSwapchainImageD3D11KHR>()));
                        currentEyeTexList.Add(swapchainImage.texture);
                        int hResultSrv = NativeBridge.CreateAndRegisterSRV_Internal(swapchainImage.texture, (int)_selectedSwapchainFormat, out IntPtr srvPtr);
                        if (hResultSrv == 0 && srvPtr != IntPtr.Zero)
                            currentEyeSrvList.Add(srvPtr);
                        else
                            currentEyeSrvList.Add(IntPtr.Zero);
                    }
                }
                finally { Marshal.FreeHGlobal(scImagesPtr); }

                _eyeSwapchainImages.Add(currentEyeTexList);
                _eyeSwapchainSRVs.Add(currentEyeSrvList);
                VRModCore.Log($"Swapchain for view {i} created. Images: {currentEyeTexList.Count}");
            }
        }

        public void UpdatePoses()
        {
            if (!IsVrAvailable || _xrSession == OpenXRConstants.XR_NULL_HANDLE) return;

            // --- RESTORED: Full event polling and session state management ---
            IntPtr eventBufferPtr = IntPtr.Zero;
            try
            {
                int eventBufferSize = XrEventDataBuffer.GetSize();
                eventBufferPtr = Marshal.AllocHGlobal(eventBufferSize);
                while (true)
                {
                    var headerForInput = new XrEventDataBaseHeader { type = XrStructureType.XR_TYPE_EVENT_DATA_BUFFER };
                    Marshal.StructureToPtr(headerForInput, eventBufferPtr, false);
                    XrResult pollResult = OpenXRAPI.xrPollEvent(_xrInstance, eventBufferPtr);
                    if (pollResult == XrResult.XR_EVENT_UNAVAILABLE) break;

                    if (pollResult < 0) { IsVrAvailable = false; break; }

                    var actualEventType = (XrStructureType)Marshal.ReadInt32(eventBufferPtr);
                    if (actualEventType == XrStructureType.XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED)
                    {
                        var stateEvent = Marshal.PtrToStructure<XrEventDataSessionStateChanged>(eventBufferPtr);
                        if (stateEvent.state != _currentSessionState)
                        {
                            VRModCore.Log($"OpenXR Session State Changed: {_currentSessionState} -> {stateEvent.state}");
                            _currentSessionState = stateEvent.state;

                            switch (_currentSessionState)
                            {
                                case XrSessionState.XR_SESSION_STATE_EXITING:
                                case XrSessionState.XR_SESSION_STATE_LOSS_PENDING:
                                    _isSessionRunning = false;
                                    IsVrAvailable = false;
                                    break;
                                case XrSessionState.XR_SESSION_STATE_STOPPING:
                                    if (_isSessionRunning)
                                    {
                                        VRModCore.Log("Session is stopping, calling xrEndSession.");
                                        OpenXRAPI.xrEndSession(_xrSession);
                                        _isSessionRunning = false;
                                    }
                                    break;
                                case XrSessionState.XR_SESSION_STATE_READY:
                                    if (!_isSessionRunning)
                                    {
                                        VRModCore.Log("Session is ready, calling xrBeginSession to resume.");
                                        var beginInfo = new XrSessionBeginInfo { type = XrStructureType.XR_TYPE_SESSION_BEGIN_INFO, primaryViewConfigurationType = _viewConfigType };
                                        if (OpenXRAPI.xrBeginSession(_xrSession, in beginInfo) == XrResult.XR_SUCCESS)
                                        {
                                            _isSessionRunning = true;
                                        }
                                    }
                                    break;
                            }
                        }
                    }
                }
            }
            finally { if (eventBufferPtr != IntPtr.Zero) Marshal.FreeHGlobal(eventBufferPtr); }

            if (!_isSessionRunning) return;

            var frameWaitInfo = new XrFrameWaitInfo { type = XrStructureType.XR_TYPE_FRAME_WAIT_INFO };
            _xrFrameState.type = XrStructureType.XR_TYPE_FRAME_STATE;
            OpenXRAPI.xrWaitFrame(_xrSession, in frameWaitInfo, out _xrFrameState);

            var frameBeginInfo = new XrFrameBeginInfo { type = XrStructureType.XR_TYPE_FRAME_BEGIN_INFO };
            if (OpenXRAPI.xrBeginFrame(_xrSession, in frameBeginInfo) == XrResult.XR_FRAME_DISCARDED)
            {
                var discardedFrameEndInfo = new XrFrameEndInfo { type = XrStructureType.XR_TYPE_FRAME_END_INFO, displayTime = _xrFrameState.predictedDisplayTime, layerCount = 0, layers = IntPtr.Zero, environmentBlendMode = XrEnvironmentBlendMode.XR_ENVIRONMENT_BLEND_MODE_OPAQUE };
                OpenXRAPI.xrEndFrame(_xrSession, in discardedFrameEndInfo);
                return;
            }

            var frameEndInfo = new XrFrameEndInfo { type = XrStructureType.XR_TYPE_FRAME_END_INFO, displayTime = _xrFrameState.predictedDisplayTime, environmentBlendMode = XrEnvironmentBlendMode.XR_ENVIRONMENT_BLEND_MODE_OPAQUE, layerCount = 0, layers = IntPtr.Zero };

            if (_xrFrameState.shouldRender == XrBool32.XR_TRUE)
            {
                var viewLocateInfo = new XrViewLocateInfo { type = XrStructureType.XR_TYPE_VIEW_LOCATE_INFO, viewConfigurationType = _viewConfigType, displayTime = _xrFrameState.predictedDisplayTime, space = _appSpace };
                _locatedViewState.type = XrStructureType.XR_TYPE_VIEW_STATE;

                IntPtr pLocatedViews = Marshal.AllocHGlobal(Marshal.SizeOf<XrView>() * _locatedViews.Length);
                try
                {
                    for (int i = 0; i < _locatedViews.Length; i++) Marshal.StructureToPtr(_locatedViews[i], pLocatedViews + (i * Marshal.SizeOf<XrView>()), false);
                    OpenXRAPI.xrLocateViews(_xrSession, in viewLocateInfo, out _locatedViewState, (uint)_locatedViews.Length, out _, pLocatedViews);
                    for (int i = 0; i < _locatedViews.Length; i++) _locatedViews[i] = Marshal.PtrToStructure<XrView>(pLocatedViews + (i * Marshal.SizeOf<XrView>()));
                }
                finally { Marshal.FreeHGlobal(pLocatedViews); }

                bool poseIsValid = (_locatedViewState.viewStateFlags & XrViewStateFlags.XR_VIEW_STATE_ORIENTATION_VALID_BIT) != 0;

                if (poseIsValid)
                {
                    var acquireInfo = new XrSwapchainImageAcquireInfo { type = XrStructureType.XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO };
                    OpenXRAPI.xrAcquireSwapchainImage(_eyeSwapchains[0], in acquireInfo, out uint acquiredIdx0);
                    OpenXRAPI.xrAcquireSwapchainImage(_eyeSwapchains[1], in acquireInfo, out uint acquiredIdx1);

                    var waitInfo = new XrSwapchainImageWaitInfo { type = XrStructureType.XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO, timeout = OpenXRConstants.XR_INFINITE_DURATION };
                    OpenXRAPI.xrWaitSwapchainImage(_eyeSwapchains[0], in waitInfo);
                    OpenXRAPI.xrWaitSwapchainImage(_eyeSwapchains[1], in waitInfo);

                    RenderEye(0, acquiredIdx0);
                    RenderEye(1, acquiredIdx1);

                    var releaseInfo = new XrSwapchainImageReleaseInfo { type = XrStructureType.XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO };
                    OpenXRAPI.xrReleaseSwapchainImage(_eyeSwapchains[0], in releaseInfo);
                    OpenXRAPI.xrReleaseSwapchainImage(_eyeSwapchains[1], in releaseInfo);

                    PopulateProjectionLayer();
                    frameEndInfo.layerCount = 1;
                    frameEndInfo.layers = _pLayersForSubmit;
                }
            }

            OpenXRAPI.xrEndFrame(_xrSession, in frameEndInfo);
        }

        private void RenderEye(int eyeIndex, uint swapchainImageIndex)
        {
            Camera currentEyeCamera = (eyeIndex == 0) ? _leftVrCamera : _rightVrCamera;
            RenderTexture currentIntermediateRT = (eyeIndex == 0) ? _leftEyeIntermediateRT : _rightEyeIntermediateRT;
            IntPtr nativeTextureResourcePtr = _eyeSwapchainImages[eyeIndex][(int)swapchainImageIndex];
            XrViewConfigurationView viewConfig = _viewConfigViews[eyeIndex];

            bool rtNeedsRecreation = (currentIntermediateRT == null || !currentIntermediateRT.IsCreated() || currentIntermediateRT.width != (int)viewConfig.recommendedImageRectWidth || currentIntermediateRT.height != (int)viewConfig.recommendedImageRectHeight);
            if (rtNeedsRecreation)
            {
                if (currentIntermediateRT != null) { currentIntermediateRT.Release(); UnityEngine.Object.Destroy(currentIntermediateRT); }

                var renderTextureFormat = QualitySettings.activeColorSpace == ColorSpace.Linear
                    ? GraphicsFormat.B8G8R8A8_SRGB
                    : GraphicsFormat.B8G8R8A8_UNorm;

                VRModCore.LogSpammyDebug($"Creating intermediate RenderTexture with format: {renderTextureFormat}");
                currentIntermediateRT = new RenderTexture((int)viewConfig.recommendedImageRectWidth, (int)viewConfig.recommendedImageRectHeight, 24, renderTextureFormat);

                if (eyeIndex == 0) _leftEyeIntermediateRT = currentIntermediateRT; else _rightEyeIntermediateRT = currentIntermediateRT;
            }

            currentEyeCamera.targetTexture = currentIntermediateRT;
            currentEyeCamera.clearFlags = _mainCameraClearFlags;
            if (_mainCameraClearFlags == CameraClearFlags.SolidColor) { currentEyeCamera.backgroundColor = _mainCameraBackgroundColor; }
            currentEyeCamera.cullingMask = _mainCameraCullingMask;
            currentEyeCamera.enabled = true;

            XrPosef eyePose = _locatedViews[eyeIndex].pose;
            Vector3 position = new(eyePose.position.x, eyePose.position.y, -eyePose.position.z);
            Quaternion rotation = new(eyePose.orientation.x, eyePose.orientation.y, -eyePose.orientation.z, -eyePose.orientation.w);
#if MONO
            currentEyeCamera.transform.localPosition = position;
            currentEyeCamera.transform.localRotation = rotation;
#elif CPP
            currentEyeCamera.transform.SetLocalPositionAndRotation(position, rotation);
#endif

            Matrix4x4 projM = CreateProjectionMatrixFromFovUsingFrustum(_locatedViews[eyeIndex].fov, currentEyeCamera.nearClipPlane, currentEyeCamera.farClipPlane);
            projM = Matrix4x4.Scale(new Vector3(1, -1, 1)) * projM;
            currentEyeCamera.projectionMatrix = projM;

            bool originalInvertCulling = GL.invertCulling;
            GL.invertCulling = true;
            currentEyeCamera.Render();
            GL.invertCulling = originalInvertCulling;
            Graphics.ExecuteCommandBuffer(_flushCommandBuffer);
            RenderTexture.active = null;

            if (currentIntermediateRT != null && currentIntermediateRT.IsCreated())
            {
                IntPtr sourceNativePtr = currentIntermediateRT.GetNativeTexturePtr();
                if (sourceNativePtr != IntPtr.Zero && nativeTextureResourcePtr != IntPtr.Zero)
                {
                    NativeBridge.DirectCopyResource_Internal(nativeTextureResourcePtr, sourceNativePtr);
                }
            }
        }

        private void PopulateProjectionLayer()
        {
            for (int i = 0; i < 2; i++)
            {
                _projectionLayerViews[i].pose = _locatedViews[i].pose;
                _projectionLayerViews[i].fov = _locatedViews[i].fov;
                _projectionLayerViews[i].subImage.swapchain = _eyeSwapchains[i];
                _projectionLayerViews[i].subImage.imageRect.offset = new XrOffset2Di { x = 0, y = 0 };
                _projectionLayerViews[i].subImage.imageRect.extent.width = (int)_viewConfigViews[i].recommendedImageRectWidth;
                _projectionLayerViews[i].subImage.imageRect.extent.height = (int)_viewConfigViews[i].recommendedImageRectHeight;
                _projectionLayerViews[i].subImage.imageArrayIndex = 0;
                Marshal.StructureToPtr(_projectionLayerViews[i], _pProjectionLayerViews + (i * Marshal.SizeOf<XrCompositionLayerProjectionView>()), false);
            }
            _projectionLayer.layerFlags = XrCompositionLayerFlags.None;
            _projectionLayer.space = _appSpace;
            _projectionLayer.viewCount = (uint)_viewConfigViews.Count;
            _projectionLayer.views = _pProjectionLayerViews;
            Marshal.StructureToPtr(_projectionLayer, _pProjectionLayer, false);
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

            _vrRig = new GameObject("UnityVRMod_XrRig");
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

            _mainCameraClearFlags = mainCamera.clearFlags;
            _mainCameraBackgroundColor = mainCamera.backgroundColor;
            _mainCameraCullingMask = mainCamera.cullingMask;

            _currentAppliedRigScale = 1.0f / Mathf.Max(0.01f, ConfigManager.VrWorldScale.Value);
            _vrRig.transform.localScale = new Vector3(_currentAppliedRigScale, _currentAppliedRigScale, _currentAppliedRigScale);

            _leftVrCameraGO = new GameObject("XrVrCamera_Left");
            _leftVrCameraGO.transform.SetParent(_vrRig.transform, false);
            _leftVrCamera = _leftVrCameraGO.AddComponent<Camera>();
            ConfigureVrCamera(_leftVrCamera, mainCamera, "Left");

            _rightVrCameraGO = new GameObject("XrVrCamera_Right");
            _rightVrCameraGO.transform.SetParent(_vrRig.transform, false);
            _rightVrCamera = _rightVrCameraGO.AddComponent<Camera>();
            ConfigureVrCamera(_rightVrCamera, mainCamera, "Right");

            _lastCalculatedVerticalOffset = 0f;
            UpdateVerticalOffset();

            VRModCore.Log("OpenXR: VR Camera Rig setup complete.");
        }

        private void ConfigureVrCamera(Camera vrCam, Camera mainCamRef, string eyeName)
        {
            VRModCore.LogRuntimeDebug($"Configuring VR Camera properties for: {eyeName}");
            vrCam.stereoTargetEye = StereoTargetEyeMask.None;
            vrCam.enabled = false;

            vrCam.clearFlags = _mainCameraClearFlags;
            vrCam.cullingMask = _mainCameraCullingMask;
            if (_mainCameraClearFlags == CameraClearFlags.SolidColor)
            {
                vrCam.backgroundColor = _mainCameraBackgroundColor;
            }

            float nearClipPlaneUserValue = ConfigManager.VrCameraNearClipPlane.Value;
            vrCam.nearClipPlane = Mathf.Max(0.001f, nearClipPlaneUserValue * _currentAppliedRigScale);

            float referenceFarClip = 1000f;
            if (mainCamRef != null)
            {
                referenceFarClip = mainCamRef.farClipPlane;
            }
            else if (_currentlyTrackedOriginalCameraGO != null)
            {
                if (_currentlyTrackedOriginalCameraGO.TryGetComponent<Camera>(out var trackedCam)) referenceFarClip = trackedCam.farClipPlane;
            }
            vrCam.farClipPlane = referenceFarClip * _currentAppliedRigScale;
        }

        public void TeardownCameraRig()
        {
            VRModCore.LogRuntimeDebug("Tearing down VR camera rig.");
            if (_leftEyeIntermediateRT != null) { _leftEyeIntermediateRT.Release(); UnityEngine.Object.Destroy(_leftEyeIntermediateRT); _leftEyeIntermediateRT = null; }
            if (_rightEyeIntermediateRT != null) { _rightEyeIntermediateRT.Release(); UnityEngine.Object.Destroy(_rightEyeIntermediateRT); _rightEyeIntermediateRT = null; }
            if (_vrRig != null) { UnityEngine.Object.Destroy(_vrRig); _vrRig = null; }
            _leftVrCameraGO = null; _leftVrCamera = null; _rightVrCameraGO = null; _rightVrCamera = null;
            _currentlyTrackedOriginalCameraGO = null;
        }

        public void TeardownVr()
        {
            VRModCore.LogRuntimeDebug("Tearing down OpenXR system.");
            TeardownCameraRig();

            if (_pProjectionLayerViews != IntPtr.Zero) { Marshal.FreeHGlobal(_pProjectionLayerViews); _pProjectionLayerViews = IntPtr.Zero; }
            if (_pProjectionLayer != IntPtr.Zero) { Marshal.FreeHGlobal(_pProjectionLayer); _pProjectionLayer = IntPtr.Zero; }
            if (_pLayersForSubmit != IntPtr.Zero) { Marshal.FreeHGlobal(_pLayersForSubmit); _pLayersForSubmit = IntPtr.Zero; }

            if (_appSpace != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroySpace != null)
                OpenXRAPI.xrDestroySpace(_appSpace);

            if (_eyeSwapchainSRVs != null)
            {
                foreach (var srvList in _eyeSwapchainSRVs)
                    foreach (var srv in srvList)
                        if (srv != IntPtr.Zero) NativeBridge.ReleaseNativeObject_Internal(srv);
            }

            if (_eyeSwapchains.Count > 0)
            {
                foreach (ulong sc in _eyeSwapchains)
                    if (sc != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroySwapchain != null)
                        OpenXRAPI.xrDestroySwapchain(sc);
            }

            if (_xrSession != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroySession != null)
                OpenXRAPI.xrDestroySession(_xrSession);

            if (_xrInstance != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroyInstance != null)
                OpenXRAPI.xrDestroyInstance(_xrInstance);

            _xrInstance = OpenXRConstants.XR_NULL_HANDLE;
            _xrSession = OpenXRConstants.XR_NULL_HANDLE;
            _appSpace = OpenXRConstants.XR_NULL_HANDLE;

            OpenXRNativeLoader.FreeOpenXRLibrary();
            IsVrAvailable = false;
        }

        private static Matrix4x4 CreateProjectionMatrixFromFovUsingFrustum(XrFovf fov, float nearClip, float farClip)
        {
            float nPR = Mathf.Tan(fov.angleRight) * nearClip;
            float nPL = Mathf.Tan(fov.angleLeft) * nearClip;
            float nPT = Mathf.Tan(fov.angleUp) * nearClip;
            float nPB = Mathf.Tan(fov.angleDown) * nearClip;

            Matrix4x4 m = new();
            m[0, 0] = (2.0f * nearClip) / (nPR - nPL);
            m[0, 2] = (nPR + nPL) / (nPR - nPL);
            m[1, 1] = (2.0f * nearClip) / (nPT - nPB);
            m[1, 2] = (nPT + nPB) / (nPT - nPB);
            m[2, 2] = -(farClip + nearClip) / (farClip - nearClip);
            m[2, 3] = -(2.0f * farClip * nearClip) / (farClip - nearClip);
            m[3, 2] = -1.0f;
            return m;
        }

        public void SetCameraNearClip(float newNearClipBaseValue)
        {
            if (_vrRig == null) return;
            Camera mainCamera = null;
            if (_currentlyTrackedOriginalCameraGO != null)
                mainCamera = _currentlyTrackedOriginalCameraGO.GetComponent<Camera>();

            if (_leftVrCamera != null) ConfigureVrCamera(_leftVrCamera, mainCamera, "Left");
            if (_rightVrCamera != null) ConfigureVrCamera(_rightVrCamera, mainCamera, "Right");
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

        public VrCameraRig GetVrCameraGameObjects()
        {
            return new VrCameraRig { LeftEye = _leftVrCameraGO, RightEye = _rightVrCameraGO };
        }
    }
}