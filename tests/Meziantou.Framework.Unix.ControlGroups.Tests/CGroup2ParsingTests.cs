namespace Meziantou.Framework.Unix.ControlGroups.Tests;

/// <summary>The cgroup file parsers are pure functions, so these run on every OS without privileges.</summary>
public sealed class CGroup2ParsingTests
{
    [Fact]
    public void CpuStat_ShouldParseEveryKnownKey()
    {
        var stat = CpuStat.Parse("""
            usage_usec 123456
            user_usec 78000
            system_usec 45456
            nr_periods 10
            nr_throttled 2
            throttled_usec 5000
            nr_bursts 1
            burst_usec 250
            """);

        Assert.Equal(123456, stat.UsageMicroseconds);
        Assert.Equal(78000, stat.UserMicroseconds);
        Assert.Equal(45456, stat.SystemMicroseconds);
        Assert.Equal(10, stat.NumberOfPeriods);
        Assert.Equal(2, stat.NumberOfThrottled);
        Assert.Equal(5000, stat.ThrottledMicroseconds);
        Assert.Equal(1, stat.NumberOfBursts);
        Assert.Equal(250, stat.BurstMicroseconds);
    }

    [Fact]
    public void CpuStat_ShouldReportNullForBandwidthKeysThatAreAbsent()
    {
        // What a cgroup without cpu.max configured actually reports.
        var stat = CpuStat.Parse("usage_usec 100\nuser_usec 60\nsystem_usec 40\n");

        Assert.Equal(100, stat.UsageMicroseconds);
        Assert.Null(stat.NumberOfPeriods);
        Assert.Null(stat.NumberOfThrottled);
        Assert.Null(stat.ThrottledMicroseconds);
        Assert.Null(stat.NumberOfBursts);
        Assert.Null(stat.BurstMicroseconds);
    }

    [Fact]
    public void CpuStat_ShouldIgnoreUnknownAndMalformedLines()
    {
        var stat = CpuStat.Parse("some_future_key 42\nusage_usec 7\nnot_a_pair\nuser_usec not_a_number\n");

        Assert.Equal(7, stat.UsageMicroseconds);
        Assert.Equal(0, stat.UserMicroseconds);
    }

    [Fact]
    public void MemoryStat_ShouldParseTheCommonKeys()
    {
        var stat = MemoryStat.Parse("""
            anon 1048576
            file 2097152
            kernel 65536
            kernel_stack 16384
            pagetables 32768
            percpu 4096
            sock 8192
            swapcached 512
            file_mapped 1024
            file_dirty 2048
            file_writeback 256
            inactive_anon 128
            active_anon 64
            inactive_file 32
            active_file 16
            unevictable 8
            slab_reclaimable 4
            slab_unreclaimable 2
            slab 6
            pswpin 11
            pswpout 12
            pgfault 13
            pgmajfault 14
            """);

        Assert.Equal(1048576, stat.Anon);
        Assert.Equal(2097152, stat.File);
        Assert.Equal(65536, stat.Kernel);
        Assert.Equal(512, stat.SwapCached);
        Assert.Equal(6, stat.Slab);
        Assert.Equal(11, stat.PageSwapIn);
        Assert.Equal(12, stat.PageSwapOut);
        Assert.Equal(13, stat.PageFault);
        Assert.Equal(14, stat.PageMajorFault);
    }

    [Fact]
    public void MemoryStat_ShouldReportZeroForKeysTheKernelDoesNotEmit()
    {
        // Documents a known limitation: 'kernel' (Linux 6.0+) and 'swapcached' (5.13+) are non-nullable,
        // so an older kernel that never reports them is indistinguishable from one reporting zero bytes.
        var stat = MemoryStat.Parse("anon 4096\nfile 8192\n");

        Assert.Equal(4096, stat.Anon);
        Assert.Equal(0, stat.Kernel);
        Assert.Equal(0, stat.SwapCached);
        Assert.Null(stat.PageFault);
    }

    [Fact]
    public void MemoryStat_ShouldReportZeroForValuesLargerThanInt64()
    {
        // Documents a known limitation: memory.stat counters are u64, but they are parsed into long,
        // so a value above long.MaxValue silently leaves the field at its default.
        var stat = MemoryStat.Parse("anon 18446744073709551615\n");

        Assert.Equal(0, stat.Anon);
    }

    [Theory]
    [InlineData("0-3,6,8-10", new[] { 0, 1, 2, 3, 6, 8, 9, 10 })]
    [InlineData("5", new[] { 5 })]
    [InlineData("0-2", new[] { 0, 1, 2 })]
    [InlineData("3,1,2", new[] { 3, 1, 2 })]
    [InlineData("", new int[0])]
    [InlineData("   ", new int[0])]
    [InlineData("5-3", new int[0])]
    [InlineData("bogus", new int[0])]
    public void ParseCpuList_ShouldExpandRanges(string cpuList, int[] expected)
    {
        Assert.Equal(expected, CGroup2.ParseCpuList(cpuList));
    }

    [Theory]
    [InlineData(new[] { 0, 1, 2 }, "0-2")]
    [InlineData(new[] { 0, 2, 4 }, "0,2,4")]
    [InlineData(new[] { 8, 9, 10, 0, 1 }, "0-1,8-10")]
    [InlineData(new[] { 5 }, "5")]
    [InlineData(new int[0], "")]
    public void ConvertToRanges_ShouldCollapseConsecutiveNumbers(int[] numbers, string expected)
    {
        Assert.Equal(expected, CGroup2.ConvertToRanges(numbers));
    }

    [Fact]
    public void ConvertToRanges_ShouldRoundTripThroughParseCpuList()
    {
        int[] cpus = [0, 1, 2, 5, 7, 8];
        Assert.Equal(cpus, CGroup2.ParseCpuList(CGroup2.ConvertToRanges(cpus)));
    }

    [Fact]
    public void ConvertToRanges_ShouldEmitDuplicatesForDuplicateInput()
    {
        // Documents a known quirk: the input is not de-duplicated, so [1, 1, 2] yields "1,1-2".
        Assert.Equal("1,1-2", CGroup2.ConvertToRanges([1, 1, 2]));
    }
}
