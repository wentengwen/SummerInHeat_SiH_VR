using UnityVRMod.Config;
using UnityVRMod.Core;
using UnityVRMod.Features.Util;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

#if CPP
using Il2CppInterop.Runtime;
#endif

namespace UnityVRMod.Features.VrVisualization
{
    internal class VrVisualizationManager
    {
        private IVrCameraSetup _cameraSetup;
        private bool _managerInitialized = false;
        private GameObject _currentlyTrackedOriginalCameraGO = null;

        private bool _isUserSafeModeActive = true;
        private float _autoSafeModeEndTime = -1f;

        private UnityAction<Scene, Scene> _sceneChangedActionDelegate;

        private bool _hasVrBeenAttemptedByUser = false;
        internal bool IsVrReady => _hasVrBeenAttemptedByUser && _cameraSetup != null && _cameraSetup.IsVrAvailable;

        internal Camera VrCameraForUIParenting
        {
            get
            {
                if (!IsVrReady) return null;
                VrCameraRig rig = _cameraSetup.GetVrCameraGameObjects();
                if (rig.LeftEye != null) return rig.LeftEye.GetComponent<Camera>();
                if (rig.RightEye != null) return rig.RightEye.GetComponent<Camera>();
                return null;
            }
        }

        internal void Initialize()
        {
            if (_managerInitialized) return;
            _managerInitialized = true;

            if (!ConfigManager.EnableVrInjection.Value)
            {
                VRModCore.Log("VR Visualization feature is disabled by config.");
                return;
            }

            _isUserSafeModeActive = ConfigManager.SafeModeStartsActive.Value;
            VRModCore.Log($"Initial user safe mode active: {_isUserSafeModeActive}. VR init will be delayed until user first deactivates Safe Mode.");

            if (ConfigManager.EnableAutomaticSafeMode.Value)
            {
                VRModCore.LogRuntimeDebug("Automatic safe mode enabled, subscribing to scene changes.");
#if CPP
                Action<Scene, Scene> csAction = OnActiveSceneChanged;
                _sceneChangedActionDelegate = DelegateSupport.ConvertDelegate<UnityAction<Scene, Scene>>(csAction);
                if (_sceneChangedActionDelegate != null) SceneManager.activeSceneChanged += _sceneChangedActionDelegate;
                else VRModCore.LogError("(IL2CPP) Failed to convert scene change delegate.");
#else
                _sceneChangedActionDelegate = OnActiveSceneChanged;
                SceneManager.activeSceneChanged += _sceneChangedActionDelegate;
#endif
            }
            VRModCore.Log("VrVisualizationManager initialized.");
        }

        private bool EnsureAndInitializeVrSubsystem()
        {
            if (_cameraSetup != null && _cameraSetup.IsVrAvailable)
            {
                VRModCore.LogRuntimeDebug("VR subsystem already initialized and available.");
                return true;
            }

            VRModCore.LogRuntimeDebug("Attempting to initialize VR subsystem...");

            string cameraSetupTypeNameFull;
#if OPENVR_BUILD
            cameraSetupTypeNameFull = "UnityVRMod.Features.VrVisualization.VrCameraSetup_CoreOpenVR";
#elif OPENXR_BUILD
            cameraSetupTypeNameFull = "UnityVRMod.Features.VrVisualization.VrCameraSetup_CoreOpenXR";
#else
            VRModCore.LogError("Critical Error! No VR backend build symbol (OPENVR_BUILD or OPENXR_BUILD) defined!");
            return false;
#endif

            try
            {
                Type setupType = Type.GetType(cameraSetupTypeNameFull);
                if (setupType == null || !typeof(IVrCameraSetup).IsAssignableFrom(setupType))
                {
                    VRModCore.LogError($"Type '{cameraSetupTypeNameFull}' not found or invalid.");
                    return false;
                }
                _cameraSetup = Activator.CreateInstance(setupType) as IVrCameraSetup;
                if (_cameraSetup == null)
                {
                    VRModCore.LogError($"Failed to create instance of '{cameraSetupTypeNameFull}'.");
                    return false;
                }

                VRModCore.Log($"Loaded {_cameraSetup.GetType().Name}. Initializing VR subsystem...");
                if (!_cameraSetup.InitializeVr(ConfigManager.VrApplicationKey.Value))
                {
                    VRModCore.LogWarning("VR subsystem initialization failed.");
                    _cameraSetup = null;
                    return false;
                }

                VRModCore.LogRuntimeDebug("VR subsystem initialization successful.");
                _hasVrBeenAttemptedByUser = true;
                                
                return true;
            }
            catch (Exception ex)
            {
                VRModCore.LogError("Exception during VR subsystem instantiation or initialization:", ex);
                _cameraSetup = null;
                return false;
            }
        }

        private void OnActiveSceneChanged(Scene current, Scene next)
        {
            VRModCore.LogRuntimeDebug($"Scene changed from '{current.name}' to '{next.name}'.");
            CameraFinder.InvalidateCache(); // Invalidate cache on scene change.
            if (!IsVrReady) return;

            if (ConfigManager.EnableAutomaticSafeMode.Value)
            {
                ActivateAutomaticSafeMode($"Scene changed to '{next.name}'");
            }
        }

        private void ActivateAutomaticSafeMode(string reason)
        {
            if (!IsVrReady || !ConfigManager.EnableAutomaticSafeMode.Value) return;

            float duration = ConfigManager.AutomaticSafeModeDurationSecs.Value;
            _autoSafeModeEndTime = Time.time + duration;
            VRModCore.Log($"Automatic Safe Mode ENGAGED for {duration:F1}s. Reason: {reason}.");
        }

