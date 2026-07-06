using Meziantou.Framework.Yaml.Tokens;

namespace Meziantou.Framework.Yaml.Tests;

public class ScannerTestHelper : YamlTest
{
    protected static StreamStart StreamStart { get { return new StreamStart(); } }

    protected static StreamEnd StreamEnd { get { return new StreamEnd(); } }

    protected static DocumentStart DocumentStart { get { return new DocumentStart(); } }

    protected static DocumentEnd DocumentEnd { get { return new DocumentEnd(); } }

    protected static VersionDirective VersionDirective(int major, int minor)
    {
        return new VersionDirective(new Version(major, minor));
    }

    protected static TagDirective TagDirective(string handle, string prefix)
    {
        return new TagDirective(handle, prefix);
    }

    protected static Tag Tag(string handle, string suffix)
    {
        return new Tag(handle, suffix);
    }

    protected static Scalar PlainScalar(string text)
    {
        return new Scalar(text, ScalarStyle.Plain);
    }

    protected static Scalar SingleQuotedScalar(string text)
    {
        return new Scalar(text, ScalarStyle.SingleQuoted);
    }

    protected static Scalar DoubleQuotedScalar(string text)
    {
        return new Scalar(text, ScalarStyle.DoubleQuoted);
    }

    protected static Scalar LiteralScalar(string text)
    {
        return new Scalar(text, ScalarStyle.Literal);
    }

    protected static Scalar FoldedScalar(string text)
    {
        return new Scalar(text, ScalarStyle.Folded);
    }

    protected static FlowSequenceStart FlowSequenceStart { get { return new FlowSequenceStart(); } }

    protected static FlowSequenceEnd FlowSequenceEnd { get { return new FlowSequenceEnd(); } }

    protected static BlockSequenceStart BlockSequenceStart { get { return new BlockSequenceStart(); } }

    protected static FlowMappingStart FlowMappingStart { get { return new FlowMappingStart(); } }

    protected static FlowMappingEnd FlowMappingEnd { get { return new FlowMappingEnd(); } }

    protected static BlockMappingStart BlockMappingStart { get { return new BlockMappingStart(); } }

    protected static Key Key { get { return new Key(); } }

    protected static Value Value { get { return new Value(); } }

    protected static FlowEntry FlowEntry { get { return new FlowEntry(); } }

    protected static BlockEntry BlockEntry { get { return new BlockEntry(); } }

    protected static BlockEnd BlockEnd { get { return new BlockEnd(); } }

    protected static Anchor Anchor(string anchor)
    {
        return new Anchor(anchor);
    }

    protected static AnchorAlias AnchorAlias(string alias)
    {
        return new AnchorAlias(alias);
    }
}


