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
            // Store the current def, which might already be a unique copy
            this.originalDef = faction.def;

            ModCore.LogDebug("FactionUpgrader initialized for " + faction.Name +
                " with def " + faction.def.defName + " at tech level " + faction.def.techLevel.ToString());
        }

        public bool UpgradeToTechLevel(TechLevel targetLevel)
        {
            try
            {
                var settings = ModCore.Settings;

                // Ensure this faction has a unique def before modifying
                var uniqueDef = FactionDefManager.EnsureUniqueDef(faction);
                if (uniqueDef == null)
                {
                    Log.Error("[" + ModCore.ModId + "] Failed to ensure unique def for " + faction.Name);
                    return false;
                }

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
                ApplyDefToFaction(selectedDef);
                UpdateFactionAssets();

                ModCore.LogDebug("Upgraded " + faction.Name + " from " +
                    originalDef.techLevel.ToString() + " to " + targetLevel.ToString());
                return true;
            }
            catch (Exception e)
            {
                Log.Error("[" + ModCore.ModId + "] Failed to upgrade faction " + faction.Name + ": " + e.ToString());
                return false;
            }
        }

        private List<FactionDef> GetCandidateFactionDefs(TechLevel techLevel)
        {
            var settings = ModCore.Settings;
            var candidates = new List<FactionDef>();

            ModCore.LogDebug("Searching for faction defs at tech level: " + techLevel.ToString());

            foreach (var def in DefDatabase<FactionDef>.AllDefs)
            {
                // Skip our unique copies - we want original defs as templates
                if (def.defName.Contains("_LemProg_"))
                {
                    ModCore.LogDebug("  Skipping unique copy: " + def.defName);
                    continue;
                }

                if (def.techLevel != techLevel)
                {
                    ModCore.LogDebug("  " + def.defName + " wrong tech level: " + def.techLevel.ToString());
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

                // Check against original def name for filtering
                var originalDefName = FactionDefManager.GetOriginalDefName(def);
                if (!settings.IsFactionDefAllowed(originalDefName))
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
                    // Skip our unique copies
                    if (def.defName.Contains("_LemProg_"))
                        continue;

                    if (def.techLevel != techLevel)
                        continue;

                    if (!def.humanlikeFaction)
                        continue;

                    if (def.isPlayer)
                        continue;

                    if (def.hidden)
                        continue;

                    var originalDefName = FactionDefManager.GetOriginalDefName(def);
                    if (!settings.IsFactionDefAllowed(originalDefName))
                        continue;

                    candidates.Add(def);
                }

                ModCore.LogDebug("Found " + candidates.Count + " candidates without similarity filter");
            }

            return candidates;
        }

        private bool IsCompatibleFactionType(FactionDef candidate)
        {
            // Get the original def name for comparison (strip _LemProg_ suffix if present)
            var originalDefName = FactionDefManager.GetOriginalDefName(originalDef);
            var candidateDefName = FactionDefManager.GetOriginalDefName(candidate);

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

            // Check for similar keywords in original def names
            string[] keywords = new string[] { "Tribe", "Pirate", "Civil", "Empire", "Rough", "Gentle", "Savage", "Outlander" };
            foreach (var keyword in keywords)
            {
                if (originalDefName.Contains(keyword) && candidateDefName.Contains(keyword))
                {
                    ModCore.LogDebug("    Compatible: both contain keyword " + keyword);
                    return true;
                }
            }

            // Check for mod source similarity
            string[] originalParts = originalDefName.Split('_');
            string[] candidateParts = candidateDefName.Split('_');

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

        private void ApplyDefToFaction(FactionDef newDef)
        {
            var copier = new FactionDefCopier(faction, originalDef, newDef);
            copier.Execute();
        }

        private void UpdateFactionAssets()
        {
            UpdateSettlements();
            ClearPawnCaches();
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
            trader.TryDestroyStock();

            var regenerateMethod = AccessTools.Method(typeof(Settlement_TraderTracker), "RegenerateStock");
            if (regenerateMethod != null)
            {
                regenerateMethod.Invoke(trader, null);
            }
        }

        private void ClearPawnCaches()
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
    }
}