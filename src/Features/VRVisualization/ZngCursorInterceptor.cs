#if OPENXR_BUILD
#if MONO
using HarmonyLib;
using UnityVRMod.Core;
#endif

namespace UnityVRMod.Features.VrVisualization
{
#if MONO
    internal static class ZngCursorInterceptor
    {
        private const string HarmonyId = "com.newunitymodder.unityvrmod.zngcursorinterceptor";
        private const float ZngLookupIntervalSeconds = 0.5f;

        private static Harmony _harmony;
        private static bool _isInstalled;
        private static bool _installFailed;
        private static Type _zngControllerType;
        private static FieldInfo _zngCursorSetField;
        private static UnityEngine.Object _zngControllerInstance;
        private static float _nextZngLookupTime;
        private static Texture2D _latestCursorTexture;

        public static void EnsureInstalled()
        {
            if (_isInstalled || _installFailed) return;

            try
            {
                _harmony = new Harmony(HarmonyId);
                _harmony.PatchAll(typeof(ZngCursorInterceptor));
                _isInstalled = true;
            }
            catch (Exception ex)
            {
                _installFailed = true;
                VRModCore.LogWarning($"[UI][OpenXR] Failed to install cursor interceptor: {ex.Message}");
            }
        }

        public static Texture2D GetLatestCursorTexture()
        {
            return _latestCursorTexture;
        }

        private static void CaptureCursorTexture(Texture2D texture)
        {
            if (!IsZngCursorTexture(texture)) return;
            _latestCursorTexture = texture;
        }

        private static bool IsZngCursorTexture(Texture2D texture)
        {
            if (!TryResolveZngController(out object zngController)) return false;
            if (_zngCursorSetField == null)
            {
                _zngCursorSetField = zngController.GetType().GetField("CursorSet", BindingFlags.Public | BindingFlags.Instance);
                if (_zngCursorSetField == null) return false;
            }

            if (_zngCursorSetField.GetValue(zngController) is not Texture2D[] cursorSet || cursorSet.Length == 0)
            {
                return texture == null;
            }

            if (texture == null) return true;
            for (int i = 0; i < cursorSet.Length; i++)
            {
                if (ReferenceEquals(cursorSet[i], texture)) return true;
            }

            return false;
        }

        private static bool TryResolveZngController(out object controller)
        {
            controller = null;

            if (_zngControllerType == null)
            {
                _zngControllerType = ResolveTypeAnyAssembly("Zng_Controller");
                if (_zngControllerType == null) return false;
            }

            if (_zngControllerInstance == null)
            {
                if (Time.time < _nextZngLookupTime) return false;

                _nextZngLookupTime = Time.time + ZngLookupIntervalSeconds;
                _zngControllerInstance = UnityEngine.Object.FindObjectOfType(_zngControllerType);
                if (_zngControllerInstance == null) return false;
            }

            controller = _zngControllerInstance;
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

        [HarmonyPatch(typeof(Cursor))]
        private static class CursorSetCursorPatch
        {
            [HarmonyPatch(nameof(Cursor.SetCursor), typeof(Texture2D), typeof(Vector2), typeof(CursorMode))]
            [HarmonyPostfix]
            private static void Postfix(Texture2D texture)
            {
                CaptureCursorTexture(texture);
            }
        }
    }
#else
    internal static class ZngCursorInterceptor
    {
        public static void EnsureInstalled() { }

        public static Texture2D GetLatestCursorTexture()
        {
            return null;
        }
    }
#endif
}
#endif
