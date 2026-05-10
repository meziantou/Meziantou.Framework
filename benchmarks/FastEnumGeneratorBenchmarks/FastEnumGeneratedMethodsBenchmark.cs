using BenchmarkDotNet.Attributes;
using FastEnumGeneratorBenchmarks.Generated;

namespace FastEnumGeneratorBenchmarks;

[MemoryDiagnoser]
public class FastEnumGeneratedMethodsBenchmark
{
    private const FlagsEnum FlagsValue = FlagsEnum.Read | FlagsEnum.Write;
    private const MetadataFlagsEnum MetadataFlagsValue = MetadataFlagsEnum.Read | MetadataFlagsEnum.Write;

    [Benchmark]
    public string ToString_default_simpleenum() => SimpleEnum.Two.ToString();

    [Benchmark]
    public string ToString_fast_simpleenum() => SimpleEnum.Two.ToStringFast();

    [Benchmark]
    public string GetName_default_simpleenum() => Enum.GetName(SimpleEnum.Two)!;

    [Benchmark]
    public string GetName_fast_simpleenum() => SimpleEnum.Two.GetName();

    [Benchmark]
    public bool HasFlag_default_simpleenum() => SimpleEnum.Two.HasFlag(SimpleEnum.One);

    [Benchmark]
    public bool HasFlag_fast_simpleenum() => FastEnumExtensions_4.HasFlag(SimpleEnum.Two, SimpleEnum.One);

    [Benchmark]
    public bool Parse_string_default_simpleenum() => Enum.Parse<SimpleEnum>("Two", ignoreCase: true) == SimpleEnum.Two;

    [Benchmark]
    public bool Parse_string_fast_simpleenum() => SimpleEnum.Parse("Two", ignoreCase: true, useMetadata: false) == SimpleEnum.Two;

    [Benchmark]
    public bool Parse_span_default_simpleenum() => Enum.Parse<SimpleEnum>("Two".AsSpan(), ignoreCase: true) == SimpleEnum.Two;

    [Benchmark]
    public bool Parse_span_fast_simpleenum() => SimpleEnum.Parse("Two".AsSpan(), ignoreCase: true, useMetadata: false) == SimpleEnum.Two;

    [Benchmark]
    public bool TryParse_string_default_simpleenum() => Enum.TryParse("Two", ignoreCase: true, out SimpleEnum result) && result == SimpleEnum.Two;

    [Benchmark]
    public bool TryParse_string_fast_simpleenum() => SimpleEnum.TryParse("Two", ignoreCase: true, useMetadata: false, out var result) && result == SimpleEnum.Two;

    [Benchmark]
    public bool TryParse_span_default_simpleenum() => Enum.TryParse("Two".AsSpan(), ignoreCase: true, out SimpleEnum result) && result == SimpleEnum.Two;

    [Benchmark]
    public bool TryParse_span_fast_simpleenum() => SimpleEnum.TryParse("Two".AsSpan(), ignoreCase: true, useMetadata: false, out var result) && result == SimpleEnum.Two;

    [Benchmark]
    public bool IsDefined_default_simpleenum() => Enum.IsDefined(SimpleEnum.Two);

    [Benchmark]
    public bool IsDefined_fast_simpleenum() => SimpleEnum.IsDefined(SimpleEnum.Two);

    [Benchmark]
    public int GetNames_default_simpleenum() => Enum.GetNames<SimpleEnum>().Length;

    [Benchmark]
    public int GetNames_fast_simpleenum() => SimpleEnum.GetNames(useMetadata: false).Length;

    [Benchmark]
    public int GetValues_default_simpleenum() => Enum.GetValues<SimpleEnum>().Length;

    [Benchmark]
    public int GetValues_fast_simpleenum() => SimpleEnum.GetValues().Length;

    [Benchmark]
    public string ToString_default_flagsenum() => FlagsValue.ToString();

    [Benchmark]
    public string ToString_fast_flagsenum() => FlagsValue.ToStringFast();

