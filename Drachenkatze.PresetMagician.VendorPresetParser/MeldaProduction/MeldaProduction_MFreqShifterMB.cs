using System.Collections.Generic;
using JetBrains.Annotations;

namespace Drachenkatze.PresetMagician.VendorPresetParser.MeldaProduction
{
    // ReSharper disable once InconsistentNaming
    [UsedImplicitly]
    public class MeldaProduction_MFreqShifterMB : MeldaProduction, IVendorPresetParser
    {
        public override List<int> SupportedPlugins => new List<int> {1296125254};

        protected override string PresetFile { get; } =
            "MMultiBandFreqShifterpresets.xml";

        protected override string RootTag { get; } = "MMultiBandFreqShifterpresetspresets";
    }
}