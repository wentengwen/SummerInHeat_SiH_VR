namespace UnityVRMod.Features.VrVisualization
{
    /// <summary>
    /// Defines the behavior of the user-toggled Safe Mode.
    /// </summary>
    public enum SafeModeLevel
    {
        /// <summary>
        /// Toggling Safe Mode on and off is fast, but may leave some objects behind temporarily.
        /// </summary>
        FastToggleOnly,
        /// <summary>
        /// Toggling Safe Mode on fully tears down the VR camera rig, providing a cleaner reset at the cost of a slight delay. (Default)
        /// </summary>
        RigReinitOnToggle,
        /// <summary>
        /// Toggling Safe Mode on fully tears down the VR rig AND reinitializes the entire VR subsystem. This is the most aggressive and safest option, useful for games with very delicate rendering pipelines.
        /// </summary>
        FullVrReinitOnToggle
    }
}