        public void ToggleUserSafeMode()
        {
            _isUserSafeModeActive = !_isUserSafeModeActive;
            VRModCore.Log($"User Safe Mode Toggled. Now: {(_isUserSafeModeActive ? "ACTIVE (Rendering OFF)" : "INACTIVE (Rendering ON)")}");

            if (_isUserSafeModeActive)
            {
                if (_cameraSetup != null)
                {
                    var level = ConfigManager.ActiveSafeModeLevel.Value;
                    VRModCore.LogRuntimeDebug($"Entering Safe Mode with level: {level}");

                    if (level == SafeModeLevel.FullVrReinitOnToggle)
                    {
                        VRModCore.Log("Tearing down full VR subsystem for re-initialization.");
                        _cameraSetup.TeardownVr();
                        _cameraSetup = null;
                        _hasVrBeenAttemptedByUser = false;
                        CameraFinder.InvalidateCache();
                    }
                    else if (level == SafeModeLevel.RigReinitOnToggle)
                    {
                        VRModCore.Log("Tearing down VR camera rig for re-initialization.");
                        _cameraSetup.TeardownCameraRig();
                        _currentlyTrackedOriginalCameraGO = null;
                        CameraFinder.InvalidateCache();
                    }
                }
            }
            else
            {
                _autoSafeModeEndTime = -1f;
                if (!_hasVrBeenAttemptedByUser)
                {
                    VRModCore.LogRuntimeDebug("First time user is disabling Safe Mode. Attempting to initialize VR subsystem...");
                    if (!EnsureAndInitializeVrSubsystem())
                    {
                        VRModCore.LogError("Failed to initialize VR subsystem. Re-enabling Safe Mode.");
                        _isUserSafeModeActive = true;
                        _hasVrBeenAttemptedByUser = false;
                    }
                }
            }
        }

        internal void Update()
        {
            if (_cameraSetup == null && _hasVrBeenAttemptedByUser)
            {
                if (!_isUserSafeModeActive)
                {
                    VRModCore.LogRuntimeDebug("VR Subsystem is fully torn down. Re-initializing due to user exiting safe mode.");
                    if (!EnsureAndInitializeVrSubsystem())
                    {
                        VRModCore.LogError("Failed to re-initialize VR subsystem. Re-enabling Safe Mode.");
                        _isUserSafeModeActive = true;
                    }
                }
                return;
            }

            if (!_hasVrBeenAttemptedByUser || _cameraSetup == null || !_cameraSetup.IsVrAvailable) return;

            bool autoSafeModeEngaged = Time.time < _autoSafeModeEndTime;
            bool shouldRender = !_isUserSafeModeActive && !autoSafeModeEngaged;

            VrCameraRig vrCameras = _cameraSetup.GetVrCameraGameObjects();
            bool rigIsSetUp = vrCameras.LeftEye != null || vrCameras.RightEye != null;

            Camera mainCam = CameraFinder.FindGameCamera();

            if (mainCam != null)
            {
                if (rigIsSetUp)
                {
                    if (_currentlyTrackedOriginalCameraGO != mainCam.gameObject)
                    {
                        VRModCore.Log($"Game's main camera changed to '{mainCam.name}'. Re-creating VR rig.");
                        _cameraSetup.TeardownCameraRig();
                        CameraFinder.InvalidateCache(); // Invalidate since we are about to re-setup
                        _cameraSetup.SetupCameraRig(mainCam);
                        _currentlyTrackedOriginalCameraGO = mainCam.gameObject;
                        if (ConfigManager.EnableAutomaticSafeMode.Value)
                            ActivateAutomaticSafeMode("Main camera changed");
                    }
                }
                else if (shouldRender)
                {
                    VRModCore.Log($"Found game camera '{mainCam.name}'. Setting up VR rig.");
                    _cameraSetup.SetupCameraRig(mainCam);
                    _currentlyTrackedOriginalCameraGO = mainCam.gameObject;
                    if (ConfigManager.EnableAutomaticSafeMode.Value)
                        ActivateAutomaticSafeMode("Initial rig setup");
                }
            }
            else if (rigIsSetUp)
            {
                VRModCore.LogWarning("Game's main camera has become null. Tearing down VR rig to prevent conflicts.");
                _cameraSetup.TeardownCameraRig();
                _currentlyTrackedOriginalCameraGO = null;
                CameraFinder.InvalidateCache();
            }

            if (shouldRender && rigIsSetUp)
            {
                _cameraSetup.UpdatePoses();
            }
        }

        internal void Shutdown()
        {
            VRModCore.LogRuntimeDebug("Shutdown called.");
            if (ConfigManager.EnableAutomaticSafeMode.Value && _sceneChangedActionDelegate != null)
            {
                SceneManager.activeSceneChanged -= _sceneChangedActionDelegate;
            }

            if (_cameraSetup != null)
            {
                VRModCore.LogRuntimeDebug($"Shutting down camera setup: {_cameraSetup.GetType().Name}.");
                _cameraSetup.TeardownVr();
            }
            _managerInitialized = false;
        }

        public void LiveUpdateWorldScale(float newScale)
        {
            if (IsVrReady)
            {
                Camera cameraComponent = _currentlyTrackedOriginalCameraGO != null
                    ? _currentlyTrackedOriginalCameraGO.GetComponent<Camera>()
                    : null;
                _cameraSetup.SetWorldScale(newScale, cameraComponent);
            }
        }

        public void LiveUpdateCameraNearClip(float newNearClip)
        {
            if (IsVrReady) _cameraSetup.SetCameraNearClip(newNearClip);
        }

        public void LiveUpdateUserEyeHeightOffset(float newOffset)
        {
            if (IsVrReady) _cameraSetup.SetUserEyeHeightOffset(newOffset);
        }
    }
}