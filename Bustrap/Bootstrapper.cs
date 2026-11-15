// To debug the automatic updater:
// - Uncomment the definition below
// - Publish the executable
// - Launch the executable (click no when it asks you to upgrade)
// - Launch Roblox (for testing web launches, run it from the command prompt)
// - To re-test the same executable, delete it from the installation folder

// #define DEBUG_UPDATER

#if DEBUG_UPDATER
#warning "Automatic updater debugging is enabled"
#endif

using ICSharpCode.SharpZipLib.Zip;
using Microsoft.VisualBasic.Devices;
using Strings = Bustrap.Resources.Strings;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Shell;
using System.Windows.Threading;
using Bustrap.AppData;
using Bustrap.Integrations;
using Bustrap.RobloxInterfaces;
using Bustrap.UI.Elements.Bootstrapper.Base;
using Bustrap.UI.ViewModels.Settings;
using static Bustrap.UI.ViewModels.Settings.ModsViewModel;

namespace Bustrap
{
    public class Bootstrapper
    {
        #region Constants

        private const int ProgressBarMaximum = 10000;
        private const double TaskbarProgressMaximumWpf = 1.0;
        private const int TaskbarProgressMaximumWinForms = WinFormsDialogBase.TaskbarProgressMaximum;

        private const string ProcRobloxPlayer = "RobloxPlayerBeta";
        private const string ProcRobloxCrash = "RobloxCrashHandler";
        private const string ProcRobloxStudio = "RobloxStudioBeta";
        private const string ProcEuroTrucks = "eurotrucks2.exe";
        private const string ProcRobloxExe = "RobloxPlayerBeta.exe";

        private const string SkyboxZipUrl = "https://github.com/KloBraticc/SkyboxPackV2/archive/refs/heads/main.zip";
        private const string SkyboxCommitApiUrl = "https://api.github.com/repos/KloBraticc/SkyboxPackV2/commits/main";
        private const string SkyboxVersionFile = "skybox.commit";

        // this isnt very needed but just incase someone edits the file it wouldnt get a error on startup for roblox
        private const string AppSettingsXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
            "<Settings>\r\n" +
            "	<ContentFolder>content</ContentFolder>\r\n" +
            "	<BaseUrl>http://www.roblox.com</BaseUrl>\r\n" +
            "</Settings>\r\n";

        private const float CpuHighThreshold = 80f;
        private const int MinWorkingSetMB = 50;
        private const int OptimizerIntervalMs = 3000;

        #endregion

        #region Native Methods

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr handle, IntPtr min, IntPtr max);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetPriorityClass(IntPtr handle, uint priorityClass);

        private const uint PROCESS_MODE_BACKGROUND_BEGIN = 0x00100000;
        private const uint PROCESS_MODE_BACKGROUND_END = 0x00200000;

        #endregion

        #region Fields

        private readonly FastZipEvents _fastZipEvents = new();
        private readonly CancellationTokenSource _cancelTokenSource = new();
        private readonly IAppData AppData;
        private readonly LaunchMode _launchMode;

        private string _launchCommandLine = App.LaunchSettings.RobloxLaunchArgs;
        private string _latestVersionGuid = null!;
        private string _latestVersionDirectory = null!;
        private PackageManifest _versionPackageManifest = null!;

        private int _isInstalling = 0;
        private long _totalDownloadedBytes = 0;
        private double _progressIncrement;
        private double _taskbarProgressIncrement;
        private double _taskbarProgressMaximum;

        private Process? _robloxProcess;
        private CancellationTokenSource? _optimizationCts;
        private CancellationTokenSource? _cpuWatcherCts;
        private AsyncMutex? _mutex;
        private int _appPid = 0;
        private bool _noConnection = false;

        private static readonly string PackFolder =
            Path.Combine(Paths.Base, "SkyboxPack");

        private static readonly HttpClient SkyboxHttpClient = new()
        {
            Timeout = TimeSpan.FromMinutes(30)
        };

        private static readonly Dictionary<string, string> SkyboxPatchFolderMap = new()
        {
            { "a564ec8aeef3614e788d02f0090089d8", "a5" },
            { "7328622d2d509b95dd4dd2c721d1ca8b", "73" },
            { "a50f6563c50ca4d5dcb255ee5cfab097", "a5" },
            { "6c94b9385e52d221f0538aadaceead2d", "6c" },
            { "9244e00ff9fd6cee0bb40a262bb35d31", "92" },
            { "78cb2e93aee0cdbd79b15a866bc93a54", "78" },
        };

        public IBootstrapperDialog? Dialog = null;
        public bool IsStudioLaunch => _launchMode != LaunchMode.Player;
        private bool _mustUpgrade => string.IsNullOrEmpty(AppData.State.VersionGuid)
                                       || !ClientExecutableExists();

        /// <summary>
        /// The player executable is renamed back and forth by the "rename to
        /// eurotrucks2" option, and that rename only happens while applying
        /// modifications. Checking for the configured name alone meant that
        /// toggling the option made the client look uninstalled and forced a
        /// full reinstall, so either name counts as installed.
        /// </summary>
        private bool ClientExecutableExists()
        {
            if (File.Exists(AppData.ExecutablePath))
                return true;

            if (IsStudioLaunch)
                return false;

            return File.Exists(Path.Combine(AppData.Directory, ProcRobloxExe))
                || File.Exists(Path.Combine(AppData.Directory, ProcEuroTrucks));
        }

        #endregion

        #region Constructor

        public Bootstrapper(LaunchMode launchMode)
        {
            _launchMode = launchMode;

            _fastZipEvents.FileFailure += (_, e) => throw e.Exception;
            _fastZipEvents.DirectoryFailure += (_, e) => throw e.Exception;
            _fastZipEvents.ProcessFile += (_, e) => e.ContinueRunning = !_cancelTokenSource.IsCancellationRequested;

            AppData = IsStudioLaunch ? new RobloxStudioData() : new RobloxPlayerData();
            Deployment.BinaryType = AppData.BinaryType;
        }

        #endregion

        #region Dialog Helpers

        private void InvokeOnDialog(Action action)
        {
            if (Dialog is null) return;

            if (Dialog is System.Windows.Forms.Control c)
            {
                if (c.InvokeRequired) c.Invoke(action); else action();
            }
            else if (Dialog is DependencyObject d)
            {
                if (!d.Dispatcher.CheckAccess()) d.Dispatcher.Invoke(action); else action();
            }
            else
            {
                action();
            }
        }

        private void SetStatus(string message) => InvokeOnDialog(() => Dialog!.Message = message);
        private void SetProgressValue(int value) => InvokeOnDialog(() => Dialog!.ProgressValue = value);
        private void SetProgressMaximum(int max) => InvokeOnDialog(() => Dialog!.ProgressMaximum = max);
        private void SetProgressStyle(ProgressBarStyle style) => InvokeOnDialog(() => Dialog!.ProgressStyle = style);

        private void UpdateProgressBar()
        {
            if (Dialog is null) return;

            InvokeOnDialog(() =>
            {
                long bytes = Interlocked.Read(ref _totalDownloadedBytes);

                int bar = (int)Math.Clamp(Math.Floor(_progressIncrement * bytes), 0, ProgressBarMaximum);
                Dialog.ProgressValue = bar;

                double tb = Math.Clamp(_taskbarProgressIncrement * bytes, 0, App.TaskbarProgressMaximum);
                Dialog.TaskbarProgressValue = tb;
            });
        }

        #endregion

        #region Core

        public async Task Run()
        {
            const string LOG_IDENT = "Bootstrapper::Run";
            App.Logger.WriteLine(LOG_IDENT, "Running bootstrapper");

            if (Dialog is not null) Dialog.CancelEnabled = true;

            SetStatus(Strings.Bootstrapper_Status_Connecting);

            var connectionResult = await Deployment.InitializeConnectivity();
            App.Logger.WriteLine(LOG_IDENT, "Connectivity check finished");

            if (connectionResult is not null)
                await HandleConnectionError(connectionResult);

#if (!DEBUG || DEBUG_UPDATER) && !QA_BUILD
            if (App.Settings.Prop.CheckForUpdates && !App.LaunchSettings.UpgradeFlag.Active)
                await CheckAndApplyUpdate(LOG_IDENT);
#endif

            bool mutexExists = false;
            try
            {
                using (Mutex.OpenExisting("Bustrap-Bootstrapper"))
                {
                    App.Logger.WriteLine(LOG_IDENT, "Bustrap-Bootstrapper mutex exists, waiting...");
                    SetStatus(Strings.Bootstrapper_Status_WaitingOtherInstances);
                    mutexExists = true;
                }
            }
            catch (WaitHandleCannotBeOpenedException) { }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Unexpected error checking mutex: {ex}");
            }

            await using var mutex = new AsyncMutex(false, "Bustrap-Bootstrapper");
            await mutex.AcquireAsync(_cancelTokenSource.Token);
            _mutex = mutex;

            if (mutexExists)
            {
                App.Settings.Load();
                App.State.Load();
            }

            if (!_noConnection)
            {
                try { await GetLatestVersionInfo(); }
                catch (Exception ex) { await HandleConnectionError(ex); }
            }