    [Benchmark]
    public string GetName_default_flagsenum() => Enum.GetName(FlagsEnum.ReadWrite)!;

    [Benchmark]
    public string GetName_fast_flagsenum() => FlagsEnum.ReadWrite.GetName();

    [Benchmark]
    public bool HasFlag_default_flagsenum() => FlagsValue.HasFlag(FlagsEnum.Write);

    [Benchmark]
    public bool HasFlag_fast_flagsenum() => FastEnumExtensions_0.HasFlag(FlagsValue, FlagsEnum.Write);

    [Benchmark]
    public bool Parse_string_default_flagsenum() => Enum.Parse<FlagsEnum>("Read, Write", ignoreCase: true) == FlagsValue;

    [Benchmark]
    public bool Parse_string_fast_flagsenum() => FlagsEnum.Parse("Read, Write", ignoreCase: true, useMetadata: false) == FlagsValue;

    [Benchmark]
    public bool Parse_span_default_flagsenum() => Enum.Parse<FlagsEnum>("Read, Write".AsSpan(), ignoreCase: true) == FlagsValue;

    [Benchmark]
    public bool Parse_span_fast_flagsenum() => FlagsEnum.Parse("Read, Write".AsSpan(), ignoreCase: true, useMetadata: false) == FlagsValue;

    [Benchmark]
    public bool TryParse_string_default_flagsenum() => Enum.TryParse("Read, Write", ignoreCase: true, out FlagsEnum result) && result == FlagsValue;

    [Benchmark]
    public bool TryParse_string_fast_flagsenum() => FlagsEnum.TryParse("Read, Write", ignoreCase: true, useMetadata: false, out var result) && result == FlagsValue;

    [Benchmark]
    public bool TryParse_span_default_flagsenum() => Enum.TryParse("Read, Write".AsSpan(), ignoreCase: true, out FlagsEnum result) && result == FlagsValue;

    [Benchmark]
    public bool TryParse_span_fast_flagsenum() => FlagsEnum.TryParse("Read, Write".AsSpan(), ignoreCase: true, useMetadata: false, out var result) && result == FlagsValue;

    [Benchmark]
    public bool IsDefined_default_flagsenum() => Enum.IsDefined(FlagsEnum.ReadWrite);

    [Benchmark]
    public bool IsDefined_fast_flagsenum() => FlagsEnum.IsDefined(FlagsEnum.ReadWrite);

    [Benchmark]
    public int GetNames_default_flagsenum() => Enum.GetNames<FlagsEnum>().Length;

    [Benchmark]
    public int GetNames_fast_flagsenum() => FlagsEnum.GetNames(useMetadata: false).Length;

    [Benchmark]
    public int GetValues_default_flagsenum() => Enum.GetValues<FlagsEnum>().Length;

    [Benchmark]
    public int GetValues_fast_flagsenum() => FlagsEnum.GetValues().Length;

    [Benchmark]
    public string ToString_default_smallenum() => SmallEnum.ThirtyOne.ToString();

    [Benchmark]
    public string ToString_fast_smallenum() => SmallEnum.ThirtyOne.ToStringFast();

    [Benchmark]
    public string GetName_default_smallenum() => Enum.GetName(SmallEnum.ThirtyOne)!;

    [Benchmark]
    public string GetName_fast_smallenum() => SmallEnum.ThirtyOne.GetName();

    [Benchmark]
    public bool HasFlag_default_smallenum() => SmallEnum.ThirtyOne.HasFlag(SmallEnum.Three);

    [Benchmark]
    public bool HasFlag_fast_smallenum() => FastEnumExtensions_5.HasFlag(SmallEnum.ThirtyOne, SmallEnum.Three);

    [Benchmark]
    public bool Parse_string_default_smallenum() => Enum.Parse<SmallEnum>("ThirtyOne", ignoreCase: true) == SmallEnum.ThirtyOne;

