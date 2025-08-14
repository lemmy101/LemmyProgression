using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace LemProgress.Systems
{
    public static class WorldEraManager
    {
        private static Type cachedWorldTechLevelType = null;
        private static FieldInfo cachedCurrentField = null;
        private static bool searchAttempted = false;

        public static void AdvanceToEra(object eraAdvancementDef)
        {
            // Extract tech level from def
            var defType = eraAdvancementDef.GetType();
            var techLevelField = AccessTools.Field(defType, "newTechLevel");
            var newTechLevel = (TechLevel)techLevelField.GetValue(eraAdvancementDef);

            AdvanceToTechLevel(newTechLevel);
        }

        public static void AdvanceToTechLevel(TechLevel newTechLevel)
        {
            var oldTechLevel = GetCurrentWorldTechLevel();

            if (oldTechLevel >= newTechLevel && !ModCore.Settings.allowDowngrades)
            {
                Log.Warning("[" + ModCore.ModId + "] Cannot downgrade from " +
                    oldTechLevel.ToString() + " to " + newTechLevel.ToString());
                return;
            }

            SetWorldTechLevel(newTechLevel);
            FactionUpgradeManager.UpgradeFactionsToTechLevel(oldTechLevel, newTechLevel);
        }

        private static TechLevel GetCurrentWorldTechLevel()
        {
            InitializeWorldTechLevelAccess();

            if (cachedCurrentField != null)
            {
                try
                {
                    var value = cachedCurrentField.GetValue(null);
                    if (value != null)
                    {
                        return (TechLevel)value;
                    }
                }
                catch (Exception e)
                {
                    Log.Warning("[" + ModCore.ModId + "] Failed to get world tech level: " + e.Message);
                }
            }

            return TechLevel.Industrial; // Default fallback
        }

        private static void SetWorldTechLevel(TechLevel level)
        {
            InitializeWorldTechLevelAccess();

            if (cachedCurrentField != null)
            {
                try
                {
                    cachedCurrentField.SetValue(null, level);
                    ModCore.LogDebug("Set world tech level to " + level.ToString());
                }
                catch (Exception e)
                {
                    Log.Warning("[" + ModCore.ModId + "] Failed to set world tech level: " + e.Message);
                }
            }
        }

        private static void InitializeWorldTechLevelAccess()
        {
            if (searchAttempted) return;
            searchAttempted = true;

            // Method 1: Try direct type access
            cachedWorldTechLevelType = AccessTools.TypeByName("WorldTechLevel.WorldTechLevel");

            // Method 2: Search through loaded mods if direct access failed
            if (cachedWorldTechLevelType == null)
            {
                ModCore.LogDebug("Direct type access failed, searching through mods...");

                var worldTechLevelMod = LoadedModManager.RunningMods
                    .FirstOrDefault(m => m.PackageId.ToLower() == "m00nl1ght.worldtechlevel" ||
                                        m.PackageIdPlayerFacing.ToLower() == "m00nl1ght.worldtechlevel");

                if (worldTechLevelMod != null)
                {
                    ModCore.LogDebug("Found WorldTechLevel mod, searching assemblies...");

                    foreach (var assembly in worldTechLevelMod.assemblies.loadedAssemblies)
                    {
                        // Try to find the type in this assembly
                        cachedWorldTechLevelType = assembly.GetType("WorldTechLevel.WorldTechLevel");
                        if (cachedWorldTechLevelType == null)
                        {
                            // Try without namespace
                            cachedWorldTechLevelType = assembly.GetType("WorldTechLevel");
                        }

                        if (cachedWorldTechLevelType == null)
                        {
                            // Search all types in the assembly
                            var types = assembly.GetTypes();
                            foreach (var type in types)
                            {
                                ModCore.LogDebug("Found type: " + type.FullName);
                                if (type.Name == "WorldTechLevel" || type.FullName.Contains("WorldTechLevel"))
                                {
                                    cachedWorldTechLevelType = type;
                                    break;
                                }
                            }
                        }

                        if (cachedWorldTechLevelType != null)
                            break;
                    }
                }
                else
                {
                    Log.Warning("[" + ModCore.ModId + "] WorldTechLevel mod not found");
                    return;
                }
            }

            if (cachedWorldTechLevelType != null)
            {
                ModCore.LogDebug("Found WorldTechLevel type: " + cachedWorldTechLevelType.FullName);

                // Try to find the Current field/property
                cachedCurrentField = AccessTools.Field(cachedWorldTechLevelType, "Current");

                if (cachedCurrentField == null)
                {
                    // Try as property
                    var prop = AccessTools.Property(cachedWorldTechLevelType, "Current");
                    if (prop != null)
                    {
                        cachedCurrentField = AccessTools.Field(cachedWorldTechLevelType, "<Current>k__BackingField");
                        if (cachedCurrentField == null)
                        {
                            // Try to get the backing field another way
                            var fields = cachedWorldTechLevelType.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                            foreach (var field in fields)
                            {
                                ModCore.LogDebug("Found field: " + field.Name + " of type " + field.FieldType.Name);
                                if (field.FieldType == typeof(TechLevel))
                                {
                                    cachedCurrentField = field;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (cachedCurrentField != null)
                {
                    ModCore.LogDebug("Found Current field: " + cachedCurrentField.Name);
                }
                else
                {
                    Log.Warning("[" + ModCore.ModId + "] Could not find Current field in WorldTechLevel");
                }
            }
            else
            {
                Log.Warning("[" + ModCore.ModId + "] Could not find WorldTechLevel type");
            }
        }
    }
}
