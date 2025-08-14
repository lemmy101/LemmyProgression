using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
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

            // Get factions that are eligible for upgrade
            var factionsToUpgrade = GetUpgradeableFactions(oldLevel, newLevel);

            // If configured, ensure each faction has a unique def before upgrading
            if (settings.ensureUniqueFactionDefs)
            {
                Log.Message("[" + ModCore.ModId + "] Ensuring unique defs for " + factionsToUpgrade.Count + " factions");
                FactionDefManager.EnsureUniqueDefsForUpgrade(factionsToUpgrade);
            }

            var upgradeCount = 0;
            var ultraCount = CountUltraTechFactions();

            foreach (var faction in factionsToUpgrade)
            {
                // Determine the target tech level for this faction
                TechLevel targetLevel;

                if (settings.onlyUpgradeOneStepAtTime)
                {
                    // Always upgrade one step from current level
                    targetLevel = GetNextTechLevel(faction.def.techLevel);
                }
                else
                {
                    // For factions at the old level (one step behind), upgrade to new level
                    // For factions further behind, upgrade them one step
                    if (faction.def.techLevel == oldLevel)
                    {
                        targetLevel = newLevel;
                    }
                    else
                    {
                        targetLevel = GetNextTechLevel(faction.def.techLevel);
                    }
                }

                // Check Ultra faction limit
                if (targetLevel == TechLevel.Ultra &&
                    ultraCount >= settings.maxUltraFactions)
                {
                    ModCore.LogDebug("Reached max Ultra factions limit (" + settings.maxUltraFactions + ")");
                    continue;
                }

                // Check if faction def is allowed
                var originalDefName = FactionDefManager.GetOriginalDefNameForFaction(faction);
                if (!settings.IsFactionDefAllowed(originalDefName))
                {
                    ModCore.LogDebug("Faction " + faction.Name + " is filtered out");
                    continue;
                }

                // Roll the dice for this faction
                if (ShouldUpgradeFaction(faction, targetLevel))
                {
                    if (targetLevel <= faction.def.techLevel && !settings.allowDowngrades)
                    {
                        continue;
                    }

                    ModCore.LogDebug("Upgrading " + faction.Name + " from " +
                        faction.def.techLevel.ToString() + " to " + targetLevel.ToString());

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

            Log.Message("[" + ModCore.ModId + "] Upgraded " + upgradeCount + " factions");

            // Clean up any orphaned references (but don't remove factions)
            FactionDefManager.CleanupRemovedFactions();
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
                // Skip player faction unless allowed
                if (faction.IsPlayer && !settings.autoUpgradePlayerFaction)
                    continue;

                // Check if it's a humanlike faction
                if (!faction.def.humanlikeFaction)
                    continue;

                // Check current tech level
                var currentTechLevel = faction.def.techLevel;

                if (settings.onlyUpgradeOneStepAtTime)
                {
                    // In one-step mode, include all factions below the new level
                    if (currentTechLevel >= newLevel && !settings.allowDowngrades)
                        continue;
                }
                else
                {
                    // In normal mode:
                    // - Factions at oldLevel (one step behind) can upgrade to newLevel
                    // - Factions below oldLevel can upgrade one step
                    // - Skip factions already at or above newLevel (unless downgrades allowed)
                    if (currentTechLevel >= newLevel && !settings.allowDowngrades)
                        continue;
                }

                // Check if faction def is allowed (use original def name for filtering)
                var originalDefName = FactionDefManager.GetOriginalDefNameForFaction(faction);
                if (!settings.IsFactionDefAllowed(originalDefName))
                {
                    ModCore.LogDebug("Faction " + faction.Name + " excluded by filter");
                    continue;
                }

                factions.Add(faction);
            }

            ModCore.LogDebug("Found " + factions.Count + " factions eligible for upgrade consideration");

            // Log the breakdown by tech level
            var techLevelCounts = new Dictionary<TechLevel, int>();
            foreach (var faction in factions)
            {
                if (!techLevelCounts.ContainsKey(faction.def.techLevel))
                    techLevelCounts[faction.def.techLevel] = 0;
                techLevelCounts[faction.def.techLevel]++;
            }

            foreach (var kvp in techLevelCounts)
            {
                ModCore.LogDebug("  - " + kvp.Value + " factions at " + kvp.Key.ToString());
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