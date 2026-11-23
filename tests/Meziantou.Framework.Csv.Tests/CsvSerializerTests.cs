using System.Buffers;
using System.Numerics;
using System.Text;
using Meziantou.Framework.Csv.Serialization;
using Meziantou.Framework.Csv.Serialization.Metadata;

namespace Meziantou.Framework.Csv.Tests;

#pragma warning disable MA0048 // File name must match type name: the models of the tests are declared with the tests
#pragma warning disable MA0008, CA1002, CA1051, CA1720, CA1819 // The models cover the members the serializer supports

public class CsvSerializerTests
{
    [Fact]
    public void RoundTrip()
    {
        #region CsvSerializer_RoundTrip
        var people = new List<Person>
        {
            new() { Name = "Alice", Age = 30, BirthDate = new DateOnly(1995, 4, 12) },
            new() { Name = "Bob, Jr.", Age = 25 },
        };

        var csv = CsvSerializer.Serialize(people);
        Assert.Equal("Name,Age,BirthDate\r\nAlice,30,1995-04-12\r\n\"Bob, Jr.\",25,\r\n", csv);

        var result = CsvSerializer.Deserialize<Person>(csv);
        Assert.Equal("Bob, Jr.", result[1].Name);
        Assert.Null(result[1].BirthDate); // An empty value is null for a nullable type
        #endregion
    }

    [Fact]
    public async Task Stream()
    {
        #region CsvSerializer_Stream
        using var stream = new MemoryStream();
        await CsvSerializer.SerializeAsync(stream, [new Person { Name = "Alice", Age = 30 }, new Person { Name = "Bob", Age = 25 }]);

        // Read the records one by one, without loading the whole file
        stream.Position = 0;
        var names = new List<string?>();
        await foreach (var person in CsvSerializer.DeserializeAsyncEnumerable<Person>(stream))
        {
            names.Add(person.Name);
        }

        Assert.Equal(["Alice", "Bob"], names);
        #endregion
    }

    [Fact]
    public async Task AllInputs()
    {
        const string Csv = "Name,Age,BirthDate\nAlice,30,2000-01-02\nBob,25,\n";
        var bytes = Encoding.UTF8.GetBytes(Csv);
        string[] expected = ["Alice|30|2000-01-02", "Bob|25|"];

        Assert.Equal(expected, Describe(CsvSerializer.Deserialize<Person>(Csv)));
        Assert.Equal(expected, Describe(CsvSerializer.Deserialize<Person>(bytes)));
        Assert.Equal(expected, Describe(CsvSerializer.Deserialize<Person>(new StringReader(Csv))));
        Assert.Equal(expected, Describe(CsvSerializer.Deserialize<Person>(new MemoryStream(bytes))));
        Assert.Equal(expected, Describe(await CsvSerializer.DeserializeAsync<Person>(new MemoryStream(bytes))));
        Assert.Equal(expected, Describe(await CsvSerializer.DeserializeAsync<Person>(new StringReader(Csv))));
        Assert.Equal(expected, Describe(CsvSerializer.DeserializeEnumerable<Person>(new StringReader(Csv))));
        Assert.Equal(expected, Describe(CsvSerializer.DeserializeEnumerable<Person>(new MemoryStream(bytes))));
        Assert.Equal(expected, Describe(await ToListAsync(CsvSerializer.DeserializeAsyncEnumerable<Person>(new StringReader(Csv)))));

        static string[] Describe(IEnumerable<Person> people) => [.. people.Select(p => $"{p.Name}|{p.Age.ToString(CultureInfo.InvariantCulture)}|{p.BirthDate?.ToString("O", CultureInfo.InvariantCulture)}")];
    }

