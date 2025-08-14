using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace LemProgress.Settings
{
    public class LemProgressMod : Mod
    {
        public static LemProgressSettings settings;
        private Vector2 scrollPosition = Vector2.zero;
        private string searchFilter = "";
        private SettingsTab currentTab = SettingsTab.General;

        private enum SettingsTab
        {
            General,
            TechLevels,
            FactionFilter,
            Advanced
        }

        public LemProgressMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<LemProgressSettings>();
            LongEventHandler.QueueLongEvent(InitializeSettings, "LemProgress.InitializingSettings", false, null);
        }

        private void InitializeSettings()
        {
            // Any initialization needed after game loads
            Log.Message("[LemProgress] Settings initialized");
        }

        public override string SettingsCategory()
        {
            return "Lemmy's Tech Progression";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // Create tab buttons
            Rect tabRect = new Rect(inRect.x, inRect.y, inRect.width, 30f);
            DrawTabs(tabRect);

            // Main content area
            Rect contentRect = new Rect(inRect.x, inRect.y + 35f, inRect.width, inRect.height - 75f);

            // Draw current tab content
            switch (currentTab)
            {
                case SettingsTab.General:
                    DrawGeneralSettings(contentRect);
                    break;
                case SettingsTab.TechLevels:
                    DrawTechLevelSettings(contentRect);
                    break;
                case SettingsTab.FactionFilter:
                    DrawFactionFilterSettings(contentRect);
                    break;
                case SettingsTab.Advanced:
                    DrawAdvancedSettings(contentRect);
                    break;
            }

            // Reset button at bottom
            Rect resetRect = new Rect(inRect.x, inRect.yMax - 30f, 120f, 25f);
            if (Widgets.ButtonText(resetRect, "Reset to Defaults"))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Are you sure you want to reset all settings to defaults?",
                    delegate { settings.ResetToDefaults(); },
                    destructive: true));
            }
        }

        private void DrawTabs(Rect rect)
        {
            float tabWidth = rect.width / 4f;

            for (int i = 0; i < 4; i++)
            {
                SettingsTab tab = (SettingsTab)i;
                Rect tabButton = new Rect(rect.x + (i * tabWidth), rect.y, tabWidth - 2f, rect.height);

                bool isSelected = currentTab == tab;
                GUI.color = isSelected ? Color.white : Color.gray;

                if (Widgets.ButtonText(tabButton, tab.ToString()))
                {
                    currentTab = tab;
                }
            }

            GUI.color = Color.white;
        }

        private void DrawGeneralSettings(Rect rect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(rect);

            // Master enable/disable at the top
            listing.Label((TaggedString)"Master Settings", -1f, "Enable or disable the entire mod");
            listing.GapLine();
            listing.Gap();

            listing.CheckboxLabeled("Enable Lemmy's Tech Progression",
                ref settings.modEnabled,
                "Master switch to enable/disable all mod functionality");

            listing.Gap();
            listing.GapLine();
            listing.Gap();

            // Only show other settings if mod is enabled
            if (settings.modEnabled)
            {
                listing.Label((TaggedString)"Faction Upgrade Settings", -1f, "Configure faction upgrade behavior");
                listing.Gap();

                // Faction upgrade chance
                string chanceLabel = "Faction Upgrade Chance: " + (settings.factionUpgradeChance * 100f).ToString("F0") + "%";
                listing.Label(chanceLabel);
                settings.factionUpgradeChance = listing.Slider(settings.factionUpgradeChance, 0f, 1f);
                listing.Gap();

                // Max ultra factions
                string ultraLabel = "Maximum Ultra-Tech Factions: " + settings.maxUltraFactions;
                listing.Label(ultraLabel);
                settings.maxUltraFactions = (int)listing.Slider(settings.maxUltraFactions, 0, 10);
                listing.Gap();

                // Checkboxes
                listing.CheckboxLabeled("Only upgrade factions one tech level at a time",
                    ref settings.onlyUpgradeOneStepAtTime,
                    "If checked, factions will only advance one tech level per era advancement");

                listing.CheckboxLabeled("Prefer similar faction types when upgrading",
                    ref settings.preferSimilarFactionTypes,
                    "If checked, the mod will try to maintain faction identity when upgrading");

                // Max tech levels behind
                listing.Gap();
                string levelsBehindLabel = "Max tech levels behind to upgrade: " + settings.maxTechLevelsBehindToUpgrade;
                listing.Label(levelsBehindLabel);
                listing.Label("Factions more than " + settings.maxTechLevelsBehindToUpgrade +
                    " levels behind won't be upgraded", -1f);
                settings.maxTechLevelsBehindToUpgrade = (int)listing.Slider(settings.maxTechLevelsBehindToUpgrade, 1, 5);

                listing.CheckboxLabeled("Show notifications when factions upgrade",
                    ref settings.notifyOnFactionUpgrade,
                    "Display a message when a faction advances to a new tech level");

                listing.CheckboxLabeled("Allow faction downgrades",
                    ref settings.allowDowngrades,
                    "WARNING: Allows factions to regress to lower tech levels");

                // Settlement upgrade delay
                listing.Gap();
                string delayLabel = "Settlement Upgrade Delay: " + settings.settlementUpgradeDelay.ToString("F0") + " days";
                listing.Label(delayLabel);
                settings.settlementUpgradeDelay = listing.Slider(settings.settlementUpgradeDelay, 0f, 60f);
            }
            else
            {
                listing.Label("Mod is disabled. Enable it above to configure settings.", -1f);
            }

            listing.End();
        }

        private void DrawTechLevelSettings(Rect rect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(rect);

            listing.Label((TaggedString)"Tech Level Settings", -1f,
                "Configure which tech levels factions can upgrade to");
            listing.GapLine();
            listing.Gap();

            if (!settings.modEnabled)
            {
                listing.Label("Mod is disabled. Enable it in General settings to configure tech levels.", -1f);
                listing.End();
                return;
            }

            string[] techLevels = new string[]
            {
                "Neolithic", "Medieval", "Industrial",
                "Spacer", "Ultra", "Archotech"
            };

            foreach (var techLevel in techLevels)
            {
                bool enabled = settings.techLevelUpgradeEnabled.ContainsKey(techLevel)
                    && settings.techLevelUpgradeEnabled[techLevel];

                bool newEnabled = enabled;
                listing.CheckboxLabeled("Allow upgrades to " + techLevel, ref newEnabled);

                if (newEnabled != enabled)
                {
                    settings.techLevelUpgradeEnabled[techLevel] = newEnabled;
                }
            }

            listing.Gap();
            listing.CheckboxLabeled("Auto-upgrade player faction",
                ref settings.autoUpgradePlayerFaction,
                "WARNING: This will change your colony's tech level");

            listing.End();
        }

        private void DrawFactionFilterSettings(Rect rect)
        {
            // Header
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, 60f);
            Listing_Standard headerListing = new Listing_Standard();
            headerListing.Begin(headerRect);

            headerListing.Label((TaggedString)"Faction Filter Settings", -1f,
                "Blacklist or whitelist specific faction definitions");
            headerListing.GapLine();

            headerListing.End();

            if (!settings.modEnabled)
            {
                Rect disabledRect = new Rect(rect.x, rect.y + 65f, rect.width, 30f);
                Widgets.Label(disabledRect, "Mod is disabled. Enable it in General settings to configure filters.");
                return;
            }

            // Search bar
            Rect searchRect = new Rect(rect.x, rect.y + 65f, rect.width - 20f, 25f);
            searchFilter = Widgets.TextField(searchRect, searchFilter);

            // Scrollable faction list
            Rect scrollViewRect = new Rect(rect.x, rect.y + 95f, rect.width, rect.height - 100f);

            var filteredFactions = DefDatabase<FactionDef>.AllDefs
                .Where(f => string.IsNullOrEmpty(searchFilter) ||
                           f.defName.ToLower().Contains(searchFilter.ToLower()) ||
                           f.label.ToLower().Contains(searchFilter.ToLower()))
                .OrderBy(f => f.label)
                .ToList();

            Rect scrollContentRect = new Rect(0f, 0f, rect.width - 20f, filteredFactions.Count * 25f);

            Widgets.BeginScrollView(scrollViewRect, ref scrollPosition, scrollContentRect);

            float yPos = 0f;
            foreach (var factionDef in filteredFactions)
            {
                Rect rowRect = new Rect(5f, yPos, scrollContentRect.width - 10f, 22f);
                DrawFactionFilterRow(rowRect, factionDef);
                yPos += 25f;
            }

            Widgets.EndScrollView();
        }

        private void DrawFactionFilterRow(Rect rect, FactionDef factionDef)
        {
            // Faction name
            Rect labelRect = new Rect(rect.x, rect.y, rect.width - 200f, rect.height);
            string labelText = factionDef.label + " (" + factionDef.defName + ")";
            Widgets.Label(labelRect, labelText);

            // Blacklist button
            Rect blacklistRect = new Rect(rect.xMax - 190f, rect.y, 90f, rect.height);
            bool isBlacklisted = settings.blacklistedFactionDefs.Contains(factionDef.defName);

            if (isBlacklisted)
            {
                GUI.color = Color.red;
            }

            if (Widgets.ButtonText(blacklistRect, isBlacklisted ? "Blacklisted" : "Blacklist"))
            {
                if (isBlacklisted)
                {
                    settings.blacklistedFactionDefs.Remove(factionDef.defName);
                }
                else
                {
                    settings.blacklistedFactionDefs.Add(factionDef.defName);
                    settings.whitelistedFactionDefs.Remove(factionDef.defName);
                }
            }

            GUI.color = Color.white;

            // Whitelist button
            Rect whitelistRect = new Rect(rect.xMax - 95f, rect.y, 90f, rect.height);
            bool isWhitelisted = settings.whitelistedFactionDefs.Contains(factionDef.defName);

            if (isWhitelisted)
            {
                GUI.color = Color.green;
            }

            if (Widgets.ButtonText(whitelistRect, isWhitelisted ? "Whitelisted" : "Whitelist"))
            {
                if (isWhitelisted)
                {
                    settings.whitelistedFactionDefs.Remove(factionDef.defName);
                }
                else
                {
                    settings.whitelistedFactionDefs.Add(factionDef.defName);
                    settings.blacklistedFactionDefs.Remove(factionDef.defName);
                }
            }

            GUI.color = Color.white;
        }

        private void DrawAdvancedSettings(Rect rect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(rect);

            listing.Label((TaggedString)"Advanced Settings", -1f, "Debug and development options");
            listing.GapLine();
            listing.Gap();

            listing.CheckboxLabeled("Enable debug logging",
                ref settings.debugLogging,
                "Shows detailed logs about faction upgrades and tech progression");

            listing.Gap();

            if (Current.Game != null)
            {
                listing.Label("Debug Actions (Current Game):");
                listing.Gap();

                if (listing.ButtonText("Force Tech Level Advance"))
                {
                    ForceAdvanceTechLevel();
                }

                if (listing.ButtonText("Log All Faction Tech Levels"))
                {
                    LogAllFactionTechLevels();
                }

                if (listing.ButtonText("Reset All Faction Caches"))
                {
                    ResetAllFactionCaches();
                }

                if (listing.ButtonText("List Available Faction Defs"))
                {
                    ListAvailableFactionDefs();
                }
            }
            else
            {
                listing.Label("Debug actions available only in-game");
            }

            listing.End();
        }

        // Debug actions
        private void ForceAdvanceTechLevel()
        {
            if (Find.World == null || Find.World.factionManager == null) return;

            TechLevel[] levels = new TechLevel[]
            {
                TechLevel.Neolithic, TechLevel.Medieval, TechLevel.Industrial,
                TechLevel.Spacer, TechLevel.Ultra, TechLevel.Archotech
            };

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (var level in levels)
            {
                var capturedLevel = level; // Capture for closure
                options.Add(new FloatMenuOption("Advance to " + capturedLevel.ToString(), delegate
                {
                    Systems.WorldEraManager.AdvanceToTechLevel(capturedLevel);
                    Messages.Message("Advanced world to " + capturedLevel.ToString(), MessageTypeDefOf.NeutralEvent);
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void LogAllFactionTechLevels()
        {
            if (Find.World == null || Find.World.factionManager == null) return;

            Log.Message("=== Current Faction Tech Levels ===");
            foreach (var faction in Find.World.factionManager.AllFactions)
            {
                Log.Message(faction.Name + ": " + faction.def.techLevel.ToString() +
                    " (" + faction.def.defName + ")");
            }

            Messages.Message("Faction tech levels logged to console",
                MessageTypeDefOf.NeutralEvent);
        }

        private void ResetAllFactionCaches()
        {
            if (Find.World == null || Find.World.factionManager == null) return;

            int count = 0;
            foreach (var faction in Find.World.factionManager.AllFactions)
            {
                if (faction.def.pawnGroupMakers != null)
                {
                    foreach (var pgm in faction.def.pawnGroupMakers)
                    {
                        // Force cache reset
                        var traverse = Traverse.Create(pgm);
                        var optionsField = traverse.Field("_options");
                        if (optionsField.FieldExists())
                        {
                            optionsField.SetValue(null);
                        }
                        var cachedField = traverse.Field("cachedOptions");
                        if (cachedField.FieldExists())
                        {
                            cachedField.SetValue(null);
                        }
                        count++;
                    }
                }
            }

            Messages.Message("Reset " + count + " faction pawn group caches", MessageTypeDefOf.NeutralEvent);
        }

        private void ListAvailableFactionDefs()
        {
            Log.Message("=== Available Faction Defs by Tech Level ===");

            var techLevels = Enum.GetValues(typeof(TechLevel)).Cast<TechLevel>();

            foreach (var techLevel in techLevels)
            {
                var defsAtLevel = new List<FactionDef>();

                foreach (var def in DefDatabase<FactionDef>.AllDefs)
                {
                    if (def.techLevel == techLevel &&
                        def.humanlikeFaction &&
                        !def.isPlayer &&
                        !def.hidden)
                    {
                        defsAtLevel.Add(def);
                    }
                }

                if (defsAtLevel.Count > 0)
                {
                    Log.Message(techLevel.ToString() + ": " + defsAtLevel.Count + " defs");
                    foreach (var def in defsAtLevel)
                    {
                        string blacklisted = settings.IsFactionDefAllowed(def.defName) ? "" : " [BLACKLISTED]";
                        Log.Message("  - " + def.defName + " (" + def.label + ")" + blacklisted);
                    }
                }
            }

            Messages.Message("Available faction defs listed in console", MessageTypeDefOf.NeutralEvent);
        }
    }
}