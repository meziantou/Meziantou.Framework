---
title: Handling errors
description: Catch YAML errors, report the position of the problem, name the file in the messages, and read without exceptions.
---

## Catch YAML errors

Every error of the content, from invalid YAML to a value that does not match its type, throws a [YamlException](cref:Meziantou.Framework.Yaml.YamlException) or a
derived class. [Start](cref:Meziantou.Framework.Yaml.YamlException.Start) and [End](cref:Meziantou.Framework.Yaml.YamlException.End) give the position of the node at fault:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlException_Location;format=md-fenced-code-block;dedent=true -->
```csharp
static string? Validate(string yaml)
{
    try
    {
        YamlSerializer.Deserialize<Product>(yaml);
        return null;
    }
    catch (YamlException exception)
    {
        // Line and Column are zero-based
        return $"line {exception.Start.Line + 1}, column {exception.Start.Column + 1}";
    }
}

Assert.Null(Validate("Id: 1"));
Assert.Equal("line 2, column 7", Validate("Id: 1\nTags: 3"));
Assert.Equal("line 1, column 5", Validate("Id: [1"));
```
<!-- endref -->

- [Line](cref:Meziantou.Framework.Yaml.Mark.Line) and [Column](cref:Meziantou.Framework.Yaml.Mark.Column) start at `0`: add `1` to show them to a user. [Index](cref:Meziantou.Framework.Yaml.Mark.Index) is the position of the character in the
  text.
- [SyntaxErrorException](cref:Meziantou.Framework.Yaml.SyntaxErrorException) and [SemanticErrorException](cref:Meziantou.Framework.Yaml.SemanticErrorException) derive from [YamlException](cref:Meziantou.Framework.Yaml.YamlException) and report YAML that is not valid,
  such as an unclosed `[` or an unsupported `%YAML` version. Catch [YamlException](cref:Meziantou.Framework.Yaml.YamlException) to handle every error of the content.
- An exception thrown by a [callback](03-Serialization/06-ReadingBehavior.md#run-code-around-serialization) is wrapped
  in a [YamlException](cref:Meziantou.Framework.Yaml.YamlException).

Other exceptions report a problem in your code rather than in the YAML, and are not meant to be caught: a
`NotSupportedException` for a type that cannot be serialized, an `InvalidOperationException` for an invalid
configuration of your types, such as two members with the same key.

## Name the file in the messages

Set [SourceName](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.SourceName), typically to the path of the file, to start the messages with it:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlSerializerOptions_SourceName;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new YamlSerializerOptions { SourceName = "config.yaml" };

var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Product>("Id: [1, 2]", options));
Assert.Equal("config.yaml: (Lin: 0, Col: 4, Chr: 4) - (Lin: 0, Col: 4, Chr: 4): Expected a Scalar token but found 'StartSequence'.", exception.Message);
```
<!-- endref -->

The message gives the start and end positions as `(Lin, Col, Chr)`, from `0`. [YamlException.SourceName](cref:Meziantou.Framework.Yaml.YamlException.SourceName) returns the
name.

## Read without exceptions

[TryDeserialize](cref:Meziantou.Framework.Yaml.YamlSerializer.TryDeserialize) returns `false` instead of throwing a [YamlException](cref:Meziantou.Framework.Yaml.YamlException):

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlSerializer_TryDeserialize;format=md-fenced-code-block;dedent=true -->
```csharp
if (YamlSerializer.TryDeserialize<Product>("Id: 1", out var value))
{
    Assert.Equal(1, value!.Id);
}

Assert.False(YamlSerializer.TryDeserialize<Product>("Id: not a number", out _));
```
<!-- endref -->

It does not tell what is wrong: use it when invalid input is expected and its cause does not matter, and catch the
exception when you report the error.