    [Fact]
    public async Task AllOutputs()
    {
        Person[] people = [new() { Name = "Alice", Age = 30 }];
        const string Expected = "Name,Age,BirthDate\r\nAlice,30,\r\n";

        Assert.Equal(Expected, CsvSerializer.Serialize(people));

        using var stream = new MemoryStream();
        CsvSerializer.Serialize(stream, people);
        Assert.Equal(Expected, Encoding.UTF8.GetString(stream.ToArray()));

        using var writer = new StringWriter();
        CsvSerializer.Serialize(writer, people);
        Assert.Equal(Expected, writer.ToString());

        var buffer = new ArrayBufferWriter<byte>();
        CsvSerializer.Serialize(buffer, people);
        Assert.Equal(Expected, Encoding.UTF8.GetString(buffer.WrittenSpan));

        using var asyncWriter = new StringWriter();
        await CsvSerializer.SerializeAsync(asyncWriter, people);
        Assert.Equal(Expected, asyncWriter.ToString());

        using var asyncWriter2 = new StringWriter();
        await CsvSerializer.SerializeAsync(asyncWriter2, ToAsyncEnumerable(people));
        Assert.Equal(Expected, asyncWriter2.ToString());

        using var asyncStream = new MemoryStream();
        await CsvSerializer.SerializeAsync(asyncStream, ToAsyncEnumerable(people));
        Assert.Equal(Expected, Encoding.UTF8.GetString(asyncStream.ToArray()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1_000)]
    [InlineData(100_000)]
    public void SerializeToString_LargeOutput(int count)
    {
        // The text is kept in chunks of growing sizes: every size must give the same text as the other outputs
        var people = Enumerable.Range(0, count).Select(i => new Person { Name = "Person \"" + i.ToString(CultureInfo.InvariantCulture) + "\", " + new string('x', i % 50), Age = i }).ToArray();
        var options = new CsvSerializerOptions { BufferSize = 64 };

        using var writer = new StringWriter();
        CsvSerializer.Serialize(writer, people, options);
        var expected = writer.ToString();

        Assert.Equal(expected, CsvSerializer.Serialize(people, options));
        Assert.Equal(expected, CsvSerializer.Serialize(people));
        Assert.HasCount(count, CsvSerializer.Deserialize<Person>(expected));
    }

    [Fact]
    public void Records()
    {
        #region CsvSerializer_Records
        var products = CsvSerializer.Deserialize<Product>("""
            Id,Name,Price
            1,Keyboard,49.90
            2,Mouse,19.99
            """);

        // Records are created with their constructor; the parameters are matched to the columns by name
        Assert.Equal(new Product(2, "Mouse", 19.99m), products[1]);
        #endregion
    }

    [Fact]
    public void Attributes()
    {
        #region CsvSerializer_Attributes
        var orders = new[] { new Order { Id = 1, Customer = "Alice", Date = new DateTime(2026, 11, 21), Secret = "hidden" } };

        var csv = CsvSerializer.Serialize(orders);
        Assert.Equal("Id,customer_name,Date\r\n1,Alice,21/11/2026\r\n", csv);

        var order = CsvSerializer.Deserialize<Order>(csv)[0];
        Assert.Equal(new DateTime(2026, 11, 21), order.Date); // Read with the format of CsvFormatAttribute
        Assert.Null(order.Secret);
        #endregion
    }

    [Fact]
    public void NamingPolicy()
    {
        #region CsvSerializer_NamingPolicy
        var options = new CsvSerializerOptions
        {
            PropertyNamingPolicy = CsvNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
        };

        var csv = CsvSerializer.Serialize([new Product(1, "Keyboard", 49.90m)], options);
        Assert.Equal("id,name,price\r\n1,Keyboard,49.90\r\n", csv);

        var products = CsvSerializer.Deserialize<Product>("ID,NAME,PRICE\n2,Mouse,19.99", options);
        Assert.Equal("Mouse", products[0].Name);
        #endregion
    }

    [Fact]
    public void Dialect()
    {
        #region CsvSerializer_Dialect
        var options = new CsvSerializerOptions
        {
            Separator = ';',
            NewLine = "\n",
            FormatProvider = new NumberFormatInfo { NumberDecimalSeparator = "," },
        };

        var csv = CsvSerializer.Serialize([new Product(1, "Keyboard", 49.90m)], options);
        Assert.Equal("Id;Name;Price\n1;Keyboard;49,90\n", csv);
        Assert.Equal(49.90m, CsvSerializer.Deserialize<Product>(csv, options)[0].Price);
        #endregion
    }

    [Fact]
    public void Headerless()
    {
        #region CsvSerializer_Headerless
        var options = new CsvSerializerOptions { HasHeaderRow = false };

        // Without a header row, the columns are read and written by position: CsvColumnIndexAttribute sets the position
        var measures = CsvSerializer.Deserialize<Measure>("2026-11-21,ignored,12.5\n2026-11-22,ignored,13", options);
        Assert.Equal(13, measures[1].Value);

        var csv = CsvSerializer.Serialize(measures, options);
        Assert.Equal("2026-11-21,,12.5\r\n2026-11-22,,13\r\n", csv);
        #endregion
    }

    [Fact]
    public void Errors()
    {
        #region CsvSerializer_Errors
        var exception = Assert.Throws<CsvException>(() => CsvSerializer.Deserialize<Product>("Id,Name,Price\n1,Keyboard,49.90\nx,Mouse,19.99"));

        Assert.Equal(3, exception.LineNumber);
        Assert.Equal(1, exception.RowIndex);
        Assert.Equal("Id", exception.ColumnName);
        Assert.StartsWith("The value 'x' of the column 'Id' cannot be converted", exception.Message);
        #endregion
    }

    [Fact]
    public void MissingAndUnmappedColumns()
    {
        #region CsvSerializer_MissingColumns
        // A member without a column keeps its default value, and a column without a member is ignored
        var people = CsvSerializer.Deserialize<Person>("Name,Country\nAlice,France");
        Assert.Equal(0, people[0].Age);

        var options = new CsvSerializerOptions
        {
            MissingColumnHandling = CsvMissingColumnHandling.Throw,
            UnmappedColumnHandling = CsvUnmappedColumnHandling.Throw,
        };
        Assert.Throws<CsvException>(() => CsvSerializer.Deserialize<Person>("Name,Country\nAlice,France", options));
        #endregion
    }

    [Fact]
    public void RequiredColumns()
    {
        Assert.Throws<CsvException>(() => CsvSerializer.Deserialize<RequiredModel>("Optional\nx"));
        Assert.Throws<CsvException>(() => CsvSerializer.Deserialize<RequiredModel>("Optional,Value,Attributed\nx,1"));
        var result = CsvSerializer.Deserialize<RequiredModel>("Optional,Value,Attributed\nx,1,2");
        Assert.Equal(1, result[0].Value);
        Assert.Equal(2, result[0].Attributed);
    }

    [Fact]
    public void UnmappedColumnWithoutHeader()
    {
        var options = new CsvSerializerOptions { HasHeaderRow = false, UnmappedColumnHandling = CsvUnmappedColumnHandling.Throw };
        Assert.Throws<CsvException>(() => CsvSerializer.Deserialize<Product>("1,a,2,extra", options));
        Assert.Single(CsvSerializer.Deserialize<Product>("1,a,2", options));
    }

    [Fact]
    public void Converters()
    {
        #region CsvSerializer_Converters
        var options = new CsvSerializerOptions { Converters = [new PointConverter()] };

        var csv = CsvSerializer.Serialize([new Shape { Name = "line", Start = new Point(1, 2) }], options);
        Assert.Equal("Name,Start\r\nLINE,1;2\r\n", csv); // UpperCaseConverter is set with CsvConverterAttribute

        var shape = CsvSerializer.Deserialize<Shape>(csv, options)[0];
        Assert.Equal(new Point(1, 2), shape.Start);
        #endregion
    }

    [Fact]
    public void NoConverter()
    {
        Assert.Throws<NotSupportedException>(() => CsvSerializer.Serialize([new Shape()]));
    }

    [Fact]
    public void BuiltInTypes()
    {
        var value = AllTypes.CreateSample();
        var empty = new AllTypes { String = "" }; // An empty value is read as an empty string
        var csv = CsvSerializer.Serialize([value, empty]);
        var result = CsvSerializer.Deserialize<AllTypes>(csv);
        Assert.Equal(value.Describe(), result[0].Describe());
        Assert.Equal(empty.Describe(), result[1].Describe());

        using var stream = new MemoryStream();
        CsvSerializer.Serialize(stream, [value, empty]);
        Assert.Equal(csv, Encoding.UTF8.GetString(stream.ToArray()));
        stream.Position = 0;
        var utf8Result = CsvSerializer.Deserialize<AllTypes>(stream);
        Assert.Equal(value.Describe(), utf8Result[0].Describe());
        Assert.Equal(empty.Describe(), utf8Result[1].Describe());
    }

    [Fact]
    public void BuiltInTypes_SpecialCharactersOfNumbers()
    {
        // The numbers and the dates of the default converters are not searched for characters that need quotes, unless one of their characters is
        // special: a separator or a quote that is a digit, a sign, a dot, a colon, T or Z still quotes them
        var value = AllTypes.CreateSample();
        List<CsvSerializerOptions> options = [new() { QuoteMode = CsvQuoteMode.Always }];
        foreach (var c in "0123456789+-.:TZ")
        {
            options.Add(new() { Separator = c });
            options.Add(new() { Quote = c });
        }

        foreach (var option in options)
        {
            var csv = CsvSerializer.Serialize([value], option);
            Assert.Equal(value.Describe(), CsvSerializer.Deserialize<AllTypes>(csv, option)[0].Describe());

            using var stream = new MemoryStream();
            CsvSerializer.Serialize(stream, [value], option);
            Assert.Equal(csv, Encoding.UTF8.GetString(stream.ToArray()));
        }
    }

    [Theory]
    [InlineData("2020-01-01T00:00:00.0000000Z")]
    [InlineData("2026-11-21T00:00:00.0000000")]
    [InlineData("2020-03-05T12:34:56.7890000+02:00")]
    [InlineData("2020-03-05T12:34:56.7890000-11:30")]
    [InlineData("2024-02-29T23:59:59.9999999Z")]
    [InlineData("2023-02-29T00:00:00.0000000Z")]
    [InlineData("2020-03-05T12:34:56.7890000+14:00")]
    [InlineData("2020-03-05T12:34:56.7890000-14:00")]
    [InlineData("2020-03-05T12:34:56.7890000+14:01")]
    [InlineData("2020-03-05T12:34:56.7890000+02:60")]
    [InlineData("2020-03-05T12:34:56.7890000+0200")]
    [InlineData("0001-01-01T00:00:00.0000000Z")]
    [InlineData("0001-01-01T00:00:00.0000000-01:00")]
    [InlineData("0001-01-01T00:30:00.0000000+01:00")]
    [InlineData("9999-12-31T23:59:59.9999999Z")]
    [InlineData("9999-12-31T23:59:59.9999999+01:00")]
    [InlineData("9999-12-31T23:59:59.9999999-01:00")]
    [InlineData("0000-01-01T00:00:00.0000000Z")]
    [InlineData("2020-01-01T24:00:00.0000000Z")]
    [InlineData("2020-01-01T23:60:00.0000000Z")]
    [InlineData("2020-01-01T23:59:60.0000000Z")]
    [InlineData("2020-01-01T23:59:59.000000aZ")]
    [InlineData("2020-01-01 23:59:59.0000000Z")]
    [InlineData("2020-01-01T23:59:59,0000000Z")]
    [InlineData("2020-01-01T23:59:59.0000000z")]
    [InlineData("2020-01-01T00:00:00Z")]
    [InlineData("2020-01-01")]
    [InlineData("2024-02-29")]
    [InlineData("2023-02-29")]
    [InlineData("0000-01-01")]
    [InlineData("2020-13-01")]
    [InlineData("2020-1-01")]
    [InlineData("12:34:56")]
    [InlineData("12:34:56.1")]
    [InlineData("12:34:56.1234567")]
    [InlineData("23:59:59.9999999")]
    [InlineData("24:00:00")]
    [InlineData("12:34:56.")]
    [InlineData("12:34:56.12345678")]
    [InlineData("1:34:56")]
    [InlineData("not a date")]
    [InlineData("")]
    public void Iso8601_MatchesBcl(string value)
    {
        // The fast parsers accept a subset of what the BCL accepts, and give the same values
        var utf8 = Encoding.UTF8.GetBytes(value);
        var invariant = CultureInfo.InvariantCulture;
        if (Serialization.Converters.Iso8601.TryParse(value, out DateTime dateTime) | Serialization.Converters.Iso8601.TryParse(utf8, out DateTime utf8DateTime))
        {
            var expected = DateTime.Parse(value, invariant, DateTimeStyles.RoundtripKind);
            Assert.Equal((expected, expected.Kind), (dateTime, dateTime.Kind));
            Assert.Equal((expected, expected.Kind), (utf8DateTime, utf8DateTime.Kind));
        }

        if (Serialization.Converters.Iso8601.TryParse(value, out DateTimeOffset offset) | Serialization.Converters.Iso8601.TryParse(utf8, out DateTimeOffset utf8Offset))
        {
            var expected = DateTimeOffset.Parse(value, invariant, DateTimeStyles.None);
            Assert.Equal((expected, expected.Offset), (offset, offset.Offset));
            Assert.Equal((expected, expected.Offset), (utf8Offset, utf8Offset.Offset));
        }

        if (Serialization.Converters.Iso8601.TryParse(value.AsSpan(), out DateOnly date) | Serialization.Converters.Iso8601.TryParse((ReadOnlySpan<byte>)utf8, out DateOnly utf8Date))
        {
            Assert.Equal(DateOnly.ParseExact(value, "O", invariant), date);
            Assert.Equal(date, utf8Date);
        }

        if (Serialization.Converters.Iso8601.TryParse(value.AsSpan(), out TimeOnly time) | Serialization.Converters.Iso8601.TryParse((ReadOnlySpan<byte>)utf8, out TimeOnly utf8Time))
        {
            Assert.Equal(TimeOnly.Parse(value, invariant), time);
            Assert.Equal(time, utf8Time);
        }
    }

    [Fact]
    public void Iso8601_ParsesRoundTripFormat()
    {
        // The values written by the converters must take the fast path
        var dateTime = new DateTime(2020, 3, 5, 12, 34, 56, DateTimeKind.Utc).AddTicks(7890123);
        var offset = new DateTimeOffset(2020, 3, 5, 12, 34, 56, TimeSpan.FromMinutes(-150)).AddTicks(1);
        foreach (var value in new[] { dateTime, DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified) })
        {
            var text = value.ToString("O", CultureInfo.InvariantCulture);
            Assert.True(Serialization.Converters.Iso8601.TryParse(text, out DateTime result));
            Assert.Equal((value, value.Kind), (result, result.Kind));
            Assert.True(Serialization.Converters.Iso8601.TryParse(Encoding.UTF8.GetBytes(text), out result));
            Assert.Equal((value, value.Kind), (result, result.Kind));
        }

        var offsetText = offset.ToString("O", CultureInfo.InvariantCulture);
        Assert.True(Serialization.Converters.Iso8601.TryParse(offsetText, out DateTimeOffset offsetResult));
        Assert.Equal((offset, offset.Offset), (offsetResult, offsetResult.Offset));
        Assert.True(Serialization.Converters.Iso8601.TryParse(Encoding.UTF8.GetBytes(offsetText), out offsetResult));
        Assert.Equal((offset, offset.Offset), (offsetResult, offsetResult.Offset));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0")]
    [InlineData("0.00")]
    [InlineData("-0.00")]
    [InlineData("1.50")]
    [InlineData("-123.45")]
    [InlineData("007")]
    [InlineData("123456789012345678")]
    [InlineData("-12345678901234567.8")]
    [InlineData("1234567890123456789")]
    [InlineData("0.000000000000000001")]
    [InlineData("79228162514264337593543950335")]
    [InlineData("1.")]
    [InlineData(".5")]
    [InlineData("1..2")]
    [InlineData("1.2.3")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1,000")]
    [InlineData("1e3")]
    [InlineData("-")]
    [InlineData("")]
    public void Decimal_MatchesBcl(string value)
    {
        // The fast parser accepts a subset of what the BCL accepts, and gives the same values, with the same scale and sign
        var utf8 = Encoding.UTF8.GetBytes(value);
        var parsed = Serialization.Converters.DecimalCsvConverter.TryParsePlain(value.AsSpan(), out var result);
        Assert.Equal(parsed, Serialization.Converters.DecimalCsvConverter.TryParsePlain((ReadOnlySpan<byte>)utf8, out var utf8Result));
        if (parsed)
        {
            var expected = decimal.Parse(value, CultureInfo.InvariantCulture);
            Assert.Equal(decimal.GetBits(expected), decimal.GetBits(result));
            Assert.Equal(decimal.GetBits(expected), decimal.GetBits(utf8Result));
        }

        var options = new CsvSerializerOptions { HasHeaderRow = false, Separator = ';' };
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var bcl))
        {
            Assert.Equal(decimal.GetBits(bcl), decimal.GetBits(CsvSerializer.Deserialize<DecimalModel>(value, options)[0].Value));
            Assert.Equal(decimal.GetBits(bcl), decimal.GetBits(CsvSerializer.Deserialize<DecimalModel>(utf8, options)[0].Value));
        }
    }

