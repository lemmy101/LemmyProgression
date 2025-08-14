using HarmonyLib;
using LemProgress.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace LemProgress.Patches
{
    public class VFETribalsPatch : IPatchDefinition
    {
        public string Name
        {
            get { return "VFE Tribals Era Advancement"; }
        }

        public string RequiredModId
        {
            get { return "oskarpotocki.vfe.tribals"; }
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
                    Log.Warning("[" + ModCore.ModId + "] VFE Tribals mod not found in loaded mods");
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
                        "VFETribals.GameComponent_Tribals",
                        "GameComponent_Tribals",
                        "VFETribals.GameComponents.GameComponent_Tribals"
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
                            if (type.Name == "GameComponent_Tribals" || type.Name.Contains("GameComponent_Tribals"))
                            {
                                targetType = type;
                                ModCore.LogDebug("Found type by search: " + type.FullName);
                                break;
                            }
                        }
                    }

                    if (targetType != null)
                    {
                        // Find the method - try different binding flags
                        targetMethod = targetType.GetMethod("AdvanceToEra",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

                        if (targetMethod != null)
                        {
                            ModCore.LogDebug("Found method: " + targetMethod.Name);
                            break;
                        }

                        // If not found, list all methods for debugging
                        var methods = targetType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.Static);
                        foreach (var method in methods)
                        {
                            ModCore.LogDebug("Available method: " + method.Name);
                            if (method.Name.Contains("Advance") || method.Name.Contains("Era"))
                            {
                                targetMethod = method;
                                break;
                            }
                        }

                        if (targetMethod != null)
                            break;
                    }
                }

                if (targetType == null)
                {
                    Log.Warning("[" + ModCore.ModId + "] Could not find GameComponent_Tribals type");
                    return;
                }

                if (targetMethod == null)
                {
                    Log.Warning("[" + ModCore.ModId + "] Could not find AdvanceToEra method");
                    return;
                }

                var prefix = AccessTools.Method(typeof(VFETribalsPatch), nameof(AdvanceToEraPrefix));
                ModCore.Harmony.Patch(targetMethod, new HarmonyMethod(prefix));

                Log.Message("[" + ModCore.ModId + "] Successfully patched VFETribals.AdvanceToEra");
            }
            catch (Exception e)
            {
                Log.Error("[" + ModCore.ModId + "] Failed to patch VFE Tribals: " + e.ToString());
            }
        }

        private static bool AdvanceToEraPrefix(object def)
        {
            try
            {
                // Use reflection to get the newTechLevel from the EraAdvancementDef
                var defType = def.GetType();
                ModCore.LogDebug("EraAdvancementDef type: " + defType.FullName);

                // Try to find the tech level field
                FieldInfo techLevelField = null;

                // Try common field names
                string[] possibleFieldNames = new string[] { "newTechLevel", "techLevel", "targetTechLevel" };
                foreach (var fieldName in possibleFieldNames)
                {
                    techLevelField = defType.GetField(fieldName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (techLevelField != null)
                        break;
                }

                // If not found, list all fields for debugging
                if (techLevelField == null)
                {
                    var fields = defType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    foreach (var field in fields)
                    {
                        ModCore.LogDebug("Available field: " + field.Name + " of type " + field.FieldType.Name);
                        if (field.FieldType == typeof(RimWorld.TechLevel))
                        {
                            techLevelField = field;
                            break;
                        }
                    }
                }

                if (techLevelField == null)
                {
                    Log.Warning("[" + ModCore.ModId + "] Could not find tech level field in EraAdvancementDef");
                    return true; // Let original method run
                }

                var newTechLevel = (RimWorld.TechLevel)techLevelField.GetValue(def);
                Log.Message("[" + ModCore.ModId + "] Advancing world to tech level: " + newTechLevel.ToString());

                WorldEraManager.AdvanceToEra(def);
            }
            catch (Exception e)
            {
                Log.Error("[" + ModCore.ModId + "] Error in AdvanceToEraPrefix: " + e.ToString());
            }

            return true; // Let original method run as well
        }
    }
}
