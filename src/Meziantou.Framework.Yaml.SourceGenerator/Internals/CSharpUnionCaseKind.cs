namespace Meziantou.Framework.Yaml.SourceGeneration;

[Flags]
internal enum CSharpUnionCaseKind
{
    None = 0,
    Boolean = 1,
    Number = 2,
    String = 4,
    Sequence = 8,
    Mapping = 16,
    Scalar = Boolean | Number | String,
    All = Scalar | Sequence | Mapping,
}