    [Fact]
    public void Decimal_FormatMatchesBcl()
    {
        // The fast formatter writes the values as the BCL does with the invariant culture, in UTF-16 and in UTF-8, or leaves them to the BCL
        var values = new List<decimal> { 0m, 1m, -1m, 0.5m, -0.05m, 1.50m, 0.00m, 123456789.123456789m, ulong.MaxValue, -(decimal)ulong.MaxValue, decimal.MaxValue, decimal.MinValue, new(0, 0, 0, isNegative: true, scale: 3), new(1, 0, 0, isNegative: false, scale: 28) };
        var state = 0x9E3779B97F4A7C15UL;
        for (var i = 0; i < 10_000; i++)
        {
            values.Add(new decimal(Next(ref state), Next(ref state) % 4 == 0 ? 0 : Next(ref state), Next(ref state) % 8 == 0 ? Next(ref state) : 0, Next(ref state) % 2 == 0, (byte)((uint)Next(ref state) % 29)));
        }

        // xorshift: deterministic values, without the warnings about Random
        static int Next(ref ulong state)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            return (int)state;
        }

        Span<char> chars = stackalloc char[64];
        Span<byte> bytes = stackalloc byte[64];
        foreach (var value in values)
        {
            var expected = value.ToString(CultureInfo.InvariantCulture);
            if (Serialization.Converters.DecimalCsvConverter.TryFormatPlain(value, chars, out var written, out var tooSmall))
            {
                Assert.False(tooSmall);
                Assert.Equal(expected, chars[..written].ToString());
                Assert.True(Serialization.Converters.DecimalCsvConverter.TryFormatPlain(value, bytes, out written, out _));
                Assert.Equal(expected, Encoding.UTF8.GetString(bytes[..written]));
                Assert.True(Serialization.Converters.DecimalCsvConverter.TryFormatPlain(value, chars[..(expected.Length - 1)], out _, out tooSmall));
                Assert.True(tooSmall);
            }
        }

