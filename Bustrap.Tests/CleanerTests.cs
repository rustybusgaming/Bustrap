using System;
using Bustrap.Enums;
using Bustrap.Integrations;
using Xunit;

namespace Bustrap.Tests
{
    /// <summary>
    /// The cleaner's options are labelled in days, but the threshold used to be
    /// built with AddHours - "one month" deleted anything older than 30 hours,
    /// including the downloaded package cache. These pin the units down.
    /// </summary>
    public class CleanerTests
    {
        [Theory]
        [InlineData(CleanerOptions.OneDay, 1)]
        [InlineData(CleanerOptions.OneWeek, 7)]
        [InlineData(CleanerOptions.OneMonth, 30)]
        [InlineData(CleanerOptions.TwoMonths, 60)]
        public void AgeIsMeasuredInDays(CleanerOptions option, int days) =>
            Assert.Equal(TimeSpan.FromDays(days), Cleaner.MaxAge(option));

        [Fact]
        public void OneMonthIsNotThirtyHours() =>
            Assert.NotEqual(TimeSpan.FromHours(30), Cleaner.MaxAge(CleanerOptions.OneMonth));

        [Fact]
        public void NeverMeansNoCleaning() =>
            Assert.Null(Cleaner.MaxAge(CleanerOptions.Never));

        [Fact]
        public void UnknownOptionIsTreatedAsNeverRatherThanHuge() =>
            // An option added later without updating the switch must not fall
            // back to a value that overflows DateTime.
            Assert.Null(Cleaner.MaxAge((CleanerOptions)999));

        [Theory]
        [InlineData(CleanerOptions.OneDay)]
        [InlineData(CleanerOptions.OneWeek)]
        [InlineData(CleanerOptions.OneMonth)]
        [InlineData(CleanerOptions.TwoMonths)]
        public void ThresholdIsRepresentable(CleanerOptions option)
        {
            // the old int.MaxValue path threw here
            var threshold = DateTime.Now - Cleaner.MaxAge(option)!.Value;
            Assert.True(threshold < DateTime.Now);
        }
    }
}
