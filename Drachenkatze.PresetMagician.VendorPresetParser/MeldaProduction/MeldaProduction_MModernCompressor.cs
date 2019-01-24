using System.Collections.Generic;
using JetBrains.Annotations;

namespace Drachenkatze.PresetMagician.VendorPresetParser.MeldaProduction
{
    // ReSharper disable once InconsistentNaming
    [UsedImplicitly]
    public class MeldaProduction_MModernCompressor : MeldaProduction, IVendorPresetParser
    {
        public override List<int> SupportedPlugins => new List<int> {1296920387};

        public void ScanBanks()
        {
            ScanPresetXMLFile("MModernCompressorpresets.xml", "MModernCompressorpresetspresets");
        }
    }
}