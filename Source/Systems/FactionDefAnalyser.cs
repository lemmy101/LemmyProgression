using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace LemProgress.Systems
{
    [StaticConstructorOnStartup]
    public static class FactionDefAnalyzer
    {
        static FactionDefAnalyzer()
        {
            // Run analysis after all defs are loaded
            LongEventHandler.QueueLongEvent(AnalyzeAllFactionDefs, "Analyzing Faction Defs", false, null);
        }

        // Add a method to analyze faction defs at upgrade time
        public static void AnalyzeFactionDefAtUpgradeTime(FactionDef def, string context = "")
        {
            try
            {
                Log.Message("[LemProgress][UPGRADE_ANALYSIS] " + context + " - Analyzing " + def.defName + ":");

                var analysis = AnalyzeSingleFactionDef(def);
                PrintSingleFactionAnalysis(analysis);

                // Compare with any active faction using this def
                if (Find.World?.factionManager?.AllFactions != null)
                {
                    var factionsUsingThisDef = Find.World.factionManager.AllFactions.Where(f => f.def == def).ToList();
                    if (factionsUsingThisDef.Count > 0)
                    {
                        Log.Message("[LemProgress][UPGRADE_ANALYSIS]   Factions currently using this def:");
                        foreach (var faction in factionsUsingThisDef)
                        {
                            Log.Message("[LemProgress][UPGRADE_ANALYSIS]     - " + faction.Name + " (ID: " + faction.loadID + ")");
                        }
                    }
                    else
                    {
                        Log.Message("[LemProgress][UPGRADE_ANALYSIS]   No factions currently using this def");
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("[LemProgress][UPGRADE_ANALYSIS] Exception analyzing " + def.defName + ": " + e.ToString());
            }
        }

        // Method to compare a faction def's state between two time points
        public static void CompareFactionDefStates(FactionDef def, string timePoint1, string timePoint2)
        {
            Log.Message("[LemProgress][COMPARISON] Comparing " + def.defName + " between " + timePoint1 + " and " + timePoint2);

            if (def.pawnGroupMakers != null)
            {
                for (int i = 0; i < def.pawnGroupMakers.Count; i++)
                {
                    var pgm = def.pawnGroupMakers[i];
                    if (pgm != null && pgm.kindDef != null)
                    {
                        var analysis = AnalyzePawnGroupMaker(pgm, def.defName);
                        Log.Message("[LemProgress][COMPARISON]   PGM " + i + " (" + pgm.kindDef.defName + "): " +
                            "G:" + analysis.GuardsCount + " C:" + analysis.CarriersCount + " T:" + analysis.TradersCount +
                            " O:" + analysis.OptionsCount + " [" + (analysis.IsWorking ? "WORKING" : "BROKEN") + "]");

                        if (analysis.OptionsGenerationError != null)
                        {
                            Log.Message("[LemProgress][COMPARISON]     Error: " + analysis.OptionsGenerationError);
                        }
                    }
                }
            }
        }

        private static void AnalyzeAllFactionDefs()
        {
            try
            {
                Log.Message("[LemProgress][ANALYSIS] Starting comprehensive faction def analysis...");

                var allFactionDefs = DefDatabase<FactionDef>.AllDefs.ToList();
                Log.Message("[LemProgress][ANALYSIS] Found " + allFactionDefs.Count + " total faction defs");

                var stats = new Dictionary<TechLevel, FactionTechLevelStats>();
                int totalDefs = 0;
                int humanlikeDefs = 0;
                int usableForUpgrades = 0;
                int defsWithPawnGroupMakers = 0;
                int defsWithWorkingPawnGroupMakers = 0;

                foreach (var def in allFactionDefs)
                {
                    totalDefs++;

                    // Initialize tech level stats if needed
                    if (!stats.ContainsKey(def.techLevel))
                    {
                        stats[def.techLevel] = new FactionTechLevelStats
                        {
                            TechLevel = def.techLevel,
                            TotalDefs = 0,
                            HumanlikeDefs = 0,
                            UsableForUpgrades = 0,
                            DefsWithPGM = 0,
                            DefsWithWorkingPGM = 0,
                            DetailedResults = new List<FactionDefAnalysis>()
                        };
                    }

                    var techStats = stats[def.techLevel];
                    techStats.TotalDefs++;

                    var analysis = AnalyzeSingleFactionDef(def);
                    techStats.DetailedResults.Add(analysis);

                    if (def.humanlikeFaction)
                    {
                        humanlikeDefs++;
                        techStats.HumanlikeDefs++;
                    }

                    if (IsUsableForUpgrades(def))
                    {
                        usableForUpgrades++;
                        techStats.UsableForUpgrades++;
                    }

                    if (def.pawnGroupMakers != null && def.pawnGroupMakers.Count > 0)
                    {
                        defsWithPawnGroupMakers++;
                        techStats.DefsWithPGM++;
                    }

                    if (analysis.HasWorkingPawnGroupMakers)
                    {
                        defsWithWorkingPawnGroupMakers++;
                        techStats.DefsWithWorkingPGM++;
                    }
                }

                // Print overall statistics
                Log.Message("[LemProgress][ANALYSIS] === OVERALL STATISTICS ===");
                Log.Message("[LemProgress][ANALYSIS] Total faction defs: " + totalDefs);
                Log.Message("[LemProgress][ANALYSIS] Humanlike defs: " + humanlikeDefs);
                Log.Message("[LemProgress][ANALYSIS] Usable for upgrades: " + usableForUpgrades);
                Log.Message("[LemProgress][ANALYSIS] Defs with pawn group makers: " + defsWithPawnGroupMakers);
                Log.Message("[LemProgress][ANALYSIS] Defs with working pawn group makers: " + defsWithWorkingPawnGroupMakers);

                // Print tech level breakdown
                Log.Message("[LemProgress][ANALYSIS] === TECH LEVEL BREAKDOWN ===");
                foreach (var techLevel in Enum.GetValues(typeof(TechLevel)).Cast<TechLevel>().OrderBy(t => (int)t))
                {
                    if (stats.ContainsKey(techLevel))
                    {
                        var stat = stats[techLevel];
                        Log.Message("[LemProgress][ANALYSIS] " + techLevel.ToString() + ":");
                        Log.Message("[LemProgress][ANALYSIS]   Total: " + stat.TotalDefs +
                            " | Humanlike: " + stat.HumanlikeDefs +
                            " | Usable: " + stat.UsableForUpgrades +
                            " | WithPGM: " + stat.DefsWithPGM +
                            " | WorkingPGM: " + stat.DefsWithWorkingPGM);
                    }
                }

                // Print detailed analysis for each tech level
                Log.Message("[LemProgress][ANALYSIS] === DETAILED FACTION DEF LISTING ===");

                // Group by tech level and then list every faction
                foreach (var techLevel in Enum.GetValues(typeof(TechLevel)).Cast<TechLevel>().OrderBy(t => (int)t))
                {
                    if (stats.ContainsKey(techLevel))
                    {
                        Log.Message("[LemProgress][ANALYSIS] " + techLevel.ToString() + " FACTIONS:");
                        foreach (var analysis in stats[techLevel].DetailedResults.OrderBy(a => a.DefName))
                        {
                            PrintSingleFactionAnalysis(analysis);
                        }
                        Log.Message(""); // Empty line between tech levels
                    }
                }

                // Print summary of problematic defs
                Log.Message("[LemProgress][ANALYSIS] === SUMMARY OF PROBLEMATIC DEFS ===");
                var problematicDefs = new List<FactionDefAnalysis>();
                foreach (var techLevel in stats.Keys)
                {
                    foreach (var analysis in stats[techLevel].DetailedResults.Where(a => a.IsUsableForUpgrades && !a.HasWorkingPawnGroupMakers))
                    {
                        problematicDefs.Add(analysis);
                    }
                }

                Log.Message("[LemProgress][ANALYSIS] Found " + problematicDefs.Count + " problematic usable faction defs:");
                foreach (var analysis in problematicDefs.OrderBy(a => a.TechLevel).ThenBy(a => a.DefName))
                {
                    Log.Warning("[LemProgress][ANALYSIS] PROBLEM: " + analysis.DefName + " (" + analysis.TechLevel + ") - " + analysis.ProblemDescription);
                }

                Log.Message("[LemProgress][ANALYSIS] Analysis complete!");
            }
            catch (Exception e)
            {
                Log.Error("[LemProgress][ANALYSIS] Exception during faction def analysis: " + e.ToString());
            }
        }

        private static FactionDefAnalysis AnalyzeSingleFactionDef(FactionDef def)
        {
            var analysis = new FactionDefAnalysis
            {
                DefName = def.defName,
                Label = def.label,
                TechLevel = def.techLevel,
                IsHumanlike = def.humanlikeFaction,
                IsPlayer = def.isPlayer,
                IsHidden = def.hidden,
                IsUsableForUpgrades = IsUsableForUpgrades(def),
                PawnGroupMakersCount = def.pawnGroupMakers?.Count ?? 0,
                PawnGroupMakerDetails = new List<PawnGroupMakerAnalysis>()
            };

            if (def.pawnGroupMakers != null)
            {
                foreach (var pgm in def.pawnGroupMakers)
                {
                    var pgmAnalysis = AnalyzePawnGroupMaker(pgm, def.defName);
                    analysis.PawnGroupMakerDetails.Add(pgmAnalysis);

                    if (pgmAnalysis.IsWorking)
                        analysis.WorkingPawnGroupMakersCount++;
                }
            }

            analysis.HasWorkingPawnGroupMakers = analysis.WorkingPawnGroupMakersCount > 0;

            if (analysis.IsUsableForUpgrades && !analysis.HasWorkingPawnGroupMakers)
            {
                if (analysis.PawnGroupMakersCount == 0)
                    analysis.ProblemDescription = "No pawn group makers";
                else
                    analysis.ProblemDescription = "All pawn group makers are broken/empty";
            }

            return analysis;
        }

        private static PawnGroupMakerAnalysis AnalyzePawnGroupMaker(PawnGroupMaker pgm, string factionDefName)
        {
            var analysis = new PawnGroupMakerAnalysis
            {
                KindDefName = pgm?.kindDef?.defName ?? "NULL",
                IsNull = pgm == null,
                HasNullKindDef = pgm?.kindDef == null
            };

            if (pgm != null && pgm.kindDef != null)
            {
                analysis.GuardsCount = pgm.guards?.Count ?? -1;
                analysis.CarriersCount = pgm.carriers?.Count ?? -1;
                analysis.TradersCount = pgm.traders?.Count ?? -1;

                // Test options generation
                try
                {
                    var options = pgm.options;
                    analysis.OptionsCount = options?.Count ?? -1;
                    analysis.OptionsGenerationSucceeded = options != null;
                    analysis.IsWorking = options != null && options.Count > 0;
                }
                catch (Exception e)
                {
                    analysis.OptionsGenerationSucceeded = false;
                    analysis.OptionsGenerationError = e.Message;
                    analysis.IsWorking = false;
                }

                analysis.HasAnyPawnContent = (analysis.GuardsCount > 0) || (analysis.CarriersCount > 0) || (analysis.TradersCount > 0);
            }

            return analysis;
        }

        private static bool IsUsableForUpgrades(FactionDef def)
        {
            return def.humanlikeFaction &&
                   !def.isPlayer &&
                   !def.hidden;
        }

        private static void PrintSingleFactionAnalysis(FactionDefAnalysis analysis)
        {
            // Create status indicators
            var statusFlags = new List<string>();

            if (!analysis.IsHumanlike) statusFlags.Add("NON-HUMAN");
            if (analysis.IsPlayer) statusFlags.Add("PLAYER");
            if (analysis.IsHidden) statusFlags.Add("HIDDEN");
            if (!analysis.IsUsableForUpgrades) statusFlags.Add("NOT-USABLE");
            if (analysis.PawnGroupMakersCount == 0) statusFlags.Add("NO-PGM");
            if (analysis.PawnGroupMakersCount > 0 && !analysis.HasWorkingPawnGroupMakers) statusFlags.Add("BROKEN-PGM");
            if (analysis.HasWorkingPawnGroupMakers) statusFlags.Add("WORKING");

            var statusText = statusFlags.Count > 0 ? " [" + string.Join(", ", statusFlags.ToArray()) + "]" : "";

            Log.Message("[LemProgress][ANALYSIS]   " + analysis.DefName + " - " +
                (string.IsNullOrEmpty(analysis.Label) ? "(no label)" : analysis.Label) + statusText);

            // If it has pawn group makers, show details
            if (analysis.PawnGroupMakersCount > 0)
            {
                Log.Message("[LemProgress][ANALYSIS]     PGM: " + analysis.PawnGroupMakersCount +
                    " total, " + analysis.WorkingPawnGroupMakersCount + " working");

                foreach (var pgm in analysis.PawnGroupMakerDetails)
                {
                    if (pgm.IsNull)
                    {
                        Log.Message("[LemProgress][ANALYSIS]       - NULL PGM");
                    }
                    else if (pgm.HasNullKindDef)
                    {
                        Log.Message("[LemProgress][ANALYSIS]       - PGM with NULL kindDef");
                    }
                    else
                    {
                        var workingStatus = pgm.IsWorking ? "WORKING" : "BROKEN";
                        var contentSummary = "G:" + pgm.GuardsCount + " C:" + pgm.CarriersCount + " T:" + pgm.TradersCount + " O:" + pgm.OptionsCount;
                        var errorInfo = pgm.OptionsGenerationError != null ? " (Error: " + pgm.OptionsGenerationError + ")" : "";

                        Log.Message("[LemProgress][ANALYSIS]       - " + pgm.KindDefName +
                            " [" + workingStatus + "] " + contentSummary + errorInfo);
                    }
                }
            }
        }

        private static void PrintDetailedAnalysis(FactionDefAnalysis analysis)
        {
            Log.Message("[LemProgress][ANALYSIS]   " + analysis.DefName + " (" + analysis.Label + "):");
            Log.Message("[LemProgress][ANALYSIS]     PGM Count: " + analysis.PawnGroupMakersCount +
                " | Working: " + analysis.WorkingPawnGroupMakersCount);

            foreach (var pgm in analysis.PawnGroupMakerDetails)
            {
                if (pgm.IsNull)
                {
                    Log.Message("[LemProgress][ANALYSIS]       NULL PGM");
                }
                else if (pgm.HasNullKindDef)
                {
                    Log.Message("[LemProgress][ANALYSIS]       PGM with NULL kindDef");
                }
                else
                {
                    Log.Message("[LemProgress][ANALYSIS]       " + pgm.KindDefName +
                        " - Guards:" + pgm.GuardsCount +
                        " Carriers:" + pgm.CarriersCount +
                        " Traders:" + pgm.TradersCount +
                        " Options:" + pgm.OptionsCount +
                        (pgm.IsWorking ? " [WORKING]" : " [BROKEN]") +
                        (pgm.OptionsGenerationError != null ? " Error: " + pgm.OptionsGenerationError : ""));
                }
            }
        }

        private class FactionTechLevelStats
        {
            public TechLevel TechLevel;
            public int TotalDefs;
            public int HumanlikeDefs;
            public int UsableForUpgrades;
            public int DefsWithPGM;
            public int DefsWithWorkingPGM;
            public List<FactionDefAnalysis> DetailedResults;
        }

        private class FactionDefAnalysis
        {
            public string DefName;
            public string Label;
            public TechLevel TechLevel;
            public bool IsHumanlike;
            public bool IsPlayer;
            public bool IsHidden;
            public bool IsUsableForUpgrades;
            public int PawnGroupMakersCount;
            public int WorkingPawnGroupMakersCount;
            public bool HasWorkingPawnGroupMakers;
            public string ProblemDescription;
            public List<PawnGroupMakerAnalysis> PawnGroupMakerDetails;
        }

        private class PawnGroupMakerAnalysis
        {
            public string KindDefName;
            public bool IsNull;
            public bool HasNullKindDef;
            public int GuardsCount;
            public int CarriersCount;
            public int TradersCount;
            public int OptionsCount;
            public bool OptionsGenerationSucceeded;
            public string OptionsGenerationError;
            public bool HasAnyPawnContent;
            public bool IsWorking;
        }
    }
}