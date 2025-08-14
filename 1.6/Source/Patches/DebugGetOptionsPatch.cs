using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace LemProgress.Patches
{
    [HarmonyPatch]
    public static class RootCauseInvestigationPatch
    {
        // Let's trace the call stack to see WHERE the null groupParms is coming from
        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(PawnGroupKindWorker_Normal), "MinPointsToGenerateAnything");
        }

        [HarmonyPrefix]
        static bool Prefix(PawnGroupMaker groupMaker, FactionDef faction, PawnGroupMakerParms parms)
        {
            try
            {
                Log.Message("[LemProgress][ROOT_CAUSE] MinPointsToGenerateAnything called:");
                Log.Message("[LemProgress][ROOT_CAUSE] groupMaker: " + (groupMaker?.kindDef?.defName ?? "NULL"));
                Log.Message("[LemProgress][ROOT_CAUSE] faction: " + (faction?.defName ?? "NULL"));
                Log.Message("[LemProgress][ROOT_CAUSE] parms: " + (parms == null ? "NULL" : "NOT NULL"));

                if (parms != null)
                {
                    Log.Message("[LemProgress][ROOT_CAUSE] parms.faction: " + (parms.faction?.Name ?? "NULL"));
                    Log.Message("[LemProgress][ROOT_CAUSE] parms.raidStrategy: " + (parms.raidStrategy?.defName ?? "NULL"));
                    Log.Message("[LemProgress][ROOT_CAUSE] parms.groupKind: " + (parms.groupKind?.defName ?? "NULL"));
                    Log.Message("[LemProgress][ROOT_CAUSE] parms.points: " + parms.points);
                }

                if (groupMaker?.options == null)
                {
                    Log.Warning("[LemProgress][ROOT_CAUSE] groupMaker.options is NULL - this will cause GetOptions to receive null options!");

                    // Check what's in the groupMaker
                    if (groupMaker != null)
                    {
                        Log.Message("[LemProgress][ROOT_CAUSE] groupMaker.guards count: " + (groupMaker.guards?.Count ?? -1));
                        Log.Message("[LemProgress][ROOT_CAUSE] groupMaker.carriers count: " + (groupMaker.carriers?.Count ?? -1));
                        Log.Message("[LemProgress][ROOT_CAUSE] groupMaker.traders count: " + (groupMaker.traders?.Count ?? -1));

                        // Try to force regeneration
                        try
                        {
                            var traverse = Traverse.Create(groupMaker);
                            var optionsField = traverse.Field("_options");
                            if (optionsField.FieldExists())
                            {
                                optionsField.SetValue(null);
                                Log.Message("[LemProgress][ROOT_CAUSE] Cleared options cache, trying regeneration...");
                                var newOptions = groupMaker.options; // This should trigger regeneration
                                Log.Message("[LemProgress][ROOT_CAUSE] Regenerated options count: " + (newOptions?.Count ?? -1));
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error("[LemProgress][ROOT_CAUSE] Failed to regenerate options: " + e.Message);
                        }
                    }
                }

                // Check if this faction was upgraded by us
                if (faction != null && WasThisFactionUpgradedByUs(faction))
                {
                    Log.Warning("[LemProgress][ROOT_CAUSE] This faction (" + faction.defName + ") was upgraded by LemProgress!");
                }

                return true; // Let original method run
            }
            catch (Exception e)
            {
                Log.Error("[LemProgress][ROOT_CAUSE] Exception in investigation: " + e.ToString());
                return true;
            }
        }

        private static bool WasThisFactionUpgradedByUs(FactionDef factionDef)
        {
            // Check if any actual faction in the world is using this def and has been upgraded
            if (Find.World?.factionManager?.AllFactions == null) return false;

            foreach (var faction in Find.World.factionManager.AllFactions)
            {
                if (faction.def == factionDef)
                {
                    // Check if this faction's name suggests it was upgraded
                    // (This is a heuristic - we could store upgrade info more formally)
                    var factionName = faction.Name;
                    // We could also check if the def name doesn't match the original faction type
                    // or if there are other indicators of upgrading
                    return true; // For now, assume any faction using this def might have been upgraded
                }
            }
            return false;
        }
    }

    // Also patch the method that calls MinPointsToGenerateAnything to see the full chain
    [HarmonyPatch(typeof(FactionDef), "MinPointsToGeneratePawnGroup")]
    public static class FactionDefMinPointsPatch
    {
        [HarmonyPrefix]
        static bool Prefix(FactionDef __instance, PawnGroupKindDef groupKind, PawnGroupMakerParms parms)
        {
            try
            {
                Log.Message("[LemProgress][FACTION_DEF] MinPointsToGeneratePawnGroup called:");
                Log.Message("[LemProgress][FACTION_DEF] __instance (FactionDef): " + __instance.defName);
                Log.Message("[LemProgress][FACTION_DEF] groupKind: " + (groupKind?.defName ?? "NULL"));
                Log.Message("[LemProgress][FACTION_DEF] parms: " + (parms == null ? "NULL" : "NOT NULL"));

                if (parms != null)
                {
                    Log.Message("[LemProgress][FACTION_DEF] parms.faction: " + (parms.faction?.Name ?? "NULL"));

                    // This is critical - check if the parms.faction matches the __instance
                    if (parms.faction?.def != __instance)
                    {
                        Log.Error("[LemProgress][FACTION_DEF] CRITICAL MISMATCH: parms.faction.def (" +
                            (parms.faction?.def?.defName ?? "NULL") + ") != __instance (" + __instance.defName + ")");
                    }
                }

                Log.Message("[LemProgress][FACTION_DEF] pawnGroupMakers count: " + (__instance.pawnGroupMakers?.Count ?? -1));

                return true;
            }
            catch (Exception e)
            {
                Log.Error("[LemProgress][FACTION_DEF] Exception: " + e.ToString());
                return true;
            }
        }
    }
}