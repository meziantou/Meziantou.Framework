using System.Buffers;
using BenchmarkDotNet.Attributes;
using FastEnumGeneratorBenchmarks.Generated;

namespace FastEnumGeneratorBenchmarks;

[MemoryDiagnoser]
public class FastEnumGeneratedMethodsBenchmark
{
    private Func<string> _toStringFast = () => "";
    private Func<string> _toStringFastMetadata = () => "";
    private Func<string> _getName = () => "";
    private Func<bool> _hasFlag = () => false;
    private Func<bool, bool> _parseString = _ => false;
    private Func<bool, bool> _parseSpan = _ => false;
    private Func<bool, bool> _tryParseString = _ => false;
    private Func<bool, bool> _tryParseSpan = _ => false;
    private Func<bool> _isDefined = () => false;
    private Func<bool, int> _getNamesLength = _ => 0;
    private Func<int> _getValuesLength = () => 0;
    private SearchValues<string> _searchValues = SearchValues.Create([""], StringComparison.OrdinalIgnoreCase);
    private SearchValues<string> _searchValuesMetadata = SearchValues.Create([""], StringComparison.OrdinalIgnoreCase);
    private HashSet<string> _hashSet = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _hashSetMetadata = new(StringComparer.OrdinalIgnoreCase);
    private string _parseToken = "";
    private string _parseMetadataToken = "";

    [Params(
        FastEnumCase.Simple,
        FastEnumCase.Flags,
        FastEnumCase.Small,
        FastEnumCase.Medium,
        FastEnumCase.Large,
        FastEnumCase.Metadata)]
    public FastEnumCase Case { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        switch (Case)
        {
            case FastEnumCase.Simple:
                ConfigureSimple();
                break;
            case FastEnumCase.Flags:
                ConfigureFlags();
                break;
            case FastEnumCase.Small:
                ConfigureSmall();
                break;
            case FastEnumCase.Medium:
                ConfigureMedium();
                break;
            case FastEnumCase.Large:
                ConfigureLarge();
                break;
            case FastEnumCase.Metadata:
                ConfigureMetadataFlags();
                break;
        }
    }

    [Benchmark]
    public string ToStringFast() => _toStringFast();

    [Benchmark]
    public string ToStringFast_Metadata() => _toStringFastMetadata();

    [Benchmark]
    public string GetName() => _getName();

    [Benchmark]
    public bool HasFlag() => _hasFlag();

    [Benchmark]
    public bool Parse_String() => _parseString(false);

    [Benchmark]
    public bool Parse_Span() => _parseSpan(false);

    [Benchmark]
    public bool Parse_String_Metadata() => _parseString(true);

    [Benchmark]
    public bool Parse_Span_Metadata() => _parseSpan(true);

    [Benchmark]
    public bool TryParse_String() => _tryParseString(false);

    [Benchmark]
    public bool TryParse_Span() => _tryParseSpan(false);

    [Benchmark]
    public bool TryParse_String_Metadata() => _tryParseString(true);

    [Benchmark]
    public bool TryParse_Span_Metadata() => _tryParseSpan(true);

    [Benchmark]
    public bool IsDefined() => _isDefined();

    [Benchmark]
    public int GetNames() => _getNamesLength(false);

    [Benchmark]
    public int GetNames_Metadata() => _getNamesLength(true);

    [Benchmark]
    public int GetValues() => _getValuesLength();

    [Benchmark]
    public bool SearchValues_Contains() => _searchValues.Contains(_parseToken);

    [Benchmark]
    public bool SearchValues_Contains_Metadata() => _searchValuesMetadata.Contains(_parseMetadataToken);

    [Benchmark]
    public bool HashSet_Contains() => _hashSet.Contains(_parseToken);

    [Benchmark]
    public bool HashSet_Contains_Metadata() => _hashSetMetadata.Contains(_parseMetadataToken);

    private void ConfigureSimple()
    {
        _toStringFast = () => SimpleEnum.Two.ToStringFast();
        _toStringFastMetadata = () => SimpleEnum.Two.ToStringFast(useMetadata: true);
        _getName = () => SimpleEnum.Two.GetName();
        _hasFlag = () => SimpleEnum.Two.HasFlag(SimpleEnum.One);
        _parseString = useMetadata => SimpleEnum.Parse(useMetadata ? _parseMetadataToken : _parseToken, ignoreCase: true, useMetadata) == SimpleEnum.Two;
        _parseSpan = useMetadata => SimpleEnum.Parse((useMetadata ? _parseMetadataToken : _parseToken).AsSpan(), ignoreCase: true, useMetadata) == SimpleEnum.Two;
        _tryParseString = useMetadata => SimpleEnum.TryParse(useMetadata ? _parseMetadataToken : _parseToken, ignoreCase: true, useMetadata, out var result) && result == SimpleEnum.Two;
        _tryParseSpan = useMetadata => SimpleEnum.TryParse((useMetadata ? _parseMetadataToken : _parseToken).AsSpan(), ignoreCase: true, useMetadata, out var result) && result == SimpleEnum.Two;
        _isDefined = () => SimpleEnum.IsDefined(SimpleEnum.Two);
        _getNamesLength = useMetadata => SimpleEnum.GetNames(useMetadata).Length;
        _getValuesLength = () => SimpleEnum.GetValues().Length;
        ConfigureLookups(SimpleEnum.GetNames(useMetadata: false).ToArray(), SimpleEnum.GetNames(useMetadata: true).ToArray(), "Two", "Two");
    }