    [Benchmark]
    public bool Parse_string_fast_smallenum() => SmallEnum.Parse("ThirtyOne", ignoreCase: true, useMetadata: false) == SmallEnum.ThirtyOne;

    [Benchmark]
    public bool Parse_span_default_smallenum() => Enum.Parse<SmallEnum>("ThirtyOne".AsSpan(), ignoreCase: true) == SmallEnum.ThirtyOne;

    [Benchmark]
    public bool Parse_span_fast_smallenum() => SmallEnum.Parse("ThirtyOne".AsSpan(), ignoreCase: true, useMetadata: false) == SmallEnum.ThirtyOne;

    [Benchmark]
    public bool TryParse_string_default_smallenum() => Enum.TryParse("ThirtyOne", ignoreCase: true, out SmallEnum result) && result == SmallEnum.ThirtyOne;

    [Benchmark]
    public bool TryParse_string_fast_smallenum() => SmallEnum.TryParse("ThirtyOne", ignoreCase: true, useMetadata: false, out var result) && result == SmallEnum.ThirtyOne;

    [Benchmark]
    public bool TryParse_span_default_smallenum() => Enum.TryParse("ThirtyOne".AsSpan(), ignoreCase: true, out SmallEnum result) && result == SmallEnum.ThirtyOne;

    [Benchmark]
    public bool TryParse_span_fast_smallenum() => SmallEnum.TryParse("ThirtyOne".AsSpan(), ignoreCase: true, useMetadata: false, out var result) && result == SmallEnum.ThirtyOne;

    [Benchmark]
    public bool IsDefined_default_smallenum() => Enum.IsDefined(SmallEnum.ThirtyOne);

    [Benchmark]
    public bool IsDefined_fast_smallenum() => SmallEnum.IsDefined(SmallEnum.ThirtyOne);

    [Benchmark]
    public int GetNames_default_smallenum() => Enum.GetNames<SmallEnum>().Length;

    [Benchmark]
    public int GetNames_fast_smallenum() => SmallEnum.GetNames(useMetadata: false).Length;

    [Benchmark]
    public int GetValues_default_smallenum() => Enum.GetValues<SmallEnum>().Length;

    [Benchmark]
    public int GetValues_fast_smallenum() => SmallEnum.GetValues().Length;

    [Benchmark]
    public string ToString_default_mediumenum() => MediumEnum.V23.ToString();

    [Benchmark]
    public string ToString_fast_mediumenum() => MediumEnum.V23.ToStringFast();

    [Benchmark]
    public string GetName_default_mediumenum() => Enum.GetName(MediumEnum.V23)!;

    [Benchmark]
    public string GetName_fast_mediumenum() => MediumEnum.V23.GetName();

    [Benchmark]
    public bool HasFlag_default_mediumenum() => MediumEnum.V23.HasFlag(MediumEnum.V03);

    [Benchmark]
    public bool HasFlag_fast_mediumenum() => FastEnumExtensions_2.HasFlag(MediumEnum.V23, MediumEnum.V03);

    [Benchmark]
    public bool Parse_string_default_mediumenum() => Enum.Parse<MediumEnum>("V23", ignoreCase: true) == MediumEnum.V23;

    [Benchmark]
    public bool Parse_string_fast_mediumenum() => MediumEnum.Parse("V23", ignoreCase: true, useMetadata: false) == MediumEnum.V23;

    [Benchmark]
    public bool Parse_span_default_mediumenum() => Enum.Parse<MediumEnum>("V23".AsSpan(), ignoreCase: true) == MediumEnum.V23;

    [Benchmark]
    public bool Parse_span_fast_mediumenum() => MediumEnum.Parse("V23".AsSpan(), ignoreCase: true, useMetadata: false) == MediumEnum.V23;

    [Benchmark]
    public bool TryParse_string_default_mediumenum() => Enum.TryParse("V23", ignoreCase: true, out MediumEnum result) && result == MediumEnum.V23;

