#if OPENVR_BUILD
using System.Runtime.InteropServices;
using UnityVRMod.Config;
using UnityVRMod.Core;
using UnityVRMod.Features.VRVisualization.OpenVR;

namespace UnityVRMod.Features.VrVisualization
{
    internal sealed class OpenVrUiInteractor
    {
        private const float TriggerPressThreshold = 0.70f;
        private const float TriggerReleaseThreshold = 0.30f;
        private const float PointerFallbackDistanceMeters = 4.0f;
        private const float UiSurfaceMaxRayDistanceMeters = 8.0f;
        private const float UiSurfaceBoundsEpsilon = 0.001f;
        private const float UiSurfaceLookupIntervalSeconds = 2.0f;
        private const float RayWidthMeters = 0.0018f;
        private const float CursorScaleMeters = 0.015f;
        private const float MinPanelScale = 0.35f;
        private const float MaxPanelScale = 3.0f;
        private const float NguiRaycastDistanceMeters = 200f;
        private const float NguiCameraRefreshIntervalSeconds = 1.0f;
        private const float NguiDistanceTieEpsilonMeters = 0.0025f;
        private const float NguiDistanceScoreWindowMeters = 0.015f;
        private const int NguiParentProbeDepth = 6;
        private const string PreferredButtonTag = "PushBt";
        private static readonly bool NguiDebugRaycastEnabled = true;
        private static readonly bool NguiDebugEventProbeEnabled = true;
        private const float NguiDebugLogIntervalSeconds = 0.20f;
        private const int NguiDebugMaxCandidates = 6;
        private const float HoldPressRepeatIntervalSeconds = 0.05f;
        private const ushort VirtualKeySpace = 0x20;
        private const uint InputTypeMouse = 0;
        private const uint InputTypeKeyboard = 1;
        private const uint MouseEventFlagLeftDown = 0x0002;
        private const uint MouseEventFlagLeftUp = 0x0004;
        private const uint KeyEventFlagKeyUp = 0x0002;
        private const string UiSurfaceName = "UnityVRMod_UIProjectionPlane";
        private const string RayObjectName = "OpenVR_UIRay";
        private const string CursorObjectName = "OpenVR_UICursor";
        private static readonly bool IsWindowsPlatform = Environment.OSVersion.Platform == PlatformID.Win32NT;

        private static readonly Color RayVisibleColor = new(1.00f, 1.00f, 1.00f, 0.95f);

        private Camera _mainCamera;
        private GameObject _uiSurface;
        private float _nextUiSurfaceLookupTime;
        private bool _loggedUiSurfaceFallback;

        private bool _wasTriggerPressed;
        private bool _pointerIsDown;
        private GameObject _pressedTarget;
        private GameObject _currentNguiHoverTarget;
        private float _nextHoldPressRepeatTime;
        private bool _isSpaceMappedPressed;
        private bool _lastSpaceMapButtonPressed;
        private bool _loggedSpaceMappingUnsupportedPlatform;
        private bool _loggedMouseInjectionUnsupportedPlatform;
        private bool _loggedMouseInjectionWindowMissing;
        private bool _lastUiInteractionUsedMouseInjection;

        private readonly List<Camera> _nguiCameras = [];
        private float _nextNguiCameraRefreshTime;
        private bool _loggedMissingNguiCamera;
        private bool _nguiBindingsResolved;
        private bool _nguiBindingsFailed;
        private Type _nguiUiCameraType;
        private MethodInfo _physicsRaycastAllMethod;
        private FieldInfo _raycastHitDistanceField;
        private PropertyInfo _raycastHitDistanceProperty;
        private FieldInfo _raycastHitColliderField;
        private PropertyInfo _raycastHitColliderProperty;
        private float _nextNguiDebugLogTime;
        private string _lastNguiDebugSignature;
        private Camera _lastNguiDebugCamera;
        private readonly List<NguiHitCandidate> _lastNguiDebugCandidates = [];
        private GameObject _lastNguiDebugTarget;
        private int _lastNguiDebugTargetScore;
        private Vector2 _lastNguiDebugPointer;
        private readonly HashSet<int> _eventProbeLoggedTargets = [];

        private GameObject _rayObject;
        private LineRenderer _rayLine;
        private Material _rayMaterial;
        private GameObject _cursorObject;
        private Renderer _cursorRenderer;
        private Material _cursorMaterial;
        private float _lastAppliedCursorScale;

        public void Initialize(Camera mainCamera)
        {
            ReleaseMappedSpaceKeyIfNeeded();

            _mainCamera = mainCamera;
            _uiSurface = null;
            _nextUiSurfaceLookupTime = 0f;
            _loggedUiSurfaceFallback = false;

            _wasTriggerPressed = false;
            _pointerIsDown = false;
            _pressedTarget = null;
            _currentNguiHoverTarget = null;
            _nextHoldPressRepeatTime = 0f;
            _isSpaceMappedPressed = false;
            _lastSpaceMapButtonPressed = false;
            _loggedSpaceMappingUnsupportedPlatform = false;
            _loggedMouseInjectionUnsupportedPlatform = false;
            _loggedMouseInjectionWindowMissing = false;
            _lastUiInteractionUsedMouseInjection = false;
            _nextNguiCameraRefreshTime = 0f;
            _loggedMissingNguiCamera = false;
            _nextNguiDebugLogTime = 0f;
            _lastNguiDebugSignature = null;
            _lastNguiDebugCamera = null;
            _lastNguiDebugTarget = null;
            _lastNguiDebugTargetScore = 0;
            _lastNguiDebugCandidates.Clear();
            _lastNguiDebugPointer = default;
            _lastAppliedCursorScale = -1f;
            _nextHoldPressRepeatTime = 0f;
            _eventProbeLoggedTargets.Clear();

            DestroyRayVisuals();
        }

        public void Teardown()
        {
            ReleaseMappedSpaceKeyIfNeeded();
            ReleasePressedIfAny();
            ClearNguiHoverTarget();

            _mainCamera = null;
            _uiSurface = null;
            _loggedUiSurfaceFallback = false;
            _loggedMissingNguiCamera = false;
            _lastNguiDebugSignature = null;
            _lastNguiDebugCamera = null;
            _lastNguiDebugTarget = null;
            _lastNguiDebugTargetScore = 0;
            _lastNguiDebugCandidates.Clear();
            _lastNguiDebugPointer = default;
            _lastAppliedCursorScale = -1f;
            _eventProbeLoggedTargets.Clear();
            _isSpaceMappedPressed = false;
            _lastSpaceMapButtonPressed = false;
            _loggedSpaceMappingUnsupportedPlatform = false;
            _loggedMouseInjectionUnsupportedPlatform = false;
            _loggedMouseInjectionWindowMissing = false;
            _lastUiInteractionUsedMouseInjection = false;

            DestroyRayVisuals();
        }

