using System;
using System.IO;
using Bustrap.Utility;
using Xunit;

namespace Bustrap.Tests
{
    /// <summary>
    /// These two guards are what stop a malicious archive writing outside the
    /// extraction directory, and stop the updater fetching a build from
    /// somewhere other than GitHub.
    /// </summary>
    public class SecurityHelpersTests
    {
        private static readonly string Root = Path.Combine(Path.GetTempPath(), "BustrapTests", "root");

        [Theory]
        [InlineData("file.txt")]
        [InlineData("nested/file.txt")]
        [InlineData("nested\\deeper\\file.txt")]
        public void PathsInsideTheDirectoryAreAllowed(string relative)
        {
            string combined = SecurityHelpers.CombineUnderDirectory(Root, relative);
            Assert.True(SecurityHelpers.IsPathUnderDirectory(combined, Root));
        }

        [Theory]
        [InlineData("../escaped.txt")]
        [InlineData("..\\escaped.txt")]
        [InlineData("nested/../../escaped.txt")]
        [InlineData("nested/../../../../../../escaped.txt")]
        public void TraversalOutOfTheDirectoryIsBlocked(string relative) =>
            Assert.Throws<InvalidOperationException>(
                () => SecurityHelpers.CombineUnderDirectory(Root, relative));

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void EmptyRelativePathIsRejected(string relative) =>
            Assert.Throws<ArgumentException>(
                () => SecurityHelpers.CombineUnderDirectory(Root, relative));

        [Fact]
        public void SiblingDirectoryIsNotConsideredInside()
        {
            // "root-other" starts with "root" as a string but is a different folder
            string sibling = Path.Combine(Path.GetDirectoryName(Root)!, "root-other", "file.txt");
            Assert.False(SecurityHelpers.IsPathUnderDirectory(sibling, Root));
        }

        [Theory]
        [InlineData("https://github.com/owner/repo/releases/download/v1/App.exe")]
        [InlineData("https://objects.githubusercontent.com/thing")]
        public void AllowedHttpsHostsPass(string url) =>
            Assert.Equal(new Uri(url).Host,
                SecurityHelpers.ValidateRemoteHttpsUrl(url, "github.com", "objects.githubusercontent.com").Host);

        [Theory]
        [InlineData("http://github.com/owner/repo")]          // not https
        [InlineData("https://evil.example.com/App.exe")]      // host not allowed
        [InlineData("https://github.com.evil.example.com/x")] // suffix trick
        [InlineData("ftp://github.com/App.exe")]
        public void DisallowedUrlsAreRejected(string url) =>
            Assert.Throws<InvalidOperationException>(
                () => SecurityHelpers.ValidateRemoteHttpsUrl(url, "github.com", "objects.githubusercontent.com"));

        [Fact]
        public void MalformedUrlIsRejected() =>
            Assert.Throws<InvalidOperationException>(
                () => SecurityHelpers.ValidateRemoteHttpsUrl("not a url", "github.com"));
    }
}
