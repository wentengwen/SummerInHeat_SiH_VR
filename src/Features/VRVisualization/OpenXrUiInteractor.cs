#if OPENXR_BUILD
using System.Runtime.InteropServices;
using UnityVRMod.Config;
using UnityVRMod.Core;

namespace UnityVRMod.Features.VrVisualization
{
    internal sealed class OpenXrUiInteractor
    {
        private const float PointerFallbackDistanceMeters = 4.0f;
        private const float UiSurfaceMaxRayDistanceMeters = 8.0f;
        private const float UiSurfaceBoundsEpsilon = 0.001f;
        private const float UiSurfaceVisualEdgeExpandNormalized = 0.08f;
        private const float UiSurfaceLookupIntervalSeconds = 2.0f;
        private const float RayWidthMeters = 0.0018f;
        private const float CursorScaleMeters = 0.015f;
        private const float MinPanelScale = 0.35f;
        private const float MaxPanelScale = 3.0f;
        private const string UiSurfaceName = "UnityVRMod_UIProjectionPlane";
        private const string UiRayLayerName = "UIRay";
        private const string RayObjectName = "OpenXR_UIRay";
        private const string CursorObjectName = "OpenXR_UICursor";
        private const string ControllerIconObjectName = "OpenXR_UITouchIcon";
        private const int UiRayLayerFallbackMask = 1 << 19;
        private const float UiRaycastDistanceMeters = 200.0f;
        private const float UiRayHitNormalOffsetMeters = 0.003f;
        private const float UiRayTouchRadiusMeters = 0.032f;
        private const float ControllerIconScaleMeters = 0.032f;
        private const float ControllerIconRightOffsetMeters = 0.055f;
        private const float ControllerIconUpOffsetMeters = 0.005f;
        private const uint InputTypeMouse = 0;
        private const uint MouseEventFlagLeftDown = 0x0002;
        private const uint MouseEventFlagLeftUp = 0x0004;
        private static readonly bool IsWindowsPlatform = Environment.OSVersion.Platform == PlatformID.Win32NT;
        private static readonly Color RayVisibleColor = new(1.00f, 1.00f, 1.00f, 0.95f);

        private Camera _mainCamera;
        private GameObject _uiSurface;
        private float _nextUiSurfaceLookupTime;
        private bool _loggedUiSurfaceFallback;
        private bool _wasTriggerPressed;
        private bool _pointerIsDown;
        private bool _resolvedUiRayLayerMask;
        private bool _loggedUiRayLayerFallback;
        private bool _physicsBindingsResolved;
        private bool _physicsBindingsFailed;
        private bool _wasGripPressedForUiRayTouch;
        private bool _loggedMouseInjectionUnsupportedPlatform;
        private bool _loggedMouseInjectionWindowMissing;
        private int _uiRayLayerMask;
        private MethodInfo _physicsRaycastAllMethod;
        private MethodInfo _physicsOverlapSphereMethod;
        private FieldInfo _raycastHitDistanceField;
        private PropertyInfo _raycastHitDistanceProperty;
        private FieldInfo _raycastHitColliderField;
        private PropertyInfo _raycastHitColliderProperty;
        private FieldInfo _raycastHitPointField;
        private PropertyInfo _raycastHitPointProperty;
        private FieldInfo _raycastHitNormalField;
        private PropertyInfo _raycastHitNormalProperty;
        private GameObject _rayObject;
        private LineRenderer _rayLine;
        private Material _rayMaterial;
        private GameObject _cursorObject;
        private Material _cursorMaterial;
        private float _lastAppliedCursorScale;
        private GameObject _controllerIconObject;
        private Material _controllerIconMaterial;
        private Texture2D _controllerIconTexture;
        private Texture2D _fixedTouchIconTexture;
        private Type _zngControllerType;
        private MethodInfo _zngButtonDownMethod;
        private UnityEngine.Object _zngControllerInstance;
        private float _nextZngControllerLookupTime;

