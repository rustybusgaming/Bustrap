using Bustrap.AppData;
using Bustrap.Integrations;
using Bustrap.Models;
using Bustrap.UI.Elements.ContextMenu;

namespace Bustrap
{
    public class Watcher : IDisposable
    {
        private readonly InterProcessLock _lock = new("Watcher");

        private readonly WatcherData? _watcherData;

        private readonly NotifyIconWrapper? _notifyIcon;

        public readonly ActivityWatcher? ActivityWatcher;

        public readonly DiscordRichPresence? RichPresence;

        public readonly IntegrationWatcher? IntegrationWatcher;

        public Watcher()
        {
            const string LOG_IDENT = "Watcher";

            if (!_lock.IsAcquired)
            {
                App.Logger.WriteLine(LOG_IDENT, "Watcher instance already exists");
                return;
            }

            string? watcherDataArg = App.LaunchSettings.WatcherFlag.Data;

#if DEBUG
            if (String.IsNullOrEmpty(watcherDataArg))
            {
                string path = new RobloxPlayerData().ExecutablePath;
                using var gameClientProcess = Process.Start(path);

                _watcherData = new() { ProcessId = gameClientProcess.Id };
            }
#else
            if (String.IsNullOrEmpty(watcherDataArg))
                throw new Exception("Watcher data not specified");
#endif

            if (!String.IsNullOrEmpty(watcherDataArg))
                _watcherData = JsonSerializer.Deserialize<WatcherData>(Encoding.UTF8.GetString(Convert.FromBase64String(watcherDataArg)));

            if (_watcherData is null)
                throw new Exception("Watcher data is invalid");

            if (App.Settings.Prop.EnableActivityTracking)
            {
                ActivityWatcher = new(_watcherData.LogFile);

                if (App.Settings.Prop.UseDisableAppPatch)
                {
                    ActivityWatcher.OnAppClose += delegate
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Received desktop app exit, closing Roblox");
                        using var process = Process.GetProcessById(_watcherData.ProcessId);
                        process.CloseMainWindow();
                    };
                }

                if (App.Settings.Prop.UseDiscordRichPresence)
                    RichPresence = new(ActivityWatcher);


                IntegrationWatcher = new IntegrationWatcher(ActivityWatcher);
            }

            _notifyIcon = new(this);
        }

        public void KillRobloxProcess() => CloseProcess(_watcherData!.ProcessId, true);

        public void CloseProcess(int pid, bool force = false)
        {
            const string LOG_IDENT = "Watcher::CloseProcess";

            try
            {
                using var process = Process.GetProcessById(pid);

                App.Logger.WriteLine(LOG_IDENT, $"Killing process '{process.ProcessName}' (pid={pid}, force={force})");

                if (process.HasExited)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"PID {pid} has already exited");
                    return;
                }

                if (force)
                    process.Kill();
                else
                    process.CloseMainWindow();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"PID {pid} could not be closed");
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        public async Task Run()
        {
            if (!_lock.IsAcquired || _watcherData is null)
                return;

            ActivityWatcher?.Start();

            using var memoryCleanerCts = new CancellationTokenSource();
            Task memoryCleaner = RunMemoryCleanerAsync(memoryCleanerCts.Token);

            await WaitForProcessExitAsync(_watcherData.ProcessId);

            memoryCleanerCts.Cancel();
            await memoryCleaner;

            if (_watcherData.AutoclosePids is not null)
            {
                foreach (int pid in _watcherData.AutoclosePids)
                    CloseProcess(pid);
            }

            if (App.LaunchSettings.TestModeFlag.Active)
                Process.Start(Paths.Process, "-settings -testmode");
        }

        /// <summary>
        /// Waits for Roblox to close. This used to list every process on the
        /// machine twice a second for the whole session; waiting on the one
        /// process costs nothing while the game is running.
        /// </summary>
        private static async Task WaitForProcessExitAsync(int pid)
        {
            const string LOG_IDENT = "Watcher::WaitForProcessExit";

            try
            {
                using var process = Process.GetProcessById(pid);
                await process.WaitForExitAsync();
                return;
            }
            catch (ArgumentException)
            {
                // not running (any more)
                return;
            }
            catch (Exception ex)
            {
                // e.g. Roblox is elevated and we are not, so its handle is off limits
                App.Logger.WriteLine(LOG_IDENT, $"Could not wait on PID {pid} directly, polling instead: {ex.Message}");
            }

            while (Utilities.GetProcessesSafe().Any(x => x.Id == pid))
                await Task.Delay(1000);
        }

        /// <summary>
        /// The periodic memory cleaner. It lives here because the watcher is the
        /// only Bustrap process that outlives the launch: the bootstrapper used
        /// to own it, on a timer that was stopped again seconds later when the
        /// bootstrapper exited, so no interval ever actually fired.
        /// </summary>
        private static async Task RunMemoryCleanerAsync(CancellationToken token)
        {
            const string LOG_IDENT = "Watcher::MemoryCleaner";

            int seconds = BustrapRobloxSettingsManager.Load().MemoryCleanerIntervalSeconds;

            if (seconds <= 0)
                return;

            App.Logger.WriteLine(LOG_IDENT, $"Cleaning Roblox memory every {seconds}s");

            try
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(seconds), token);

                    try
                    {
                        RobloxMemoryCleaner.CleanAllRobloxMemory();
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException(LOG_IDENT, ex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public void Dispose()
        {
            App.Logger.WriteLine("Watcher::Dispose", "Disposing Watcher");

            // first, so the desktop resolution comes back before anything else
            // tears down
            ActivityWatcher?.Dispose();
            IntegrationWatcher?.Dispose();
            _notifyIcon?.Dispose();
            RichPresence?.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}