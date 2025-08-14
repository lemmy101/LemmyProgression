using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LemProgress.Patches;
using LemProgress.Settings;
using LemProgress.Systems;
using Verse;

namespace LemProgress
{
    [StaticConstructorOnStartup]
    public static class ModCore
    {
        public const string ModId = "LemmyMods.LemProgression";
        private static Harmony harmony;

        public static Harmony Harmony
        {
            get { return harmony; }
        }

        public static LemProgressSettings Settings
        {
            get { return LemProgressMod.settings; }
        }

        static ModCore()
        {
            Log.Message("[" + ModId + "] Initializing...");

            harmony = new Harmony(ModId);

            // Initialize systems in order
            PatchManager.Initialize(harmony);

            if (Settings != null && Settings.debugLogging)
            {
                Log.Message("[" + ModId + "] Debug logging enabled");
            }

            Log.Message("[" + ModId + "] Initialization complete");
        }

        public static void LogDebug(string message)
        {
            if (Settings != null && Settings.debugLogging)
            {
                Log.Message("[" + ModId + "][DEBUG] " + message);
            }
        }
    }
}
