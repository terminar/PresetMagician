using System.Collections.Generic;
using JetBrains.Annotations;

namespace Drachenkatze.PresetMagician.VendorPresetParser.MeldaProduction
{
    // ReSharper disable once InconsistentNaming
    [UsedImplicitly]
    public class MeldaProduction_MDistortionMB: MeldaProduction, IVendorPresetParser
    {
        public override List<int> SupportedPlugins => new List<int> {1298426227};

        public void ScanBanks()
        {
            ScanPresetXMLFile("MMultiBandDistortionpresets.xml", "MMultiBandDistortionpresetspresets");
        }
    }
}