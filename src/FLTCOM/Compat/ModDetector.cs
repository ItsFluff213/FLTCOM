using System;
using FLTCOM.Core.Protocol;

namespace FLTCOM.Compat
{
    /// <summary>Finds mods FLTCOM cannot run beside. Each one refuses the link for its own reason.</summary>
    internal static class ModDetector
    {
        private static readonly (string Assembly, ByeReason Reason)[] Denials =
        {
            ("LmpClient", ByeReason.MultiplayerSession),
            ("ksp_plugin_adapter", ByeReason.UnsupportedPhysics),
            ("RealSolarSystem", ByeReason.UnsupportedScale),
        };

        public static ByeReason? Denial()
        {
            foreach (AssemblyLoader.LoadedAssembly loaded in AssemblyLoader.loadedAssemblies)
            {
                foreach ((string assembly, ByeReason reason) in Denials)
                {
                    if (loaded.name.EndsWith(assembly, StringComparison.OrdinalIgnoreCase))
                    {
                        return reason;
                    }
                }
            }
            return null;
        }

        public static string Words(ByeReason reason)
        {
            switch (reason)
            {
                case ByeReason.MultiplayerSession: return "LINK DENIED, MULTIPLAYER SESSION";
                case ByeReason.UnsupportedPhysics: return "LINK DENIED, UNSUPPORTED PHYSICS";
                case ByeReason.UnsupportedScale: return "LINK DENIED, UNSUPPORTED SCALE";
                default: return "LINK DENIED";
            }
        }
    }
}
