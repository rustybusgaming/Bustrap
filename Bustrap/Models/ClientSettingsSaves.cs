using System;
using System.IO;
using System.Text.Json;
using Bustrap;

public static class BustrapRobloxSettingsManager // lowk didnt know what tf to name this file
{
    public class BustrapRobloxSettings
    {
        public int MemoryCleanerIntervalSeconds { get; set; }
    }

    private static readonly string FolderPath = Paths.Base;

    private static readonly string FilePath =
        Path.Combine(FolderPath, "BustrapRobloxSaves.json");

    public static BustrapRobloxSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new BustrapRobloxSettings();

            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<BustrapRobloxSettings>(json)
                   ?? new BustrapRobloxSettings();
        }
        catch
        {
            return new BustrapRobloxSettings();
        }
    }

    public static void Save(BustrapRobloxSettings settings)
    {
        try
        {
            if (!Directory.Exists(FolderPath))
                Directory.CreateDirectory(FolderPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
        }
    }
}
