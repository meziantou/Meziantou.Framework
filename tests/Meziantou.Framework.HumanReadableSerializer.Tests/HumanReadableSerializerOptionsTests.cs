using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Meziantou.Framework.HumanReadable.Converters;
using Meziantou.Framework.HumanReadable.ValueFormatters;

namespace Meziantou.Framework.HumanReadable.Tests;
public sealed class HumanReadableSerializerOptionsTests
{
    [Fact]
    public void ReadmeExample_Serialize()
    {
        #region HumanReadableSerializer_Serialize
        var obj = new { Name = "Gérald Barré", Nickname = "meziantou", Age = 32, MultiLineString = "line1\nline2" };
        var output = HumanReadableSerializer.Serialize(obj);
        #endregion

        Assert.Equal("Name: Gérald Barré\nNickname: meziantou\nAge: 32\nMultiLineString:\n  line1\n  line2", output);
    }

    [Fact]
    public void ReadmeExample_Options()
    {
        var obj = new { Name = "Gérald Barré", Nickname = (string?)null };

        #region HumanReadableSerializer_Options
        var options = new HumanReadableSerializerOptions()
        {
            IncludeFields = true,
            DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingNull,
        };
        HumanReadableSerializer.Serialize(obj, options);
        #endregion

        Assert.Equal("Name: Gérald Barré", HumanReadableSerializer.Serialize(obj, options));
    }

    [Fact]
    public void ReadmeExample_TypeYouDoNotOwn()
    {
        #region HumanReadableSerializer_TypeYouDoNotOwn
        var options = new HumanReadableSerializerOptions();

        // Add the HumanReadableIgnoreAttribute to the User.DisplayName property
        options.AddAttribute(typeof(User), "DisplayName", new HumanReadableIgnoreAttribute());

        options.IgnoreMember<User>(x => x.DisplayName);
        options.IgnoreMember<User>(x => new { x.DisplayName, x.CreatedAt });
        #endregion

        var user = new User { Name = "meziantou", DisplayName = "Gérald Barré", CreatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        Assert.Equal("Name: meziantou", HumanReadableSerializer.Serialize(user, options));
    }

    private sealed class User
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    [Fact]
    public void CloneShouldCopyAllSettableProperties()
    {
        var properties = GetSettableProperties();
        Assert.NotEmpty(properties);

        var defaults = new HumanReadableSerializerOptions();
        var options = new HumanReadableSerializerOptions();
        foreach (var property in properties)
        {
            property.SetValue(options, CreateNonDefaultValue(property, property.GetValue(defaults)));
        }

        var clone = options with { };

        foreach (var property in properties)
        {
            Assert.Equal(property.GetValue(options), property.GetValue(clone), message: property.Name);
        }
    }

    [Fact]
    public void CloneOfDefaultOptionsShouldKeepDefaultValues()
    {
        var defaults = new HumanReadableSerializerOptions();

        var clone = defaults with { };

        Assert.Equal(64, clone.MaxDepth);
        foreach (var property in GetSettableProperties())
        {
            Assert.Equal(property.GetValue(defaults), property.GetValue(clone), message: property.Name);
        }
    }

    [Fact]
    public void CloneOfReadOnlyOptionsShouldBeMutableAndKeepSettings()
    {
        var options = new HumanReadableSerializerOptions
        {
            PropertyOrder = StringComparer.Ordinal,
            DictionaryKeyOrder = StringComparer.OrdinalIgnoreCase,
            IncludeObsoleteMembers = true,
        };
        options.MakeReadOnly();

        var clone = options with { };
        clone.IncludeFields = true;

        Assert.False(clone.IsReadOnly);
        Assert.Same(StringComparer.Ordinal, clone.PropertyOrder);
        Assert.Same(StringComparer.OrdinalIgnoreCase, clone.DictionaryKeyOrder);
        Assert.True(clone.IncludeObsoleteMembers);
    }

    private static PropertyInfo[] GetSettableProperties()
    {
        return typeof(HumanReadableSerializerOptions)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.SetMethod is { IsPublic: true })
            .ToArray();
    }

