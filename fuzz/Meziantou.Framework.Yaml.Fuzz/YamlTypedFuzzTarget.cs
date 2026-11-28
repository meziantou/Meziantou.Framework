using System.Collections.ObjectModel;
using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Fuzz;

/// <summary>
/// The reflection-based serializer and the source-generated one read the same objects from a document, whatever the
/// order, the case, the duplicates or the spelling of the keys and of the scalars. What they read is written back to
/// YAML that reads back to the same objects, and the UTF-8 overloads write the UTF-8 bytes of the text.
/// </summary>
internal static class YamlTypedFuzzTarget
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static readonly string[] Tokens =
    [
        "Name", "Count", "Total", "Price", "Ratio", "Enabled", "CreatedAt", "Optional", "Child", "Tags", "Children", "Metadata", "Sku", "Quantity", "UnitPrice",
        "name", "COUNT", "<<", "&a ", "*a", "null", "~", "true", "True", "false", "0x1F", "0o17", "0b101", "1_000", "+5", "-0", "1.5e3", ".5", ".inf", "-.inf", ".nan",
        "2024-01-01T00:00:00.0000000Z", "2024-01-01T00:00:00.0000000", "2024-01-01T00:00:00.0000000+02:00", "2024-01-01", "9223372036854775808", "\"", "'", ": ", "- ", "{", "}", "[", "]", ", ", "\n", "  ",
    ];

    private static readonly YamlSerializerOptions[] Options =
    [
        new() { MaxDepth = 64 },
        new() { MaxDepth = 64, DuplicateKeyHandling = YamlDuplicateKeyHandling.LastWins },
        new() { MaxDepth = 64, DuplicateKeyHandling = YamlDuplicateKeyHandling.FirstWins },
        new() { MaxDepth = 64, PropertyNameCaseInsensitive = true },
        new() { MaxDepth = 64, Schema = YamlSchemaKind.Failsafe },
        new() { MaxDepth = 64, Schema = YamlSchemaKind.Json, DuplicateKeyHandling = YamlDuplicateKeyHandling.LastWins },
        new() { MaxDepth = 64, MappingOrder = YamlMappingOrderPolicy.Sorted, UnmappedMemberHandling = YamlUnmappedMemberHandling.Disallow },
        new() { MaxDepth = 64, UseSchema = true },
    ];

    public static void Run(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.Pick(Options, FuzzInput.ReadByte(ref data));
        var yaml = FuzzText.Decode(data);
        var context = new TypedFuzzContext(options);

        var reflection = Read(() => YamlSerializer.Deserialize<TypedPayload>(yaml, options));
        var generated = Read(() => YamlSerializer.Deserialize<TypedPayload>(yaml, context));
        FuzzAssert.Equal(reflection.Error is null, generated.Error is null, $"The reflection-based and the source-generated serializers disagree: {reflection.Error ?? "no error"} / {generated.Error ?? "no error"}");
        if (reflection.Value is null || generated.Value is null)
        {
            FuzzAssert.Equal(reflection.Value is null, generated.Value is null, "Only one serializer reads null");
            return;
        }

        var written = YamlSerializer.Serialize(reflection.Value, options);
        FuzzAssert.Equal(written, YamlSerializer.Serialize(generated.Value, options), "The reflection-based and the source-generated serializers read different values");

        // The UTF-8 bytes read the same values as the text, and the UTF-8 overloads write the bytes of the text
        FuzzAssert.Equal(written, Write(Read(() => YamlSerializer.Deserialize<TypedPayload>(StrictUtf8.GetBytes(yaml), options)).Value, options), "The text and the UTF-8 bytes read different values");
        AssertUtf8(written, YamlSerializer.SerializeToUtf8Bytes(reflection.Value, options), "SerializeToUtf8Bytes");
        AssertUtf8(YamlSerializer.Serialize(generated.Value, context), YamlSerializer.SerializeToUtf8Bytes(generated.Value, context), "SerializeToUtf8Bytes with the generated metadata");
        using (var stream = new MemoryStream())
        {
            YamlSerializer.Serialize(stream, reflection.Value, options);
            AssertUtf8(written, stream.ToArray(), "The Stream overload");
        }

        TypedPayload? reloaded;
        try
        {
            reloaded = YamlSerializer.Deserialize<TypedPayload>(written, options);
        }
        catch (YamlException ex)
        {
            throw new FuzzAssertionException($"The written YAML cannot be read back: {ex.Message}\n{written}", ex);
        }

        FuzzAssert.Equal(written, Write(reloaded, options), "The written values do not read back to the same values");
    }

    private static string? Write(TypedPayload? value, YamlSerializerOptions options) => value is null ? null : YamlSerializer.Serialize(value, options);

    private static (TypedPayload? Value, string? Error) Read(Func<TypedPayload?> read)
    {
        try
        {
            return (read(), null);
        }
        catch (YamlException ex)
        {
            return (null, ex.Message);
        }
    }

    private static void AssertUtf8(string text, byte[] actual, string name)
    {
        // The values read from a document are valid text: a lone surrogate would have been reported by the reader
        FuzzAssert.True(StrictUtf8.GetBytes(text).AsSpan().SequenceEqual(actual), $"{name} does not write the UTF-8 bytes of the text: {FuzzAssert.Format(text)}");
    }

    public static IEnumerable<FuzzSeed> GetSeeds()
    {
        string[] documents =
        [
            "Name: n\nCount: 1\nTotal: 2\nPrice: 1.50\nRatio: 0.25\nEnabled: true\nCreatedAt: 2024-01-01T00:00:00.0000000Z\nOptional: 3\n",
            "Child: {Sku: a, Quantity: 2, UnitPrice: 3.5}\nTags: [a, b]\nChildren:\n  - Sku: x\n    Quantity: 1\n  - {Sku: y}\nMetadata: {k: v, k2: v2}\n",
            "Count: 1\nName: n\nCount: 2\n",
            "name: lower\nCOUNT: 4\n",
            "base: &b {Sku: a, Quantity: 1}\n",
            "Child:\n  <<: {Sku: merged, Quantity: 5}\n  Quantity: 6\n",
            "\"Name\": quoted\n'Count': 7\n\"Na\\u006De\": escaped\n",
            "Count: 0x1F\nTotal: 1_000\nPrice: +5\nRatio: .inf\nOptional: ~\n",
            "Count: 9223372036854775808\n",
            "CreatedAt: 2024-01-01T00:00:00.0000000+02:00\n",
            "CreatedAt: 2024-01-01\n",
            "Enabled: True\nRatio: -0\nPrice: 1e3\n",
            "Name: null\nTags: null\nChild: ~\n",
            "Unknown: 1\nName: n\n",
            "Metadata: {a: 1, a: 2}\n",
            "Metadata: {a: 1, <<: {a: 10, b: 20, c: 30}, b: 2}\n",
            "Metadata: {a: 1, <<: {b: 20}, a: 2}\n",
            "Metadata: {<<: [{a: 1}, {a: 2, b: 3}], a: 4, a: 5}\n",
            "Metadata: {a: 1, A: 2}\n",
            "Name: a\n<<: {Count: 1, Name: b}\nCount: 2\nCount: 3\n",
            "Tags: [a, b]\nTags: [c]\n",
            "Children: [{Sku: a, Sku: b}]\n",
            "{Name: flow, Count: 3, Child: {Quantity: 4}}\n",
            "Name: |\n  block\n  text\nCount: 1\n",
            "",
        ];

        var index = 0;
        foreach (var document in documents)
        {
            for (byte option = 0; option < Options.Length; option++)
            {
                yield return FuzzSeed.FromText("typed-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + option.ToString(System.Globalization.CultureInfo.InvariantCulture), [option], document);
            }

            index++;
        }
    }

    internal sealed class TypedPayload
    {
        public string? Name { get; set; }

        public int Count { get; set; }

        public long Total { get; set; }

        public decimal Price { get; set; }

        public double Ratio { get; set; }

        public bool Enabled { get; set; }

        public DateTime CreatedAt { get; set; }

        public int? Optional { get; set; }

        public TypedChild? Child { get; set; }

        public List<string>? Tags { get; set; }

        public Collection<TypedChild>? Children { get; set; }

        public Dictionary<string, string>? Metadata { get; set; }
    }

    internal sealed class TypedChild
    {
        public string? Sku { get; set; }

        public int Quantity { get; set; }

        public decimal UnitPrice { get; set; }
    }
}