        public void Initialize(Camera mainCamera)
        {
            DestroyRayVisuals();

            _mainCamera = mainCamera;
            _uiSurface = null;
            _nextUiSurfaceLookupTime = 0f;
            _loggedUiSurfaceFallback = false;
            _wasTriggerPressed = false;
            _pointerIsDown = false;
            _wasGripPressedForUiRayTouch = false;
            _resolvedUiRayLayerMask = false;
            _loggedUiRayLayerFallback = false;
            _physicsBindingsResolved = false;
            _physicsBindingsFailed = false;
            _loggedMouseInjectionUnsupportedPlatform = false;
            _loggedMouseInjectionWindowMissing = false;
            _uiRayLayerMask = 0;
            _physicsRaycastAllMethod = null;
            _physicsOverlapSphereMethod = null;
            _raycastHitDistanceField = null;
            _raycastHitDistanceProperty = null;
            _raycastHitColliderField = null;
            _raycastHitColliderProperty = null;
            _raycastHitPointField = null;
            _raycastHitPointProperty = null;
            _raycastHitNormalField = null;
            _raycastHitNormalProperty = null;
            _lastAppliedCursorScale = -1f;
            _controllerIconTexture = null;
            _fixedTouchIconTexture = CreateFixedTouchIconTexture();
            _zngControllerType = null;
            _zngButtonDownMethod = null;
            _zngControllerInstance = null;
            _nextZngControllerLookupTime = 0f;
        }

        public void Teardown()
        {
            ReleasePointerIfNeeded();
            DestroyRayVisuals();
            _mainCamera = null;
            _uiSurface = null;
            _loggedUiSurfaceFallback = false;
            _wasTriggerPressed = false;
            _wasGripPressedForUiRayTouch = false;
            _resolvedUiRayLayerMask = false;
            _loggedUiRayLayerFallback = false;
            _physicsBindingsResolved = false;
            _physicsBindingsFailed = false;
            _loggedMouseInjectionUnsupportedPlatform = false;
            _loggedMouseInjectionWindowMissing = false;
            _uiRayLayerMask = 0;
            _physicsRaycastAllMethod = null;
            _physicsOverlapSphereMethod = null;
            _raycastHitDistanceField = null;
            _raycastHitDistanceProperty = null;
            _raycastHitColliderField = null;
            _raycastHitColliderProperty = null;
            _raycastHitPointField = null;
            _raycastHitPointProperty = null;
            _raycastHitNormalField = null;
            _raycastHitNormalProperty = null;
            _lastAppliedCursorScale = -1f;
            _controllerIconTexture = null;
            _fixedTouchIconTexture = null;
            _zngControllerType = null;
            _zngButtonDownMethod = null;
            _zngControllerInstance = null;
            _nextZngControllerLookupTime = 0f;
        }

        public void Update(GameObject vrRig, bool hasPointerPose, Vector3 rayOrigin, Vector3 rayDirection, bool triggerPressed)
        {
            if (_mainCamera == null && Camera.main != null)
            {
                _mainCamera = Camera.main;
            }

            if (vrRig == null)
            {
                HandleNoPointerFrame(triggerPressed);
                SetRayVisible(false);
                _wasTriggerPressed = triggerPressed;
                return;
            }

            EnsureRayVisuals(vrRig);

            if (!hasPointerPose || rayDirection.sqrMagnitude <= 0.0001f)
            {
                HandleNoPointerFrame(triggerPressed);
                SetRayVisible(false);
                _wasTriggerPressed = triggerPressed;
                return;
            }

            Vector3 normalizedRayDirection = rayDirection.normalized;
            bool justPressed = triggerPressed && !_wasTriggerPressed;
            bool justReleased = !triggerPressed && _wasTriggerPressed;

            if (!TryGetPointerScreenPosition(rayOrigin, normalizedRayDirection, out Vector2 pointerScreenPos, out Vector3 rayEndPoint, out bool hasVisualTarget))
            {
                HandleNoPointerFrame(triggerPressed);
                // Keep ray visible on the frame/edge ring, but do not inject UI input.
                UpdateRayVisual(rayOrigin, rayEndPoint, hasTarget: hasVisualTarget);
                _wasTriggerPressed = triggerPressed;
                return;
            }

            _ = TryInjectMousePointerPosition(pointerScreenPos);

            if (justPressed)
            {
                if (TrySendMouseLeftEvent(true, out int downError))
                {
                    _pointerIsDown = true;
                }
                else
                {
                    VRModCore.LogWarning($"[UI][OpenXR][Mouse] Failed to inject LeftDown (Win32Error={downError}).");
                }
            }

            if (_pointerIsDown && justReleased)
            {
                if (!TrySendMouseLeftEvent(false, out int upError))
                {
                    VRModCore.LogWarning($"[UI][OpenXR][Mouse] Failed to inject LeftUp (Win32Error={upError}).");
                }

                _pointerIsDown = false;
            }

            // Ray/cursor visibility should depend on ray hit mapping, not OS mouse injection success.
            UpdateRayVisual(rayOrigin, rayEndPoint, hasTarget: true);
            _wasTriggerPressed = triggerPressed;
        }

