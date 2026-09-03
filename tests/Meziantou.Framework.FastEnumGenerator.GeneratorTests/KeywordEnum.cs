namespace Meziantou.Framework.FastEnumGenerator.GeneratorTests;

/// <summary>Members named after C# keywords must stay escaped in the generated code.</summary>
public enum KeywordEnum
{
    @class = 0,
    @event = 1,
    @new = 2,
}
