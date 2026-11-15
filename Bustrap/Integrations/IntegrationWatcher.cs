namespace Bustrap.Integrations
{
    public class IntegrationWatcher : IDisposable
    {
        private readonly ActivityWatcher _activityWatcher;
        private readonly Dictionary<int, CustomIntegration> _activeIntegrations = new();

        public IntegrationWatcher(ActivityWatcher activityWatcher)
        {
            _activityWatcher = activityWatcher;

            _activityWatcher.OnGameJoin += OnGameJoin;
            _activityWatcher.OnGameLeave += OnGameLeave;
        }

        private void OnGameJoin(object? sender, EventArgs e)
        {
            if (!_activityWatcher.InGame)
                return;

            long currentGameId = _activityWatcher.Data.PlaceId;

            foreach (var integration in App.Settings.Prop.CustomIntegrations)
            {
                if (!integration.SpecifyGame || integration.GameID != currentGameId.ToString())
                    continue;

                LaunchIntegration(integration);
            }
        }

        private void OnGameLeave(object? sender, EventArgs e)
        {
            foreach (var pid in _activeIntegrations.Keys.ToList())
            {
                var integration = _activeIntegrations[pid];
                if (integration.AutoCloseOnGame)
                {
                    TerminateProcess(pid);
                    _activeIntegrations.Remove(pid);
                }
            }
        }

        private void LaunchIntegration(CustomIntegration integration)
        {
            const string LOG_IDENT = "IntegrationWatcher::LaunchIntegration";

            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = integration.Location,
                    Arguments = (integration.LaunchArgs ?? "").Replace("\r\n", " "),
                    WorkingDirectory = Path.GetDirectoryName(integration.Location),
                    UseShellExecute = true
                });

                if (process != null)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Integration '{integration.Name}' launched for game ID '{integration.GameID}' (PID {process.Id}).");
                    _activeIntegrations[process.Id] = integration;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to launch integration '{integration.Name}': {ex.Message}");
            }
        }

        private void TerminateProcess(int pid)
        {
            const string LOG_IDENT = "IntegrationWatcher::TerminateProcess";

            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill();

                App.Logger.WriteLine(LOG_IDENT, $"Terminated integration process (PID {pid}).");
            }
            catch (Exception)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to terminate process (PID {pid}), likely already exited.");
            }
        }

        public void Dispose()
        {
            // Roblox has closed. Only close what the user asked to have closed:
            // this used to kill every game-specific integration, including the
            // ones with both auto-close options switched off.
            foreach (var (pid, integration) in _activeIntegrations)
            {
                if (integration.AutoClose || integration.AutoCloseOnGame)
                    TerminateProcess(pid);
            }

            _activeIntegrations.Clear();

            GC.SuppressFinalize(this);
        }
    }
}