        public bool UpdateUiRayTouch(GameObject vrRig, bool hasHandPose, Vector3 handWorldPos, Quaternion handWorldRot, bool gripPressed)
        {
            if (vrRig == null)
            {
                SetControllerIconVisible(false);
                _wasGripPressedForUiRayTouch = gripPressed;
                return false;
            }

            EnsureControllerIconVisual(vrRig);
            bool gripDown = gripPressed && !_wasGripPressedForUiRayTouch;

            if (!hasHandPose)
            {
                SetControllerIconVisible(false);
                _wasGripPressedForUiRayTouch = gripPressed;
                return false;
            }

            bool mappedIconHit = TryGetUiRayTouchInteraction(handWorldPos, out _, out string icon, out bool hasUiRayTouchHit);
            if (!hasUiRayTouchHit)
            {
                SetControllerIconVisible(false);
                _wasGripPressedForUiRayTouch = gripPressed;
                return false;
            }

            UpdateControllerIconVisual(_fixedTouchIconTexture, handWorldPos, handWorldRot);

            bool triggered = false;
            if (mappedIconHit && gripDown)
            {
                triggered = TryInvokeZngButtonDown(icon);
            }

            _wasGripPressedForUiRayTouch = gripPressed;
            return triggered;
        }

        private void HandleNoPointerFrame(bool triggerPressedNow)
        {
            if (_pointerIsDown && !triggerPressedNow)
            {
                ReleasePointerIfNeeded();
            }
        }

        private bool TryGetPointerScreenPosition(Vector3 rayOrigin, Vector3 rayDirection, out Vector2 pointerScreenPos, out Vector3 rayEndPoint, out bool hasVisualTarget)
        {
            pointerScreenPos = default;
            rayEndPoint = rayOrigin + (rayDirection * PointerFallbackDistanceMeters);
            hasVisualTarget = false;

            bool constrainToUiSurface;
            Vector3 uiHitPoint;
            if (TryGetUiSurfaceScreenPosition(rayOrigin, rayDirection, out pointerScreenPos, out uiHitPoint, out constrainToUiSurface, out bool hasVisualSurfaceHit))
            {
                rayEndPoint = uiHitPoint;
                hasVisualTarget = true;
                return true;
            }

            if (constrainToUiSurface)
            {
                rayEndPoint = uiHitPoint;
                hasVisualTarget = hasVisualSurfaceHit;
                return false;
            }

            if (_uiSurface != null)
            {
                return false;
            }

            if (_mainCamera == null)
            {
                return false;
            }

            Vector3 fallbackPointWorld = rayOrigin + rayDirection * PointerFallbackDistanceMeters;
            rayEndPoint = fallbackPointWorld;
            Vector3 screenPoint = _mainCamera.WorldToScreenPoint(fallbackPointWorld);
            if (screenPoint.z <= 0f) return false;

            pointerScreenPos = new Vector2(
                Mathf.Clamp(screenPoint.x, 0f, Screen.width),
                Mathf.Clamp(screenPoint.y, 0f, Screen.height));
            hasVisualTarget = true;

            if (!_loggedUiSurfaceFallback)
            {
                _loggedUiSurfaceFallback = true;
                VRModCore.LogWarning($"[UI][OpenXR] '{UiSurfaceName}' not found. Using main-camera screen fallback for UI ray mapping.");
            }

            return true;
        }

        private bool TryGetUiSurfaceScreenPosition(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            out Vector2 screenPos,
            out Vector3 hitWorldPoint,
            out bool constrainToUiSurface,
            out bool hasVisualSurfaceHit)
        {
            screenPos = default;
            hitWorldPoint = rayOrigin + (rayDirection * PointerFallbackDistanceMeters);
            constrainToUiSurface = false;
            hasVisualSurfaceHit = false;

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
                return false;
            }

