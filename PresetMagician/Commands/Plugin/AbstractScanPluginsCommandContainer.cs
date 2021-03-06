using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Anotar.Catel;
using Catel.IoC;
using Catel.MVVM;
using Catel.Services;
using Catel.Threading;
using Catel.Windows;
using PresetMagician.Core.EventArgs;
using PresetMagician.Core.Interfaces;
using PresetMagician.Core.Models;
using PresetMagician.Core.Services;
using PresetMagician.Services.Interfaces;
using PresetMagician.Utils.Logger;

// ReSharper disable once CheckNamespace
namespace PresetMagician
{
    // ReSharper disable once UnusedMember.Global
    public abstract class AbstractScanPluginsCommandContainer : ThreadedApplicationNotBusyCommandContainer
    {
        protected readonly IApplicationService _applicationService;
        protected readonly IDispatcherService _dispatcherService;
        protected readonly ICommandManager _commandManager;
        protected readonly INativeInstrumentsResourceGeneratorService _resourceGeneratorService;
        protected readonly PluginService _pluginService;
        protected readonly GlobalService GlobalService;
        private readonly PresetDataPersisterService _presetDataPersisterService;
        private readonly DataPersisterService _dataPersisterService;
        private readonly IAdvancedMessageService _messageService;
        private readonly RemoteVstService _remoteVstService;
        private int _totalPresets;
        private int _currentPresetIndex;
        private int updateThrottle;
        private int _currentPluginIndex;
        private Plugin _currentPlugin;
        protected bool SkipPresetLoading = false;

        protected AbstractScanPluginsCommandContainer(string command, ICommandManager commandManager,
            IServiceLocator serviceLocator)
            : base(command, commandManager, serviceLocator)
        {
            _messageService = ServiceLocator.ResolveType<IAdvancedMessageService>();
            _applicationService = ServiceLocator.ResolveType<IApplicationService>();
            _dispatcherService = ServiceLocator.ResolveType<IDispatcherService>();
            _commandManager = commandManager;
            _dataPersisterService = ServiceLocator.ResolveType<DataPersisterService>();
            _presetDataPersisterService = ServiceLocator.ResolveType<PresetDataPersisterService>();
            ;
            _resourceGeneratorService =
                ServiceLocator.ResolveType<INativeInstrumentsResourceGeneratorService>();
            _pluginService = ServiceLocator.ResolveType<PluginService>();
            GlobalService = ServiceLocator.ResolveType<GlobalService>();
            _remoteVstService = ServiceLocator.ResolveType<RemoteVstService>();

            GlobalService.Plugins.CollectionChanged += OnPluginsListChanged;
        }

        protected abstract List<Plugin> GetPluginsToScan();


        protected override bool CanExecute(object parameter)
        {
            return base.CanExecute(parameter) && GlobalService.Plugins.Count > 0;
        }

        protected void OnPluginsListChanged(object o, NotifyCollectionChangedEventArgs ev)
        {
            InvalidateCommand();
        }

        protected override async Task ExecuteThreaded(object parameter)
        {
            var pluginsToScan = GetPluginsToScan();


            var pluginsToUpdateMetadata =
                (from p in GlobalService.Plugins
                    where p.RequiresMetadataScan
                    orderby p.PluginName, p.DllFilename
                    select p).ToList();

            _applicationService.StartApplicationOperation(this, "Analyzing VST plugins: Loading missing metadata",
                pluginsToUpdateMetadata.Count);

            var pluginsToRemove = new List<Plugin>();
            var mergedPlugins = new List<(Plugin oldPlugin, Plugin mergedIntoPlugin)>();
            var progress = _applicationService.GetApplicationProgress();
            // First pass: Load missing metadata
            try
            {
                var result = await TaskHelper.Run(
                    async () => await _pluginService.UpdateMetadata(pluginsToUpdateMetadata, progress), true,
                    progress.CancellationToken);

                pluginsToRemove = result.removedPlugins;
                mergedPlugins = result.mergedPlugins;

                await _dispatcherService.InvokeAsync(() =>
                {
                    foreach (var plugin in pluginsToRemove)
                    {
                        GlobalService.Plugins.Remove(plugin);

                        if (pluginsToScan.Contains(plugin))
                        {
                            pluginsToScan.Remove(plugin);
                        }
                    }
                });
            }
            catch (Exception e)
            {
                _applicationService.AddApplicationOperationError(
                    $"Unable to update metadata because of {e.GetType().FullName}: {e.Message}");
                LogTo.Debug(e.StackTrace);
            }

            _applicationService.StopApplicationOperation("Analyzing VST plugins Metadata analysis complete.");

            if (mergedPlugins.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var merge in (from p in mergedPlugins orderby p.oldPlugin.LastKnownGoodDllPath select p))
                {
                    sb.AppendLine(
                        $"{merge.oldPlugin.LastKnownGoodDllPath} => {merge.mergedIntoPlugin.LastKnownGoodDllPath}");
                }


                var result = await _messageService.ShowAsync(
                    "Automatically merged different plugin DLLs to the same plugin. Affected plugin(s):" +
                    Environment.NewLine + Environment.NewLine +
                    sb + Environment.NewLine + Environment.NewLine +
                    "Would you like to abort the analysis now, so that you can review the settings for each affected plugin? (Highly recommended!)",
                    "Auto-merged Plugins", HelpLinks.SETTINGS_PLUGIN_DLL, MessageButton.YesNo, MessageImage.Question);

                if (result == MessageResult.Yes)
                {
                    // ReSharper disable once MethodSupportsCancellation
                    _dataPersisterService.Save();
                    _commandManager.ExecuteCommand(Commands.Application.CancelOperation);
                }
            }