        Assert.True(Serialization.Converters.DecimalCsvConverter.TryFormatPlain(-123.450m, chars, out var plainWritten, out _));
        Assert.Equal("-123.450", chars[..plainWritten].ToString());

        var models = values.Select(value => new DecimalModel { Value = value }).ToList();
        Assert.Equal(string.Concat(values.Select(value => value.ToString(CultureInfo.InvariantCulture) + "\r\n")), CsvSerializer.Serialize(models, new CsvSerializerOptions { HasHeaderRow = false }));
    }

    [Fact]
    public void Metadata_ParameterWithoutMember()
    {
        // A constructor parameter with a default value and no member gets its default value
        var options = new CsvSerializerOptions();
        var typeInfo = CsvMetadataServices.CreateObjectInfo(options, new CsvObjectInfoValues<LabeledItem>
        {
            ObjectWithParameterizedConstructorCreator = args => new LabeledItem(args.GetValue<int>(0), args.GetValue<string>(1), args.GetValue<int>(2)),
            ConstructorParameters =
            [
                new CsvParameterInfoValues { Name = "id", ParameterType = typeof(int), Position = 0 },
                new CsvParameterInfoValues { Name = "label", ParameterType = typeof(string), Position = 1, HasDefaultValue = true, DefaultValue = "none" },
                new CsvParameterInfoValues { Name = "rank", ParameterType = typeof(int), Position = 2, HasDefaultValue = true, DefaultValue = null },
            ],
            Properties =
            [
                CsvMetadataServices.CreatePropertyInfo(options, new CsvPropertyInfoValues<LabeledItem, int> { MemberName = "Id", Getter = item => item.Id, ParameterIndex = 0 }),
            ],
        });

        var items = CsvSerializer.Deserialize("Id\n1\n2", typeInfo);
        Assert.Equal(["1|none|0", "2|none|0"], items.Select(item => $"{item.Id.ToString(CultureInfo.InvariantCulture)}|{item.Label}|{item.Rank.ToString(CultureInfo.InvariantCulture)}"));

        // Without a default value, the parameter needs a member
        Assert.Throws<InvalidOperationException>(() => CsvMetadataServices.CreateObjectInfo(options, new CsvObjectInfoValues<LabeledItem>
        {
            ObjectWithParameterizedConstructorCreator = args => new LabeledItem(args.GetValue<int>(0), args.GetValue<string>(1), 0),
            ConstructorParameters =
            [
                new CsvParameterInfoValues { Name = "id", ParameterType = typeof(int), Position = 0 },
                new CsvParameterInfoValues { Name = "label", ParameterType = typeof(string), Position = 1 },
            ],
            Properties =
            [
                CsvMetadataServices.CreatePropertyInfo(options, new CsvPropertyInfoValues<LabeledItem, int> { MemberName = "Id", Getter = item => item.Id, ParameterIndex = 0 }),
            ],
        }));
    }

    [Fact]
    public void Booleans()
    {
        var result = CsvSerializer.Deserialize<BooleanModel>("Value\ntrue\nFALSE\n1\n0");
        Assert.Equal([true, false, true, false], result.Select(r => r.Value));
        Assert.Throws<CsvException>(() => CsvSerializer.Deserialize<BooleanModel>("Value\nyes"));
        Assert.Throws<CsvException>(() => CsvSerializer.Deserialize<BooleanModel>("Value\nyes"u8.ToArray()));
    }

    [Fact]
    public void Members()
    {
        var model = CsvSerializer.Deserialize<MembersModel>("Init,Required,Private,PrivateSetter,Field,ReadOnly\n1,2,3,4,5,6")[0];
        Assert.Equal(1, model.Init);
        Assert.Equal(2, model.Required);
        Assert.Equal(3, model.GetPrivate());
        Assert.Equal(0, model.PrivateSetter); // A private setter is not used without CsvIncludeAttribute
        Assert.Equal(5, model.Field);
        Assert.Equal(0, model.ReadOnly);

        Assert.Equal("Init,Required,Private,PrivateSetter,ReadOnly,Field\r\n1,2,3,0,0,5\r\n", CsvSerializer.Serialize([model]));
    }

    [Fact]
    public void Fields()
    {
        var options = new CsvSerializerOptions { IncludeFields = true };
        var csv = CsvSerializer.Serialize([new FieldsModel { A = 1, B = "x" }], options);
        Assert.Equal("A,B\r\n1,x\r\n", csv);
        Assert.Equal("x", CsvSerializer.Deserialize<FieldsModel>(csv, options)[0].B);
        Assert.Equal("\r\n\r\n", CsvSerializer.Serialize([new FieldsModel()]));
    }

    [Fact]
    public void Struct()
    {
        var csv = CsvSerializer.Serialize([new StructModel { X = 1, Y = 2 }]);
        Assert.Equal("X,Y\r\n1,2\r\n", csv);
        Assert.Equal(new StructModel { X = 1, Y = 2 }, CsvSerializer.Deserialize<StructModel>(csv)[0]);
    }

    [Fact]
    public void Inheritance()
    {
        var csv = CsvSerializer.Serialize([new DerivedModel { Base = 1, Derived = 2, Virtual = 3 }]);
        Assert.Equal("Base,Virtual,Derived\r\n1,3,2\r\n", csv);
        var model = CsvSerializer.Deserialize<DerivedModel>(csv)[0];
        Assert.Equal(3, model.Virtual);
    }

    [Fact]
    public void Callbacks()
    {
        var model = CsvSerializer.Deserialize<CallbackModel>("Value\n1")[0];
        Assert.Equal(["OnDeserializing", "OnDeserialized"], model.Calls);
        CsvSerializer.Serialize([model]);
        Assert.Equal(["OnDeserializing", "OnDeserialized", "OnSerializing", "OnSerialized"], model.Calls);
    }

    [Fact]
    public void ConstructorWithDefaultValues()
    {
        var result = CsvSerializer.Deserialize<DefaultValuesModel>("A\n1");
        Assert.Equal(1, result[0].A);
        Assert.Equal(42, result[0].B);
        Assert.Equal("default", result[0].C);
        Assert.Equal(Color.Blue, result[0].Color);
        Assert.Null(result[0].Optional);
        Assert.Equal(1.5m, result[0].Ratio);
        Assert.Equal('z', result[0].Letter);

        var generated = CsvSerializer.Deserialize("A,Optional\n1,7", TestCsvContext.Default.DefaultValuesModel);
        Assert.Equal(Color.Blue, generated[0].Color);
        Assert.Equal(7, generated[0].Optional);
        Assert.Equal(1.5m, generated[0].Ratio);
        Assert.Equal('z', generated[0].Letter);
    }

    [Fact]
    public void ConstructorParameterWithoutMember()
    {
        var result = CsvSerializer.Deserialize<ParameterOnlyModel>("value,factor\n2,3");
        Assert.Equal(6, result[0].Product);
        Assert.Equal("Product\r\n6\r\n", CsvSerializer.Serialize(result)); // The parameters without a member are not written
    }

    [Fact]
    public void SelectedConstructor()
    {
        var result = CsvSerializer.Deserialize<SelectedConstructorModel>("Value\n2");
        Assert.Equal("ctor:2", result[0].Value);
        Assert.Throws<InvalidOperationException>(() => CsvSerializer.Deserialize<AmbiguousConstructorModel>("Value\n2"));
    }

    [Fact]
    public void ShortRecords()
    {
        var result = CsvSerializer.Deserialize<Person>("Name,Age\nAlice\n\nBob,25");
        Assert.HasCount(2, result);
        Assert.Equal(0, result[0].Age);
        Assert.Equal(25, result[1].Age);
    }

    [Fact]
    public void NullRecord()
    {
        Assert.Throws<ArgumentException>(() => CsvSerializer.Serialize<Person>([null!]));
    }

    [Fact]
    public void Comments()
    {
        var options = new CsvSerializerOptions { CommentCharacter = '#', Trim = CsvTrimOptions.Outer };
        var result = CsvSerializer.Deserialize<Person>("# comment\nName , Age\n Alice , 30 ", options);
        Assert.Equal("Alice", result[0].Name);
        Assert.Equal(30, result[0].Age);
    }

    [Fact]
    public void EscapeFormulas()
    {
        var options = new CsvSerializerOptions { EscapeFormulas = true };
        Assert.Equal("Name,Age,BirthDate\r\n'=1+1,-5,\r\n", CsvSerializer.Serialize([new Person { Name = "=1+1", Age = -5 }], options));
    }

    [Fact]
    public void TypeInfo()
    {
        var typeInfo = CsvSerializerOptions.Default.GetTypeInfo<Order>();
        Assert.Equal(["Id", "customer_name", "Date"], typeInfo.Properties.Select(p => p.Name));
        Assert.Same(typeInfo, CsvSerializerOptions.Default.GetTypeInfo<Order>());
        Assert.Equal("dd/MM/yyyy", typeInfo.Properties[2].Format);
    }

    [Fact]
    public void UnsupportedTypes()
    {
        Assert.Throws<NotSupportedException>(() => CsvSerializer.Deserialize<IDisposable>("a"));
        Assert.Throws<NotSupportedException>(() => CsvSerializer.Deserialize<Stream>("a"));
    }

    [Fact]
    public void OptionsCopy()
    {
        var options = new CsvSerializerOptions { Separator = ';', PropertyNameCaseInsensitive = true, Converters = [new PointConverter()] };
        var copy = new CsvSerializerOptions(options) { NewLine = "\n" };
        Assert.Equal(';', copy.Separator);
        Assert.True(copy.PropertyNameCaseInsensitive);
        Assert.Single(copy.Converters);
        Assert.Equal("\n", copy.NewLine);
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> values)
    {
        var result = new List<T>();
        await foreach (var value in values)
        {
            result.Add(value);
        }

        return result;
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> values)
    {
        foreach (var value in values)
        {
            await Task.Yield();
            yield return value;
        }
    }
}

