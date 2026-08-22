using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Navigation;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace Bustrap.UI.Elements.Settings.Pages
{
    public partial class HubPage
    {
        private const string LOG_IDENT = "HubPage";

        private static readonly Uri ReleasesApiUri =
            new("https://api.github.com/repos/rustybusgaming/Bustrap/releases");

        private static readonly HttpClient HttpClient = CreateHttpClient();
        private static readonly string CacheFile =
            Path.Combine(Paths.Base, "Releases.json");

        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public ObservableCollection<GithubRelease> Releases { get; } = new();
        private readonly ICollectionView _releasesView;

        private readonly DispatcherTimer _refreshTimer;
        private FileSystemWatcher? _cacheWatcher;
        private CancellationTokenSource? _cts;

        private string? _etag;

        // The last payload actually rendered. Both the network fetch and the
        // cache watcher can deliver the same JSON (writing the cache file makes
        // the watcher fire), so this keeps the list from being torn down and
        // rebuilt - which resets scroll position and the active search filter -
        // when nothing has actually changed.
        private string? _renderedJson;

        private bool _initialLoadDone;

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "BustrapApp/1.0 (+https://github.com/rustybusgaming/Bustrap)");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        public HubPage()
        {
            InitializeComponent();
            DataContext = this;

            _releasesView = CollectionViewSource.GetDefaultView(Releases);

            Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);

            _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
            _refreshTimer.Tick += OnRefreshTick;

            // Polling and the file watcher only run while the page is on screen.
            // Navigating away stops both, so repeated visits can't stack up
            // background loops that keep hitting the GitHub API forever.
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_cts is not null)
                return;

            var cts = new CancellationTokenSource();
            _cts = cts;

            StartCacheWatcher();
            _refreshTimer.Start();

            bool firstLoad = !_initialLoadDone;
            _initialLoadDone = true;

            if (firstLoad)
                await LoadFromCacheAsync();

            await LoadReleasesAsync(firstLoad, cts.Token);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _refreshTimer.Stop();

            _cacheWatcher?.Dispose();
            _cacheWatcher = null;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private async void OnRefreshTick(object? sender, EventArgs e)
        {
            var cts = _cts;

            if (cts is null)
                return;

            await LoadReleasesAsync(false, cts.Token);
        }

        private void StartCacheWatcher()
        {
            if (_cacheWatcher is not null)
                return;

            _cacheWatcher = new FileSystemWatcher
            {
                Path = Path.GetDirectoryName(CacheFile)!,
                Filter = Path.GetFileName(CacheFile),
                NotifyFilter = NotifyFilters.LastWrite |
                               NotifyFilters.Size |
                               NotifyFilters.FileName
            };

            _cacheWatcher.Changed += OnCacheFileTouched;
            _cacheWatcher.Created += OnCacheFileTouched;

            _cacheWatcher.EnableRaisingEvents = true;
        }

        private async void OnCacheFileTouched(object sender, FileSystemEventArgs e) =>
            await LoadFromCacheAsync();

        private async Task LoadReleasesAsync(bool force, CancellationToken token)
        {
            try
            {
                using var request =
                    new HttpRequestMessage(HttpMethod.Get, ReleasesApiUri);

                if (!force && !string.IsNullOrEmpty(_etag))
                    request.Headers.IfNoneMatch.Add(
                        new EntityTagHeaderValue(_etag));

                using var response = await HttpClient.SendAsync(request, token);

                if (response.StatusCode == HttpStatusCode.NotModified)
                    return;

                if (!response.IsSuccessStatusCode)
                {
                    App.Logger.WriteLine(LOG_IDENT,
                        $"Could not fetch releases: {(int)response.StatusCode} {response.ReasonPhrase}");
                    return;
                }

                _etag = response.Headers.ETag?.Tag;

                string json = await response.Content.ReadAsStringAsync(token);

                await File.WriteAllTextAsync(CacheFile, json, token);

                Render(json);
            }
            catch (OperationCanceledException)
            {
                // page was navigated away from, or the request timed out
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::LoadReleasesAsync", ex);
            }
        }

        private async Task LoadFromCacheAsync()
        {
            if (!File.Exists(CacheFile))
                return;

            try
            {
                Render(await File.ReadAllTextAsync(CacheFile));
            }
            catch (IOException)
            {
                // the cache file is mid-write; the next event will pick it up
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::LoadFromCacheAsync", ex);
            }
        }

        private void Render(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json == _renderedJson)
                return;

            var releases = JsonSerializer.Deserialize<GithubRelease[]>(json, JsonOptions)
                           ?? Array.Empty<GithubRelease>();

            _renderedJson = json;

            UpdateReleasesCollection(releases);
        }

        private void UpdateReleasesCollection(GithubRelease[] releases)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Releases.Clear();
                foreach (var rel in releases)
                {
                    rel.CalculateTotals();
                    Releases.Add(rel);
                }
            });
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // SearchBox is a ui:TextBox, which derives from the WPF TextBox -
            // casting to the WinForms one silently produced null and left the
            // filter permanently disabled.
            string query =
                (sender as System.Windows.Controls.TextBox)?.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(query))
            {
                _releasesView.Filter = null;
            }
            else
            {
                _releasesView.Filter = obj =>
                {
                    if (obj is not GithubRelease r) return false;

                    bool Matches(string? s) =>
                        !string.IsNullOrEmpty(s) &&
                        s.Contains(query, StringComparison.OrdinalIgnoreCase);

                    return Matches(r.Name) ||
                           Matches(r.TagName) ||
                           Matches(r.Body);
                };
            }

            _releasesView.Refresh();
        }

        private void Hyperlink_RequestNavigate(
            object sender,
            RequestNavigateEventArgs e)
        {
            try
            {
                e.Handled = true;
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"{LOG_IDENT}::Hyperlink_RequestNavigate", ex);
            }
        }

        public class GithubRelease
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("tag_name")]
            public string? TagName { get; set; }

            [JsonPropertyName("body")]
            public string? Body { get; set; }

            [JsonPropertyName("prerelease")]
            public bool Prerelease { get; set; }

            [JsonPropertyName("draft")]
            public bool Draft { get; set; }

            [JsonPropertyName("published_at")]
            public DateTimeOffset PublishedAt { get; set; }

            [JsonPropertyName("html_url")]
            public string? HtmlUrl { get; set; }

            [JsonPropertyName("assets")]
            public GithubAsset[] Assets { get; set; } = Array.Empty<GithubAsset>();

            public int TotalDownloads { get; private set; }

            public void CalculateTotals()
            {
                TotalDownloads =
                    Assets?.Sum(a => a.DownloadCount) ?? 0;
            }
        }

        public class GithubAsset
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("content_type")]
            public string? ContentType { get; set; }

            [JsonPropertyName("browser_download_url")]
            public string? BrowserDownloadUrl { get; set; }

            [JsonPropertyName("size")]
            public long Size { get; set; }

            [JsonPropertyName("download_count")]
            public int DownloadCount { get; set; }

            public double SizeMb =>
                Size / 1024d / 1024d;
        }
    }
}