            if (!_noConnection)
            {
                bool upToDate = true;

                if (AppData.State.VersionGuid != _latestVersionGuid || _mustUpgrade)
                    upToDate = await UpgradeRoblox();

                if (_cancelTokenSource.IsCancellationRequested) return;

                // A failed upgrade leaves _latestVersionDirectory half-extracted.
                // Copying mods into it would only dress up a broken install, so
                // fall through and launch whatever client is still on disk.
                if (upToDate)
                {
                    await ApplyModifications();
                    ApplyLockDefaultIfNeeded(LOG_IDENT);
                }
            }

            if (IsStudioLaunch) WindowsRegistry.RegisterStudio();
            else WindowsRegistry.RegisterPlayer();

            await mutex.ReleaseAsync();

            // Whatever left us without a client (a failed first install, no
            // connection) has already been reported. Trying to start an
            // executable that is not there only stacked a second, vaguer error
            // dialog on top of the real one.
            bool clientPresent = ClientExecutableExists();
            if (!clientPresent)
                App.Logger.WriteLine(LOG_IDENT, "No Roblox client is installed, nothing to launch.");

            if (clientPresent && !App.LaunchSettings.NoLaunchFlag.Active && !_cancelTokenSource.IsCancellationRequested)
                await StartRoblox();

