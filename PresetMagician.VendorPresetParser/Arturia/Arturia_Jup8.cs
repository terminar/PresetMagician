using System.Collections.Generic;
using JetBrains.Annotations;
using PresetMagician.Core.Interfaces;

namespace PresetMagician.VendorPresetParser.Arturia
{
    // ReSharper disable once InconsistentNaming
    [UsedImplicitly]
    public class Arturia_Jup8 : Arturia, IVendorPresetParser
    {
        public override List<int> SupportedPlugins => new List<int> {1247105075};

        protected override List<string> GetInstrumentNames()
        {
            return new List<string> {"Jup-8"};
        }
    }
}