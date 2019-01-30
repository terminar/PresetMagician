﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration.Attributes;
using Drachenkatze.PresetMagician.Utils;
using Drachenkatze.PresetMagician.VendorPresetParser;
using PresetMagician.Models;
using PresetMagician.ProcessIsolation;
using PresetMagician.ProcessIsolation.Services;
using PresetMagician.VstHost.VST;
using SharedModels;
using Type = SharedModels.Type;

namespace PresetMagician.VendorPresetParserTest
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            var pluginTestDirectory = @"C:\Program Files\VSTPlugins";
            var testResults = new List<PluginTestResult>();

            var presetParserDictionary = VendorPresetParser.GetPresetHandlerListByPlugin();

            ApplicationDatabaseContext.OverrideDbPath = Path.Combine(Directory.GetCurrentDirectory(), "test.sqlite3");

            if (File.Exists(ApplicationDatabaseContext.OverrideDbPath))
            {
                File.Delete(ApplicationDatabaseContext.OverrideDbPath);
            }

            Console.WriteLine(ApplicationDatabaseContext.OverrideDbPath);
            var testData = ReadTestData();
            var ignoredPlugins = ReadIgnoredPlugins();

            Console.WriteLine(testData);

            List<string> IgnoredPresetParsers = new List<string>();
            IgnoredPresetParsers.Add("VoidPresetParser");

            foreach (var presetParserKeyValue in presetParserDictionary)
            {
                var presetParser = presetParserKeyValue.Value;
                var pluginId = presetParserKeyValue.Key;

                if (IgnoredPresetParsers.Contains(presetParser.PresetParserType))
                {
                    continue;
                }

                if (IsIgnored(ignoredPlugins, presetParser.PresetParserType, pluginId))
                {
                    continue;
                }

                Console.Write(presetParser.PresetParserType + ": ");
                
                var start = DateTime.Now;

                var pluginLocation = new PluginLocation
                {
                    DllPath = @"C:\Program Files\VstPlugins\Foobar.dll", IsPresent = true
                };

                var plugin = new Plugin {PluginId = pluginId, PluginLocation = pluginLocation};

                var stubProcess = new StubIsolatedProcess();
                
                var remoteInstance = new RemotePluginInstance(stubProcess, plugin);


                presetParser.DataPersistence = new NullPresetPersistence();
                presetParser.PluginInstance = remoteInstance;
                presetParser.RootBank = plugin.RootBank.First();
                presetParser.Presets = new ObservableCollection<Preset>();

                var testResult = new PluginTestResult
                {
                    VendorPresetParser = presetParser.PresetParserType,
                    PluginId = plugin.PluginId
                };

                double timeForNumPresets = 0;
                double timeForDoScan = 0;
                double totalTime = 0;
                try
                {
                    presetParser.Init();
                    testResult.ReportedPresets = presetParser.GetNumPresets();
                    timeForNumPresets = (DateTime.Now - start).TotalSeconds;
                    start = DateTime.Now;
                    presetParser.DoScan().GetAwaiter().GetResult();
                    timeForDoScan = (DateTime.Now - start).TotalSeconds;
                    totalTime = timeForNumPresets + timeForDoScan;
                }
                catch (Exception e)
                {
                    testResult.Error = "Errored";
                }

                testResult.Presets = plugin.Presets.Count;

                
                // ReSharper disable once LocalizableElement
                Console.WriteLine($"{testResult.Presets} parsed in {totalTime}s (DoScan {timeForDoScan}s NumPresets {timeForNumPresets}s");

                var testDataEntry = GetTestDataEntry(testData, presetParser.PresetParserType, pluginId);
                var foundPreset = false;
                foreach (var preset in plugin.Presets)
                {
                    if (preset.BankPath == "")
                    {
                        testResult.BankMissing++;
                    }

                    if (testDataEntry != null)
                    {
                        if (preset.PresetName == testDataEntry.ProgramName &&
                            preset.PresetHash == testDataEntry.Hash.TrimEnd() &&
                            preset.BankPath == testDataEntry.BankPath)
                        {
                            foundPreset = true;
                        }
                    }
                }

                if (plugin.Presets.Count > 0)
                {
                    var randomPreset = plugin.Presets.OrderBy(qu => Guid.NewGuid()).First();
                    testResult.RndHash = randomPreset.PresetHash;
                    testResult.RndPresetName = randomPreset.PresetName;
                    testResult.RndBankPath = randomPreset.BankPath;
                    //testResult.RandomPresetSource = randomPreset.SourceFile;
                }

                if (foundPreset && plugin.Presets.Count > 5 && testResult.BankMissing < 2 &&
                     plugin.Presets.Count == testResult.ReportedPresets)
                {
                    testResult.IsOK = true;
                }


                testResults.Add(testResult);
            }


            var consoleTable = ConsoleTable.From(from testRes in testResults
                where testRes.IsOK == false
                orderby testRes.Presets
                select testRes);

            Console.WriteLine(consoleTable.ToMinimalString());

            Console.WriteLine("Stuff left: " + consoleTable.Rows.Count);
        }

        public static List<TestData> ReadTestData()
        {
            using (var reader = new StreamReader("testdata.csv"))
            using (var csv = new CsvReader(reader))
            {
                return csv.GetRecords<TestData>().ToList();
            }
        }

        public static List<IgnoredPlugins> ReadIgnoredPlugins()
        {
            using (var reader = new StreamReader("ignoredplugins.csv"))
            using (var csv = new CsvReader(reader))
            {
                return csv.GetRecords<IgnoredPlugins>().ToList();
            }
        }

        public static TestData GetTestDataEntry(List<TestData> testData, string presetParser, int pluginId)
        {
            return (from testDataEntry in testData
                where
                    testDataEntry.PluginId == pluginId && testDataEntry.PresetParser == presetParser
                select testDataEntry).FirstOrDefault();
        }

        public static bool IsIgnored(List<IgnoredPlugins> ignoredPlugins, string presetParser, int pluginId)
        {
            return (from testDataEntry in ignoredPlugins
                where
                    testDataEntry.PluginId == pluginId && testDataEntry.PresetParser == presetParser
                select testDataEntry).Any();
        }
    }

    class StubIsolatedProcess : IIsolatedProcess
    {
        public void Kill(string reason)
        {
            
        }

        public int Pid { get; }

        public ProxiedRemoteVstService GetVstService()
        {
            return new ProxiedRemoteVstService(new StubRemoteVstService(), this);

        }
    }
    
     public class StubRemoteVstService : IRemoteVstService
     {
         public void KillSelf()
         {
             
         }

         public bool Exists(string file)
         {
             return true;
         }

         public long GetSize(string file)
         {
             return 0;
         }

         public string GetHash(string file)
         {
             return "";
         }

         public byte[] GetContents(string file)
         {
             return null;
         }

         public int GetPluginVendorVersion(Guid pluginGuid)
         {
             return 0;
         }

         public string GetPluginProductString(Guid pluginGuid)
         {
             return null;
         }

         public string GetEffectivePluginName(Guid pluginGuid)
         {
             return null;
         }

         public bool Ping()
         {
             return true;
         }
 
         public Guid RegisterPlugin(string dllPath, bool backgroundProcessing = true)
         {
             var guid = Guid.NewGuid();
             return guid;
         }
 
         public string GetPluginHash(Guid guid)
         {
             return null;
         }
 
         public void LoadPlugin(Guid guid)
         {
            
         }
 
         public void UnloadPlugin(Guid guid)
         {
            
         }
 
         public void ReloadPlugin(Guid guid)
         {
            
         }
 
         private RemoteVstPlugin GetPluginByGuid(Guid guid)
         {
             return null;
         }
 
         public bool OpenEditorHidden(Guid pluginGuid)
         {
             return true;
         }
 
         public bool OpenEditor(Guid pluginGuid)
         {
             return true;
         }
 
         public void CloseEditor(Guid pluginGuid)
         {
           
         }
 
         public byte[] CreateScreenshot(Guid pluginGuid)
         {
             return null;
         }
 
         public string GetPluginName(Guid pluginGuid)
         {
             return null;
         }
 
         public string GetPluginVendor(Guid pluginGuid)
         {
             return null;
         }
 
         public List<PluginInfoItem> GetPluginInfoItems(Guid pluginGuid)
         {
             return null;
         }
 
         public VstPluginInfoSurrogate GetPluginInfo(Guid pluginGuid)
         {
             return null;
         }
 
         public void SetProgram(Guid pluginGuid, int program)
         {
            
         }
 
         public string GetCurrentProgramName(Guid pluginGuid)
         {
             return null;
         }
 
         public byte[] GetChunk(Guid pluginGuid, bool isPreset)
         {
             return Encoding.UTF8.GetBytes("foo");
         }
 
         public void SetChunk(Guid pluginGuid, byte[] data, bool isPreset)
         {
    
         }
 
         public void ExportNksAudioPreview(Guid pluginGuid, PresetExportInfo preset, byte[] presetData,
             string userContentDirectory, int initialDelay)
         {

         }
 
         public void ExportNks(Guid pluginGuid, PresetExportInfo preset, byte[] presetData, string userContentDirectory)
         {
          
         }
     }
    internal class NullPresetPersistence : IDataPersistence
    {
#pragma warning disable 1998
        public async Task PersistPreset(Preset preset, byte[] data)
#pragma warning restore 1998
        {
            preset.Plugin.Presets.Add(preset);
            preset.PresetHash = HashUtils.getIxxHash(data);
        }

#pragma warning disable 1998
        public async Task Flush()
#pragma warning restore 1998
        {
        }

        public Type GetOrCreateType(string typeName, string subTypeName = "")
        {
            return new Type();
        }

        public Mode GetOrCreateMode(string modeName)
        {
            return new Mode();
        }
    }

    class PluginTestResult
    {
        public int Presets { get; set; }
        public int ReportedPresets { get; set; }
        public string VendorPresetParser { get; set; }
        public string Error { get; set; }
        public int PluginId { get; set; }
        public int BankMissing { get; set; }
        public string RndPresetName { get; set; }
        public string RndBankPath { get; set; }
        public string RndHash { get; set; }

        public string CsvData
        {
            get
            {
                if (Presets > 5 && BankMissing < 2 && Presets == ReportedPresets)
                {
                    return string.Join(",", new string[]
                    {
                        VendorPresetParser,
                        PluginId.ToString(),
                        RndPresetName,
                        RndBankPath,
                        RndHash
                    });
                }

                return "";
            }
        }

        public bool IsOK { get; set; }
        public string RandomPresetSource { get; set; }
    }

    public class TestData
    {
        [Name("PresetParser")] public string PresetParser { get; set; }

        [Name("PluginId")] public int PluginId { get; set; }

        [Name("ProgramName")] public string ProgramName { get; set; }

        [Name("BankPath")] public string BankPath { get; set; }

        [Name("Hash")] public string Hash { get; set; }
    }

    public class IgnoredPlugins
    {
        [Name("PresetParser")] public string PresetParser { get; set; }

        [Name("PluginId")] public int PluginId { get; set; }
    }
}