    [Benchmark]
    public bool TryParse_string_fast_mediumenum() => MediumEnum.TryParse("V23", ignoreCase: true, useMetadata: false, out var result) && result == MediumEnum.V23;

    [Benchmark]
    public bool TryParse_span_default_mediumenum() => Enum.TryParse("V23".AsSpan(), ignoreCase: true, out MediumEnum result) && result == MediumEnum.V23;

    [Benchmark]
    public bool TryParse_span_fast_mediumenum() => MediumEnum.TryParse("V23".AsSpan(), ignoreCase: true, useMetadata: false, out var result) && result == MediumEnum.V23;

    [Benchmark]
    public bool IsDefined_default_mediumenum() => Enum.IsDefined(MediumEnum.V23);

    [Benchmark]
    public bool IsDefined_fast_mediumenum() => MediumEnum.IsDefined(MediumEnum.V23);

    [Benchmark]
    public int GetNames_default_mediumenum() => Enum.GetNames<MediumEnum>().Length;

    [Benchmark]
    public int GetNames_fast_mediumenum() => MediumEnum.GetNames(useMetadata: false).Length;

    [Benchmark]
    public int GetValues_default_mediumenum() => Enum.GetValues<MediumEnum>().Length;

    [Benchmark]
    public int GetValues_fast_mediumenum() => MediumEnum.GetValues().Length;

    [Benchmark]
    public string ToString_default_largeenum() => LargeEnum.V080.ToString();

    [Benchmark]
    public string ToString_fast_largeenum() => LargeEnum.V080.ToStringFast();

    [Benchmark]
    public string GetName_default_largeenum() => Enum.GetName(LargeEnum.V080)!;

    [Benchmark]
    public string GetName_fast_largeenum() => LargeEnum.V080.GetName();

    [Benchmark]
    public bool HasFlag_default_largeenum() => LargeEnum.V080.HasFlag(LargeEnum.V016);

    [Benchmark]
    public bool HasFlag_fast_largeenum() => FastEnumExtensions_1.HasFlag(LargeEnum.V080, LargeEnum.V016);

    [Benchmark]
    public bool Parse_string_default_largeenum() => Enum.Parse<LargeEnum>("V080", ignoreCase: true) == LargeEnum.V080;

    [Benchmark]
    public bool Parse_string_fast_largeenum() => LargeEnum.Parse("V080", ignoreCase: true, useMetadata: false) == LargeEnum.V080;

    [Benchmark]
    public bool Parse_span_default_largeenum() => Enum.Parse<LargeEnum>("V080".AsSpan(), ignoreCase: true) == LargeEnum.V080;

    [Benchmark]
    public bool Parse_span_fast_largeenum() => LargeEnum.Parse("V080".AsSpan(), ignoreCase: true, useMetadata: false) == LargeEnum.V080;

    [Benchmark]
    public bool TryParse_string_default_largeenum() => Enum.TryParse("V080", ignoreCase: true, out LargeEnum result) && result == LargeEnum.V080;

    [Benchmark]
    public bool TryParse_string_fast_largeenum() => LargeEnum.TryParse("V080", ignoreCase: true, useMetadata: false, out var result) && result == LargeEnum.V080;

    [Benchmark]
    public bool TryParse_span_default_largeenum() => Enum.TryParse("V080".AsSpan(), ignoreCase: true, out LargeEnum result) && result == LargeEnum.V080;

    [Benchmark]
    public bool TryParse_span_fast_largeenum() => LargeEnum.TryParse("V080".AsSpan(), ignoreCase: true, useMetadata: false, out var result) && result == LargeEnum.V080;

    [Benchmark]
    public bool IsDefined_default_largeenum() => Enum.IsDefined(LargeEnum.V080);

    [Benchmark]
    public bool IsDefined_fast_largeenum() => LargeEnum.IsDefined(LargeEnum.V080);

    [Benchmark]
    public int GetNames_default_largeenum() => Enum.GetNames<LargeEnum>().Length;