            hitWorldPoint = rayOrigin + (rayDirection * enterDistance);
            if (!TryGetProjectionPlaneUv(_uiSurface, hitWorldPoint, out Vector2 uv))
            {
                hasVisualSurfaceHit = IsWithinProjectionPlaneVisualBounds(_uiSurface, hitWorldPoint);
                return false;
            }

            hasVisualSurfaceHit = true;
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

        private static Rect GetPrimaryNguiScreenRect()
        {
            if (TryGetPrimaryNguiPixelRect(out Rect pixelRect))
            {
                return pixelRect;
            }

            return new Rect(0f, 0f, Screen.width, Screen.height);
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

        private bool TryGetUiRayTouchInteraction(Vector3 handWorldPos, out Vector3 hitPointWorld, out string icon, out bool hasUiRayTouchHit)
        {
            hitPointWorld = handWorldPos;
            icon = string.Empty;
            hasUiRayTouchHit = false;

            if (!EnsurePhysicsBindings()) return false;

            int layerMask = GetUiRayLayerMask();
            if (layerMask == 0) return false;

            int paramCount = _physicsOverlapSphereMethod.GetParameters().Length;
            object[] args;
            if (paramCount == 3)
            {
                args = [ handWorldPos, UiRayTouchRadiusMeters, layerMask ];
            }
            else if (paramCount == 2)
            {
                args = [ handWorldPos, UiRayTouchRadiusMeters ];
            }
            else
            {
                ParameterInfo[] parameters = _physicsOverlapSphereMethod.GetParameters();
                args = new object[paramCount];
                args[0] = handWorldPos;
                args[1] = UiRayTouchRadiusMeters;
                for (int i = 2; i < paramCount; i++)
                {
                    Type parameterType = parameters[i].ParameterType;
                    if (i == 2 && parameterType == typeof(int))
                    {
                        args[i] = layerMask;
                    }
                    else if (parameterType.IsEnum)
                    {
                        args[i] = Enum.ToObject(parameterType, 0);
                    }
                    else if (parameterType.IsValueType)
                    {
                        args[i] = Activator.CreateInstance(parameterType);
                    }
                    else
                    {
                        args[i] = null;
                    }
                }
            }

            object overlapsObj = _physicsOverlapSphereMethod.Invoke(null, args);
            if (overlapsObj is not Array overlaps || overlaps.Length == 0) return false;

            float bestDistance = float.MaxValue;
            Vector3 bestPoint = default;
            string bestIcon = string.Empty;
            bool foundTouch = false;

            foreach (object colliderObj in overlaps)
            {
                if (colliderObj == null) continue;

                GameObject hitGo = ExtractGameObjectFromCollider(colliderObj);
                if (hitGo == null) continue;

                foundTouch = true;
                Vector3 point = GetColliderClosestPoint(colliderObj, handWorldPos);
                float hitDistance = Vector3.Distance(handWorldPos, point);
                if (hitDistance >= bestDistance) continue;

                bestDistance = hitDistance;
                bestPoint = point;
                if (TryMapUiRayColliderToIcon(hitGo.name, out string mappedIcon))
                {
                    bestIcon = mappedIcon;
                }
                else
                {
                    bestIcon = string.Empty;
                }
            }

            hasUiRayTouchHit = foundTouch;
            if (string.IsNullOrEmpty(bestIcon)) return false;

            icon = bestIcon;
            hitPointWorld = bestPoint;
            return true;
        }

        private int GetUiRayLayerMask()
        {
            if (_resolvedUiRayLayerMask) return _uiRayLayerMask;

            _resolvedUiRayLayerMask = true;
            int uiRayLayer = LayerMask.NameToLayer(UiRayLayerName);
            if (uiRayLayer >= 0 && uiRayLayer <= 31)
            {
                _uiRayLayerMask = 1 << uiRayLayer;
                return _uiRayLayerMask;
            }

            _uiRayLayerMask = UiRayLayerFallbackMask;
            if (!_loggedUiRayLayerFallback)
            {
                _loggedUiRayLayerFallback = true;
                VRModCore.LogWarning($"[UI][OpenXR] Layer '{UiRayLayerName}' not found. Falling back to mask {UiRayLayerFallbackMask}.");
            }

            return _uiRayLayerMask;
        }

        private bool EnsurePhysicsBindings()
        {
            if (_physicsBindingsResolved) return true;
            if (_physicsBindingsFailed) return false;

            Type physicsType = ResolveTypeAnyAssembly("UnityEngine.Physics");
            if (physicsType == null)
            {
                _physicsBindingsFailed = true;
                VRModCore.LogWarning("[UI][OpenXR] UnityEngine.Physics type not found. UIRay interaction disabled.");
                return false;
            }

            _physicsRaycastAllMethod = physicsType.GetMethod("RaycastAll", BindingFlags.Public | BindingFlags.Static, null, [ typeof(Ray), typeof(float), typeof(int) ], null)
                ?? physicsType.GetMethod("RaycastAll", BindingFlags.Public | BindingFlags.Static, null, [ typeof(Ray), typeof(float) ], null)
                ?? physicsType.GetMethod("RaycastAll", BindingFlags.Public | BindingFlags.Static, null, [ typeof(Ray) ], null);

            if (_physicsRaycastAllMethod == null)
            {
                _physicsBindingsFailed = true;
                VRModCore.LogWarning("[UI][OpenXR] Physics.RaycastAll overload not found. UIRay interaction disabled.");
                return false;
            }

            _physicsOverlapSphereMethod = physicsType.GetMethod("OverlapSphere", BindingFlags.Public | BindingFlags.Static, null, [ typeof(Vector3), typeof(float), typeof(int) ], null)
                ?? physicsType.GetMethod("OverlapSphere", BindingFlags.Public | BindingFlags.Static, null, [ typeof(Vector3), typeof(float) ], null);

            if (_physicsOverlapSphereMethod == null)
            {
                foreach (MethodInfo candidate in physicsType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!string.Equals(candidate.Name, "OverlapSphere", StringComparison.Ordinal)) continue;
                    ParameterInfo[] ps = candidate.GetParameters();
                    if (ps.Length < 2) continue;
                    if (ps[0].ParameterType != typeof(Vector3) || ps[1].ParameterType != typeof(float)) continue;
                    _physicsOverlapSphereMethod = candidate;
                    break;
                }
            }

            if (_physicsOverlapSphereMethod == null)
            {
                _physicsBindingsFailed = true;
                VRModCore.LogWarning("[UI][OpenXR] Physics.OverlapSphere overload not found. UIRay touch interaction disabled.");
                return false;
            }

            Type raycastHitType = ResolveTypeAnyAssembly("UnityEngine.RaycastHit");
            if (raycastHitType != null)
            {
                _raycastHitDistanceField = raycastHitType.GetField("distance", BindingFlags.Public | BindingFlags.Instance);
                _raycastHitDistanceProperty = raycastHitType.GetProperty("distance", BindingFlags.Public | BindingFlags.Instance);
                _raycastHitColliderField = raycastHitType.GetField("collider", BindingFlags.Public | BindingFlags.Instance);
                _raycastHitColliderProperty = raycastHitType.GetProperty("collider", BindingFlags.Public | BindingFlags.Instance);
                _raycastHitPointField = raycastHitType.GetField("point", BindingFlags.Public | BindingFlags.Instance);
                _raycastHitPointProperty = raycastHitType.GetProperty("point", BindingFlags.Public | BindingFlags.Instance);
                _raycastHitNormalField = raycastHitType.GetField("normal", BindingFlags.Public | BindingFlags.Instance);
                _raycastHitNormalProperty = raycastHitType.GetProperty("normal", BindingFlags.Public | BindingFlags.Instance);
            }

            _physicsBindingsResolved = true;
            return true;
        }

