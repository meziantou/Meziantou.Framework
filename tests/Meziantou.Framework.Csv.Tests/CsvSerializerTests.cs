using System.Buffers;
using System.Numerics;
using System.Text;
using Meziantou.Framework.Csv.Internals;
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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(33)]
    [InlineData(8_192)]
    [InlineData(8_193)]
    [InlineData(30_001)]
    public async Task Deserialize_RecordCount(int count)
    {
        // The records are collected in segments, then copied to the list: the list has every record, in order, whatever the number of segments
        var csv = string.Concat(Enumerable.Range(0, count).Select(i => i.ToString(CultureInfo.InvariantCulture) + "\n"));
        var bytes = Encoding.UTF8.GetBytes(csv);
        var options = new CsvSerializerOptions { HasHeaderRow = false };
        var expected = Enumerable.Range(0, count).Select(i => (decimal)i).ToList();

        Assert.Equal(expected, Values(CsvSerializer.Deserialize<DecimalModel>(csv, options)));
        Assert.Equal(expected, Values(CsvSerializer.Deserialize<DecimalModel>(bytes, options)));
        Assert.Equal(expected, Values(CsvSerializer.Deserialize<DecimalModel>(new StringReader(csv), options)));
        Assert.Equal(expected, Values(CsvSerializer.Deserialize<DecimalModel>(new MemoryStream(bytes), options)));
        Assert.Equal(expected, Values(await CsvSerializer.DeserializeAsync<DecimalModel>(new MemoryStream(bytes), options)));
        Assert.Equal(expected, Values(await CsvSerializer.DeserializeAsync<DecimalModel>(new StringReader(csv), options)));

        // A value type is collected in segments of its own size
        var structs = CsvSerializer.Deserialize<StructModel>(csv, options);
        Assert.HasCount(count, structs);
        Assert.Equal(Enumerable.Range(0, count), structs.Select(item => item.X));

        static List<decimal> Values(List<DecimalModel> models)
        {
            Assert.Equal(models.Count, models.Capacity);
            return [.. models.Select(model => model.Value)];
        }
    }

    [Fact]
    public void Serialize_TextIsWrittenAsTheWriterWritesIt()
    {
        // The text of the default converter is written by the writer: quoted and escaped as needed, in every quote mode, as UTF-16 and as UTF-8, with
        // long values that grow a small buffer while they are converted
        string?[] names = [null, "", "a", "plain text of 20 ch", "a,b", "\"", "x\ny", new string('a', 17) + "\r", "=1", "é", "日\"本😀", "\u012C" + new string('b', 20), new string('a', 5000), new string('é', 3000), new string('a', 4000) + "é,"];
        CsvSerializerOptions[] allOptions = [new(), new() { QuoteMode = CsvQuoteMode.Always }, new() { EscapeFormulas = true }, new() { Separator = ';', Quote = '\'' }, new() { BufferSize = 16 }, new() { BufferSize = 16, QuoteMode = CsvQuoteMode.Always }];
        foreach (var options in allOptions)
        {
            var writerOption = new CsvWriterOptions { Separator = options.Separator, Quote = options.Quote, QuoteMode = options.QuoteMode, EscapeFormulas = options.EscapeFormulas };
            var people = names.Select(name => new Person { Name = name, Age = 1 }).ToArray();

            var expected = new StringBuilder();
            using (var writer = CsvWriter.Create(expected, writerOption))
            {
                writer.WriteRow("Name", "Age", "BirthDate");
                foreach (var person in people)
                {
                    writer.WriteField(person.Name);
                    writer.WriteField(person.Age);
                    writer.WriteField("");
                    writer.EndRow();
                }
            }

            Assert.Equal(expected.ToString(), CsvSerializer.Serialize(people, options));

            using var stream = new MemoryStream();
            CsvSerializer.Serialize(stream, people, options);
            Assert.Equal(expected.ToString(), Encoding.UTF8.GetString(stream.ToArray()));
        }
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
    public void Utf8StringsOfAsciiAndOtherRecords()
    {
        // The strings of the records whose bytes are all ASCII are not validated, for the members and the constructor parameters: a character from
        // U+0080 anywhere, in a record or in a record that shares a SIMD block with it, gives the strings of UTF-16 data
        for (var length = 0; length < 140; length++)
        {
            var sb = new StringBuilder("Id,Name,Price\n");
            for (var record = 0; record < 8; record++)
            {
                var name = new string('a', length) + (record % 4) switch { 1 => "é", 2 => "😀", 3 => "", _ => "b" };
                sb.Append(record).Append(',').Append(record % 3 is 0 ? "\"" + name + "\"\"x\"" : name).Append(",1.5\n");
            }

            var text = sb.ToString();
            var bytes = Encoding.UTF8.GetBytes(text);
            Assert.Equal(CsvSerializer.Deserialize<Product>(text), CsvSerializer.Deserialize<Product>(bytes));

            var expected = CsvSerializer.Deserialize<Person>(text.Replace("Id,", "Age,", StringComparison.Ordinal)).Select(person => person.Name);
            var actual = CsvSerializer.Deserialize<Person>(Encoding.UTF8.GetBytes(text.Replace("Id,", "Age,", StringComparison.Ordinal))).Select(person => person.Name);
            Assert.Equal(expected, actual);
        }
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
    public void DeserializationErrorHandler()
    {
        #region CsvSerializer_DeserializationErrorHandler
        var errors = new List<string>();
        var options = new CsvSerializerOptions
        {
            DeserializationErrorHandler = context =>
            {
                errors.Add($"line {context.LineNumber}: '{context.RawValue}' is not a valid {context.MemberName}");
                return CsvDeserializationErrorAction.SkipRecord; // or Ignore to keep the default value, or Throw
            },
        };

        var products = CsvSerializer.Deserialize<Product>("Id,Name,Price\n1,Keyboard,49.90\nx,Mouse,19.99\n3,Screen,199", options);

        Assert.Equal(["Keyboard", "Screen"], products.Select(p => p.Name));
        Assert.Equal(["line 3: 'x' is not a valid Id"], errors);
        #endregion
    }

    [Fact]
    public async Task DeserializationErrorHandler_AllInputs()
    {
        const string Csv = "Name,Age,BirthDate\nAlice,30,2000-01-02\nBob,x,\nCharlie,20,not a date\nDavid,40,\n";
        var bytes = Encoding.UTF8.GetBytes(Csv);
        foreach (var (action, expected) in new (CsvDeserializationErrorAction, string[])[]
        {
            (CsvDeserializationErrorAction.SkipRecord, ["Alice|30|2000-01-02", "David|40|"]),
            (CsvDeserializationErrorAction.Ignore, ["Alice|30|2000-01-02", "Bob|0|", "Charlie|20|", "David|40|"]),
        })
        {
            var errors = new List<string>();
            var options = new CsvSerializerOptions
            {
                DeserializationErrorHandler = context =>
                {
                    errors.Add(string.Create(CultureInfo.InvariantCulture, $"{context.Kind} {context.RowIndex} {context.LineNumber} {context.ColumnIndex} {context.ColumnName} {context.MemberName} {context.RawValue} {context.Type.Name} {context.Exception.InnerException?.GetType().Name}"));
                    return action;
                },
            };

            string[] expectedErrors = ["ConversionFailed 1 3 1 Age Age x Person FormatException", "ConversionFailed 2 4 2 BirthDate BirthDate not a date Person FormatException"];
            async Task Check(Func<Task<IEnumerable<Person>>> deserialize)
            {
                errors.Clear();
                Assert.Equal(expected, Describe(await deserialize()));
                Assert.Equal(expectedErrors, errors);
            }

            await Check(() => Task.FromResult<IEnumerable<Person>>(CsvSerializer.Deserialize<Person>(Csv, options)));
            await Check(() => Task.FromResult<IEnumerable<Person>>(CsvSerializer.Deserialize<Person>(bytes, options)));
            await Check(() => Task.FromResult<IEnumerable<Person>>(CsvSerializer.Deserialize<Person>(new StringReader(Csv), options)));
            await Check(() => Task.FromResult<IEnumerable<Person>>(CsvSerializer.Deserialize<Person>(new MemoryStream(bytes), options)));
            await Check(async () => await CsvSerializer.DeserializeAsync<Person>(new MemoryStream(bytes), options));
            await Check(async () => await CsvSerializer.DeserializeAsync<Person>(new StringReader(Csv), options));
            await Check(() => Task.FromResult<IEnumerable<Person>>(CsvSerializer.DeserializeEnumerable<Person>(new StringReader(Csv), options).ToList()));
            await Check(() => Task.FromResult<IEnumerable<Person>>(CsvSerializer.DeserializeEnumerable<Person>(new MemoryStream(bytes), options).ToList()));
            await Check(async () => await ToListAsync(CsvSerializer.DeserializeAsyncEnumerable<Person>(new StringReader(Csv), options)));
            await Check(async () => await ToListAsync(CsvSerializer.DeserializeAsyncEnumerable<Person>(new MemoryStream(bytes), options)));
        }

        static string[] Describe(IEnumerable<Person> people) => [.. people.Select(p => $"{p.Name}|{p.Age.ToString(CultureInfo.InvariantCulture)}|{p.BirthDate?.ToString("O", CultureInfo.InvariantCulture)}")];
    }

    [Fact]
    public void DeserializationErrorHandler_Throw()
    {
        const string Csv = "Id,Name,Price\n1,Keyboard,49.90\nx,Mouse,19.99";
        var expected = Assert.Throws<CsvException>(() => CsvSerializer.Deserialize<Product>(Csv));
        CsvDeserializationErrorContext? context = null;
        var options = new CsvSerializerOptions { DeserializationErrorHandler = c => { context = c; return CsvDeserializationErrorAction.Throw; } };
        var exception = Assert.Throws<CsvException>(() => CsvSerializer.Deserialize<Product>(Csv, options));
        Assert.Equal(expected.Message, exception.Message);
        Assert.Same(context!.Exception, exception);
        Assert.Equal(31, context.Position);
        Assert.Equal(1, context.RowIndex);

        Assert.Throws<InvalidOperationException>(() => CsvSerializer.Deserialize<Product>(Csv, new CsvSerializerOptions { DeserializationErrorHandler = _ => (CsvDeserializationErrorAction)42 }));
        Assert.Throws<FormatException>(() => CsvSerializer.Deserialize<Product>(Csv, new CsvSerializerOptions { DeserializationErrorHandler = _ => throw new FormatException() }));
    }

    [Fact]
    public void DeserializationErrorHandler_ConstructorParameters()
    {
        // A parameter that cannot be converted gets its default value
        var options = new CsvSerializerOptions { DeserializationErrorHandler = _ => CsvDeserializationErrorAction.Ignore };
        var model = Assert.Single(CsvSerializer.Deserialize<DefaultValuesModel>("A,B,C\n1,x,y", options));
        Assert.Equal(1, model.A);
        Assert.Equal(42, model.B);
        Assert.Equal("y", model.C);

        var products = CsvSerializer.Deserialize<Product>("Id,Name,Price\nx,a,1\n2,b,y", options);
        Assert.Equal([new Product(0, "a", 1), new Product(2, "b", 0)], products);

        options = new CsvSerializerOptions { DeserializationErrorHandler = _ => CsvDeserializationErrorAction.SkipRecord };
        Assert.Empty(CsvSerializer.Deserialize<DefaultValuesModel>("A,B,C\n1,x,y", options));
    }

    [Fact]
    public void DeserializationErrorHandler_RequiredColumns()
    {
        var errors = new List<CsvDeserializationErrorContext>();
        foreach (var action in (CsvDeserializationErrorAction[])[CsvDeserializationErrorAction.Ignore, CsvDeserializationErrorAction.SkipRecord])
        {
            // A column of the header row is not about a record: the records are returned, without the value of the column
            errors.Clear();
            var options = new CsvSerializerOptions { DeserializationErrorHandler = context => { errors.Add(context); return action; } };
            var result = CsvSerializer.Deserialize<RequiredModel>("Optional,Value\nx,1\ny,2", options);
            Assert.Equal([1, 2], result.Select(r => r.Value));
            Assert.All(result, r => Assert.Equal(0, r.Attributed));
            var error = Assert.Single(errors);
            Assert.Equal(CsvDeserializationErrorKind.MissingColumn, error.Kind);
            Assert.Equal("Attributed", error.MemberName);
            Assert.Null(error.ColumnIndex);

            // A record without a value for a required column
            errors.Clear();
            result = CsvSerializer.Deserialize<RequiredModel>("Optional,Value,Attributed\nx,1\ny,2,3", options);
            Assert.Equal(action is CsvDeserializationErrorAction.Ignore ? ["x|1|0", "y|2|3"] : ["y|2|3"], result.Select(r => $"{r.Optional}|{r.Value}|{r.Attributed}"));
            error = Assert.Single(errors);
            Assert.Equal(CsvDeserializationErrorKind.MissingRequiredValue, error.Kind);
            Assert.Equal("Attributed", error.MemberName);
            Assert.Equal("Attributed", error.ColumnName);
            Assert.Equal(2, error.ColumnIndex);
            Assert.Equal(0, error.RowIndex);
        }
    }

    [Fact]
    public void DeserializationErrorHandler_MissingAndUnmappedColumns()
    {
        var errors = new List<CsvDeserializationErrorContext>();
        var options = new CsvSerializerOptions
        {
            MissingColumnHandling = CsvMissingColumnHandling.Throw,
            UnmappedColumnHandling = CsvUnmappedColumnHandling.Throw,
            DeserializationErrorHandler = context => { errors.Add(context); return CsvDeserializationErrorAction.SkipRecord; },
        };

        var people = CsvSerializer.Deserialize<Person>("Name,Country\nAlice,France\nBob,Spain", options);
        Assert.Equal(["Alice", "Bob"], people.Select(p => p.Name));
        Assert.Equal(["UnmappedColumn 1 Country ", "MissingColumn   Age", "MissingColumn   BirthDate"], errors.Select(e => $"{e.Kind} {e.ColumnIndex} {e.ColumnName} {e.MemberName}"));

        // Without a header row, the columns after the last member are checked for each record
        errors.Clear();
        options = new CsvSerializerOptions { HasHeaderRow = false, UnmappedColumnHandling = CsvUnmappedColumnHandling.Throw, DeserializationErrorHandler = context => { errors.Add(context); return context.RowIndex == 0 ? CsvDeserializationErrorAction.Ignore : CsvDeserializationErrorAction.SkipRecord; } };
        var products = CsvSerializer.Deserialize<Product>("1,a,2,extra\n2,b,3,extra\n3,c,4", options);
        Assert.Equal([1, 3], products.Select(p => p.Id));
        Assert.Equal([0L, 1L], errors.Select(e => e.RowIndex!.Value));
        Assert.All(errors, e => Assert.Equal(CsvDeserializationErrorKind.UnmappedColumn, e.Kind));
        Assert.All(errors, e => Assert.Equal(3, e.ColumnIndex));

        // Each extra column is reported: the handler can accept one and not the next
        errors.Clear();
        options = new CsvSerializerOptions { HasHeaderRow = false, UnmappedColumnHandling = CsvUnmappedColumnHandling.Throw, DeserializationErrorHandler = context => { errors.Add(context); return context.ColumnIndex == 3 ? CsvDeserializationErrorAction.Ignore : CsvDeserializationErrorAction.SkipRecord; } };
        products = CsvSerializer.Deserialize<Product>("1,a,2,extra\n2,b,3,extra,extra\n3,c,4", options);
        Assert.Equal([1, 3], products.Select(p => p.Id));
        Assert.Equal(["0 3", "1 3", "1 4"], errors.Select(e => $"{e.RowIndex} {e.ColumnIndex}"));
    }

    [Fact]
    public void DeserializationErrorHandler_SetterExceptionsAreNotReported()
    {
        var calls = 0;
        var options = new CsvSerializerOptions { DeserializationErrorHandler = _ => { calls++; return CsvDeserializationErrorAction.Ignore; } };
        var exception = Assert.Throws<CsvException>(() => CsvSerializer.Deserialize<ValidatingSetterModel>("Value\n-1", options));
        Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
        Assert.Equal(0L, exception.RowIndex);
        Assert.Equal(0, calls);

        // The same exception as without a handler
        exception = Assert.Throws<CsvException>(() => CsvSerializer.Deserialize<ValidatingSetterModel>("Value\n-1"));
        Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
        exception = Assert.Throws<CsvException>(() => CsvSerializer.Deserialize<ValidatingSetterModel>("Value\n-1"u8.ToArray(), options));
        Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
        Assert.Equal(0, calls);

        // The conversion errors are still reported
        var models = CsvSerializer.Deserialize<ValidatingSetterModel>("Value\nabc\n2", options);
        Assert.Equal([0, 2], models.Select(m => m.Value));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void BadDataHandler_Serializer()
    {
        var errors = new List<CsvBadDataContext>();
        var options = new CsvSerializerOptions { BadDataHandler = context => { errors.Add(context); return CsvBadDataAction.Skip; } };
        var csv = "Id,Name,Price\n1,\"a\"b,2\n3,c,4";
        Assert.Equal([new Product(3, "c", 4)], CsvSerializer.Deserialize<Product>(csv, options));
        Assert.Equal([new Product(3, "c", 4)], CsvSerializer.Deserialize<Product>(Encoding.UTF8.GetBytes(csv), options));
        Assert.HasCount(2, errors);
        Assert.All(errors, e => Assert.Equal("Name", e.ColumnName));

        // Accept is the same as non-strict mode
        options = new CsvSerializerOptions { Strict = true, BadDataHandler = _ => CsvBadDataAction.Accept };
        Assert.Equal(CsvSerializer.Deserialize<Product>(csv), CsvSerializer.Deserialize<Product>(csv, options));
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
        // The numbers, the dates and the Boolean values of the default converters are not searched for characters that need quotes, unless one of
        // their characters is special: a separator or a quote that is a digit, a sign, a dot, a colon, T, Z or a letter of true or false still quotes them
        // A record with true and one with false, so that every letter of the Boolean values is written
        var falseValue = AllTypes.CreateSample();
        falseValue.Boolean = false;
        AllTypes[] values = [AllTypes.CreateSample(), falseValue];
        List<CsvSerializerOptions> options = [new() { QuoteMode = CsvQuoteMode.Always }];
        foreach (var c in "0123456789+-.:TZaeflrstu")
        {
            options.Add(new() { Separator = c });
            options.Add(new() { Quote = c });
        }

        foreach (var option in options)
        {
            var csv = CsvSerializer.Serialize(values, option);
            var result = CsvSerializer.Deserialize<AllTypes>(csv, option);
            Assert.Equal(values.Select(value => value.Describe()), result.Select(value => value.Describe()));

            using var stream = new MemoryStream();
            CsvSerializer.Serialize(stream, values, option);
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

    [Theory]
    [InlineData("0")]
    [InlineData("-0")]
    [InlineData("0.0")]
    [InlineData("-0.00")]
    [InlineData("007")]
    [InlineData("-007")]
    [InlineData("1.5")]
    [InlineData("-123.45")]
    [InlineData("0.1")]
    [InlineData("0.3")]
    [InlineData("123456789")]
    [InlineData("-123456789")]
    [InlineData("999999999")]
    [InlineData("1234567890")]
    [InlineData("2147483647")]
    [InlineData("-2147483648")]
    [InlineData("2147483648")]
    [InlineData("123456789012345678")]
    [InlineData("-123456789012345678")]
    [InlineData("999999999999999999")]
    [InlineData("9223372036854775807")]
    [InlineData("-9223372036854775808")]
    [InlineData("9223372036854775808")]
    [InlineData("999999999999999")]
    [InlineData("9999999999999999")]
    [InlineData("0.123456789012345")]
    [InlineData("0.1234567890123456")]
    [InlineData("12345678.9012345")]
    [InlineData("9007199254740993")]
    [InlineData("1.")]
    [InlineData(".5")]
    [InlineData("1..2")]
    [InlineData("1.2.3")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1,000")]
    [InlineData("1e3")]
    [InlineData("NaN")]
    [InlineData("-")]
    [InlineData("--1")]
    [InlineData("")]
    public void PlainNumbers_MatchBcl(string value)
    {
        // The fast parsers accept a subset of what the BCL accepts, and give the same values, with the same sign for zero
        var utf8 = Encoding.UTF8.GetBytes(value);
        var invariant = CultureInfo.InvariantCulture;
        AssertMatches<int>(value, utf8);
        AssertMatches<long>(value, utf8);

        // The doubles have their own converter: the fast parser of the integers rejects them, and the converter gives the value of the BCL
        AssertMatches<double>(value, utf8);

        // Other types and cultures go through the general parsers
        Assert.False(Serialization.Converters.PlainNumberParser.TryParse<char, short>(value, invariant, out _));
        Assert.False(Serialization.Converters.PlainNumberParser.TryParse<char, decimal>(value, invariant, out _));
        Assert.False(Serialization.Converters.PlainNumberParser.TryParse<char, int>(value, (CultureInfo)invariant.Clone(), out _));

        static void AssertMatches<T>(string value, byte[] utf8)
            where T : ISpanParsable<T>, IUtf8SpanParsable<T>
        {
            var invariant = CultureInfo.InvariantCulture;
            var parsed = Serialization.Converters.PlainNumberParser.TryParse<char, T>(value, invariant, out var result);
            Assert.Equal(parsed, Serialization.Converters.PlainNumberParser.TryParse<byte, T>(utf8, invariant, out var utf8Result));
            if (parsed)
            {
                var expected = T.Parse(value, invariant);
                AssertSame(expected, result);
                AssertSame(expected, utf8Result);
            }

            // The converters give the values and the errors of the BCL
            var converter = CsvMetadataServices.GetConverter<T>(CsvSerializerOptions.Default);
            var context = new CsvConverterContext(CsvSerializerOptions.Default);
            if (T.TryParse(value, invariant, out var bcl))
            {
                AssertSame(bcl, converter.Read(value, context));
                AssertSame(bcl, converter.Read(utf8, context));
            }
            else
            {
                Assert.ThrowsAny<Exception>(() => converter.Read(value, context));
                Assert.ThrowsAny<Exception>(() => converter.Read(utf8, context));
            }
        }

        static void AssertSame<T>(T expected, T actual)
        {
            // Compares the bits of the doubles, so that 0 and -0 are different
            if (expected is double expectedDouble && actual is double actualDouble)
            {
                Assert.Equal(BitConverter.DoubleToInt64Bits(expectedDouble), BitConverter.DoubleToInt64Bits(actualDouble));
            }
            else
            {
                Assert.Equal(expected, actual);
            }
        }
    }

    [Theory]
    [InlineData(0x9E3779B97F4A7C15UL)]
    public void PlainNumbers_MatchBcl_Random(ulong seed)
    {
        var values = CreateRandomNumbers(seed, 20_000);
        var parsedCount = 0;
        foreach (var value in values)
        {
            if (MatchesBcl(value))
            {
                parsedCount++;
            }
        }

        // About a quarter of the values are integers of 18 digits or less, which take the fast path of long: the test checks it, not only the
        // general parser. The other values are rejected and left to the general parser.
        Assert.True(parsedCount > values.Count / 5, parsedCount.ToString(CultureInfo.InvariantCulture));

        // Returns whether the fast parser of long accepts the value
        static bool MatchesBcl(string value)
        {
            var invariant = CultureInfo.InvariantCulture;
            var utf8 = Encoding.UTF8.GetBytes(value);
            if (Serialization.Converters.PlainNumberParser.TryParse<char, int>(value, invariant, out var intResult))
            {
                Assert.Equal(int.Parse(value, invariant), intResult);
                Assert.True(Serialization.Converters.PlainNumberParser.TryParse<byte, int>(utf8, invariant, out var utf8IntResult));
                Assert.Equal(intResult, utf8IntResult);
            }

            if (!Serialization.Converters.PlainNumberParser.TryParse<char, long>(value, invariant, out var longResult))
                return false;

            Assert.Equal(long.Parse(value, invariant), longResult);
            Assert.True(Serialization.Converters.PlainNumberParser.TryParse<byte, long>(utf8, invariant, out var utf8LongResult));
            Assert.Equal(longResult, utf8LongResult);
            return true;
        }
    }

    // Integers of 1 to 19 digits, decimals of up to 15 decimals, and doubles as the serializer writes them, with a deterministic xorshift generator
    // (no warnings about Random). The seed is a parameter of the test, not a constant: the value content analysis of the security analyzers
    // (CA3xxx, CA5xxx) computes the possible values of a variable, and from a constant seed, the values of each step of the generator in the loop
    // grew until the compilation used more than 25 GB.
    private static List<string> CreateRandomNumbers(ulong seed, int count)
    {
        var invariant = CultureInfo.InvariantCulture;
        var state = seed;
        var values = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var integer = Next(ref state) % (ulong)Math.Pow(10, Math.Min((int)(Next(ref state) % 19) + 1, 18));
            var text = (Next(ref state) % 2 == 0 ? "-" : "") + integer.ToString(invariant);
            values.Add(text);

            var scale = (int)(Next(ref state) % 16);
            if (scale > 0)
            {
                values.Add(text + "." + (Next(ref state) % (ulong)Math.Pow(10, scale)).ToString(invariant).PadLeft(scale, '0'));
            }

            values.Add(BitConverter.Int64BitsToDouble((long)(Next(ref state) >> 2) + 0x3000000000000000).ToString("R", invariant));
            values.Add(Math.Round(BitConverter.UInt64BitsToDouble(0x3FF0000000000000 | (Next(ref state) >> 12)) * 1000, (int)(Next(ref state) % 6), MidpointRounding.ToEven).ToString(invariant));
        }

        return values;

        static ulong Next(ref ulong state)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            return state;
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

    [Theory]
    [InlineData("0")]
    [InlineData("-0")]
    [InlineData("+0")]
    [InlineData("0.0")]
    [InlineData("-0.000")]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData("+1.5")]
    [InlineData("1.50")]
    [InlineData("-123.45")]
    [InlineData("0.1")]
    [InlineData("0.3")]
    [InlineData("3.14159")]
    [InlineData("1.")]
    [InlineData(".5")]
    [InlineData("-.5")]
    [InlineData("1e3")]
    [InlineData("1E3")]
    [InlineData("1e+3")]
    [InlineData("1.5e-3")]
    [InlineData("1.e3")]
    [InlineData(".5e1")]
    [InlineData("1e0000")]
    [InlineData("1e22")]
    [InlineData("1e23")]
    [InlineData("1e-22")]
    [InlineData("1e-23")]
    [InlineData("9007199254740992")]
    [InlineData("9007199254740993")]
    [InlineData("16777216")]
    [InlineData("16777217")]
    [InlineData("2048")]
    [InlineData("2049")]
    [InlineData("65504")]
    [InlineData("65520")]
    [InlineData("100000")]
    [InlineData("0.00001")]
    [InlineData("1234567890123456789")]
    [InlineData("12345678901234567890")]
    [InlineData("0.0000000000000000001")]
    [InlineData("12345.678901234567")]
    [InlineData("1.7976931348623157E+308")]
    [InlineData("4.9406564584124654E-324")]
    [InlineData("0.30000000000000004")]
    [InlineData("9007199254740995")]
    [InlineData("18014398509481986")]
    [InlineData("18014398509481990")]
    [InlineData("9007199254740993e5")]
    [InlineData("9223372036854775807")]
    [InlineData("18446744073709551615")]
    [InlineData("1.0000000000000002")]
    [InlineData("0.9999999999999999")]
    [InlineData("2.2250738585072014E-308")]
    [InlineData("1.2345678901234567E-48")]
    [InlineData("1.2345678901234567E-49")]
    [InlineData("1.2345678901234567E+80")]
    [InlineData("1.2345678901234567E+81")]
    [InlineData("123456789012345678e64")]
    [InlineData("0e100")]
    [InlineData("-0e-100")]
    [InlineData("1e400")]
    [InlineData("NaN")]
    [InlineData("-Infinity")]
    [InlineData("1,000")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1e")]
    [InlineData("1e+")]
    [InlineData("e3")]
    [InlineData(".")]
    [InlineData("-")]
    [InlineData("--1")]
    [InlineData("1.2.3")]
    [InlineData("0x10")]
    [InlineData("")]
    public void FloatingPoint_MatchesBcl(string value)
    {
        // The fast parser accepts a subset of what the BCL accepts, and gives the same values, with the same sign, for double, float and Half
        AssertMatchesBcl<double>(value);
        AssertMatchesBcl<float>(value);
        AssertMatchesBcl<Half>(value);

        if (double.TryParse(value, CultureInfo.InvariantCulture, out _))
        {
            var text = value + ";" + value + ";" + value;
            var options = new CsvSerializerOptions { HasHeaderRow = false, Separator = ';' };
            foreach (var model in new[] { CsvSerializer.Deserialize<FloatingPointModel>(text, options)[0], CsvSerializer.Deserialize<FloatingPointModel>(Encoding.UTF8.GetBytes(text), options)[0] })
            {
                Assert.Equal(Format(double.Parse(value, CultureInfo.InvariantCulture)), Format(model.Double));
                Assert.Equal(Format(float.Parse(value, CultureInfo.InvariantCulture)), Format(model.Single));
                Assert.Equal(Format(Half.Parse(value, CultureInfo.InvariantCulture)), Format(model.Half));
            }
        }
    }

    [Fact]
    public void FloatingPoint_Half_MatchesBclForEveryFastValue()
    {
        // Every mantissa and exponent the fast path of Half can accept
        var accepted = 0;
        for (var mantissa = 0; mantissa <= 2100; mantissa++)
        {
            for (var exponent = -5; exponent <= 5; exponent++)
            {
                var text = mantissa.ToString(CultureInfo.InvariantCulture) + "e" + exponent.ToString(CultureInfo.InvariantCulture);
                accepted += AssertMatchesBcl<Half>(text) ? 1 : 0;
                AssertMatchesBcl<Half>("-" + text);
            }
        }

        // The values that overflow, such as 100e4, are left to the BCL
        Assert.True(accepted > 12_000, "Too few values take the fast path: " + accepted.ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.2)]
    [InlineData(0.29)]
    [InlineData(0.3)]
    [InlineData(0.30000000000000004)]
    [InlineData(-12.3)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1e8)]
    [InlineData(1e-4)]
    [InlineData(9.9e-5)]
    [InlineData(0.0001234)]
    [InlineData(999999999.9)]
    [InlineData(1e9)]
    [InlineData(123456789.123)]
    [InlineData(4503599627370497.0)]
    [InlineData(5e-324)]
    [InlineData(double.MaxValue)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0.0)]
    public void FloatingPoint_FormatMatchesBcl(double value)
    {
        AssertFormatMatchesBcl(value);
        AssertFormatMatchesBcl(-value);
    }

    [Fact]
    public void FloatingPoint_FormatMatchesBclForRandomValues()
    {
        // Numbers with up to 12 decimals and up to 16 digits, and the doubles around them: the fast path writes those with few decimals, with the text
        // of the BCL, and leaves the others to it
        var state = 0x853C49E6748FEA9BUL;
        var handled = 0;
        for (var i = 0; i < 200_000; i++)
        {
            var decimals = (int)(Next(ref state) % 13);
            var digits = Next(ref state) % (Next(ref state) % 2 == 0 ? 100_000UL : 10_000_000_000_000_000UL);
            var value = (double)digits / Math.Pow(10, decimals) * (Next(ref state) % 2 == 0 ? 1 : -1);
            handled += AssertFormatMatchesBcl(value) ? 1 : 0;
            AssertFormatMatchesBcl(Math.BitIncrement(value));
            AssertFormatMatchesBcl(Math.BitDecrement(value));
            AssertFormatMatchesBcl(BitConverter.Int64BitsToDouble((long)Next(ref state)));
        }

        Assert.True(handled > 50_000, "Too few values take the fast path: " + handled.ToString(CultureInfo.InvariantCulture));

        static ulong Next(ref ulong state)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            return state;
        }
    }

    // The fast path writes the text of the BCL in UTF-16 and UTF-8, or reports a destination too small
    private static bool AssertFormatMatchesBcl(double value)
    {
        var expected = value.ToString(CultureInfo.InvariantCulture);
        var chars = new char[64];
        if (!InvariantFloatingPoint.TryFormat(value, chars.AsSpan(), out var written, out var tooSmall))
        {
            Assert.False(InvariantFloatingPoint.TryFormat(value, new byte[64].AsSpan(), out _, out _));
            return false;
        }

        Assert.False(tooSmall);
        Assert.Equal(expected, new string(chars, 0, written));

        var bytes = new byte[64];
        Assert.True(InvariantFloatingPoint.TryFormat(value, bytes.AsSpan(), out var bytesWritten, out tooSmall));
        Assert.False(tooSmall);
        Assert.Equal(expected, Encoding.UTF8.GetString(bytes, 0, bytesWritten));

        Assert.True(InvariantFloatingPoint.TryFormat(value, new char[expected.Length - 1].AsSpan(), out written, out tooSmall));
        Assert.True(tooSmall);
        Assert.Equal(0, written);
        return true;
    }

    [Fact]
    public void FloatingPoint_PowersOfFive()
    {
        // The table of the algorithm of Eisel and Lemire, as fast_float computes it: the 128 most significant bits of 5^q, truncated for a positive q,
        // and the reciprocal of 5^-q rounded up for a negative q
        var powers = InvariantFloatingPoint.PowersOfFive;
        Assert.HasCount(2 * (InvariantFloatingPoint.MaxPowerOfFive - InvariantFloatingPoint.MinPowerOfFive + 1), powers.ToArray());
        var two128 = BigInteger.One << 128;
        for (var q = InvariantFloatingPoint.MinPowerOfFive; q <= InvariantFloatingPoint.MaxPowerOfFive; q++)
        {
            BigInteger expected;
            if (q < 0)
            {
                var power = BigInteger.Pow(5, -q);
                var bits = (int)power.GetBitLength();
                var shift = q >= -27 ? bits + 127 : (2 * bits) + 128;
                expected = ((BigInteger.One << shift) / power) + 1;
                while (expected >= two128)
                {
                    expected >>= 1;
                }
            }
            else
            {
                expected = BigInteger.Pow(5, q);
                while (expected < (BigInteger.One << 127))
                {
                    expected <<= 1;
                }

                while (expected >= two128)
                {
                    expected >>= 1;
                }
            }

            var index = 2 * (q - InvariantFloatingPoint.MinPowerOfFive);
            Assert.Equal(expected, ((BigInteger)powers[index] << 64) | powers[index + 1], "5^" + q.ToString(CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void FloatingPoint_RoundTripValuesMatchBcl()
    {
        // The round-trip format of doubles has up to 17 digits, and the one of floats up to 9: those whose power of ten is in the table of Eisel-Lemire
        // take the fast path, and those written with more digits and their neighbors give the same values as the BCL
        var state = 0x2545F4914F6CDD1DUL;
        var accepted = 0;
        for (var i = 0; i < 50_000; i++)
        {
            var exponent = (int)(Next(ref state) % 130) - 50;
            var number = (double)(Next(ref state) >> 11) / (1UL << 53) * Math.Pow(10, exponent);
            var text = number.ToString("R", CultureInfo.InvariantCulture);
            accepted += AssertMatchesBcl<double>(text) ? 1 : 0;
            AssertMatchesBcl<double>("-" + text);
            AssertMatchesBcl<double>(BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(number) + 1).ToString("R", CultureInfo.InvariantCulture));
            AssertMatchesBcl<double>(number.ToString("E18", CultureInfo.InvariantCulture));
        }

        Assert.True(accepted > 45_000, "Too few values take the fast path: " + accepted.ToString(CultureInfo.InvariantCulture));

        var acceptedSingles = 0;
        for (var i = 0; i < 50_000; i++)
        {
            var exponent = (int)(Next(ref state) % 76) - 38;
            var number = (float)((double)(Next(ref state) >> 11) / (1UL << 53) * Math.Pow(10, exponent));
            var text = number.ToString("R", CultureInfo.InvariantCulture);
            acceptedSingles += AssertMatchesBcl<float>(text) ? 1 : 0;
            AssertMatchesBcl<float>("-" + text);
            AssertMatchesBcl<float>(BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(number) + 1).ToString("R", CultureInfo.InvariantCulture));
            AssertMatchesBcl<float>(number.ToString("E12", CultureInfo.InvariantCulture));
        }

        Assert.True(acceptedSingles > 45_000, "Too few floats take the fast path: " + acceptedSingles.ToString(CultureInfo.InvariantCulture));

        static ulong Next(ref ulong state)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            return state;
        }
    }

    [Fact]
    public void FloatingPoint_MatchesBclForRandomValues()
    {
        var state = 0x9E3779B97F4A7C15UL;
        var accepted = 0;
        for (var i = 0; i < 20_000; i++)
        {
            // Mantissas around the limits of the fast paths, written with a dot at any position or with an exponent
            var mantissa = (Next(ref state) % 4) switch
            {
                0 => Next(ref state) % (1UL << 54),
                1 => Next(ref state) % (1UL << 25),
                2 => Next(ref state) % 100_000,
                _ => Next(ref state) % 10_000_000_000_000_000_000UL,
            };

            var digits = mantissa.ToString(CultureInfo.InvariantCulture);
            var dot = (int)(Next(ref state) % (ulong)(digits.Length + 1));
            var withDot = digits[..dot] + "." + digits[dot..];
            var exponent = (int)(Next(ref state) % 61) - 30;
            foreach (var text in new[] { withDot, digits + "e" + exponent.ToString(CultureInfo.InvariantCulture), "-" + withDot + "E" + exponent.ToString(CultureInfo.InvariantCulture) })
            {
                accepted += AssertMatchesBcl<double>(text) ? 1 : 0;
                AssertMatchesBcl<float>(text);
            }

            // The texts the BCL writes: shortest round-trip, and rounded to a few decimals
            var number = BitConverter.Int64BitsToDouble((long)Next(ref state));
            AssertMatchesBcl<double>(number.ToString("R", CultureInfo.InvariantCulture));
            var small = (double)(Next(ref state) % 100_000_000) / 997;
            accepted += AssertMatchesBcl<double>(small.ToString("R", CultureInfo.InvariantCulture)) ? 1 : 0;
            accepted += AssertMatchesBcl<double>(small.ToString("F" + ((int)(Next(ref state) % 7)).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)) ? 1 : 0;
            AssertMatchesBcl<float>(((float)small).ToString("R", CultureInfo.InvariantCulture));
        }

        Assert.True(accepted > 20_000, "Too few values take the fast path: " + accepted.ToString(CultureInfo.InvariantCulture));

        // xorshift: deterministic values, without the warnings about Random
        static ulong Next(ref ulong state)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            return state;
        }
    }

    [Fact]
    public void FloatingPoint_ReadersUseFastPath()
    {
        // The fields parse with the fast path with the invariant culture, and with the BCL with another culture
        using (var reader = CsvReader.CreateFromString("-123.45;1e3;0.1", new CsvReaderOptions { HasHeaderRow = false, Separator = ';' }))
        {
            Assert.True(reader.Read());
            Assert.Equal(-123.45, reader.Current[0].Parse<double>());
            Assert.Equal(1000f, reader.Current[1].Parse<float>());
            Assert.True(reader.Current[2].TryParse<Half>(out var half));
            Assert.Equal((Half)0.1, half);
        }

        using (var reader = Utf8CsvReader.Create("-123.45;1e3;0.1"u8.ToArray(), new CsvReaderOptions { HasHeaderRow = false, Separator = ';' }))
        {
            Assert.True(reader.Read());
            Assert.Equal(-123.45, reader.Current[0].Parse<double>());
            Assert.Equal(1000f, reader.Current[1].Parse<float>());
            Assert.True(reader.Current[2].TryParse<Half>(out var half));
            Assert.Equal((Half)0.1, half);
        }

        var french = new NumberFormatInfo { NumberDecimalSeparator = "," };
        using (var reader = CsvReader.CreateFromString("-123,45", new CsvReaderOptions { HasHeaderRow = false, Separator = ';', FormatProvider = french }))
        {
            Assert.True(reader.Read());
            Assert.Equal(-123.45, reader.Current[0].Parse<double>());
        }

        Assert.Equal(-123.45, CsvSerializer.Deserialize<FloatingPointModel>("-123,45;0;0", new CsvSerializerOptions { HasHeaderRow = false, Separator = ';', FormatProvider = french })[0].Double);
    }

    private static bool AssertMatchesBcl<T>(string value)
        where T : IBinaryFloatingPointIeee754<T>, IUtf8SpanParsable<T>
    {
        var parsed = InvariantFloatingPoint.TryParse(value.AsSpan(), out T result);
        Assert.Equal(parsed, InvariantFloatingPoint.TryParse((ReadOnlySpan<byte>)Encoding.UTF8.GetBytes(value), out T utf8Result));
        if (parsed)
        {
            Assert.True(T.TryParse(value, CultureInfo.InvariantCulture, out var expected), "The BCL rejects a value the fast parser accepts: " + value);
            Assert.Equal(Format(expected), Format(result));
            Assert.Equal(Format(expected), Format(utf8Result));
        }

        return parsed;
    }

    // The round-trip format distinguishes every value, including the negative zero
    private static string Format<T>(T value)
        where T : IFormattable
        => value.ToString("R", CultureInfo.InvariantCulture);

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
        CsvBadDataHandler badDataHandler = _ => CsvBadDataAction.Skip;
        CsvDeserializationErrorHandler errorHandler = _ => CsvDeserializationErrorAction.Ignore;
        var options = new CsvSerializerOptions { Separator = ';', PropertyNameCaseInsensitive = true, Converters = [new PointConverter()], BadDataHandler = badDataHandler, DeserializationErrorHandler = errorHandler, MaxDegreeOfParallelism = 3 };
        var copy = new CsvSerializerOptions(options) { NewLine = "\n" };
        Assert.Same(badDataHandler, copy.BadDataHandler);
        Assert.Same(errorHandler, copy.DeserializationErrorHandler);
        Assert.Equal(';', copy.Separator);
        Assert.True(copy.PropertyNameCaseInsensitive);
        Assert.Single(copy.Converters);
        Assert.Equal("\n", copy.NewLine);
        Assert.Equal(3, copy.MaxDegreeOfParallelism);
    }

    [Fact]
    public void Parallel()
    {
        #region CsvSerializer_Parallel
        var csv = "X,Y\n" + string.Concat(Enumerable.Range(0, 10_000).Select(i => FormattableString.Invariant($"{i},{i * 0.5}\n")));

        // The data is split into records on the calling thread, and the records are converted to objects on the thread pool
        var options = new CsvSerializerOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        var values = CsvSerializer.Deserialize<ParallelModel>(csv, options);

        Assert.HasCount(10_000, values);
        Assert.Equal(4_999.5, values[^1].Y); // The objects are in the order of the records
        #endregion
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(255, 2)]
    [InlineData(256, 2)]
    [InlineData(257, 2)]
    [InlineData(5_000, 2)]
    [InlineData(5_000, 3)]
    [InlineData(5_000, -1)]
    public async Task Parallel_MatchesSequential(int count, int degreeOfParallelism)
    {
        // Quoted values, escaped quotes and line terminators in values: the positions, the line numbers and the unescaped values come from the copies of the records
        var csv = new StringBuilder("Id,Name,Price\n");
        var peopleCsv = new StringBuilder("Name,Age\n");
        for (var i = 0; i < count; i++)
        {
            var name = (i % 7) switch
            {
                0 => "\"Name, with a separator\"",
                1 => "\"Name with \"\"quotes\"\"\"",
                2 => "\"Name on\ntwo lines\"",
                3 => "Ünïcödé " + i.ToString(CultureInfo.InvariantCulture),
                4 => "",
                _ => "Name " + i.ToString(CultureInfo.InvariantCulture),
            };

            csv.Append(CultureInfo.InvariantCulture, $"{i},{name},{i * 1.25m}\n");
            peopleCsv.Append(CultureInfo.InvariantCulture, $"{name},{i}\n");
            if (i % 100 == 0)
            {
                csv.Append('\n'); // An empty line is skipped
            }
        }

        var text = csv.ToString();
        var bytes = Encoding.UTF8.GetBytes(text);
        var sequential = new CsvSerializerOptions();
        var parallel = new CsvSerializerOptions { MaxDegreeOfParallelism = degreeOfParallelism };
        var expected = CsvSerializer.Deserialize<Product>(text, sequential);
        Assert.HasCount(count, expected);

        Assert.Equal(expected, CsvSerializer.Deserialize<Product>(text, parallel));
        Assert.Equal(expected, CsvSerializer.Deserialize<Product>(bytes, parallel));
        Assert.Equal(expected, CsvSerializer.Deserialize<Product>(new StringReader(text), parallel));
        Assert.Equal(expected, CsvSerializer.Deserialize<Product>(new MemoryStream(bytes), parallel));
        Assert.Equal(expected, await CsvSerializer.DeserializeAsync<Product>(new MemoryStream(bytes), parallel));
        Assert.Equal(expected, await CsvSerializer.DeserializeAsync<Product>(new StringReader(text), parallel));
        Assert.Equal(expected, CsvSerializer.DeserializeEnumerable<Product>(new StringReader(text), parallel).ToList());
        Assert.Equal(expected, CsvSerializer.DeserializeEnumerable<Product>(new MemoryStream(bytes), parallel).ToList());
        Assert.Equal(expected, await ToListAsync(CsvSerializer.DeserializeAsyncEnumerable<Product>(new StringReader(text), parallel)));
        Assert.Equal(expected, await ToListAsync(CsvSerializer.DeserializeAsyncEnumerable<Product>(new MemoryStream(bytes), parallel)));

        // A type with setters, and a generated context
        var context = new TestCsvContext(new CsvSerializerOptions { MaxDegreeOfParallelism = degreeOfParallelism });
        var people = peopleCsv.ToString();
        Assert.Equal(
            CsvSerializer.Deserialize(people, TestCsvContext.Default.Person).Select(p => (p.Name, p.Age)),
            CsvSerializer.Deserialize(people, context.Person).Select(p => (p.Name, p.Age)));
    }

    [Fact]
    public async Task Parallel_ErrorsAreThrownInOrder()
    {
        // A conversion error in record 2000, then another one in record 2500, then malformed data in record 2800
        static string CreateCsv(bool conversionErrors)
        {
            var csv = new StringBuilder("Id,Name,Price\n");
            for (var i = 0; i < 3_000; i++)
            {
                var id = conversionErrors && i is 2_000 or 2_500 ? "x" : i.ToString(CultureInfo.InvariantCulture);
                var name = i is 2_800 ? "\"a\"b" : "a";
                csv.Append(CultureInfo.InvariantCulture, $"{id},{name},1\n");
            }

            return csv.ToString();
        }

        foreach (var (data, expectedCount) in new[] { (CreateCsv(conversionErrors: true), 2_000), (CreateCsv(conversionErrors: false), 2_800) })
        {
            var sequential = new CsvSerializerOptions { Strict = true };
            var parallel = new CsvSerializerOptions { Strict = true, MaxDegreeOfParallelism = 4 };
            var expected = Assert.Throws<CsvException>(() => CsvSerializer.Deserialize<Product>(data, sequential));
            Assert.Equal(expectedCount, expected.RowIndex);

            void Check(CsvException exception)
            {
                Assert.Equal(expected.Message, exception.Message);
                Assert.Equal(expected.RowIndex, exception.RowIndex);
                Assert.Equal(expected.LineNumber, exception.LineNumber);
                Assert.Equal(expected.Position, exception.Position);
            }

            Check(Assert.Throws<CsvException>(() => CsvSerializer.Deserialize<Product>(data, parallel)));
            Check(Assert.Throws<CsvException>(() => CsvSerializer.Deserialize<Product>(new MemoryStream(Encoding.UTF8.GetBytes(data)), parallel)));
            Check(await Assert.ThrowsAsync<CsvException>(() => CsvSerializer.DeserializeAsync<Product>(new StringReader(data), parallel)));

            // The enumerable overloads return the records before the error
            var read = 0;
            Check(Assert.Throws<CsvException>(() =>
            {
                foreach (var product in CsvSerializer.DeserializeEnumerable<Product>(new StringReader(data), parallel))
                {
                    Assert.Equal(read, product.Id);
                    read++;
                }
            }));
            Assert.Equal(expectedCount, read);

            read = 0;
            Check(await Assert.ThrowsAsync<CsvException>(async () =>
            {
                await foreach (var product in CsvSerializer.DeserializeAsyncEnumerable<Product>(new MemoryStream(Encoding.UTF8.GetBytes(data)), parallel))
                {
                    read++;
                }
            }));
            Assert.Equal(expectedCount, read);
        }
    }

    [Fact]
    public async Task Parallel_LongRecords()
    {
        // Records longer than the buffer of a batch, alone in their batch or after short records
        var csv = new StringBuilder("Id,Name,Price\n");
        for (var i = 0; i < 600; i++)
        {
            var name = i % 50 == 0 ? new string((char)('a' + (i % 26)), 70_000 + i) : "\"a\"\"b\"";
            csv.Append(CultureInfo.InvariantCulture, $"{i},{name},{i}\n");
        }

        var text = csv.ToString();
        var bytes = Encoding.UTF8.GetBytes(text);
        var options = new CsvSerializerOptions { MaxDegreeOfParallelism = 3 };
        var expected = CsvSerializer.Deserialize<Product>(text);
        Assert.Equal(expected, CsvSerializer.Deserialize<Product>(text, options));
        Assert.Equal(expected, CsvSerializer.Deserialize<Product>(new StringReader(text), options));
        Assert.Equal(expected, await CsvSerializer.DeserializeAsync<Product>(new MemoryStream(bytes), options));
    }

    [Fact]
    public void Parallel_StopEnumerating()
    {
        var csv = "Id,Name,Price\n" + string.Concat(Enumerable.Range(0, 10_000).Select(i => $"{i},a,1\n"));
        var options = new CsvSerializerOptions { MaxDegreeOfParallelism = 4 };
        Assert.Equal([0, 1, 2], CsvSerializer.DeserializeEnumerable<Product>(new StringReader(csv), options).Take(3).Select(p => p.Id));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Parallel_CancelEnumerating(int degreeOfParallelism)
    {
        // The records are read before the first one is returned: the cancellation is still checked for each record
        var csv = "Id,Name,Price\n1,a,1\n2,b,2\n3,c,3\n";
        var options = new CsvSerializerOptions { MaxDegreeOfParallelism = degreeOfParallelism };
        using var cts = new CancellationTokenSource();
        var ids = new List<int>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var product in CsvSerializer.DeserializeAsyncEnumerable<Product>(new StringReader(csv), options, cts.Token))
            {
                ids.Add(product.Id);
                await cts.CancelAsync();
            }
        });

        Assert.Equal([1], ids);
    }

    [Fact]
    public async Task Parallel_ConversionErrorBeforeCancellation()
    {
        // The setter of the second record cancels the token, then fails: the first record and the error come first, like reading the records one by one
        var csv = "Id\n1\n2\n3\n";
        foreach (var degreeOfParallelism in new[] { 1, 4 })
        {
            using var cts = new CancellationTokenSource();
            CancelingModel.Source = cts;
            var options = new CsvSerializerOptions { MaxDegreeOfParallelism = degreeOfParallelism };
            var ids = new List<int>();
            var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await foreach (var item in CsvSerializer.DeserializeAsyncEnumerable<CancelingModel>(new StringReader(csv), options, cts.Token))
                {
                    ids.Add(item.Id);
                }
            });

            Assert.Equal([1], ids);
            Assert.IsNotAssignableTo<OperationCanceledException>(exception);
        }
    }

    [Fact]
    public void Parallel_DeserializationErrorHandler()
    {
        var csv = "Id,Name,Price\n" + string.Concat(Enumerable.Range(0, 5_000).Select(i => i % 10 == 0 ? "x,a,1\n" : $"{i},a,1\n"));
        var callers = 0;
        var maxCallers = 0;
        var errors = new List<long?>();
        var options = new CsvSerializerOptions
        {
            MaxDegreeOfParallelism = 4,
            DeserializationErrorHandler = context =>
            {
                // The handler is not called by two threads at the same time
                var current = Interlocked.Increment(ref callers);
                maxCallers = Math.Max(maxCallers, current);
                errors.Add(context.RowIndex);
                Thread.SpinWait(1_000);
                Interlocked.Decrement(ref callers);
                return CsvDeserializationErrorAction.SkipRecord;
            },
        };

        var products = CsvSerializer.Deserialize<Product>(csv, options);
        Assert.Equal(Enumerable.Range(0, 5_000).Where(i => i % 10 != 0), products.Select(p => p.Id));
        Assert.Equal(1, maxCallers);
        Assert.Equal(Enumerable.Range(0, 500).Select(i => (long?)(i * 10)), errors.Order());
    }

    [Fact]
    public void Parallel_InvalidDegreeOfParallelism()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CsvSerializerOptions { MaxDegreeOfParallelism = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CsvSerializerOptions { MaxDegreeOfParallelism = -2 });
        Assert.Equal(1, CsvSerializerOptions.Default.MaxDegreeOfParallelism);
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

#region CsvSerializer_ParallelModel
public sealed class ParallelModel
{
    public int X { get; set; }

    public double Y { get; set; }
}
#endregion

public sealed class CancelingModel
{
    public static CancellationTokenSource? Source { get; set; }

    public int Id
    {
        get;
        set
        {
            if (value == 2)
            {
                Source?.Cancel();
                throw new InvalidOperationException("Invalid id");
            }

            field = value;
        }
    }
}

public sealed class DecimalModel
{
    public decimal Value { get; set; }
}

public sealed class FloatingPointModel
{
    public double Double { get; set; }

    public float Single { get; set; }

    public Half Half { get; set; }
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

public sealed class ValidatingSetterModel
{
    public int Value
    {
        get;
        set => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
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
