using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace LemProgress.Systems
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using RimWorld;
    using RimWorld.Planet;
    using Verse;
    using HarmonyLib;

    namespace LemProgress.Systems
    {
        /// <summary>
        /// Manages faction def instances to ensure each faction has a unique def
        /// </summary>
        public static class FactionDefManager
        {
            private static Dictionary<Faction, FactionDef> uniqueFactionDefs = new Dictionary<Faction, FactionDef>();
            private static int defCounter = 0;

            /// <summary>
            /// Ensures a faction has a unique def instance that can be modified without affecting other factions
            /// </summary>
            public static FactionDef EnsureUniqueDef(Faction faction)
            {
                if (faction == null || faction.def == null)
                    return null;

                // Check if we already created a unique def for this faction
                if (uniqueFactionDefs.ContainsKey(faction))
                {
                    return uniqueFactionDefs[faction];
                }

                // Check if this def is already unique (not shared with other factions)
                if (IsDefUnique(faction))
                {
                    uniqueFactionDefs[faction] = faction.def;
                    return faction.def;
                }

                // Get all factions that share this def
                var factionsWithSameDef = GetFactionsWithDef(faction.def);

                // If this is the last faction to be made unique, it keeps the original def
                // This ensures the original def is always in use by at least one faction
                int alreadyMadeUnique = 0;
                foreach (var otherFaction in factionsWithSameDef)
                {
                    if (uniqueFactionDefs.ContainsKey(otherFaction) && otherFaction != faction)
                        alreadyMadeUnique++;
                }

                // If all other factions have been made unique, this one keeps the original
                if (alreadyMadeUnique >= factionsWithSameDef.Count - 1)
                {
                    ModCore.LogDebug("Faction " + faction.Name + " keeping original def " + faction.def.defName +
                        " (last faction with this def)");
                    uniqueFactionDefs[faction] = faction.def;
                    return faction.def;
                }

                // Create a unique copy of the def
                var uniqueDef = CreateUniqueDefCopy(faction.def, faction);

                // Replace the faction's def with the unique one
                faction.def = uniqueDef;
                uniqueFactionDefs[faction] = uniqueDef;

                ModCore.LogDebug("Created unique def for faction " + faction.Name + " (was " + faction.def.defName + ")");

                return uniqueDef;
            }

            /// <summary>
            /// Gets all factions using a specific def
            /// </summary>
            private static List<Faction> GetFactionsWithDef(FactionDef def)
            {
                var factions = new List<Faction>();

                if (Find.World == null || Find.World.factionManager == null)
                    return factions;

                foreach (var faction in Find.World.factionManager.AllFactions)
                {
                    // Check both the current def and the original def (before making unique)
                    if (faction.def == def ||
                        (faction.def.defName.StartsWith(def.defName) && faction.def.defName.Contains("_LemProg_")))
                    {
                        factions.Add(faction);
                    }
                }

                return factions;
            }

            /// <summary>
            /// Checks if a faction's def is unique (not shared with other factions)
            /// </summary>
            private static bool IsDefUnique(Faction faction)
            {
                if (Find.World == null || Find.World.factionManager == null)
                    return true;

                int count = 0;
                foreach (var otherFaction in Find.World.factionManager.AllFactions)
                {
                    if (otherFaction.def == faction.def)
                    {
                        count++;
                        if (count > 1)
                            return false;
                    }
                }

                return true;
            }

            /// <summary>
            /// Creates a deep copy of a FactionDef with a unique defName
            /// </summary>
            private static FactionDef CreateUniqueDefCopy(FactionDef sourceDef, Faction forFaction)
            {
                var newDef = new FactionDef();

                // Copy all fields
                var fields = typeof(FactionDef).GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                foreach (var field in fields)
                {
                    try
                    {
                        var value = field.GetValue(sourceDef);

                        // Special handling for collections
                        if (value is System.Collections.IList && !(value is string))
                        {
                            value = DeepCopyList((System.Collections.IList)value, field.FieldType);
                        }

                        field.SetValue(newDef, value);
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[" + ModCore.ModId + "] Failed to copy field " + field.Name + ": " + e.Message);
                    }
                }

                // Make the defName unique
                defCounter++;
                newDef.defName = sourceDef.defName + "_LemProg_" + defCounter;

                // Ensure the new def isn't registered in the DefDatabase
                // This prevents conflicts and allows us to have multiple "versions" of the same base def

                ModCore.LogDebug("Created unique def: " + newDef.defName + " for faction " + forFaction.Name);

                return newDef;
            }

            private static object DeepCopyList(System.Collections.IList source, Type fieldType)
            {
                if (source == null) return null;

                Type elementType;
                if (fieldType.IsArray)
                {
                    elementType = fieldType.GetElementType();
                }
                else
                {
                    var genericArgs = fieldType.GetGenericArguments();
                    elementType = genericArgs.Length > 0 ? genericArgs[0] : typeof(object);
                }

                var listType = typeof(List<>).MakeGenericType(elementType);
                var newList = (System.Collections.IList)Activator.CreateInstance(listType);

                foreach (var item in source)
                {
                    newList.Add(item);
                }

                if (fieldType.IsArray)
                {
                    var array = Array.CreateInstance(elementType, newList.Count);
                    newList.CopyTo(array, 0);
                    return array;
                }

                return newList;
            }

            /// <summary>
            /// Cleans up references for removed factions
            /// </summary>
            public static void CleanupRemovedFactions()
            {
                if (Find.World == null || Find.World.factionManager == null)
                    return;

                var currentFactions = new HashSet<Faction>(Find.World.factionManager.AllFactions);
                var toRemove = uniqueFactionDefs.Keys.Where(f => !currentFactions.Contains(f)).ToList();

                foreach (var faction in toRemove)
                {
                    uniqueFactionDefs.Remove(faction);
                }
            }

            /// <summary>
            /// Gets statistics about faction def usage
            /// </summary>
            public static Dictionary<string, int> GetDefUsageStats()
            {
                var stats = new Dictionary<string, int>();

                if (Find.World == null || Find.World.factionManager == null)
                    return stats;

                foreach (var faction in Find.World.factionManager.AllFactions)
                {
                    var defName = faction.def.defName;
                    if (stats.ContainsKey(defName))
                        stats[defName]++;
                    else
                        stats[defName] = 1;
                }

                return stats;
            }

            /// <summary>
            /// Validates that all original defs are still in use
            /// </summary>
            public static void ValidateDefUsage()
            {
                if (Find.World == null || Find.World.factionManager == null)
                    return;

                var allFactionDefs = DefDatabase<FactionDef>.AllDefsListForReading;
                var usedDefs = new HashSet<FactionDef>();

                // Collect all defs currently in use
                foreach (var faction in Find.World.factionManager.AllFactions)
                {
                    usedDefs.Add(faction.def);

                    // Also track the original def if this is a unique copy
                    var originalDefName = GetOriginalDefName(faction.def);
                    var originalDef = allFactionDefs.FirstOrDefault(d => d.defName == originalDefName);
                    if (originalDef != null)
                        usedDefs.Add(originalDef);
                }

                // Check for orphaned original defs
                foreach (var def in allFactionDefs)
                {
                    // Skip our unique copies
                    if (def.defName.Contains("_LemProg_"))
                        continue;

                    // Skip hidden and player defs
                    if (def.hidden || def.isPlayer)
                        continue;

                    if (!usedDefs.Contains(def))
                    {
                        ModCore.LogDebug("Warning: FactionDef " + def.defName + " is not used by any faction");
                    }
                }
            }

            /// <summary>
            /// Consolidates factions with duplicate defs by removing extras
            /// </summary>
            public static void ConsolidateDuplicateFactions(int maxPerDef = 2)
            {
                if (Find.World == null || Find.World.factionManager == null)
                    return;

                var defGroups = new Dictionary<string, List<Faction>>();

                // Group factions by their original def name
                foreach (var faction in Find.World.factionManager.AllFactions)
                {
                    if (faction.IsPlayer) continue;

                    var defName = GetOriginalDefName(faction.def);

                    if (!defGroups.ContainsKey(defName))
                        defGroups[defName] = new List<Faction>();

                    defGroups[defName].Add(faction);
                }

                // Remove excess factions
                foreach (var group in defGroups)
                {
                    if (group.Value.Count > maxPerDef)
                    {
                        ModCore.LogDebug("Found " + group.Value.Count + " factions with def " + group.Key);

                        // Sort by various criteria to keep the most important ones
                        var sortedFactions = group.Value
                            .OrderByDescending(f => f.PlayerRelationKind == FactionRelationKind.Ally)
                            .ThenByDescending(f => f.PlayerGoodwill)
                            .ThenByDescending(f => GetFactionSettlementCount(f))
                            .ToList();

                        // Remove excess factions
                        for (int i = maxPerDef; i < sortedFactions.Count; i++)
                        {
                            RemoveFactionFromWorld(sortedFactions[i]);
                        }
                    }
                }
            }

            /// <summary>
            /// Gets the original def name (without our unique suffix)
            /// </summary>
            private static string GetOriginalDefName(FactionDef def)
            {
                var defName = def.defName;
                if (defName.Contains("_LemProg_"))
                {
                    return defName.Substring(0, defName.IndexOf("_LemProg_"));
                }
                return defName;
            }

            /// <summary>
            /// Ensures all factions have unique defs in an efficient batch operation
            /// </summary>
            public static void EnsureAllFactionsHaveUniqueDefs()
            {
                if (Find.World == null || Find.World.factionManager == null)
                    return;

                // Group factions by their current def
                var defGroups = new Dictionary<FactionDef, List<Faction>>();

                foreach (var faction in Find.World.factionManager.AllFactions)
                {
                    if (faction.IsPlayer) continue;

                    if (!defGroups.ContainsKey(faction.def))
                        defGroups[faction.def] = new List<Faction>();

                    defGroups[faction.def].Add(faction);
                }

                // Process each group
                foreach (var group in defGroups)
                {
                    if (group.Value.Count <= 1)
                        continue; // Already unique

                    ModCore.LogDebug("Processing " + group.Value.Count + " factions with def " + group.Key.defName);

                    // Keep the first faction with the original def
                    // Give all others unique copies
                    for (int i = 0; i < group.Value.Count; i++)
                    {
                        var faction = group.Value[i];

                        if (i == 0)
                        {
                            // First faction keeps the original def
                            uniqueFactionDefs[faction] = faction.def;
                            ModCore.LogDebug("Faction " + faction.Name + " keeping original def " + faction.def.defName);
                        }
                        else
                        {
                            // Others get unique copies
                            var uniqueDef = CreateUniqueDefCopy(group.Key, faction);
                            faction.def = uniqueDef;
                            uniqueFactionDefs[faction] = uniqueDef;
                            ModCore.LogDebug("Created unique def for faction " + faction.Name);
                        }
                    }
                }
            }

            private static int GetFactionSettlementCount(Faction faction)
            {
                int count = 0;
                foreach (var settlement in Find.WorldObjects.Settlements)
                {
                    if (settlement.Faction == faction)
                        count++;
                }
                return count;
            }

            /// <summary>
            /// Removes a faction from the world (as cleanly as possible)
            /// </summary>
            public static void RemoveFactionFromWorld(Faction faction)
            {
                if (faction == null || faction.IsPlayer)
                    return;

                try
                {
                    ModCore.LogDebug("Attempting to remove faction: " + faction.Name);

                    // Remove all settlements belonging to this faction
                    var settlements = Find.WorldObjects.Settlements
                        .Where(s => s.Faction == faction)
                        .ToList();

                    foreach (var settlement in settlements)
                    {
                        Find.WorldObjects.Remove(settlement);
                    }

                    // Remove any other world objects
                    var worldObjects = Find.WorldObjects.AllWorldObjects
                        .Where(o => o.Faction == faction)
                        .ToList();

                    foreach (var obj in worldObjects)
                    {
                        Find.WorldObjects.Remove(obj);
                    }

                    // Try to remove the faction from the faction manager
                    // Since Remove doesn't exist, we need to use reflection
                    var factionManager = Find.World.factionManager;
                    var factionsField = AccessTools.Field(typeof(FactionManager), "allFactions");

                    if (factionsField != null)
                    {
                        var factionsList = factionsField.GetValue(factionManager) as List<Faction>;
                        if (factionsList != null && factionsList.Contains(faction))
                        {
                            factionsList.Remove(faction);
                            ModCore.LogDebug("Successfully removed faction " + faction.Name + " from world");
                        }
                    }

                    // Clean up our tracking
                    if (uniqueFactionDefs.ContainsKey(faction))
                    {
                        uniqueFactionDefs.Remove(faction);
                    }
                }
                catch (Exception e)
                {
                    Log.Error("[" + ModCore.ModId + "] Failed to remove faction: " + e.ToString());
                }
            }
        }
    }
}
