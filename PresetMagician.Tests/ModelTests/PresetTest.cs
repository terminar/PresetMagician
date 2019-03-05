using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization.Json;
using Catel.Collections;
using Catel.Data;
using Catel.IoC;
using Catel.Reflection;
using Catel.Runtime.Serialization;
using FluentAssertions;
using Newtonsoft.Json;
using PresetMagician.Serialization;
using SharedModels;
using SharedModels.Collections;
using SharedModels.Extensions;
using Xunit;
using Xunit.Abstractions;
using JsonSerializer = Catel.Runtime.Serialization.Json.JsonSerializer;
using Type = SharedModels.Type;

// ReSharper disable PossibleNullReferenceException

namespace PresetMagician.Tests.ModelTests
{
    public class PresetTests: EmptyDbContextManager
    {
        private List<string> ExpectedDatabaseProperties = new List<string>
        {
            "PresetId",
            "PluginId", "VstPluginId", "IsMetadataModified", "LastExported", "BankPath", "PresetSize",
            "PresetCompressedSize", "PresetName", "PreviewNoteNumber", "Author", "Comment",
            "SourceFile", "PresetHash", "LastExportedPresetHash", "IsIgnored", "UserModifiedMetadata"
        };

        private Dictionary<string, object> BackupTestProperties =
            new Dictionary<string, object>
            {
                {"PresetId", Guid.Empty.ToString()},
                {"PluginId", 15},
                {"VstPluginId", 1234},
                {"IsMetadataModified", true},
                {"LastExported", DateTime.Now}, {"BankPath", "nonexistant"}, {"PresetSize", 12311},
                {"PresetCompressedSize", 3333},
                {"PresetName", "idk"},
                {"PreviewNoteNumber", 111},
                {"Author", "ibims"},
                {"Comment", "all okay"},
                {"SourceFile", "nowhere to be found"},
                {"PresetHash", "ff0ff"},
                {"LastExportedPresetHash", "yooooobar"}, {"IsIgnored", true},
                {"UserModifiedMetadata","[\"bla\"]"}
            };

        private Dictionary<string, object> PropertiesWhichShouldNotModifyIsMetadataModified =
            new Dictionary<string, object>
            {
                {"LastExportedPresetHash", "bla"},
                {"LastExported", DateTime.Now}
            };

        private Dictionary<string, object> PropertiesWhichShouldModifyIsMetadataModified =
            new Dictionary<string, object>
            {
                {"PresetName", "bla"},
                {"PreviewNoteNumber", 15},
                {"Author", "bla"},
                {"Comment", "bla"},
                {"BankPath", "bla/foo"},
                {"PresetHash", "dingdong"}
            };

        private HashSet<string> PropertiesWhichAreNotRelevantToMetadataModified =
            new HashSet<string>
            {
                "HasErrors",
                "HasWarnings",
                "IsDirty",
                "EditableProperties",
                "IsUserModified",
                "IsEditing",
                "TrackingState",
                "UserModifiedProperties",
                "ModifiedProperties",
                "EntityIdentifier",
                "IsReadOnly",
                "SourceFile",
                "IsIgnored",
                "PluginId",
                "Plugin",
                "SerializedUserModifiedMetadata",
                "ChangedSinceLastExport",
                "PresetId",
                "VstPluginId",
                "IsMetadataModified"
            };

        private readonly ITestOutputHelper _output;

        public PresetTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static Preset GetFreshPresetTestSubject()
        {
            var mode = new Mode();
            var type = new Type();
            var preset = new Preset();
            preset.BankPath = "foo/bar";
            var plugin = new Plugin();
            preset.Plugin = plugin;
            plugin.Presets.Add(preset);
            preset.Types.Add(type);
            preset.Modes.Add(mode);
            preset.LastExportedPresetHash = "foobar";
            preset.PresetHash = "foobar";
            preset.PresetName = "my preset";
            preset.IsMetadataModified = false;

            return preset;
        }

