using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Bustrap.Integrations;

namespace Bustrap.UI.ViewModels.ContextMenu
{
    /// <summary>
    /// Surfaces whatever Spotify, TIDAL, Apple Music or any other media app is
    /// playing, with transport controls. Backed by <see cref="MediaSessionWatcher"/>.
    /// </summary>
    public sealed class MediaSessionViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly MediaSessionWatcher _watcher = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        public IAsyncRelayCommand PlayPauseCommand { get; }
        public IAsyncRelayCommand NextCommand { get; }
        public IAsyncRelayCommand PreviousCommand { get; }

        public MediaSessionViewModel()
        {
            PlayPauseCommand = new AsyncRelayCommand(async () => await _watcher.TogglePlayPauseAsync());
            NextCommand = new AsyncRelayCommand(async () => await _watcher.SkipNextAsync());
            PreviousCommand = new AsyncRelayCommand(async () => await _watcher.SkipPreviousAsync());

            _watcher.Changed += OnChanged;
            _ = _watcher.StartAsync();
        }

        private string _source = "";
        public string Source
        {
            get => _source;
            private set { _source = value; OnPropertyChanged(); }
        }

        private string _title = "";
        public string Title
        {
            get => _title;
            private set { _title = value; OnPropertyChanged(); }
        }

        private string _artist = "";
        public string Artist
        {
            get => _artist;
            private set { _artist = value; OnPropertyChanged(); }
        }

        private string _album = "";
        public string Album
        {
            get => _album;
            private set { _album = value; OnPropertyChanged(); }
        }

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            private set
            {
                _isPlaying = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlayPauseLabel));
            }
        }

        private bool _hasSession;
        public bool HasSession
        {
            get => _hasSession;
            private set
            {
                _hasSession = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SessionVisibility));
                OnPropertyChanged(nameof(EmptyVisibility));
            }
        }

        public string PlayPauseLabel => IsPlaying ? "Pause" : "Play";
        public Visibility SessionVisibility => HasSession ? Visibility.Visible : Visibility.Collapsed;
        public Visibility EmptyVisibility => HasSession ? Visibility.Collapsed : Visibility.Visible;

        private void OnChanged(object? sender, MediaSessionWatcher.NowPlaying? nowPlaying)
        {
            // SMTC raises its events on a background thread
            var dispatcher = Application.Current?.Dispatcher;

            if (dispatcher is null)
                return;

            dispatcher.InvokeAsync(() =>
            {
                HasSession = nowPlaying is not null;
                Source = nowPlaying?.Source ?? "";
                Title = nowPlaying?.Title ?? "";
                Artist = nowPlaying?.Artist ?? "";
                Album = nowPlaying?.Album ?? "";
                IsPlaying = nowPlaying?.IsPlaying ?? false;
            });
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public void Dispose()
        {
            _watcher.Changed -= OnChanged;
            _watcher.Dispose();
        }
    }
}
