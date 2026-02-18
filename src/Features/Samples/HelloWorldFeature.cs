using UnityVRMod.Core;
using UniverseLib.Input;

namespace UnityVRMod.Features.Samples
{
    internal static class HelloWorldFeature
    {
        private const KeyCode TriggerKey = KeyCode.F8;

        public static void Initialize()
        {
            VRModCore.Log("HelloWorldFeature initialized. Press F8 to print Hello World.");
        }

        public static void Update()
        {
            if (InputManager.GetKeyDown(TriggerKey))
            {
                VRModCore.Log("Hello World from custom script!");
            }
        }
    }
}
