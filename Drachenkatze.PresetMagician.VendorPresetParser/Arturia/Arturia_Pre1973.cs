using System.Collections.Generic;
using JetBrains.Annotations;

namespace Drachenkatze.PresetMagician.VendorPresetParser.Arturia
{
    // ReSharper disable once InconsistentNaming
    [UsedImplicitly]
    public class Arturia_Pre1973: Arturia, IVendorPresetParser
    {
        public override List<int> SupportedPlugins => new List<int> { 1347565363 };

        public void ScanBanks()
        {
            var instruments = new List<string> {"1973-Pre"};
            ScanPresets(instruments);
        }
    }
}