#region CsvSerializer_PersonModel
public sealed class Person
{
    public string? Name { get; set; }

    public int Age { get; set; }

    public DateOnly? BirthDate { get; set; }
}
#endregion

#region CsvSerializer_ProductModel
public sealed record Product(int Id, string Name, decimal Price);
#endregion

#region CsvSerializer_OrderModel
public sealed class Order
{
    [CsvPropertyOrder(-1)] // Written first
    public int Id { get; set; }

    [CsvPropertyName("customer_name")]
    public string? Customer { get; set; }

    [CsvFormat("dd/MM/yyyy")]
    public DateTime Date { get; set; }

    [CsvIgnore]
    public string? Secret { get; set; }
}
#endregion

#region CsvSerializer_MeasureModel
public sealed class Measure
{
    [CsvColumnIndex(0)]
    public DateOnly Date { get; set; }

    [CsvColumnIndex(2)]
    public double Value { get; set; }
}
#endregion

#region CsvSerializer_ConverterModel
public readonly record struct Point(int X, int Y);

public sealed class PointConverter : CsvConverter<Point>
{
    public override Point Read(ReadOnlySpan<char> value, CsvConverterContext context)
    {
        var separator = value.IndexOf(';');
        if (separator < 0)
            throw new FormatException("A point is written 'x;y'.");

        return new Point(int.Parse(value[..separator], context.FormatProvider), int.Parse(value[(separator + 1)..], context.FormatProvider));
    }

