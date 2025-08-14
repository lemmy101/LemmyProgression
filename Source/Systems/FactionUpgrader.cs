using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

                if (settings.preferSimilarFactionTypes && !IsCompatibleFactionType(def))
                    continue;

                candidates.Add(def);
            }

            return candidates;
        }

        private bool IsCompatibleFactionType(FactionDef candidate)
        {
            // Prefer same category
            if (originalDef.categoryTag != null && candidate.categoryTag == originalDef.categoryTag)
                return true;

            // Check hostility compatibility
            if (originalDef.permanentEnemy != candidate.permanentEnemy)
                return false;

            // Check for similar keywords in def names
            string[] keywords = new string[] { "Tribe", "Pirate", "Civil", "Empire", "Rough", "Gentle", "Savage" };
            foreach (var keyword in keywords)
            {
                if (originalDef.defName.Contains(keyword) && candidate.defName.Contains(keyword))
                    return true;
            }

            return true;
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