            if (!progress.CancellationToken.IsCancellationRequested && !SkipPresetLoading)
            {
                _applicationService.StartApplicationOperation(this, "Analyzing VST plugins",
                    pluginsToScan.Count);
                progress = _applicationService.GetApplicationProgress();


                await TaskHelper.Run(
                    async () => await AnalyzePlugins(pluginsToScan.OrderBy(p => p.PluginName).ToList(),
                        progress.CancellationToken), true,
                    progress.CancellationToken);

                // ReSharper disable once MethodSupportsCancellation
                _dataPersisterService.Save();
            }


            if (progress.CancellationToken.IsCancellationRequested)
            {
                _applicationService.StopApplicationOperation("Plugin analysis cancelled.");
            }
            else
            {
                _applicationService.StopApplicationOperation("Plugin analysis completed.");
            }

            var unreportedPlugins =
                (from plugin in GlobalService.Plugins
                    where !plugin.IsReported && !plugin.DontReport && !plugin.IsSupported && plugin.HasMetadata &&
                          plugin.IsEnabled
                    select plugin).ToList();

            if (unreportedPlugins.Any())
            {
                var result = await _messageService.ShowCustomRememberMyChoiceDialogAsync(
                    "There are unsupported plugins which are not reported." +
                    Environment.NewLine +
                    "Would you like to report them, so we can implement support for them?",
                    "Report Unsupported Plugins", null, MessageButton.YesNo, MessageImage.Question,
                    "Don't ask again for the currently unreported plugins");


                if (result.result == MessageResult.Yes)
                {
                    _commandManager.ExecuteCommand(Commands.Plugin.ReportUnsupportedPlugins);
                }

                if (result.dontChecked)
                {
                    foreach (var plugin in unreportedPlugins)
                    {
                        plugin.DontReport = true;
                    }

                    _dataPersisterService.Save();
                }
            }
        }

        private void ContextOnPresetUpdated(object sender, PresetUpdatedEventArgs e)
        {
            updateThrottle++;
            _currentPresetIndex++;

            if (_currentPresetIndex > _totalPresets)
            {
                Debug.WriteLine(
                    $"{e.NewValue.Plugin.PluginName}: Got called with {e.NewValue.Metadata.PresetName} index {_currentPresetIndex} of {_totalPresets}");
            }

            if (updateThrottle > 10)
            {
                _applicationService.UpdateApplicationOperationStatus(
                    _currentPluginIndex,
                    $"Adding/Updating presets for {_currentPlugin.PluginName} ({_currentPresetIndex} / {_totalPresets}): Preset {e.NewValue.Metadata.PresetName}");
                updateThrottle = 0;
            }
        }


