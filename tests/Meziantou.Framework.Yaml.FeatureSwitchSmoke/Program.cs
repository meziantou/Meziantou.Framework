using Meziantou.Framework.Yaml;

AppContext.SetSwitch("Meziantou.Framework.Yaml.YamlSerializer.IsReflectionEnabledByDefault", false);

static int RoundTrip<T>(T value, int errorCode)
{
    var yaml = YamlSerializer.Serialize(value);
    var roundTrip = YamlSerializer.Deserialize<T>(yaml);
    return EqualityComparer<T>.Default.Equals(roundTrip, value) ? 0 : errorCode;
}

var code = RoundTrip(true, 1); if (code != 0) return code;
code = RoundTrip((byte)123, 2); if (code != 0) return code;
code = RoundTrip((sbyte)-5, 3); if (code != 0) return code;
code = RoundTrip((short)-1234, 4); if (code != 0) return code;
code = RoundTrip((ushort)4321, 5); if (code != 0) return code;
code = RoundTrip(42, 6); if (code != 0) return code;
code = RoundTrip(42u, 7); if (code != 0) return code;
code = RoundTrip(42L, 8); if (code != 0) return code;
code = RoundTrip(42UL, 9); if (code != 0) return code;
code = RoundTrip((nint)42, 10); if (code != 0) return code;
code = RoundTrip((nuint)42, 11); if (code != 0) return code;
code = RoundTrip(3.25f, 12); if (code != 0) return code;
code = RoundTrip(3.25d, 13); if (code != 0) return code;
code = RoundTrip(3.25m, 14); if (code != 0) return code;
code = RoundTrip('A', 15); if (code != 0) return code;
code = RoundTrip("hello", 16); if (code != 0) return code;
code = RoundTrip((int?)null, 17); if (code != 0) return code;
code = RoundTrip((bool?)true, 18); if (code != 0) return code;

var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
{
    ["a"] = 1,
    ["b"] = new object?[]
    {
        "x",
        2,
        new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["c"] = true,
        },
    },
    ["c"] = new List<object?>
    {
        null,
        "y",
    },
};

var yaml = YamlSerializer.Serialize(payload);
var model = YamlSerializer.Deserialize<Dictionary<string, object?>>(yaml);
if (model is null)
{
    return 2;
}

if (!model.TryGetValue("a", out var aValue) || aValue is not long aLong || aLong != 1)
{
    return 3;
}

if (!model.TryGetValue("b", out var bValue) || bValue is not List<object?> bList || bList.Count != 3)
{
    return 4;
}

if (bList[0] is not string s || s != "x")
{
    return 5;
}

if (bList[1] is not long bLong || bLong != 2)
{
    return 6;
}

if (bList[2] is not Dictionary<string, object?> inner || inner.Count != 1 || inner["c"] is not bool b || !b)
{
    return 7;
}

if (!model.TryGetValue("c", out var cValue) || cValue is not List<object?> cList || cList.Count != 2)
{
    return 8;
}

if (cList[0] is not null || cList[1] is not string cy || cy != "y")
{
    return 9;
}

return 0;
