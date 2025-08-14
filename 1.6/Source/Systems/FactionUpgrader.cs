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

                ModCore.LogDebug("=== ANALYZING CANDIDATE DEFS ===");
                foreach (var candidate in candidateDefs.Take(3)) // Log first 3 candidates
                {
                    FactionDefAnalyzer.AnalyzeFactionDefAtUpgradeTime(candidate, "CANDIDATE");
                }

                var selectedDef = SelectBestCandidate(candidateDefs);

                ModCore.LogDebug("=== SELECTED DEF ANALYSIS ===");
                FactionDefAnalyzer.AnalyzeFactionDefAtUpgradeTime(selectedDef, "SELECTED FOR UPGRADE");

                // Store old def info for logging and potential rollback
                var oldDef = faction.def;
                var oldDefName = faction.def.defName;
                var oldTechLevel = faction.def.techLevel;

                ModCore.LogDebug("=== BEFORE DEF SWAP ===");
                FactionDefAnalyzer.CompareFactionDefStates(selectedDef, "before-swap", "selected-def");

                // Simple def swap - RimWorld handles all the persistence
                faction.def = selectedDef;

                ModCore.LogDebug("=== AFTER DEF SWAP ===");
                FactionDefAnalyzer.AnalyzeFactionDefAtUpgradeTime(faction.def, "POST-SWAP");

                // Clear any cached data that might reference the old def
            //    ClearPawnGenerationCaches();
                UpdateFactionAssets();

                ModCore.LogDebug("Upgraded " + faction.Name + " from " +
                    oldTechLevel.ToString() + " (" + oldDefName + ") to " +
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
            ModCore.LogDebug("=== VALIDATING FACTION DEF AT UPGRADE TIME ===");

            // Run real-time analysis to compare with startup
            FactionDefAnalyzer.AnalyzeFactionDefAtUpgradeTime(def, "PRE-UPGRADE VALIDATION");

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

            // CRITICAL: Check each pawn group maker thoroughly
            int validPawnGroupMakers = 0;
            foreach (var pgm in def.pawnGroupMakers)
            {
                if (pgm == null)
                {
                    ModCore.LogDebug("Found null pawn group maker in " + def.defName);
                    continue;
                }

                if (pgm.kindDef == null)
                {
                    ModCore.LogDebug("Pawn group maker has null kindDef in " + def.defName);
                    continue;
                }

                // Check that the pawn generation lists have content
                bool hasValidPawns = false;

                if (pgm.guards != null && pgm.guards.Count > 0)
                {
                    ModCore.LogDebug("  PGM " + pgm.kindDef.defName + " has " + pgm.guards.Count + " guards");
                    hasValidPawns = true;
                }

                if (pgm.carriers != null && pgm.carriers.Count > 0)
                {
                    ModCore.LogDebug("  PGM " + pgm.kindDef.defName + " has " + pgm.carriers.Count + " carriers");
                    hasValidPawns = true;
                }

                if (pgm.traders != null && pgm.traders.Count > 0)
                {
                    ModCore.LogDebug("  PGM " + pgm.kindDef.defName + " has " + pgm.traders.Count + " traders");
                    hasValidPawns = true;
                }

                if (!hasValidPawns)
                {
                    Log.Warning("[" + ModCore.ModId + "] Pawn group maker " + pgm.kindDef.defName +
                        " in " + def.defName + " has no guards, carriers, or traders - this will cause null options");
                    continue;
                }

                try
                {
                    // Try to access options safely - this will trigger generation
                    var options = pgm.options;
                    if (options != null && options.Count > 0)
                    {
                        validPawnGroupMakers++;
                        ModCore.LogDebug("  Valid pawn group maker: " + pgm.kindDef.defName + " with " + options.Count + " options");
                    }
                    else
                    {
                        Log.Warning("[" + ModCore.ModId + "] Pawn group maker " + pgm.kindDef.defName +
                            " generated null or empty options despite having pawn lists");
                    }
                }
                catch (Exception e)
                {
                    Log.Warning("[" + ModCore.ModId + "] Pawn group maker options generation failed for " +
                        def.defName + "." + pgm.kindDef.defName + ": " + e.Message);
                    continue;
                }
            }

            if (validPawnGroupMakers == 0)
            {
                Log.Warning("[" + ModCore.ModId + "] Faction def " + def.defName +
                    " has no viable pawn group makers (checked " + def.pawnGroupMakers.Count + " total). " +
                    "This def cannot be used for faction upgrades as it will cause raid generation to fail.");
                return false;
            }

            ModCore.LogDebug("Faction def " + def.defName + " validation passed (" +
                validPawnGroupMakers + "/" + def.pawnGroupMakers.Count + " viable pawn group makers)");
            return true;
        }

        private bool ValidateFactionState()
        {
            try
            {
                if (faction == null)
                {
                    Log.Error("[" + ModCore.ModId + "] Faction is null after upgrade");
                    return false;
                }

                if (faction.def == null)
                {
                    Log.Error("[" + ModCore.ModId + "] Faction def is null after upgrade");
                    return false;
                }

                // Test that the faction can generate minimum raid points
                if (faction.def.pawnGroupMakers != null)
                {
                    bool hasValidPawnGroupMaker = false;

                    foreach (var pgm in faction.def.pawnGroupMakers)
                    {
                        if (pgm == null)
                        {
                            Log.Warning("[" + ModCore.ModId + "] Found null pawn group maker in " + faction.def.defName);
                            continue;
                        }

                        try
                        {
                            // Ensure this pawn group maker is properly initialized
                            EnsurePawnGroupMakerIntegrity(pgm);

                            // Test basic pawn group functionality with more defensive approach
                            var testParms = new PawnGroupMakerParms();
                            testParms.faction = faction;
                            testParms.points = 100f; // Small test value

                            // Check if options are accessible
                            var options = pgm.options;
                            if (options == null)
                            {
                                Log.Warning("[" + ModCore.ModId + "] Pawn group maker has null options after integrity check");
                                continue;
                            }

                            // This is the critical test - can it calculate minimum points without null reference?
                            var minPoints = pgm.MinPointsToGenerateAnything(faction.def, testParms);
                            ModCore.LogDebug("Pawn group maker " + pgm.kindDef?.defName + " min points: " + minPoints);
                            hasValidPawnGroupMaker = true;
                        }
                        catch (Exception e)
                        {
                            Log.Warning("[" + ModCore.ModId + "] Pawn group maker failed validation test for " +
                                faction.def.defName + ": " + e.Message);
                            continue;
                        }
                    }

                    if (!hasValidPawnGroupMaker)
                    {
                        Log.Error("[" + ModCore.ModId + "] No valid pawn group makers found for faction " + faction.Name);
                        return false;
                    }
                }

                ModCore.LogDebug("Faction state validation passed for " + faction.Name);
                return true;
            }
            catch (Exception e)
            {
                Log.Error("[" + ModCore.ModId + "] Exception during faction state validation: " + e.ToString());
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
            // If similarity preference is disabled, all factions are compatible
            var settings = ModCore.Settings;
            if (!settings.preferSimilarFactionTypes)
            {
                return true;
            }

            // Use a scoring system instead of hard requirements
            int compatibilityScore = CalculateCompatibilityScore(candidate);

            // Require at least some compatibility (score > 0) but be more lenient
            bool isCompatible = compatibilityScore > 0;

            ModCore.LogDebug("    Compatibility score for " + candidate.defName + ": " + compatibilityScore +
                " (compatible: " + isCompatible + ")");

            return isCompatible;
        }

        private int CalculateCompatibilityScore(FactionDef candidate)
        {
            int score = 0;

            // High priority: Enemy status must match (this is important for gameplay)
            if (originalDef.permanentEnemy == candidate.permanentEnemy)
            {
                score += 10;
                ModCore.LogDebug("      +10 Same enemy status");
            }
            else
            {
                ModCore.LogDebug("      Enemy status mismatch - this is a major incompatibility");
                return 0; // This is a hard requirement - pirates shouldn't become peaceful outlanders
            }

            // Medium priority: Category tags
            if (originalDef.categoryTag != null && candidate.categoryTag == originalDef.categoryTag)
            {
                score += 5;
                ModCore.LogDebug("      +5 Same category tag");
            }

            // Medium priority: Similar faction type keywords
            string[] keywords = new string[] { "Tribe", "Pirate", "Civil", "Empire", "Rough", "Gentle", "Savage", "Outlander" };
            foreach (var keyword in keywords)
            {
                if (originalDef.defName.Contains(keyword) && candidate.defName.Contains(keyword))
                {
                    score += 3;
                    ModCore.LogDebug("      +3 Shared keyword: " + keyword);
                    break; // Only count one keyword match
                }
            }

            // Lower priority: Mod source similarity
            string[] originalParts = originalDef.defName.Split('_');
            string[] candidateParts = candidate.defName.Split('_');

            if (originalParts.Length > 0 && candidateParts.Length > 0 &&
                originalParts[0] == candidateParts[0])
            {
                score += 2;
                ModCore.LogDebug("      +2 Same mod prefix");
            }

            // Bonus: Natural enemy status match
            if (originalDef.naturalEnemy == candidate.naturalEnemy)
            {
                score += 1;
                ModCore.LogDebug("      +1 Same natural enemy status");
            }

            // Fallback: If no specific matches but both are basic vanilla-style factions
            if (score == 10) // Only has enemy status match
            {
                if (IsBasicHumanlikeFaction(originalDef) && IsBasicHumanlikeFaction(candidate))
                {
                    score += 2;
                    ModCore.LogDebug("      +2 Both are basic humanlike factions");
                }
            }

            return score;
        }

        private bool IsBasicHumanlikeFaction(FactionDef def)
        {
            // Check if this is a basic humanlike faction (not highly specialized)
            return def.humanlikeFaction &&
                   !def.hidden &&
                   !def.isPlayer &&
                   def.pawnGroupMakers != null &&
                   def.pawnGroupMakers.Count > 0;
        }

        private bool HasViablePawnGeneration(FactionDef def)
        {
            if (def.pawnGroupMakers == null || def.pawnGroupMakers.Count == 0)
            {
                ModCore.LogDebug("  " + def.defName + " has no pawn group makers");
                return false;
            }

            ModCore.LogDebug("  " + def.defName + " checking " + def.pawnGroupMakers.Count + " pawn group makers:");

            // Check if at least one pawn group maker has actual pawn generation content
            foreach (var pgm in def.pawnGroupMakers)
            {
                if (pgm == null)
                {
                    ModCore.LogDebug("    PGM is null");
                    continue;
                }

                if (pgm.kindDef == null)
                {
                    ModCore.LogDebug("    PGM has null kindDef");
                    continue;
                }

                ModCore.LogDebug("    PGM " + pgm.kindDef.defName + ":");
                ModCore.LogDebug("      guards: " + (pgm.guards?.Count ?? -1));
                ModCore.LogDebug("      carriers: " + (pgm.carriers?.Count ?? -1));
                ModCore.LogDebug("      traders: " + (pgm.traders?.Count ?? -1));

                // CRITICAL: Try to force initialization of the pawn group maker
                try
                {
                    // Force resolve references if not already done
                    if (pgm.guards == null)
                    {
                        ModCore.LogDebug("      Attempting to initialize guards...");
                        // Try to trigger initialization through reflection
                        var traverse = Traverse.Create(pgm);

                        // Some pawn group makers might need to be resolved
                        var resolveMethod = AccessTools.Method(typeof(PawnGroupMaker), "ResolveReferences");
                        if (resolveMethod != null)
                        {
                            resolveMethod.Invoke(pgm, null);
                            ModCore.LogDebug("      Called ResolveReferences on PGM");
                        }
                    }

                    // Check again after potential initialization
                    ModCore.LogDebug("      After init attempt:");
                    ModCore.LogDebug("        guards: " + (pgm.guards?.Count ?? -1));
                    ModCore.LogDebug("        carriers: " + (pgm.carriers?.Count ?? -1));
                    ModCore.LogDebug("        traders: " + (pgm.traders?.Count ?? -1));

                    // Check for any non-empty pawn lists
                    bool hasContent = false;

                    if (pgm.guards != null && pgm.guards.Count > 0)
                        hasContent = true;
                    else if (pgm.carriers != null && pgm.carriers.Count > 0)
                        hasContent = true;
                    else if (pgm.traders != null && pgm.traders.Count > 0)
                        hasContent = true;

                    if (hasContent)
                    {
                        ModCore.LogDebug("      Found content, testing options generation...");
                        // Quick test - can it generate options without crashing?
                        try
                        {
                            var options = pgm.options;
                            if (options != null && options.Count > 0)
                            {
                                ModCore.LogDebug("      SUCCESS: Generated " + options.Count + " options");
                                return true; // Found at least one working pawn group maker
                            }
                            else
                            {
                                ModCore.LogDebug("      Options generation returned null or empty");
                            }
                        }
                        catch (Exception e)
                        {
                            ModCore.LogDebug("      Options generation failed: " + e.Message);
                            continue; // This pawn group maker is broken, try others
                        }
                    }
                    else
                    {
                        ModCore.LogDebug("      No content found in any pawn lists");
                    }
                }
                catch (Exception e)
                {
                    ModCore.LogDebug("      Exception during PGM analysis: " + e.Message);
                    continue;
                }
            }

            ModCore.LogDebug("  " + def.defName + " has no viable pawn generation");
            return false; // No viable pawn group makers found
        }

        private void TryForceFactionDefInitialization(FactionDef def)
        {
            try
            {
                ModCore.LogDebug("  Forcing initialization for " + def.defName);

                // Try to force resolution of the faction def
                var resolveMethod = AccessTools.Method(typeof(FactionDef), "ResolveReferences");
                if (resolveMethod != null)
                {
                    resolveMethod.Invoke(def, null);
                    ModCore.LogDebug("    Called FactionDef.ResolveReferences");
                }

                // Try to force resolution of each pawn group maker
                if (def.pawnGroupMakers != null)
                {
                    foreach (var pgm in def.pawnGroupMakers)
                    {
                        if (pgm != null)
                        {
                            var pgmResolveMethod = AccessTools.Method(typeof(PawnGroupMaker), "ResolveReferences");
                            if (pgmResolveMethod != null)
                            {
                                pgmResolveMethod.Invoke(pgm, null);
                                ModCore.LogDebug("    Called PawnGroupMaker.ResolveReferences for " + pgm.kindDef?.defName);
                            }

                            // Also try to force post-load initialization
                            var postLoadMethod = AccessTools.Method(typeof(PawnGroupMaker), "PostLoadInit");
                            if (postLoadMethod != null)
                            {
                                postLoadMethod.Invoke(pgm, null);
                                ModCore.LogDebug("    Called PawnGroupMaker.PostLoadInit for " + pgm.kindDef?.defName);
                            }

                            // Log the state after initialization attempts
                            ModCore.LogDebug("    After init - guards: " + (pgm.guards?.Count ?? -1) +
                                ", carriers: " + (pgm.carriers?.Count ?? -1) +
                                ", traders: " + (pgm.traders?.Count ?? -1));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModCore.LogDebug("  Failed to force initialization for " + def.defName + ": " + e.Message);
            }
        }

        private FactionDef SelectBestCandidate(List<FactionDef> candidates)
        {
            var settings = ModCore.Settings;

            if (!settings.preferSimilarFactionTypes || candidates.Count == 1)
            {
                return candidates.RandomElement();
            }

            // Score candidates based on similarity using the same scoring system
            var scored = new List<ScoredFactionDef>();
            foreach (var candidate in candidates)
            {
                scored.Add(new ScoredFactionDef
                {
                    Def = candidate,
                    Score = CalculateCompatibilityScore(candidate) // Reuse the same scoring logic
                });
            }

            // Sort by score descending
            scored.Sort((a, b) => b.Score.CompareTo(a.Score));

            // Select from top candidates with some randomness
            int topCount = Math.Max(1, scored.Count / 3);
            var topCandidates = scored.Take(topCount).ToList();

            var selected = topCandidates.RandomElement();
            ModCore.LogDebug("    Selected " + selected.Def.defName + " with score " + selected.Score +
                " from " + topCandidates.Count + " top candidates");

            return selected.Def;
        }

        private class ScoredFactionDef
        {
            public FactionDef Def { get; set; }
            public int Score { get; set; }
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

                // Clear and reinitialize options cache
                var traverse = Traverse.Create(pawnGroupMaker);
                var optionsField = traverse.Field("_options");
                if (optionsField.FieldExists())
                {
                    optionsField.SetValue(null);
                }

                // Force options regeneration and ensure it's not null
                try
                {
                    var options = pawnGroupMaker.options;
                    if (options == null)
                    {
                        Log.Warning("[" + ModCore.ModId + "] PawnGroupMaker options still null after regeneration, " +
                            "this may indicate a deeper issue with the faction def");

                        // As a last resort, try to set an empty options list
                        if (optionsField.FieldExists())
                        {
                            optionsField.SetValue(new List<PawnGenOption>());
                        }
                    }
                    else
                    {
                        ModCore.LogDebug("Successfully ensured " + options.Count + " pawn options for pawn group maker");
                    }
                }
                catch (Exception e)
                {
                    Log.Warning("[" + ModCore.ModId + "] Could not regenerate pawn options: " + e.Message);

                    // Set empty list as fallback
                    if (optionsField.FieldExists())
                    {
                        optionsField.SetValue(new List<PawnGenOption>());
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("[" + ModCore.ModId + "] Failed to ensure pawn group maker integrity: " + e.Message);
            }
        }

        private void UpdateFactionAssets()
        {
          //  UpdateSettlements();
            ClearWorldCaches();
        }

        private void ClearWorldCaches()
        {
            try
            {
                // Clear world-level caches that might reference old faction defs
                if (Find.World?.factionManager != null)
                {
                    var factionManagerTraverse = Traverse.Create(Find.World.factionManager);

                    // Clear any cached faction lists or raid-related caches
                    string[] worldCacheFields = new string[]
                    {
                        "cachedAllFactions", "cachedAllFactionsInViewOrder",
                        "cachedNonPlayerFactions", "cachedHostileFactions"
                    };

                    foreach (var fieldName in worldCacheFields)
                    {
                        var field = factionManagerTraverse.Field(fieldName);
                        if (field.FieldExists())
                        {
                            field.SetValue(null);
                            ModCore.LogDebug("Cleared world cache: " + fieldName);
                        }
                    }
                }

                // Clear storyteller caches that might affect raid generation
                if (Find.Storyteller != null)
                {
                    var storytellerTraverse = Traverse.Create(Find.Storyteller);
                    var incidentQueueField = storytellerTraverse.Field("incidentQueue");
                    if (incidentQueueField.FieldExists())
                    {
                        // Don't clear the queue, but we might need to in the future
                        ModCore.LogDebug("Storyteller incident queue exists");
                    }
                }

                ModCore.LogDebug("Cleared world-level caches for faction upgrade");
            }
            catch (Exception e)
            {
                ModCore.LogDebug("Failed to clear world caches: " + e.Message);
            }
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