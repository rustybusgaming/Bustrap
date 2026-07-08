using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Windows;

namespace Bustrap
{
    public class JsonManager<T> where T : class, new()
    {
        private static readonly JsonSerializerOptions LoadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            IncludeFields = true
        };

        private static readonly JsonSerializerOptions SaveOptions = new()
        {
            WriteIndented = true,
            IncludeFields = true
        };

        public T OriginalProp { get; set; } = new();
        public T Prop { get; set; } = new();
        public virtual string ClassName => typeof(T).Name;
        public string? LastFileHash { get; private set; }
        public virtual string BackupsLocation => Path.Combine(Paths.Base, "Backup.json");
        public virtual string FileLocation => Path.Combine(Paths.Base, $"{ClassName}.json");
        public virtual string BackupFileLocation => FileLocation + ".bak";
        public virtual string LOG_IDENT_CLASS => $"JsonManager<{ClassName}>";

        public virtual void Load(bool alertFailure = true)
        {
            string LOG_IDENT = $"{LOG_IDENT_CLASS}::Load";
            App.Logger.WriteLine(LOG_IDENT, $"Loading from {FileLocation}...");

            try
            {
                if (!File.Exists(FileLocation))
                {
                    App.Logger.WriteLine(LOG_IDENT, "File does not exist, saving defaults.");
                    Save();
                    return;
                }

                // Use async file I/O with a 64KB buffer for fewer syscalls on larger files.
                using var stream = new FileStream(
                    FileLocation,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite,
                    bufferSize: 65536,
                    useAsync: false);
                using var reader = new StreamReader(stream);
                string json = reader.ReadToEnd();

                T? settings = JsonSerializer.Deserialize<T>(json, LoadOptions);
                if (settings is null)
                    throw new InvalidOperationException("Deserialization returned null.");

                Prop = settings;

                LastFileHash = SafeGetFileHash(FileLocation);
                App.Logger.WriteLine(LOG_IDENT, "Loaded successfully!");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Failed to load!");
                App.Logger.WriteException(LOG_IDENT, ex);

                if (TryLoadBackup(LOG_IDENT, alertFailure))
                    return;

                if (alertFailure)
                {
                    Frontend.ShowMessageBox($"Failed to load settings:\n\n{ex.Message}", MessageBoxImage.Warning);

                    try
                    {
                        if (File.Exists(FileLocation))
                            File.Copy(FileLocation, BackupFileLocation, true);

                        App.Logger.WriteLine(LOG_IDENT, $"Created backup file: {BackupFileLocation}");
                    }
                    catch (Exception copyEx)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Failed to create backup file: {BackupFileLocation}");
                        App.Logger.WriteException(LOG_IDENT, copyEx);
                    }
                }

                Save();
            }
        }

        public virtual void Save()
        {
            string LOG_IDENT = $"{LOG_IDENT_CLASS}::Save";
            App.Logger.WriteLine(LOG_IDENT, $"Saving to {FileLocation}...");

            Directory.CreateDirectory(Path.GetDirectoryName(FileLocation)!);

            var options = SaveOptions;

            const int maxRetries = 5;
            const int delayMs = 100;
            int attempts = 0;

            while (true)
            {
                try
                {
                    string json = JsonSerializer.Serialize(Prop, options);

                    // Atomic-ish write: write to a temp file then move, so a crash mid-save
                    // can't corrupt the existing config. Avoids the IOException retry loop.
                    string tmp = FileLocation + ".tmp";
                    File.WriteAllText(tmp, json);
                    if (File.Exists(FileLocation))
                        File.Replace(tmp, FileLocation, BackupFileLocation);
                    else
                        File.Move(tmp, FileLocation);

                    LastFileHash = SafeGetFileHash(FileLocation);
                    App.Logger.WriteLine(LOG_IDENT, "Save complete!");
                    break;
                }
                catch (IOException ex) when ((ex.HResult & 0xFFFF) == 32 && attempts < maxRetries)
                {
                    attempts++;
                    Thread.Sleep(delayMs);
                }
                catch (UnauthorizedAccessException ex) when (attempts < maxRetries)
                {
                    attempts++;
                    Thread.Sleep(delayMs);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Failed to save");
                    App.Logger.WriteException(LOG_IDENT, ex);

                    string errorMessage = string.Format(Resources.Strings.Bootstrapper_JsonManagerSaveFailed, ClassName, ex.Message);
                    Frontend.ShowMessageBox(errorMessage, MessageBoxImage.Warning);
                    break;
                }
            }
        }

        private static string? SafeGetFileHash(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    return MD5Hash.FromStream(stream);
            }
            catch
            {
                return null;
            }
        }

        public bool HasFileOnDiskChanged()
        {
            try
            {
                string? currentHash = SafeGetFileHash(FileLocation);
                return LastFileHash != currentHash;
            }
            catch
            {
                return true;
            }
        }

        public void SaveBackup(string name)
        {
            const string LOGGER_STRING = "SaveBackup::Backups";
            string baseDir = Paths.SavedBackups;

            try
            {
                if (string.IsNullOrWhiteSpace(name))
                    return;

                Directory.CreateDirectory(baseDir);

                string filePath = ResolveBackupPath(baseDir, name);
                string json = JsonSerializer.Serialize(Prop, SaveOptions);
                File.WriteAllText(filePath, json);

                App.Logger.WriteLine(LOGGER_STRING, $"Backup '{name}' saved successfully.");
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox($"Failed to save backup:\n{ex.Message}", MessageBoxImage.Error);
            }
        }

        public void LoadBackup(string? name, bool? clearFlags)
        {
            const string LOGGER_STRING = "LoadBackup::Backups";
            string baseDir = Paths.SavedBackups;

            try
            {
                if (string.IsNullOrWhiteSpace(name))
                    return;

                string filePath = ResolveBackupPath(baseDir, name);

                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"Backup file '{name}' not found.");

                string json;
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                    json = reader.ReadToEnd();

                T? settings = JsonSerializer.Deserialize<T>(json, LoadOptions);
                if (settings is null)
                    throw new InvalidOperationException("Deserialization returned null.");

                if (clearFlags == true)
                {
                    Prop = settings;
                }
                else if (settings is IDictionary<string, object> settingsDict && Prop is IDictionary<string, object> propDict)
                {
                    foreach (var kvp in settingsDict)
                    {
                        if (kvp.Value != null)
                            propDict[kvp.Key] = kvp.Value;
                    }
                }

                App.Logger.WriteLine(LOGGER_STRING, $"Backup '{name}' loaded successfully.");
                App.FastFlags.Save();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOGGER_STRING, ex);
                Frontend.ShowMessageBox($"Failed to load backup:\n{ex.Message}", MessageBoxImage.Error);
            }
        }

        private static string ResolveBackupPath(string baseDir, string inputName)
        {
            string fileName = Path.GetFileName(inputName.Trim());

            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("Backup file name cannot be empty.", nameof(inputName));

            if (Path.GetExtension(fileName).Length == 0)
                fileName += ".json";

            var validation = PathValidator.IsFileNameValid(fileName);
            if (validation != PathValidator.ValidationResult.Ok)
                throw new InvalidOperationException($"Invalid backup file name: {fileName}");

            string fullPath = Path.GetFullPath(Path.Combine(baseDir, fileName));
            if (!SecurityHelpers.IsPathUnderDirectory(fullPath, baseDir))
                throw new InvalidOperationException("Backup path escapes the backup directory.");

            return fullPath;
        }

        private bool TryLoadBackup(string logIdent, bool alertFailure)
        {
            if (!File.Exists(BackupFileLocation))
                return false;

            try
            {
                using var stream = new FileStream(
                    BackupFileLocation,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite,
                    bufferSize: 65536,
                    useAsync: false);
                using var reader = new StreamReader(stream);
                string json = reader.ReadToEnd();

                T? settings = JsonSerializer.Deserialize<T>(json, LoadOptions);
                if (settings is null)
                    return false;

                Prop = settings;
                Save();
                App.Logger.WriteLine(logIdent, $"Recovered {ClassName} settings from backup.");

                if (alertFailure)
                    Frontend.ShowMessageBox($"Recovered {ClassName} settings from the previous backup.", MessageBoxImage.Information);

                return true;
            }
            catch (Exception backupEx)
            {
                App.Logger.WriteLine(logIdent, $"Failed to recover from backup file: {BackupFileLocation}");
                App.Logger.WriteException(logIdent, backupEx);
                return false;
            }
        }
    }
}