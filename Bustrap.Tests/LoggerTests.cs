using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Bustrap.Tests
{
    /// <summary>
    /// The logger is written to from every thread in the app. Its history used
    /// to be a bare List mutated without a lock, which under concurrent writes
    /// loses entries, corrupts the list or throws from inside the caller.
    /// </summary>
    public class LoggerTests
    {
        [Fact]
        public void ConcurrentWritesDoNotCorruptHistory()
        {
            var logger = new Logger();

            Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 16 }, worker =>
            {
                for (int i = 0; i < 2000; i++)
                    logger.WriteLine("LoggerTests", $"worker {worker} line {i}");
            });

            // capped, and every surviving entry intact
            Assert.Equal(150, logger.History.Count);
            Assert.All(logger.History, line => Assert.Contains("[LoggerTests] worker ", line));
        }

        [Fact]
        public void HistoryKeepsTheMostRecentEntries()
        {
            var logger = new Logger();

            for (int i = 0; i < 500; i++)
                logger.WriteLine("LoggerTests", $"line {i}");

            Assert.Equal(150, logger.History.Count);
            Assert.EndsWith("line 350", logger.History.First());
            Assert.EndsWith("line 499", logger.History.Last());
        }

        [Fact]
        public async Task DocumentCanBeReadWhileWriting()
        {
            var logger = new Logger();

            var writer = Task.Run(() =>
            {
                for (int i = 0; i < 20000; i++)
                    logger.WriteLine("LoggerTests", $"line {i}");
            });

            while (!writer.IsCompleted)
                _ = logger.AsDocument;

            await writer;
            Assert.Equal(150, logger.AsDocument.Split('\n').Length);
        }
    }
}
