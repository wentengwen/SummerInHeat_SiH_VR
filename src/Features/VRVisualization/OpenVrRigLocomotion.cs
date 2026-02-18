#if OPENVR_BUILD
using System.Reflection;
using System.Runtime.InteropServices;
using UnityVRMod.Config;
using UnityVRMod.Core;
using UnityVRMod.Features.VRVisualization.OpenVR;

namespace UnityVRMod.Features.VrVisualization
{
    internal sealed class OpenVrRigLocomotion
    {
        private const float SnapTurnDegrees = 30f;
        private const float SnapTurnThreshold = 0.65f;
        private const float SnapTurnReleaseThreshold = 0.25f;
        private const float SnapTurnCooldownSeconds = 0.25f;

        private const float TeleportMaxDistanceMeters = 12f;
        private const float TeleportMinRayYAbs = 0.08f;
        private const float TeleportMarkerScale = 0.12f;
        private const float DefaultInitialEyeHeightAboveGroundMeters = 1.65f;
        private const float GroundPlaneRefreshIntervalSeconds = 1.5f;
        private const string GroundLayerName = "BG";
        private const float DefaultGripDragSensitivity = 0.45f;
        private const float GripDragDeadzoneMeters = 0.002f;
        private const float GripDragMaxStepMeters = 0.20f;
        private const float GripReleaseGraceSeconds = 0.08f;
        private const float GripDeltaSmoothing = 0.35f;
        private static bool GripDragLockY = false;
        private static bool GripDragInvertDirection = true;

        private static readonly string[] GroundNamesExact =
        [
            "floor",
            "ground",
            "tatami"
        ];

        private bool _snapTurnLatched;
        private float _nextSnapTurnTime;
        private bool _lastTriggerPressed;
        private GameObject _teleportMarker;
        private Material _teleportMarkerMaterial;
        private bool _isGripDragging;
        private Vector3 _lastGripControllerTrackingLocalPos;
        private Vector3 _smoothedGripDelta;
        private float _lastGripPressedTime;

        private bool _hasGroundPlaneY;
        private float _groundPlaneY;
        private string _groundObjectName = string.Empty;
        private float _nextGroundProbeTime;
        private bool _hasLoggedMissingGround;
        private bool _hasAppliedInitialGroundHeight;

        private static readonly Color TeleportValidColor = new(0.1f, 0.95f, 0.2f, 0.85f);
        private static readonly Color TeleportInvalidColor = new(0.95f, 0.2f, 0.1f, 0.7f);

        public void Update(CVRSystem hmd, GameObject vrRig, TrackedDevicePose_t[] trackedPoses)
        {
            if (hmd == null || vrRig == null || trackedPoses == null) return;

            if (!TryReadRightControllerState(hmd, out uint rightDeviceIndex, out VRControllerState_t rightState))
            {
                SetTeleportMarkerVisible(false);
                _lastTriggerPressed = false;
                _snapTurnLatched = false;
                _isGripDragging = false;
                return;
            }

            UpdateSnapTurn(vrRig, trackedPoses, rightState.rAxis0.x);
            EnsureInitialHeightFromGround(vrRig, trackedPoses);

            bool gripPressed = IsButtonPressed(rightState.ulButtonPressed, EVRButtonId.k_EButton_Grip);
            UpdateGripDragTranslate(vrRig, trackedPoses, rightDeviceIndex, gripPressed);

            if (gripPressed)
            {
                SetTeleportMarkerVisible(false);
                _lastTriggerPressed = false;
                return;
            }

            bool hasTeleportTarget = TryGetTeleportTarget(vrRig, trackedPoses, rightDeviceIndex, out Vector3 teleportTargetGround);
            UpdateTeleportMarker(vrRig, hasTeleportTarget, teleportTargetGround);

            bool touchpadClickPressed = IsButtonPressed(rightState.ulButtonPressed, EVRButtonId.k_EButton_SteamVR_Touchpad);
            if (touchpadClickPressed && !_lastTriggerPressed && hasTeleportTarget)
            {
                PerformTeleport(vrRig, trackedPoses, teleportTargetGround);
            }
            _lastTriggerPressed = touchpadClickPressed;
        }

