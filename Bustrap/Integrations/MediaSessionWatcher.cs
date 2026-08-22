using System;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.Control;

namespace Bustrap.Integrations
{
    /// <summary>
    /// Reports whatever is playing through Windows' System Media Transport
    /// Controls.
    ///
    /// Spotify, TIDAL, Apple Music, iTunes and browser players all register a
    /// session there, so this one integration covers every service at once.
    /// That matters because the per-service alternative barely exists: Spotify's
    /// Web API needs a registered OAuth app and only controls playback for
    /// Premium accounts, and neither TIDAL nor Apple Music publishes an API a
    /// third-party desktop client can use. SMTC needs no account, no key and no
    /// network call, and it keeps working when those services change their APIs.
    /// </summary>
    public sealed class MediaSessionWatcher : IDisposable
    {
        private const string LOG_IDENT = "MediaSessionWatcher";

        public sealed class NowPlaying
        {
            public string Source { get; init; } = "";
            public string Title { get; init; } = "";
            public string Artist { get; init; } = "";
            public string Album { get; init; } = "";
            public bool IsPlaying { get; init; }
        }

        /// <summary>Raised on a background thread - marshal before touching UI.</summary>
        public event EventHandler<NowPlaying?>? Changed;

        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _session;
        private bool _disposed;

        public NowPlaying? Current { get; private set; }

        public async Task StartAsync()
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.CurrentSessionChanged += OnCurrentSessionChanged;

                AttachToCurrentSession();
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                // no media session support, or the request was refused - the
                // panel just stays empty rather than taking the window down
                App.Logger.WriteException($"{LOG_IDENT}::StartAsync", ex);
            }
        }

        private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            AttachToCurrentSession();
            _ = RefreshAsync();
        }

        private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
            _ = RefreshAsync();

        private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
            _ = RefreshAsync();

        private void AttachToCurrentSession()
        {
            DetachFromSession();

            if (_disposed)
                return;

            _session = _manager?.GetCurrentSession();

            if (_session is null)
                return;

            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        }

        private void DetachFromSession()
        {
            if (_session is null)
                return;

            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session = null;
        }

        private async Task RefreshAsync()
        {
            try
            {
                var session = _session;

                if (session is null)
                {
                    Publish(null);
                    return;
                }

                var properties = await session.TryGetMediaPropertiesAsync();
                var playback = session.GetPlaybackInfo();

                Publish(new NowPlaying
                {
                    Source = FriendlySourceName(session.SourceAppUserModelId),
                    Title = properties?.Title ?? "",
                    Artist = properties?.Artist ?? "",
                    Album = properties?.AlbumTitle ?? "",
                    IsPlaying = playback?.PlaybackStatus
                        == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                });
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::RefreshAsync", ex);
            }
        }

        private void Publish(NowPlaying? nowPlaying)
        {
            Current = nowPlaying;
            Changed?.Invoke(this, nowPlaying);
        }

        /// <summary>
        /// Turns an app user model id into something worth showing. Spotify and
        /// TIDAL report an executable name; Apple Music is a packaged app, so it
        /// reports a package family name instead.
        /// </summary>
        private static string FriendlySourceName(string? appId)
        {
            if (string.IsNullOrWhiteSpace(appId))
                return "Unknown";

            string id = appId.ToLowerInvariant();

            if (id.Contains("spotify")) return "Spotify";
            if (id.Contains("tidal")) return "TIDAL";
            if (id.Contains("applemusic")) return "Apple Music";
            if (id.Contains("itunes")) return "iTunes";
            if (id.Contains("msedge")) return "Microsoft Edge";
            if (id.Contains("chrome")) return "Chrome";
            if (id.Contains("firefox")) return "Firefox";

            // strip the executable suffix, and the "!App" tail packaged apps carry
            int bang = appId.IndexOf('!');
            string name = bang > 0 ? appId[..bang] : appId;

            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? name[..^4]
                : name;
        }

        public Task<bool> TogglePlayPauseAsync() => TryControlAsync(s => s.TryTogglePlayPauseAsync());

        public Task<bool> SkipNextAsync() => TryControlAsync(s => s.TrySkipNextAsync());

        public Task<bool> SkipPreviousAsync() => TryControlAsync(s => s.TrySkipPreviousAsync());

        private async Task<bool> TryControlAsync(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> action)
        {
            var session = _session;

            if (session is null)
                return false;

            try
            {
                return await action(session);
            }
            catch (Exception ex)
            {
                // the app can refuse or disappear mid-command
                App.Logger.WriteException($"{LOG_IDENT}::TryControlAsync", ex);
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            DetachFromSession();

            if (_manager is not null)
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;

            _manager = null;
        }
    }
}
