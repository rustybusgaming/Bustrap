namespace Bustrap.Utility
{
    internal static class SecurityHelpers
    {
        public static bool IsPathUnderDirectory(string candidatePath, string directoryPath)
        {
            string fullDirectory = Path.GetFullPath(directoryPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            string fullCandidate = Path.GetFullPath(candidatePath);

            return fullCandidate.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }

        public static string CombineUnderDirectory(string directoryPath, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("Path cannot be empty.", nameof(relativePath));

            string normalizedRelative = relativePath
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);

            string combined = Path.GetFullPath(Path.Combine(directoryPath, normalizedRelative));

            if (!IsPathUnderDirectory(combined, directoryPath))
                throw new InvalidOperationException($"Blocked path traversal attempt: {relativePath}");

            return combined;
        }

        public static Uri ValidateRemoteHttpsUrl(string url, params string[] allowedHosts)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                throw new InvalidOperationException($"Invalid URL: {url}");

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Only HTTPS URLs are allowed: {url}");

            if (allowedHosts.Length > 0 &&
                !allowedHosts.Any(host => string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Host is not allowed: {uri.Host}");
            }

            return uri;
        }
    }
}
