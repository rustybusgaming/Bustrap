namespace Bustrap.Utility
{
    /// <summary>
    /// Decides whether a release tag from GitHub is newer than the running
    /// build. Pulled out of the bootstrapper so it can be tested on its own -
    /// when this is wrong, updates silently stop happening and nobody notices.
    /// </summary>
    public static class VersionComparer
    {
        /// <param name="remoteTag">Release tag, with or without a leading "v".</param>
        /// <param name="localVersion">The running assembly version.</param>
        public static bool IsNewer(string? remoteTag, string? localVersion)
        {
            if (string.IsNullOrWhiteSpace(remoteTag))
                return false;

            remoteTag = remoteTag.TrimStart('v', 'V');
            localVersion = string.IsNullOrWhiteSpace(localVersion) ? "0.0.0" : localVersion;

            if (Version.TryParse(localVersion, out var local) && Version.TryParse(remoteTag, out var remote))
                return remote > local;

            // Tags that aren't plain version numbers ("1.2.3-beta") fall back to
            // a text comparison. It is not a correct ordering - "10.0" sorts
            // below "9.0" - but it is what shipped, so it stays until releases
            // are known to be tagged consistently.
            return string.Compare(remoteTag, localVersion, StringComparison.OrdinalIgnoreCase) > 0;
        }
    }
}
