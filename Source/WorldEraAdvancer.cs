using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VFETribals;

namespace LemProgress
{
    public static class WorldEraAdvancer
    {
        public static void AdvanceTo(EraAdvancementDef def)
        {
            var old = WorldTechLevel.WorldTechLevel.Current;

            WorldTechLevel.WorldTechLevel.Current = def.newTechLevel;

            FactionTechUpgrader.UpgradeAllFactionsToTechLevel(old, def.newTechLevel);
        }
    }
}
