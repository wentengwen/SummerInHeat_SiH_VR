using System.Globalization;
using UnityVRMod.Core;

namespace UnityVRMod.Features.Util
{
    /// <summary>
    /// A simple read-only struct to hold parsed pose override data.
    /// Uses float.NaN to differentiate between a user-set value and an unset '~' value.
    /// </summary>
    public readonly struct PoseOverride(Vector3 position, Vector3 rotation)
    {
        public readonly Vector3 Position = position;
        public readonly Vector3 Rotation = rotation; // Stored as Euler angles
    }

    /// <summary>
    /// A helper class to handle parsing the scene pose override config string.
    /// </summary>
    public static class PoseParser
    {
        public static Dictionary<string, PoseOverride> Parse(string configValue)
        {
            var poseOverrides = new Dictionary<string, PoseOverride>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(configValue))
                return poseOverrides;

            string[] entries = configValue.Split(';');
            foreach (string entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;

                string[] parts = entry.Split('|');
                if (parts.Length < 2 || parts.Length > 3)
                {
                    VRModCore.LogWarning($"Invalid ScenePoseOverrides entry format. Expected 'Scene|Pos' or 'Scene|Pos|Rot', got '{entry}'.");
                    continue;
                }

                string sceneName = parts[0].Trim();
                if (string.IsNullOrEmpty(sceneName))
                {
                    VRModCore.LogWarning("ScenePoseOverrides entry is missing a mandatory scene name.");
                    continue;
                }

                Vector3 position = ParseVector(parts[1]);
                Vector3 rotation = new(float.NaN, float.NaN, float.NaN); // Default to all '~'

                if (parts.Length == 3)
                {
                    rotation = ParseVector(parts[2]);
                }

                poseOverrides[sceneName] = new PoseOverride(position, rotation);
            }
            return poseOverrides;
        }

        private static Vector3 ParseVector(string vectorString)
        {
            string[] components = vectorString.Trim().Split(' ');
            if (components.Length != 3)
            {
                VRModCore.LogWarning($"Invalid vector format in ScenePoseOverrides. Expected 'X Y Z', got '{vectorString}'. Defaulting to original values.");
                return new Vector3(float.NaN, float.NaN, float.NaN);
            }

            return new Vector3(
                ParseComponent(components[0]),
                ParseComponent(components[1]),
                ParseComponent(components[2])
            );
        }

        private static float ParseComponent(string component)
        {
            if (component.Trim() == "~")
            {
                // Use a special value to indicate 'use original'. We'll use float.NaN.
                return float.NaN;
            }
            if (float.TryParse(component, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
            {
                return result;
            }
            VRModCore.LogWarning($"Could not parse component '{component}' in ScenePoseOverrides. Defaulting to original value.");
            return float.NaN; // Default to '~' if parsing fails
        }
    }
}