        private void UpdateGripDragTranslate(GameObject vrRig, TrackedDevicePose_t[] trackedPoses, uint rightDeviceIndex, bool gripPressed)
        {
            if (gripPressed)
            {
                _lastGripPressedTime = Time.time;
            }

            bool treatAsHeld = gripPressed || (_isGripDragging && (Time.time - _lastGripPressedTime) <= GripReleaseGraceSeconds);
            if (!treatAsHeld)
            {
                _isGripDragging = false;
                _smoothedGripDelta = Vector3.zero;
                return;
            }

            if (!TryGetControllerTrackingLocalPosition(trackedPoses, rightDeviceIndex, out Vector3 currentControllerTrackingLocalPos))
            {
                _isGripDragging = false;
                _smoothedGripDelta = Vector3.zero;
                return;
            }

            if (!_isGripDragging)
            {
                _isGripDragging = true;
                _lastGripControllerTrackingLocalPos = currentControllerTrackingLocalPos;
                _smoothedGripDelta = Vector3.zero;
                float dragSensitivity = GetGripDragSensitivity();
                VRModCore.Log($"[Locomotion] Grip drag start (sensitivity={dragSensitivity:F2}, invert={GripDragInvertDirection}, lockY={GripDragLockY})");
                return;
            }

            Vector3 rawControllerDeltaLocal = currentControllerTrackingLocalPos - _lastGripControllerTrackingLocalPos;
            _lastGripControllerTrackingLocalPos = currentControllerTrackingLocalPos;

            if (GripDragLockY)
            {
                rawControllerDeltaLocal.y = 0f;
            }

            if (GripDragInvertDirection)
            {
                rawControllerDeltaLocal = -rawControllerDeltaLocal;
            }

            float rawDeltaMagnitude = rawControllerDeltaLocal.magnitude;
            if (rawDeltaMagnitude < GripDragDeadzoneMeters)
            {
                return;
            }

            if (rawDeltaMagnitude > GripDragMaxStepMeters)
            {
                rawControllerDeltaLocal = rawControllerDeltaLocal / rawDeltaMagnitude * GripDragMaxStepMeters;
            }

            Vector3 smoothedControllerDeltaLocal = Vector3.Lerp(_smoothedGripDelta, rawControllerDeltaLocal, GripDeltaSmoothing);
            _smoothedGripDelta = smoothedControllerDeltaLocal;

            float sensitivity = GetGripDragSensitivity();
            if (sensitivity <= 0f)
            {
                return;
            }

            // Convert tracking-local delta to world translation without position feedback from rig movement.
            Vector3 controllerDeltaWorld = vrRig.transform.TransformVector(smoothedControllerDeltaLocal);
            vrRig.transform.position += controllerDeltaWorld * sensitivity;
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

        private void UpdateSnapTurn(GameObject vrRig, TrackedDevicePose_t[] trackedPoses, float rightStickX)
        {
            if (Mathf.Abs(rightStickX) <= SnapTurnReleaseThreshold)
            {
                _snapTurnLatched = false;
                return;
            }

            if (_snapTurnLatched || Time.time < _nextSnapTurnTime || Mathf.Abs(rightStickX) < SnapTurnThreshold)
            {
                return;
            }

            float turnAngle = rightStickX > 0f ? SnapTurnDegrees : -SnapTurnDegrees;
            RotateRigAroundHmd(vrRig, trackedPoses, turnAngle);

            _snapTurnLatched = true;
            _nextSnapTurnTime = Time.time + SnapTurnCooldownSeconds;

            VRModCore.Log($"[Locomotion] SnapTurn {turnAngle:F0} deg");
        }

        private static void RotateRigAroundHmd(GameObject vrRig, TrackedDevicePose_t[] trackedPoses, float turnAngle)
        {
            Vector3 pivotWorld = vrRig.transform.position;
            if (trackedPoses.Length > OpenVR.k_unTrackedDeviceIndex_Hmd &&
                trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd].bPoseIsValid)
            {
                Vector3 hmdLocalPos = GetPositionFromHmdMatrix(trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd].mDeviceToAbsoluteTracking);
                pivotWorld = vrRig.transform.TransformPoint(hmdLocalPos);
            }

