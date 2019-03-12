using System.Collections.Generic;
using JetBrains.Annotations;
using PresetMagician.Core.Interfaces;

namespace Drachenkatze.PresetMagician.VendorPresetParser.Arturia
{
    // ReSharper disable once InconsistentNaming
    [UsedImplicitly]
    public class Arturia_Modular : Arturia, IVendorPresetParser
    {
        public override List<int> SupportedPlugins => new List<int> {1297040435};

        protected override List<string> GetInstrumentNames()
        {
            return new List<string> {"Modular"};
        }
    }
}