using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Anotar.Catel;
using Catel.Collections;
using Catel.Services;
using PresetMagician.Core.ApplicationTask;
using PresetMagician.Core.Models;
using PresetMagician.Utils.Logger;
using PresetMagician.Utils.Progress;

namespace PresetMagician.Core.Services
{
    public class PluginService
    {
        private readonly IDispatcherService _dispatcherService;
        private readonly RemoteVstService _remoteVstService;
        private readonly GlobalFrontendService _globalFrontendService;
        private readonly GlobalService _globalService;
        private readonly VendorPresetParserService _vendorPresetParserService;

        private Dictionary<string, string> DllHashes = new Dictionary<string, string>();

        public PluginService(RemoteVstService remoteVstService, IDispatcherService dispatcherService,
            GlobalFrontendService globalFrontendService, GlobalService globalService,
            VendorPresetParserService vendorPresetParserService)
        {
            _dispatcherService = dispatcherService;
            _remoteVstService = remoteVstService;
            _globalFrontendService = globalFrontendService;
            _globalService = globalService;
            _vendorPresetParserService = vendorPresetParserService;
        }

        /// <summary>
        /// Returns all plugin DLLs found in the specified directories
        /// </summary>
        /// <param name="vstDirectories">The VST directories to scan</param>
        /// <param name="applicationProgress">The progress reporter</param>
        /// <returns></returns>
        public HashSet<string> GetPluginDlls(IList<VstDirectory> vstDirectories,
            ApplicationProgress applicationProgress)
        {
            var vstPluginDLLFiles = new HashSet<string>();

            var dllFiles = new List<string>();
            var directoriesToScan = (from directory in vstDirectories where directory.Active select directory).ToList();

            var progressStatus = new CountProgress(directoriesToScan.Count);

            foreach (var i in directoriesToScan)
            {
                progressStatus.Current = directoriesToScan.IndexOf(i);
                progressStatus.Status = i.Path;

                applicationProgress.Progress.Report(progressStatus);

                if (applicationProgress.CancellationToken.IsCancellationRequested)
                {
                    return vstPluginDLLFiles;
                }

                try
                {
                    var vstPlugins = new List<string>();
                    foreach (var file in Directory.EnumerateFiles(
                        i.Path, "*.dll", SearchOption.AllDirectories))
                    {
                        vstPlugins.Add(file);
                        progressStatus.Status = file;

                        applicationProgress.Progress.Report(progressStatus);
                    }

                    dllFiles = vstPlugins;
                }
                catch (Exception e)
                {
                    applicationProgress.LogReporter.Report(new LogEntry(LogLevel.Error,
                        $"Unable to scan {i.Path} because of {e.Message}"));
                }

                if (dllFiles.Count > 0)
                {
                    vstPluginDLLFiles.AddRange(dllFiles);
                }
            }

            return vstPluginDLLFiles;
        }

        /// <summary>
        /// Checks all plugins in the list if they are present and if their DLL hashes have changes
        /// </summary>
        /// <returns>List of newly created plugins which were created because their DLL hash has changed</returns>
        public async Task<List<Plugin>> VerifyPlugins(IList<Plugin> plugins, HashSet<string> dllPaths,
            ApplicationProgress applicationProgress)
        {
            var remoteFileService = _remoteVstService.GetRemoteVstService();

            var pluginsToAdd = new List<Plugin>();
            var progressStatus = new CountProgress(plugins.Count);
            foreach (var plugin in plugins.ToList())
            {
                if (applicationProgress.CancellationToken.IsCancellationRequested)
                {
                    return pluginsToAdd;
                }

                try
                {
                    progressStatus.Status = $"Verifying {plugin.DllPath}";
                    progressStatus.Current = plugins.IndexOf(plugin);

                    applicationProgress.Progress.Report(progressStatus);
                    await UpdatePluginLocations(plugin);
                    plugin.UpdateRequiresMetadataScanFlag(_globalService.PresetMagicianVersion);

                    if (plugin.PluginLocation != null && !plugin.PluginLocation.IsPresent)
                    {
                        var newPluginLocation =
                            (from p in plugin.PluginLocations where p.IsPresent select p).FirstOrDefault();
                        plugin.PluginLocation = newPluginLocation;
                    }

                    foreach (var pluginLocation in plugin.PluginLocations)
                    {
                        if (pluginLocation.IsPresent)
                        {
                            dllPaths.Remove(pluginLocation.DllPath);
                        }
                    }

                    plugin.NativeInstrumentsResource.Load(plugin);
                }
                catch (Exception e)
                {
                    plugin.LogPluginError("verifying plugin", e);
                }
            }

            foreach (var dllPath in dllPaths)
            {
                var newPlugin = new Plugin
                {
                    PluginLocation = new PluginLocation
                    {
                        DllPath = dllPath, DllHash = await GetDllHash(dllPath),
                        LastModifiedDateTime = remoteFileService.GetLastModifiedDate(dllPath), IsPresent = true
                    }
                };

                newPlugin.UpdateRequiresMetadataScanFlag(_globalService.PresetMagicianVersion);

                progressStatus.Status = $"Adding Plugin {newPlugin.PluginLocation.DllPath}";
                applicationProgress.Progress.Report(progressStatus);
                pluginsToAdd.Add(newPlugin);
            }

            return pluginsToAdd;
        }