        private float GetRaycastHitDistance(object hit)
        {
            if (hit == null) return float.MaxValue;

            if (_raycastHitDistanceField != null)
            {
                object value = _raycastHitDistanceField.GetValue(hit);
                if (value is float f) return f;
            }

            if (_raycastHitDistanceProperty != null)
            {
                object value = _raycastHitDistanceProperty.GetValue(hit, null);
                if (value is float f) return f;
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

        private Vector3 GetRaycastHitPoint(object hit)
        {
            if (hit == null) return default;

            if (_raycastHitPointField != null)
            {
                object value = _raycastHitPointField.GetValue(hit);
                if (value is Vector3 point) return point;
            }

            if (_raycastHitPointProperty != null)
            {
                object value = _raycastHitPointProperty.GetValue(hit, null);
                if (value is Vector3 point) return point;
            }

            object byName = GetMemberValue(hit, "point");
            return byName is Vector3 byNamePoint ? byNamePoint : default;
        }

        private Vector3 GetRaycastHitNormal(object hit)
        {
            if (hit == null) return Vector3.forward;

            if (_raycastHitNormalField != null)
            {
                object value = _raycastHitNormalField.GetValue(hit);
                if (value is Vector3 normal) return normal;
            }

            if (_raycastHitNormalProperty != null)
            {
                object value = _raycastHitNormalProperty.GetValue(hit, null);
                if (value is Vector3 normal) return normal;
            }

            object byName = GetMemberValue(hit, "normal");
            return byName is Vector3 byNameNormal ? byNameNormal : Vector3.forward;
        }

        private static Vector3 GetColliderClosestPoint(object colliderObj, Vector3 worldPoint)
        {
            if (colliderObj is Component colliderComponent)
            {
                MethodInfo closestPointMethod = colliderComponent.GetType().GetMethod("ClosestPoint", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(Vector3) }, null);
                if (closestPointMethod != null)
                {
                    object value = closestPointMethod.Invoke(colliderComponent, new object[] { worldPoint });
                    if (value is Vector3 closestPoint)
                    {
                        return closestPoint;
                    }
                }

                return colliderComponent.transform.position;
            }

            return worldPoint;
        }

        private void EnsureControllerIconVisual(GameObject vrRig)
        {
            if (_controllerIconObject != null) return;

            _controllerIconObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _controllerIconObject.name = ControllerIconObjectName;
            _controllerIconObject.transform.SetParent(vrRig.transform, true);
            SetLayerRecursively(_controllerIconObject, vrRig.layer);
            _controllerIconObject.transform.localScale = Vector3.one * ControllerIconScaleMeters;

            object collider = _controllerIconObject.GetComponent("Collider");
            if (collider is UnityEngine.Object colliderObj) UnityEngine.Object.Destroy(colliderObj);

            Renderer iconRenderer = _controllerIconObject.GetComponent<Renderer>();
            if (iconRenderer != null)
            {
                iconRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                iconRenderer.receiveShadows = false;
                Shader iconShader = Shader.Find("Sprites/Default");
                if (iconShader == null) iconShader = Shader.Find("Unlit/Transparent");
                if (iconShader == null) iconShader = Shader.Find("Unlit/Texture");
                _controllerIconMaterial = new Material(iconShader);
                iconRenderer.sharedMaterial = _controllerIconMaterial;
            }

            if (_fixedTouchIconTexture == null)
            {
                _fixedTouchIconTexture = CreateFixedTouchIconTexture();
            }

            SetControllerIconVisible(false);
        }

        private void UpdateControllerIconVisual(Texture2D texture, Vector3 handWorldPos, Quaternion handWorldRot)
        {
            if (_controllerIconObject == null)
            {
                return;
            }

            if (texture == null || _controllerIconMaterial == null)
            {
                SetControllerIconVisible(false);
                return;
            }

            if (!ReferenceEquals(_controllerIconTexture, texture))
            {
                _controllerIconTexture = texture;
                _controllerIconMaterial.mainTexture = texture;
            }

            Vector3 iconWorldPos = handWorldPos + handWorldRot * new Vector3(ControllerIconRightOffsetMeters, ControllerIconUpOffsetMeters, 0f);
            _controllerIconObject.transform.position = iconWorldPos;
            _controllerIconObject.transform.localScale = Vector3.one * ControllerIconScaleMeters;

            if (_mainCamera != null)
            {
                Vector3 forward = _mainCamera.transform.position - iconWorldPos;
                if (forward.sqrMagnitude > 0.000001f)
                {
                    _controllerIconObject.transform.rotation = Quaternion.LookRotation(forward.normalized, _mainCamera.transform.up);
                }
            }
            else
            {
                _controllerIconObject.transform.rotation = handWorldRot;
            }

            SetControllerIconVisible(true);
        }

        private void SetControllerIconVisible(bool visible)
        {
            if (_controllerIconObject != null && _controllerIconObject.activeSelf != visible)
            {
                _controllerIconObject.SetActive(visible);
            }
        }

        private static Texture2D CreateFixedTouchIconTexture()
        {
            const int size = 64;
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "OpenXR_UITouchFixedIcon"
            };

            Color32 clear = new(0, 0, 0, 0);
            Color32 fill = new(255, 245, 170, 235);
            Color32 edge = new(255, 255, 255, 255);
            int center = size / 2;
            float outer = size * 0.45f;
            float inner = size * 0.34f;

            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center + 0.5f;
                    float dy = y - center + 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    Color32 color = clear;
                    if (d <= outer)
                    {
                        color = d >= inner ? edge : fill;
                    }

                    pixels[y * size + x] = color;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static GameObject ExtractGameObjectFromCollider(object colliderObj)
        {
            if (colliderObj is Component colliderComponent)
            {
                return colliderComponent.gameObject;
            }

            if (colliderObj == null) return null;
            PropertyInfo gameObjectProperty = colliderObj.GetType().GetProperty("gameObject", BindingFlags.Public | BindingFlags.Instance);
            return gameObjectProperty?.GetValue(colliderObj, null) as GameObject;
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName)) return null;