    public override bool TryFormat(Point value, Span<char> destination, out int charsWritten, CsvConverterContext context)
        => destination.TryWrite(context.FormatProvider, $"{value.X};{value.Y}", out charsWritten);
}

public sealed class UpperCaseConverter : CsvConverter<string?>
{
    public override string? Read(ReadOnlySpan<char> value, CsvConverterContext context) => value.ToString().ToLowerInvariant();

    public override bool TryFormat(string? value, Span<char> destination, out int charsWritten, CsvConverterContext context)
    {
        charsWritten = 0;
        if (value is null)
            return true;

        if (destination.Length < value.Length)
            return false;

        charsWritten = value.AsSpan().ToUpperInvariant(destination);
        return true;
    }
}

public sealed class Shape
{
    [CsvConverter(typeof(UpperCaseConverter))]
    public string? Name { get; set; }

    public Point Start { get; set; }
}
#endregion

public sealed class RequiredModel
{
    public string? Optional { get; set; }

    public required int Value { get; set; }

    [CsvRequired]
    public int Attributed { get; set; }
}

public sealed class BooleanModel
{
    public bool Value { get; set; }
}

public sealed class LabeledItem(int id, string label, int rank)
{
    public int Id { get; } = id;

    public string Label { get; } = label;

    public int Rank { get; } = rank;
}

