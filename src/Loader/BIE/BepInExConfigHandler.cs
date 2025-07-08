#if BIE

using BepInEx.Configuration;
using UnityVRMod.Config;
using UnityVRMod.Core;

namespace UnityVRMod.Loader.BIE
{
    public class BepInExConfigHandler : ConfigHandler
    {
        private static ConfigFile BepInExConfig => VRModBepInPlugin.Instance.Config;

        private const string CONFIG_CATEGORY = "General";

        public override void Init() { }

        public override void RegisterConfigElement<T>(ConfigElement<T> configElement)
        {
            if (BepInExConfig == null)
            {
                VRModCore.LogError($"Cannot register config '{configElement.Name}': BepInExConfig is null!");
                return;
            }

            ConfigEntry<T> entry = BepInExConfig.Bind(
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
            if (BepInExConfig == null)
            {
                VRModCore.LogError($"Cannot get config '{element.Name}': BepInExConfig is null! Returning cached value.");
                return element.Value;
            }

            if (BepInExConfig.TryGetEntry(CONFIG_CATEGORY, element.Name, out ConfigEntry<T> configEntry))
            {
                return configEntry.Value;
            }
            else
            {
                VRModCore.LogRuntimeDebug($"Config entry '{element.Name}' not found in BepInEx store, returning cached value.");
                return element.Value;
            }
        }

        public override void SetConfigValue<T>(ConfigElement<T> element, T value)
        {
            if (BepInExConfig == null)
            {
                VRModCore.LogError($"Cannot set config '{element.Name}': BepInExConfig is null!");
                return;
            }

            if (BepInExConfig.TryGetEntry(CONFIG_CATEGORY, element.Name, out ConfigEntry<T> configEntry))
            {
                configEntry.Value = value;
            }
            else
            {
                VRModCore.LogError($"Could not set config entry '{element.Name}'. Not found in BepInEx store.");
            }
        }

        public override void LoadConfig()
        {
            if (BepInExConfig == null)
            {
                VRModCore.LogError("Cannot load config: BepInExConfig is null!");
                return;
            }
            foreach (KeyValuePair<string, IConfigElement> entry in ConfigManager.ConfigElements)
            {
                string key = entry.Key;
                var def = new ConfigDefinition(CONFIG_CATEGORY, key);
                if (BepInExConfig.ContainsKey(def) && BepInExConfig[def] is ConfigEntryBase configEntry)
                {
                    IConfigElement config = entry.Value;
                    try
                    {
                        config.BoxedValue = configEntry.BoxedValue;
                    }
                    catch (Exception ex)
                    {
                        VRModCore.LogError($"Failed to sync loaded config value for {key}:", ex);
                    }
                }
                else
                {
                    VRModCore.LogRuntimeDebug($"Entry '{key}' in category '{CONFIG_CATEGORY}' not found in BepInExConfig. Using default/current value.");
                }
            }
            VRModCore.LogRuntimeDebug("LoadConfig sync complete.");
        }

        public override void SaveConfig()
        {
            if (BepInExConfig == null)
            {
                VRModCore.LogError("Cannot save config: BepInExConfig is null!");
                return;
            }
            VRModCore.LogRuntimeDebug("Requesting BepInExConfig.Save()...");
            try
            {
                BepInExConfig.Save();
                VRModCore.Log("Main BepInExConfig saved.");
            }
            catch (Exception e)
            {
                VRModCore.LogError("Exception during SaveConfig:", e);
            }
        }
    }
}

#endif