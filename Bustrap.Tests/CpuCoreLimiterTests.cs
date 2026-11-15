using Xunit;

namespace Bustrap.Tests
{
    /// <summary>
    /// The mask was built as (1L &lt;&lt; cores) - 1. C# masks a long shift to six
    /// bits, so at 64 that is (1L &lt;&lt; 0) - 1 = 0: an empty affinity mask, which
    /// Windows refuses. These pin the edges down.
    /// </summary>
    public class CpuCoreLimiterTests
    {
        [Theory]
        [InlineData(1, 0b1L)]
        [InlineData(2, 0b11L)]
        [InlineData(4, 0b1111L)]
        [InlineData(8, 0xFFL)]
        [InlineData(32, 0xFFFF_FFFFL)]
        [InlineData(63, long.MaxValue)]
        public void SelectsTheFirstNCores(int cores, long expected) =>
            Assert.Equal(expected, CpuCoreLimiter.AffinityMask(cores));

        [Theory]
        [InlineData(64)]
        [InlineData(65)]
        [InlineData(128)]
        public void SixtyFourOrMoreMeansEveryCore(int cores) =>
            Assert.Equal(-1L, CpuCoreLimiter.AffinityMask(cores));

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void NeverProducesAnEmptyMask(int cores) =>
            Assert.Equal(1L, CpuCoreLimiter.AffinityMask(cores));

        [Fact]
        public void MaskIsNeverZero()
        {
            for (int cores = -4; cores <= 256; cores++)
                Assert.NotEqual(0L, CpuCoreLimiter.AffinityMask(cores));
        }
    }
}