public sealed class DecimalModel
{
    public decimal Value { get; set; }
}

public enum Color
{
    Red,
    Green,
    Blue,
}

[Flags]
public enum Permissions
{
    None = 0,
    Read = 1,
    Write = 2,
}

public sealed class AllTypes
{
    public string? String { get; set; }
    public bool Boolean { get; set; }
    public char Char { get; set; }
    public byte Byte { get; set; }
    public sbyte SByte { get; set; }
    public short Int16 { get; set; }
    public ushort UInt16 { get; set; }
    public int Int32 { get; set; }
    public uint UInt32 { get; set; }
    public long Int64 { get; set; }
    public ulong UInt64 { get; set; }
    public Int128 Int128 { get; set; }
    public UInt128 UInt128 { get; set; }
    public nint IntPtr { get; set; }
    public nuint UIntPtr { get; set; }
    public Half Half { get; set; }
    public float Single { get; set; }
    public double Double { get; set; }
    public decimal Decimal { get; set; }
    public BigInteger BigInteger { get; set; }
    public Guid Guid { get; set; }
    public DateTime DateTime { get; set; }
    public DateTimeOffset DateTimeOffset { get; set; }
    public DateOnly DateOnly { get; set; }
    public TimeOnly TimeOnly { get; set; }
    public TimeSpan TimeSpan { get; set; }
    public Uri? Uri { get; set; }
    public Version? Version { get; set; }
    public byte[]? Bytes { get; set; }
    public Color Color { get; set; }
    public Permissions Permissions { get; set; }
    public int? NullableInt32 { get; set; }
    public Color? NullableColor { get; set; }
    public Guid? NullableGuid { get; set; }
    public DateTime? NullableDateTime { get; set; }
    public Complex Complex { get; set; }