        public void Update(CVRSystem hmd, GameObject vrRig, TrackedDevicePose_t[] trackedPoses)
        {
            if (hmd == null || vrRig == null || trackedPoses == null) return;
            EnsureRayVisuals(vrRig);

            if (!TryReadRightControllerState(hmd, out uint rightDeviceIndex, out VRControllerState_t rightState))
            {
                UpdateRightMenuSpaceMapping(false);
                _wasTriggerPressed = false;
                ReleasePressedIfAny();
                ClearNguiHoverTarget();
                SetRayVisible(false);
                return;
            }

            bool rightMenuPressed = IsButtonPressed(rightState.ulButtonPressed, EVRButtonId.k_EButton_ApplicationMenu);
            UpdateRightMenuSpaceMapping(rightMenuPressed);
            bool useMouseInjection = ConfigManager.OpenVR_UseMouseInjectionForUi?.Value ?? false;
            if (useMouseInjection != _lastUiInteractionUsedMouseInjection)
            {
                if (_lastUiInteractionUsedMouseInjection)
                {
                    if (_pointerIsDown && IsWindowsPlatform)
                    {
                        _ = TrySendMouseLeftEvent(false, out _);
                    }
                }
                else
                {
                    if (_pointerIsDown && _pressedTarget != null)
                    {
                        SendNguiPress(_pressedTarget, false);
                    }
                }

                _pointerIsDown = false;
                _pressedTarget = null;
                _nextHoldPressRepeatTime = 0f;
                ClearNguiHoverTarget();
                _lastUiInteractionUsedMouseInjection = useMouseInjection;
            }

            if (!TryGetControllerRay(vrRig, trackedPoses, rightDeviceIndex, out Vector3 rayOrigin, out Vector3 rayDirection))
            {
                HandleNoPointerFrame(triggerPressedNow: false);
                SetRayVisible(false);
                _wasTriggerPressed = false;
                return;
            }

            bool triggerPressed = IsTriggerPressed(rightState);
            if (!TryGetPointerScreenPosition(rayOrigin, rayDirection, out Vector2 pointerScreenPos, out Vector3 rayEndPoint))
            {
                HandleNoPointerFrame(triggerPressed);
                UpdateRayVisual(rayOrigin, rayEndPoint, hasTarget: false);
                _wasTriggerPressed = triggerPressed;
                return;
            }

            if (useMouseInjection)
            {
                ClearNguiHoverTarget();
                bool justPressedMouse = triggerPressed && !_wasTriggerPressed;
                bool justReleasedMouse = !triggerPressed && _wasTriggerPressed;
                bool pointerInjected = TryInjectMousePointerPosition(pointerScreenPos);

                if (justPressedMouse)
                {
                    if (TrySendMouseLeftEvent(true, out int downError))
                    {
                        _pointerIsDown = true;
                    }
                    else
                    {
                        VRModCore.LogWarning($"[UI][Mouse] Failed to inject LeftDown (Win32Error={downError}).");
                    }
                }

                if (_pointerIsDown && justReleasedMouse)
                {
                    if (!TrySendMouseLeftEvent(false, out int upError))
                    {
                        VRModCore.LogWarning($"[UI][Mouse] Failed to inject LeftUp (Win32Error={upError}).");
                    }

                    _pointerIsDown = false;
                }

                UpdateRayVisual(rayOrigin, rayEndPoint, hasTarget: pointerInjected);
                _wasTriggerPressed = triggerPressed;
                return;
            }

            GameObject nguiTarget = TryRaycastNguiTarget(pointerScreenPos);
            bool justPressed = triggerPressed && !_wasTriggerPressed;
            bool justReleased = !triggerPressed && _wasTriggerPressed;

            UpdateNguiHoverTarget(nguiTarget);

            if (justPressed && nguiTarget != null)
            {
                SendNguiPress(nguiTarget, true);
                _pressedTarget = nguiTarget;
                _pointerIsDown = true;
                _nextHoldPressRepeatTime = Time.time + HoldPressRepeatIntervalSeconds;
                LogNguiEventProbeIfNeeded(nguiTarget);
            }

            if (justPressed)
            {
                LogNguiDebugSnapshot("press");
            }

            if (_pointerIsDown && triggerPressed && _pressedTarget != null && Time.time >= _nextHoldPressRepeatTime)
            {
                // Keep-alive press for NGUI controls that poll/refresh hold state during long press.
                SendNguiPress(_pressedTarget, true);
                _nextHoldPressRepeatTime = Time.time + HoldPressRepeatIntervalSeconds;
            }

            if (_pointerIsDown && justReleased)
            {
                GameObject releaseTarget = _pressedTarget ?? nguiTarget;
                if (releaseTarget != null) SendNguiPress(releaseTarget, false);
                if (_pressedTarget != null && _pressedTarget == nguiTarget) SendNguiClick(_pressedTarget);

                _pointerIsDown = false;
                _pressedTarget = null;
                _nextHoldPressRepeatTime = 0f;
            }

            UpdateRayVisual(rayOrigin, rayEndPoint, hasTarget: nguiTarget != null);
            _wasTriggerPressed = triggerPressed;
        }

        private void HandleNoPointerFrame(bool triggerPressedNow)
        {
            if (_pointerIsDown && !triggerPressedNow)
            {
                ReleasePressedIfAny();
            }

            ClearNguiHoverTarget();
        }

        private bool TryGetPointerScreenPosition(Vector3 rayOrigin, Vector3 rayDirection, out Vector2 pointerScreenPos, out Vector3 rayEndPoint)
        {
            pointerScreenPos = default;
            rayEndPoint = rayOrigin + (rayDirection * PointerFallbackDistanceMeters);

            bool constrainToUiSurface;
            Vector3 uiHitPoint;
            if (TryGetUiSurfaceScreenPosition(rayOrigin, rayDirection, out pointerScreenPos, out uiHitPoint, out constrainToUiSurface))
            {
                rayEndPoint = uiHitPoint;
                return true;
            }

            // If the projection plane exists, interaction is constrained to that plane.
            if (constrainToUiSurface)
            {
                rayEndPoint = uiHitPoint;
                return false;
            }

            if (_uiSurface != null)
            {
                return false;
            }

            if (_mainCamera == null) return false;

            Vector3 fallbackPointWorld = rayOrigin + rayDirection * PointerFallbackDistanceMeters;
            rayEndPoint = fallbackPointWorld;
            Vector3 screenPoint = _mainCamera.WorldToScreenPoint(fallbackPointWorld);
            if (screenPoint.z <= 0f) return false;

            pointerScreenPos = new Vector2(
                Mathf.Clamp(screenPoint.x, 0f, Screen.width),
                Mathf.Clamp(screenPoint.y, 0f, Screen.height));

            if (!_loggedUiSurfaceFallback)
            {
                _loggedUiSurfaceFallback = true;
                VRModCore.LogWarning($"[UI] '{UiSurfaceName}' not found. Using main-camera screen fallback for NGUI ray mapping.");
            }

            return true;
        }

