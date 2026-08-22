using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Bustrap;

public static class GithubUpdater
{
    private static readonly string[] AllowedReleaseHosts = new[]
    {
        "api.github.com",
        "github.com",
        "objects.githubusercontent.com",
        "releases.githubusercontent.com"
    };

    private static readonly HttpClient http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "Bustrap-Updater" } }
    };

    public static async Task<string?> GetLatestVersionTagAsync()
    {
        try
        {
            string url = "https://api.github.com/repos/rustybusgaming/Bustrap/releases/latest";
            string response = await http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            return doc.RootElement.GetProperty("tag_name").GetString();
        }
        catch (Exception ex)
        {
            App.Logger.WriteLine("GitHubUpdater", $"Failed to get latest release tag: {ex}");
            return null;
        }
    }

    public static async Task<bool> DownloadAndInstallUpdate(string tag)
    {
        try
        {
            string url = "https://api.github.com/repos/rustybusgaming/Bustrap/releases/latest";
            string response = await http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            var assets = doc.RootElement.GetProperty("assets");

            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                string downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";

                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    return await UpdateExe(downloadUrl, name);

                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    return await UpdateZip(downloadUrl, name);
            }

            App.Logger.WriteLine("GitHubUpdater", "No valid .exe or .zip asset found.");
            return false;
        }
        catch (Exception ex)
        {
            App.Logger.WriteLine("GitHubUpdater", $"Update failed: {ex}");
            return false;
        }
    }

    private static async Task<bool> UpdateExe(string url, string name)
    {
        SecurityHelpers.ValidateRemoteHttpsUrl(url, AllowedReleaseHosts);

        string tempDir = Path.Combine(Path.GetTempPath(), "Bustrap_Update");
        Directory.CreateDirectory(tempDir);

        string staged = Path.Combine(tempDir, name);
        await File.WriteAllBytesAsync(staged, await http.GetByteArrayAsync(url));

        string currentExe = Environment.ProcessPath!;
        string backupExe = currentExe + ".old";

        if (File.Exists(backupExe))
            File.Delete(backupExe);

        // Windows lets a running executable be renamed but not deleted, so the
        // old build is moved aside instead of overwritten. If putting the new
        // build down then fails, that move HAS to be undone - otherwise the
        // install is left with no executable and the next launch finds nothing.
        File.Move(currentExe, backupExe);

        try
        {
            File.Copy(staged, currentExe, true);
            return true;
        }
        catch (Exception ex)
        {
            App.Logger.WriteException("GitHubUpdater::UpdateExe", ex);
            Rollback(backupExe, currentExe);
            return false;
        }
    }

    private static async Task<bool> UpdateZip(string url, string name)
    {
        SecurityHelpers.ValidateRemoteHttpsUrl(url, AllowedReleaseHosts);

        string tempDir = Path.Combine(Path.GetTempPath(), "Bustrap_Update");
        Directory.CreateDirectory(tempDir);

        string zipPath = Path.Combine(tempDir, name);
        await File.WriteAllBytesAsync(zipPath, await http.GetByteArrayAsync(url));

        string extractPath = Path.Combine(tempDir, "Extracted");
        if (Directory.Exists(extractPath))
            Directory.Delete(extractPath, true);
        ExtractZipSafely(zipPath, extractPath);

        string currentDir = AppContext.BaseDirectory;

        // Same problem as the single-exe path, spread over many files: a
        // failure partway through would leave a half-old half-new install.
        // Everything replaced is moved aside first so it can all be put back.
        var moved = new List<(string Destination, string Backup)>();

        try
        {
            foreach (string file in Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(extractPath, file);
                string dest = Path.Combine(currentDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                if (File.Exists(dest))
                {
                    string backup = dest + ".old";
                    if (File.Exists(backup))
                        File.Delete(backup);

                    File.Move(dest, backup);
                    moved.Add((dest, backup));
                }

                File.Copy(file, dest, true);
            }

            return true;
        }
        catch (Exception ex)
        {
            App.Logger.WriteException("GitHubUpdater::UpdateZip", ex);

            foreach (var (destination, backup) in moved)
                Rollback(backup, destination);

            return false;
        }
    }

    private static void ExtractZipSafely(string zipPath, string extractPath)
    {
        Directory.CreateDirectory(extractPath);

        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            string destinationPath = SecurityHelpers.CombineUnderDirectory(extractPath, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static void Rollback(string backup, string destination)
    {
        try
        {
            if (File.Exists(destination))
                File.Delete(destination);

            File.Move(backup, destination);
        }
        catch (Exception ex)
        {
            // Nothing left to fall back on. The user has to know, because the
            // install is now missing a file it needs to start.
            App.Logger.WriteException("GitHubUpdater::Rollback", ex);
            Frontend.ShowMessageBox(
                $"Bustrap could not finish updating and could not restore the previous version.\n\n" +
                $"The previous build is still on disk at:\n{backup}\n\n" +
                $"Rename it back to:\n{destination}\n\n" +
                $"or reinstall Bustrap from the website.",
                System.Windows.MessageBoxImage.Error);
        }
    }
}