            Dialog?.CloseBootstrapper();
        }

        private async Task CheckAndApplyUpdate(string logIdent)
        {
            var latestTag = await GithubUpdater.GetLatestVersionTagAsync();
            if (string.IsNullOrEmpty(latestTag)) return;

            string normalizedTag = latestTag.TrimStart('v', 'V');
            string localVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
            App.Version = localVersion;

            App.Logger.WriteLine(logIdent, $"Local: {localVersion} | Remote: {normalizedTag}");

            if (!IsNewerVersion(normalizedTag)) return;

            SetStatus($"Updating to v{normalizedTag}...");

            bool ok = await GithubUpdater.DownloadAndInstallUpdate(latestTag);
            if (ok)
            {
                App.Logger.WriteLine(logIdent, "Update installed restarting Bustrap...");
                RestartApplication();
            }
            else
            {
                App.Logger.WriteLine(logIdent, "Update failed continuing without updating.");
            }
        }

        private static bool IsNewerVersion(string remoteTag)
        {
            if (!App.Settings.Prop.CheckForUpdates) return false;

            string local = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

            return VersionComparer.IsNewer(remoteTag, local);
        }

        private static void RestartApplication()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath!,
                    UseShellExecute = false
                };

                // Relaunch with what we were launched with. Restarting bare
                // dropped the roblox-player: link, so clicking Play on the
                // website during an update opened the menu instead of the game.
                foreach (string arg in App.LaunchSettings.Args)
                    startInfo.ArgumentList.Add(arg);

                // -upgrade makes the relaunched copy skip the update check. Now
                // that it comes back through the bootstrapper, a release whose
                // tag and binary version disagree would otherwise update,
                // restart and update again forever.
                if (!App.LaunchSettings.UpgradeFlag.Active)
                    startInfo.ArgumentList.Add("-upgrade");

                Process.Start(startInfo);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("Bootstrapper::Restart", $"Failed to restart: {ex}");
            }
        }

        private async Task HandleConnectionError(Exception exception)
        {
            const string LOG_IDENT = "Bootstrapper::HandleConnectionError";
            if (exception is null) return;

            if (exception is AggregateException agg)
                exception = agg.InnerException ?? agg;

            App.Logger.WriteException(LOG_IDENT, exception);

            if (Interlocked.Read(ref _totalDownloadedBytes) > 0 &&
                Volatile.Read(ref _isInstalling) == 1)
            {
                App.Logger.WriteLine(LOG_IDENT, "Already upgrading — skipping retry.");
                return;
            }

            // This used to return early on a 403 after resetting the channel,
            // leaving _noConnection false with no version resolved - the caller
            // then tried to install into a null directory. Channel fallback is
            // handled inside GetLatestVersionInfo; anything that still reaches
            // here means there is no version to work with.
            _noConnection = true;
            Frontend.ShowMessageBox(
                "A network or server issue occurred. Try switching your channel in Settings or relaunching.",
                MessageBoxImage.Warning, MessageBoxButton.OK);
        }

        private void ApplyLockDefaultIfNeeded(string logIdent)
        {
            if (!App.Settings.Prop.LockDefault) return;

            var allowedFlags = new Dictionary<string, string>
            {
                { "FFlagHandleAltEnterFullscreenManually", "False" }
            };

            try
            {
                string path = Path.Combine(_latestVersionDirectory,
                    "ClientSettings", "ClientAppSettings.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(allowedFlags,
                    new JsonSerializerOptions { WriteIndented = true }));
                App.Logger.WriteLine(logIdent, "LockDefault: ClientAppSettings.json enforced.");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(logIdent, "LockDefault enforcement failed: " + ex.Message);
            }
        }

        public void Cancel()
        {
            const string LOG_IDENT = "Bootstrapper::Cancel";

            if (_cancelTokenSource.IsCancellationRequested) return;

            App.Logger.WriteLine(LOG_IDENT, "Cancelling launch...");
            _cancelTokenSource.Cancel();

            if (Dialog is not null) Dialog.CancelEnabled = false;

            if (Volatile.Read(ref _isInstalling) == 1)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(_latestVersionDirectory) &&
                        Directory.Exists(_latestVersionDirectory))
                        Directory.Delete(_latestVersionDirectory, true);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Could not fully clean up installation!");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }
            else if (_appPid != 0)
            {
                try { Process.GetProcessById(_appPid).Kill(); }
                catch (Exception error)
                {
                    App.Logger.WriteException("Bootstrapper::Cancel", error);
                }
            }

            Dialog?.CloseBootstrapper();
            App.SoftTerminate(ErrorCode.ERROR_CANCELLED);
        }

        #endregion

        #region Version Info

        private async Task GetLatestVersionInfo()
        {
            const string LOG_IDENT = "Bootstrapper::GetLatestVersionInfo";

            if (!App.Settings.Prop.UpdateRoblox && !_mustUpgrade)
            {
                // Updates are turned off and a client is installed, so the
                // installed version IS the target. Resolving the newest version
                // here pointed everything downstream at a folder that was never
                // installed: mods and fast flags were copied there, and the
                // client that actually launched never saw them.
                App.Logger.WriteLine(LOG_IDENT, "Roblox updates are disabled, staying on the installed version.");
                _latestVersionGuid = AppData.State.VersionGuid;
            }
            else
            {
                ClientVersion? clientVersion = null;
                var infoUrl = Deployment.GetInfoUrl(Deployment.Channel);

                using (var response = await App.HttpClient.GetAsync(
                    infoUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"HTTP {(int)response.StatusCode}");

                        // 401 and 404 mean the channel is private or gone just
                        // as much as 403 does; all three fall back to production
                        // instead of being reported as a network failure.
                        if (!Deployment.IsDefaultChannel && Deployment.BadChannelCodes.Contains(response.StatusCode))
                        {
                            App.Logger.WriteLine(LOG_IDENT, $"Channel {Deployment.Channel} is unavailable, switching to the default channel.");
                            Deployment.Channel = Deployment.DefaultChannel;
                            clientVersion = await Deployment.GetInfo(Deployment.Channel).ConfigureAwait(false);
                        }
                        else
                        {
                            throw new HttpRequestException(
                                $"Bad status: {response.StatusCode}", null, response.StatusCode);
                        }
                    }
                    else
                    {
                        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                        if (!mediaType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
                            throw new Exception("Invalid response content-type from version endpoint.");

                        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        clientVersion = JsonSerializer.Deserialize<ClientVersion>(json)
                            ?? throw new Exception("ClientVersion JSON deserialized to null.");
                    }
                }

                if (string.IsNullOrWhiteSpace(clientVersion?.VersionGuid))
                    throw new Exception("VersionGuid missing from clientVersion response.");

                _latestVersionGuid = clientVersion.VersionGuid;
            }

            _latestVersionDirectory = Path.Combine(Paths.Versions, _latestVersionGuid);

            string manifestUrl = $"https://setup.rbxcdn.com/{_latestVersionGuid}-rbxPkgManifest.txt";
            App.Logger.WriteLine(LOG_IDENT, $"Fetching manifest: {manifestUrl}");

            using var manifestResp = await App.HttpClient.GetAsync(
                manifestUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

            if (!manifestResp.IsSuccessStatusCode)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Manifest HTTP {(int)manifestResp.StatusCode} — empty manifest.");
                _versionPackageManifest = new("");
                return;
            }

            var manifestText = await manifestResp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(manifestText) || manifestText.TrimStart().StartsWith("<"))
            {
                App.Logger.WriteLine(LOG_IDENT, "Manifest returned HTML/empty — skipping parse.");
                _versionPackageManifest = new("");
                return;
            }

            _versionPackageManifest = new(manifestText);
            App.Logger.WriteLine(LOG_IDENT, $"Manifest: {_versionPackageManifest.Count} entries.");
        }

        #endregion

        #region Roblox Launch

        private async Task StartRoblox(CancellationToken ct = default)
        {
            const string LOG_IDENT = "Bootstrapper::StartRoblox";
            SetStatus("Starting Roblox");

            try
            {
                await Task.Run(LaunchFleasionIfEnabled, ct);
                HandleFullBright();
                NormalizeRobloxLocale();

                var startInfo = BuildStartInfo();

                if (_launchMode == LaunchMode.StudioAuth)
                {
                    Process.Start(startInfo);
                    return;
                }

                string rbxLogDir = Path.Combine(Paths.LocalAppData, "Roblox", "logs");
                Directory.CreateDirectory(rbxLogDir);

                string? logFileName = await WaitForLogFileAsync(rbxLogDir, startInfo, ct);

                if (string.IsNullOrEmpty(logFileName))
                {
                    App.Logger.WriteLine(LOG_IDENT, "Unable to identify log file.");
                    Frontend.ShowPlayerErrorDialog();
                    return;
                }

                App.Logger.WriteLine(LOG_IDENT, $"Log file: {logFileName}");
                _ = _mutex?.ReleaseAsync();

                if (IsStudioLaunch) return;

                var autoclosePids = LaunchCustomIntegrations(LOG_IDENT);
                await DisableCrashHandlerIfNeeded(LOG_IDENT, ct);
                LaunchWatcherIfNeeded(logFileName, autoclosePids);

                await Task.Delay(2500, ct).ConfigureAwait(false);

                if (_robloxProcess is not null)
                {
                    _robloxProcess.EnableRaisingEvents = true;
                    _robloxProcess.Exited += (_, __) => StopOptimizer();
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Unexpected error in StartRoblox: {ex}");
                Frontend.ShowPlayerErrorDialog();
            }
        }

        private void NormalizeRobloxLocale()
        {
            if (_launchMode != LaunchMode.Player || App.Settings.Prop?.ForceRobloxLanguage != true)
                return;

            var match = Regex.Match(
                _launchCommandLine ?? string.Empty,
                @"gameLocale:([a-zA-Z_-]+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (match.Success)
            {
                _launchCommandLine = Regex.Replace(
                    _launchCommandLine ?? string.Empty,
                    @"robloxLocale:[a-zA-Z_-]+",
                    $"robloxLocale:{match.Groups[1].Value}",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
        }

        private ProcessStartInfo BuildStartInfo()
        {
            var si = new ProcessStartInfo
            {
                FileName = AppData.ExecutablePath,
                Arguments = _launchCommandLine ?? string.Empty,
                WorkingDirectory = AppData.Directory,
                UseShellExecute = false
            };

            if (_launchMode == LaunchMode.Player && ShouldRunAsAdmin())
            {
                si.Verb = "runas";
                si.UseShellExecute = true;
            }

            return si;
        }

        private async Task<string?> WaitForLogFileAsync(
            string rbxLogDir,
            ProcessStartInfo startInfo,
            CancellationToken ct)
        {
            const string LOG_IDENT = "Bootstrapper::WaitForLogFile";

            using var logEvent = new AutoResetEvent(false);
            using var watcherCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            string? logFileName = null;

            using var watcher = new FileSystemWatcher(rbxLogDir, "*.log")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            watcher.Created += (_, e) => Task.Run(async () =>
            {
                for (int i = 0; i < 10; i++)
                {
                    if (File.Exists(e.FullPath)) { logFileName = e.FullPath; logEvent.Set(); return; }
                    try { await Task.Delay(100, watcherCts.Token); } catch { return; }
                }
            });

            watcher.Renamed += (_, e) =>
            {
                if (e.FullPath.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                { logFileName = e.FullPath; logEvent.Set(); }
            };

            watcher.Error += (_, e) =>
            {
                App.Logger.WriteLine(LOG_IDENT, $"Watcher error: {e.GetException().Message}");
                logEvent.Set();
            };

            try
            {
                _robloxProcess = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Failed to start Roblox process.");

                _appPid = _robloxProcess.Id;

                _ = Task.Run(() => TryApplyPriorityAsync(_robloxProcess, LOG_IDENT, ct), ct);

                try { _robloxProcess.WaitForInputIdle(1000); } catch (Exception exception)
                {
                    App.Logger.WriteException("Bootstrapper::WaitForLogFileAsync", exception);
                }

                StartCpuLimitWatcherIfNeeded();
                StartOptimizerIfNeeded();

                if (App.Settings.Prop?.MultiAccount == true)
                    RobloxMemoryCleaner.CleanAllRobloxMemory();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return null;
            }

            await Task.Run(() => logEvent.WaitOne(TimeSpan.FromSeconds(35)), ct);
            watcherCts.Cancel();

            if (string.IsNullOrEmpty(logFileName))
            {
                try
                {
                    logFileName = Directory
                        .EnumerateFiles(rbxLogDir, "*.log")
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Log enumeration failed: {ex.Message}");
                }
            }

            return logFileName;
        }

        /// <returns>
        /// PIDs of the integrations that asked to be closed along with Roblox.
        /// These have to reach the watcher: this process exits a few seconds
        /// after launch, so it cannot do the closing itself.
        /// </returns>
        private List<int> LaunchCustomIntegrations(string logIdent)
        {
            var autoclosePids = new List<int>();
            foreach (var integration in (App.Settings.Prop?.CustomIntegrations ?? Enumerable.Empty<CustomIntegration>())
                     .Where(i => !i.SpecifyGame))
            {
                if (string.IsNullOrWhiteSpace(integration.Location) ||
                    !File.Exists(integration.Location))
                {
                    App.Logger.WriteLine(logIdent, $"Integration missing: {integration.Name}");
                    continue;
                }

                try
                {
                    var ip = Process.Start(new ProcessStartInfo
                    {
                        FileName = integration.Location,
                        Arguments = (integration.LaunchArgs ?? "").Replace("\r\n", " "),
                        WorkingDirectory = Path.GetDirectoryName(integration.Location)!,
                        UseShellExecute = true
                    });

                    if (ip != null && integration.AutoClose)
                        autoclosePids.Add(ip.Id);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(logIdent, $"Failed to launch '{integration.Name}': {ex.Message}");
                }
            }

            return autoclosePids;
        }

        private async Task DisableCrashHandlerIfNeeded(string logIdent, CancellationToken ct)
        {
            if (App.Settings.Prop.DisableCrash != true) return;

            await Task.Delay(800, ct).ConfigureAwait(false);

            foreach (var handler in Process.GetProcessesByName(ProcRobloxCrash))
            {
                try
                {
                    if (handler.HasExited) continue;
                    handler.CloseMainWindow();
                    if (!handler.WaitForExit(1000))
                        handler.Kill(entireProcessTree: true);
                    App.Logger.WriteLine(logIdent, $"CrashHandler {handler.Id} terminated.");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(logIdent, $"CrashHandler kill error {handler.Id}: {ex.Message}");
                }
                finally { handler.Dispose(); }
            }
        }

        private void LaunchWatcherIfNeeded(string logFileName, List<int> autoclosePids)
        {
            // The watcher is the only part of Bustrap that stays alive for the
            // whole Roblox session, so anything that has to act later - closing
            // integrations when Roblox exits, cleaning memory on an interval -
            // needs it running, whether or not activity tracking is on.
            bool needWatcher = (App.Settings?.Prop.EnableActivityTracking ?? false)
                || App.LaunchSettings.TestModeFlag?.Active == true
                || autoclosePids.Count > 0
                || BustrapRobloxSettingsManager.Load().MemoryCleanerIntervalSeconds > 0;

            if (!needWatcher) return;

            using var ipl = new InterProcessLock("Watcher", TimeSpan.FromSeconds(5));

            var watcherData = new WatcherData
            {
                ProcessId = _appPid,
                LogFile = logFileName,
                AutoclosePids = autoclosePids
            };

            string b64 = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(watcherData)));

            var args = new StringBuilder($"-watcher \"{b64}\"");
            if (App.LaunchSettings.TestModeFlag?.Active == true) args.Append(" -testmode");

            if (ipl.IsAcquired)
                Process.Start(Paths.Process, args.ToString());
        }

        private static void LaunchFleasionIfEnabled()
        {
            try
            {
                if (!App.Settings.Prop.Fleasion) return;

                var existing = Process.GetProcessesByName("Fleasion")
                    .FirstOrDefault(p => !p.HasExited);

                if (existing != null) { WaitForFleasionAdmin(existing); return; }

                string path = Path.Combine(Paths.Base, "Fleasion", "Fleasion.exe");
                if (!File.Exists(path)) return;

                Process? proc = null;
                try
                {
                    proc = Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true,
                        Verb = "runas"
                    });
                }
                catch (Win32Exception ex)
                {
                    App.Logger.WriteLine("LaunchHandler", $"Fleasion launch failed: {ex.Message}");
                    return;
                }

                if (proc != null) WaitForFleasionAdmin(proc);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("LaunchHandler::LaunchFleasionIfEnabled", ex.ToString());
            }
        }

        private static void WaitForFleasionAdmin(Process process)
        {
            const int TimeoutMs = 10_000;
            const int Interval = 200;
            int waited = 0;

            while (waited < TimeoutMs)
            {
                process.Refresh();
                if (process.MainWindowHandle != IntPtr.Zero && IsProcessElevated(process))
                {
                    App.Logger.WriteLine("Fleasion", "Running as admin.");
                    return;
                }
                Thread.Sleep(Interval);
                waited += Interval;
            }

            App.Logger.WriteLine("Fleasion", "Timed out waiting for admin.");
        }

        private static bool IsProcessElevated(Process process)
        {
            try
            {
                var wi = new WindowsIdentity(process.Handle);
                return new WindowsPrincipal(wi).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private bool ShouldRunAsAdmin()
        {
            foreach (var root in WindowsRegistry.Roots)
            {
                using var key = root.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers");
                if (key is null) continue;

                string? flags = (string?)key.GetValue(AppData.ExecutablePath);
                if (flags?.Contains("RUNASADMIN", StringComparison.OrdinalIgnoreCase) == true)
                    return true;
            }
            return false;
        }

        #endregion

        #region Memory / Process Optimization

        private void StartOptimizerIfNeeded()
        {
            if (App.Settings.Prop?.OptimizeRoblox != true &&
                App.Settings.Prop?.IsBetterServersEnabled != true) return;

            _optimizationCts?.Cancel();
            _optimizationCts = new CancellationTokenSource();
            Task.Run(() => OptimizerLoop(_optimizationCts.Token));
        }

        private void StopOptimizer()
        {
            _optimizationCts?.Cancel();
            _optimizationCts = null;
        }

        private async Task OptimizerLoop(CancellationToken token)
        {
            var processNames = new[] { "Roblox", ProcRobloxPlayer, "Roblox Game Client" };
            var optimizedPids = new HashSet<int>();
            var cpuCounters = new Dictionary<int, PerformanceCounter>();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var robloxProcesses = processNames
                        .SelectMany(n => Process.GetProcessesByName(n))
                        .Where(p => !p.HasExited)
                        .GroupBy(p => p.Id)
                        .Select(g => g.First())
                        .ToList();

                    if (robloxProcesses.Count == 0)
                    {
                        await Task.Delay(5000, token);
                        continue;
                    }

                    foreach (var proc in robloxProcesses)
                    {
                        try
                        {
                            if (!optimizedPids.Contains(proc.Id))
                            {
                                ApplyRuntimeOptimizations(proc);
                                optimizedPids.Add(proc.Id);
                            }

                            SetProcessWorkingSetSize(proc.Handle, (IntPtr)(-1), (IntPtr)(-1));
                            await MonitorProcessCpu(proc, cpuCounters, token);
                        }
                        catch (Exception ex) when (!token.IsCancellationRequested)
                        {
                            App.Logger.WriteLine("Optimizer", $"[PID {proc.Id}] {ex.Message}");
                        }
                    }

                    optimizedPids.RemoveWhere(pid => robloxProcesses.All(p => p.Id != pid));
                    foreach (var pid in cpuCounters.Keys.Except(
                        robloxProcesses.Select(p => p.Id)).ToList())
                    {
                        cpuCounters[pid].Dispose();
                        cpuCounters.Remove(pid);
                    }

                    await Task.Delay(OptimizerIntervalMs, token);
                }
                catch (TaskCanceledException) { break; }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("Optimizer", $"[Loop] {ex.Message}");
                }
            }

            foreach (var c in cpuCounters.Values) c.Dispose();
            App.Logger.WriteLine("Optimizer", "Stopped.");
        }

        private static void ApplyRuntimeOptimizations(Process process)
        {
            void Safe(Action a, string desc)
            {
                try { a(); }
                catch (Exception ex)
                { App.Logger.WriteLine("Optimizer", $"Failed to {desc}: {ex.Message}"); }
            }

            int cores = Environment.ProcessorCount;
            long affinityMask = CpuCoreLimiter.AffinityMask(cores);

            Safe(() => process.ProcessorAffinity = (IntPtr)affinityMask, "set CPU affinity");

            ulong totalMem = new ComputerInfo().TotalPhysicalMemory;
            long maxWS = totalMem switch
            {
                var t when t > 64UL * 1024 * 1024 * 1024 => 32L * 1024 * 1024 * 1024,
                var t when t > 32UL * 1024 * 1024 * 1024 => 16L * 1024 * 1024 * 1024,
                var t when t > 16UL * 1024 * 1024 * 1024 => 8L * 1024 * 1024 * 1024,
                var t when t > 8UL * 1024 * 1024 * 1024 => 4L * 1024 * 1024 * 1024,
                _ => 2L * 1024 * 1024 * 1024
            };

            Safe(() =>
            {
                process.MaxWorkingSet = new IntPtr(Math.Min(maxWS, (long)IntPtr.MaxValue));
                process.MinWorkingSet = new IntPtr(MinWorkingSetMB * 1024 * 1024);
            }, "set working set");

            App.Logger.WriteLine("Optimizer",
                $"Applied to {process.ProcessName} (PID {process.Id})");
        }

        private static async Task MonitorProcessCpu(
            Process process,
            Dictionary<int, PerformanceCounter> cpuCounters,
            CancellationToken token)
        {
            try
            {
                process.Refresh();

                if (!cpuCounters.ContainsKey(process.Id))
                {
                    var ctr = new PerformanceCounter(
                        "Process", "% Processor Time", process.ProcessName, true);
                    ctr.NextValue();
                    cpuCounters[process.Id] = ctr;
                }

                await Task.Delay(300, token);

                float cpu = cpuCounters[process.Id].NextValue() / Environment.ProcessorCount;
                if (cpu > CpuHighThreshold)
                    process.PriorityClass = ProcessPriorityClass.AboveNormal;

                try { process.PriorityBoostEnabled = true; } catch (Exception error)
                {
                    App.Logger.WriteException("Bootstrapper::MonitorProcessCpu", error);
                }
                SetPriorityClass(process.Handle, PROCESS_MODE_BACKGROUND_END);
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                App.Logger.WriteLine("Optimizer", $"[CPU monitor PID {process.Id}] {ex.Message}");
            }
        }

        private void StartCpuLimitWatcherIfNeeded()
        {
            var priority = App.Settings.Prop?.SelectedCpuPriority;
            if (string.IsNullOrEmpty(priority) ||
                priority.Equals("Automatic", StringComparison.OrdinalIgnoreCase)) return;

            _cpuWatcherCts?.Cancel();
            _cpuWatcherCts = new CancellationTokenSource();
            var token = _cpuWatcherCts.Token;

            Task.Run(async () =>
            {
                var seenPids = new HashSet<int>();

                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        var parts = App.Settings.Prop.SelectedCpuPriority.Split(' ');
                        if (int.TryParse(parts[0], out int coreCount) && coreCount > 0)
                        {
                            int total = Environment.ProcessorCount;
                            coreCount = Math.Clamp(coreCount, 1, total);
                            long mask = CpuCoreLimiter.AffinityMask(coreCount);

                            foreach (var proc in Process.GetProcessesByName(ProcRobloxPlayer))
                            {
                                try
                                {
                                    if (!seenPids.Contains(proc.Id) && !proc.HasExited)
                                    {
                                        proc.ProcessorAffinity = (IntPtr)mask;
                                        App.Logger.WriteLine("CPUWatcher",
                                            $"PID {proc.Id}: {coreCount}/{total} cores");
                                        seenPids.Add(proc.Id);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    App.Logger.WriteLine("CPUWatcher",
                                        $"PID {proc.Id} failed: {ex.Message}");
                                }
                                finally { proc.Dispose(); }
                            }

                            seenPids.RemoveWhere(pid =>
                            {
                                try { Process.GetProcessById(pid); return false; }
                                catch { return true; }
                            });
                        }

                        await Task.Delay(5000, token);
                    }
                }
                catch (TaskCanceledException) { }
                finally { _cpuWatcherCts?.Dispose(); _cpuWatcherCts = null; }
            }, token);
        }

        private static async Task TryApplyPriorityAsync(
            Process proc, string logIdent, CancellationToken ct)
        {
            try
            {
                await Task.Delay(1100, ct).ConfigureAwait(false);
                proc.Refresh();
                if (proc.HasExited) return;

                string pname = App.Settings.Prop?.PriorityLimit ?? "Normal";
                var newPriority = pname switch
                {
                    "Realtime" => ProcessPriorityClass.RealTime,
                    "High" => ProcessPriorityClass.High,
                    "Above Normal" => ProcessPriorityClass.AboveNormal,
                    "Below Normal" => ProcessPriorityClass.BelowNormal,
                    "Low" => ProcessPriorityClass.Idle,
                    _ => ProcessPriorityClass.Normal
                };

                proc.PriorityClass = newPriority;
                App.Logger.WriteLine(logIdent, $"Priority set: {pname}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(logIdent, $"Priority worker: {ex.Message}");
            }
        }

        #endregion

        #region FullBright

        private void HandleFullBright()
        {
            const string LOG_IDENT = "FullBright";

            try
            {
                if (!Directory.Exists(Paths.Versions)) return;

                string backupDir = Path.Combine(Paths.Base, "FullBrightBackup");
                string metaPath = Path.Combine(backupDir, "brdf.json");
                Directory.CreateDirectory(backupDir);

                if (App.Settings.Prop?.Fullbright == false)
                {
                    RestoreFullBright(backupDir, metaPath, LOG_IDENT);
                    return;
                }

                if (App.Settings.Prop?.Fullbright != true) return;

                foreach (var versionDir in Directory.GetDirectories(Paths.Versions, "version-*"))
                {
                    var brdf = Directory
                        .EnumerateFiles(
                            Path.Combine(versionDir, "PlatformContent", "pc", "textures"),
                            "brdfLUT.*",
                            SearchOption.AllDirectories)
                        .FirstOrDefault();

                    if (brdf == null || !File.Exists(brdf)) continue;

                    string relPath = Path.GetRelativePath(Paths.Versions, Path.GetDirectoryName(brdf)!);
                    string fileName = Path.GetFileName(brdf);
                    string backup = Path.Combine(backupDir, fileName);

                    if (!File.Exists(backup))
                    {
                        File.Copy(brdf, backup);
                        File.WriteAllText(metaPath, JsonSerializer.Serialize(
                            new BrdfBackupInfo { RelativePath = relPath, FileName = fileName }));
                        App.Logger.WriteLine(LOG_IDENT, $"Backed up {fileName}");
                    }

                    File.Delete(brdf);
                    App.Logger.WriteLine(LOG_IDENT, "brdfLUT removed (FullBright ON).");
                    break;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error: {ex}");
            }
        }

        private static void RestoreFullBright(string backupDir, string metaPath, string logIdent)
        {
            if (!File.Exists(metaPath)) return;

            var meta = JsonSerializer.Deserialize<BrdfBackupInfo>(File.ReadAllText(metaPath));
            if (meta is null) return;

            string restorePath = Path.Combine(Paths.Versions, meta.RelativePath, meta.FileName);
            string backupFile = Path.Combine(backupDir, meta.FileName);

            if (!File.Exists(backupFile)) return;

            Directory.CreateDirectory(Path.GetDirectoryName(restorePath)!);
            File.Copy(backupFile, restorePath, overwrite: true);
            App.Logger.WriteLine(logIdent, $"Restored {meta.FileName}");
        }

        private sealed class BrdfBackupInfo
        {
            public string RelativePath { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
        }

        #endregion

        #region Roblox Install / Upgrade

        private void MigrateCompatibilityFlags()
        {
            string oldLoc = Path.Combine(Paths.Versions, AppData.State.VersionGuid, AppData.ExecutableName);
            string newLoc = Path.Combine(_latestVersionDirectory, AppData.ExecutableName);

            using var key = Registry.CurrentUser.CreateSubKey(
                "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers");
            string? flags = key.GetValue(oldLoc) as string;

            if (flags is not null)
            {
                App.Logger.WriteLine("MigrateCompat", $"{oldLoc} -> {newLoc}");
                key.SetValueSafe(newLoc, flags);
                key.DeleteValueSafe(oldLoc);
            }
        }

        private static void KillRobloxPlayers()
        {
            const string LOG_IDENT = "KillRobloxPlayers";

            foreach (var proc in Process.GetProcessesByName(ProcRobloxPlayer)
                .Concat(Process.GetProcessesByName(ProcRobloxCrash)))
            {
                try { proc.Kill(); }
                catch (Exception ex)
                { App.Logger.WriteLine(LOG_IDENT, $"Kill {proc.Id} failed: {ex.Message}"); }
            }
        }

        private void CleanupVersionsFolder()
        {
            const string LOG_IDENT = "CleanupVersionsFolder";

            foreach (string dir in Directory.GetDirectories(Paths.Versions))
            {
                string name = Path.GetFileName(dir);
                if (name == App.State.Prop.Player.VersionGuid ||
                    name == App.State.Prop.Studio.VersionGuid) continue;

                try { Directory.Delete(dir, true); App.Logger.WriteLine(LOG_IDENT, $"Deleted: {dir}"); }
                catch (Exception ex)
                { App.Logger.WriteLine(LOG_IDENT, $"Failed to delete {dir}: {ex.Message}"); }
            }
        }

        private static async Task WithRetryAsync(
            Func<Task> action,
            string context,
            int maxAttempts = 5,
            int baseDelayMs = 750,
            Func<Exception, bool>? isTransient = null,
            CancellationToken ct = default)
        {
            isTransient ??= static ex =>
                ex is TaskCanceledException or HttpRequestException or IOException or SocketException;

            var rng = new Random();

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try { await action().ConfigureAwait(false); return; }
                catch (Exception ex) when (!ct.IsCancellationRequested && isTransient(ex) && attempt < maxAttempts)
                {
                    int delay = baseDelayMs * (1 << (attempt - 1));
                    int jitter = (int)(delay * (0.15 * (rng.NextDouble() * 2 - 1)));
                    delay = Math.Clamp(delay + jitter, 250, 10_000);

                    App.Logger.WriteLine(context,
                        $"Transient error ({attempt}/{maxAttempts}): {ex.Message}. Retrying in {delay}ms...");
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
            }
        }

        private static async Task SafeDeleteDirectoryAsync(
            string path, string context, CancellationToken ct)
        {
            if (!Directory.Exists(path)) return;

            await WithRetryAsync(
                async () =>
                {
                    foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                        try { File.SetAttributes(f, FileAttributes.Normal); } catch (Exception exception)
                        {
                            App.Logger.WriteException("Bootstrapper::SafeDeleteDirectoryAsync", exception);
                        }
                    Directory.Delete(path, recursive: true);
                    await Task.CompletedTask;
                },
                context,
                maxAttempts: 5,
                baseDelayMs: 600,
                isTransient: ex => ex is IOException or UnauthorizedAccessException,
                ct: ct).ConfigureAwait(false);
        }

        /// <returns>
        /// True when the client in <see cref="_latestVersionDirectory"/> is
        /// complete and ready for modifications; false when the upgrade was
        /// refused, cancelled or failed.
        /// </returns>
        private async Task<bool> UpgradeRoblox()
        {
            const string LOG_IDENT = "Bootstrapper::UpgradeRoblox";
            var ct = _cancelTokenSource.Token;

            if (Interlocked.Exchange(ref _isInstalling, 1) == 1)
            {
                App.Logger.WriteLine(LOG_IDENT, "Upgrade already in progress — skipping.");
                return false;
            }

            try
            {
                if (!App.Settings.Prop.UpdateRoblox)
                {
                    SetStatus(Strings.Bootstrapper_Status_CancelUpgrade);
                    await Task.Delay(250, ct).ConfigureAwait(false);

                    // only reachable with no client installed: when one is,
                    // GetLatestVersionInfo pins the installed version and no
                    // upgrade is attempted at all
                    Frontend.ShowMessageBox(
                        Strings.Bootstrapper_Dialog_NoUpgradeWithoutClient,
                        MessageBoxImage.Warning, MessageBoxButton.OK);
                    return false;
                }

                SetStatus(string.IsNullOrEmpty(AppData.State.VersionGuid)
                    ? "Installing Packages"
                    : "Upgrading Packages");

                Directory.CreateDirectory(Paths.Base);
                Directory.CreateDirectory(Paths.Downloads);
                Directory.CreateDirectory(Paths.Versions);

                var cachedHashes = Directory.Exists(Paths.Downloads)
                    ? Directory.GetFiles(Paths.Downloads).Select(Path.GetFileName).ToList()
                    : new List<string?>();

                if (!IsStudioLaunch)
                    await Task.Run(KillRobloxPlayers, ct).ConfigureAwait(false);

                if (Directory.Exists(_latestVersionDirectory))
                    await SafeDeleteDirectoryAsync(_latestVersionDirectory, LOG_IDENT, ct).ConfigureAwait(false);

                Directory.CreateDirectory(_latestVersionDirectory);

                if (_versionPackageManifest is null || !_versionPackageManifest.Any())
                    throw new Exception("Package manifest is null or empty.");

                long totalPacked = _versionPackageManifest.Sum(p => (long)p.PackedSize);
                long totalUnpacked = _versionPackageManifest.Sum(p => (long)p.Size);

                // Packages are cached under Base, the client is extracted under
                // Versions. A negative result means the drive could not be
                // identified (a UNC path, say) - that is "unknown", not "full",
                // and used to block the install with a bogus out-of-space error.
                long required = (long)((totalPacked + totalUnpacked) * 1.1);
                long free = Filesystem.GetFreeDiskSpace(Paths.Versions);
                if (free < 0)
                    free = Filesystem.GetFreeDiskSpace(Paths.Base);

                if (free >= 0 && free < required)
                {
                    Frontend.ShowMessageBox(Strings.Bootstrapper_NotEnoughSpace, MessageBoxImage.Error);
                    App.Terminate(ErrorCode.ERROR_INSTALL_FAILURE);
                    return false;
                }

                if (Dialog is not null)
                {
                    SetProgressStyle(ProgressBarStyle.Continuous);
                    Dialog.TaskbarProgressState = TaskbarItemProgressState.Normal;
                    SetProgressMaximum(ProgressBarMaximum);
                    _progressIncrement = (double)ProgressBarMaximum / Math.Max(1, totalPacked);
                    _taskbarProgressMaximum = Dialog is WinFormsDialogBase
                        ? TaskbarProgressMaximumWinForms : TaskbarProgressMaximumWpf;
                    _taskbarProgressIncrement = _taskbarProgressMaximum / Math.Max(1, totalPacked);
                }

                int totalPackages = _versionPackageManifest.Count;
                int packagesComplete = 0;
                Exception? firstFailure = null;
                string? failedPackage = null;
                var sw = Stopwatch.StartNew();

                Interlocked.Exchange(ref _totalDownloadedBytes, 0);

                // One failed package has to stop the others, but that is not the
                // user cancelling. Cancelling the bootstrapper's own token for it
                // made the failure surface as "The operation was canceled" and
                // then exit without a word; a linked source keeps the two apart.
                using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var batchToken = batchCts.Token;

                using var throttler = new SemaphoreSlim(8);

                var tasks = _versionPackageManifest.Select(async package =>
                {
                    try { await throttler.WaitAsync(batchToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }

                    try
                    {
                        // DownloadPackage retries on its own
                        await DownloadPackage(package, batchToken).ConfigureAwait(false);

                        await WithRetryAsync(
                            () => { ExtractPackage(package); return Task.CompletedTask; },
                            $"{LOG_IDENT}::Extract({package.Name})",
                            maxAttempts: 4, baseDelayMs: 800,
                            isTransient: ex => ex is IOException or UnauthorizedAccessException,
                            ct: batchToken).ConfigureAwait(false);

                        Interlocked.Increment(ref packagesComplete);
                    }
                    catch (OperationCanceledException) when (batchToken.IsCancellationRequested) { }
                    catch (Exception ex)
                    {
                        if (Interlocked.CompareExchange(ref firstFailure, ex, null) is null)
                            failedPackage = package.Name;

                        App.Logger.WriteLine(LOG_IDENT, $"Package {package.Name} failed: {ex}");
                        batchCts.Cancel();
                    }
                    finally { throttler.Release(); }
                }).ToList();

                // Progress is driven from here rather than from each download, so
                // the bar and the ETA advance while packages are in flight
                // instead of jumping once per finished package.
                using var progressCts = new CancellationTokenSource();
                var progressTask = Task.Run(async () =>
                {
                    while (!progressCts.IsCancellationRequested)
                    {
                        try
                        {
                            long bytes = Interlocked.Read(ref _totalDownloadedBytes);
                            double speed = bytes / Math.Max(0.5, sw.Elapsed.TotalSeconds);
                            double remaining = Math.Clamp((totalPacked - bytes) / Math.Max(speed, 1), 0, 359_999);
                            string eta = TimeSpan.FromSeconds(remaining).ToString(@"hh\:mm\:ss");

                            SetStatus($"Downloading packages... ({Volatile.Read(ref packagesComplete)}/{totalPackages}) | ETA: {eta}");
                            UpdateProgressBar();

                            await Task.Delay(250, progressCts.Token).ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                            // cancelled, or the dialog is already closing -
                            // either way there is nothing left to report to
                            break;
                        }
                    }
                });

                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                finally
                {
                    progressCts.Cancel();
                    await progressTask.ConfigureAwait(false);
                    sw.Stop();
                }

                if (ct.IsCancellationRequested) return false;
                if (firstFailure is not null)
                    throw new Exception($"Package {failedPackage} failed: {firstFailure.Message}", firstFailure);

                UpdateProgressBar();

                if (Dialog is not null)
                {
                    SetProgressStyle(ProgressBarStyle.Marquee);
                    Dialog.TaskbarProgressState = TaskbarItemProgressState.Indeterminate;
                    SetStatus(Strings.Bootstrapper_Status_Configuring);
                }

                await WithRetryAsync(
                    () => File.WriteAllTextAsync(
                        Path.Combine(_latestVersionDirectory, "AppSettings.xml"), AppSettingsXml, ct),
                    $"{LOG_IDENT}::Write(AppSettings.xml)",
                    maxAttempts: 3, baseDelayMs: 600,
                    isTransient: ex => ex is IOException or UnauthorizedAccessException,
                    ct: ct).ConfigureAwait(false);

                try { MigrateCompatibilityFlags(); }
                catch (Exception ex)
                { App.Logger.WriteLine(LOG_IDENT, $"MigrateCompatibilityFlags: {ex.Message}"); }

                AppData.State.VersionGuid = _latestVersionGuid;
                AppData.State.PackageHashes.Clear();
                foreach (var p in _versionPackageManifest)
                    AppData.State.PackageHashes[p.Name] = p.Signature;

                try { CleanupVersionsFolder(); }
                catch (Exception ex)
                { App.Logger.WriteLine(LOG_IDENT, $"CleanupVersionsFolder: {ex.Message}"); }

                var allHashes = (App.State.Prop.Player?.PackageHashes.Values ?? Enumerable.Empty<string>())
                    .Concat(App.State.Prop.Studio?.PackageHashes.Values ?? Enumerable.Empty<string>())
                    .ToHashSet();

                await Task.WhenAll(cachedHashes
                    .Where(h => h != null && !allHashes.Contains(h))
                    .Select(h => WithRetryAsync(
                        () => { string p = Path.Combine(Paths.Downloads, h!); if (File.Exists(p)) File.Delete(p); return Task.CompletedTask; },
                        $"{LOG_IDENT}::DeleteCache({h})",
                        maxAttempts: 3, baseDelayMs: 500,
                        isTransient: ex => ex is IOException or UnauthorizedAccessException,
                        ct: ct))).ConfigureAwait(false);

                try
                {
                    // EstimatedSize is in kilobytes. This used to divide by the
                    // download buffer-size setting, so changing that setting
                    // changed the size Windows reports for Bustrap, and the int
                    // sum overflowed (and threw) once a Studio install passed 2 GB.
                    int distSize = (int)Math.Min(int.MaxValue,
                        _versionPackageManifest.Sum(x => (long)x.Size + x.PackedSize) / 1024);
                    AppData.State.Size = distSize;

                    int totalSize = (App.State.Prop.Player?.Size ?? 0) + (App.State.Prop.Studio?.Size ?? 0);
                    using var uk = Registry.CurrentUser.CreateSubKey(App.UninstallKey);
                    uk?.SetValueSafe("EstimatedSize", totalSize);
                }
                catch (Exception ex)
                { App.Logger.WriteLine(LOG_IDENT, $"Register size failed: {ex.Message}"); }

                App.State.Save();
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // TaskCanceledException alone missed the plain
                // OperationCanceledException that most cancellation paths throw,
                // so cancelling could pop an "upgrade failed" error box
                App.Logger.WriteLine(LOG_IDENT, "Upgrade cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Upgrade error: {ex}");
                Frontend.ShowMessageBox(
                    $"{Strings.Bootstrapper_Status_Upgrading} failed:\n{ex.Message}",
                    MessageBoxImage.Error);
                return false;
            }
            finally
            {
                Interlocked.Exchange(ref _isInstalling, 0);
            }
        }

        #endregion

        #region Download

        private async Task DownloadPackage(Package package, CancellationToken ct)
        {
            string LOG_IDENT = $"Bootstrapper::DownloadPackage.{package.Name}";

            ct.ThrowIfCancellationRequested();

            Directory.CreateDirectory(Paths.Downloads);

            string packageUrl = Deployment.GetLocation($"/{_latestVersionGuid}-{package.Name}");
            if (!packageUrl.StartsWith("https://setup.rbxcdn.com", StringComparison.OrdinalIgnoreCase))
                packageUrl = $"https://setup.rbxcdn.com/{_latestVersionGuid}-{package.Name}";

            string robloxCache = Path.Combine(
                Paths.LocalAppData, "Roblox", "Downloads", package.Signature);

            if (File.Exists(package.DownloadPath))
            {
                if (HashMatches(package.DownloadPath, package.Signature))
                {
                    App.Logger.WriteLine(LOG_IDENT, "Already downloaded — skipping.");
                    Interlocked.Add(ref _totalDownloadedBytes, package.PackedSize);
                    return;
                }
                File.Delete(package.DownloadPath);
            }
            else if (File.Exists(robloxCache))
            {
                try
                {
                    // Roblox's own cache is outside our control, so it gets the
                    // same checksum as a download. It used to be trusted blindly,
                    // and a stale or truncated file there failed the whole
                    // upgrade at extraction with no way to recover.
                    if (HashMatches(robloxCache, package.Signature))
                    {
                        File.Copy(robloxCache, package.DownloadPath, true);
                        Interlocked.Add(ref _totalDownloadedBytes, package.PackedSize);
                        return;
                    }

                    App.Logger.WriteLine(LOG_IDENT, "Roblox cache entry failed its checksum, downloading instead.");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Roblox cache copy failed: {ex.Message}");
                }
            }

            const int MaxRetries = 10;
            const int MaxChecksumFailures = 3;
            const int MaxParallelSegments = 4;
            const int BufferSize = 1024 * 1024;
            const long MinMultiPartSize = BufferSize * 4L;

            string tempFile = package.DownloadPath + ".part";
            if (File.Exists(tempFile)) File.Delete(tempFile);

            // Bytes this package has added to the shared progress total. Every
            // chunk is counted as it arrives and a failed attempt takes its
            // share back out, so the bar neither double counts nor runs
            // backwards. (Multi-part downloads used to be counted once per chunk
            // and then again in full on completion.)
            long reported = 0;
            void Report(long bytes)
            {
                Interlocked.Add(ref reported, bytes);
                Interlocked.Add(ref _totalDownloadedBytes, bytes);
            }
            void Unreport() =>
                Interlocked.Add(ref _totalDownloadedBytes, -Interlocked.Exchange(ref reported, 0));

            bool allowMultipart = true;
            int checksumFailures = 0;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                bool usedMultipart = false;

                try
                {
                    using var response = await App.HttpClient.GetAsync(
                        packageUrl, HttpCompletionOption.ResponseHeadersRead, ct);

                    if (!response.IsSuccessStatusCode)
                    {
                        if (response.StatusCode == HttpStatusCode.Forbidden)
                        {
                            packageUrl = $"https://setup.rbxcdn.com/{_latestVersionGuid}-{package.Name}";
                            continue;
                        }
                        response.EnsureSuccessStatusCode();
                    }

                    var contentLength = response.Content.Headers.ContentLength;
                    bool supportsRanges =
                        allowMultipart &&
                        contentLength.HasValue &&
                        contentLength.Value >= MinMultiPartSize &&
                        response.Headers.AcceptRanges?.Contains("bytes") == true;

                    if (supportsRanges)
                    {
                        usedMultipart = true;
                        response.Dispose();
                        await DownloadMultipartAsync(
                            packageUrl, tempFile, contentLength!.Value,
                            BufferSize, MaxParallelSegments, Report, LOG_IDENT, ct);
                    }
                    else
                    {
                        await DownloadSingleThreadAsync(
                            response, tempFile, BufferSize, Report, LOG_IDENT, ct);
                    }

                    string hash = MD5Hash.FromFile(tempFile);
                    if (!hash.Equals(package.Signature, StringComparison.OrdinalIgnoreCase))
                        throw new ChecksumFailedException(
                            $"Checksum mismatch for {package.Name}: expected {package.Signature}, got {hash}");

                    File.Move(tempFile, package.DownloadPath, true);

                    // settle on the manifest's size so the total lands exactly on 100%
                    Report(package.PackedSize - Interlocked.Read(ref reported));
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Only our own cancellation ends the download. An HttpClient
                    // timeout is also a TaskCanceledException, and it used to be
                    // swallowed here as if the package had downloaded - the
                    // missing file then blew up at extraction.
                    Unreport();
                    TryDelete(tempFile);
                    throw;
                }
                catch (ChecksumFailedException ex) when (++checksumFailures >= MaxChecksumFailures)
                {
                    Unreport();
                    TryDelete(tempFile);
                    // The string's placeholder is {0}; this used to Replace("[link]"),
                    // which matched nothing and left a literal "{0}" in the dialog.
                    const string helpUrl = "https://github.com/bloxstraplabs/bloxstrap/wiki/Bloxstrap-is-unable-to-download-Roblox";
                    Frontend.ShowConnectivityDialog(
                        Strings.Dialog_Connectivity_UnableToDownload,
                        string.Format(Strings.Dialog_Connectivity_UnableToDownloadReason, $"[{helpUrl}]({helpUrl})"),
                        MessageBoxImage.Error, ex);
                    App.Terminate(ErrorCode.ERROR_CANCELLED);
                    return;
                }
                catch (Exception ex)
                {
                    if (ex is AggregateException agg) ex = agg.Flatten().InnerException ?? agg;
                    App.Logger.WriteLine(LOG_IDENT, $"Attempt {attempt}/{MaxRetries}: {ex.Message}");

                    Unreport();
                    TryDelete(tempFile);

                    // a range download that failed or came back corrupt is
                    // retried as one plain request, in case something between us
                    // and the CDN mishandles ranges
                    if (usedMultipart)
                        allowMultipart = false;

                    if (attempt == MaxRetries) throw;
                    await Task.Delay(Math.Min(2000 * attempt, 10_000), ct);
                }
            }

            // every attempt was spent on 403 redirects
            throw new HttpRequestException($"Unable to download {package.Name}: access was denied on every mirror.");
        }

        private static bool HashMatches(string path, string signature) =>
            MD5Hash.FromFile(path).Equals(signature, StringComparison.OrdinalIgnoreCase);

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("Bootstrapper::TryDelete", $"Could not delete {path}: {ex.Message}");
            }
        }

        private static async Task DownloadSingleThreadAsync(
            HttpResponseMessage response,
            string tempFile,
            int bufferSize,
            Action<long> report,
            string logIdent,
            CancellationToken token)
        {
            await using var net = await response.Content.ReadAsStreamAsync(token);
            await using var file = new FileStream(
                tempFile, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

            byte[] buf = ArrayPool<byte>.Shared.Rent(bufferSize);
            long total = 0;

            try
            {
                int read;
                while ((read = await net.ReadAsync(buf.AsMemory(0, bufferSize), token)) > 0)
                {
                    await file.WriteAsync(buf.AsMemory(0, read), token);
                    total += read;
                    report(read);
                }
                App.Logger.WriteLine(logIdent, $"Downloaded {total:N0} bytes (single-thread)");
            }
            finally { ArrayPool<byte>.Shared.Return(buf); }
        }

        private static async Task DownloadMultipartAsync(
            string url,
            string tempFile,
            long contentLength,
            int bufferSize,
            int maxSegments,
            Action<long> report,
            string logIdent,
            CancellationToken token)
        {
            const long MinSegment = 2L * 1024 * 1024;
            int segs = (int)Math.Clamp(contentLength / MinSegment, 1, maxSegments);

            long segSize = contentLength / segs;
            App.Logger.WriteLine(logIdent,
                $"Multi-part: {segs} segments × ~{segSize:N0} bytes");

            await using var fs = new FileStream(
                tempFile, FileMode.Create, FileAccess.Write, FileShare.Read,
                bufferSize, FileOptions.Asynchronous | FileOptions.RandomAccess);
            fs.SetLength(contentLength);

            var fileLock = new object();
            long totalRead = 0;

            // if one segment fails there is no point finishing the others
            using var segmentCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var segmentToken = segmentCts.Token;

            var tasks = Enumerable.Range(0, segs).Select(i =>
            {
                long start = i * segSize;
                long end = i == segs - 1 ? contentLength - 1 : start + segSize - 1;

                return Task.Run(async () =>
                {
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get, url)
                        {
                            Headers = { Range = new RangeHeaderValue(start, end) }
                        };

                        using var res = await App.HttpClient.SendAsync(
                            req, HttpCompletionOption.ResponseHeadersRead, segmentToken);
                        res.EnsureSuccessStatusCode();

                        // A server or proxy that ignores Range answers 200 with
                        // the whole file. Writing that at this segment's offset
                        // would silently corrupt the package.
                        if (res.StatusCode != HttpStatusCode.PartialContent)
                            throw new HttpRequestException(
                                $"Range request was answered with {(int)res.StatusCode} instead of 206.");

                        await using var net = await res.Content.ReadAsStreamAsync(segmentToken);
                        byte[] buf = ArrayPool<byte>.Shared.Rent(bufferSize);
                        long pos = start;

                        try
                        {
                            int read;
                            while ((read = await net.ReadAsync(buf.AsMemory(0, bufferSize), segmentToken)) > 0)
                            {
                                if (pos + read > end + 1)
                                    throw new HttpRequestException("Range response was longer than the range requested.");

                                lock (fileLock)
                                {
                                    fs.Position = pos;
                                    fs.Write(buf, 0, read);
                                }
                                pos += read;
                                Interlocked.Add(ref totalRead, read);
                                report(read);
                            }
                        }
                        finally { ArrayPool<byte>.Shared.Return(buf); }
                    }
                    catch
                    {
                        segmentCts.Cancel();
                        throw;
                    }
                }, segmentToken);
            }).ToList();

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // WhenAll can surface a sibling's cancellation rather than the
                // segment that actually failed; report the real cause
                var failure = tasks
                    .Where(t => t.IsFaulted)
                    .Select(t => t.Exception!.GetBaseException())
                    .FirstOrDefault();

                if (failure is not null)
                    throw new HttpRequestException($"Multi-part download failed: {failure.Message}", failure);

                throw;
            }

            await fs.FlushAsync(token);

            App.Logger.WriteLine(logIdent, $"Downloaded {totalRead:N0} bytes (multi-part)");
        }

        private void ExtractPackage(Package package, List<string>? files = null)
        {
            const string LOG_IDENT = "Bootstrapper::ExtractPackage";

            string? packageDir = AppData.PackageDirectoryMap.GetValueOrDefault(package.Name);
            if (packageDir is null)
            {
                App.Logger.WriteLine(LOG_IDENT, $"WARNING: {package.Name} not in package map!");
                return;
            }

            string? fileFilter = null;
            if (files is not null)
            {
                var regexList = files.Select(f =>
                    "^" + f.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)") + "$");
                fileFilter = string.Join(';', regexList);
            }

            App.Logger.WriteLine(LOG_IDENT, $"Extracting {package.Name}...");
            new FastZip(_fastZipEvents)
                .ExtractZip(package.DownloadPath, Path.Combine(_latestVersionDirectory, packageDir), fileFilter);
            App.Logger.WriteLine(LOG_IDENT, $"Done: {package.Name}");
        }

        #endregion

        #region Modifications

        private async Task ApplyModifications()
        {
            const string LOG_IDENT = "Bootstrapper::ApplyModifications";
            SetStatus(Strings.Bootstrapper_Status_ApplyingModifications);

            File.Delete(Path.Combine(Paths.Base, "ModManifest.txt"));
            Directory.CreateDirectory(Paths.Mods);

            // Each step gets its own try: these all ran inside one, so GitHub
            // being unreachable or rate-limited (60 unauthenticated API calls an
            // hour) while checking for a newer pack skipped applying the pack
            // that was already on disk.
            try { await ApplySkyboxPatchToRobloxStorageAsync(); }
            catch (Exception ex) { App.Logger.WriteLine(LOG_IDENT, $"Skybox patch failed: {ex.Message}"); }

            try { await EnsureSkyboxPackDownloadedAsync(); }
            catch (Exception ex) { App.Logger.WriteLine(LOG_IDENT, $"Skybox pack update failed: {ex.Message}"); }

            try
            {
                await ApplySkyboxAsync(App.Settings.Prop.SkyboxName, Paths.Mods);
                App.Logger.WriteLine(LOG_IDENT, "Skybox applied.");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Skybox failed: {ex.Message}");
            }

            string modFontDir = Path.Combine(Paths.Mods, "content\\fonts\\families");
            ApplyCustomFont(LOG_IDENT, modFontDir);
            var modFolderFiles = new List<string>();

            foreach (string file in Directory.GetFiles(Paths.Mods, "*.*", SearchOption.AllDirectories))
            {
                if (_cancelTokenSource.IsCancellationRequested) return;

                string rel = file.Substring(Paths.Mods.Length + 1);

                if (rel == "README.txt") { File.Delete(file); continue; }
                if (rel.EndsWith(".lock")) continue;
                if (rel.EndsWith(".mesh")) continue;
                if (!App.Settings.Prop.UseFastFlagManager &&
                    string.Equals(rel, "ClientSettings\\ClientAppSettings.json",
                        StringComparison.OrdinalIgnoreCase)) continue;

                modFolderFiles.Add(rel);

                string src = Path.Combine(Paths.Mods, rel);
                string dest = Path.Combine(_latestVersionDirectory, rel);

                if (File.Exists(dest) && MD5Hash.FromFile(src) == MD5Hash.FromFile(dest))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                Filesystem.AssertReadOnly(dest);
                File.Copy(src, dest, true);
                Filesystem.AssertReadOnly(dest);
                App.Logger.WriteLine(LOG_IDENT, $"Copied mod: {rel}");
            }

            var fileRestoreMap = new Dictionary<string, List<string>>();
            foreach (string loc in App.State.Prop.ModManifest)
            {
                if (modFolderFiles.Contains(loc)) continue;

                var entry = AppData.PackageDirectoryMap
                    .SingleOrDefault(x => !string.IsNullOrEmpty(x.Value) && loc.StartsWith(x.Value));
                string pkgName = entry.Key;

                if (string.IsNullOrEmpty(pkgName))
                {
                    string v = Path.Combine(_latestVersionDirectory, loc);
                    if (File.Exists(v)) File.Delete(v);
                    continue;
                }

                if (!fileRestoreMap.ContainsKey(pkgName)) fileRestoreMap[pkgName] = new();
                fileRestoreMap[pkgName].Add(loc.Substring(entry.Value.Length));
            }

            foreach (var (pkgName, files) in fileRestoreMap)
            {
                if (_cancelTokenSource.IsCancellationRequested) return;
                var pkg = _versionPackageManifest.Find(x => x.Name == pkgName);
                if (pkg is not null)
                {
                    await DownloadPackage(pkg, _cancelTokenSource.Token);
                    ExtractPackage(pkg, files);
                }
            }

            App.State.Prop.ModManifest = modFolderFiles;
            App.State.Save();

  
            try
            {
                bool isEuro = File.Exists(Path.Combine(_latestVersionDirectory, ProcEuroTrucks));
                if (App.Settings.Prop.RenameClientToEuroTrucks2 && !isEuro)
                    File.Move(Path.Combine(_latestVersionDirectory, ProcRobloxExe),
                              Path.Combine(_latestVersionDirectory, ProcEuroTrucks));
                else if (!App.Settings.Prop.RenameClientToEuroTrucks2 && isEuro)
                    File.Move(Path.Combine(_latestVersionDirectory, ProcEuroTrucks),
                              Path.Combine(_latestVersionDirectory, ProcRobloxExe));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "EuroTrucks rename failed: " + ex.Message);
            }
        }

        private void ApplyCustomFont(string logIdent, string modFontDir)
        {
            if (!File.Exists(Paths.CustomFont))
            {
                if (Directory.Exists(modFontDir)) Directory.Delete(modFontDir, true);
                return;
            }

            const string fontAsset = "rbxasset://fonts/CustomFont.ttf";
            string familiesDir = Path.Combine(_latestVersionDirectory, "content", "fonts", "families");
            Directory.CreateDirectory(modFontDir);
            Directory.CreateDirectory(familiesDir);

            foreach (string jsonPath in Directory.GetFiles(familiesDir))
            {
                string name = Path.GetFileName(jsonPath);
                string modPath = Path.Combine(modFontDir, name);
                if (File.Exists(modPath)) continue;

                var family = JsonSerializer.Deserialize<FontFamily>(File.ReadAllText(jsonPath));
                if (family is null) continue;

                bool changed = false;
                foreach (var face in family.Faces)
                {
                    if (face.AssetId != fontAsset)
                    { face.AssetId = fontAsset; changed = true; }
                }

                if (changed)
                    File.WriteAllText(modPath,
                        JsonSerializer.Serialize(family, new JsonSerializerOptions { WriteIndented = true }));
            }

            App.Logger.WriteLine(logIdent, "Custom font applied.");
        }

        #endregion

        #region Skybox

        private async Task<string> GetLatestCommitShaAsync()
        {
            // SkyboxHttpClient allows 30 minutes, which suits the pack download
            // but not a version check that every launch waits on
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var req = new HttpRequestMessage(HttpMethod.Get, SkyboxCommitApiUrl);
            req.Headers.UserAgent.ParseAdd("SkyboxInstaller");
            using var res = await SkyboxHttpClient.SendAsync(req, timeout.Token);
            res.EnsureSuccessStatusCode();
            using var stream = await res.Content.ReadAsStreamAsync();
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream);
            return doc.RootElement.GetProperty("sha").GetString()!;
        }

        private static string? GetLocalCommit() =>
            File.Exists(Path.Combine(PackFolder, SkyboxVersionFile))
                ? File.ReadAllText(Path.Combine(PackFolder, SkyboxVersionFile))
                : null;

        private static void SaveLocalCommit(string sha) =>
            File.WriteAllText(Path.Combine(PackFolder, SkyboxVersionFile), sha);

        public async Task EnsureSkyboxPackDownloadedAsync()
        {
            Directory.CreateDirectory(PackFolder);

            string latest = await GetLatestCommitShaAsync();
            if (GetLocalCommit() == latest &&
                Directory.GetFiles(PackFolder, "*", SearchOption.AllDirectories).Length > 0)
                return;

            SetStatus("Updating Skybox Pack...");

            string tempZip = Path.Combine(Path.GetTempPath(), "SkyboxPackV2.zip");

            using (var response = await SkyboxHttpClient.GetAsync(
                SkyboxZipUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                long total = response.Content.Headers.ContentLength ?? -1L;
                long read = 0;
                byte[] buf = new byte[262144];
                var lastPrint = Stopwatch.StartNew();

                await using var src = await response.Content.ReadAsStreamAsync();
                await using var dst = new FileStream(
                    tempZip, FileMode.Create, FileAccess.Write, FileShare.None, buf.Length, true);

                int chunk;
                while ((chunk = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, chunk));
                    read += chunk;

                    if (lastPrint.ElapsedMilliseconds > 200)
                    {
                        SetStatus(total > 0
                            ? $"Downloading Skybox... {read * 100.0 / total:F1}%"
                            : $"Downloading Skybox... {BytesToString(read)}");
                        lastPrint.Restart();
                    }
                }
            }

            if (Directory.Exists(PackFolder)) Directory.Delete(PackFolder, true);
            Directory.CreateDirectory(PackFolder);

            using (var zip = System.IO.Compression.ZipFile.OpenRead(tempZip))
            {
                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    var parts = entry.FullName.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length <= 1)
                        continue;

                    string relativePath = Path.Combine(parts.Skip(1).ToArray());
                    string dest = SecurityHelpers.CombineUnderDirectory(PackFolder, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                    using var es = entry.Open();
                    using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write);
                    await es.CopyToAsync(fs);
                }
            }

            SaveLocalCommit(latest);
            File.Delete(tempZip);
        }

        public static async Task ApplySkyboxAsync(string skyboxName, string modsFolder)
        {
            string src = Path.Combine(PackFolder, skyboxName);
            if (!Directory.Exists(src))
                throw new DirectoryNotFoundException($"Skybox '{skyboxName}' not found.");

            string dest = Path.Combine(modsFolder, "PlatformContent", "pc", "textures", "sky");

            if (Directory.Exists(dest))
            {
                foreach (var f in Directory.GetFiles(dest, "*.*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(dest, true);
            }

            Directory.CreateDirectory(dest);

            foreach (var file in Directory.GetFiles(src, "*.*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(src, file);
                string dOut = Path.Combine(dest, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dOut)!);
                File.Copy(file, dOut, true);
            }
        }

        public static async Task ApplySkyboxPatchToRobloxStorageAsync()
        {
            string rbxStorage = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox", "rbx-storage");

            const string githubBase = "https://raw.githubusercontent.com/KloBraticc/SkyboxPatch/main/assets/";

            // This runs on every launch, in front of the user. The requests are
            // independent, so they go out together rather than in sequence, on a
            // short timeout so an unreachable GitHub cannot hold the launch up.
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

            await Task.WhenAll(SkyboxPatchFolderMap.Select(async entry =>
            {
                var (hash, folder) = entry;

                try
                {
                    string dir = Path.Combine(rbxStorage, folder);
                    Directory.CreateDirectory(dir);
                    string dest = Path.Combine(dir, hash);

                    byte[] data = await http.GetByteArrayAsync(githubBase + hash).ConfigureAwait(false);
                    if (File.Exists(dest))
                        File.SetAttributes(dest, FileAttributes.Normal);
                    await File.WriteAllBytesAsync(dest, data).ConfigureAwait(false);
                    File.SetAttributes(dest, FileAttributes.ReadOnly);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("SkyboxPatch", $"Failed {hash}: {ex.Message}");
                }
            })).ConfigureAwait(false);
        }

        private static string BytesToString(long bytes)
        {
            if (bytes == 0) return "0 B";
            string[] suf = { "B", "KB", "MB", "GB", "TB" };
            int place = (int)Math.Floor(Math.Log(Math.Abs(bytes), 1024));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return $"{num} {suf[Math.Min(place, suf.Length - 1)]}";
        }

        #endregion
    }
}