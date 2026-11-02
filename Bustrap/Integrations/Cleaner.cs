using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Shapes;

namespace Bustrap.Integrations
{
    public class Cleaner
    {
        public static Dictionary<string, string?> Directories = new Dictionary<string, string?> {
            { "BustrapLogs", Paths.Logs },
            { "BustrapCache", Paths.Downloads },
            { "RobloxLogs", Paths.RobloxLogs },
            { "RobloxCache", Paths.RobloxCache }
        };

        public static void DoCleaning()
        {
            const string LOG_IDENT = "Cleaner::DoCleaning";

            App.Logger.WriteLine(LOG_IDENT, "Cleaner has started");

            TimeSpan? maxAge = MaxAge(App.Settings.Prop.CleanerOptions);

            if (maxAge is null)
            {
                App.Logger.WriteLine(LOG_IDENT, "Cleaner is set to never, nothing to do");
                return;
            }

            var Threshold = DateTime.Now - maxAge.Value;

            foreach (var directory in Directories)
            {
                string? Folder = directory.Value;
                string Type = directory.Key;

                if (!App.Settings.Prop.CleanerDirectories.Contains(Type))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Skipping {Type}");
                    continue;
                }

                if (String.IsNullOrEmpty(Folder) || !Directory.Exists(Folder))
                    continue;

                try
                {
                    string[] Files = RecursivlyGetFiles(Folder);

                    App.Logger.WriteLine(LOG_IDENT, $"Running cleaner in {directory}, {Files.Length} files found");

                    foreach (string file in Files)
                    {
                        // verify file
                        if (!VerifyFile(file, Threshold))
                            continue;

                        // attempt deletion
                        try { File.Delete(file); }
                        catch (Exception ex)
                        {
                            App.Logger.WriteLine(LOG_IDENT, $"Unable to delete {file}");
                            App.Logger.WriteException(LOG_IDENT, ex);
                            continue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to clean up {Folder}");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }

            App.Logger.WriteLine(LOG_IDENT, "Cleaner finished");
        }

        /// <summary>
        /// How old a file has to be before the cleaner removes it, or null for
        /// "never". These are days. The threshold used to be built with
        /// AddHours, so "one month" deleted anything older than 30 hours -
        /// including the downloaded package cache, forcing re-downloads every
        /// couple of days.
        ///
        /// Never, and any option added without updating this switch, return
        /// null rather than a huge number. The old int.MaxValue fallback put the
        /// threshold about 245,000 years in the past, which DateTime cannot
        /// represent, so AddHours threw. The caller happened to skip Never.
        /// </summary>
        public static TimeSpan? MaxAge(CleanerOptions option) => option switch
        {
            CleanerOptions.OneDay => TimeSpan.FromDays(1),
            CleanerOptions.OneWeek => TimeSpan.FromDays(7),
            CleanerOptions.OneMonth => TimeSpan.FromDays(30),
            CleanerOptions.TwoMonths => TimeSpan.FromDays(60),
            _ => null,
        };

        private static bool VerifyFile(string file, DateTime Threshold)
        {
            // true = can be deleted
            // false = silently cancel deletion for current file
            // exception = deletion could be dangerous, cancels cleaner for current directory

            if (!File.Exists(file))
                return false;

            if (File.GetCreationTime(file) > Threshold)
                return false;

            // TODO add more safety checks?
            if (!file.Contains("Roblox") && !file.Contains(App.ProjectName) && !file.Contains(Paths.Base))
                throw new Exception($"{file} was in disallowed directory");

            if (file.Contains("Windows"))
                throw new Exception($"{file} was in Windows directory"); // we dont want any contact with windows directory
                                                                         // this will cancel the cleaner process
            return true;
        }

        private static string[] RecursivlyGetFiles(string Folder)
        {
            List<string> filesList = new List<string>();

            if (String.IsNullOrEmpty(Folder) || !Directory.Exists(Folder))
                throw new Exception("Folder was not found");

            foreach (string File in Directory.EnumerateFiles(Folder, "*.*", SearchOption.AllDirectories))
            {
                filesList.Add(File);
            }

            return filesList.ToArray();
        }

    }
}