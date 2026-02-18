using UnityVRMod.Config;
using UnityVRMod.Core;
using UnityVRMod.Features.Shared;
using UniverseLib.Input;

namespace UnityVRMod.Features.Debug
{
    internal static class TemporaryLiveReloadTester
    {
        private static readonly List<IConfigElement> _reloadableSettings = [];
        private static int _selectedSettingIndex = -1;
        private static string _displayMessage = "";
        private static float _messageClearTime = 0f;

        // Keys for interaction
        private const KeyCode NEXT_SETTING_KEY = KeyCode.PageDown;
        private const KeyCode PREV_SETTING_KEY = KeyCode.PageUp;
        private const KeyCode INCREMENT_KEY = KeyCode.KeypadPlus;
        private const KeyCode DECREMENT_KEY = KeyCode.KeypadMinus;
        private const KeyCode TOGGLE_BOOL_KEY = KeyCode.KeypadEnter;
        private const KeyCode TEST_API_KEY = KeyCode.F10; // New key for our test

        // Step values for increment/decrement
        private const float FLOAT_STEP = 0.1f;
        private const int INT_STEP = 1;

        public static void Initialize()
        {
            _reloadableSettings.Clear();
            VRModCore.Log("Initializing and scanning for reloadable settings...");

            if (ConfigManager.ConfigElements != null && ConfigManager.ConfigElements.Count > 0)
            {
                foreach (var kvp in ConfigManager.ConfigElements)
                {
                    IConfigElement element = kvp.Value;
                    if (element.ElementType != typeof(KeyCode) && !element.IsInternal)
                    {
                        if (element.ElementType == typeof(float) ||
                            element.ElementType == typeof(int) ||
                            element.ElementType == typeof(bool) ||
                            element.ElementType.IsEnum)
                        {
                            _reloadableSettings.Add(element);
                            VRModCore.LogRuntimeDebug($"Added '{element.Name}' (Type: {element.ElementType.Name}) to testable list.");
                        }
                    }
                }
            }

            _reloadableSettings.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

            _selectedSettingIndex = _reloadableSettings.Count > 0 ? 0 : -1;
            if (_selectedSettingIndex != -1)
            {
                DisplaySelectedSetting();
            }
            else
            {
                VRModCore.Log("No suitable non-internal, non-KeyCode settings found to test with inc/dec/toggle.");
            }
        }

        public static void Update()
        {
            if (Time.time > _messageClearTime && !string.IsNullOrEmpty(_displayMessage))
            {
                _displayMessage = "";
            }

            if (InputManager.GetKeyDown(TEST_API_KEY))
            {
                TestSharedVrApi();
                return; // Don't process other keys on the same frame
            }

            if (_selectedSettingIndex == -1 || _reloadableSettings.Count == 0) return;


            IConfigElement selectedElement = _reloadableSettings[_selectedSettingIndex];

            if (InputManager.GetKeyDown(NEXT_SETTING_KEY))
            {
                _selectedSettingIndex = (_selectedSettingIndex + 1) % _reloadableSettings.Count;
                DisplaySelectedSetting();
            }
            else if (InputManager.GetKeyDown(PREV_SETTING_KEY))
            {
                _selectedSettingIndex = (_selectedSettingIndex - 1 + _reloadableSettings.Count) % _reloadableSettings.Count;
                DisplaySelectedSetting();
            }
            else if (InputManager.GetKeyDown(INCREMENT_KEY))
            {
                ModifyValue(selectedElement, true);
                DisplaySelectedSetting();
            }
            else if (InputManager.GetKeyDown(DECREMENT_KEY))
            {
                ModifyValue(selectedElement, false);
                DisplaySelectedSetting();
            }
            else if (InputManager.GetKeyDown(TOGGLE_BOOL_KEY))
            {
                if (selectedElement.ElementType == typeof(bool))
                {
                    var boolElement = (ConfigElement<bool>)selectedElement;
                    boolElement.Value = !boolElement.Value; // This will trigger OnValueChanged
                    SetDisplayMessage($"Toggled [{selectedElement.Name}] to {boolElement.Value}");
                    DisplaySelectedSetting(); // Update display immediately
                }
            }
        }

        private static void TestSharedVrApi()
        {
            VRModCore.Log("--- Running SharedVrApi.GetCurrentVrCamera() Test ---");
            Camera vrCam = SharedVrApi.GetCurrentVrCamera();
            if (vrCam != null)
            {
                SetDisplayMessage($"SUCCESS: Found VR Camera: '{vrCam.name}'");
            }
            else
            {
                SetDisplayMessage("FAILURE: GetCurrentVrCamera() returned NULL.");
            }
            VRModCore.Log("--- Test Complete ---");
        }

        private static void ModifyValue(IConfigElement element, bool increment)
        {
            if (element.ElementType == typeof(float))
            {
                var floatElement = (ConfigElement<float>)element;
                float newVal = floatElement.Value + (increment ? FLOAT_STEP : -FLOAT_STEP);
                floatElement.Value = (float)Math.Round(newVal, 4);
                SetDisplayMessage($"Adjusted [{element.Name}] to {floatElement.Value}");
            }
            else if (element.ElementType == typeof(int))
            {
                var intElement = (ConfigElement<int>)element;
                intElement.Value += (increment ? INT_STEP : -INT_STEP);
                SetDisplayMessage($"Adjusted [{element.Name}] to {intElement.Value}");
            }
            else if (element.ElementType.IsEnum)
            {
                var enumValues = Enum.GetValues(element.ElementType);
                int currentIndex = Array.IndexOf(enumValues, element.BoxedValue);
                int nextIndex = currentIndex + (increment ? 1 : -1);
                if (nextIndex >= enumValues.Length) nextIndex = 0;
                if (nextIndex < 0) nextIndex = enumValues.Length - 1;
                element.BoxedValue = enumValues.GetValue(nextIndex);
                SetDisplayMessage($"Set [{element.Name}] to {element.BoxedValue}");
            }
        }

        private static void SetDisplayMessage(string message, float duration = 2f)
        {
            _displayMessage = message;
            VRModCore.Log($"{message}");
            _messageClearTime = Time.time + duration;
        }

        private static void DisplaySelectedSetting()
        {
            if (_selectedSettingIndex != -1)
            {
                IConfigElement selected = _reloadableSettings[_selectedSettingIndex];
                string typeName = selected.ElementType.IsEnum ? "Enum" : selected.ElementType.Name;
                SetDisplayMessage($"Selected: [{selected.Name}] (Type: {typeName}, Current: {selected.BoxedValue}) | +/- or Enter (for bool)");
            }
        }
    }
}
