using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace LemProgress.Patches
{
    public class WorldTechLevelPatch : IPatchDefinition
    {
        public string Name
        {
            get { return "World Tech Level Integration"; }
        }

        public string RequiredModId
        {
            get { return "m00nl1ght.worldtechlevel"; }
        }

        public bool ShouldApply()
        {
            return ModsConfig.IsActive(RequiredModId);
        }

        public void Apply()
        {
            try
            {
                // Find the mod
                var mod = LoadedModManager.RunningMods
                    .FirstOrDefault(m => m.PackageId.ToLower() == RequiredModId.ToLower() ||
                                        m.PackageIdPlayerFacing.ToLower() == RequiredModId.ToLower());

                if (mod == null)
                {
                    Log.Warning("[" + ModCore.ModId + "] WorldTechLevel mod not found in loaded mods");
                    return;
                }

                Type targetType = null;
                MethodInfo targetMethod = null;

                // Search through mod's assemblies
                foreach (var assembly in mod.assemblies.loadedAssemblies)
                {
                    // Try different type name patterns
                    string[] possibleTypeNames = new string[]
                    {
                        "WorldTechLevel.TechLevelUtility",
                        "TechLevelUtility",
                        "WorldTechLevel.Utilities.TechLevelUtility"
                    };

                    foreach (var typeName in possibleTypeNames)
                    {
                        targetType = assembly.GetType(typeName);
                        if (targetType != null)
                        {
                            ModCore.LogDebug("Found type: " + targetType.FullName);
                            break;
                        }
                    }

                    // If not found by name, search all types
                    if (targetType == null)
                    {
                        var types = assembly.GetTypes();
                        foreach (var type in types)
                        {
                            if (type.Name == "TechLevelUtility" || type.Name.Contains("TechLevelUtility"))
                            {
                                targetType = type;
                                ModCore.LogDebug("Found type by search: " + type.FullName);
                                break;
                            }
                        }
                    }

                    if (targetType != null)
                    {
                        // Find the method
                        targetMethod = targetType.GetMethod("PlayerResearchFilterLevel",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                        if (targetMethod != null)
                        {
                            ModCore.LogDebug("Found method: " + targetMethod.Name);
                            break;
                        }
                    }
                }

                if (targetType == null)
                {
                    Log.Warning("[" + ModCore.ModId + "] Could not find TechLevelUtility type");
                    return;
                }

                if (targetMethod == null)
                {
                    Log.Warning("[" + ModCore.ModId + "] Could not find PlayerResearchFilterLevel method");
                    return;
                }

                var prefix = AccessTools.Method(typeof(WorldTechLevelPatch), nameof(ResearchFilterPrefix));
                ModCore.Harmony.Patch(targetMethod, new HarmonyMethod(prefix));

                Log.Message("[" + ModCore.ModId + "] Successfully patched WorldTechLevel.PlayerResearchFilterLevel");
            }
            catch (Exception e)
            {
                Log.Error("[" + ModCore.ModId + "] Failed to patch WorldTechLevel: " + e.ToString());
            }
        }

        private static bool ResearchFilterPrefix(ref TechLevel __result)
        {
            var settings = ModCore.Settings;

            if (!settings.modEnabled)
            {
                return true;
            }

            __result = TechLevel.Archotech;
            return false;
        }
    }
}
