using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LemProgress.Systems.LemProgress.Systems;
using Verse;

namespace LemProgress.Systems
{
    public static class FactionUpgradeManager
    {
        private static readonly Random random = new Random();

        public static void UpgradeFactionsToTechLevel(TechLevel oldLevel, TechLevel newLevel)
        {
            var settings = ModCore.Settings;

            if (!settings.IsTechLevelUpgradeEnabled(newLevel))
            {
                ModCore.LogDebug("Upgrades to " + newLevel.ToString() + " are disabled in settings");
                return;
            }

            if (!ValidateWorld()) return;

            // First, consolidate duplicate factions if needed
            ConsolidateDuplicateFactionsIfNeeded();

            var factionsToUpgrade = GetUpgradeableFactions(oldLevel, newLevel);
            var upgradeCount = 0;
            var ultraCount = CountUltraTechFactions();

            foreach (var faction in factionsToUpgrade)
            {
                // Check Ultra faction limit
                if (newLevel == TechLevel.Ultra &&
                    ultraCount >= settings.maxUltraFactions)
                {
                    ModCore.LogDebug("Reached max Ultra factions limit (" + settings.maxUltraFactions + ")");
                    break;
                }

                // Check if faction def is allowed
                if (!settings.IsFactionDefAllowed(faction.def.defName))
                {
                    ModCore.LogDebug("Faction " + faction.Name + " is filtered out");
                    continue;
                }

                if (ShouldUpgradeFaction(faction, newLevel))
                {
                    var targetLevel = settings.onlyUpgradeOneStepAtTime
                        ? GetNextTechLevel(faction.def.techLevel)
                        : newLevel;

                    if (targetLevel <= faction.def.techLevel && !settings.allowDowngrades)
                    {
                        continue;
                    }

                    var upgrader = new FactionUpgrader(faction);
                    if (upgrader.UpgradeToTechLevel(targetLevel))
                    {
                        upgradeCount++;

                        if (targetLevel == TechLevel.Ultra)
                            ultraCount++;

                        if (settings.notifyOnFactionUpgrade)
                        {
                            NotifyFactionUpgrade(faction, targetLevel);
                        }

                        // Apply settlement upgrade delay if configured
                        if (settings.settlementUpgradeDelay > 0)
                        {
                            ScheduleSettlementUpgrades(faction, settings.settlementUpgradeDelay);
                        }
                    }
                }
            }

            Log.Message("[" + ModCore.ModId + "] Upgraded " + upgradeCount + " factions to " + newLevel.ToString());

            // Clean up after upgrades
            FactionDefManager.CleanupRemovedFactions();
        }

        private static void ConsolidateDuplicateFactionsIfNeeded()
        {
            var stats = FactionDefManager.GetDefUsageStats();
            bool needsConsolidation = false;

            foreach (var stat in stats)
            {
                if (stat.Value > 2) // More than 2 factions with same def
                {
                    ModCore.LogDebug("Found " + stat.Value + " factions using def " + stat.Key);
                    needsConsolidation = true;
                }
            }

            if (needsConsolidation)
            {
                Log.Message("[" + ModCore.ModId + "] Consolidating duplicate factions...");
                FactionDefManager.ConsolidateDuplicateFactions(2); // Max 2 per def
            }
        }

        private static bool ValidateWorld()
        {
            if (Find.World == null || Find.World.factionManager == null ||
                Find.World.factionManager.AllFactions == null)
            {
                Log.Error("[" + ModCore.ModId + "] World or FactionManager not initialized");
                return false;
            }
            return true;
        }

        private static List<Faction> GetUpgradeableFactions(TechLevel oldLevel, TechLevel newLevel)
        {
            var settings = ModCore.Settings;
            var factions = new List<Faction>();

            foreach (var faction in Find.World.factionManager.AllFactions)
            {
                // Check tech level requirements
                if (settings.onlyUpgradeOneStepAtTime && faction.def.techLevel != oldLevel)
                    continue;

                if (!settings.onlyUpgradeOneStepAtTime &&
                    faction.def.techLevel >= newLevel && !settings.allowDowngrades)
                    continue;

                // Check if player faction upgrade is allowed
                if (faction.IsPlayer && !settings.autoUpgradePlayerFaction)
                    continue;

                // Check if it's a humanlike faction
                if (!faction.def.humanlikeFaction)
                    continue;

                // Check if faction def is allowed
                if (!settings.IsFactionDefAllowed(faction.def.defName))
                    continue;

                factions.Add(faction);
            }

            return factions;
        }

        private static bool ShouldUpgradeFaction(Faction faction, TechLevel targetLevel)
        {
            var settings = ModCore.Settings;
            return random.NextDouble() < settings.factionUpgradeChance;
        }

        private static int CountUltraTechFactions()
        {
            int count = 0;
            foreach (var faction in Find.World.factionManager.AllFactions)
            {
                if (faction.def.techLevel == TechLevel.Ultra)
                    count++;
            }
            return count;
        }

        private static TechLevel GetNextTechLevel(TechLevel current)
        {
            switch (current)
            {
                case TechLevel.Animal:
                    return TechLevel.Neolithic;
                case TechLevel.Neolithic:
                    return TechLevel.Medieval;
                case TechLevel.Medieval:
                    return TechLevel.Industrial;
                case TechLevel.Industrial:
                    return TechLevel.Spacer;
                case TechLevel.Spacer:
                    return TechLevel.Ultra;
                case TechLevel.Ultra:
                    return TechLevel.Archotech;
                default:
                    return current;
            }
        }

        private static void NotifyFactionUpgrade(Faction faction, TechLevel newLevel)
        {
            string message = faction.Name + " has advanced to " + newLevel.ToString() + " technology!";
            Messages.Message(message, MessageTypeDefOf.NeutralEvent);
        }

        private static void ScheduleSettlementUpgrades(Faction faction, float delayDays)
        {
            // This would require a custom GameComponent to track and execute delayed upgrades
            // For now, log the intent
            ModCore.LogDebug("Settlement upgrades for " + faction.Name + " scheduled in " + delayDays + " days");
        }
    }
}