            vrRig.transform.RotateAround(pivotWorld, Vector3.up, turnAngle);
        }

        private bool TryGetTeleportTarget(GameObject vrRig, TrackedDevicePose_t[] trackedPoses, uint rightDeviceIndex, out Vector3 targetWorld)
        {
            targetWorld = default;

            if (rightDeviceIndex >= trackedPoses.Length || !trackedPoses[rightDeviceIndex].bPoseIsValid)
            {
                return false;
            }

            if (!TryGetGroundPlaneY(vrRig, out float groundPlaneY))
            {
                return false;
            }

            HmdMatrix34_t rightPose = trackedPoses[rightDeviceIndex].mDeviceToAbsoluteTracking;
            Vector3 rightLocalPos = GetPositionFromHmdMatrix(rightPose);
            Quaternion rightLocalRot = GetRotationFromHmdMatrix(rightPose);

            Vector3 originWorld = vrRig.transform.TransformPoint(rightLocalPos);
            Vector3 directionWorld = vrRig.transform.TransformDirection(rightLocalRot * Vector3.forward).normalized;

            if (Mathf.Abs(directionWorld.y) < TeleportMinRayYAbs)
            {
                return false;
            }

            float t = (groundPlaneY - originWorld.y) / directionWorld.y;
            if (t <= 0f || t > TeleportMaxDistanceMeters)
            {
                return false;
            }

            targetWorld = originWorld + directionWorld * t;
            targetWorld.y = groundPlaneY;
            return true;
        }

        private bool TryGetGroundPlaneY(GameObject vrRig, out float groundPlaneY)
        {
            groundPlaneY = 0f;

            if (Time.time >= _nextGroundProbeTime)
            {
                _nextGroundProbeTime = Time.time + GroundPlaneRefreshIntervalSeconds;

                if (TryFindGroundPlaneY(vrRig.transform.position.y, out float foundY, out string foundName))
                {
                    bool changed = !_hasGroundPlaneY ||
                                   Mathf.Abs(foundY - _groundPlaneY) > 0.01f ||
                                   !string.Equals(foundName, _groundObjectName, StringComparison.Ordinal);

                    _groundPlaneY = foundY;
                    _groundObjectName = foundName;
                    _hasGroundPlaneY = true;
                    _hasLoggedMissingGround = false;

                    if (changed)
                    {
                        VRModCore.Log($"[Locomotion] Ground plane source='{_groundObjectName}' y={_groundPlaneY:F2}");
                    }
                }
                else if (!_hasGroundPlaneY && !_hasLoggedMissingGround)
                {
                    VRModCore.LogWarning("[Locomotion] No ground object found (Layer=BG, name exact match: floor/ground/tatami).");
                    _hasLoggedMissingGround = true;
                }
            }

            if (!_hasGroundPlaneY)
            {
                return false;
            }

            groundPlaneY = _groundPlaneY;
            return true;
        }

