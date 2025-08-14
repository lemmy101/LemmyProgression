using UnityEngine;
using Verse;
using System.Collections.Generic;
using RimWorld;

namespace LemProgress.Settings
{
    public class LemProgressSettings : ModSettings
    {
        // Master Enable/Disable
        public bool modEnabled = true;

        // Faction Upgrade Settings
        public float factionUpgradeChance = 0.5f;
        public int maxUltraFactions = 1;
        public bool onlyUpgradeOneStepAtTime = false;
        public bool preferSimilarFactionTypes = true;
        public int maxTechLevelsBehindToUpgrade = 4; // How many levels behind can still upgrade

        // Tech Level Settings
        public bool allowDowngrades = false;
        public bool notifyOnFactionUpgrade = true;
        public float settlementUpgradeDelay = 0f; // Days before settlements update

        // Advanced Settings
        public bool debugLogging = false;
        public bool autoUpgradePlayerFaction = false;
        public Dictionary<string, bool> techLevelUpgradeEnabled = new Dictionary<string, bool>
        {
            { "Neolithic", true },
            { "Medieval", true },
            { "Industrial", true },
            { "Spacer", true },
            { "Ultra", true },
            { "Archotech", false }
        };

        // Filter Settings
        public List<string> blacklistedFactionDefs = new List<string>();
        public List<string> whitelistedFactionDefs = new List<string>();

        public override void ExposeData()
        {
            // Master setting
            Scribe_Values.Look(ref modEnabled, "modEnabled", true);

            // Basic settings
            Scribe_Values.Look(ref factionUpgradeChance, "factionUpgradeChance", 0.5f);
            Scribe_Values.Look(ref maxUltraFactions, "maxUltraFactions", 1);
            Scribe_Values.Look(ref onlyUpgradeOneStepAtTime, "onlyUpgradeOneStepAtTime", false);
            Scribe_Values.Look(ref preferSimilarFactionTypes, "preferSimilarFactionTypes", true);
            Scribe_Values.Look(ref maxTechLevelsBehindToUpgrade, "maxTechLevelsBehindToUpgrade", 2);

            // Tech level settings
            Scribe_Values.Look(ref allowDowngrades, "allowDowngrades", false);
            Scribe_Values.Look(ref notifyOnFactionUpgrade, "notifyOnFactionUpgrade", true);
            Scribe_Values.Look(ref settlementUpgradeDelay, "settlementUpgradeDelay", 0f);

            // Advanced settings
            Scribe_Values.Look(ref debugLogging, "debugLogging", false);
            Scribe_Values.Look(ref autoUpgradePlayerFaction, "autoUpgradePlayerFaction", false);

            // Collections
            Scribe_Collections.Look(ref techLevelUpgradeEnabled, "techLevelUpgradeEnabled",
                LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref blacklistedFactionDefs, "blacklistedFactionDefs",
                LookMode.Value);
            Scribe_Collections.Look(ref whitelistedFactionDefs, "whitelistedFactionDefs",
                LookMode.Value);

            base.ExposeData();

            // Initialize collections if null after loading
            if (techLevelUpgradeEnabled == null)
            {
                techLevelUpgradeEnabled = GetDefaultTechLevelSettings();
            }
            if (blacklistedFactionDefs == null)
            {
                blacklistedFactionDefs = new List<string>();
            }
            if (whitelistedFactionDefs == null)
            {
                whitelistedFactionDefs = new List<string>();
            }
        }

        private Dictionary<string, bool> GetDefaultTechLevelSettings()
        {
            return new Dictionary<string, bool>
            {
                { "Neolithic", true },
                { "Medieval", true },
                { "Industrial", true },
                { "Spacer", true },
                { "Ultra", true },
                { "Archotech", false }
            };
        }

        public bool IsTechLevelUpgradeEnabled(TechLevel level)
        {
            var key = level.ToString();
            return techLevelUpgradeEnabled.ContainsKey(key) && techLevelUpgradeEnabled[key];
        }

        public bool IsFactionDefAllowed(string defName)
        {
            // If we have a whitelist, only allow those
            if (whitelistedFactionDefs != null && whitelistedFactionDefs.Count > 0)
            {
                return whitelistedFactionDefs.Contains(defName);
            }

            // Otherwise, check blacklist
            if (blacklistedFactionDefs != null && blacklistedFactionDefs.Contains(defName))
            {
                return false;
            }

            // Default to allowed
            return true;
        }

        public void ResetToDefaults()
        {
            modEnabled = true;
            factionUpgradeChance = 0.5f;
            maxUltraFactions = 1;
            onlyUpgradeOneStepAtTime = false;
            preferSimilarFactionTypes = true;

            allowDowngrades = false;
            notifyOnFactionUpgrade = true;
            settlementUpgradeDelay = 0f;

            debugLogging = false;
            autoUpgradePlayerFaction = false;

            techLevelUpgradeEnabled = GetDefaultTechLevelSettings();
            blacklistedFactionDefs.Clear();
            whitelistedFactionDefs.Clear();
        }
    }
}