using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Nodes;
using Bustrap.Enums;

namespace Bustrap.Models.Persistable
{
    /// <summary>
    /// Represents configuration settings for Bustrap.
    /// </summary>
    public class AppSettings : IMigratableSettings
    {
        // General Configuration
        public BootstrapperStyle BootstrapperStyle { get; set; } = BootstrapperStyle.FluentAeroDialog;
        public BootstrapperIcon BootstrapperIcon { get; set; } = BootstrapperIcon.IconBustrap;
        public CleanerOptions CleanerOptions { get; set; } = CleanerOptions.Never;
        public List<string> CleanerDirectories { get; set; } = new List<string>();
        public string BootstrapperTitle { get; set; } = App.ProjectName;
        public string BootstrapperIconCustomLocation { get; set; } = "";
        public Theme Theme2 { get; set; } = Theme.Dark;
        public string? SelectedCustomTheme { get; set; } = null;
        public bool CheckForUpdates { get; set; } = true;
        public string SelectedCpuPriority { get; set; } = "Automatic";
        public bool IsChannelEnabled { get; set; } = false;
        public bool UpdateRoblox { get; set; } = true;

        public int CleanRobloxNumber = 0;
        public bool DisableCrash { get; set; } = false;
        public int CpuCoreLimit { get; set; } = Environment.ProcessorCount;
        public string ShiftlockCursorSelectedPath { get; set; } = "";
        public string UseCustomIcon { get; set; } = "";
        public string CustomGameName { get; set; } = "";
        public string PriorityLimit { get; set; } = "Normal";
        public string ArrowCursorSelectedPath { get; set; } = "";
        public string ArrowFarCursorSelectedPath { get; set; } = "";
        public string IBeamCursorSelectedPath { get; set; } = "";

        public bool EnableAnalytics { get; set; } = true;
        public bool UseFastFlagManager { get; set; } = true;
        public bool WPFSoftwareRender { get; set; } = false;
        public bool ConfirmLaunches { get; set; } = true;

        public bool SmoothScrollBar { get; set; } = false; // wanna keep this on false so people may not be annoyed by it being on
        public bool NotificationWindowShow { get; set; }
        public bool SongChangeNotification { get; set; } = true;
        public bool BackgroundWindow { get; set; } = true;
        public bool UsePlaceId { get; set; } = false;
        public bool ClearFont { get; set; } = false;
        public bool AniWatch { get; set; } = false;

        public bool Fleasion { get; set; } = false;
        public string PlaceId { get; set; } = "";
        public bool OptimizeRoblox { get; set; } = false;
        public bool VoidNotify { get; set; } = true;
        public bool ServerPingCounter { get; set; } = false;
        public bool ShowServerDetailsUI { get; set; } = false;
        public bool RenameClientToEuroTrucks2 { get; set; } = false;

        public bool MotionBlurOverlay { get; set; } = false;


        public string Locale { get; set; } = "nil";
        public string BufferSizeKbte { get; set; } = "1024";
        public string BufferSizeKilobytes { get; set; } = "2048";
        public string SkyboxName { get; set; } = "Default";
        public string LastServerSave { get; set; } = "112757576021097";
        public bool SkyBoxDataSending { get; set; } = false;

        public bool FFlagRPCDisplayer { get; set; } = true;

        public bool FPSCounter { get; set; } = false;

        public bool CurrentTimeDisplay { get; set; } = false;
        public bool ExclusiveFullscreen { get; set; } = false;
        public bool Crosshair { get; set; } = false;
        public bool LockDefault { get; set; } = false;
        public bool GameWIP { get; set; } = false;
        public bool ForceRobloxLanguage { get; set; } = true;

        public bool IngameChatDiscord { get; set; } = false;

        // Analytics & Tracking
        public bool EnableActivityTracking { get; set; } = true;
        public bool ShowServerUptime { get; set; } = true;

        public string DownloadingStringFormat { get; set; } = Strings.Bootstrapper_Status_Downloading + " {0} - {1}MB / {2}MB";

        public bool Fullbright { get; set; } = false;

        public bool GameIconChecked { get; set; } = true;
        public bool ServerLocationGame { get; set; } = false;
        public bool GameNameChecked { get; set; } = true;
        public bool GameCreatorChecked { get; set; } = true;
        public bool GameStatusChecked { get; set; } = true;

        // Rich Presence (Discord Integration)
        public bool UseDiscordRichPresence { get; set; } = true;
        public bool HideRPCButtons { get; set; } = true;
        public bool ShowAccountOnRichPresence { get; set; } = true;
        public bool MultiAccount { get; set; } = false;
        public bool ShowServerDetails { get; set; } = true;

        public bool OverlaysEnabled { get; set; } = false;
        public bool SwiftTunnelEnabled { get; set; } = false;
        public string SwiftTunnelRegion { get; set; } = "auto";
        public bool SwiftTunnelAutoConnect { get; set; } = false;
        public bool SwiftTunnelSplitTunnel { get; set; } = false;
        public bool SwiftTunnelRememberLogin { get; set; } = false;

        public double Brightness { get; set; } = 50;

        // Mod Settings
        public CursorType CursorType { get; set; } = CursorType.Default;

        // Custom Integrations
        public ObservableCollection<CustomIntegration> CustomIntegrations { get; set; } = new();

        // Mod Preset Configuration
        public bool UseDisableAppPatch { get; set; } = false;

        // Roblox Deployment Settings
        public string Channel { get; set; } = RobloxInterfaces.Deployment.DefaultChannel;

        public string LaunchGameID { get; set; } = "";
        public bool IsGameEnabled { get; set; } = false;
        public bool MatchUniverseId { get; set; } = true;
        public long? TargetUniverseId { get; set; }
        public bool IsBetterServersEnabled { get; set; } = false;
        public bool GradientMovement { get; set; } = false;
        public bool VoidRPC { get; set; } = true;


        public ResolutionSetting? InGameResolution { get; set; }

        /// <summary>
        /// Values written by builds that used the old property names. Without
        /// this a rename silently resets the setting for everyone who upgrades.
        /// </summary>
        public bool Migrate(JsonObject raw)
        {
            bool changed = false;

            changed |= CarryOver<bool>(raw, "SmooothBARRyesirikikthxlucipook",
                nameof(SmoothScrollBar), value => SmoothScrollBar = value);

            changed |= CarryOver<string>(raw, "BufferSizeKbtes",
                nameof(BufferSizeKilobytes), value => BufferSizeKilobytes = value);

            changed |= CarryOver<bool>(raw, "ServerUptimeBetterBLOXcuzitsbetterXD",
                nameof(ShowServerUptime), value => ShowServerUptime = value);

            changed |= CarryOver<bool>(raw, "GRADmentFR",
                nameof(GradientMovement), value => GradientMovement = value);

            return changed;
        }

        private static bool CarryOver<TValue>(JsonObject raw, string legacyName, string currentName, Action<TValue> apply)
        {
            // a file already written by a current build wins - the legacy key
            // may still be sitting there from a config that was hand-edited
            if (raw.ContainsKey(currentName))
                return false;

            if (!raw.TryGetPropertyValue(legacyName, out JsonNode? node) || node is null)
                return false;

            try
            {
                // throws if the stored value isn't the type the property expects
                apply(node.GetValue<TValue>());
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException($"AppSettings::CarryOver({legacyName})", ex);
                return false;
            }
        }

        public class ResolutionSetting
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public int RefreshRate { get; set; }
        }
    }
}