        /// <summary>
        /// Updates all plugin locations for a plugin. Sets the IsPresent flag.
        /// </summary>
        /// <param name="plugin"></param>
        private async Task UpdatePluginLocations(Plugin plugin)
        {
            var remoteFileService = _remoteVstService.GetRemoteVstService();

            foreach (var pluginLocation in plugin.PluginLocations)
            {
                if (!remoteFileService.Exists(pluginLocation.DllPath))
                {
                    pluginLocation.IsPresent = false;
                    continue;
                }

                var lastModification = remoteFileService.GetLastModifiedDate(pluginLocation.DllPath);

                if (lastModification == pluginLocation.LastModifiedDateTime)
                {
                    pluginLocation.IsPresent = true;
                    continue;
                }

                var currentHash = await GetDllHash(pluginLocation.DllPath);

                pluginLocation.IsPresent = pluginLocation.DllHash == currentHash;
            }
        }

        /// <summary>
        /// Calculate the DLL hashes for each plugin
        /// </summary>
        private async Task<string> GetDllHash(string dllPath)
        {
            var remoteFileService = _remoteVstService.GetRemoteVstService();


            var hash = remoteFileService.GetHash(dllPath);

            if (DllHashes.ContainsKey(dllPath))
            {
                DllHashes[dllPath] = hash;
            }
            else
            {
                DllHashes.Add(dllPath, hash);
            }

            return DllHashes[dllPath];
        }

