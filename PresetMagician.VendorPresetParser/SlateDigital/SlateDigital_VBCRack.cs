using System;
using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;
using PresetMagician.Core.Interfaces;

namespace PresetMagician.VendorPresetParser.SlateDigital
{
    // ReSharper disable once InconsistentNaming
    [UsedImplicitly]
    public class SlateDigital_VBCRack : SlateDigitalPresetParser, IVendorPresetParser
    {
        public override List<int> SupportedPlugins => new List<int> {1447183211};

        protected override string PresetSectionName { get; } = "VBCk";

        protected override string GetParseDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                @"Slate Digital\Virtual Buss Compressors Rack\Presets");
        }
    }
}