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
    public void MemoryStat_ShouldReportNullForKeysTheKernelDoesNotEmit()
    {
        // 'kernel' needs Linux 6.0+ and 'swapcached' needs 5.13+, so an older kernel reports neither.
        // That must be distinguishable from a cgroup genuinely using zero bytes.
        var stat = MemoryStat.Parse("anon 4096\nfile 8192\n");

        Assert.Equal(4096, stat.Anon);
        Assert.Null(stat.Kernel);
        Assert.Null(stat.SwapCached);
        Assert.Null(stat.PageFault);
    }

    [Fact]
    public void MemoryStat_ShouldReportZeroWhenTheKernelReportsZero()
    {
        var stat = MemoryStat.Parse("anon 0\nkernel 0\n");

        Assert.Equal(0, stat.Anon);
        Assert.Equal(0, stat.Kernel);
    }

    [Fact]
    public void MemoryStat_ShouldReportNullForValuesLargerThanInt64()
    {
        // Documents a known limitation: memory.stat counters are u64, but they are parsed into long,
        // so a value above long.MaxValue is treated as if the kernel had not reported it.
        var stat = MemoryStat.Parse("anon 18446744073709551615\n");

        Assert.Null(stat.Anon);
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

    [Fact]
    public void ParseLimit_ShouldTellTheFourOutcomesApart()
    {
        // The point of CGroupValue: a caller deciding whether to apply its own limit must not confuse
        // "the controller is not enabled" with "memory is unlimited".
        Assert.Equal(CGroupValueState.Unavailable, CGroup2.ParseLimit(null).State);
        Assert.Equal(CGroupValueState.NotConfigured, CGroup2.ParseLimit("max\n").State);
        Assert.Equal(CGroupValueState.Configured, CGroup2.ParseLimit("1073741824\n").State);
        Assert.Equal(CGroupValueState.Invalid, CGroup2.ParseLimit("something-new\n").State);
    }

    [Fact]
    public void ParseLimit_ShouldReturnTheValue()
    {
        var value = CGroup2.ParseLimit("1073741824\n");

        Assert.Equal(1073741824, value.Value);
        Assert.Equal("1073741824", value.RawValue);
    }

    [Fact]
    public void ParseLimit_ShouldKeepTheRawValueOfContentItCannotParse()
    {
        // A kernel format change must be diagnosable, so the unparsed content is kept.
        var value = CGroup2.ParseLimit(" something-new \n");

        Assert.Equal(CGroupValueState.Invalid, value.State);
        Assert.Equal("something-new", value.RawValue);
    }

    [Theory]
    [InlineData("42\n", 42L)]
    [InlineData(" 0 ", 0L)]
    public void ParseInt64_ShouldParseNumbers(string content, long expected)
    {
        Assert.Equal(expected, CGroup2.ParseInt64(content).Value);
    }

    [Fact]
    public void ParseInt64_ShouldNotTreatMaxAsNotConfigured()
    {
        // Counters such as memory.current never report "max", so it is invalid content rather than "no limit".
        Assert.Equal(CGroupValueState.Invalid, CGroup2.ParseInt64("max\n").State);
    }

    [Fact]
    public void ParseInt32_ShouldParseNumbers()
    {
        Assert.Equal(200, CGroup2.ParseInt32("200\n").Value);
        Assert.Equal(CGroupValueState.Invalid, CGroup2.ParseInt32("99999999999\n").State);
        Assert.Equal(CGroupValueState.Unavailable, CGroup2.ParseInt32(null).State);
    }

    [Fact]
    public void ParseCpuMax_ShouldParseAQuotaAndAPeriod()
    {
        var value = CGroup2.ParseCpuMax("50000 100000\n");

        Assert.Equal(CGroupValueState.Configured, value.State);
        Assert.Equal(new CpuMax(50000, 100000), value.Value);
    }

    [Fact]
    public void ParseCpuMax_ShouldKeepThePeriodWhenTheQuotaIsUnlimited()
    {
        // The period stays meaningful without a quota, so an unlimited cpu.max is Configured with a null quota
        // rather than NotConfigured, which would throw the period away.
        var value = CGroup2.ParseCpuMax("max 50000\n");

        Assert.Equal(CGroupValueState.Configured, value.State);
        Assert.Null(value.Value.MaxMicroseconds);
        Assert.Equal(50000, value.Value.PeriodMicroseconds);
    }

    [Theory]
    [InlineData("100000\n")]
    [InlineData("abc 100000\n")]
    [InlineData("50000 abc\n")]
    [InlineData("50000 100000 200000\n")]
    public void ParseCpuMax_ShouldReportInvalidContent(string content)
    {
        Assert.Equal(CGroupValueState.Invalid, CGroup2.ParseCpuMax(content).State);
    }

    [Fact]
    public void ParseCpuMax_ShouldReportAnAbsentFile()
    {
        Assert.Equal(CGroupValueState.Unavailable, CGroup2.ParseCpuMax(null).State);
    }

    [Fact]
    public void ParseHugeTlbEventsMax_ShouldReadTheMaxKey()
    {
        Assert.Equal(7, CGroup2.ParseHugeTlbEventsMax("max 7\n").Value);
    }

    [Fact]
    public void ParseHugeTlbEventsMax_ShouldReportInvalidContentWhenTheMaxKeyIsAbsent()
    {
        Assert.Equal(CGroupValueState.Invalid, CGroup2.ParseHugeTlbEventsMax("other 7\n").State);
    }

    [Fact]
    public void ParseHugeTlbEventsMax_ShouldReportAPageSizeTheKernelDoesNotProvide()
    {
        Assert.Equal(CGroupValueState.Unavailable, CGroup2.ParseHugeTlbEventsMax(null).State);
    }

    [Fact]
    public void ParseCpuListValue_ShouldReportAnEmptyFileAsAValue()
    {
        // An empty cpuset.cpus means the cgroup inherits from its parent, which is a value, not an absent one.
        var value = CGroup2.ParseCpuListValue("\n");

        Assert.Equal(CGroupValueState.Configured, value.State);
        Assert.Empty(value.Value);
    }

    [Fact]
    public void ParseCpuListValue_ShouldParseRanges()
    {
        Assert.Equal([0, 1, 2, 5], CGroup2.ParseCpuListValue("0-2,5\n").Value);
        Assert.Equal(CGroupValueState.Unavailable, CGroup2.ParseCpuListValue(null).State);
    }

    [Fact]
    public void CGroupValue_DefaultShouldBeUnavailable()
    {
        CGroupValue<long> value = default;

        Assert.Equal(CGroupValueState.Unavailable, value.State);
        Assert.False(value.IsConfigured);
        Assert.Null(value.RawValue);
    }

    [Fact]
    public void CGroupValue_ValueShouldThrowWhenThereIsNoValue()
    {
        Assert.Throws<InvalidOperationException>(() => CGroup2.ParseLimit("max").Value);
        Assert.Throws<InvalidOperationException>(() => CGroup2.ParseLimit(null).Value);
        Assert.Throws<InvalidOperationException>(() => CGroup2.ParseLimit("bogus").Value);
    }

    [Fact]
    public void CGroupValue_TryGetValueShouldOnlySucceedWhenConfigured()
    {
        Assert.True(CGroup2.ParseLimit("1024").TryGetValue(out var value));
        Assert.Equal(1024, value);

        Assert.False(CGroup2.ParseLimit("max").TryGetValue(out _));
        Assert.False(CGroup2.ParseLimit(null).TryGetValue(out _));
    }

    [Fact]
    public void CGroupValue_GetValueOrDefaultShouldFallBackWhenThereIsNoValue()
    {
        Assert.Equal(1024, CGroup2.ParseLimit("1024").GetValueOrDefault(-1));
        Assert.Equal(-1, CGroup2.ParseLimit("max").GetValueOrDefault(-1));
        Assert.Equal(-1, CGroup2.ParseLimit(null).GetValueOrDefault(-1));
        Assert.Equal(0, CGroup2.ParseLimit(null).GetValueOrDefault());
    }

    [Fact]
    public void CGroupValue_ShouldCompareStateAndValue()
    {
        Assert.Equal(CGroup2.ParseLimit("1024"), CGroup2.ParseLimit("1024"));
        Assert.NotEqual(CGroup2.ParseLimit("1024"), CGroup2.ParseLimit("2048"));
        Assert.NotEqual(CGroup2.ParseLimit("max"), CGroup2.ParseLimit(null));
    }
}
