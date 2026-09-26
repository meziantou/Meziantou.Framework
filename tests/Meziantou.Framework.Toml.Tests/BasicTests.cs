using System;
using System.Collections.Generic;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Parsing;

namespace Meziantou.Framework.Toml.Tests;

public class BasicTests
{
    [Fact]
    public void TestTableArraysContainingPrimitiveArraysSerialize()
    {
        var test = @"[[table_array]]
primitive_list = [4, 5, 6]
";

        var model = TomlSerializer.Deserialize<TomlTable>(test);
        Assert.NotNull(model);
        var tomlOut = TomlSerializer.Serialize(model!);

        Assert.Equal(test.ReplaceLineEndings("\n"), tomlOut.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void TestHelloWorld()
    {
        var toml = @"global = ""this is a string""
# This is a comment of a table
[my_table]
key = 1 # Comment a key
value = true
 list = [4, 5, 6]
";

        var model = TomlSerializer.Deserialize<TomlTable>(toml);
        Assert.NotNull(model);
        var nonNullModel = model!;
        // Prints "this is a string"
        var global = nonNullModel["global"];
        TestContext.Current.TestOutputHelper?.WriteLine($"found global = \"{global}\"");
        Assert.Equal("this is a string", global);
        // Prints 1
        var key = ((TomlTable)nonNullModel["my_table"]!)["key"];
        TestContext.Current.TestOutputHelper?.WriteLine($"found key = {key}");
        Assert.Equal(1L, key);
        // Check list
        var list = (TomlArray)((TomlTable)nonNullModel["my_table"]!)["list"]!;
        TestContext.Current.TestOutputHelper?.WriteLine($"found list = {string.Join(", ", list)}");
        Assert.Equal(new TomlArray() { 4, 5, 6 }, list);
    }

    [Theory]
    [InlineData(7, 32, 0, 0)]
    [InlineData(7, 32, 0, 999)]
    [InlineData(0, 32, 0, 0)]
    public void TestLocalTime(int hour, int minute, int second, int millisecond)
    {
        var toml = $@"time = {hour:D2}:{minute:D2}:{second:D2}.{millisecond:D3}";
        var model = TomlSerializer.Deserialize<TomlTable>(toml);
        Assert.NotNull(model);
        var localTime = (TomlDateTime)model!["time"];

        Assert.Equal(hour, localTime.DateTime.Hour);
        Assert.Equal(minute, localTime.DateTime.Minute);
        Assert.Equal(second, localTime.DateTime.Second);
        Assert.Equal(millisecond, localTime.DateTime.Millisecond);
    }

    [Fact]
    public void TestEmptyComment()
    {
        var input = "#\n";
        var doc = SyntaxParser.Parse(input);
        var docAsStr = doc.ToString();
        Assert.Equal(input, docAsStr);
    }

    [Fact]
    public void TestIntegerOverflowIsRejected()
    {
        // TOML requires an error when an integer cannot be represented as a
        // signed 64-bit value; a positive literal in [2^63, 2^64-1] must not
        // silently wrap to a negative long.
        Assert.False(SyntaxParser.Parse("a = 9223372036854775807").HasErrors);  // long.MaxValue
        Assert.False(SyntaxParser.Parse("a = -9223372036854775808").HasErrors); // long.MinValue
        Assert.True(SyntaxParser.Parse("a = 9223372036854775808").HasErrors);   // 2^63
        Assert.True(SyntaxParser.Parse("a = 18446744073709551615").HasErrors);  // 2^64-1
    }

    [Fact]
    public void SimpleTest()
    {
        var test = @"[table-1]
key1 = ""some string""    # This is a comment
key2 = 123
Key3 = true
Key4 = false
Key5 = +inf

[table-2]
key1 = ""another string""
key2 = 456
";
        var doc = SyntaxParser.Parse(test);
        Assert.Equal(test, doc.ToString());
    }

    [Fact]
    public void TestInlineArray()
    {
        var input = @"x = [1,
2,
3
]
";
        var model = TomlSerializer.Deserialize<TomlTable>(input);
        Assert.NotNull(model);
        var array = model!["x"] as TomlArray;
        Assert.NotNull(array);
        var nonNullArray = array!;
        Assert.HasCount(3, nonNullArray);
        Assert.Equal(1L, nonNullArray[0]);
        Assert.Equal(2L, nonNullArray[1]);
        Assert.Equal(3L, nonNullArray[2]);
    }
}
