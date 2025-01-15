using System.ComponentModel;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.Json;
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
            Assert.Equal(3, data.GetLength());
            Assert.Equal("foo", data.RevealToString());
        }

        Assert.Throws<ObjectDisposedException>(() => data.GetLength());
        Assert.Throws<ObjectDisposedException>(() => data.RevealToArray());
        Assert.Throws<ObjectDisposedException>(() => data.RevealToString());
        Assert.Throws<ObjectDisposedException>(() => data.RevealInto(new char[1]));
        Assert.Throws<ObjectDisposedException>(() => data.RevealAndUse("", (span, arg) => { }));
    }

    [Fact]
    [SuppressMessage("Design", "MA0150:Do not call the default object.ToString explicitly")]
    public void ToStringDoesNotRevealValue()
    {
        using var data = SensitiveData.Create("foo");
        var text = data.ToString();
        Assert.DoesNotContain("foo", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SystemTestJsonDoesNotRevealValue()
    {
        using var data = SensitiveData.Create("foo");
        var text = JsonSerializer.Serialize(data);
        Assert.DoesNotContain("foo", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [SuppressMessage("Performance", "CA1869:Cache and reuse 'JsonSerializerOptions' instances", Justification = "Only used once")]
    public void SystemTestJsonDoesNotRevealValue_Field()
    {
        using var data = SensitiveData.Create("foo");
        var text = JsonSerializer.Serialize(data, new JsonSerializerOptions { IncludeFields = true });
        Assert.DoesNotContain("foo", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NewtonsoftJsonDoesNotRevealValue()
    {
        using var data = Meziantou.Framework.SensitiveData.Create("foo");
        var text = Newtonsoft.Json.JsonConvert.SerializeObject(data);
        Assert.DoesNotContain("foo", text, StringComparison.OrdinalIgnoreCase);
    }

#pragma warning disable SYSLIB0011 // Type or member is obsolete
    [Fact]
    public void BinaryFormatterDoesNotRevealValue()
    {
        using var data = Meziantou.Framework.SensitiveData.Create("foo");

        using var ms = new MemoryStream();
        var formatter = new BinaryFormatter();
        Assert.Throws<SerializationException>(() => formatter.Serialize(ms, data));
    }
#pragma warning restore SYSLIB0011 // Type or member is obsolete

    [Fact]
    public void CanConvertFromString()
    {
        using var data = (SensitiveData<char>)TypeDescriptor.GetConverter(typeof(SensitiveData<char>)).ConvertFromString("bar");
        Assert.Equal("bar", data.RevealToString());
    }

    [Fact]
    public void TypeConverterToStringDoesNotRevealValue()
    {
        using var data = SensitiveData.Create("foo");
        Assert.Throws<InvalidOperationException>(() => TypeDescriptor.GetConverter(typeof(SensitiveData<char>)).ConvertToString(data));
    }
}