using System.Text.Json.Nodes;
using Bustrap.Models.Persistable;
using Xunit;

namespace Bustrap.Tests
{
    /// <summary>
    /// Four settings were renamed out of names like
    /// "SmooothBARRyesirikikthxlucipook". Every config already on disk still
    /// uses the old keys, and System.Text.Json drops keys it doesn't recognise
    /// without a word - so without this migration everyone's settings quietly
    /// reset on upgrade. These tests are what keep that from regressing.
    /// </summary>
    public class AppSettingsMigrationTests
    {
        private static JsonObject Raw(string json) => JsonNode.Parse(json)!.AsObject();

        [Fact]
        public void LegacyBooleanIsCarriedOver()
        {
            var settings = new AppSettings();
            Assert.True(settings.Migrate(Raw("""{"SmooothBARRyesirikikthxlucipook": true}""")));
            Assert.True(settings.SmoothScrollBar);
        }

        [Fact]
        public void LegacyStringIsCarriedOver()
        {
            var settings = new AppSettings();
            Assert.True(settings.Migrate(Raw("""{"BufferSizeKbtes": "8192"}""")));
            Assert.Equal("8192", settings.BufferSizeKilobytes);
        }

        [Fact]
        public void LegacyFalseIsCarriedOverNotJustTrue()
        {
            var settings = new AppSettings { ShowServerUptime = true };
            Assert.True(settings.Migrate(Raw("""{"ServerUptimeBetterBLOXcuzitsbetterXD": false}""")));
            Assert.False(settings.ShowServerUptime);
        }

        [Fact]
        public void AllFourLegacyKeysAreHandled()
        {
            var settings = new AppSettings();

            Assert.True(settings.Migrate(Raw("""
            {
                "SmooothBARRyesirikikthxlucipook": true,
                "BufferSizeKbtes": "4096",
                "ServerUptimeBetterBLOXcuzitsbetterXD": false,
                "GRADmentFR": true
            }
            """)));

            Assert.True(settings.SmoothScrollBar);
            Assert.Equal("4096", settings.BufferSizeKilobytes);
            Assert.False(settings.ShowServerUptime);
            Assert.True(settings.GradientMovement);
        }

        [Fact]
        public void CurrentKeyWinsOverLegacyKey()
        {
            // deserialisation already applied the current key; the legacy one is
            // stale and must not overwrite it
            var settings = new AppSettings { GradientMovement = false };

            Assert.False(settings.Migrate(Raw("""
            {"GradientMovement": false, "GRADmentFR": true}
            """)));

            Assert.False(settings.GradientMovement);
        }

        [Fact]
        public void FileWithNoLegacyKeysIsNotRewritten()
        {
            var settings = new AppSettings();
            Assert.False(settings.Migrate(Raw("""{"Theme2": "Dark", "GradientMovement": true}""")));
        }

        [Fact]
        public void EmptyDocumentIsHarmless() =>
            Assert.False(new AppSettings().Migrate(Raw("{}")));

        [Theory]
        [InlineData("""{"GRADmentFR": "not a boolean"}""")]
        [InlineData("""{"GRADmentFR": null}""")]
        [InlineData("""{"GRADmentFR": {"nested": true}}""")]
        [InlineData("""{"BufferSizeKbtes": 4096}""")]
        public void JunkInALegacyKeyIsIgnoredRatherThanThrowing(string json)
        {
            var settings = new AppSettings();
            var before = settings.GradientMovement;

            Assert.False(settings.Migrate(Raw(json)));
            Assert.Equal(before, settings.GradientMovement);
        }
    }
}