        private static bool TryFindGroundPlaneY(float referenceY, out float groundY, out string groundName)
        {
            groundY = 0f;
            groundName = string.Empty;

            GameObject[] objects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            int bgLayer = LayerMask.NameToLayer(GroundLayerName);
            if (bgLayer < 0) return false;

            bool hasCandidate = false;
            float bestDistance = float.MaxValue;
            float bestY = 0f;
            string bestName = string.Empty;

            foreach (GameObject go in objects)
            {
                if (go == null || !go.activeInHierarchy) continue;
                if (go.layer != bgLayer) continue;
                if (!IsGroundNameCandidate(go.name)) continue;

                float candidateY;
                if (!TryGetColliderTopY(go, out candidateY))
                {
                    candidateY = go.transform.position.y;
                }

                float distance = Mathf.Abs(candidateY - referenceY);
                if (!hasCandidate || distance < bestDistance)
                {
                    bestDistance = distance;
                    bestY = candidateY;
                    bestName = go.name;
                    hasCandidate = true;
                }
            }

            if (!hasCandidate) return false;

            groundY = bestY;
            groundName = bestName;
            return true;
        }

        private static bool IsGroundNameCandidate(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName)) return false;

            string trimmedName = objectName.Trim();
            foreach (string candidate in GroundNamesExact)
            {
                if (string.Equals(trimmedName, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetControllerTrackingLocalPosition(TrackedDevicePose_t[] trackedPoses, uint deviceIndex, out Vector3 positionLocal)
        {
            positionLocal = default;

            if (deviceIndex >= trackedPoses.Length || !trackedPoses[deviceIndex].bPoseIsValid)
            {
                return false;
            }

            positionLocal = GetPositionFromHmdMatrix(trackedPoses[deviceIndex].mDeviceToAbsoluteTracking);
            return true;
        }

        private static bool TryGetColliderTopY(GameObject go, out float y)
        {
            y = 0f;
            if (go == null) return false;

            object collider = go.GetComponent("Collider");
            if (collider == null) return false;

            PropertyInfo boundsProp = collider.GetType().GetProperty("bounds", BindingFlags.Public | BindingFlags.Instance);
            if (boundsProp == null) return false;

            object boundsObj = boundsProp.GetValue(collider);
            if (boundsObj == null) return false;

            PropertyInfo maxProp = boundsObj.GetType().GetProperty("max", BindingFlags.Public | BindingFlags.Instance);
            if (maxProp != null)
            {
                object maxObj = maxProp.GetValue(boundsObj);
                if (maxObj is Vector3 maxVec)
                {
                    y = maxVec.y;
                    return true;
                }
            }

            return false;
        }

        private void PerformTeleport(GameObject vrRig, TrackedDevicePose_t[] trackedPoses, Vector3 targetGroundWorld)
        {
            if (TryGetCurrentHmdWorldPose(vrRig, trackedPoses, out Vector3 hmdWorldPos, out Vector3 hmdOffsetFromRig))
            {
                Vector3 desiredHmdWorld = new(targetGroundWorld.x, hmdWorldPos.y, targetGroundWorld.z);
                vrRig.transform.position = desiredHmdWorld - hmdOffsetFromRig;
                VRModCore.Log($"[Locomotion] Teleport -> Ground({targetGroundWorld.x:F2}, {targetGroundWorld.y:F2}, {targetGroundWorld.z:F2}) KeepEyeY={desiredHmdWorld.y:F2}");
                return;
            }

            Vector3 fallbackRigPos = vrRig.transform.position;
            vrRig.transform.position = new Vector3(targetGroundWorld.x, fallbackRigPos.y, targetGroundWorld.z);
            VRModCore.LogWarning("[Locomotion] Teleport fallback: HMD pose invalid, keeping rig Y.");
        }

        private void EnsureInitialHeightFromGround(GameObject vrRig, TrackedDevicePose_t[] trackedPoses)
        {
            if (_hasAppliedInitialGroundHeight) return;
            if (!TryGetGroundPlaneY(vrRig, out float groundY)) return;
            if (!TryGetCurrentHmdWorldPose(vrRig, trackedPoses, out Vector3 hmdWorldPos, out _)) return;

            float targetHmdY = groundY + GetInitialEyeHeightAboveGroundMeters();
            float deltaY = targetHmdY - hmdWorldPos.y;
            if (Mathf.Abs(deltaY) > 0.001f)
            {
                vrRig.transform.position += new Vector3(0f, deltaY, 0f);
            }

            _hasAppliedInitialGroundHeight = true;
            VRModCore.Log($"[Locomotion] Initial height aligned: groundY={groundY:F2}, eyeY={targetHmdY:F2}");
        }

        private static bool TryGetCurrentHmdWorldPose(GameObject vrRig, TrackedDevicePose_t[] trackedPoses, out Vector3 hmdWorldPos, out Vector3 hmdOffsetFromRig)
        {
            hmdWorldPos = default;
            hmdOffsetFromRig = default;

            if (trackedPoses.Length <= OpenVR.k_unTrackedDeviceIndex_Hmd || !trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd].bPoseIsValid)
            {
                return false;
            }

            Vector3 hmdLocalPos = GetPositionFromHmdMatrix(trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd].mDeviceToAbsoluteTracking);
            hmdWorldPos = vrRig.transform.TransformPoint(hmdLocalPos);
            hmdOffsetFromRig = hmdWorldPos - vrRig.transform.position;
            return true;
        }

        private static float GetInitialEyeHeightAboveGroundMeters()
        {
#if OPENVR_BUILD
            if (ConfigManager.OpenVR_InitialEyeHeightAboveGroundMeters != null)
            {
                return Mathf.Clamp(ConfigManager.OpenVR_InitialEyeHeightAboveGroundMeters.Value, 0.20f, 3.00f);
            }
#endif
            return DefaultInitialEyeHeightAboveGroundMeters;
        }

        private static float GetGripDragSensitivity()
        {
#if OPENVR_BUILD
            if (ConfigManager.OpenVR_GripDragSensitivity != null)
            {
                return Mathf.Clamp(ConfigManager.OpenVR_GripDragSensitivity.Value, 0.00f, 3.00f);
            }
#endif
            return DefaultGripDragSensitivity;
        }

        private void UpdateTeleportMarker(GameObject vrRig, bool hasTeleportTarget, Vector3 targetWorld)
        {
            EnsureTeleportMarker(vrRig);
            if (_teleportMarker == null)
            {
                return;
            }

            _teleportMarker.SetActive(true);
            _teleportMarker.transform.position = targetWorld;
            _teleportMarker.transform.localScale = Vector3.one * TeleportMarkerScale;

            if (_teleportMarkerMaterial != null)
            {
                _teleportMarkerMaterial.color = hasTeleportTarget ? TeleportValidColor : TeleportInvalidColor;
            }

            if (!hasTeleportTarget)
            {
                _teleportMarker.transform.position = vrRig.transform.position + Vector3.up * 0.02f;
            }
        }

        private void EnsureTeleportMarker(GameObject vrRig)
        {
            if (_teleportMarker != null) return;

            _teleportMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _teleportMarker.name = "OpenVR_TeleportTargetMarker";
            _teleportMarker.transform.SetParent(vrRig.transform, true);

            var collider = _teleportMarker.GetComponent("Collider");
            if (collider != null) UnityEngine.Object.Destroy(collider);

            var renderer = _teleportMarker.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                _teleportMarkerMaterial = renderer.material;
                _teleportMarkerMaterial.color = TeleportInvalidColor;
            }
        }

        private void SetTeleportMarkerVisible(bool visible)
        {
            if (_teleportMarker != null && _teleportMarker.activeSelf != visible)
            {
                _teleportMarker.SetActive(visible);
            }
        }

        private static bool IsButtonPressed(ulong buttonMask, EVRButtonId buttonId)
        {
            int buttonBit = (int)buttonId;
            if (buttonBit < 0 || buttonBit > 63) return false;
            ulong bit = 1UL << buttonBit;
            return (buttonMask & bit) != 0;
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

        private static Vector3 GetPositionFromHmdMatrix(HmdMatrix34_t matrix)
        {
            return new Vector3(matrix.m3, matrix.m7, -matrix.m11);
        }
    }
}

#endif
