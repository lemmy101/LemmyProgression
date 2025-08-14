using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace LemProgress
{
    public static class CrossModPatcher
    {
        private static Harmony harmony;

        public static void Initialize(string yourModId)
        {
            harmony = new Harmony(yourModId);
        }

        /// <summary>
        /// Patches a method from another mod
        /// </summary>
        public static bool PatchMethod(
            string targetModId,
            string targetTypeName,
            string targetMethodName,
            HarmonyMethod prefix = null,
            HarmonyMethod postfix = null,
            HarmonyMethod transpiler = null,
            HarmonyMethod finalizer = null)
        {
            var targetType = GetTypeFromMod(targetModId, targetTypeName);
            if (targetType == null)
            {
                Log.Warning($"[CrossModPatcher] Could not find type {targetTypeName} in mod {targetModId}");
                return false;
            }

            var targetMethod = targetType.GetMethod(targetMethodName,
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.Instance);

            if (targetMethod == null)
            {
                Log.Warning($"[CrossModPatcher] Could not find method {targetMethodName} in type {targetTypeName}");
                return false;
            }

            harmony.Patch(targetMethod, prefix, postfix, transpiler, finalizer);
            Log.Message($"[CrossModPatcher] Successfully patched {targetTypeName}.{targetMethodName}");
            return true;
        }

        /// <summary>
        /// Patches a method with specific parameter types (for overload resolution)
        /// </summary>
        public static bool PatchMethodExact(
            string targetModId,
            string targetTypeName,
            string targetMethodName,
            Type[] parameterTypes,
            HarmonyMethod prefix = null,
            HarmonyMethod postfix = null)
        {
            var targetType = GetTypeFromMod(targetModId, targetTypeName);
            if (targetType == null) return false;

            var targetMethod = targetType.GetMethod(targetMethodName,
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.Instance,
                null, parameterTypes, null);

            if (targetMethod == null)
            {
                Log.Warning($"[CrossModPatcher] Could not find method {targetMethodName} with specified parameters");
                return false;
            }

            harmony.Patch(targetMethod, prefix, postfix);
            return true;
        }

        /// <summary>
        /// Gets a type from a specific mod's assemblies
        /// </summary>
        public static Type GetTypeFromMod(string modId, string typeName)
        {
            var mod = LoadedModManager.RunningMods
                .FirstOrDefault(m => m.PackageId.ToLower() == modId.ToLower() ||
                                    m.PackageIdPlayerFacing.ToLower() == modId.ToLower());

            if (mod == null)
            {
                Log.Warning($"[CrossModPatcher] Mod {modId} not found or not loaded");
                return null;
            }

            foreach (var assembly in mod.assemblies.loadedAssemblies)
            {
                var type = assembly.GetType(typeName);
                if (type != null) return type;
            }

            return null;
        }

        /// <summary>
        /// Checks if a mod is loaded and active
        /// </summary>
        public static bool IsModLoaded(string modId)
        {
            return LoadedModManager.RunningMods
                .Any(m => m.PackageId.ToLower() == modId.ToLower() ||
                         m.PackageIdPlayerFacing.ToLower() == modId.ToLower());
        }

        /// <summary>
        /// Creates a HarmonyMethod helper
        /// </summary>
        public static HarmonyMethod CreateHarmonyMethod(Type patchClass, string methodName, int priority = -1)
        {
            var method = patchClass.GetMethod(methodName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            if (method == null)
            {
                Log.Error($"[CrossModPatcher] Could not find patch method {methodName} in {patchClass.Name}");
                return null;
            }

            var harmonyMethod = new HarmonyMethod(method);
            if (priority >= 0)
                harmonyMethod.priority = priority;

            return harmonyMethod;
        }
    }

    public class PatchDefinition
    {
        public string ModId { get; set; }
        public string TypeName { get; set; }
        public string MethodName { get; set; }
        public Type PatchClass { get; set; }
        public string PrefixMethod { get; set; }
        public string PostfixMethod { get; set; }
        public int Priority { get; set; } = -1;
        public Type[] ParameterTypes { get; set; }
    }

    public static class CrossModPatcherExtended
    {
        public static void ApplyPatches(Type patchClass, params PatchDefinition[] patches)
        {
            foreach (var patch in patches)
            {
                if (!CrossModPatcher.IsModLoaded(patch.ModId))
                {
                    Log.Message($"Skipping patches for {patch.ModId} - mod not loaded");
                    continue;
                }

                HarmonyMethod prefix = null;
                HarmonyMethod postfix = null;

                if (!patch.PrefixMethod.NullOrEmpty())
                    prefix = CrossModPatcher.CreateHarmonyMethod(patchClass, patch.PrefixMethod, patch.Priority);

                if (!patch.PostfixMethod.NullOrEmpty())
                    postfix = CrossModPatcher.CreateHarmonyMethod(patchClass, patch.PostfixMethod, patch.Priority);

                if (patch.ParameterTypes != null)
                {
                    CrossModPatcher.PatchMethodExact(
                        patch.ModId, patch.TypeName, patch.MethodName,
                        patch.ParameterTypes, prefix, postfix);
                }
                else
                {
                    CrossModPatcher.PatchMethod(
                        patch.ModId, patch.TypeName, patch.MethodName,
                        prefix, postfix);
                }
            }
        }
    }
}