        private async Task AnalyzePlugins(IList<Plugin> pluginsToScan, CancellationToken cancellationToken)
        {
            foreach (var plugin in pluginsToScan)
            {
                if (!plugin.IsPresent)
                {
                    continue;
                }

                if (!plugin.HasMetadata)
                {
                    continue;
                }

                LogTo.Debug($"Begin analysis of {plugin.DllFilename}");
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                _globalFrontendService.SelectedPlugin = plugin;

                try
                {
                    using (var remotePluginInstance = _remoteVstService.GetRemotePluginInstance(plugin))
                    {
                        _applicationService.UpdateApplicationOperationStatus(
                            pluginsToScan.IndexOf(plugin), $"Scanning {plugin.DllFilename}");


                        if (!plugin.HasMetadata)
                        {
                            if (plugin.LoadError)
                            {
                                LogTo.Debug($"Skipping {plugin.DllPath} because a load error occured");
                            }
                            else
                            {
                                throw new Exception(
                                    $"Plugin {plugin.DllPath} has no metadata and was loaded correctly.");
                            }
                        }

                        if (plugin.PresetParser == null)
                        {
                            throw new Exception(
                                $"Plugin {plugin.DllPath} has no preset parser. Please report this as a bug.");
                        }


                        var wasLoaded = remotePluginInstance.IsLoaded;
                        plugin.PresetParser.PluginInstance = remotePluginInstance;
                        plugin.PresetParser.DataPersistence = _presetDataPersisterService;
                        await _presetDataPersisterService.OpenDatabase();

                        _presetDataPersisterService.PresetUpdated += ContextOnPresetUpdated;
                        _currentPluginIndex = pluginsToScan.IndexOf(plugin);
                        _currentPlugin = plugin;

                        plugin.PresetParser.RootBank = plugin.RootBank.First();

                        plugin.PresetParser.Logger.MirrorTo(plugin.Logger);
                        _totalPresets = plugin.PresetParser.GetNumPresets();
                        _currentPresetIndex = 0;

                        await plugin.PresetParser.DoScan();

                        await _presetDataPersisterService.CloseDatabase();
                        _dataPersisterService.SavePresetsForPlugin(plugin);

                        await _dispatcherService.InvokeAsync(() => { plugin.NativeInstrumentsResource.Load(plugin); });

                        if (GlobalService.RuntimeConfiguration.AutoCreateResources &&
                            _resourceGeneratorService.ShouldCreateScreenshot(remotePluginInstance))
                        {
                            plugin.Logger.Debug(
                                $"Auto-generating resources for {plugin.DllFilename} - Opening Editor");
                            _applicationService.UpdateApplicationOperationStatus(
                                pluginsToScan.IndexOf(plugin),
                                $"Auto-generating resources for {plugin.DllFilename} - Opening Editor");
                            if (!remotePluginInstance.IsLoaded)
                            {
                                await remotePluginInstance.LoadPlugin();
                            }

                            remotePluginInstance.OpenEditorHidden();
                            _dispatcherService.Invoke(() => Application.Current.MainWindow.BringWindowToTop());
                            await Task.Delay(1000);
                        }

                        await _dispatcherService.InvokeAsync(() =>
                        {
                            if (GlobalService.RuntimeConfiguration.AutoCreateResources &&
                                _resourceGeneratorService.NeedToGenerateResources(remotePluginInstance))
                            {
                                plugin.Logger.Debug(
                                    $"Auto-generating resources for {plugin.DllFilename} - Creating screenshot and applying magic");
                                _applicationService.UpdateApplicationOperationStatus(
                                    pluginsToScan.IndexOf(plugin),
                                    $"Auto-generating resources for {plugin.DllFilename} - Creating screenshot and applying magic");

                                _resourceGeneratorService.AutoGenerateResources(remotePluginInstance);
                            }
                        });
                        wasLoaded = remotePluginInstance.IsLoaded;


                        _applicationService.UpdateApplicationOperationStatus(
                            pluginsToScan.IndexOf(plugin),
                            $"{plugin.DllFilename} - Updating Database");
                        _dataPersisterService.SavePlugin(plugin);


                        if (wasLoaded)
                        {
                            plugin.Logger.Debug($"Unloading {plugin.DllFilename}");
                            remotePluginInstance.UnloadPlugin();
                        }
                    }
                }
                catch (Exception e)
                {
                    plugin.LogPluginError("loading presets", e);

                    var errorMessage =
                        $"Unable to analyze {plugin.DllFilename} because of {e.GetType().FullName}: {e.Message}";
                    _applicationService.AddApplicationOperationError(errorMessage + " - see plugin log for details");
                }

                if (plugin.PresetParser != null && plugin.PresetParser.Logger.HasLoggedEntries(LogLevel.Error))
                {
                    var errors = plugin.PresetParser.Logger.GetFilteredLogEntries(new List<LogLevel> {LogLevel.Error});

                    foreach (var error in errors)
                    {
                        _applicationService.AddApplicationOperationError(error.Message);
                    }
                }

                // Remove the event handler here, so we can be absolutely sure we removed this.
                _presetDataPersisterService.PresetUpdated -= ContextOnPresetUpdated;

                LogTo.Debug($"End analysis of {plugin.DllFilename}");
            }
        }
    }
}