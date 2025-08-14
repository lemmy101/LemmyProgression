using UnityEngine;
using Verse;
using System.Collections.Generic;
using RimWorld;

namespace LemProgress.Settings
{
    public class LemProgressSettings : ModSettings
    {
        // Faction Upgrade Settings
        public float factionUpgradeChance = 0.5f;
        public int maxUltraFactions = 1;
        public bool onlyUpgradeOneStepAtTime = false;
        public bool preferSimilarFactionTypes = true;

        // Preservation Settings
        public bool preserveFactionNames = true;
        public bool preserveFactionDescriptions = true;
        public bool preserveFactionRelations = true;
        public bool preserveFactionColors = true;
        public bool preserveFactionIcons = false;

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
            // Basic settings
            Scribe_Values.Look(ref factionUpgradeChance, "factionUpgradeChance", 0.5f);
            Scribe_Values.Look(ref maxUltraFactions, "maxUltraFactions", 1);
            Scribe_Values.Look(ref onlyUpgradeOneStepAtTime, "onlyUpgradeOneStepAtTime", false);
            Scribe_Values.Look(ref preferSimilarFactionTypes, "preferSimilarFactionTypes", true);

            // Preservation settings
            Scribe_Values.Look(ref preserveFactionNames, "preserveFactionNames", true);
            Scribe_Values.Look(ref preserveFactionDescriptions, "preserveFactionDescriptions", true);
            Scribe_Values.Look(ref preserveFactionRelations, "preserveFactionRelations", true);
            Scribe_Values.Look(ref preserveFactionColors, "preserveFactionColors", true);
            Scribe_Values.Look(ref preserveFactionIcons, "preserveFactionIcons", false);

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
            if (whitelistedFactionDefs.Count > 0)
            {
                return whitelistedFactionDefs.Contains(defName);
            }
            return !blacklistedFactionDefs.Contains(defName);
        }

        public void ResetToDefaults()
        {
            factionUpgradeChance = 0.5f;
            maxUltraFactions = 1;
            onlyUpgradeOneStepAtTime = false;
            preferSimilarFactionTypes = true;

            preserveFactionNames = true;
            preserveFactionDescriptions = true;
            preserveFactionRelations = true;
            preserveFactionColors = true;
            preserveFactionIcons = false;

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