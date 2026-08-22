using System;
using System.IO;
using System.Text;
using Bustrap.Utility;
using Xunit;

namespace Bustrap.Tests
{
    /// <summary>
    /// Downloaded Roblox packages are accepted or rejected by comparing this
    /// against the signature in the deployment manifest, so the exact output
    /// format matters as much as the hash itself.
    /// </summary>
    public class MD5HashTests
    {
        // the well-known MD5 of the empty input and of "abc"
        private const string EmptyHash = "d41d8cd98f00b204e9800998ecf8427e";
        private const string AbcHash = "900150983cd24fb0d6963f7d28e17f72";

        [Fact]
        public void HashesKnownBytes()
        {
            Assert.Equal(EmptyHash, MD5Hash.FromBytes(Array.Empty<byte>()));
            Assert.Equal(AbcHash, MD5Hash.FromBytes(Encoding.ASCII.GetBytes("abc")));
        }

        [Fact]
        public void OutputIsLowercaseHexWithoutSeparators()
        {
            string hash = MD5Hash.FromBytes(Encoding.ASCII.GetBytes("abc"));

            Assert.Equal(32, hash.Length);
            Assert.DoesNotContain("-", hash);
            Assert.Equal(hash.ToLowerInvariant(), hash);
        }

        [Fact]
        public void StreamIsHashedFromTheStartNotTheCurrentPosition()
        {
            using var stream = new MemoryStream(Encoding.ASCII.GetBytes("abc"));
            stream.Seek(2, SeekOrigin.Begin);

            Assert.Equal(AbcHash, MD5Hash.FromStream(stream));
        }

        [Fact]
        public void FileAndBytesAgree()
        {
            string path = Path.Combine(Path.GetTempPath(), $"bustrap-md5-{Guid.NewGuid():N}.bin");
            byte[] data = Encoding.ASCII.GetBytes("the quick brown fox");

            try
            {
                File.WriteAllBytes(path, data);
                Assert.Equal(MD5Hash.FromBytes(data), MD5Hash.FromFile(path));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
