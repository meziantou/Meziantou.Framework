using Meziantou.Framework.Yaml.Events;
using Meziantou.Framework.Yaml.Tokens;
using AnchorAlias = Meziantou.Framework.Yaml.Events.AnchorAlias;
using DocumentEnd = Meziantou.Framework.Yaml.Events.DocumentEnd;
using DocumentStart = Meziantou.Framework.Yaml.Events.DocumentStart;
using Scalar = Meziantou.Framework.Yaml.Events.Scalar;
using StreamEnd = Meziantou.Framework.Yaml.Events.StreamEnd;
using StreamStart = Meziantou.Framework.Yaml.Events.StreamStart;

namespace Meziantou.Framework.Yaml.Tests;

public class ParserTestHelper : YamlTest
{
    protected const bool Explicit = false;
    protected const bool Implicit = true;
    protected const string TagYaml = "tag:yaml.org,2002:";

    protected static readonly TagDirective[] DefaultTags = new[]
    {
        new TagDirective("!", "!"),
        new TagDirective("!!", TagYaml),
    };

    protected static StreamStart StreamStart { get { return new StreamStart(); } }

    protected static StreamEnd StreamEnd { get { return new StreamEnd(); } }

    protected static DocumentStart DocumentStart(bool isImplicit)
    {
        return DocumentStart(isImplicit, null, DefaultTags);
    }

    protected static DocumentStart DocumentStart(bool isImplicit, VersionDirective? version, params TagDirective[] tags)
    {
        return new DocumentStart(version, new TagDirectiveCollection(tags), isImplicit);
    }

    protected static VersionDirective Version(int major, int minor)
    {
        return new VersionDirective(new Version(major, minor));
    }

    protected static TagDirective TagDirective(string handle, string prefix)
    {
        return new TagDirective(handle, prefix);
    }

    protected static DocumentEnd DocumentEnd(bool isImplicit)
    {
        return new DocumentEnd(isImplicit);
    }

    protected static Scalar PlainScalar(string text)
    {
        return new Scalar(null, null, text, ScalarStyle.Plain, true, false);
    }

    protected static Scalar SingleQuotedScalar(string text)
    {
        return new Scalar(null, null, text, ScalarStyle.SingleQuoted, false, true);
    }

    protected static Scalar DoubleQuotedScalar(string text)
    {
        return DoubleQuotedScalar(null, text);
    }

    protected static Scalar ExplicitDoubleQuotedScalar(string tag, string text)
    {
        return DoubleQuotedScalar(tag, text, false);
    }

    protected static Scalar DoubleQuotedScalar(string? tag, string text, bool quotedImplicit = true)
    {
        return new Scalar(null, tag, text, ScalarStyle.DoubleQuoted, false, quotedImplicit);
    }

    protected static Scalar LiteralScalar(string text)
    {
        return new Scalar(null, null, text, ScalarStyle.Literal, false, true);
    }

    protected static Scalar FoldedScalar(string text)
    {
        return new Scalar(null, null, text, ScalarStyle.Folded, false, true);
    }

    protected static SequenceStart BlockSequenceStart { get { return new SequenceStart(null, null, true, YamlStyle.Block); } }

    protected static SequenceStart FlowSequenceStart { get { return new SequenceStart(null, null, true, YamlStyle.Flow); } }

    protected static SequenceStart AnchoredFlowSequenceStart(string anchor)
    {
        return new SequenceStart(anchor, null, true, YamlStyle.Flow);
    }

    protected static SequenceEnd SequenceEnd { get { return new SequenceEnd(); } }

    protected static MappingStart BlockMappingStart { get { return new MappingStart(null, null, true, YamlStyle.Block); } }

    protected static MappingStart TaggedBlockMappingStart(string tag)
    {
        return new MappingStart(null, tag, false, YamlStyle.Block);
    }

    protected static MappingStart FlowMappingStart { get { return new MappingStart(null, null, true, YamlStyle.Flow); } }

    protected static MappingEnd MappingEnd { get { return new MappingEnd(); } }

    protected static AnchorAlias AnchorAlias(string alias)
    {
        return new AnchorAlias(alias);
    }
}
