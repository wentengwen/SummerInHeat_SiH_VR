using UnityVRMod.Config; 

namespace UnityVRMod.Loader
{
    public interface IVRModLoader
    {
        string InteropAssembliesPath { get; }
        ConfigHandler ConfigHandler { get; }
        Action<object> LogMessage { get; }
        Action<object> LogWarning { get; }
        Action<object> LogError { get; }
    }
}