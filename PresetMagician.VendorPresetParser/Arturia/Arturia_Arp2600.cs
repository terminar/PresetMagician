using System.Collections.Generic;
using JetBrains.Annotations;
using PresetMagician.Core.Interfaces;

namespace PresetMagician.VendorPresetParser.Arturia
{
    [UsedImplicitly]
    public class Arturia_Arp2600 : Arturia, IVendorPresetParser
    {
        public override List<int> SupportedPlugins => new List<int> {1095913523};

        protected override List<string> GetInstrumentNames()
        {
            return new List<string> {"ARP 2600"};
        }
    }
}