            Type type = instance.GetType();
            FieldInfo field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) return field.GetValue(instance);

            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return property?.GetValue(instance, null);
        }

        private static bool TryMapUiRayColliderToIcon(string colliderName, out string icon)
        {
            switch (colliderName)
            {
                case "HS_anaruA00_Coll":
                    icon = "\u30A2\u30CA\u30EB";
                    return true;
                case "BP_Breast_R01_Z_Coll":
                case "milkR_(Z)_Point":
                    icon = "\u80F8RZ";
                    return true;
                case "BP_Breast_L01_Z_Coll":
                case "milkL_(Z)_Point":
                    icon = "\u80F8LZ";
                    return true;
                case "HS_pussy00_Coll":
                    icon = "\u30DE\u30F3\u30B3";
                    return true;
                case "HS_siriL00_Coll":
                    icon = "\u5C3BL";
                    return true;
                case "HS_siriR00_Coll":
                    icon = "\u5C3BR";
                    return true;
                default:
                    icon = string.Empty;
                    return false;
            }
        }

        private bool TryInvokeZngButtonDown(string icon)
        {
            if (string.IsNullOrEmpty(icon)) return false;
            if (!TryResolveZngButtonDown(out object zngController, out MethodInfo buttonDownMethod)) return false;

            try
            {
                buttonDownMethod.Invoke(zngController, new object[] { icon });
                return true;
            }
            catch (Exception ex)
            {
                _zngControllerInstance = null;
                VRModCore.LogWarning($"[UI][OpenXR] Failed invoking Zng_Controller.ButtonDown0('{icon}'): {ex.Message}");
                return false;
            }
        }

        private bool TryResolveZngButtonDown(out object zngController, out MethodInfo buttonDownMethod)
        {
            zngController = null;
            buttonDownMethod = null;

            if (_zngControllerType == null)
            {
                _zngControllerType = ResolveTypeAnyAssembly("Zng_Controller");
                if (_zngControllerType == null) return false;
            }

            if (_zngButtonDownMethod == null)
            {
                _zngButtonDownMethod = _zngControllerType.GetMethod("ButtonDown0", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
                if (_zngButtonDownMethod == null) return false;
            }

            if (_zngControllerInstance == null)
            {
                if (Time.time < _nextZngControllerLookupTime) return false;

                _nextZngControllerLookupTime = Time.time + 0.5f;
                _zngControllerInstance = UnityEngine.Object.FindObjectOfType(_zngControllerType);
                if (_zngControllerInstance == null) return false;
            }

            zngController = _zngControllerInstance;
            buttonDownMethod = _zngButtonDownMethod;
            return true;
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

        private void EnsureRayVisuals(GameObject vrRig)
        {
            if (_rayLine != null && _cursorObject != null) return;

            _rayObject = new GameObject(RayObjectName);
            _rayObject.transform.SetParent(vrRig.transform, false);
            SetLayerRecursively(_rayObject, vrRig.layer);
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
            SetLayerRecursively(_cursorObject, vrRig.layer);
            _cursorObject.transform.localScale = Vector3.one * (CursorScaleMeters * GetUiPanelScaleMultiplier());

            object collider = _cursorObject.GetComponent("Collider");
            if (collider is UnityEngine.Object colliderObj) UnityEngine.Object.Destroy(colliderObj);

            Renderer cursorRenderer = _cursorObject.GetComponent<Renderer>();
            if (cursorRenderer != null)
            {
                cursorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                cursorRenderer.receiveShadows = false;
                Shader cursorShader = Shader.Find("Sprites/Default");
                if (cursorShader == null) cursorShader = Shader.Find("Unlit/Color");
                _cursorMaterial = new Material(cursorShader);
                _cursorMaterial.color = RayVisibleColor;
                cursorRenderer.sharedMaterial = _cursorMaterial;
            }

            SetRayVisible(false);
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null) return;

            root.layer = layer;
            Transform tr = root.transform;
            for (int i = 0; i < tr.childCount; i++)
            {
                SetLayerRecursively(tr.GetChild(i).gameObject, layer);
            }
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
            float configuredScale = ConfigManager.OpenXR_UiPanelScale?.Value ?? 1.0f;
            return Mathf.Clamp(configuredScale, MinPanelScale, MaxPanelScale);
        }

        private void DestroyRayVisuals()
        {
            if (_rayObject != null) UnityEngine.Object.Destroy(_rayObject);
            if (_cursorObject != null) UnityEngine.Object.Destroy(_cursorObject);
            if (_rayMaterial != null) UnityEngine.Object.Destroy(_rayMaterial);
            if (_cursorMaterial != null) UnityEngine.Object.Destroy(_cursorMaterial);
            if (_controllerIconObject != null) UnityEngine.Object.Destroy(_controllerIconObject);
            if (_controllerIconMaterial != null) UnityEngine.Object.Destroy(_controllerIconMaterial);
            if (_fixedTouchIconTexture != null) UnityEngine.Object.Destroy(_fixedTouchIconTexture);

            _rayObject = null;
            _rayLine = null;
            _rayMaterial = null;
            _cursorObject = null;
            _cursorMaterial = null;
            _controllerIconObject = null;
            _controllerIconMaterial = null;
            _controllerIconTexture = null;
            _fixedTouchIconTexture = null;
        }

        private static bool TryGetProjectionPlaneUv(GameObject surface, Vector3 hitWorldPoint, out Vector2 uv)
        {
            uv = default;
            if (surface == null) return false;

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

        private static bool IsWithinProjectionPlaneVisualBounds(GameObject surface, Vector3 hitWorldPoint)
        {
            if (surface == null) return false;

            Vector3 local = surface.transform.InverseTransformPoint(hitWorldPoint);
            float visualHalf = 0.5f + UiSurfaceVisualEdgeExpandNormalized + UiSurfaceBoundsEpsilon;
            return local.x >= -visualHalf && local.x <= visualHalf &&
                   local.y >= -visualHalf && local.y <= visualHalf;
        }

        private void ReleasePointerIfNeeded()
        {
            if (!_pointerIsDown) return;

            if (IsWindowsPlatform)
            {
                _ = TrySendMouseLeftEvent(false, out _);
            }

            _pointerIsDown = false;
        }

        private bool TryInjectMousePointerPosition(Vector2 pointerScreenPos)
        {
            if (!IsWindowsPlatform)
            {
                if (!_loggedMouseInjectionUnsupportedPlatform)
                {
                    _loggedMouseInjectionUnsupportedPlatform = true;
                    VRModCore.LogWarning("[UI][OpenXR][Mouse] Mouse pointer injection is only supported on Windows.");
                }

                return false;
            }

            if (!TryGetMouseInjectionWindowHandle(out IntPtr windowHandle))
            {
                if (!_loggedMouseInjectionWindowMissing)
                {
                    _loggedMouseInjectionWindowMissing = true;
                    VRModCore.LogWarning("[UI][OpenXR][Mouse] Failed to resolve game window handle for pointer injection.");
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
            [FieldOffset(0)] public MOUSEINPUT mi;
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
    }
}
#endif