        private bool TryGetUiSurfaceScreenPosition(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            out Vector2 screenPos,
            out Vector3 hitWorldPoint,
            out bool constrainToUiSurface)
        {
            screenPos = default;
            hitWorldPoint = rayOrigin + (rayDirection * PointerFallbackDistanceMeters);
            constrainToUiSurface = false;

            RefreshUiSurfaceReferenceIfNeeded();
            if (_uiSurface == null || !_uiSurface.activeInHierarchy) return false;
            constrainToUiSurface = true;

            Plane surfacePlane = new(_uiSurface.transform.forward, _uiSurface.transform.position);
            if (!surfacePlane.Raycast(new Ray(rayOrigin, rayDirection), out float enterDistance) || enterDistance <= 0f)
            {
                return false;
            }

            if (enterDistance > UiSurfaceMaxRayDistanceMeters)
            {
                // Avoid huge "infinite" ray extension when the ray is nearly parallel to the panel plane.
                return false;
            }

            hitWorldPoint = rayOrigin + (rayDirection * enterDistance);

            if (!TryGetProjectionPlaneUv(_uiSurface, hitWorldPoint, out Vector2 uv))
            {
                return false;
            }

            Rect mappingRect = GetPrimaryNguiScreenRect();
            screenPos = new Vector2(
                mappingRect.xMin + (uv.x * mappingRect.width),
                mappingRect.yMin + (uv.y * mappingRect.height));
            return true;
        }

        private void RefreshUiSurfaceReferenceIfNeeded()
        {
            if (Time.time < _nextUiSurfaceLookupTime) return;

            _nextUiSurfaceLookupTime = Time.time + UiSurfaceLookupIntervalSeconds;
            _uiSurface = GameObject.Find(UiSurfaceName);
            if (_uiSurface != null)
            {
                _loggedUiSurfaceFallback = false;
            }
        }

        private GameObject TryRaycastNguiTarget(Vector2 pointerScreenPos)
        {
            if (!EnsureNguiBindings()) return null;
            RefreshNguiCameras();

            float closestDistance = float.MaxValue;
            int bestScore = int.MinValue;
            float bestDistance = float.MaxValue;
            GameObject bestTarget = null;
            Camera bestCamera = null;
            List<NguiHitCandidate> bestCandidates = null;
            foreach (Camera nguiCamera in _nguiCameras)
            {
                if (nguiCamera == null || !nguiCamera.enabled || !nguiCamera.gameObject.activeInHierarchy) continue;

                if (!TryRaycastNguiCamera(nguiCamera, pointerScreenPos, out GameObject target, out float distance, out int score, out List<NguiHitCandidate> candidates)) continue;
                if (target == null) continue;

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                }

                float windowDistance = closestDistance + NguiDistanceScoreWindowMeters;
                bool insideScoreWindow = distance <= windowDistance;
                if (!insideScoreWindow) continue;

                bool isBetterScore = score > bestScore;
                bool isTieButCloser = score == bestScore && distance < bestDistance - NguiDistanceTieEpsilonMeters;
                if (isBetterScore || isTieButCloser)
                {
                    bestDistance = distance;
                    bestScore = score;
                    bestTarget = target;
                    bestCamera = nguiCamera;
                    bestCandidates = candidates;
                }
            }

            CaptureNguiDebugSnapshot(pointerScreenPos, bestCamera, bestTarget, bestScore, bestCandidates);
            return bestTarget;
        }

        private bool EnsureNguiBindings()
        {
            if (_nguiBindingsResolved) return true;
            if (_nguiBindingsFailed) return false;

            _nguiUiCameraType = ResolveTypeAnyAssembly("UICamera");
            if (_nguiUiCameraType == null)
            {
                _nguiBindingsFailed = true;
                VRModCore.LogWarning("[UI][NGUI] UICamera type not found. NGUI interaction disabled.");
                return false;
            }

            Type physicsType = ResolveTypeAnyAssembly("UnityEngine.Physics");
            if (physicsType == null)
            {
                _nguiBindingsFailed = true;
                VRModCore.LogWarning("[UI][NGUI] UnityEngine.Physics type not found. NGUI interaction disabled.");
                return false;
            }

            _physicsRaycastAllMethod = physicsType.GetMethod("RaycastAll", BindingFlags.Public | BindingFlags.Static, null, [ typeof(Ray), typeof(float), typeof(int) ], null)
                ?? physicsType.GetMethod("RaycastAll", BindingFlags.Public | BindingFlags.Static, null, [ typeof(Ray), typeof(float) ], null)
                ?? physicsType.GetMethod("RaycastAll", BindingFlags.Public | BindingFlags.Static, null, [ typeof(Ray) ], null);

            if (_physicsRaycastAllMethod == null)
            {
                _nguiBindingsFailed = true;
                VRModCore.LogWarning("[UI][NGUI] Physics.RaycastAll overload not found. NGUI interaction disabled.");
                return false;
            }

            Type raycastHitType = ResolveTypeAnyAssembly("UnityEngine.RaycastHit");
            if (raycastHitType != null)
            {
                _raycastHitDistanceField = raycastHitType.GetField("distance", BindingFlags.Public | BindingFlags.Instance);
                _raycastHitDistanceProperty = raycastHitType.GetProperty("distance", BindingFlags.Public | BindingFlags.Instance);
                _raycastHitColliderField = raycastHitType.GetField("collider", BindingFlags.Public | BindingFlags.Instance);
                _raycastHitColliderProperty = raycastHitType.GetProperty("collider", BindingFlags.Public | BindingFlags.Instance);
            }

            _nguiBindingsResolved = true;
            return true;
        }

