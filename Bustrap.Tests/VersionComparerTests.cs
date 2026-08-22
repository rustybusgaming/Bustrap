using Bustrap.Utility;
using Xunit;

namespace Bustrap.Tests
{
    /// <summary>
    /// Guards the check that decides whether an update is offered. When this is
    /// wrong the app stops updating and the failure is completely silent.
    /// </summary>
    public class VersionComparerTests
    {
        [Theory]
        [InlineData("1.1.0.6", "1.1.0.5")]
        [InlineData("v1.1.0.6", "1.1.0.5")]
        [InlineData("V1.1.0.6", "1.1.0.5")]
        [InlineData("1.2.0.0", "1.1.9.9")]
        [InlineData("2.0.0.0", "1.99.99.99")]
        [InlineData("1.1.0.10", "1.1.0.9")]
        public void NewerRemoteTagIsAnUpdate(string remote, string local) =>
            Assert.True(VersionComparer.IsNewer(remote, local));

        [Theory]
        [InlineData("1.1.0.5", "1.1.0.5")]
        [InlineData("v1.1.0.5", "1.1.0.5")]
        [InlineData("1.1.0.4", "1.1.0.5")]
        [InlineData("1.0.9.9", "1.1.0.0")]
        [InlineData("0.9.0.0", "1.0.0.0")]
        public void SameOrOlderRemoteTagIsNotAnUpdate(string remote, string local) =>
            Assert.False(VersionComparer.IsNewer(remote, local));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void MissingRemoteTagIsNotAnUpdate(string? remote) =>
            Assert.False(VersionComparer.IsNewer(remote, "1.1.0.5"));

        [Fact]
        public void MissingLocalVersionIsTreatedAsZero() =>
            Assert.True(VersionComparer.IsNewer("1.0.0.0", null));

        [Fact]
        public void ShorterVersionsStillCompareNumerically()
        {
            Assert.True(VersionComparer.IsNewer("1.2", "1.1"));
            Assert.False(VersionComparer.IsNewer("1.1", "1.2"));
        }
    }
}
