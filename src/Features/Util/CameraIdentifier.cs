namespace UnityVRMod.Features.Util
{
    /// <summary>
    /// A simple read-only struct to hold the parsed data for a single camera override.
    /// </summary>
    public readonly struct CameraIdentifier(string path, string scene)
    {
        public readonly string Path = path;
        public readonly string Scene = scene;
    }

    /// <summary>
    /// A helper class to handle parsing and stringifying the camera override config string.
    /// This allows for easy conversion between the string representation in the config file
    /// and a structured list for use in the mod's logic.
    /// </summary>
    public static class CameraIdentifierHelper
    {
        private const char EntrySeparator = ';';
        private const char ScenePathSeparator = '|';

        /// <summary>
        /// Parses the config string into a list of CameraIdentifier objects.
        /// </summary>
        /// <param name="configValue">The raw string from the config file.</param>
        /// <returns>A list of parsed CameraIdentifier objects.</returns>
        public static List<CameraIdentifier> Parse(string configValue)
        {
            var identifiers = new List<CameraIdentifier>();
            if (string.IsNullOrWhiteSpace(configValue))
                return identifiers;

            string[] entries = configValue.Split([EntrySeparator], StringSplitOptions.RemoveEmptyEntries);
            foreach (string entry in entries)
            {
                string[] parts = entry.Split(ScenePathSeparator);
                if (parts.Length == 2)
                {
                    // Format: "SceneName|GameObjectPath"
                    identifiers.Add(new CameraIdentifier(parts[1].Trim(), parts[0].Trim()));
                }
                else if (parts.Length == 1)
                {
                    // Format: "GameObjectPath" (applies to any scene)
                    identifiers.Add(new CameraIdentifier(parts[0].Trim(), string.Empty));
                }
            }
            return identifiers;
        }

        /// <summary>
        /// Converts a list of CameraIdentifier objects back into a config file string.
        /// </summary>
        /// <param name="identifiers">The list of CameraIdentifier objects.</param>
        /// <returns>A formatted string suitable for saving to the config file.</returns>
        public static string Stringify(List<CameraIdentifier> identifiers)
        {
            if (identifiers == null || identifiers.Count == 0)
                return string.Empty;

            var entries = new List<string>();
            foreach (var identifier in identifiers)
            {
                if (string.IsNullOrWhiteSpace(identifier.Path))
                    continue;

                if (!string.IsNullOrEmpty(identifier.Scene))
                {
                    entries.Add($"{identifier.Scene}{ScenePathSeparator}{identifier.Path}");
                }
                else
                {
                    entries.Add(identifier.Path);
                }
            }
            return string.Join($"{EntrySeparator} ", entries);
        }
    }
}