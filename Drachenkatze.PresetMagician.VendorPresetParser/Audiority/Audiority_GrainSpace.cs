using System;
using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;

namespace Drachenkatze.PresetMagician.VendorPresetParser.Audiority
{
    // ReSharper disable once InconsistentNaming
    [UsedImplicitly]
    public class Audiority_GrainSpace: Audiority, IVendorPresetParser
    {
        public override List<int> SupportedPlugins => new List<int> {1097090899};


        public void ScanBanks()
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
                @"Audiority\Presets\GrainSpace");

            DoScan(RootBank, directory);
        }
    }
}