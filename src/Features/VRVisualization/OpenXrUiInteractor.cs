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
        private const string RayObjectName = "OpenXR_UIRay";
        private const string CursorObjectName = "OpenXR_UICursor";
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
        private bool _loggedMouseInjectionUnsupportedPlatform;
        private bool _loggedMouseInjectionWindowMissing;
        private GameObject _rayObject;
        private LineRenderer _rayLine;
        private Material _rayMaterial;
        private GameObject _cursorObject;
        private Material _cursorMaterial;
        private float _lastAppliedCursorScale;

        public void Initialize(Camera mainCamera)
        {
            _mainCamera = mainCamera;
            _uiSurface = null;
            _nextUiSurfaceLookupTime = 0f;
            _loggedUiSurfaceFallback = false;
            _wasTriggerPressed = false;
            _pointerIsDown = false;
            _loggedMouseInjectionUnsupportedPlatform = false;
            _loggedMouseInjectionWindowMissing = false;
            _lastAppliedCursorScale = -1f;
            DestroyRayVisuals();
        }

        public void Teardown()
        {
            ReleasePointerIfNeeded();
            _mainCamera = null;
            _uiSurface = null;
            _loggedUiSurfaceFallback = false;
            _wasTriggerPressed = false;
            _loggedMouseInjectionUnsupportedPlatform = false;
            _loggedMouseInjectionWindowMissing = false;
            _lastAppliedCursorScale = -1f;
            DestroyRayVisuals();
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

            if (!TryGetPointerScreenPosition(rayOrigin, rayDirection.normalized, out Vector2 pointerScreenPos, out Vector3 rayEndPoint, out bool hasVisualTarget))
            {
                HandleNoPointerFrame(triggerPressed);
                // Keep ray visible on the frame/edge ring, but do not inject UI input.
                UpdateRayVisual(rayOrigin, rayEndPoint, hasTarget: hasVisualTarget);
                _wasTriggerPressed = triggerPressed;
                return;
            }

            _ = TryInjectMousePointerPosition(pointerScreenPos);
            bool justPressed = triggerPressed && !_wasTriggerPressed;
            bool justReleased = !triggerPressed && _wasTriggerPressed;

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

            _rayObject = null;
            _rayLine = null;
            _rayMaterial = null;
            _cursorObject = null;
            _cursorMaterial = null;
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
