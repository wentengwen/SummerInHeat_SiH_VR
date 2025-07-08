using BepInEx.Configuration;
using UnityVRMod.Core;

namespace UnityVRMod.Config
{
    /// <summary>
    /// Handles configuration elements marked as 'internal' using a separate BepInEx ConfigFile.
    /// </summary>
    public class InternalConfigHandler : ConfigHandler
    {
        internal static string InternalConfigPath;
        private ConfigFile _internalConfigFile;

        private const string CONFIG_CATEGORY = "Internal";

        public override void Init()
        {
            InternalConfigPath = Path.Combine(BepInEx.Paths.ConfigPath, $"{VRModCore.MOD_NAME}.Internal.cfg");
            VRModCore.Log($"InternalConfigHandler initializing. Path: {InternalConfigPath}");

            try
            {
                _internalConfigFile = new ConfigFile(InternalConfigPath, true);
                LoadConfig();
                VRModCore.LogRuntimeDebug("InternalConfigHandler: ConfigFile instance created.");
            }
            catch (Exception ex)
            {
                VRModCore.LogError($"CRITICAL ERROR initializing internal ConfigFile at '{InternalConfigPath}':", ex);
                _internalConfigFile = null;
            }
        }

        public override void RegisterConfigElement<T>(ConfigElement<T> configElement)
        {
            if (_internalConfigFile == null)
            {
                VRModCore.LogError($"Cannot register internal config '{configElement.Name}': Internal ConfigFile is null!");
                return;
            }

            ConfigEntry<T> entry = _internalConfigFile.Bind(
                CONFIG_CATEGORY,
                configElement.Name,
                (T)configElement.DefaultValue,
                configElement.Description);

            configElement.Value = entry.Value;

            entry.SettingChanged += (sender, args) =>
            {
                configElement.Value = entry.Value;
            };
        }

        public override T GetConfigValue<T>(ConfigElement<T> element)
        {
            if (_internalConfigFile == null)
            {
                VRModCore.LogError($"Cannot get internal config '{element.Name}': Internal ConfigFile is null!");
                return element.Value;
            }

            if (_internalConfigFile.TryGetEntry(CONFIG_CATEGORY, element.Name, out ConfigEntry<T> configEntry))
            {
                return configEntry.Value;
            }
            else
            {
                VRModCore.LogRuntimeDebug($"Config entry '{element.Name}' not found in BIE store, returning cached value.");
                return element.Value;
            }
        }

        public override void SetConfigValue<T>(ConfigElement<T> element, T value)
        {
            if (_internalConfigFile == null)
            {
                VRModCore.LogError($"Cannot set internal config '{element.Name}': Internal ConfigFile is null!");
                return;
            }

            if (_internalConfigFile.TryGetEntry(CONFIG_CATEGORY, element.Name, out ConfigEntry<T> configEntry))
            {
                configEntry.Value = value;
            }
            else
            {
                VRModCore.LogError($"Could not set internal config entry '{element.Name}'. Not found in internal store.");
            }
        }

        public override void LoadConfig()
        {
            if (_internalConfigFile == null)
            {
                VRModCore.LogError("Cannot load internal config: Internal ConfigFile is null!");
                return;
            }

            foreach (KeyValuePair<string, IConfigElement> entry in ConfigManager.InternalConfigs)
            {
                string key = entry.Key;
                var def = new ConfigDefinition(CONFIG_CATEGORY, key);
                if (_internalConfigFile.ContainsKey(def) && _internalConfigFile[def] is ConfigEntryBase configEntry)
                {
                    IConfigElement config = entry.Value;
                    try
                    {
                        config.BoxedValue = configEntry.BoxedValue;
                    }
                    catch (Exception ex)
                    {
                        VRModCore.LogError($"Failed to sync loaded internal config value for {key}:", ex);
                    }
                }
            }
            VRModCore.LogRuntimeDebug("Internal config load sync complete.");
        }

        public override void SaveConfig()
        {
            if (_internalConfigFile == null)
            {
                VRModCore.LogError("Cannot save internal config, ConfigFile is null!");
                return;
            }

            VRModCore.LogRuntimeDebug($"Saving internal config to '{InternalConfigPath}'...");
            try
            {
                _internalConfigFile.Save();
                VRModCore.Log($"Internal config saved to '{InternalConfigPath}'.");
            }
            catch (Exception e)
            {
                VRModCore.LogError("Exception during SaveConfig:", e);
            }
        }

        public override void OnAnyConfigChanged()
        {
            // The BepInEx ConfigFile saves automatically when a value is changed via its setter,
            // so this explicit save call is not currently needed.
        }
    }
}