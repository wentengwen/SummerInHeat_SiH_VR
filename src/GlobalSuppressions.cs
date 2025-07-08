// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Style", "IDE0057:Use range operator",
    Justification = ".NET 4.7.2 (C# 7.x) target lacks C# 8+ range operator; older syntax required for compatibility.")]
[assembly: SuppressMessage("Style", "IDE0031:Use null propagation",
    Justification = "Unity GameObjects should not use null propagation, this is a Unity Mod targeting GameObjects a lot.")]
[assembly: SuppressMessage("Style", "IDE0071:Simplify interpolation",
    Justification = "The Mono configuration (net472) does not always support this")]
