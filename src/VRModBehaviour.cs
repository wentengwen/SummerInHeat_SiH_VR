using UnityVRMod.Config;

#if CPP
using Il2CppInterop.Runtime.Injection;
#endif

#pragma warning disable IDE0130 
namespace UnityVRMod.Core
#pragma warning restore IDE0130
{
    public class VRModBehaviour : MonoBehaviour
    {
        internal static VRModBehaviour Instance { get; private set; }

#if CPP
#pragma warning disable IDE0290
        public VRModBehaviour(IntPtr ptr) : base(ptr) { }
#pragma warning restore IDE0290
#endif

        internal static void Setup()
        {
#if CPP
            ClassInjector.RegisterTypeInIl2Cpp<VRModBehaviour>();
#endif

            GameObject behaviourHost = new("VRModBehaviour_Host");
            DontDestroyOnLoad(behaviourHost);
            behaviourHost.hideFlags = HideFlags.HideAndDontSave;

            Instance = behaviourHost.AddComponent<VRModBehaviour>();

            if (Instance == null)
            {
                VRModCore.LogError("Failed to create VRModBehaviour instance!");
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "This cannot be static or else Unity will not be able to call it.")]
        internal void Update()
        {
            VRModCore.Update();
        }

        internal void OnApplicationQuit()
        {
            VRModCore.Log("Game quitting. Performing cleanup...");

            if (VRModCore.VrVisualizationFeature != null)
            {
                VRModCore.LogRuntimeDebug("Shutting down VrVisualizationFeature...");
                try
                {
                    VRModCore.VrVisualizationFeature.Shutdown();
                }
                catch (Exception ex)
                {
                    VRModCore.LogError("Exception during VrVisualizationFeature.Shutdown():", ex);
                }
            }
            else
            {
                VRModCore.LogRuntimeDebug("VrVisualizationFeature is null, skipping its shutdown.");
            }

            ConfigManager.SaveAll();

            if (this != null && gameObject != null)
                Destroy(gameObject);

            VRModCore.LogRuntimeDebug("OnApplicationQuit finished.");
        }
    }
}