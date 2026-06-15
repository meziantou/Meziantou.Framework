namespace Meziantou.Framework.Yamlish;

internal static class BuiltInYamlishConverters
{
    private static readonly Dictionary<Type, YamlishConverter> Converters = new()
    {
        [typeof(bool)] = new BooleanYamlishConverter(),
        [typeof(byte)] = new ScalarYamlishConverter<byte>(),
        [typeof(char)] = new ScalarYamlishConverter<char>(),
        [typeof(DateOnly)] = new ScalarYamlishConverter<DateOnly>(ParseDateOnly, FormatDateOnly),
        [typeof(DateTime)] = new ScalarYamlishConverter<DateTime>(ParseDateTime, FormatDateTime),
        [typeof(DateTimeOffset)] = new ScalarYamlishConverter<DateTimeOffset>(ParseDateTimeOffset, FormatDateTimeOffset),
        [typeof(decimal)] = new ScalarYamlishConverter<decimal>(),
        [typeof(double)] = new ScalarYamlishConverter<double>(ParseDouble, FormatDouble),
        [typeof(float)] = new ScalarYamlishConverter<float>(ParseSingle, FormatSingle),
        [typeof(Guid)] = new ScalarYamlishConverter<Guid>(Guid.Parse, FormatGuid),
        [typeof(int)] = new ScalarYamlishConverter<int>(),
        [typeof(IntPtr)] = new ScalarYamlishConverter<IntPtr>(ParseIntPtr, FormatIntPtr),
        [typeof(long)] = new ScalarYamlishConverter<long>(),
        [typeof(sbyte)] = new ScalarYamlishConverter<sbyte>(),
        [typeof(short)] = new ScalarYamlishConverter<short>(),
        [typeof(string)] = new StringYamlishConverter(),
        [typeof(TimeOnly)] = new ScalarYamlishConverter<TimeOnly>(ParseTimeOnly, FormatTimeOnly),
        [typeof(TimeSpan)] = new ScalarYamlishConverter<TimeSpan>(ParseTimeSpan, FormatTimeSpan),
        [typeof(uint)] = new ScalarYamlishConverter<uint>(),
        [typeof(UIntPtr)] = new ScalarYamlishConverter<UIntPtr>(ParseUIntPtr, FormatUIntPtr),
        [typeof(ulong)] = new ScalarYamlishConverter<ulong>(),
        [typeof(Uri)] = new ScalarYamlishConverter<Uri>(),
        [typeof(ushort)] = new ScalarYamlishConverter<ushort>(),
    };

    public static YamlishConverter? GetConverter(Type type)
    {
        if (Converters.TryGetValue(type, out var converter))
            return converter;

        return type.IsEnum ? new EnumYamlishConverter(type) : null;
    }

    private static DateOnly ParseDateOnly(string value) => DateOnly.Parse(value, CultureInfo.InvariantCulture);

    private static string FormatDateOnly(DateOnly value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTime ParseDateTime(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string FormatDateTime(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDateTimeOffset(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string FormatDateTimeOffset(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static double ParseDouble(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string FormatDouble(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static float ParseSingle(string value) => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string FormatSingle(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string FormatGuid(Guid value) => value.ToString("D");

    private static IntPtr ParseIntPtr(string value) => new(long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture));

    private static string FormatIntPtr(IntPtr value) => value.ToInt64().ToString(CultureInfo.InvariantCulture);

    private static TimeOnly ParseTimeOnly(string value) => TimeOnly.Parse(value, CultureInfo.InvariantCulture);

    private static string FormatTimeOnly(TimeOnly value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static TimeSpan ParseTimeSpan(string value) => TimeSpan.Parse(value, CultureInfo.InvariantCulture);

    private static string FormatTimeSpan(TimeSpan value) => value.ToString("c", CultureInfo.InvariantCulture);

    private static UIntPtr ParseUIntPtr(string value) => new(ulong.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture));

    private static string FormatUIntPtr(UIntPtr value) => value.ToUInt64().ToString(CultureInfo.InvariantCulture);
}
