using System.ComponentModel;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.Json;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Meziantou.Framework.Tests;

public sealed class SensitiveDataTests
{
    [Fact]
    public void RevealToString()
    {
        var data = SensitiveData.Create("foo");
        using (data)
        {
            data.GetLength().Should().Be(3);
            data.RevealToString().Should().Be("foo");
        }

        FluentActions.Invoking(() => data.GetLength()).Should().Throw<ObjectDisposedException>();
        FluentActions.Invoking(() => data.RevealToArray()).Should().Throw<ObjectDisposedException>();
        FluentActions.Invoking(() => data.RevealToString()).Should().Throw<ObjectDisposedException>();
        FluentActions.Invoking(() => data.RevealInto(new char[1])).Should().Throw<ObjectDisposedException>();
        FluentActions.Invoking(() => data.RevealAndUse("", (span, arg) => { })).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void ToStringDoesNotRevealValue()
    {
        using var data = SensitiveData.Create("foo");
        var text = data.ToString();
        text.Should().NotContain("foo");
    }

    [Fact]
    public void SystemTestJsonDoesNotRevealValue()
    {
        using var data = SensitiveData.Create("foo");
        var text = JsonSerializer.Serialize(data);
        text.Should().NotContain("foo");
    }

    [Fact]
    public void SystemTestJsonDoesNotRevealValue_Field()
    {
        using var data = SensitiveData.Create("foo");
        var text = JsonSerializer.Serialize(data, new JsonSerializerOptions { IncludeFields = true });
        text.Should().NotContain("foo");
    }

    [Fact]
    public void NewtonsoftJsonDoesNotRevealValue()
    {
        using var data = Meziantou.Framework.SensitiveData.Create("foo");
        var text = Newtonsoft.Json.JsonConvert.SerializeObject(data);
        text.Should().NotContain("foo");
    }

#pragma warning disable SYSLIB0011 // Type or member is obsolete
    [Fact]
    public void BinaryFormatterDoesNotRevealValue()
    {
        using var data = Meziantou.Framework.SensitiveData.Create("foo");

        using var ms = new MemoryStream();
        var formatter = new BinaryFormatter();
        FluentActions.Invoking(() => formatter.Serialize(ms, data)).Should().Throw<SerializationException>();
    }
#pragma warning restore SYSLIB0011 // Type or member is obsolete

    [Fact]
    public void CanConvertFromString()
    {
        using var data = (SensitiveData<char>)TypeDescriptor.GetConverter(typeof(SensitiveData<char>)).ConvertFromString("bar");
        data.RevealToString().Should().Be("bar");
    }

    [Fact]
    public void TypeConverterToStringDoesNotRevealValue()
    {
        using var data = SensitiveData.Create("foo");
        FluentActions.Invoking(() => TypeDescriptor.GetConverter(typeof(SensitiveData<char>)).ConvertToString(data)).Should().Throw<InvalidOperationException>();
    }
}