    private static object CreateNonDefaultValue(PropertyInfo property, object? defaultValue)
    {
        var type = property.PropertyType;
        object value = type switch
        {
            _ when property.Name is nameof(HumanReadableSerializerOptions.NewLine) => "\r\n",
            _ when type == typeof(bool) => !(bool)defaultValue!,
            _ when type == typeof(int) => (int)defaultValue! + 1,
            _ when type.IsEnum => Enum.GetValues(type).Cast<object>().First(item => !item.Equals(defaultValue)),
            _ when type == typeof(IComparer<string>) => StringComparer.Ordinal,
            _ => throw new InvalidOperationException($"Cannot create a non-default value for property '{property.Name}' of type '{type}'. Update the test to support this type."),
        };

        Assert.NotEqual(defaultValue, value);
        return value;
    }

    [Fact]
    public void CloneShouldCreateNewConvertersInstance()
    {
        var options = new HumanReadableSerializerOptions();
        options.Converters.Add(new DummyConverter());
        HumanReadableSerializer.Serialize(12, options);

        var clone = options with { };

        Assert.Single(clone.Converters);
        clone.Converters.Clear();
        HumanReadableSerializer.Serialize(12, clone);

        Assert.Empty(clone.Converters);
        Assert.Single(options.Converters);
    }

    [Fact]
    public void CloneShouldCopyAllSettings()
    {
        var options = new HumanReadableSerializerOptions
        {
            DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingNull,
            DictionaryKeyOrder = StringComparer.Ordinal,
            IncludeFields = true,
            IncludeObsoleteMembers = true,
            MaxDepth = 12,
            NewLine = "\r\n",
            PropertyOrder = StringComparer.OrdinalIgnoreCase,
            ShowInvisibleCharactersInValues = true,
        };

        var clone = options with { };

        Assert.Equal(HumanReadableIgnoreCondition.WhenWritingNull, clone.DefaultIgnoreCondition);
        Assert.Equal(StringComparer.Ordinal, clone.DictionaryKeyOrder);
        Assert.Equal(true, clone.IncludeFields);
        Assert.Equal(true, clone.IncludeObsoleteMembers);
        Assert.Equal(12, clone.MaxDepth);
        Assert.Equal("\r\n", clone.NewLine);
        Assert.Equal(StringComparer.OrdinalIgnoreCase, clone.PropertyOrder);
        Assert.Equal(true, clone.ShowInvisibleCharactersInValues);
    }

    [Fact]
    public void ReadOnly_SettingsCannotBeChanged()
    {
        var options = new HumanReadableSerializerOptions();
        options.MakeReadOnly();

        Assert.Throws<InvalidOperationException>(() => options.MaxDepth = 1);
        Assert.Throws<InvalidOperationException>(() => options.ShowInvisibleCharactersInValues = true);
        Assert.Throws<InvalidOperationException>(() => options.IncludeFields = true);
    }

    [Fact]
    public void NewLine_DefaultsToLineFeed()
    {
        var text = HumanReadableSerializer.Serialize(new MultiLinePayload { Value = "line1\r\nline2" });

        Assert.Equal("\n", new HumanReadableSerializerOptions().NewLine);
        Assert.Equal("A: 1\nValue:\n  line1\n  line2", text);
    }

    [Fact]
    public void NewLine_CarriageReturnLineFeed()
    {
        var options = new HumanReadableSerializerOptions { NewLine = "\r\n" };

        var text = HumanReadableSerializer.Serialize(new MultiLinePayload { Value = "line1\nline2" }, options);

        Assert.Equal("A: 1\r\nValue:\r\n  line1\r\n  line2", text);
    }

