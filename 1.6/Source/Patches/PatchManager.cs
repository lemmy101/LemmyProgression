using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace LemProgress.Patches
{
    public static class PatchManager
    {
        private static readonly List<IPatchDefinition> patches = new List<IPatchDefinition>
        {
            new WorldTechLevelPatch(),
            new VFETribalsPatch()
        };

        public static void Initialize(Harmony harmony)
        {
            ApplyAttributePatches(harmony);
            ApplyRuntimePatches();
        }

        private static void ApplyAttributePatches(Harmony harmony)
        {
            harmony.PatchAll();
            Log.Message("[" + ModCore.ModId + "] Applied attribute-based patches (including debug patches)");
        }

        private static void ApplyRuntimePatches()
        {
            foreach (var patch in patches.Where(p => p.ShouldApply()))
            {
                try
                {
                    patch.Apply();
                    Log.Message("[" + ModCore.ModId + "] Applied patch: " + patch.Name);
                }
                catch (Exception e)
                {
                    Log.Error("[" + ModCore.ModId + "] Failed to apply patch " + patch.Name + ": " + e.ToString());
                }
            }
        }
    }

    // Patch interface for runtime patches
    public interface IPatchDefinition
    {
        string Name { get; }
        string RequiredModId { get; }
        bool ShouldApply();
        void Apply();
    }
}