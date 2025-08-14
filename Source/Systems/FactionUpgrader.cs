using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace LemProgress.Systems
{
    public class FactionUpgrader
    {
        private readonly Faction faction;
        private readonly FactionDef originalDef;
        private static readonly Random random = new Random();

        public FactionUpgrader(Faction faction)
        {
            if (faction == null)
                throw new ArgumentNullException("faction");

            this.faction = faction;
            this.originalDef = faction.def;

            ModCore.LogDebug("FactionUpgrader initialized for " + faction.Name +
                " with def " + faction.def.defName + " at tech level " + faction.def.techLevel.ToString());
        }

        public bool UpgradeToTechLevel(TechLevel targetLevel)
        {
            try
            {
                var settings = ModCore.Settings;

                // Check if downgrade is allowed
                if (originalDef.techLevel > targetLevel && !settings.allowDowngrades)
                {
                    ModCore.LogDebug("Downgrade blocked for " + faction.Name);
                    return false;
                }

                var candidateDefs = GetCandidateFactionDefs(targetLevel);
                if (!candidateDefs.Any())
                {
                    Log.Warning("[" + ModCore.ModId + "] No faction defs available for " + targetLevel.ToString());
                    return false;
                }

                var selectedDef = SelectBestCandidate(candidateDefs);

                // Validate the selected def before applying it
                if (!ValidateFactionDef(selectedDef))
                {
                    Log.Warning("[" + ModCore.ModId + "] Selected faction def " + selectedDef.defName +
                        " failed validation, upgrade cancelled for " + faction.Name);
                    return false;
                }

                // Simple def swap - RimWorld handles all the persistence
                var oldDefName = faction.def.defName;
                faction.def = selectedDef;

                // Clear any cached data that might reference the old def
                ClearPawnGenerationCaches();
                UpdateFactionAssets();

                ModCore.LogDebug("Upgraded " + faction.Name + " from " +
                    originalDef.techLevel.ToString() + " (" + oldDefName + ") to " +
                    targetLevel.ToString() + " (" + selectedDef.defName + ")");
                return true;
            }
            catch (Exception e)
            {
                Log.Error("[" + ModCore.ModId + "] Failed to upgrade faction " + faction.Name + ": " + e.ToString());
                return false;
            }
        }

        private bool ValidateFactionDef(FactionDef def)
        {
            if (def == null)
            {
                ModCore.LogDebug("Faction def is null");
                return false;
            }

            if (def.pawnGroupMakers == null)
            {
                ModCore.LogDebug("Faction def " + def.defName + " has null pawn group makers");
                return false;
            }

            if (def.pawnGroupMakers.Count == 0)
            {
                ModCore.LogDebug("Faction def " + def.defName + " has no pawn group makers");
                return false;
            }

            // Check if at least one pawn group maker has viable options
            foreach (var pgm in def.pawnGroupMakers)
            {
                if (pgm == null) continue;

                try
                {
                    // Try to access options - this will trigger generation
                    var options = pgm.options;
                    if (options != null && options.Count > 0)
                    {
                        ModCore.LogDebug("Faction def " + def.defName + " validation passed");
                        return true; // At least one working pawn group maker
                    }
                }
                catch (Exception e)
                {
                    ModCore.LogDebug("Pawn group maker validation failed: " + e.Message);
                    continue;
                }
            }

            Log.Warning("[" + ModCore.ModId + "] Faction def " + def.defName +
                " has no viable pawn group makers");
            return false;
        }

        private List<FactionDef> GetCandidateFactionDefs(TechLevel techLevel)
        {
            var settings = ModCore.Settings;
            var candidates = new List<FactionDef>();

            ModCore.LogDebug("Searching for faction defs at tech level: " + techLevel.ToString());

            foreach (var def in DefDatabase<FactionDef>.AllDefs)
            {
                if (def.techLevel != techLevel)
                {
                    continue;
                }

                if (!def.humanlikeFaction)
                {
                    ModCore.LogDebug("  " + def.defName + " not humanlike");
                    continue;
                }

                if (def.isPlayer)
                {
                    ModCore.LogDebug("  " + def.defName + " is player faction");
                    continue;
                }

                if (def.hidden)
                {
                    ModCore.LogDebug("  " + def.defName + " is hidden");
                    continue;
                }

                // Check against blacklist/whitelist
                if (!settings.IsFactionDefAllowed(def.defName))
                {
                    ModCore.LogDebug("  " + def.defName + " is blacklisted");
                    continue;
                }

                if (settings.preferSimilarFactionTypes && !IsCompatibleFactionType(def))
                {
                    ModCore.LogDebug("  " + def.defName + " not compatible type");
                    continue;
                }

                ModCore.LogDebug("  Adding candidate: " + def.defName);
                candidates.Add(def);
            }

            ModCore.LogDebug("Found " + candidates.Count + " candidate faction defs for " + techLevel.ToString());

            // If no candidates found with similarity preference, try without it
            if (candidates.Count == 0 && settings.preferSimilarFactionTypes)
            {
                ModCore.LogDebug("No compatible types found, trying without similarity filter...");

                foreach (var def in DefDatabase<FactionDef>.AllDefs)
                {
                    if (def.techLevel != techLevel)
                        continue;

                    if (!def.humanlikeFaction)
                        continue;

                    if (def.isPlayer)
                        continue;

                    if (def.hidden)
                        continue;

                    if (!settings.IsFactionDefAllowed(def.defName))
                        continue;

                    candidates.Add(def);
                }

                ModCore.LogDebug("Found " + candidates.Count + " candidates without similarity filter");
            }

            return candidates;
        }

        private bool IsCompatibleFactionType(FactionDef candidate)
        {
            // Prefer same category
            if (originalDef.categoryTag != null && candidate.categoryTag == originalDef.categoryTag)
            {
                ModCore.LogDebug("    Compatible: same category tag");
                return true;
            }

            // Check hostility compatibility
            if (originalDef.permanentEnemy != candidate.permanentEnemy)
            {
                ModCore.LogDebug("    Not compatible: different enemy status");
                return false;
            }

            // Check for similar keywords in def names
            string[] keywords = new string[] { "Tribe", "Pirate", "Civil", "Empire", "Rough", "Gentle", "Savage", "Outlander" };
            foreach (var keyword in keywords)
            {
                if (originalDef.defName.Contains(keyword) && candidate.defName.Contains(keyword))
                {
                    ModCore.LogDebug("    Compatible: both contain keyword " + keyword);
                    return true;
                }
            }

            // Check for mod source similarity
            string[] originalParts = originalDef.defName.Split('_');
            string[] candidateParts = candidate.defName.Split('_');

            if (originalParts.Length > 0 && candidateParts.Length > 0 &&
                originalParts[0] == candidateParts[0])
            {
                ModCore.LogDebug("    Compatible: same mod prefix");
                return true;
            }

            ModCore.LogDebug("    Not compatible: no matching criteria");
            return false;
        }

        private FactionDef SelectBestCandidate(List<FactionDef> candidates)
        {
            var settings = ModCore.Settings;

            if (!settings.preferSimilarFactionTypes || candidates.Count == 1)
            {
                return candidates.RandomElement();
            }

            // Score candidates based on similarity
            var scored = new List<ScoredFactionDef>();
            foreach (var candidate in candidates)
            {
                scored.Add(new ScoredFactionDef
                {
                    Def = candidate,
                    Score = CalculateSimilarityScore(candidate)
                });
            }

            // Sort by score descending
            scored.Sort((a, b) => b.Score.CompareTo(a.Score));

            // Select from top candidates with some randomness
            int topCount = Math.Max(1, scored.Count / 3);
            var topCandidates = scored.Take(topCount).ToList();
            return topCandidates.RandomElement().Def;
        }

        private class ScoredFactionDef
        {
            public FactionDef Def { get; set; }
            public int Score { get; set; }
        }

        private int CalculateSimilarityScore(FactionDef candidate)
        {
            int score = 0;

            if (candidate.categoryTag == originalDef.categoryTag)
                score += 10;
            if (candidate.permanentEnemy == originalDef.permanentEnemy)
                score += 5;
            if (candidate.naturalEnemy == originalDef.naturalEnemy)
                score += 3;

            // Check for mod source similarity
            string[] original = originalDef.defName.Split('_');
            string[] candidateParts = candidate.defName.Split('_');

            if (original.Length > 0 && candidateParts.Length > 0 &&
                original[0] == candidateParts[0])
            {
                score += 7;
            }

            return score;
        }

        private void ClearPawnGenerationCaches()
        {
            if (faction.def.pawnGroupMakers == null) return;

            foreach (var pawnGroupMaker in faction.def.pawnGroupMakers)
            {
                var traverse = Traverse.Create(pawnGroupMaker);

                // Clear all possible cache fields
                string[] cacheFields = new string[] { "_options", "cachedOptions", "options" };
                foreach (var fieldName in cacheFields)
                {
                    var field = traverse.Field(fieldName);
                    if (field.FieldExists())
                    {
                        field.SetValue(null);
                    }
                }

                // Force regeneration of options to ensure they're not null
                try
                {
                    var options = pawnGroupMaker.options; // This will trigger regeneration
                    ModCore.LogDebug("Regenerated " + options.Count + " pawn options for " + pawnGroupMaker.kindDef?.defName);
                }
                catch (Exception e)
                {
                    Log.Warning("[" + ModCore.ModId + "] Failed to regenerate pawn options for " + faction.Name + ": " + e.Message);

                    // If regeneration fails, ensure we have empty lists instead of null
                    EnsurePawnGroupMakerIntegrity(pawnGroupMaker);
                }
            }

            // Clear faction-level caches
            var factionTraverse = Traverse.Create(faction);
            string[] factionCacheFields = new string[]
            {
                "cachedPawnGenerator", "cachedRandomPawnGenerator",
                "cachedPawnKinds", "cachedFighters"
            };

            foreach (var fieldName in factionCacheFields)
            {
                var field = factionTraverse.Field(fieldName);
                if (field.FieldExists())
                {
                    field.SetValue(null);
                }
            }

            ModCore.LogDebug("Cleared pawn caches for " + faction.Name);
        }

        private void EnsurePawnGroupMakerIntegrity(PawnGroupMaker pawnGroupMaker)
        {
            try
            {
                // Ensure critical lists are initialized
                if (pawnGroupMaker.guards == null)
                    pawnGroupMaker.guards = new List<PawnGenOption>();

                if (pawnGroupMaker.carriers == null)
                    pawnGroupMaker.carriers = new List<PawnGenOption>();

                if (pawnGroupMaker.traders == null)
                    pawnGroupMaker.traders = new List<PawnGenOption>();

                // Try to get options through reflection if the property is problematic
                var traverse = Traverse.Create(pawnGroupMaker);
                var optionsField = traverse.Field("_options");
                if (optionsField.FieldExists() && optionsField.GetValue() == null)
                {
                    optionsField.SetValue(new List<PawnGenOption>());
                    ModCore.LogDebug("Initialized empty options list for problematic pawn group maker");
                }
            }
            catch (Exception e)
            {
                Log.Warning("[" + ModCore.ModId + "] Failed to ensure pawn group maker integrity: " + e.Message);
            }
        }

        private void UpdateFactionAssets()
        {
            UpdateSettlements();
        }

        private void UpdateSettlements()
        {
            var settlements = new List<Settlement>();
            foreach (var worldObject in Find.WorldObjects.Settlements)
            {
                if (worldObject.Faction == faction)
                    settlements.Add(worldObject);
            }

            foreach (var settlement in settlements)
            {
                if (settlement.trader != null)
                {
                    RegenerateTraderStock(settlement.trader);
                }
            }
        }

        private void RegenerateTraderStock(Settlement_TraderTracker trader)
        {
            try
            {
                trader.TryDestroyStock();

                var regenerateMethod = AccessTools.Method(typeof(Settlement_TraderTracker), "RegenerateStock");
                if (regenerateMethod != null)
                {
                    regenerateMethod.Invoke(trader, null);
                }
            }
            catch (Exception e)
            {
                ModCore.LogDebug("Failed to regenerate trader stock: " + e.Message);
            }
        }
    }
}