    [Theory]
    [InlineData("\n", "a\u240D\u240A\nb\u240D\nc\u240A\nd")]
    [InlineData("\r\n", "a\u240D\u240A\r\nb\u240D\r\nc\u240A\r\nd")]
    public void NewLine_ShowInvisibleCharacters(string newLine, string expected)
    {
        var options = new HumanReadableSerializerOptions { NewLine = newLine, ShowInvisibleCharactersInValues = true };

        var text = HumanReadableSerializer.Serialize("a\r\nb\rc\nd", options);

        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r")]
    [InlineData(" ")]
    public void NewLine_RejectsUnsupportedValues(string newLine)
    {
        var options = new HumanReadableSerializerOptions();

        Assert.Throws<ArgumentException>(() => options.NewLine = newLine);
    }

    [Fact]
    public void NewLine_IsCopiedByClone()
    {
        var options = new HumanReadableSerializerOptions { NewLine = "\r\n" };

        var clone = options with { };

        Assert.Equal("\r\n", clone.NewLine);
    }

    [Fact]
    public void DefaultOptionsProduceTheSameOutputAsAnExplicitInstance()
    {
        var subject = new Payload();

        Assert.Equal(HumanReadableSerializer.Serialize(subject, new HumanReadableSerializerOptions()), HumanReadableSerializer.Serialize(subject));
    }

    [Fact]
    public void DefaultOptionsCanBeUsedConcurrently()
    {
        var subject = new Payload();
        var expected = HumanReadableSerializer.Serialize(subject);

        var results = new string[64];
        Parallel.For(0, results.Length, i => results[i] = HumanReadableSerializer.Serialize(subject));

        Assert.All(results, result => Assert.Equal(expected, result));
    }

    // Deliberately not an anonymous type: SerializerTests.Type_AnonymType asserts a
    // Roslyn-generated anonymous type ordinal, which shifts when new shapes are added.
    private sealed class Payload
    {
        public int Id { get; } = 1;
        public string Name { get; } = "test";
        public string[] Tags { get; } = ["a", "b"];
        public DateTime When { get; } = new(2123, 4, 5, 6, 7, 8, DateTimeKind.Utc);
    }

    private sealed class MultiLinePayload
    {
        public int A { get; } = 1;
        public string? Value { get; init; }
    }

    private sealed class DummyConverter : HumanReadableConverter
    {
        public override bool CanConvert(Type type) => false;
        public override void WriteValue(HumanReadableTextWriter writer, object? value, Type valueType, HumanReadableSerializerOptions options) { }
    }

    public static TheoryData<string> Mutators() => [.. MutatorActions.Keys];

    private static readonly Dictionary<string, Action<HumanReadableSerializerOptions>> MutatorActions = new(StringComparer.Ordinal)
    {
        [nameof(HumanReadableSerializerOptions.MaxDepth)] = options => options.MaxDepth = 1,
        [nameof(HumanReadableSerializerOptions.ShowInvisibleCharactersInValues)] = options => options.ShowInvisibleCharactersInValues = true,
        [nameof(HumanReadableSerializerOptions.NewLine)] = options => options.NewLine = "\r\n",
        [nameof(HumanReadableSerializerOptions.PropertyOrder)] = options => options.PropertyOrder = StringComparer.Ordinal,
        [nameof(HumanReadableSerializerOptions.DictionaryKeyOrder)] = options => options.DictionaryKeyOrder = StringComparer.Ordinal,
        [nameof(HumanReadableSerializerOptions.IncludeFields)] = options => options.IncludeFields = true,
        [nameof(HumanReadableSerializerOptions.IncludeObsoleteMembers)] = options => options.IncludeObsoleteMembers = true,
        [nameof(HumanReadableSerializerOptions.DefaultIgnoreCondition)] = options => options.DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingNull,
        ["Converters.Add"] = options => options.Converters.Add(new DummyConverter()),
        ["Converters.Insert"] = options => options.Converters.Insert(0, new DummyConverter()),
        ["Converters.Clear"] = options => options.Converters.Clear(),
        [nameof(HumanReadableSerializerOptions.AddFormatter)] = options => options.AddFormatter("text/plain", new Meziantou.Framework.HumanReadable.ValueFormatters.JsonFormatter()),
        ["AddAttribute(Type)"] = options => options.AddAttribute(typeof(Payload), new HumanReadableConverterAttribute(typeof(DummyConverter))),
        ["AddAttribute(Type, string)"] = options => options.AddAttribute(typeof(Payload), nameof(Payload.Id), new HumanReadableIgnoreAttribute()),
        ["AddAttribute(PropertyInfo)"] = options => options.AddAttribute(typeof(Payload).GetProperty(nameof(Payload.Id))!, new HumanReadableIgnoreAttribute()),
        ["AddAttribute(Expression)"] = options => options.AddAttribute<Payload>(payload => payload.Id, new HumanReadableIgnoreAttribute()),
        [nameof(HumanReadableSerializerOptions.AddPropertyAttribute)] = options => options.AddPropertyAttribute(property => true, new HumanReadableIgnoreAttribute()),
        [nameof(HumanReadableSerializerOptions.AddFieldAttribute)] = options => options.AddFieldAttribute(field => true, new HumanReadableIgnoreAttribute()),
        [nameof(HumanReadableSerializerOptions.AddTypeAttribute)] = options => options.AddTypeAttribute(type => true, new HumanReadableConverterAttribute(typeof(DummyConverter))),
    };

    [Theory]
    [MemberData(nameof(Mutators))]
    public void ReadOnly_MutatorsThrow(string mutator)
    {
        var options = new HumanReadableSerializerOptions();
        options.MakeReadOnly();

        Assert.Throws<InvalidOperationException>(() => MutatorActions[mutator](options));
    }

    [Theory]
    [MemberData(nameof(Mutators))]
    public void Serialize_MakesOptionsReadOnly(string mutator)
    {
        var options = new HumanReadableSerializerOptions();
        HumanReadableSerializer.Serialize(new Payload(), options);

        Assert.True(options.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => MutatorActions[mutator](options));
    }

    [Fact]
    public void CloneShouldCopyFormattersAndAttributes()
    {
        var options = new HumanReadableSerializerOptions()
            .AddJsonFormatter(new Meziantou.Framework.HumanReadable.ValueFormatters.JsonFormatterOptions { WriteIndented = true });
        options.IgnoreMember<Payload>(payload => payload.Tags);
        options.AddAttribute(typeof(Payload), nameof(Payload.Name), new HumanReadablePropertyNameAttribute("DisplayName"));
        options.AddTypeAttribute(type => type == typeof(DateTime), new HumanReadableConverterAttribute(new ConstantConverter()));
        HumanReadableSerializer.Serialize(new Payload(), options);

        var clone = options with { };
        using var content = new StringContent("""{"a":1}""", encoding: null, "application/json");

        Assert.Equal("Id: 1\nDisplayName: test\nWhen: constant", HumanReadableSerializer.Serialize(new Payload(), clone));
        Assert.Equal("Headers:\n  Content-Type: application/json; charset=utf-8\nValue:\n  {\n    \"a\": 1\n  }", HumanReadableSerializer.Serialize(content, clone));
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("APPLICATION/JSON")]
    [InlineData("text/json")]
    [InlineData("application/problem+json")]
    public void GetFormatter_Json(string mediaType)
    {
        var options = new HumanReadableSerializerOptions().AddJsonFormatter();

        Assert.IsType<Meziantou.Framework.HumanReadable.ValueFormatters.JsonFormatter>(options.GetFormatter(mediaType));
    }

    [Theory]
    [InlineData("application/xml")]
    [InlineData("TEXT/XML")]
    [InlineData("application/atom+xml")]
    public void GetFormatter_Xml(string mediaType)
    {
        var options = new HumanReadableSerializerOptions().AddXmlFormatter();

        Assert.IsType<Meziantou.Framework.HumanReadable.ValueFormatters.XmlFormatter>(options.GetFormatter(mediaType));
    }

    [Fact]
    public void GetFormatter_Unknown()
    {
        var options = new HumanReadableSerializerOptions().AddJsonFormatter().AddXmlFormatter();

        Assert.Null(options.GetFormatter("text/plain"));
    }

    private sealed class ConstantConverter : HumanReadableConverter
    {
        public override bool CanConvert(Type type) => true;
        public override void WriteValue(HumanReadableTextWriter writer, object? value, Type valueType, HumanReadableSerializerOptions options) => writer.WriteValue("constant");
    }

    [Fact]
    public void Documentation_GettingStarted()
    {
        #region HumanReadableSerializer_GettingStarted
        var order = new
        {
            Id = 42,
            Customer = new { Name = "Ada Lovelace", Email = "ada@example.com" },
            Lines = new[]
            {
                new { Product = "Keyboard", Quantity = 1 },
                new { Product = "Mouse", Quantity = 2 },
            },
            Notes = "Leave at the door\nRing twice",
        };

        var text = HumanReadableSerializer.Serialize(order);

        Assert.Equal("""
            Id: 42
            Customer:
              Name: Ada Lovelace
              Email: ada@example.com
            Lines:
              - Product: Keyboard
                Quantity: 1
              - Product: Mouse
                Quantity: 2
            Notes:
              Leave at the door
              Ring twice
            """, text);
        #endregion
    }

    [Fact]
    public void Documentation_Values()
    {
        #region HumanReadableSerializer_Values
        var value = new
        {
            Missing = (string?)null,
            Empty = Array.Empty<int>(),
            Matrix = new[] { new[] { 1, 2 }, new[] { 3, 4 } },
            Scores = new Dictionary<string, int>(StringComparer.Ordinal) { ["Ada"] = 12, ["Alan"] = 9 },
            Day = DayOfWeek.Friday,
            Date = new DateTimeOffset(2024, 6, 15, 12, 30, 0, TimeSpan.FromHours(2)),
            Duration = TimeSpan.FromMinutes(90),
            Price = 12.5m,
            Id = new Guid("1c7a7a4e-0f43-4a3f-9d5b-6a7cb4b1f2d3"),
            Website = new Uri("https://www.meziantou.net/"),
        };

        Assert.Equal("""
            Missing: <null>
            Empty: []
            Matrix:
              - - 1
                - 2
              - - 3
                - 4
            Scores:
              Ada: 12
              Alan: 9
            Day: Friday
            Date: 2024-06-15T12:30:00+02:00
            Duration: 01:30:00
            Price: 12.5
            Id: 1c7a7a4e-0f43-4a3f-9d5b-6a7cb4b1f2d3
            Website: https://www.meziantou.net/
            """, HumanReadableSerializer.Serialize(value));
        #endregion
    }

    [Fact]
    public void Documentation_Attributes()
    {
        #region HumanReadableSerializer_AttributesUsage
        var invoice = new Invoice
        {
            Number = "INV-001",
            Total = 99.9m,
            InternalNote = "VIP customer",
            Discount = null,
            Revision = 3,
        };

        Assert.Equal("""
            Number: INV-001
            Total (EUR): 99.9
            Revision: 3
            """, HumanReadableSerializer.Serialize(invoice));
        #endregion
    }

    #region HumanReadableSerializer_AttributesType
    private sealed class Invoice
    {
        [HumanReadablePropertyName("Total (EUR)")]
        public decimal Total { get; set; }

        [HumanReadablePropertyOrder(0)]
        public string Number { get; set; } = "";

        [HumanReadableIgnore]
        public string? InternalNote { get; set; }

        [HumanReadableIgnore(Condition = HumanReadableIgnoreCondition.WhenWritingNull)]
        public string? Discount { get; set; }

        // Non-public members are serialized when they have [HumanReadableInclude]
        [HumanReadableInclude]
        internal int Revision { get; set; }
    }
    #endregion

    [Fact]
    public void Documentation_DefaultIgnoreCondition()
    {
        #region HumanReadableSerializer_DefaultIgnoreCondition
        var user = new { Name = "Ada", Age = 0, Email = (string?)null, Roles = Array.Empty<string>() };

        var options = new HumanReadableSerializerOptions
        {
            DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingDefaultOrEmptyCollection,
        };

        Assert.Equal("Name: Ada", HumanReadableSerializer.Serialize(user, options));
        #endregion
    }

    [Fact]
    public void Documentation_TypeYouDoNotOwn()
    {
        #region HumanReadableSerializer_IgnoreMembersOfTypeYouDoNotOwn
        var address = new MailAddress("ada@example.com", "Ada Lovelace");

        var options = new HumanReadableSerializerOptions();
        options.IgnoreMember<MailAddress>(x => new { x.User, x.Host });
        options.AddAttribute(typeof(MailAddress), nameof(MailAddress.DisplayName), new HumanReadablePropertyNameAttribute("Name"));

        Assert.Equal("""
            Name: Ada Lovelace
            Address: ada@example.com
            """, HumanReadableSerializer.Serialize(address, options));
        #endregion
    }

    [Fact]
    public void Documentation_Order()
    {
        #region HumanReadableSerializer_Order
        var value = new
        {
            Name = "Ada",
            Links = new Dictionary<string, string>(StringComparer.Ordinal) { ["web"] = "example.com", ["code"] = "github.com" },
            Age = 36,
        };

        var options = new HumanReadableSerializerOptions
        {
            PropertyOrder = StringComparer.Ordinal,
            DictionaryKeyOrder = StringComparer.Ordinal,
        };

        Assert.Equal("""
            Age: 36
            Links:
              code: github.com
              web: example.com
            Name: Ada
            """, HumanReadableSerializer.Serialize(value, options));
        #endregion
    }

    [Fact]
    public void Documentation_Converter()
    {
        #region HumanReadableSerializer_ConverterUsage
        var options = new HumanReadableSerializerOptions();
        options.Converters.Add(new TemperatureConverter());

        var forecast = new { City = "Paris", Min = new Temperature(12.3), Max = new Temperature(21) };

        Assert.Equal("""
            City: Paris
            Min: 12.3 °C
            Max: 21.0 °C
            """, HumanReadableSerializer.Serialize(forecast, options));
        #endregion
    }

    #region HumanReadableSerializer_Converter
    private sealed record Temperature(double Celsius);

    private sealed class TemperatureConverter : HumanReadableConverter<Temperature>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, Temperature? value, HumanReadableSerializerOptions options)
        {
            // The serializer writes null values itself, so value is never null here
            ArgumentNullException.ThrowIfNull(value);
            writer.WriteValue(value.Celsius.ToString("0.0", CultureInfo.InvariantCulture) + " °C");
        }
    }
    #endregion

    [Fact]
    public void Documentation_ObjectConverter()
    {
        #region HumanReadableSerializer_ObjectConverterUsage
        var options = new HumanReadableSerializerOptions();
        options.Converters.Add(new MoneyConverter());

        Assert.Equal("""
            Total:
              Amount: 12.5
              Currency: EUR
            """, HumanReadableSerializer.Serialize(new { Total = new Money(12.5m, "EUR") }, options));
        #endregion
    }

    #region HumanReadableSerializer_ObjectConverter
    private sealed record Money(decimal Amount, string Currency);

    private sealed class MoneyConverter : HumanReadableConverter<Money>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, Money? value, HumanReadableSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(value);
            writer.StartObject();
            writer.WritePropertyName("Amount");
            HumanReadableSerializer.Serialize(writer, value.Amount, options);
            writer.WritePropertyName("Currency");
            writer.WriteValue(value.Currency);
            writer.EndObject();
        }
    }
    #endregion

    [Fact]
    public void Documentation_JsonAndXml()
    {
        #region HumanReadableSerializer_JsonAndXml
        var options = new HumanReadableSerializerOptions()
            .AddJsonFormatter(new JsonFormatterOptions { OrderProperties = true })
            .AddXmlFormatter(new XmlFormatterOptions { OrderAttributes = true });

        var value = new
        {
            Json = JsonNode.Parse("""{"name":"Ada","id":42}"""),
            Xml = XElement.Parse("""<user name="Ada" id="42"><role>admin</role></user>"""),
        };

        Assert.Equal("""
            Json:
              {
                "id": 42,
                "name": "Ada"
              }
            Xml:
              <user id="42" name="Ada">
                <role>admin</role>
              </user>
            """, HumanReadableSerializer.Serialize(value, options));
        #endregion
    }

    [Fact]
    public void Documentation_JsonAsObject()
    {
        #region HumanReadableSerializer_JsonAsObject
        var options = new HumanReadableSerializerOptions()
            .AddJsonFormatter(new JsonFormatterOptions { FormatAsStandardObject = true });

        var value = new { Json = JsonNode.Parse("""{"name":"Ada","roles":["admin","dev"]}""") };

        Assert.Equal("""
            Json:
              name: Ada
              roles:
                - admin
                - dev
            """, HumanReadableSerializer.Serialize(value, options));
        #endregion
    }

    [Fact]
    public void Documentation_HttpResponse()
    {
        using var httpResponseMessage = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"name":"Ada","id":42}""", Encoding.UTF8, "application/json"),
        };
        httpResponseMessage.Headers.Location = new Uri("https://example.com/users/42");
        httpResponseMessage.Headers.Date = DateTimeOffset.UtcNow;
        httpResponseMessage.RequestMessage = new HttpRequestMessage(HttpMethod.Post, "https://example.com/users");

        #region HumanReadableSerializer_HttpResponse
        var options = new HumanReadableSerializerOptions()
            .AddHttpConverters(new HumanReadableHttpOptions
            {
                ResponseMessageOptions = { RequestMessageFormat = HttpRequestMessageFormat.MethodAndUri },
            })
            .AddJsonFormatter(new JsonFormatterOptions { OrderProperties = true });

        var text = HumanReadableSerializer.Serialize(httpResponseMessage, options);

        Assert.Equal("""
            StatusCode: 201 (Created)
            Headers:
              Location: https://example.com/users/42
            Content:
              Headers:
                Content-Type: application/json; charset=utf-8
              Value:
                {
                  "id": 42,
                  "name": "Ada"
                }
            RequestMessage: POST https://example.com/users
            """, text);
        #endregion
    }

    [Fact]
    public void Documentation_HttpHeaders()
    {
        using var httpResponseMessage = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Hello"),
        };
        httpResponseMessage.Headers.Add("X-Request-Id", "5f1d0c2a");
        httpResponseMessage.Headers.Add("Set-Cookie", "session=abc123; Path=/; HttpOnly");

        #region HumanReadableSerializer_HttpHeaders
        var httpOptions = new HumanReadableHttpOptions();
        httpOptions.ResponseMessageOptions.ExcludedHeaderNames.Add("X-Request-Id");
        httpOptions.ResponseMessageOptions.HeaderValueTransformer.Add(new RedactCookieFormatter());

        var options = new HumanReadableSerializerOptions().AddHttpConverters(httpOptions);

        Assert.Equal("""
            StatusCode: 200 (OK)
            Headers:
              Set-Cookie: [redacted]
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
              Value: Hello
            """, HumanReadableSerializer.Serialize(httpResponseMessage, options));
        #endregion
    }

    #region HumanReadableSerializer_HttpHeaderValueFormatter
    private sealed class RedactCookieFormatter : HttpHeaderValueFormatter
    {
        public override string FormatHeaderValue(string headerName, string headerValue)
        {
            return string.Equals(headerName, "Set-Cookie", StringComparison.OrdinalIgnoreCase) ? "[redacted]" : headerValue;
        }
    }
    #endregion

    [Fact]
    public void Documentation_TextOptions()
    {
        #region HumanReadableSerializer_TextOptions
        var value = new { Name = "Ada ", Notes = "line1\tend\nline2" };

        var options = new HumanReadableSerializerOptions
        {
            ShowInvisibleCharactersInValues = true,
        };

        Assert.Equal("""
            Name: Ada␠
            Notes:
              line1␉end␊
              line2
            """, HumanReadableSerializer.Serialize(value, options));

        // Windows line endings
        var windowsOptions = new HumanReadableSerializerOptions { NewLine = "\r\n" };
        Assert.Equal("Name: Ada \r\nNotes:\r\n  line1\tend\r\n  line2", HumanReadableSerializer.Serialize(value, windowsOptions));
        #endregion
    }

    [Fact]
    public void Documentation_ReadOnlyOptions()
    {
        #region HumanReadableSerializer_ReadOnlyOptions
        var options = new HumanReadableSerializerOptions { PropertyOrder = StringComparer.Ordinal };
        _ = HumanReadableSerializer.Serialize(new { B = 1, A = 2 }, options);

        // The options cannot change once they have been used...
        Assert.True(options.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => options.IncludeFields = true);

        // ...but a copy can
        var copy = options with { IncludeFields = true };
        Assert.False(copy.IsReadOnly);
        #endregion
    }
}