    private void ConfigureFlags()
    {
        var value = FlagsEnum.Read | FlagsEnum.Write;
        _toStringFast = () => value.ToStringFast();
        _toStringFastMetadata = () => value.ToStringFast(useMetadata: true);
        _getName = () => value.GetName();
        _hasFlag = () => value.HasFlag(FlagsEnum.Write);
        _parseString = useMetadata => FlagsEnum.Parse(useMetadata ? _parseMetadataToken : _parseToken, ignoreCase: true, useMetadata) == value;
        _parseSpan = useMetadata => FlagsEnum.Parse((useMetadata ? _parseMetadataToken : _parseToken).AsSpan(), ignoreCase: true, useMetadata) == value;
        _tryParseString = useMetadata => FlagsEnum.TryParse(useMetadata ? _parseMetadataToken : _parseToken, ignoreCase: true, useMetadata, out var result) && result == value;
        _tryParseSpan = useMetadata => FlagsEnum.TryParse((useMetadata ? _parseMetadataToken : _parseToken).AsSpan(), ignoreCase: true, useMetadata, out var result) && result == value;
        _isDefined = () => FlagsEnum.IsDefined(FlagsEnum.ReadWrite);
        _getNamesLength = useMetadata => FlagsEnum.GetNames(useMetadata).Length;
        _getValuesLength = () => FlagsEnum.GetValues().Length;
        ConfigureLookups(FlagsEnum.GetNames(useMetadata: false).ToArray(), FlagsEnum.GetNames(useMetadata: true).ToArray(), "Read, Write", "Read, Write");
    }

    private void ConfigureSmall()
    {
        _toStringFast = () => SmallEnum.ThirtyOne.ToStringFast();
        _toStringFastMetadata = () => SmallEnum.ThirtyOne.ToStringFast(useMetadata: true);
        _getName = () => SmallEnum.ThirtyOne.GetName();
        _hasFlag = () => SmallEnum.ThirtyOne.HasFlag(SmallEnum.Three);
        _parseString = useMetadata => SmallEnum.Parse(useMetadata ? _parseMetadataToken : _parseToken, ignoreCase: true, useMetadata) == SmallEnum.ThirtyOne;
        _parseSpan = useMetadata => SmallEnum.Parse((useMetadata ? _parseMetadataToken : _parseToken).AsSpan(), ignoreCase: true, useMetadata) == SmallEnum.ThirtyOne;
        _tryParseString = useMetadata => SmallEnum.TryParse(useMetadata ? _parseMetadataToken : _parseToken, ignoreCase: true, useMetadata, out var result) && result == SmallEnum.ThirtyOne;
        _tryParseSpan = useMetadata => SmallEnum.TryParse((useMetadata ? _parseMetadataToken : _parseToken).AsSpan(), ignoreCase: true, useMetadata, out var result) && result == SmallEnum.ThirtyOne;
        _isDefined = () => SmallEnum.IsDefined(SmallEnum.ThirtyOne);
        _getNamesLength = useMetadata => SmallEnum.GetNames(useMetadata).Length;
        _getValuesLength = () => SmallEnum.GetValues().Length;
        ConfigureLookups(SmallEnum.GetNames(useMetadata: false).ToArray(), SmallEnum.GetNames(useMetadata: true).ToArray(), "ThirtyOne", "ThirtyOne");
    }

