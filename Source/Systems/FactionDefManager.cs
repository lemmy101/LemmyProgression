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
    /// Manages faction def instances to ensure each faction can be upgraded independently
    /// </summary>
    public static class FactionDefManager
    {
        private static Dictionary<Faction, FactionDef> uniqueFactionDefs = new Dictionary<Faction, FactionDef>();
        private static Dictionary<Faction, string> originalDefNames = new Dictionary<Faction, string>();
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

            // Store the original def name if we haven't already
            if (!originalDefNames.ContainsKey(faction))
            {
                originalDefNames[faction] = GetOriginalDefName(faction.def);
            }

            // Check if this def is already unique (not shared with other factions)
            if (IsDefUnique(faction))
            {
                uniqueFactionDefs[faction] = faction.def;
                ModCore.LogDebug("Faction " + faction.Name + " already has unique def " + faction.def.defName);
                return faction.def;
            }

            // Get all factions that share this def
            var factionsWithSameDef = GetFactionsWithDef(faction.def);
            ModCore.LogDebug("Found " + factionsWithSameDef.Count + " factions sharing def " + faction.def.defName);

            // Create a unique copy of the def for this faction
            var uniqueDef = CreateUniqueDefCopy(faction.def, faction);

            // Replace the faction's def with the unique one
            faction.def = uniqueDef;
            uniqueFactionDefs[faction] = uniqueDef;

            Log.Message("[" + ModCore.ModId + "] Created unique def for faction " + faction.Name +
                " (copy of " + originalDefNames[faction] + ", new name: " + uniqueDef.defName + ")");

            return uniqueDef;
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
        /// Gets all factions using a specific def
        /// </summary>
        private static List<Faction> GetFactionsWithDef(FactionDef def)
        {
            var factions = new List<Faction>();

            if (Find.World == null || Find.World.factionManager == null)
                return factions;

            foreach (var faction in Find.World.factionManager.AllFactions)
            {
                if (faction.def == def)
                {
                    factions.Add(faction);
                }
            }

            return factions;
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
            var originalName = GetOriginalDefName(sourceDef);
            newDef.defName = originalName + "_LemProg_" + defCounter;

            // Update the generated def name field if it exists
            var generatedDefNameField = AccessTools.Field(typeof(FactionDef), "generatedDefName");
            if (generatedDefNameField != null)
            {
                generatedDefNameField.SetValue(newDef, newDef.defName);
            }

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
        /// Gets the original def name (without our unique suffix)
        /// </summary>
        public static string GetOriginalDefName(FactionDef def)
        {
            var defName = def.defName;
            if (defName.Contains("_LemProg_"))
            {
                return defName.Substring(0, defName.IndexOf("_LemProg_"));
            }
            return defName;
        }

        /// <summary>
        /// Gets the original def name for a faction (tracks what it started as)
        /// </summary>
        public static string GetOriginalDefNameForFaction(Faction faction)
        {
            if (originalDefNames.ContainsKey(faction))
                return originalDefNames[faction];

            return GetOriginalDefName(faction.def);
        }

        /// <summary>
        /// Ensures all factions that will be upgraded have unique defs
        /// </summary>
        public static void EnsureUniqueDefsForUpgrade(List<Faction> factionsToUpgrade)
        {
            if (factionsToUpgrade == null)
                return;

            foreach (var faction in factionsToUpgrade)
            {
                if (!faction.IsPlayer)
                {
                    EnsureUniqueDef(faction);
                }
            }
        }

        /// <summary>
        /// Gets statistics about faction def usage
        /// </summary>
        public static Dictionary<string, DefUsageInfo> GetDefUsageStats()
        {
            var stats = new Dictionary<string, DefUsageInfo>();

            if (Find.World == null || Find.World.factionManager == null)
                return stats;

            foreach (var faction in Find.World.factionManager.AllFactions)
            {
                var defName = faction.def.defName;
                var originalName = GetOriginalDefNameForFaction(faction);

                if (!stats.ContainsKey(originalName))
                {
                    stats[originalName] = new DefUsageInfo
                    {
                        OriginalDefName = originalName,
                        TotalCount = 0,
                        Factions = new List<FactionInfo>()
                    };
                }

                stats[originalName].TotalCount++;
                stats[originalName].Factions.Add(new FactionInfo
                {
                    Name = faction.Name,
                    CurrentDefName = defName,
                    IsUnique = defName.Contains("_LemProg_")
                });
            }

            return stats;
        }

        /// <summary>
        /// Cleans up references for removed factions (if any are removed by other means)
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
                originalDefNames.Remove(faction);
            }

            if (toRemove.Count > 0)
            {
                ModCore.LogDebug("Cleaned up " + toRemove.Count + " removed faction references");
            }
        }

        public class DefUsageInfo
        {
            public string OriginalDefName { get; set; }
            public int TotalCount { get; set; }
            public List<FactionInfo> Factions { get; set; }
        }

        public class FactionInfo
        {
            public string Name { get; set; }
            public string CurrentDefName { get; set; }
            public bool IsUnique { get; set; }
        }
    }
}