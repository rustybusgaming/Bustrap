using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Bustrap
{
    public static class CpuCoreLimiter
    {
        /// <summary>
        /// Affinity mask selecting the first <paramref name="coreCount"/> logical
        /// processors.
        ///
        /// Written as a function because the obvious one-liner,
        /// <c>(1L &lt;&lt; coreCount) - 1</c>, is wrong at 64: C# masks the shift
        /// count to six bits, so <c>1L &lt;&lt; 64</c> is 1 and the mask comes out as
        /// zero - which Windows rejects. A mask also only covers one processor
        /// group, so anything from 64 up means "all of them".
        /// </summary>
        public static long AffinityMask(int coreCount)
        {
            if (coreCount < 1)
                coreCount = 1;

            return coreCount >= 64 ? -1L : (1L << coreCount) - 1;
        }

        /// <summary>
        /// Limits the current process to use only the specified number of CPU cores.
        /// </summary>
        /// <param name="coreCount">Number of CPU cores to allow (minimum 1, maximum number of logical processors).</param>
        public static void SetCpuCoreLimit(int coreCount)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Console.WriteLine("CPU affinity setting is only supported on Windows.");
                return;
            }

            int maxCores = Environment.ProcessorCount;
            if (coreCount < 1)
                coreCount = 1;
            else if (coreCount > maxCores)
                coreCount = maxCores;

            try
            {
                long affinityMask = AffinityMask(coreCount);

                Process currentProcess = Process.GetCurrentProcess();
                currentProcess.ProcessorAffinity = (IntPtr)affinityMask;

                Console.WriteLine($"CPU affinity successfully set to {coreCount} core(s).");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to set CPU affinity.");
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}