    private void ConfigureMedium()
    {
        _toStringFast = () => MediumEnum.V23.ToStringFast();
        _toStringFastMetadata = () => MediumEnum.V23.ToStringFast(useMetadata: true);
        _getName = () => MediumEnum.V23.GetName();
        _hasFlag = () => MediumEnum.V23.HasFlag(MediumEnum.V03);
        _parseString = useMetadata => MediumEnum.Parse(useMetadata ? _parseMetadataToken : _parseToken, ignoreCase: true, useMetadata) == MediumEnum.V23;
        _parseSpan = useMetadata => MediumEnum.Parse((useMetadata ? _parseMetadataToken : _parseToken).AsSpan(), ignoreCase: true, useMetadata) == MediumEnum.V23;
        _tryParseString = useMetadata => MediumEnum.TryParse(useMetadata ? _parseMetadataToken : _parseToken, ignoreCase: true, useMetadata, out var result) && result == MediumEnum.V23;
        _tryParseSpan = useMetadata => MediumEnum.TryParse((useMetadata ? _parseMetadataToken : _parseToken).AsSpan(), ignoreCase: true, useMetadata, out var result) && result == MediumEnum.V23;
        _isDefined = () => MediumEnum.IsDefined(MediumEnum.V23);
        _getNamesLength = useMetadata => MediumEnum.GetNames(useMetadata).Length;
        _getValuesLength = () => MediumEnum.GetValues().Length;
        ConfigureLookups(MediumEnum.GetNames(useMetadata: false).ToArray(), MediumEnum.GetNames(useMetadata: true).ToArray(), "V23", "V23");
    }

    private void ConfigureLarge()
    {
        _toStringFast = () => LargeEnum.V080.ToStringFast();
        _toStringFastMetadata = () => LargeEnum.V080.ToStringFast(useMetadata: true);
        _getName = () => LargeEnum.V080.GetName();
        _hasFlag = () => LargeEnum.V080.HasFlag(LargeEnum.V016);
        _parseString = useMetadata => LargeEnum.Parse(useMetadata ? _parseMetadataToken : _parseToken, ignoreCase: true, useMetadata) == LargeEnum.V080;
        _parseSpan = useMetadata => LargeEnum.Parse((useMetadata ? _parseMetadataToken : _parseToken).AsSpan(), ignoreCase: true, useMetadata) == LargeEnum.V080;
        _tryParseString = useMetadata => LargeEnum.TryParse(useMetadata ? _parseMetadataToken : _parseToken, ignoreCase: true, useMetadata, out var result) && result == LargeEnum.V080;
        _tryParseSpan = useMetadata => LargeEnum.TryParse((useMetadata ? _parseMetadataToken : _parseToken).AsSpan(), ignoreCase: true, useMetadata, out var result) && result == LargeEnum.V080;
        _isDefined = () => LargeEnum.IsDefined(LargeEnum.V080);
        _getNamesLength = useMetadata => LargeEnum.GetNames(useMetadata).Length;
        _getValuesLength = () => LargeEnum.GetValues().Length;
        ConfigureLookups(LargeEnum.GetNames(useMetadata: false).ToArray(), LargeEnum.GetNames(useMetadata: true).ToArray(), "V080", "V080");
    }

    private void ConfigureMetadataFlags()
    {
        var value = MetadataFlagsEnum.Read | MetadataFlagsEnum.Write;
        _toStringFast = () => value.ToStringFast();
        _toStringFastMetadata = () => value.ToStringFast(useMetadata: true);
        _getName = () => value.GetName();
        _hasFlag = () => value.HasFlag(MetadataFlagsEnum.Read);
        _parseString = useMetadata => MetadataFlagsEnum.Parse(useMetadata ? _parseMetadataToken : _parseToken, ignoreCase: true, useMetadata) == value;
        _parseSpan = useMetadata => MetadataFlagsEnum.Parse((useMetadata ? _parseMetadataToken : _parseToken).AsSpan(), ignoreCase: true, useMetadata) == value;
        _tryParseString = useMetadata => MetadataFlagsEnum.TryParse(useMetadata ? _parseMetadataToken : _parseToken, ignoreCase: true, useMetadata, out var result) && result == value;
        _tryParseSpan = useMetadata => MetadataFlagsEnum.TryParse((useMetadata ? _parseMetadataToken : _parseToken).AsSpan(), ignoreCase: true, useMetadata, out var result) && result == value;
        _isDefined = () => MetadataFlagsEnum.IsDefined(MetadataFlagsEnum.Read);
        _getNamesLength = useMetadata => MetadataFlagsEnum.GetNames(useMetadata).Length;
        _getValuesLength = () => MetadataFlagsEnum.GetValues().Length;
        ConfigureLookups(
            MetadataFlagsEnum.GetNames(useMetadata: false).ToArray(),
            MetadataFlagsEnum.GetNames(useMetadata: true).ToArray(),
            "Read, Write",
            "Read metadata, Write metadata");
    }

    private void ConfigureLookups(string[] names, string[] metadataNames, string parseToken, string parseMetadataToken)
    {
        _parseToken = parseToken;
        _parseMetadataToken = parseMetadataToken;
        _searchValues = SearchValues.Create(names, StringComparison.OrdinalIgnoreCase);
        _searchValuesMetadata = SearchValues.Create(metadataNames, StringComparison.OrdinalIgnoreCase);
        _hashSet = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _hashSetMetadata = metadataNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public enum FastEnumCase
    {
        Simple,
        Flags,
        Small,
        Medium,
        Large,
        Metadata,
    }
}