        private void RefreshNguiCameras()
        {
            if (Time.time < _nextNguiCameraRefreshTime) return;
            _nextNguiCameraRefreshTime = Time.time + NguiCameraRefreshIntervalSeconds;

            _nguiCameras.Clear();
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam == null) continue;
                if (cam.GetComponent(_nguiUiCameraType) == null) continue;
                _nguiCameras.Add(cam);
            }

            if (_nguiCameras.Count == 0)
            {
                if (!_loggedMissingNguiCamera)
                {
                    _loggedMissingNguiCamera = true;
                    VRModCore.LogWarning("[UI][NGUI] No camera with UICamera component found.");
                }
            }
            else
            {
                _loggedMissingNguiCamera = false;
            }
        }

        private bool TryRaycastNguiCamera(
            Camera nguiCamera,
            Vector2 pointerScreenPos,
            out GameObject target,
            out float distance,
            out int targetScore,
            out List<NguiHitCandidate> candidates)
        {
            target = null;
            distance = float.MaxValue;
            targetScore = int.MinValue;
            candidates = NguiDebugRaycastEnabled ? [] : null;
            if (_physicsRaycastAllMethod == null || nguiCamera == null) return false;

            Ray ray = nguiCamera.ScreenPointToRay(pointerScreenPos);
            int layerMask = GetNguiLayerMask(nguiCamera);
            object[] args;

            int paramCount = _physicsRaycastAllMethod.GetParameters().Length;
            if (paramCount == 3)
            {
                args = [ ray, NguiRaycastDistanceMeters, layerMask ];
            }
            else if (paramCount == 2)
            {
                args = [ ray, NguiRaycastDistanceMeters ];
            }
            else
            {
                args = [ ray ];
            }

            object hitsObj = _physicsRaycastAllMethod.Invoke(null, args);
            if (hitsObj is not Array hits || hits.Length == 0) return false;

            float closestDistance = float.MaxValue;
            int bestScore = int.MinValue;
            float bestDistance = float.MaxValue;
            GameObject bestTarget = null;

            foreach (object hit in hits)
            {
                if (hit == null) continue;
                float hitDistance = GetRaycastHitDistance(hit);
                object colliderObj = GetRaycastHitCollider(hit);
                GameObject hitGo = null;
                if (colliderObj is Component colliderComponent)
                {
                    hitGo = colliderComponent.gameObject;
                }
                else if (colliderObj != null)
                {
                    PropertyInfo gameObjectProp = colliderObj.GetType().GetProperty("gameObject", BindingFlags.Public | BindingFlags.Instance);
                    hitGo = gameObjectProp?.GetValue(colliderObj, null) as GameObject;
                }

                if (hitGo == null) continue;
                int hitScore = GetNguiHitScore(hitGo);
                int widgetDepth = GetDepthFromComponentInParents(hitGo, "UIWidget", NguiParentProbeDepth);
                int panelDepth = GetDepthFromComponentInParents(hitGo, "UIPanel", NguiParentProbeDepth);

                if (candidates != null)
                {
                    candidates.Add(new NguiHitCandidate(hitGo, hitDistance, hitGo.layer, hitScore, widgetDepth, panelDepth));
                }

                if (hitDistance < closestDistance)
                {
                    closestDistance = hitDistance;
                }

                float windowDistance = closestDistance + NguiDistanceScoreWindowMeters;
                bool insideScoreWindow = hitDistance <= windowDistance;
                if (!insideScoreWindow) continue;

                bool isBetterScore = hitScore > bestScore;
                bool isTieButCloser = hitScore == bestScore && hitDistance < bestDistance - NguiDistanceTieEpsilonMeters;
                if (isBetterScore || isTieButCloser)
                {
                    bestTarget = hitGo;
                    bestScore = hitScore;
                    bestDistance = hitDistance;
                }
            }

            if (bestTarget == null) return false;

            if (candidates != null)
            {
                candidates.Sort(static (a, b) =>
                {
                    int byDistance = a.Distance.CompareTo(b.Distance);
                    if (byDistance != 0) return byDistance;
                    return b.Score.CompareTo(a.Score);
                });
                if (candidates.Count > NguiDebugMaxCandidates)
                {
                    candidates.RemoveRange(NguiDebugMaxCandidates, candidates.Count - NguiDebugMaxCandidates);
                }
            }

            target = bestTarget;
            distance = bestDistance;
            targetScore = bestScore;
            return true;
        }

        private void CaptureNguiDebugSnapshot(
            Vector2 pointerScreenPos,
            Camera selectedCamera,
            GameObject selectedTarget,
            int selectedTargetScore,
            List<NguiHitCandidate> candidates)
        {
            if (!NguiDebugRaycastEnabled) return;

            _lastNguiDebugPointer = pointerScreenPos;
            _lastNguiDebugCamera = selectedCamera;
            _lastNguiDebugTarget = selectedTarget;
            _lastNguiDebugTargetScore = selectedTargetScore;
            _lastNguiDebugCandidates.Clear();
            if (candidates == null) return;

            foreach (NguiHitCandidate candidate in candidates)
            {
                _lastNguiDebugCandidates.Add(candidate);
            }
        }

        private void LogNguiDebugSnapshot(string reason)
        {
            if (!NguiDebugRaycastEnabled) return;
            if (Time.time < _nextNguiDebugLogTime) return;
            _nextNguiDebugLogTime = Time.time + NguiDebugLogIntervalSeconds;

            string cameraName = _lastNguiDebugCamera != null ? _lastNguiDebugCamera.name : "<none>";
            string targetName = _lastNguiDebugTarget != null ? _lastNguiDebugTarget.name : "<none>";
            string hitSummary;
            if (_lastNguiDebugCandidates.Count == 0)
            {
                hitSummary = "hits=0";
            }
            else
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("hits=");
                sb.Append(_lastNguiDebugCandidates.Count);
                sb.Append(" [");
                for (int i = 0; i < _lastNguiDebugCandidates.Count; i++)
                {
                    NguiHitCandidate candidate = _lastNguiDebugCandidates[i];
                    string layerName = LayerMask.LayerToName(candidate.Layer);
                    if (string.IsNullOrEmpty(layerName)) layerName = "UnnamedLayer";

                    sb.Append(i);
                    sb.Append(':');
                    sb.Append(candidate.Target != null ? candidate.Target.name : "<null>");
                    sb.Append('@');
                    sb.Append(candidate.Distance.ToString("F3"));
                    sb.Append("m");
                    sb.Append(" layer=");
                    sb.Append(layerName);
                    sb.Append('(');
                    sb.Append(candidate.Layer);
                    sb.Append(')');
                    sb.Append(" score=");
                    sb.Append(candidate.Score);
                    sb.Append(" widgetDepth=");
                    sb.Append(FormatDepthValue(candidate.WidgetDepth));
                    sb.Append(" panelDepth=");
                    sb.Append(FormatDepthValue(candidate.PanelDepth));

                    if (i < _lastNguiDebugCandidates.Count - 1) sb.Append(", ");
                }
                sb.Append(']');
                hitSummary = sb.ToString();
            }

            string msg =
                $"[UI][NGUI][Debug] reason={reason} pointer=({_lastNguiDebugPointer.x:F1},{_lastNguiDebugPointer.y:F1}) " +
                $"camera={cameraName} selected={targetName}(score={_lastNguiDebugTargetScore}) {hitSummary}";

            if (msg == _lastNguiDebugSignature) return;
            _lastNguiDebugSignature = msg;
            VRModCore.Log(msg);
        }

        private int GetNguiHitScore(GameObject hitGo)
        {
            if (hitGo == null) return int.MinValue;

            int score = 0;
            bool hasUiButtonSelf = HasComponentOnSelf(hitGo, "UIButton");
            bool hasUiButton = hasUiButtonSelf || HasComponentInParents(hitGo, "UIButton", NguiParentProbeDepth);
            bool hasUiButtonScaleSelf = HasComponentOnSelf(hitGo, "UIButtonScale");
            bool hasUiButtonScale = hasUiButtonScaleSelf || HasComponentInParents(hitGo, "UIButtonScale", NguiParentProbeDepth);
            bool hasPreferredTag = HasTagInParents(hitGo, PreferredButtonTag, NguiParentProbeDepth);
            if (hasUiButtonSelf) score += 260;
            else if (hasUiButton) score += 100;
            if (hasUiButtonScaleSelf) score += 160;
            else if (hasUiButtonScale) score += 45;
            if (hasUiButton && hasPreferredTag) score += 200;
            if (HasComponentInParents(hitGo, "UIEventListener", NguiParentProbeDepth)) score += 40;
            if (HasComponentInParents(hitGo, "UIToggle", NguiParentProbeDepth) || HasComponentInParents(hitGo, "UICheckbox", NguiParentProbeDepth)) score += 80;
            if (HasComponentInParents(hitGo, "UISlider", NguiParentProbeDepth)) score += 75;
            if (HasComponentInParents(hitGo, "UIProgressBar", NguiParentProbeDepth) || HasComponentInParents(hitGo, "UIScrollBar", NguiParentProbeDepth)) score += 65;
            if (HasComponentInParents(hitGo, "UIInput", NguiParentProbeDepth)) score += 60;
            if (HasComponentInParents(hitGo, "UIWidget", NguiParentProbeDepth)) score += 5;
            return score;
        }

        private static bool HasComponentOnSelf(GameObject target, string componentTypeName)
        {
            if (target == null || string.IsNullOrEmpty(componentTypeName)) return false;
            return target.GetComponent(componentTypeName) != null;
        }

        private static bool HasComponentInParents(GameObject target, string componentTypeName, int maxDepth)
        {
            if (target == null || string.IsNullOrEmpty(componentTypeName)) return false;

            Transform current = target.transform;
            int depth = 0;
            while (current != null && depth <= maxDepth)
            {
                if (current.gameObject.GetComponent(componentTypeName) != null) return true;
                current = current.parent;
                depth++;
            }

            return false;
        }

        private static bool HasTagInParents(GameObject target, string tagName, int maxDepth)
        {
            if (target == null || string.IsNullOrEmpty(tagName)) return false;

            Transform current = target.transform;
            int depth = 0;
            while (current != null && depth <= maxDepth)
            {
                // Using .tag avoids CompareTag invalid-tag exceptions in games with custom tag setups.
                if (current.gameObject.tag == tagName) return true;
                current = current.parent;
                depth++;
            }

            return false;
        }

        private static int GetDepthFromComponentInParents(GameObject target, string componentTypeName, int maxDepth)
        {
            if (target == null || string.IsNullOrEmpty(componentTypeName)) return int.MinValue;

            Transform current = target.transform;
            int depth = 0;
            while (current != null && depth <= maxDepth)
            {
                Component component = current.gameObject.GetComponent(componentTypeName);
                if (component != null)
                {
                    object value = GetMemberValue(component, "depth");
                    if (value is int i) return i;
                    if (value is short s) return s;
                    if (value is byte b) return b;
                    if (value is long l) return (int)l;
                    if (value is float f) return Mathf.RoundToInt(f);
                }

                current = current.parent;
                depth++;
            }

            return int.MinValue;
        }

        private static string FormatDepthValue(int depth)
        {
            return depth == int.MinValue ? "n/a" : depth.ToString();
        }

        private static int GetNguiLayerMask(Camera nguiCamera)
        {
            if (nguiCamera == null) return ~0;
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
            {
                int uiMask = 1 << uiLayer;
                if ((nguiCamera.cullingMask & uiMask) != 0) return uiMask;
            }
            return nguiCamera.cullingMask;
        }

        private Rect GetPrimaryNguiScreenRect()
        {
            Rect fallback = new(0f, 0f, Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0) return fallback;
            if (!EnsureNguiBindings()) return fallback;

            RefreshNguiCameras();
            Camera bestCamera = null;
            float bestDepth = float.MinValue;
            foreach (Camera cam in _nguiCameras)
            {
                if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy) continue;
                if (bestCamera == null || cam.depth > bestDepth)
                {
                    bestCamera = cam;
                    bestDepth = cam.depth;
                }
            }

            if (bestCamera == null) return fallback;

            Rect cameraRect = bestCamera.pixelRect;
            float xMin = Mathf.Clamp(cameraRect.xMin, 0f, Screen.width);
            float yMin = Mathf.Clamp(cameraRect.yMin, 0f, Screen.height);
            float xMax = Mathf.Clamp(cameraRect.xMax, 0f, Screen.width);
            float yMax = Mathf.Clamp(cameraRect.yMax, 0f, Screen.height);
            if (xMax - xMin < 1f || yMax - yMin < 1f) return fallback;

            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private float GetRaycastHitDistance(object hit)
        {
            if (hit == null) return float.MaxValue;

            if (_raycastHitDistanceField != null)
            {
                object v = _raycastHitDistanceField.GetValue(hit);
                if (v is float f) return f;
            }

            if (_raycastHitDistanceProperty != null)
            {
                object v = _raycastHitDistanceProperty.GetValue(hit, null);
                if (v is float f) return f;
            }

            object byName = GetMemberValue(hit, "distance");
            if (byName is float byNameFloat) return byNameFloat;
            return float.MaxValue;
        }

        private object GetRaycastHitCollider(object hit)
        {
            if (hit == null) return null;
            if (_raycastHitColliderField != null) return _raycastHitColliderField.GetValue(hit);
            if (_raycastHitColliderProperty != null) return _raycastHitColliderProperty.GetValue(hit, null);
            return GetMemberValue(hit, "collider");
        }

        private void UpdateNguiHoverTarget(GameObject target)
        {
            if (target == _currentNguiHoverTarget) return;

            if (_currentNguiHoverTarget != null)
            {
                SendNguiHover(_currentNguiHoverTarget, false);
            }

            _currentNguiHoverTarget = target;
            if (_currentNguiHoverTarget != null)
            {
                SendNguiHover(_currentNguiHoverTarget, true);
            }
        }

        private void ClearNguiHoverTarget()
        {
            if (_currentNguiHoverTarget != null)
            {
                SendNguiHover(_currentNguiHoverTarget, false);
                _currentNguiHoverTarget = null;
            }
        }

        private static void SendNguiHover(GameObject target, bool hovered)
        {
            if (target == null) return;
            target.SendMessageUpwards("OnHover", hovered, SendMessageOptions.DontRequireReceiver);
        }

        private static void SendNguiPress(GameObject target, bool pressed)
        {
            if (target == null) return;
            target.SendMessageUpwards("OnPress", pressed, SendMessageOptions.DontRequireReceiver);
        }

        private static void SendNguiClick(GameObject target)
        {
            if (target == null) return;
            target.SendMessageUpwards("OnClick", SendMessageOptions.DontRequireReceiver);
        }

        private void LogNguiEventProbeIfNeeded(GameObject target)
        {
            if (!NguiDebugEventProbeEnabled || target == null) return;

            int targetId = target.GetInstanceID();
            if (_eventProbeLoggedTargets.Contains(targetId)) return;
            _eventProbeLoggedTargets.Add(targetId);

            var sb = new System.Text.StringBuilder();
            sb.Append("[UI][NGUI][Probe] target='");
            sb.Append(target.name);
            sb.Append("' handlers=");

            bool hasAnyHandlers = false;
            Transform current = target.transform;
            int depth = 0;
            while (current != null && depth <= NguiParentProbeDepth)
            {
                Component[] components = current.gameObject.GetComponents<Component>();
                if (components != null)
                {
                    for (int i = 0; i < components.Length; i++)
                    {
                        Component component = components[i];
                        if (component == null) continue;

                        Type type = component.GetType();
                        List<string> handledEvents = GetNguiHandledEvents(type);
                        List<string> listenerDelegates = GetUiEventListenerDelegates(component);
                        if (handledEvents.Count == 0 && listenerDelegates.Count == 0) continue;

                        if (hasAnyHandlers) sb.Append(" | ");
                        hasAnyHandlers = true;
                        sb.Append(current.gameObject.name);
                        sb.Append(":");
                        sb.Append(type.Name);
                        sb.Append("{");
                        bool hasEntry = false;
                        if (handledEvents.Count > 0)
                        {
                            sb.Append("methods=");
                            sb.Append(string.Join(",", handledEvents));
                            hasEntry = true;
                        }
                        if (listenerDelegates.Count > 0)
                        {
                            if (hasEntry) sb.Append(";");
                            sb.Append("listeners=");
                            sb.Append(string.Join(",", listenerDelegates));
                        }
                        sb.Append("}");
                    }
                }

                current = current.parent;
                depth++;
            }

            if (!hasAnyHandlers) sb.Append("<none>");
            VRModCore.Log(sb.ToString());
        }

        private static List<string> GetNguiHandledEvents(Type componentType)
        {
            List<string> names = [];
            if (componentType == null) return names;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            TryAddEventMethod(componentType, names, "OnPress", [ typeof(bool) ], flags);
            TryAddEventMethod(componentType, names, "OnClick", Type.EmptyTypes, flags);
            TryAddEventMethod(componentType, names, "OnDoubleClick", Type.EmptyTypes, flags);
            TryAddEventMethod(componentType, names, "OnDragStart", Type.EmptyTypes, flags);
            TryAddEventMethod(componentType, names, "OnDrag", [ typeof(Vector2) ], flags);
            TryAddEventMethod(componentType, names, "OnDragEnd", Type.EmptyTypes, flags);
            TryAddEventMethod(componentType, names, "OnHover", [ typeof(bool) ], flags);
            TryAddEventMethod(componentType, names, "OnSelect", [ typeof(bool) ], flags);
            TryAddEventMethod(componentType, names, "OnDrop", [ typeof(GameObject) ], flags);
            TryAddEventMethod(componentType, names, "OnScroll", [ typeof(float) ], flags);
            TryAddEventMethod(componentType, names, "OnTooltip", [ typeof(bool) ], flags);
            TryAddEventMethod(componentType, names, "OnKey", [ typeof(KeyCode) ], flags);
            return names;
        }

        private static void TryAddEventMethod(Type type, List<string> list, string methodName, Type[] args, BindingFlags flags)
        {
            MethodInfo method = type.GetMethod(methodName, flags, null, args, null);
            if (method != null) list.Add(methodName);
        }

        private static List<string> GetUiEventListenerDelegates(Component component)
        {
            List<string> names = [];
            if (component == null) return names;

            Type type = component.GetType();
            if (!string.Equals(type.Name, "UIEventListener", StringComparison.OrdinalIgnoreCase)) return names;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo[] fields = type.GetFields(flags);
            foreach (FieldInfo field in fields)
            {
                if (field.IsStatic) continue;
                if (!typeof(Delegate).IsAssignableFrom(field.FieldType)) continue;

                object value = field.GetValue(component);
                if (value is Delegate d && d.GetInvocationList().Length > 0)
                {
                    names.Add(field.Name);
                }
            }

            return names;
        }

        private void EnsureRayVisuals(GameObject vrRig)
        {
            if (_rayLine != null && _cursorObject != null) return;

            _rayObject = new GameObject(RayObjectName);
            _rayObject.transform.SetParent(vrRig.transform, false);
            _rayLine = _rayObject.AddComponent<LineRenderer>();
            _rayLine.useWorldSpace = true;
            _rayLine.positionCount = 2;
            _rayLine.startWidth = RayWidthMeters;
            _rayLine.endWidth = RayWidthMeters;
            _rayLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _rayLine.receiveShadows = false;
            _rayLine.numCapVertices = 4;

            Shader rayShader = Shader.Find("Sprites/Default");
            if (rayShader == null) rayShader = Shader.Find("Unlit/Color");
            _rayMaterial = new Material(rayShader);
            _rayMaterial.color = RayVisibleColor;
            _rayLine.material = _rayMaterial;

            _cursorObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _cursorObject.name = CursorObjectName;
            _cursorObject.transform.SetParent(vrRig.transform, true);
            _cursorObject.transform.localScale = Vector3.one * (CursorScaleMeters * GetUiPanelScaleMultiplier());

            object collider = _cursorObject.GetComponent("Collider");
            if (collider is UnityEngine.Object colliderObj) UnityEngine.Object.Destroy(colliderObj);

            _cursorRenderer = _cursorObject.GetComponent<Renderer>();
            if (_cursorRenderer != null)
            {
                _cursorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _cursorRenderer.receiveShadows = false;
                Shader cursorShader = Shader.Find("Sprites/Default");
                if (cursorShader == null) cursorShader = Shader.Find("Unlit/Color");
                _cursorMaterial = new Material(cursorShader);
                _cursorMaterial.color = RayVisibleColor;
                _cursorRenderer.sharedMaterial = _cursorMaterial;
            }

            SetRayVisible(false);
        }

        private void SetRayVisible(bool visible)
        {
            if (_rayLine != null) _rayLine.enabled = visible;
            if (_cursorObject != null && _cursorObject.activeSelf != visible) _cursorObject.SetActive(visible);
        }

        private void UpdateRayVisual(Vector3 origin, Vector3 endPoint, bool hasTarget)
        {
            if (_rayLine == null || _cursorObject == null) return;
            UpdateCursorScaleIfNeeded();

            SetRayVisible(hasTarget);
            if (!hasTarget) return;

            _rayLine.SetPosition(0, origin);
            _rayLine.SetPosition(1, endPoint);

            if (_rayMaterial != null) _rayMaterial.color = RayVisibleColor;
            if (_cursorMaterial != null) _cursorMaterial.color = RayVisibleColor;

            _cursorObject.transform.position = endPoint;
        }

        private void UpdateCursorScaleIfNeeded()
        {
            if (_cursorObject == null) return;

            float scaleMultiplier = GetUiPanelScaleMultiplier();
            if (Mathf.Abs(scaleMultiplier - _lastAppliedCursorScale) <= 0.0001f) return;

            _lastAppliedCursorScale = scaleMultiplier;
            _cursorObject.transform.localScale = Vector3.one * (CursorScaleMeters * scaleMultiplier);
        }

        private static float GetUiPanelScaleMultiplier()
        {
            float configuredScale = ConfigManager.OpenVR_UiPanelScale?.Value ?? 1.0f;
            return Mathf.Clamp(configuredScale, MinPanelScale, MaxPanelScale);
        }

        private void DestroyRayVisuals()
        {
            if (_rayObject != null) UnityEngine.Object.Destroy(_rayObject);
            if (_cursorObject != null) UnityEngine.Object.Destroy(_cursorObject);
            if (_rayMaterial != null) UnityEngine.Object.Destroy(_rayMaterial);
            if (_cursorMaterial != null) UnityEngine.Object.Destroy(_cursorMaterial);

            _rayObject = null;
            _rayLine = null;
            _rayMaterial = null;
            _cursorObject = null;
            _cursorRenderer = null;
            _cursorMaterial = null;
        }

        private static bool TryGetProjectionPlaneUv(GameObject surface, Vector3 hitWorldPoint, out Vector2 uv)
        {
            uv = default;
            if (surface == null) return false;

            // Projection plane is a Unity quad (X/Y in local -0.5..0.5). Use strict local bounds.
            Vector3 local = surface.transform.InverseTransformPoint(hitWorldPoint);
            if (local.x < -0.5f - UiSurfaceBoundsEpsilon || local.x > 0.5f + UiSurfaceBoundsEpsilon ||
                local.y < -0.5f - UiSurfaceBoundsEpsilon || local.y > 0.5f + UiSurfaceBoundsEpsilon)
            {
                return false;
            }

            uv.x = Mathf.Clamp01(local.x + 0.5f);
            uv.y = Mathf.Clamp01(local.y + 0.5f);
            return true;
        }

        private void ReleasePressedIfAny()
        {
            if (!_pointerIsDown) return;

            if (_lastUiInteractionUsedMouseInjection)
            {
                if (IsWindowsPlatform)
                {
                    _ = TrySendMouseLeftEvent(false, out _);
                }
            }
            else if (_pressedTarget != null)
            {
                SendNguiPress(_pressedTarget, false);
            }

            _pointerIsDown = false;
            _pressedTarget = null;
            _nextHoldPressRepeatTime = 0f;
        }

        private bool TryInjectMousePointerPosition(Vector2 pointerScreenPos)
        {
            if (!IsWindowsPlatform)
            {
                if (!_loggedMouseInjectionUnsupportedPlatform)
                {
                    _loggedMouseInjectionUnsupportedPlatform = true;
                    VRModCore.LogWarning("[UI][Mouse] Mouse pointer injection is only supported on Windows.");
                }

                return false;
            }

            if (!TryGetMouseInjectionWindowHandle(out IntPtr windowHandle))
            {
                if (!_loggedMouseInjectionWindowMissing)
                {
                    _loggedMouseInjectionWindowMissing = true;
                    VRModCore.LogWarning("[UI][Mouse] Failed to resolve game window handle for pointer injection.");
                }

                return false;
            }

            _loggedMouseInjectionWindowMissing = false;

            if (!GetClientRect(windowHandle, out RECT clientRect))
            {
                return false;
            }

            int clientWidth = clientRect.Right - clientRect.Left;
            int clientHeight = clientRect.Bottom - clientRect.Top;
            if (clientWidth <= 0 || clientHeight <= 0) return false;

            float screenWidth = Mathf.Max(1f, Screen.width - 1f);
            float screenHeight = Mathf.Max(1f, Screen.height - 1f);
            float clampedX = Mathf.Clamp(pointerScreenPos.x, 0f, screenWidth);
            float clampedY = Mathf.Clamp(pointerScreenPos.y, 0f, screenHeight);

            int clientX = Mathf.Clamp(Mathf.RoundToInt((clampedX / screenWidth) * (clientWidth - 1)), 0, clientWidth - 1);
            int clientY = Mathf.Clamp(Mathf.RoundToInt(((screenHeight - clampedY) / screenHeight) * (clientHeight - 1)), 0, clientHeight - 1);

            POINT clientPoint = new() { X = clientX, Y = clientY };
            if (!ClientToScreen(windowHandle, ref clientPoint))
            {
                return false;
            }

            return SetCursorPos(clientPoint.X, clientPoint.Y);
        }

        private readonly struct NguiHitCandidate(GameObject target, float distance, int layer, int score, int widgetDepth, int panelDepth)
        {
            public GameObject Target { get; } = target;
            public float Distance { get; } = distance;
            public int Layer { get; } = layer;
            public int Score { get; } = score;
            public int WidgetDepth { get; } = widgetDepth;
            public int PanelDepth { get; } = panelDepth;
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null) return null;
            Type type = instance.GetType();
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            PropertyInfo property = type.GetProperty(memberName, Flags);
            if (property != null && property.CanRead) return property.GetValue(instance, null);

            FieldInfo field = type.GetField(memberName, Flags);
            if (field != null) return field.GetValue(instance);

            return null;
        }

        private static Type ResolveTypeAnyAssembly(string fullTypeName)
        {
            Type type = Type.GetType(fullTypeName, false);
            if (type != null) return type;

            type = Type.GetType($"{fullTypeName}, UnityEngine.PhysicsModule", false);
            if (type != null) return type;

            type = Type.GetType($"{fullTypeName}, UnityEngine.CoreModule", false);
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

        private void UpdateRightMenuSpaceMapping(bool rightMenuPressed)
        {
            bool mappingEnabled = ConfigManager.OpenVR_MapRightMenuButtonToSpace?.Value ?? false;
            if (!mappingEnabled)
            {
                ReleaseMappedSpaceKeyIfNeeded();
                _lastSpaceMapButtonPressed = false;
                return;
            }

            if (!IsWindowsPlatform)
            {
                if (!_loggedSpaceMappingUnsupportedPlatform)
                {
                    _loggedSpaceMappingUnsupportedPlatform = true;
                    VRModCore.LogWarning("[InputMap] Right Menu/B -> Space mapping is only supported on Windows.");
                }
                return;
            }

            if (rightMenuPressed == _lastSpaceMapButtonPressed) return;
            _lastSpaceMapButtonPressed = rightMenuPressed;

            if (TrySendSpaceKeyEvent(rightMenuPressed, out int lastError))
            {
                _isSpaceMappedPressed = rightMenuPressed;
                VRModCore.Log($"[InputMap] Right Menu/B -> Space {(rightMenuPressed ? "Down" : "Up")}");
                return;
            }

            if (!rightMenuPressed)
            {
                _isSpaceMappedPressed = false;
            }

            VRModCore.LogWarning($"[InputMap] Failed to inject Space {(rightMenuPressed ? "Down" : "Up")} (Win32Error={lastError}).");
        }

        private void ReleaseMappedSpaceKeyIfNeeded()
        {
            if (!_isSpaceMappedPressed) return;

            if (IsWindowsPlatform)
            {
                _ = TrySendSpaceKeyEvent(false, out _);
            }

            _isSpaceMappedPressed = false;
            _lastSpaceMapButtonPressed = false;
        }

        private static bool TrySendSpaceKeyEvent(bool keyDown, out int lastError)
        {
            INPUT[] input =
            [
                new INPUT
                {
                    type = InputTypeKeyboard,
                    U = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = VirtualKeySpace,
                            wScan = 0,
                            dwFlags = keyDown ? 0u : KeyEventFlagKeyUp,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                }
            ];

            uint sent = SendInput(1u, input, Marshal.SizeOf(typeof(INPUT)));
            if (sent == 1u)
            {
                lastError = 0;
                return true;
            }

            lastError = Marshal.GetLastWin32Error();
            return false;
        }

        private static bool TrySendMouseLeftEvent(bool buttonDown, out int lastError)
        {
            INPUT[] input =
            [
                new INPUT
                {
                    type = InputTypeMouse,
                    U = new InputUnion
                    {
                        mi = new MOUSEINPUT
                        {
                            dx = 0,
                            dy = 0,
                            mouseData = 0,
                            dwFlags = buttonDown ? MouseEventFlagLeftDown : MouseEventFlagLeftUp,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                }
            ];

            uint sent = SendInput(1u, input, Marshal.SizeOf(typeof(INPUT)));
            if (sent == 1u)
            {
                lastError = 0;
                return true;
            }

            lastError = Marshal.GetLastWin32Error();
            return false;
        }

        private static bool TryGetMouseInjectionWindowHandle(out IntPtr windowHandle)
        {
            windowHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            if (windowHandle != IntPtr.Zero) return true;

            windowHandle = GetForegroundWindow();
            return windowHandle != IntPtr.Zero;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private static bool TryReadRightControllerState(CVRSystem hmd, out uint deviceIndex, out VRControllerState_t state)
        {
            deviceIndex = hmd.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);
            state = new VRControllerState_t();

            if (deviceIndex == OpenVR.k_unTrackedDeviceIndexInvalid || !hmd.IsTrackedDeviceConnected(deviceIndex))
            {
                return false;
            }

            return hmd.GetControllerState(deviceIndex, ref state, (uint)Marshal.SizeOf(typeof(VRControllerState_t)));
        }

        private bool IsTriggerPressed(VRControllerState_t state)
        {
            if (IsButtonPressed(state.ulButtonPressed, EVRButtonId.k_EButton_SteamVR_Trigger))
            {
                return true;
            }

            float triggerValue = state.rAxis1.x;
            float threshold = _wasTriggerPressed ? TriggerReleaseThreshold : TriggerPressThreshold;
            return triggerValue >= threshold;
        }

        private static bool TryGetControllerRay(GameObject vrRig, TrackedDevicePose_t[] trackedPoses, uint deviceIndex, out Vector3 rayOriginWorld, out Vector3 rayDirectionWorld)
        {
            rayOriginWorld = default;
            rayDirectionWorld = default;

            if (vrRig == null || trackedPoses == null || deviceIndex >= trackedPoses.Length || !trackedPoses[deviceIndex].bPoseIsValid)
            {
                return false;
            }

            HmdMatrix34_t controllerPose = trackedPoses[deviceIndex].mDeviceToAbsoluteTracking;
            Vector3 localPos = GetPositionFromHmdMatrix(controllerPose);
            Quaternion localRot = GetRotationFromHmdMatrix(controllerPose);

            rayOriginWorld = vrRig.transform.TransformPoint(localPos);
            rayDirectionWorld = vrRig.transform.TransformDirection(localRot * Vector3.forward).normalized;
            return rayDirectionWorld.sqrMagnitude > 0.0001f;
        }

        private static bool IsButtonPressed(ulong buttonMask, EVRButtonId buttonId)
        {
            int buttonBit = (int)buttonId;
            if (buttonBit < 0 || buttonBit > 63) return false;
            ulong bit = 1UL << buttonBit;
            return (buttonMask & bit) != 0;
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
    }
}
#endif
