using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace LemProgress.Patches
{
    [HarmonyPatch(typeof(FactionDef), "MinPointsToGeneratePawnGroup")]
    public static class NullPawnGroupMakerFix
    {
        [HarmonyPrefix]
        static bool Prefix(FactionDef __instance, PawnGroupKindDef groupKind, PawnGroupMakerParms parms, ref float __result)
        {
            try
            {
                if (__instance.pawnGroupMakers == null || __instance.pawnGroupMakers.Count == 0)
                {
                    __result = float.MaxValue;
                    return false;
                }

                // Filter out null pawn group makers before calling Min()
                var validPawnGroupMakers = __instance.pawnGroupMakers
                    .Where(pgm => pgm != null && pgm.kindDef == groupKind)
                    .ToList();

                if (validPawnGroupMakers.Count == 0)
                {
                    ModCore.LogDebug("No valid pawn group makers found for " + __instance.defName + " with group kind " + groupKind?.defName);
                    __result = float.MaxValue;
                    return false;
                }

                // Check for any null pawn group makers and log them
                var nullCount = __instance.pawnGroupMakers.Count(pgm => pgm == null);
                if (nullCount > 0)
                {
                    Log.Warning("[" + ModCore.ModId + "] Found " + nullCount + " null pawn group makers in " + __instance.defName +
                        " - this would have caused a crash. Filtering them out.");
                }

                ModCore.LogDebug("FactionDef " + __instance.defName + " has " + validPawnGroupMakers.Count +
                    " valid pawn group makers for " + groupKind?.defName);

                // Safely call Min() on the filtered list
                try
                {
                    __result = validPawnGroupMakers.Min(pgm => pgm.MinPointsToGenerateAnything(__instance, parms));
                    return false; // Skip original method
                }
                catch (Exception e)
                {
                    Log.Error("[" + ModCore.ModId + "] Exception in Min() even after filtering nulls for " +
                        __instance.defName + ": " + e.ToString());

                    // Try individual testing to find the problematic pawn group maker
                    foreach (var pgm in validPawnGroupMakers)
                    {
                        try
                        {
                            var testResult = pgm.MinPointsToGenerateAnything(__instance, parms);
                            ModCore.LogDebug("PGM " + pgm.kindDef.defName + " returned: " + testResult);
                        }
                        catch (Exception pgmException)
                        {
                            Log.Error("[" + ModCore.ModId + "] PGM " + pgm.kindDef.defName + " failed: " + pgmException.Message);
                        }
                    }

                    __result = float.MaxValue;
                    return false;
                }
            }
            catch (Exception e)
            {
                Log.Error("[" + ModCore.ModId + "] Exception in NullPawnGroupMakerFix: " + e.ToString());
                // Fall back to original method
                return true;
            }
        }
    }

    // Also add a patch to validate faction defs after they're loaded/modified
    [HarmonyPatch]
    public static class FactionDefValidationPatch
    {
        // Patch any method that might modify faction defs to ensure they don't introduce nulls
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(FactionDef), "ResolveReferences");
            // Add other methods that might modify faction defs if we find them
        }

        [HarmonyPostfix]
        static void Postfix(FactionDef __instance)
        {
            try
            {
                if (__instance.pawnGroupMakers != null)
                {
                    // Check for null pawn group makers
                    for (int i = 0; i < __instance.pawnGroupMakers.Count; i++)
                    {
                        if (__instance.pawnGroupMakers[i] == null)
                        {
                            Log.Warning("[" + ModCore.ModId + "] Found null pawn group maker at index " + i +
                                " in faction def " + __instance.defName + " during ResolveReferences");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("[" + ModCore.ModId + "] Exception in FactionDefValidationPatch: " + e.ToString());
            }
        }
    }
}