    [Benchmark]
    public int GetNames_fast_largeenum() => LargeEnum.GetNames(useMetadata: false).Length;

    [Benchmark]
    public int GetValues_default_largeenum() => Enum.GetValues<LargeEnum>().Length;

    [Benchmark]
    public int GetValues_fast_largeenum() => LargeEnum.GetValues().Length;

    [Benchmark]
    public string ToString_default_metadataflagsenum() => MetadataFlagsValue.ToString();

    [Benchmark]
    public string ToString_fast_metadataflagsenum() => MetadataFlagsValue.ToStringFast();

    [Benchmark]
    public string GetName_default_metadataflagsenum() => Enum.GetName(MetadataFlagsEnum.Read)!;

    [Benchmark]
    public string GetName_fast_metadataflagsenum() => MetadataFlagsEnum.Read.GetName();

    [Benchmark]
    public bool HasFlag_default_metadataflagsenum() => MetadataFlagsValue.HasFlag(MetadataFlagsEnum.Write);

    [Benchmark]
    public bool HasFlag_fast_metadataflagsenum() => FastEnumExtensions_3.HasFlag(MetadataFlagsValue, MetadataFlagsEnum.Write);

    [Benchmark]
    public bool Parse_string_default_metadataflagsenum() => Enum.Parse<MetadataFlagsEnum>("Read", ignoreCase: true) == MetadataFlagsEnum.Read;

    [Benchmark]
    public bool Parse_string_fast_metadataflagsenum() => MetadataFlagsEnum.Parse("Read", ignoreCase: true, useMetadata: false) == MetadataFlagsEnum.Read;

    [Benchmark]
    public bool Parse_span_default_metadataflagsenum() => Enum.Parse<MetadataFlagsEnum>("Read".AsSpan(), ignoreCase: true) == MetadataFlagsEnum.Read;

    [Benchmark]
    public bool Parse_span_fast_metadataflagsenum() => MetadataFlagsEnum.Parse("Read".AsSpan(), ignoreCase: true, useMetadata: false) == MetadataFlagsEnum.Read;

    [Benchmark]
    public bool TryParse_string_default_metadataflagsenum() => Enum.TryParse("Read", ignoreCase: true, out MetadataFlagsEnum result) && result == MetadataFlagsEnum.Read;

    [Benchmark]
    public bool TryParse_string_fast_metadataflagsenum() => MetadataFlagsEnum.TryParse("Read", ignoreCase: true, useMetadata: false, out var result) && result == MetadataFlagsEnum.Read;

    [Benchmark]
    public bool TryParse_span_default_metadataflagsenum() => Enum.TryParse("Read".AsSpan(), ignoreCase: true, out MetadataFlagsEnum result) && result == MetadataFlagsEnum.Read;

    [Benchmark]
    public bool TryParse_span_fast_metadataflagsenum() => MetadataFlagsEnum.TryParse("Read".AsSpan(), ignoreCase: true, useMetadata: false, out var result) && result == MetadataFlagsEnum.Read;

    [Benchmark]
    public bool IsDefined_default_metadataflagsenum() => Enum.IsDefined(MetadataFlagsEnum.Read);

    [Benchmark]
    public bool IsDefined_fast_metadataflagsenum() => MetadataFlagsEnum.IsDefined(MetadataFlagsEnum.Read);

    [Benchmark]
    public int GetNames_default_metadataflagsenum() => Enum.GetNames<MetadataFlagsEnum>().Length;

    [Benchmark]
    public int GetNames_fast_metadataflagsenum() => MetadataFlagsEnum.GetNames(useMetadata: false).Length;

    [Benchmark]
    public int GetValues_default_metadataflagsenum() => Enum.GetValues<MetadataFlagsEnum>().Length;

    [Benchmark]
    public int GetValues_fast_metadataflagsenum() => MetadataFlagsEnum.GetValues().Length;
}
