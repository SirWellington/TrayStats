using System.Runtime.InteropServices;

namespace TrayStats.Services;

/// <summary>Maps logical processors to physical cores via GetLogicalProcessorInformationEx.</summary>
internal static class CpuTopology
{
    private const int RelationProcessorCore = 0;
    private const byte LtpPcSmt = 0x1;

    public sealed record Info(bool[] IsPPerLp, int[][] LpsOfCore)
    {
        public static readonly Info Unknown = new(
            Enumerable.Repeat(true, Environment.ProcessorCount).ToArray(),
            Array.Empty<int[]>());
    }

    /// <summary>
    /// Returns per-LP core type (true = SMT/performance core) and the LP list of each physical core.
    /// Falls back to "all P-cores" when the API is unavailable or returns inconsistent data.
    /// </summary>
    public static Info Get()
    {
        int lpCount = Environment.ProcessorCount;
        const int cap = 65536;
        uint len = (uint)cap;
        IntPtr buf = Marshal.AllocHGlobal(cap);
        try
        {
            if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buf, ref len))
                return Info.Unknown;

            var isPPerLp = new bool[lpCount];
            Array.Fill(isPPerLp, true);
            var covered = new bool[lpCount];
            var lpsOfCore = new List<int[]>();
            var smtOfCore = new List<bool>();
            var efficiencyOfCore = new List<byte>();

            int offset = 0;
            while (offset + 8 <= len)
            {
                uint size = (uint)Marshal.ReadInt32(buf, offset + 4);
                if (size < 48 || offset + size > len) break;

                byte flags = Marshal.ReadByte(buf, offset + 8);
                byte efficiencyClass = Marshal.ReadByte(buf, offset + 9);
                // GROUP_AFFINITY.Mask (KAFFINITY) sits at entry offset +32 in the current layout.
                ulong maskLo = (ulong)(uint)Marshal.ReadInt32(buf, offset + 32);
                ulong maskHi = (ulong)(uint)Marshal.ReadInt32(buf, offset + 36);

                var lps = new List<int>();
                for (int b = 0; b < lpCount && b < 64; b++)
                {
                    if (((maskLo | (maskHi << 32)) & (1UL << b)) == 0) continue;
                    if (covered[b]) return Info.Unknown;
                    covered[b] = true;
                    lps.Add(b);
                }

                if (lps.Count == 0) return Info.Unknown;
                lpsOfCore.Add([.. lps]);
                smtOfCore.Add((flags & LtpPcSmt) != 0);
                efficiencyOfCore.Add(efficiencyClass);
                offset += (int)size;
            }

            if (offset != len || !covered.All(c => c))
                return Info.Unknown;

            byte maxEfficiency = efficiencyOfCore.Max();
            bool mixedEfficiency = efficiencyOfCore.Any(e => e != maxEfficiency);
            bool mixedSmt = smtOfCore.Any(s => s) && smtOfCore.Any(s => !s);
            for (int core = 0; core < lpsOfCore.Count; core++)
            {
                // Efficiency classes distinguish hybrid cores; SMT is a fallback on hybrid
                // CPUs that don't expose distinct classes. Uniform CPUs are all performance cores.
                bool isP = mixedEfficiency ? efficiencyOfCore[core] == maxEfficiency :
                    !mixedSmt || smtOfCore[core];
                foreach (int lp in lpsOfCore[core]) isPPerLp[lp] = isP;
            }

            return new Info(isPPerLp, [.. lpsOfCore]);
        }
        catch
        {
            return Info.Unknown;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint returnLength);
}
