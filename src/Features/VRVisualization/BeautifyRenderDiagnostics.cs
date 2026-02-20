#if MONO
using HarmonyLib;
using UnityVRMod.Config;
using UnityVRMod.Core;
using System.Text;

namespace UnityVRMod.Features.VrVisualization
{
    internal static class BeautifyRenderDiagnostics
    {
        private const string HarmonyId = "com.newunitymodder.unityvrmod.beautifydiag";
        private const float RepeatLogIntervalSeconds = 5f;

        private static Harmony _harmony;
        private static bool _installed;
        private static bool _installFailed;
        private static readonly Dictionary<int, string> LastSignatureByInstance = [];
        private static readonly Dictionary<int, float> NextLogTimeByInstance = [];

        public static void EnsureInstalled()
        {
            if (_installed || _installFailed) return;

            try
            {
                Type beautifyType = ResolveTypeAnyAssembly("BeautifyEffect.Beautify") ?? ResolveTypeAnyAssembly("Beautify");
                if (beautifyType == null)
                {
                    _installFailed = true;
                    VRModCore.LogWarning("[PostFX][BeautifyDiag] Beautify type not found; diagnostics patch not installed.");
                    return;
                }

                MethodInfo onRenderImage = beautifyType.GetMethod(
                    "OnRenderImage",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    [typeof(RenderTexture), typeof(RenderTexture)],
                    null);

                if (onRenderImage == null)
                {
                    _installFailed = true;
                    VRModCore.LogWarning($"[PostFX][BeautifyDiag] OnRenderImage not found on '{beautifyType.FullName}'.");
                    return;
                }

                MethodInfo prefix = typeof(BeautifyRenderDiagnostics).GetMethod(
                    nameof(OnBeautifyOnRenderImagePrefix),
                    BindingFlags.Static | BindingFlags.NonPublic);

                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(onRenderImage, prefix: new HarmonyMethod(prefix));
                _installed = true;
                VRModCore.LogRuntimeDebug($"[PostFX][BeautifyDiag] Installed hook on {beautifyType.FullName}.OnRenderImage");
            }
            catch (Exception ex)
            {
                _installFailed = true;
                VRModCore.LogWarning($"[PostFX][BeautifyDiag] Failed to install diagnostics patch: {ex.Message}");
            }
        }

        private static void OnBeautifyOnRenderImagePrefix(object __instance, RenderTexture source, RenderTexture destination)
        {
            if (!(ConfigManager.EnableRuntimeDebugLogging?.Value ?? false)) return;
            if (__instance == null) return;

            Component component = __instance as Component;
            Camera camera = component != null ? component.GetComponent<Camera>() : null;
            string cameraName = camera != null ? camera.name : "null";
            int instanceId = component != null ? component.GetInstanceID() : __instance.GetHashCode();
            string tonemap = ReadTonemapValue(__instance);
            string sourceDesc = DescribeRenderTexture(source);
            string destinationDesc = DescribeRenderTexture(destination);
            string signature = $"{cameraName}|tone={tonemap}|src={sourceDesc}|dst={destinationDesc}";

            bool changed = !LastSignatureByInstance.TryGetValue(instanceId, out string previousSignature) ||
                           !string.Equals(previousSignature, signature, StringComparison.Ordinal);
            float now = Time.unscaledTime;
            bool periodicDue = !NextLogTimeByInstance.TryGetValue(instanceId, out float nextTime) || now >= nextTime;
            if (!changed && !periodicDue)
            {
                return;
            }

            LastSignatureByInstance[instanceId] = signature;
            NextLogTimeByInstance[instanceId] = now + RepeatLogIntervalSeconds;

            string objectPath = component != null ? BuildTransformPath(component.transform) : "unknown";
            VRModCore.LogRuntimeDebug(
                $"[PostFX][BeautifyDiag] cam='{cameraName}', path='{objectPath}', tonemap='{tonemap}', source={sourceDesc}, destination={destinationDesc}");
        }

        private static string ReadTonemapValue(object instance)
        {
            if (instance == null) return "n/a";
            Type type = instance.GetType();

            try
            {
                PropertyInfo tonemapProperty = type.GetProperty("tonemap", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (tonemapProperty != null && tonemapProperty.CanRead)
                {
                    object value = tonemapProperty.GetValue(instance, null);
                    if (value != null) return value.ToString();
                }
            }
            catch
            {
            }

            try
            {
                FieldInfo tonemapField = type.GetField("_tonemap", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (tonemapField != null)
                {
                    object value = tonemapField.GetValue(instance);
                    if (value != null) return value.ToString();
                }
            }
            catch
            {
            }

            return "unknown";
        }

        private static string DescribeRenderTexture(RenderTexture texture)
        {
            if (texture == null) return "null(backbuffer)";

            return
                $"name='{texture.name}', size={texture.width}x{texture.height}, gfx={texture.graphicsFormat}, rt={texture.format}, sRGB={(texture.sRGB ? 1 : 0)}, msaa={texture.antiAliasing}";
        }

        private static string BuildTransformPath(Transform transform)
        {
            if (transform == null) return string.Empty;

            StringBuilder sb = new();
            Transform current = transform;
            while (current != null)
            {
                if (sb.Length == 0)
                {
                    sb.Insert(0, current.name);
                }
                else
                {
                    sb.Insert(0, '/');
                    sb.Insert(0, current.name);
                }

                current = current.parent;
            }

            return sb.ToString();
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
    }
}
#endif
