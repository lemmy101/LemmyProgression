using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace LemProgress
{
    public static class FactionDefUtility
    {
        /// <summary>
        /// Gets all faction defs that match the specified tech level and aren't currently in the world
        /// </summary>
        /// <param name="techLevel">The tech level to filter by</param>
        /// <param name="additionalFilter">Optional additional filter delegate</param>
        /// <returns>List of faction defs that match criteria</returns>
        public static List<FactionDef> GetAvailableFactionDefs(
            TechLevel techLevel,
            Predicate<FactionDef> additionalFilter = null)
        {
            // Get all currently active faction defs in the world
            var activeFactionDefs = GetActiveFactionDefs();

            Log.Message($"LemProg: " + activeFactionDefs.Count + " potential factions.");

            // Filter all faction defs
            var availableFactionDefs = DefDatabase<FactionDef>.AllDefs
                .Where(def =>
                    // Match tech level
                    def.techLevel == techLevel &&

                    // Not currently in the world
                    //  !activeFactionDefs.Contains(def) &&
                    def.humanlikeFaction &&
                    // Not a player faction
                    !def.isPlayer &&

                    // Not hidden (some factions are hidden for special events)
                    !def.hidden &&

                    // Apply additional filter if provided
                    (additionalFilter == null || additionalFilter(def))
                )
                .ToList();

            return availableFactionDefs;
        }

       
        /// <summary>
        /// Helper method to get all faction defs currently active in the world
        /// </summary>
        private static HashSet<FactionDef> GetActiveFactionDefs()
        {
            if (Find.World?.factionManager?.AllFactions == null)
                return new HashSet<FactionDef>();

            return Find.World.factionManager.AllFactions
                .Where(f => f.def != null)
                .Select(f => f.def)
                .ToHashSet();
        }

        private static bool IsSimilarFactionType(FactionDef def1, FactionDef def2)
        {
            // Check if factions are similar enough to be considered the same "type"
            // You can customize this logic based on your needs

            // Check if both are hostile or both are not
            if (def1.permanentEnemy != def2.permanentEnemy) return false;

            // Check if they have similar culture/category
            if (def1.categoryTag != null && def2.categoryTag != null)
            {
                return def1.categoryTag == def2.categoryTag;
            }

            // Check if they're from the same mod (similar naming convention)
            var mod1Prefix = def1.defName.Split('_').FirstOrDefault();
            var mod2Prefix = def2.defName.Split('_').FirstOrDefault();
            if (mod1Prefix != null && mod2Prefix != null && mod1Prefix == mod2Prefix)
            {
                return true;
            }

            // Check for similar keywords in def names
            var keywords = new[] { "Tribe", "Pirate", "Civil", "Empire", "Rough", "Gentle" };
            foreach (var keyword in keywords)
            {
                if (def1.defName.Contains(keyword) && def2.defName.Contains(keyword))
                    return true;
            }

            return false;
        }


        /// <summary>
        /// Copies all def properties from source def to target faction's def, preserving name
        /// </summary>
        /// <param name="targetFaction">The faction to modify</param>
        /// <param name="sourceDef">The def to copy properties from</param>
        /// <param name="preserveOptions">Options for what to preserve from the original</param>
        public static void CopyDefToFaction(
            Faction targetFaction,
            FactionDef sourceDef,
            DefCopyOptions preserveOptions = null)
        {
            if (targetFaction == null || sourceDef == null)
            {
                Log.Error("LemProg: Cannot copy def - null faction or source def");
                return;
            }

            if (targetFaction.IsPlayer)
            {
                Log.Warning("LemProg: Modifying player faction def - this may cause issues");
            }

            preserveOptions = preserveOptions ?? new DefCopyOptions();
            var originalDef = targetFaction.def;

            Log.Message($"LemProg: Copying def properties from {sourceDef.defName} to {targetFaction.Name}");

            // Store preserved values
            var preservedValues = new PreservedDefValues(originalDef, preserveOptions);

            // Copy all fields using reflection
            CopyDefFields(sourceDef, originalDef, preserveOptions);

            // Restore preserved values
            preservedValues.RestoreTo(originalDef);

            // Update faction-specific elements
            UpdateFactionAfterDefChange(targetFaction, originalDef, sourceDef);

            Log.Message($"LemProg: Successfully updated {targetFaction.Name} with properties from {sourceDef.label}");
        }

        /// <summary>
        /// Copies def fields from source to target
        /// </summary>
        private static void CopyDefFields(FactionDef source, FactionDef target, DefCopyOptions options)
        {
            // Get all fields from FactionDef
            var fields = typeof(FactionDef).GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                // Skip fields we should preserve
                if (ShouldSkipField(field.Name, options)) continue;

                try
                {
                    var value = field.GetValue(source);

                    // Deep copy collections to avoid reference issues
                    if (value is System.Collections.IList list && !(value is string))
                    {
                        value = CreateListCopy(list, field.FieldType);
                    }

                    field.SetValue(target, value);
                }
                catch (Exception e)
                {
                    Log.Warning($"LemProg: Failed to copy field {field.Name}: {e.Message}");
                }
            }

            // Also copy properties if any
            var properties = typeof(FactionDef).GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                if (ShouldSkipField(prop.Name, options)) continue;

                try
                {
                    var value = prop.GetValue(source);
                    prop.SetValue(target, value);
                }
                catch (Exception e)
                {
                    Log.Warning($"LemProg: Failed to copy property {prop.Name}: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Creates a copy of a list to avoid reference issues
        /// </summary>
        private static object CreateListCopy(System.Collections.IList sourceList, Type fieldType)
        {
            if (sourceList == null) return null;

            var elementType = fieldType.IsArray
                ? fieldType.GetElementType()
                : fieldType.GetGenericArguments().FirstOrDefault() ?? typeof(object);

            var newList = Activator.CreateInstance(
                typeof(List<>).MakeGenericType(elementType)) as System.Collections.IList;

            foreach (var item in sourceList)
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
        /// Determines if a field should be skipped during copying
        /// </summary>
        private static bool ShouldSkipField(string fieldName, DefCopyOptions options)
        {
            // Always skip these core identity fields
            if (fieldName == "defName" || fieldName == "shortHash" || fieldName == "index")
                return true;

            // Check preservation options
            if (options.PreserveName && (fieldName == "label" || fieldName == "fixedName"))
                return true;

            if (options.PreserveDescription && fieldName == "description")
                return true;

            if (options.PreserveIcon && (fieldName == "factionIcon" || fieldName == "settlementTexture"))
                return true;

            if (options.PreserveColor && fieldName == "colorSpectrum")
                return true;

            if (options.PreserveRelations &&
                (fieldName == "permanentEnemy" || fieldName == "naturalEnemy" ||
                 fieldName == "goodwillDailyGain" || fieldName == "goodwillDailyFall"))
                return true;

            if (options.PreserveExpansion && fieldName == "expansionRate")
                return true;

            // Check custom field preservation
            if (options.CustomPreserveFields != null &&
                options.CustomPreserveFields.Contains(fieldName))
                return true;

            return false;
        }

        /// <summary>
        /// Upgrades a faction's def to a higher tech level while preserving identity
        /// </summary>
        public static void UpgradeFactionDef(
            Faction faction,
            TechLevel targetTechLevel,
            DefCopyOptions preserveOptions = null)
        {
            preserveOptions = preserveOptions ?? DefCopyOptions.PreserveIdentity();

            var availableDefs = GetAvailableFactionDefs(
                targetTechLevel,
                def => IsSimilarFactionType(faction.def, def)
            );

            if (!availableDefs.Any())
            {
                availableDefs = GetAvailableFactionDefs(targetTechLevel);
            }

            if (!availableDefs.Any())
            {
                Log.Warning($"LemProg: No faction defs available at {targetTechLevel}");
                return;
            }

            var chosenDef = availableDefs.RandomElement();
            CopyDefToFaction(faction, chosenDef, preserveOptions);
        }

        /// <summary>
        /// Updates faction after def change - ENHANCED VERSION
        /// </summary>
        private static void UpdateFactionAfterDefChange(Faction faction, FactionDef modifiedDef, FactionDef sourceDef)
        {
            // Update tech level-dependent aspects
            if (modifiedDef.techLevel != sourceDef.techLevel)
            {
                Log.Message($"LemProg: Tech level changed from {modifiedDef.techLevel} to {sourceDef.techLevel}");

                // Update all settlements' trader stock
                var settlements = Find.WorldObjects.Settlements
                    .Where(s => s.Faction == faction);

                foreach (var settlement in settlements)
                {
                    if (settlement.trader != null)
                    {
                        settlement.trader.TryDestroyStock();
                        Traverse.Create(settlement.trader).Method("RegenerateStock").GetValue();
                    }
                }
            }

            // CRITICAL: Clear pawn group maker caches
            ClearPawnGroupMakerCaches(faction);

            // Force recache of faction's fighters
            RecacheFactionPawnGroups(faction);

            // Refresh faction name if needed
            if (!string.IsNullOrEmpty(modifiedDef.fixedName))
            {
                faction.Name = modifiedDef.fixedName;
            }
        }

        /// <summary>
        /// Clears all cached pawn group data
        /// </summary>
        private static void ClearPawnGroupMakerCaches(Faction faction)
        {
            if (faction.def.pawnGroupMakers == null) return;

            foreach (var pawnGroupMaker in faction.def.pawnGroupMakers)
            {
                // Clear the cached options using Traverse
                var traverse = Traverse.Create(pawnGroupMaker);

                // PawnGroupMaker has a private field "_options" that caches generated options
                var cachedOptionsField = traverse.Field("_options");
                if (cachedOptionsField.FieldExists())
                {
                    cachedOptionsField.SetValue(null);
                    Log.Message($"LemProg: Cleared cached options for pawn group maker {pawnGroupMaker.kindDef}");
                }

                // Also clear any other cached data
                var cachedField = traverse.Field("cachedOptions");
                if (cachedField.FieldExists())
                {
                    cachedField.SetValue(null);
                }
            }

            // Clear faction's cached data
            ClearFactionCaches(faction);
        }

        /// <summary>
        /// Clears faction-level caches
        /// </summary>
        private static void ClearFactionCaches(Faction faction)
        {
            var traverse = Traverse.Create(faction);

            // Clear any cached pawn generator if it exists
            var pawnGeneratorField = traverse.Field("cachedPawnGenerator");
            if (pawnGeneratorField.FieldExists())
            {
                pawnGeneratorField.SetValue(null);
            }

            // Clear any other faction-level caches
            var allCachedFields = new[] { "cachedRandomPawnGenerator", "cachedPawnKinds", "cachedFighters" };
            foreach (var fieldName in allCachedFields)
            {
                var field = traverse.Field(fieldName);
                if (field.FieldExists())
                {
                    field.SetValue(null);
                }
            }
        }

        /// <summary>
        /// Forces recalculation of pawn groups
        /// </summary>
        private static void RecacheFactionPawnGroups(Faction faction)
        {
            // Force regeneration by accessing the pawn group makers
            if (faction.def.pawnGroupMakers != null)
            {
                foreach (var pawnGroupMaker in faction.def.pawnGroupMakers)
                {
                    // This forces recalculation of options
                    _ = pawnGroupMaker.options;
                }
            }

            Log.Message($"LemProg: Forced pawn group recache for {faction.Name}");
        }

        /// <summary>
        /// Alternative method: Completely rebuild pawn group makers from source
        /// </summary>
        private static void RebuildPawnGroupMakers(FactionDef targetDef, FactionDef sourceDef)
        {
            if (sourceDef.pawnGroupMakers == null) return;

            // Clear existing
            targetDef.pawnGroupMakers?.Clear();
            targetDef.pawnGroupMakers = new List<PawnGroupMaker>();

            // Deep copy pawn group makers
            foreach (var sourcePGM in sourceDef.pawnGroupMakers)
            {
                var newPGM = new PawnGroupMaker();

                // Copy all fields
                foreach (var field in typeof(PawnGroupMaker).GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    try
                    {
                        var value = field.GetValue(sourcePGM);

                        // Deep copy lists
                        if (value is System.Collections.IList list && !(value is string))
                        {
                            value = CreateListCopy(list, field.FieldType);
                        }

                        field.SetValue(newPGM, value);
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"LemProg: Failed to copy PawnGroupMaker field {field.Name}: {e.Message}");
                    }
                }

                targetDef.pawnGroupMakers.Add(newPGM);
            }

            Log.Message($"LemProg: Rebuilt {targetDef.pawnGroupMakers.Count} pawn group makers");
        }

        /// <summary>
        /// Enhanced version of CopyDefToFaction that properly handles pawn generation
        /// </summary>
        public static void CopyDefToFactionEnhanced(
            Faction targetFaction,
            FactionDef sourceDef,
            DefCopyOptions preserveOptions = null)
        {
            if (targetFaction == null || sourceDef == null)
            {
                Log.Error("LemProg: Cannot copy def - null faction or source def");
                return;
            }

            if (targetFaction.IsPlayer)
            {
                Log.Warning("LemProg: Modifying player faction def - this may cause issues");
            }

            preserveOptions = preserveOptions ?? new DefCopyOptions();
            var originalDef = targetFaction.def;

            Log.Message($"LemProg: Copying def properties from {sourceDef.defName} to {targetFaction.Name}");

            // Store preserved values
            var preservedValues = new PreservedDefValues(originalDef, preserveOptions);

            // Copy all fields EXCEPT pawn group makers initially
            CopyDefFieldsExcept(sourceDef, originalDef, preserveOptions, new[] { "pawnGroupMakers" });

            // Handle pawn group makers specially
            if (!preserveOptions.PreservePawnGroups)
            {
                RebuildPawnGroupMakers(originalDef, sourceDef);
            }

            // Restore preserved values
            preservedValues.RestoreTo(originalDef);

            // Clear all caches
            ClearPawnGroupMakerCaches(targetFaction);

            // Update faction-specific elements
            UpdateFactionAfterDefChange(targetFaction, originalDef, sourceDef);

            // Force immediate recache
            RecacheFactionPawnGroups(targetFaction);

            Log.Message($"LemProg: Successfully updated {targetFaction.Name} with properties from {sourceDef.label}");
        }

        /// <summary>
        /// Copy def fields except specified ones
        /// </summary>
        private static void CopyDefFieldsExcept(FactionDef source, FactionDef target, DefCopyOptions options, string[] exceptFields)
        {
            var fields = typeof(FactionDef).GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                // Skip excepted fields
                if (exceptFields.Contains(field.Name)) continue;

                // Skip fields we should preserve
                if (ShouldSkipField(field.Name, options)) continue;

                try
                {
                    var value = field.GetValue(source);

                    // Deep copy collections to avoid reference issues
                    if (value is System.Collections.IList list && !(value is string))
                    {
                        value = CreateListCopy(list, field.FieldType);
                    }

                    field.SetValue(target, value);
                }
                catch (Exception e)
                {
                    Log.Warning($"LemProg: Failed to copy field {field.Name}: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Force refresh all faction pawn generation (nuclear option)
        /// </summary>
        public static void ForceRefreshAllFactionPawnGeneration()
        {
            foreach (var faction in Find.FactionManager.AllFactions)
            {
                if (!faction.IsPlayer)
                {
                    ClearPawnGroupMakerCaches(faction);
                    RecacheFactionPawnGroups(faction);
                }
            }

            Log.Message("LemProg: Force refreshed all faction pawn generation");
        }
    }

    /// <summary>
    /// Enhanced DefCopyOptions with pawn group preservation
    /// </summary>
    public class DefCopyOptions
    {
        public bool PreserveName { get; set; } = true;
        public bool PreserveDescription { get; set; } = true;
        public bool PreserveIcon { get; set; } = false;
        public bool PreserveColor { get; set; } = true;
        public bool PreserveRelations { get; set; } = true;
        public bool PreserveExpansion { get; set; } = false;
        public bool PreservePawnGroups { get; set; } = false; // NEW
        public List<string> CustomPreserveFields { get; set; }

        // Updated preset configurations
        public static DefCopyOptions PreserveIdentity()
        {
            return new DefCopyOptions
            {
                PreserveName = true,
                PreserveDescription = true,
                PreserveIcon = true,
                PreserveColor = true,
                PreserveRelations = false,
                PreserveExpansion = false,
                PreservePawnGroups = false // Copy new pawn groups
            };
        }

        public static DefCopyOptions TechUpgradeOnly()
        {
            return new DefCopyOptions
            {
                PreserveName = true,
                PreserveDescription = true,
                PreserveIcon = true,
                PreserveColor = true,
                PreserveRelations = true,
                PreserveExpansion = true,
                PreservePawnGroups = false // This ensures new tech equipment
            };
        }
    }


    /// <summary>
    /// Helper class to store and restore preserved values
    /// </summary>
    internal class PreservedDefValues
    {
        private readonly Dictionary<string, object> preservedFields = new Dictionary<string, object>();
        private readonly DefCopyOptions options;

        public PreservedDefValues(FactionDef def, DefCopyOptions options)
        {
            this.options = options;

            // Always preserve these
            preservedFields["defName"] = def.defName;
            preservedFields["shortHash"] = def.shortHash;
            preservedFields["index"] = def.index;

            // Conditionally preserve based on options
            if (options.PreserveName)
            {
                preservedFields["label"] = def.label;
                preservedFields["fixedName"] = def.fixedName;
            }

            if (options.PreserveDescription)
                preservedFields["description"] = def.description;

            if (options.PreserveIcon)
            {
                preservedFields["factionIcon"] = def.FactionIcon;
                if (def.SettlementTexture != null)
                    preservedFields["settlementTexture"] = def.SettlementTexture;
            }

            if (options.PreserveColor && def.colorSpectrum != null)
                preservedFields["colorSpectrum"] = def.colorSpectrum;

            if (options.PreserveRelations)
            {
                preservedFields["permanentEnemy"] = def.permanentEnemy;
                preservedFields["naturalEnemy"] = def.naturalEnemy;
            }

        }

        public void RestoreTo(FactionDef def)
        {
            foreach (var kvp in preservedFields)
            {
                var field = typeof(FactionDef).GetField(kvp.Key,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (field != null)
                {
                    field.SetValue(def, kvp.Value);
                }
                else
                {
                    var prop = typeof(FactionDef).GetProperty(kvp.Key,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    if (prop != null && prop.CanWrite)
                    {
                        prop.SetValue(def, kvp.Value);
                    }
                }
            }
        }
    }
}
