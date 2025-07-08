# UnityVRMod (Beta)

UnityVRMod is a framework I am developing for **injecting a full 6-degrees-of-freedom (6DOF) VR experience** into traditional, non-VR Unity games. It is also designed to be a powerful tool for **extending the functionality of existing native VR games.**

Built with universality as its core principle, this mod targets a wide range of Unity versions (from approximately **Unity 5.6 to the latest releases**) and supports both **Mono** and **IL2CPP** runtimes. This is achieved by bypassing Unity's version-locked XR packages and interacting directly with the VR runtimes themselves. While I will strive to maintain this broad compatibility, it is possible that future features may require dropping support for the oldest engine versions. Legacy builds will remain available if this occurs.

Currently, the mod supports **DirectX 11 on Windows (64-bit)**, with two backend options:

* **OpenXR (Recommended):** The primary, modern, and most performant backend. It provides a stable, low-latency experience and should be your default choice.
* **OpenVR (Fallback):** A stable fallback option for games that may have compatibility issues with my OpenXR implementation.

## Beta Notice

This is a beta release (`v0.1.0-beta`). While the core VR visualization is stable and performant, many advanced features are still under development. For the best experience, especially for troubleshooting and advanced configuration, I **strongly recommend** using this mod alongside [**UnityExplorer**](https://github.com/sinai-dev/UnityExplorer). UnityExplorer is an invaluable in-game tool for finding camera paths, scene names, and object hierarchies, which is immensely helpful for configuring this mod's advanced override features.

## Installation

This mod requires **BepInEx 6**. These steps will guide you through the full installation process.

1. **Install BepInEx:** Follow the official [**BepInEx Installation Guide**](https://docs.bepinex.dev/master/articles/user_guide/installation/index.html) to install BepInEx 6 (pre release 2 or bleeding edge build 647+) into your game. The guide will show you how to determine if your game is **Mono** or **IL2CPP** and provide the correct download link.

2. **Download UnityVRMod:** Go to this project's [**Releases**](https://github.com/newunitymodder/UnityVRMod/releases) page. Download the `.zip` file that matches your game's runtime (**Mono** or **IL2CPP**) and your preferred VR backend (**OpenXR** or **OpenVR**).

3. **Install UnityVRMod:** Open the UnityVRMod `.zip` file you just downloaded. Drag its `UnityVRMod` folder into `Bepinex/plugins` directory of your game.

4. **First Launch & Configuration:** Run your game once. This will generate the mod's configuration file at `BepInEx/config/com.newunitymodder.unityvrmod.cfg`. You can open this file in a text editor to make changes.

## Configuration

All options can be changed by editing the `BepInEx/config/com.newunitymodder.unityvrmod.cfg` file with a text editor.

### [VR Injection]

These settings control the mod's core behavior.

* **Enable VR Injection** (`true` / `false`, Default: `true`)
  * This is the master switch.
  * `true`: The mod will actively inject and initialize your preferred VR backend (OpenVR or OpenXR) to create a VR experience. Use this for non-VR games.
  * `false`: The mod enters a passive "Extend-Only" mode. It will not inject or initialize its VR system but will prepare its other features (like the future Command Palette) to work with a game's **native** VR implementation, if one exists.

### [VR Rendering Settings]

These settings fine-tune the visual VR experience. All are live-reloadable in-game.

* **VR World Scale** (Default: `1.0`)
  * Adjusts the perceived size of the world. `> 1.0` makes you feel smaller (the world is bigger); `< 1.0` makes you feel larger (the world is smaller).
* **VR Camera Near Clip** (Default: `0.01`)
  * The closest distance (in meters) that the VR cameras can see. Lower values are generally better for avoiding objects popping out of view when close, but can cause visual artifacts in some games. This value is automatically scaled by `VR WorldScale`.
* **User Eye Height Offset** (Default: `0.0`)
  * A **relative adjustment** in meters to the height reported by your VR headset. This is used to fine-tune your perceived height. For example, if the game feels too tall, try a value like `-0.1`. This value is also scaled by `VR WorldScale`.

### [Advanced Overrides]

These are powerful settings for forcing compatibility with difficult games. Use UnityExplorer to find the necessary names and paths.

* **Scene-Specific Pose Overrides** (Default: empty)
  * Manually sets the VR rig's starting position and/or rotation in specific scenes. This is essential for games where the default camera is not where you want to start (e.g., inside a wall, too far away, or facing the wrong direction). Accepts multiple values separated by semicolon.
  * **Format:** `SceneName|X Y Z|Pitch Yaw Roll;` (separate multiple rules with a semicolon).
  * The scene name is **mandatory**.
  * The rotation part is **optional**.
  * Use `~` as a wildcard to keep the game's original value for any specific axis.
  * **Examples:**
    * `Level1|10 5 20` - In "Level1", start at position (10, 5, 20) with the game's default rotation.
    * `MainMenu||~ 180 ~` - In "MainMenu", keep the game's starting position but rotate the view 180 degrees on the Y-axis.
    * `Cutscene|~ 0 ~|` - In "Cutscene", force the player's Y-position to 0 but keep the original X, Z, and rotation.

* **Asserted Camera Overrides** (Default: empty)
  * This is the highest-priority method for camera detection and forces the mod to use a specific camera by its full hierarchy path. Accepts multiple values separated by semicolon.
  * **In Injected Mode (`Enable VR Injection = true`):** Use this to force the VR rig to follow a specific in-game camera if the default (`Camera.main`) is not the desired one (e.g., a cinematic camera).
  * **In Extend-Only Mode (`Enable VR Injection = false`):** Use this to tell the mod which camera is the primary VR camera in a native VR game, if automatic detection fails.
  * **Format:** `SceneName|GameObject/Path/To/Camera;` The scene name is optional but recommended.

### [General Settings]

* **Toggle Safe Mode Keybind** (Default: `F11`)
  * The key used to toggle VR rendering on and off. This is your main tool for temporarily disabling VR if you encounter an issue.
* **Safe Mode Level** (Default: `RigReinitOnToggle`)
  * Defines the behavior of the Safe Mode toggle.
  * `FastToggleOnly`: Instantly stops rendering. It's the quickest option but may not fix deeper issues.
  * `RigReinitOnToggle`: Stops rendering and completely destroys and rebuilds the VR camera rig. This provides a clean reset
  * `FullVrReinitOnToggle`: The most aggressive option. It completely shuts down and reinitializes the entire VR subsystem. Use this if `RigReinitOnToggle` fails to recover the session.
* **Enable Automatic Safe Mode** (Default: `true`)
  * Automatically enters safe mode for a short duration during scene loads. This is highly recommended to prevent crashes and visual glitches during transitions.
* **Enable Runtime Debug Logging** (Default: `false`)
  * Enables detailed diagnostic messages in the BepInEx console. Only enable this if you are troubleshooting an issue and plan to submit a bug report.

### [OpenVR-Specific Stability Settings]

These settings only apply if you are using an **OpenVR** build.

* **OpenVR WaitGetPoses Delay (ms)** (Default: `2`)
  * A small delay in milliseconds before retrieving headset poses. A value of `2` or higher is recommended to prevent driver crashes in some games when using the OpenVR backend.
* **OpenVR Max Render Target Dimension** (Default: `0`)
  * Sets a maximum width or height for the VR eye textures (`0` = use default). Lowering this (e.g., to `1440`) can improve performance on lower-end hardware at the cost of visual clarity.

## Known Issues & Workarounds

* **OpenVR Safe Mode (`RigReinitOnToggle`) Failure:**
  * **Issue:** In the OpenVR build, using the `RigReinitOnToggle` safe mode can cause the VR session to get stuck on the SteamVR grid, failing to recover.
  * **Workaround:** If you are using the OpenVR build, set your `Safe Mode Level` to `FullVrReinitOnToggle`. This will correctly recover the session, though it takes a few seconds longer. This is a high-priority bug I am actively investigating.

* **Multi-Camera Games:**
  * **Issue:** The mod may not display the correct view in games that use multiple active cameras for rendering (e.g., one for the world, one for the character).
  * **Workaround:** Use [**UnityExplorer**](https://github.com/sinai-dev/UnityExplorer) and enable its **FreeCam** feature. This mod will automatically detect and use the FreeCam as the main camera, providing a complete view.

* **Scene Transition Issues:**
  * **Issue:** While generally stable, some games may still encounter issues during scene loads.
  * **Workaround:** Try increasing the `Automatic Safe Mode Duration`. If the issue persists, try switching between the OpenXR and OpenVR builds of the mod.

## Version History

* **v0.1.0-beta**
  * Initial beta release after rapid alpha development.
  * Core VR injection for OpenXR and OpenVR is stable.
  * Some testing features are included for configuration live reloading.
  * Stub VR Abstraction layer class. This will be used by the next features for a truly backend-agnostic experience.