        [Fact]
        public void TestPresetDatabaseFields()
        {
            using (var context = ApplicationDatabaseContext.Create())
            {
                var actualDbProperties =
                    (from prop in context.GetTableColumns(typeof(Preset)) select prop.Key).ToList();

                var missingTestProperties = actualDbProperties.Except(ExpectedDatabaseProperties);
                missingTestProperties.Should().BeEmpty("Missing in unit test");

                var missingModelProperties = ExpectedDatabaseProperties.Except(actualDbProperties);
                missingModelProperties.Should().BeEmpty("Missing in model");
            }
        }

        [Fact]
        public void TestPresetCatelBackup()
        {
            
            var backupValues = new Dictionary<string, object>();

            using (var context = ApplicationDatabaseContext.Create())
            {
                var preset = GetFreshPresetTestSubject();

                foreach (var propName in ExpectedDatabaseProperties)
                {
                    var property = (from prop in context.GetTableColumns(typeof(Preset))
                        where prop.Key == propName
                        select prop.Value).First();

                    var value = preset.GetType().GetProperty(property.Name).GetValue(preset);
                    backupValues.Add(propName, value);
                }

                preset.Plugin.BeginEdit();
                preset.IsEditing.Should().BeTrue();
                preset.Plugin.IsEditing.Should().BeTrue();
                preset.Plugin.Presets.IsEditing.Should().BeTrue();
                
                foreach (var propName in ExpectedDatabaseProperties)
                {
                    var property = (from prop in context.GetTableColumns(typeof(Preset))
                        where prop.Key == propName
                        select prop.Value).First();
                    
                    BackupTestProperties.Should()
                        .ContainKey(propName, "because we need data to put into to test the backup");

                    BackupTestProperties[propName].Should()
                        .NotBeSameAs(preset.GetType().GetProperty(property.Name).GetValue(preset), "the test data must be different, otherwise we can't test the backup");

                    preset.GetType().GetProperty(property.Name).SetValue(preset, BackupTestProperties[propName]);
                }

                var plugin = preset.Plugin;
                (preset.Plugin as IEditableObject).CancelEdit();
                preset = plugin.Presets.First();
                

                foreach (var propName in ExpectedDatabaseProperties)
                {
                    var property = (from prop in context.GetTableColumns(typeof(Preset))
                        where prop.Key == propName
                        select prop.Value).First();

                    var actualValue = PropertyHelper.GetPropertyValue(preset, property.Name);
                    var expectedValue = backupValues[propName];

                    actualValue.Should().BeEquivalentTo(expectedValue, $"because the backup should work for property {property.Name}");
                }

                preset.Plugin.Should().Be(plugin);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        [Fact]
        public void TestPresetModifiedField()
        {
            var preset = GetFreshPresetTestSubject();

            preset.ChangedSinceLastExport.Should().BeFalse("It is unmodified");
            preset.IsMetadataModified.Should().BeFalse("It is unmodified");
        }

        [Fact]
        public void TestBackupPerformance()
        {
           
            for (var i = 0; i < 100; i++)
            {
                var preset2 = GetFreshPresetTestSubject();
                ((IEditableObject) preset2).BeginEdit();
            }

        }
        
        [Fact]
        public void TestBackupAndRestorePerformance()
        {
            for (var i = 0; i < 100; i++)
            {
                var preset2 = GetFreshPresetTestSubject();
                ((IEditableObject) preset2).BeginEdit();
                ((IEditableObject) preset2).CancelEdit();
            }
        }

        [Fact]
        public void TestIsMetadataModified()
        {
            var testedProperties = new HashSet<string>();

            ModelBase.DisablePropertyChangeNotifications.Should().Be(false);

            foreach (var notModifyProperty in PropertiesWhichShouldNotModifyIsMetadataModified)
            {
                var preset2 = GetFreshPresetTestSubject();
                ((IEditableObject) preset2).BeginEdit();

                preset2.GetType().GetProperty(notModifyProperty.Key).SetValue(preset2, notModifyProperty.Value);

                preset2.IsMetadataModified.Should()
                    .BeFalse($"{notModifyProperty.Key} should not modify IsMetadataModified");

                testedProperties.Add(notModifyProperty.Key);
            }

            foreach (var modifyProperty in PropertiesWhichShouldModifyIsMetadataModified)
            {
                var preset2 = GetFreshPresetTestSubject();
                (preset2 as IEditableObject).BeginEdit();
                preset2.GetType().GetProperty(modifyProperty.Key).SetValue(preset2, modifyProperty.Value);

                preset2.IsMetadataModified.Should()
                    .BeTrue($"{modifyProperty.Key} should modify IsMetadataModified when edit mode is on");

                if (Preset.PresetParserMetadataProperties.Contains(modifyProperty.Key))
                {
                    preset2.UserOverwrittenProperties.Should().Contain(modifyProperty.Key,
                        $"UserOverwrittenProperties should contain {modifyProperty.Key} because it's a PresetParserMetadataProperties which is now user modified");
                }

                testedProperties.Add(modifyProperty.Key);
            }

            foreach (var modifyProperty in PropertiesWhichShouldModifyIsMetadataModified)
            {
                var preset2 = GetFreshPresetTestSubject();
                preset2.GetType().GetProperty(modifyProperty.Key).SetValue(preset2, modifyProperty.Value);

                preset2.IsMetadataModified.Should()
                    .BeFalse($"{modifyProperty.Key} should not modify IsMetadataModified if edit mode is off");

                testedProperties.Add(modifyProperty.Key);
            }

            TestModesCollection(testedProperties);
            TestTypesCollection(testedProperties);

            var preset = GetFreshPresetTestSubject();
            (preset as IEditableObject).BeginEdit();
            preset.PresetSize = 1234;
            preset.IsMetadataModified.Should()
                .BeTrue("Changing the preset size should modify IsMetadataModified when edit mode is on");
            preset.UserModifiedProperties.Should().NotContain("PresetSize");
            testedProperties.Add("PresetSize");

            preset = GetFreshPresetTestSubject();
            (preset as IEditableObject).BeginEdit();
            preset.PresetCompressedSize = 1234;
            preset.IsMetadataModified.Should()
                .BeTrue("Changing the preset compressed size should modify IsMetadataModified when edit mode is on");
            preset.UserModifiedProperties.Should().NotContain("PresetCompressedSize");
            testedProperties.Add("PresetCompressedSize");

            preset = GetFreshPresetTestSubject();
            (preset as IEditableObject).BeginEdit();
            preset.PreviewNote.NoteNumber = 121;
            preset.IsMetadataModified.Should()
                .BeTrue("Changing the preview note object should modify IsMetadataModified when edit mode is on");
            preset.UserModifiedProperties.Should().NotContain("PreviewNote");
            preset.UserModifiedProperties.Should().Contain("PreviewNoteNumber");
            testedProperties.Add("PreviewNote");

            TestPresetBank(testedProperties);
            testedProperties.AddRange(PropertiesWhichAreNotRelevantToMetadataModified);


            var allProperties =
                (from prop in typeof(Preset).GetProperties() where !prop.GetType().IsEnum select prop.Name).ToList();

            allProperties.Except(testedProperties).Should().BeEmpty("We want to test ALL TEH PROPERTIEZ");
        }

        private void TestPresetBank(HashSet<string> testedProperties)
        {
            var preset = GetFreshPresetTestSubject();
            ((IEditableObject) preset).BeginEdit();

            preset.Plugin.RootBank.PresetBanks.First().PresetBanks.Add(new PresetBank() {BankName = "yo mama"});
            preset.IsMetadataModified.Should().BeFalse("a bank was added but did not change our bank path");

            preset = GetFreshPresetTestSubject();
            (preset as IEditableObject).BeginEdit();
            var pp = new PresetBank() {BankName = "yo mama"};
            preset.Plugin.RootBank.PresetBanks.First().PresetBanks.Add(pp);
            preset.PresetBank = pp;
            preset.IsMetadataModified.Should().BeTrue("the bank was changed and should trigger a change");

            preset = GetFreshPresetTestSubject();
            (preset as IEditableObject).BeginEdit();
            preset.Plugin.RootBank.PresetBanks.First().First().BankName = "diz changed";
            preset.IsMetadataModified.Should().BeTrue("the bank name was changed and should trigger a change");

            testedProperties.Add("PresetBank");
        }

        private void TestModesCollection(HashSet<string> testedProperties)
        {
            var preset = GetFreshPresetTestSubject();
            (preset as IEditableObject).BeginEdit();
            preset.Modes.First().Name = "test";
            preset.IsMetadataModified.Should()
                .BeTrue("Changing a mode name should modify IsMetadataModified when edit mode is on");
            preset.UserOverwrittenProperties.Should().Contain("Modes");

            preset = GetFreshPresetTestSubject();
            (preset as IEditableObject).BeginEdit();
            preset.Modes.RemoveFirst();
            preset.IsMetadataModified.Should()
                .BeTrue("Removing a mode should modify IsMetadataModified when edit mode is on");
            preset.UserOverwrittenProperties.Should().Contain("Modes");

            preset = GetFreshPresetTestSubject();
            (preset as IEditableObject).BeginEdit();
            preset.Modes.Add(new Mode {Name = "bla"});
            preset.IsMetadataModified.Should()
                .BeTrue("Adding a mode name should modify IsMetadataModified when edit mode is on");
            preset.UserOverwrittenProperties.Should().Contain("Modes");

            preset = GetFreshPresetTestSubject();
            (preset as IEditableObject).BeginEdit();
            preset.Modes = new TrackableCollection<Mode>();
            preset.IsMetadataModified.Should()
                .BeTrue("Replacing the modes collection should modify IsMetadataModified when edit mode is on");
            preset.UserOverwrittenProperties.Should().Contain("Modes");

            preset = GetFreshPresetTestSubject();
            var oldModes = preset.Modes;
            preset.Modes = new TrackableCollection<Mode>();

            (preset as IEditableObject).BeginEdit();
            oldModes.Clear();
            preset.IsMetadataModified.Should()
                .BeFalse(
                    "Modifying a detached modes collection should not modify IsMetadataModified when edit mode is on");
            preset.UserOverwrittenProperties.Should().NotContain("Modes");

            testedProperties.Add("Modes");
        }

        private void TestTypesCollection(HashSet<string> testedProperties)
        {
            var preset = GetFreshPresetTestSubject();
            (preset as IEditableObject).BeginEdit();
            preset.Types.First().Name = "test";
            preset.IsMetadataModified.Should()
                .BeTrue("Changing a type name should modify IsMetadataModified when edit mode is on");
            preset.UserOverwrittenProperties.Should().Contain("Types");

            preset = GetFreshPresetTestSubject();
            (preset as IEditableObject).BeginEdit();
            preset.Types.First().SubTypeName = "test";
            preset.IsMetadataModified.Should()
                .BeTrue("Changing a type name should modify IsMetadataModified when edit mode is on");
            preset.UserOverwrittenProperties.Should().Contain("Types");

            preset = GetFreshPresetTestSubject();
            (preset as IEditableObject).BeginEdit();
            preset.Types.RemoveFirst();
            preset.IsMetadataModified.Should()
                .BeTrue("Removing a type should modify IsMetadataModified when edit mode is on");
            preset.UserOverwrittenProperties.Should().Contain("Types");

            preset = GetFreshPresetTestSubject();
            (preset as IEditableObject).BeginEdit();
            preset.Types.Add(new Type {Name = "bla"});
            preset.IsMetadataModified.Should()
                .BeTrue("Adding a type name should modify IsMetadataModified when edit mode is on");
            preset.UserOverwrittenProperties.Should().Contain("Types");

            preset = GetFreshPresetTestSubject();
            (preset as IEditableObject).BeginEdit();
            preset.Types = new TrackableCollection<Type>();
            preset.IsMetadataModified.Should()
                .BeTrue("Replacing the type collection should modify IsMetadataModified when edit mode is on");
            preset.UserOverwrittenProperties.Should().Contain("Types");

            preset = GetFreshPresetTestSubject();
            var oldTypes = preset.Types;
            preset.Types = new TrackableCollection<Type>();

            (preset as IEditableObject).BeginEdit();
            oldTypes.Clear();
            preset.IsMetadataModified.Should()
                .BeFalse(
                    "Modifying a detached types collection should not modify IsMetadataModified when edit mode is on");
            preset.UserOverwrittenProperties.Should().NotContain("Types");

            testedProperties.Add("Types");
        }
    }
}