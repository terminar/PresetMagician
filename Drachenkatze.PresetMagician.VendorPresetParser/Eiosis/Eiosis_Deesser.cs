using System;
using System.Collections.Generic;
using System.IO;
using Drachenkatze.PresetMagician.VendorPresetParser.SlateDigital;
using JetBrains.Annotations;

namespace Drachenkatze.PresetMagician.VendorPresetParser.Eiosis
{
    // ReSharper disable once InconsistentNaming
    [UsedImplicitly]
    public class Eiosis_Deesser : SlateDigital.SlateDigitalPresetParser, IVendorPresetParser
    {
       public override List<int> SupportedPlugins => new List<int> {1160922195};

       protected override string GetParseDirectory()
       {
           return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
               @"Eiosis\E2Deesser\Presets");
       }

       protected override string PresetSectionName { get; } = "E2DS";
    }
}