        public async
            Task<(List<Plugin> removedPlugins, List<(Plugin oldPlugin, Plugin mergedIntoPlugin)> mergedPlugins)>
            UpdateMetadata(IList<Plugin> pluginsToUpdate,
                ApplicationProgress applicationProgress)
        {
            var pluginsToRemove = new List<Plugin>();
            var mergedPlugins = new List<(Plugin oldPlugin, Plugin mergedIntoPlugin)>();
            var progressStatus = new CountProgress(pluginsToUpdate.Count);

            foreach (var plugin in pluginsToUpdate)
            {
                if (applicationProgress.CancellationToken.IsCancellationRequested)
                {
                    return (pluginsToRemove, mergedPlugins);
                }

                try
                {
                    _globalFrontendService.SelectedPlugin = plugin;
                    progressStatus.Current = pluginsToUpdate.IndexOf(plugin);
                    progressStatus.Status = $"Loading metadata for {plugin.DllFilename}";
                    applicationProgress.Progress.Report(progressStatus);

                    var originalPluginLocation = plugin.PluginLocation;
                    foreach (var pluginLocation in plugin.PluginLocations)
                    {
                        if (pluginLocation.IsPresent &&
                            plugin.RequiresMetadataScan
                            /*pluginLocation.LastMetadataAnalysisVersion != _globalService.PresetMagicianVersion &&
                            (!pluginLocation.HasMetadata || plugin.RequiresMetadataScan || pluginLocation.PresetParser == null || (pluginLocation.PresetParser != null &&
                                                             pluginLocation.PresetParser.RequiresRescan()))*/)
                        {
                            plugin.PluginLocation = pluginLocation;
                            pluginLocation.LastMetadataAnalysisVersion = _globalService.PresetMagicianVersion;

                            using (var remotePluginInstance =
                                _remoteVstService.GetRemotePluginInstance(plugin, false))
                            {
                                try
                                {
                                    await remotePluginInstance.LoadPlugin();
                                    plugin.Logger.Debug($"Attempting to find presetParser for {plugin.PluginName}");
                                    pluginLocation.PresetParser = null;
                                    _vendorPresetParserService.DeterminatePresetParser(remotePluginInstance);
                                    remotePluginInstance.UnloadPlugin();
                                }
                                catch (Exception e)
                                {
                                    applicationProgress.LogReporter.Report(new LogEntry(LogLevel.Error,
                                        $"Error while loading metadata for {plugin.DllPath}: {e.GetType().FullName} {e.Message}"));
                                    LogTo.Debug(e.StackTrace);
                                }
                            }
                        }
                    }

                    plugin.PluginLocation = originalPluginLocation;
                    plugin.EnsurePluginLocationIsPresent();
                    plugin.UpdateRequiresMetadataScanFlag(_globalService.PresetMagicianVersion);

                    if (!plugin.HasMetadata && plugin.PluginLocation == null && plugin.Presets.Count == 0)
                    {
                        // No metadata and still no plugin location dll => remove it

                        applicationProgress.LogReporter.Report(new LogEntry(LogLevel.Info,
                            "Removing unknown plugin entry without metadata, without presets and without plugin dll"));
                        pluginsToRemove.Add(plugin);

                        continue;
                    }

                    if (!plugin.HasMetadata)
                    {
                        // Still no metadata, probably plugin loading failure. Try again in the next version.
                        continue;
                    }

                    if (_globalService.RuntimeConfiguration.StripBridgedPluginPrefix &&
                        plugin.PluginName.StartsWith("[jBridge]"))
                    {
                        plugin.OverridePluginName = true;
                        plugin.OverriddenPluginName = plugin.PluginName.Replace("[jBridge]", "");
                    }


                    // We now got metadata - check if there's an existing plugin with the same plugin ID. If so,
                    // merge this plugin with the existing one if it has no presets.
                    var existingPlugin =
                        (from p in _globalService.Plugins
                            where p.VstPluginId == plugin.VstPluginId && p.PluginId != plugin.PluginId &&
                                  plugin.IsPresent &&
                                  !pluginsToRemove.Contains(p)
                            select p)
                        .FirstOrDefault();

                    if (existingPlugin == null)
                    {
                        continue;
                    }

                    if (plugin.Presets.Count == 0)
                    {
                        // There's an existing plugin which this plugin can be merged into. Schedule it for removal
                        pluginsToRemove.Add(plugin);
                        mergedPlugins.Add((oldPlugin: plugin, mergedIntoPlugin: existingPlugin));

                        if (existingPlugin.PluginLocation == null)
                        {
                            if (Core.UseDispatcher)
                            {
                                await _dispatcherService.InvokeAsync(() =>
                                {
                                    existingPlugin.PluginLocation = plugin.PluginLocation;
                                });
                            }
                            else
                            {
                                existingPlugin.PluginLocation = plugin.PluginLocation;
                            }
                        }
                        else
                        {
                            existingPlugin.PluginLocations.Add(plugin.PluginLocation);
                            existingPlugin.EnsurePluginLocationIsPresent();
                        }
                    }
                    else if (existingPlugin.Presets.Count == 0)
                    {
                        // The existing plugin has no presets - remove it!
                        pluginsToRemove.Add(existingPlugin);
                        mergedPlugins.Add((oldPlugin: existingPlugin, mergedIntoPlugin: plugin));
                        if (plugin.PluginLocation == null)
                        {
                            if (Core.UseDispatcher)
                            {
                                await _dispatcherService.InvokeAsync(() =>
                                {
                                    plugin.PluginLocation = existingPlugin.PluginLocation;
                                });
                            }
                            else
                            {
                                plugin.PluginLocation = existingPlugin.PluginLocation;
                            }
                        }
                        else
                        {
                            plugin.PluginLocations.Add(existingPlugin.PluginLocation);
                            existingPlugin.EnsurePluginLocationIsPresent();
                        }
                    }
                }
                catch (Exception e)
                {
                    applicationProgress.LogReporter.Report(new LogEntry(LogLevel.Error,
                        $"Unable to load  metadata for {plugin.DllFilename} because of {e.GetType().FullName} {e.Message}"));
                }


                await _dispatcherService.InvokeAsync(() => { plugin.NativeInstrumentsResource.Load(plugin); });
            }

            return (pluginsToRemove, mergedPlugins);
        }
    }
}