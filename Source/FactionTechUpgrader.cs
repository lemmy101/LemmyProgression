using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace LemProgress
{


    public static class FactionTechUpgrader
    {
        private static MethodInfo regenerateStockMethod;
        static Random rand = new Random();

        static FactionTechUpgrader()
        {
            // Cache the private method reference at startup
            regenerateStockMethod = AccessTools.Method(typeof(Settlement_TraderTracker), "RegenerateStock");
            if (regenerateStockMethod == null)
            {
                Log.Error("LemProg: Could not find RegenerateStock method!");
            }
        }

        /// <summary>
        /// Upgrades all factions to a specific tech level
        /// </summary>
        public static void UpgradeAllFactionsToTechLevel(TechLevel oldTechLevel, TechLevel targetLevel)
        {
            if (Find.World?.factionManager?.AllFactions == null)
            {
                Log.Error("LemProg: World or FactionManager not initialized");
                return;
            }

            var list = Find.World.factionManager.AllFactions.ToList();

            foreach (var faction in list)                                                                                      
            {
                if (faction.def.techLevel < targetLevel)
                {
                    // don't upgrade every faction
                    if(rand.Next(100) < 80)
                        UpgradeFaction(faction, faction.def.techLevel, faction.def.techLevel+1);
                }
                
            }

            Log.Message($"LemProg: Upgraded all factions to {targetLevel}");
        }

        /// <summary>
        /// Upgrades a specific faction
        /// </summary>
        public static void UpgradeFaction(Faction faction, TechLevel oldTechLevel, TechLevel newTechLevel)
        {
            if (faction == null || faction.def == null) return;

            var oldLevel = faction.def.techLevel;

            if (oldTechLevel != oldLevel)
            {
                Log.Message($"LemProg: Skipped {faction.Name} at {oldLevel}");
                return;
            }

            if (faction.IsPlayer)
                return;

            if (!faction.def.humanlikeFaction)
                return;

            // Skip if already at or above target level
            if (oldLevel >= newTechLevel) return;

            Log.Message($"LemProg: Upgrading {faction.Name} from {oldLevel} to {newTechLevel}");

            var choices = FactionDefUtility.GetAvailableFactionDefs(newTechLevel);

            Log.Message($"LemProg: "  + choices.Count + " choices to replace");

            if (choices.Count > 0)
            {
                FactionDefUtility.CopyDefToFactionEnhanced(faction, choices[rand.Next(choices.Count)], new DefCopyOptions(){});
            }

            /*
            // Create a new def or modify the existing one
            UpdateFactionDef(faction, newTechLevel);

            // Update faction name if needed
            UpdateFactionName(faction, oldLevel, newTechLevel);

            // Update faction bases
            UpdateFactionBases(faction, newTechLevel);

            // Update faction relationships and goodwill if needed
            UpdateFactionRelations(faction, oldLevel, newTechLevel);

            // Update faction's available items and equipment
            UpdateFactionEquipment(faction, newTechLevel);
            */



        }

        /// <summary>
        /// Updates the faction def with new tech level
        /// </summary>
        private static void UpdateFactionDef(Faction faction, TechLevel newTechLevel)
        {
            // Option 1: Direct modification (simpler but less compatible)
  //          faction.def.techLevel = newTechLevel;

            // Option 2: Create a modified copy of the def (safer for compatibility)
            
            var newDef = new FactionDef();
            // Copy all fields from old def
            foreach (var field in typeof(FactionDef).GetFields())
            {
                field.SetValue(newDef, field.GetValue(faction.def));
            }
            newDef.techLevel = newTechLevel;


            faction.def = newDef;
            
        }

        /// <summary>
        /// Updates faction name based on tech progression
        /// </summary>
        private static void UpdateFactionName(Faction faction, TechLevel oldLevel, TechLevel newLevel)
        {
       
            var baseName = faction.Name;

            // Remove old tech level indicators
            var techIndicators = new[] { "Tribal", "Medieval", "Industrial", "Spacer", "Ultra", "Glitterworld" };
            foreach (var indicator in techIndicators)
            {
                baseName = baseName.Replace(indicator, "").Trim();
            }

            string techPrefix = "";
            // Add new tech level prefix/suffix based on the new level
            switch (newLevel)
            {
                case TechLevel.Animal:
                    techPrefix = "Animal ";
                    break;
                case TechLevel.Medieval:
                    techPrefix = "Medieval ";
                    break;
                case TechLevel.Industrial:
                    techPrefix = "Industrial ";
                    break;
                case TechLevel.Spacer:
                    techPrefix = "Spacer ";
                    break;
                case TechLevel.Neolithic:
                    techPrefix = "Neolithic ";
                    break;
                case TechLevel.Ultra:
                    techPrefix = "Ultra ";
                    break;
                case TechLevel.Archotech:
                    techPrefix = "Archotech ";
                    break;
                case TechLevel.Undefined:
                    techPrefix = "Undefined ";
                    break;
            }


            // Update the faction name
            faction.Name = techPrefix + baseName;

            Log.Message($"LemProg: Renamed faction to {faction.Name}");
        }

        /// <summary>
        /// Updates all settlements belonging to the faction
        /// </summary>
        private static void UpdateFactionBases(Faction faction, TechLevel newTechLevel)
        {
            var settlements = Find.WorldObjects.Settlements
                .Where(s => s.Faction == faction);

            foreach (var settlement in settlements)
            {
                // Update trader stock if it's a trading settlement
                if (settlement.trader != null)
                {
                    RefreshTraderStock(settlement, newTechLevel);
                }

                // You could also update the settlement's name here if desired
                UpdateSettlementName(settlement, newTechLevel);
            }
        }

        /// <summary>
        /// Updates settlement names to reflect new tech level
        /// </summary>
        private static void UpdateSettlementName(Settlement settlement, TechLevel techLevel)
        {
            // This is optional - you might want to keep original names
            // Example: Add tech level descriptor to certain settlements

            if (settlement.HasMap) return; // Don't rename active player maps

            // You could implement custom naming logic here
            // For example, upgrading "Village" to "City" for industrial, etc.
        }

        /// <summary>
        /// Refreshes trader stock for a settlement
        /// </summary>
        private static void RefreshTraderStock(Settlement settlement, TechLevel techLevel)
        {
            if (settlement.trader == null) return;

            // Force regeneration of trader stock
            settlement.trader.TryDestroyStock();

            // Call the private RegenerateStock method using Harmony
            if (regenerateStockMethod != null)
            {
                regenerateStockMethod.Invoke(settlement.trader, null);
            }
            else
            {
                Log.Warning($"LemProg: Could not regenerate stock for {settlement.Name}");
            }
        }

        /// <summary>
        /// Updates faction relationships based on tech level changes
        /// </summary>
        private static void UpdateFactionRelations(Faction faction, TechLevel oldLevel, TechLevel newLevel)
        {
            // Higher tech factions might have different natural goodwill ranges
            if (faction.def.naturalEnemy) return; // Don't modify hostile factions

            // Adjust natural goodwill based on tech level
            var techDifference = (int)newLevel - (int)oldLevel;
            var goodwillAdjustment = techDifference * 10; // Arbitrary multiplier

            // Update relationships with player
            if (faction != Faction.OfPlayer)
            {
                faction.TryAffectGoodwillWith(Faction.OfPlayer, goodwillAdjustment);
            }
        }

        /// <summary>
        /// Updates the equipment and items available to the faction
        /// </summary>
        private static void UpdateFactionEquipment(Faction faction, TechLevel newTechLevel)
        {
            // Update pawn generator to use appropriate equipment
            if (faction.def.pawnGroupMakers != null)
            {
                foreach (var pawnGroupMaker in faction.def.pawnGroupMakers)
                {
                    // Update tech level restrictions for pawn generation
                    if (pawnGroupMaker.options != null)
                    {
                        foreach (var option in pawnGroupMaker.options)
                        {
                            // This ensures pawns spawn with appropriate gear
                            // The actual implementation depends on your needs
                        }
                    }
                }
            }

            // Update allowed stuff categories based on tech level
            UpdateAllowedStuff(faction, newTechLevel);
        }

        /// <summary>
        /// Updates what items/materials the faction can use
        /// </summary>
        private static void UpdateAllowedStuff(Faction faction, TechLevel techLevel)
        {
            // Define what stuff categories are available at each tech level
            var allowedByTechLevel = new Dictionary<TechLevel, List<StuffCategoryDef>>
            {
                [TechLevel.Neolithic] = new List<StuffCategoryDef>
                        { StuffCategoryDefOf.Woody, StuffCategoryDefOf.Stony },
                [TechLevel.Medieval] = new List<StuffCategoryDef>
                        { StuffCategoryDefOf.Woody, StuffCategoryDefOf.Stony, StuffCategoryDefOf.Metallic },
                [TechLevel.Industrial] = new List<StuffCategoryDef>
                    {
                        StuffCategoryDefOf.Woody, StuffCategoryDefOf.Stony, StuffCategoryDefOf.Metallic,
                        StuffCategoryDefOf.Fabric
                    },
                // Add more as needed
            };

            // Apply the changes
            if (allowedByTechLevel.ContainsKey(techLevel))
            {
                faction.def.allowedCultures?.Clear();
                // You might need to recreate or modify faction's culture settings
            }
        }

    }

}

