using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using VFETribals;

namespace LemProgress
{

    [HarmonyPatch(typeof(WorldTechLevel.TechLevelUtility))]
    [HarmonyPatch(nameof(WorldTechLevel.TechLevelUtility.PlayerResearchFilterLevel))] //annotation boiler plate to tell Harmony what to patch. Refer to docs.
    static internal class TechLevelPatcher
    {
        static bool Prefix(ref TechLevel __result) //pass the __result by ref to alter it.
        {
             __result = TechLevel.Archotech; //alter the result.
            return false; //return false to skip execution of the original.
        }
    }

    [HarmonyPatch(typeof(GameComponent_Tribals))]
    [HarmonyPatch(nameof(GameComponent_Tribals.AdvanceToEra))] //annotation boiler plate to tell Harmony what to patch. Refer to docs.
    static internal class AdvanceTechLevelPatcher
    {
        static bool Prefix(EraAdvancementDef def) //pass the __result by ref to alter it.
        {
            Log.Message("Setting world to: " + def.newTechLevel);
           
            WorldEraAdvancer.AdvanceTo(def);

            return true; //return false to skip execution of the original.
        }
    }
}
