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

            /// <summary>
            /// True only for Spotify, TIDAL and Apple Music. Everything else
            /// that registers a session - browsers, games, Discord calls - is
            /// still reported, but callers can use this to ignore it.
            /// </summary>
            public bool IsMusicService { get; init; }
        }

        /// <summary>Raised on a background thread - marshal before touching UI.</summary>
        public event EventHandler<NowPlaying?>? Changed;

        /// <summary>
        /// Raised only when the track itself changes - not on play/pause, seeking
        /// or the position ticking over. Also a background thread.
        /// </summary>
        public event EventHandler<NowPlaying>? TrackChanged;

        private static MediaSessionWatcher? _shared;
        private static readonly object _sharedLock = new();

        /// <summary>
        /// Process-wide instance. The tray menu and the music window both listen,
        /// and holding one session manager rather than several keeps the event
        /// traffic down. Deliberately never disposed - it lives as long as the app.
        /// </summary>
        public static MediaSessionWatcher Shared
        {
            get
            {
                lock (_sharedLock)
                {
                    if (_shared is null)
                    {
                        _shared = new MediaSessionWatcher();
                        _ = _shared.StartAsync();
                    }

                    return _shared;
                }
            }
        }

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

                var (sourceName, isMusicService) = DescribeSource(session.SourceAppUserModelId);

                Publish(new NowPlaying
                {
                    Source = sourceName,
                    IsMusicService = isMusicService,
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

        private string _lastTrackKey = "";

        private void Publish(NowPlaying? nowPlaying)
        {
            Current = nowPlaying;
            Changed?.Invoke(this, nowPlaying);

            // SMTC re-fires on every play/pause and metadata touch, so the track
            // has to be compared rather than trusting the event itself
            string key = nowPlaying is null
                ? ""
                : $"{nowPlaying.Source}|{nowPlaying.Title}|{nowPlaying.Artist}";

            if (key == _lastTrackKey)
                return;

            _lastTrackKey = key;

            if (nowPlaying is not null && !string.IsNullOrWhiteSpace(nowPlaying.Title))
                TrackChanged?.Invoke(this, nowPlaying);
        }

        /// <summary>
        /// The three services the song-change toast is limited to. Matched on
        /// the app user model id, which is an executable name for Spotify and
        /// TIDAL and a package family name for Apple Music.
        /// </summary>
        private static readonly (string Match, string Name)[] MusicServices =
        {
            ("spotify", "Spotify"),
            ("tidal", "TIDAL"),
            ("applemusic", "Apple Music"),
        };

        /// <summary>
        /// Turns an app user model id into a display name, and says whether it
        /// is one of the music services rather than a browser or a game.
        /// </summary>
        private static (string Name, bool IsMusicService) DescribeSource(string? appId)
        {
            if (string.IsNullOrWhiteSpace(appId))
                return ("Unknown", false);

            string id = appId.ToLowerInvariant();

            foreach (var (match, name) in MusicServices)
            {
                if (id.Contains(match))
                    return (name, true);
            }

            // recognised, but not one of the three - named for the Streaming
            // tab, and deliberately not flagged as a music service
            if (id.Contains("itunes")) return ("iTunes", false);
            if (id.Contains("msedge")) return ("Microsoft Edge", false);
            if (id.Contains("chrome")) return ("Chrome", false);
            if (id.Contains("firefox")) return ("Firefox", false);

            // strip the executable suffix, and the "!App" tail packaged apps carry
            int bang = appId.IndexOf('!');
            string trimmed = bang > 0 ? appId[..bang] : appId;

            if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[..^4];

            return (trimmed, false);
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