    public static AllTypes CreateSample() => new()
    {
        String = "text, with \"quotes\"",
        Boolean = true,
        Char = ',',
        Byte = 1,
        SByte = -2,
        Int16 = -3,
        UInt16 = 4,
        Int32 = -5,
        UInt32 = 6,
        Int64 = -7,
        UInt64 = 8,
        Int128 = Int128.MaxValue,
        UInt128 = UInt128.MaxValue,
        IntPtr = -9,
        UIntPtr = 10,
        Half = (Half)1.5,
        Single = 1.25f,
        Double = 3.14159,
        Decimal = 12.345m,
        BigInteger = BigInteger.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture),
        Guid = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"),
        DateTime = new DateTime(2026, 11, 21, 10, 30, 0, DateTimeKind.Utc),
        DateTimeOffset = new DateTimeOffset(2026, 11, 21, 10, 30, 0, TimeSpan.FromHours(2)),
        DateOnly = new DateOnly(2026, 11, 21),
        TimeOnly = new TimeOnly(10, 30, 15),
        TimeSpan = TimeSpan.FromMinutes(90),
        Uri = new Uri("https://example.com/a?b=c"),
        Version = new Version(1, 2, 3),
        Bytes = [1, 2, 3, 255],
        Color = Color.Blue,
        Permissions = Permissions.Read | Permissions.Write,
        NullableInt32 = 42,
        NullableColor = Color.Green,
        NullableGuid = Guid.Empty,
        NullableDateTime = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Local),
        Complex = new Complex(1, 2),
    };

    public string Describe() => string.Join("|", typeof(AllTypes).GetProperties().Select(p => p.GetValue(this) switch
    {
        null => "null",
        byte[] bytes => Convert.ToBase64String(bytes),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        var value => value.ToString(),
    }));
}

public sealed class MembersModel
{
    private int _private;

    public int Init { get; init; }

    public required int Required { get; set; }

    [CsvInclude]
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "The serializer reads and writes it")]
    private int Private { get => _private; set => _private = value; }

    public int PrivateSetter { get; private set; }

    public int ReadOnly { get; }

    [CsvInclude]
    public int Field;

    public int GetPrivate() => _private;
}

public sealed class FieldsModel
{
    public int A;
    public string? B;
}

public struct StructModel
{
    public int X { get; set; }

    public int Y { get; set; }
}

public class BaseModel
{
    public int Base { get; set; }

    public virtual int Virtual { get; set; }
}

public sealed class DerivedModel : BaseModel
{
    public int Derived { get; set; }

    public override int Virtual { get; set; }
}

public sealed class CallbackModel : ICsvOnDeserializing, ICsvOnDeserialized, ICsvOnSerializing, ICsvOnSerialized
{
    public int Value { get; set; }

    [CsvIgnore]
    public List<string> Calls { get; } = [];

    public void OnDeserializing() => Calls.Add(nameof(OnDeserializing));

    public void OnDeserialized() => Calls.Add(nameof(OnDeserialized));

    public void OnSerializing() => Calls.Add(nameof(OnSerializing));

    public void OnSerialized() => Calls.Add(nameof(OnSerialized));
}

public sealed class DefaultValuesModel(int a, int b = 42, string c = "default", Color color = Color.Blue, int? optional = null, decimal ratio = 1.5m, char letter = 'z')
{
    public int A { get; } = a;

    public int B { get; } = b;

    public string C { get; } = c;

    public Color Color { get; } = color;

    public int? Optional { get; } = optional;

    public decimal Ratio { get; } = ratio;

    public char Letter { get; } = letter;
}

public sealed class ParameterOnlyModel(int value, int factor)
{
    public int Product { get; } = value * factor;
}

public sealed class SelectedConstructorModel
{
    public SelectedConstructorModel()
    {
    }

    [CsvConstructor]
    public SelectedConstructorModel(string value)
    {
        Value = "ctor:" + value;
    }

    public string? Value { get; }
}

public sealed class AmbiguousConstructorModel
{
    public AmbiguousConstructorModel(int value)
    {
        Value = value;
    }

    public AmbiguousConstructorModel(string value)
    {
        Value = value.Length;
    }

    public int Value { get; }
}
