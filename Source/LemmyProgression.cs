using HarmonyLib;
using System.Reflection;
using Verse;
namespace LemProgress

{
    [StaticConstructorOnStartup]
    public static class LemmyProgression
    {
        static LemmyProgression() //our constructor
        {

            Log.Message("LemProg: Init");

            // Initialize the patcher
            CrossModPatcher.Initialize("LemmyMods.LemProgression");

            // Define all patches
            var patches = new[]
            {
                new PatchDefinition
                {
                    ModId = "m00nl1ght.worldtechlevel",
                    TypeName = "WorldTechLevel.TechLevelUtility",
                    MethodName = "PlayerResearchFilterLevel",
                    PatchClass = typeof(TechLevelPatcher),
                    PrefixMethod = "Prefix"
                },
                new PatchDefinition
                {
                    ModId = "oskarpotocki.vfe.tribals",
                    TypeName = "VFETribals.GameComponent_Tribals",
                    MethodName = "AdvanceToEra",
                    PatchClass = typeof(AdvanceTechLevelPatcher),
                    PrefixMethod = "Prefix"
                }
            };

            // Apply all patches
            foreach (var patch in patches)
            {
                if (CrossModPatcher.IsModLoaded(patch.ModId))
                {
                    Log.Message($"LemProg: Found {patch.ModId}");

                    bool success = CrossModPatcher.PatchMethod(
                        targetModId: patch.ModId,
                        targetTypeName: patch.TypeName,
                        targetMethodName: patch.MethodName,
                        prefix: CrossModPatcher.CreateHarmonyMethod(patch.PatchClass, patch.PrefixMethod)
                    );

                    if (success)
                        Log.Message($"LemProg: Successfully patched {patch.MethodName}");
                }
            }
        }
    }
    
}
