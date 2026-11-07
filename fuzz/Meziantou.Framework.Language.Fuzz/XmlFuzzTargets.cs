using System.Buffers;
using System.Text;
using System.Xml;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Language.Xml;

namespace Meziantou.Framework.Language.Fuzz;

/// <summary>
/// XML documents: the shared invariants (the parser has no depth limit, it keeps its own stack), and the agreement with
/// System.Xml, which the library documents: a document System.Xml reads produces no diagnostic, with known exceptions.
/// The formatter must be idempotent and produce a document without diagnostics.
/// </summary>
internal static class XmlFuzzTargets
{
    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Parse,
        XmlResolver = null,

        // An entity expansion bomb is rejected quickly rather than expanded
        MaxCharactersFromEntities = 1_000_000,
    };

    /// <summary>The non-ASCII characters both System.Xml and the Fifth Edition accept in a name, but not at its start.</summary>
    private static readonly char[] NameOnlyCharacters = [.. Enumerable.Range(0x80, 0xD800 - 0x80)
        .Select(value => (char)value)
        .Where(value => XmlConvert.IsNCNameChar(value) && !XmlConvert.IsStartNCNameChar(value) && SyntaxFacts.IsNameCharacter(new Rune(value)) && !SyntaxFacts.IsNameStartCharacter(new Rune(value)))];

    private static readonly string[] Tokens =
    [
        "<", ">", "</", "/>", "<a>", "</a>", "<a/>", "<?xml version=\"1.0\"?>", "<?xml version='1.1' encoding='UTF-8' standalone='yes'?>", "<?xml", "?>", "<?pi data?>", "<!--", "-->", "--",
        "<![CDATA[", "]]>", "<!DOCTYPE a [", "<!DOCTYPE a SYSTEM \"a.dtd\">", "<!ENTITY e \"x\">", "<!ENTITY % p \"y\">", "%p;", "<!ELEMENT a ANY>", "<!ATTLIST a b CDATA #IMPLIED>", "]>",
        "&e;", "&amp;", "&lt;", "&gt;", "&quot;", "&apos;", "&#x41;", "&#65;", "&#x10FFFF;", "&#0;", "&#xD800;", "&",
        "xmlns=\"urn:a\"", "xmlns:p=\"urn:p\"", "xmlns:p=\"\"", "p:", "xml:lang=\"en\"", "xmlns:xml=", "xmlns:", "=", "\"", "'", " a=\"1\"", " b='2'",
        "\n", "\t", "\r\n", "\r", " ", "\uFEFF", "\u0001", "\uFFFE",
    ];

    public static IReadOnlyList<FuzzTarget> Targets { get; } =
    [
        new("language-xml", Parse) { Seeds = GetSeeds, Dictionary = Tokens },
        new("language-xml-format", Format) { Seeds = GetSeeds, Dictionary = Tokens },
    ];

    private static void Parse(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        var tree = XmlSyntaxTree.ParseText(text);
        var shape = SyntaxTreeInvariants.Check(tree, text, "XML", diagnosticsOnNodes: false, diagnosticsInSourceOrder: true);
        var root = tree.GetRoot();
        FuzzAssert.Equal(text, root.Nodes.ToFullString() + root.EndOfFileToken.ToFullString(), "The nodes and the end of the file do not reproduce the text");

        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case XmlAttributeSyntax attribute:
                    _ = attribute.Name;
                    _ = attribute.Value;
                    break;

                case XmlTextSyntax value:
                    _ = value.Value;
                    break;

                case XmlDocumentTypeSyntax documentType:
                    _ = documentType.Name;
                    _ = documentType.Value;
                    break;
            }
        }

        CheckAgainstSystemXml(tree, text);

        var walker = new CountingWalker();
        try
        {
            walker.Visit(root);
            SyntaxTreeInvariants.CheckWalker(shape, walker.TokenCount, walker.TriviaCount);
        }
        catch (InsufficientExecutionStackException)
        {
            // Documented by the walker for a tree nested too deeply to walk: the parser does not limit the depth
        }
    }

    private static void Format(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        var tree = XmlSyntaxTree.ParseText(text);

        // The formatter returns the document unchanged when System.Xml cannot read it
        var formatted = Formatter.Format(tree);
        if (ReferenceEquals(formatted, tree.GetRoot()))
            return;

        // The formatted document is the parse of what XmlWriter wrote, which System.Xml can read
        var formattedText = formatted.ToFullString();
        var formattedTree = (XmlSyntaxTree)formatted.SyntaxTree!;
        SyntaxTreeInvariants.Check(formattedTree, formattedText, "XML", diagnosticsOnNodes: false, diagnosticsInSourceOrder: true);
        var diagnostics = formattedTree.GetDiagnostics().Where(diagnostic => !IsKnownDivergence(diagnostic)).ToList();
        FuzzAssert.True(diagnostics.Count == 0, $"The formatted document {FuzzAssert.Format(formattedText)} has diagnostics: {Describe(diagnostics)}");

        var twice = Formatter.Format(formatted);
        FuzzAssert.Equal(formattedText, twice.ToFullString(), "Formatting the formatted document changes it");
    }

    /// <summary>A document System.Xml reads produces no diagnostic, and a document without diagnostics is one System.Xml reads.</summary>
    private static void CheckAgainstSystemXml(XmlSyntaxTree tree, string text)
    {
        // A byte order mark is the encoding signature of the document, which the parser ignores, but XmlReader reads it
        // as a character when it reads a string
        if (text.StartsWith('\uFEFF', StringComparison.Ordinal))
        {
            text = text[1..];
        }

        // A document using more of the name characters System.Xml does not know than there are characters to stand in for
        // them cannot be compared
        if (ReplaceFifthEditionNameCharacters(text) is not { } comparableText)
            return;

        var error = ReadWithXmlReader(comparableText);
        var diagnostics = tree.GetDiagnostics();
        if (error is null)
        {
            var unexpected = diagnostics.Where(diagnostic => !IsKnownDivergence(diagnostic)).ToList();
            FuzzAssert.True(unexpected.Count == 0, $"System.Xml reads the document but the parser reports {Describe(unexpected)}");
            return;
        }

        if (diagnostics.Count > 0)
            return;

        // The parser reads the document type declaration just far enough to find its end and the names of its entities, so
        // the errors inside it are System.Xml's alone
        if (tree.GetRoot().DescendantNodes().Any(node => node is XmlDocumentTypeSyntax))
            return;

        // XML 1.0 (Fifth Edition) allows any 1.x version, which System.Xml rejects
        if (error.Contains("Version number", StringComparison.Ordinal))
            return;

        FuzzAssert.Fail($"System.Xml rejects the document ({error}) but the parser reports nothing");
    }

    /// <summary>
    /// The places where the parser follows the specifications and System.Xml does not, as the readme of the library and
    /// XmlSyntaxTreeTests.MalformedDocumentsSystemXmlAccepts list them: System.Xml reading a string never looks at the
    /// encoding name, and accepts an element name with the <c>xmlns</c> prefix. It also accepts a version that only starts
    /// like <c>1.0</c>, such as <c>version="1.0  x"</c>, where XML 1.0 requires <c>1.</c> followed by digits.
    /// </summary>
    private static bool IsKnownDivergence(Diagnostic diagnostic)
        => diagnostic.Message.EndsWith("is not a valid encoding name.", StringComparison.Ordinal)
        || diagnostic.Message.EndsWith("is not a valid XML version.", StringComparison.Ordinal)
        || diagnostic.Message.Equals("An element name cannot use the 'xmlns' prefix.", StringComparison.Ordinal);

    /// <summary>
    /// System.Xml reads names with the character classes of XML 1.0 (Fourth Edition) and none outside the basic plane, but
    /// the parser follows the <c>NameStartChar</c> and <c>NameChar</c> productions of the Fifth Edition, which accept more:
    /// <c>&lt;a&#x7DE;/&gt;</c> is a well-formed document that System.Xml rejects. Each character only the Fifth Edition
    /// accepts is replaced, wherever it appears, by one of the same production that both accept and that the text does not
    /// use. The replacement is one-to-one, so names that differ still differ, and outside a name both are character data,
    /// so System.Xml still judges everything else in the document.
    /// </summary>
    /// <returns>The text to give System.Xml, or <see langword="null"/> when there are not enough characters to stand in.</returns>
    private static string? ReplaceFifthEditionNameCharacters(string text)
    {
        StringBuilder? result = null;
        Dictionary<Rune, char>? replacements = null;
        HashSet<char>? usedCharacters = null;
        var nextStartCharacter = 0x4E00; // The CJK ideographs are name start characters in both editions
        var nextNameCharacter = 0;

        var index = 0;
        while (index < text.Length)
        {
            if (Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out var length) is not OperationStatus.Done || rune.Value < 0x80 || !IsFifthEditionOnly(rune, out var isStartCharacter))
            {
                result?.Append(text, index, length);
                index += length;
                continue;
            }

            result ??= new StringBuilder(text.Length).Append(text, 0, index);
            replacements ??= [];
            usedCharacters ??= [.. text];
            if (!replacements.TryGetValue(rune, out var replacement))
            {
                if (isStartCharacter)
                {
                    while (nextStartCharacter <= 0xFFFF && (usedCharacters.Contains((char)nextStartCharacter) || !IsNameStartCharacterInBothEditions((char)nextStartCharacter)))
                    {
                        nextStartCharacter++;
                    }

                    if (nextStartCharacter > 0xFFFF)
                        return null;

                    replacement = (char)nextStartCharacter++;
                }
                else
                {
                    while (nextNameCharacter < NameOnlyCharacters.Length && usedCharacters.Contains(NameOnlyCharacters[nextNameCharacter]))
                    {
                        nextNameCharacter++;
                    }

                    if (nextNameCharacter == NameOnlyCharacters.Length)
                        return null;

                    replacement = NameOnlyCharacters[nextNameCharacter++];
                }

                replacements.Add(rune, replacement);
            }

            result.Append(replacement);
            index += length;
        }

        return result?.ToString() ?? text;

        static bool IsFifthEditionOnly(Rune rune, out bool isStartCharacter)
        {
            isStartCharacter = SyntaxFacts.IsNameStartCharacter(rune);
            if (isStartCharacter)
                return !rune.IsBmp || !IsNameStartCharacterInBothEditions((char)rune.Value);

            return SyntaxFacts.IsNameCharacter(rune) && (!rune.IsBmp || !XmlConvert.IsNCNameChar((char)rune.Value));
        }
    }

    private static bool IsNameStartCharacterInBothEditions(char value) => XmlConvert.IsStartNCNameChar(value) && SyntaxFacts.IsNameStartCharacter(new Rune(value));

    private static string? ReadWithXmlReader(string text)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), ReaderSettings);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    _ = reader.MoveToFirstAttribute();
                }
            }

            return null;
        }
        catch (XmlException ex)
        {
            return ex.Message;
        }
    }

    private static string Describe(IEnumerable<Diagnostic> diagnostics)
        => string.Join(", ", diagnostics.Select(diagnostic => $"{diagnostic.Id} at {diagnostic.Location.SourceSpan}: {diagnostic.Message}"));

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        string[] documents =
        [
            "<root><item version='1.0.0' /></root>",
            "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?>\n<!-- comment -->\n<root>\n  <book id = '1' lang=\"en\">Title &amp; &lt;more&gt; &#x41;&#66;</book>\n  <![CDATA[ <raw> & ]]>\n  <?pi some data?>\n</root>\n",
            "<root xmlns:pkg='urn:test' xmlns='urn:default'><pkg:item pkg:version='1.0.0' xml:lang='en' /></root>",
            "<!DOCTYPE root [\n  <!ELEMENT root ANY>\n  <!ATTLIST root a CDATA #IMPLIED>\n  <!ENTITY e \"entity text\">\n  <!ENTITY % p \"x\">\n]>\n<root a='&e;'>&e;</root>",
            "<!DOCTYPE html SYSTEM \"about:legacy-compat\"><html><body/></html>",
            "<root>\n  <item>\n</root>",
            "<a><b></a></b>",
            "<a b='1' b='2' c=\"<\" d=unquoted e></a>",
            "<a>&undeclared; & &#0; &#xFFFE; ]]></a>",
            "<?xml version=\"1.1\"?><a/>",
            "<?xml version=\"1.0\" encoding=\"8bit\"?><a/>",
            "<xmlns:a/>",
            "<p:a xmlns:p='urn:p' xmlns:q='urn:p' p:x='1' q:x='2'/>",
            "",
            "text<a/>",
            "<a/><b/>",
            "<!-- a -- b --><a/>",
            "<a><!-- unterminated",
            "<a><![CDATA[ unterminated",
            "<?xml version=\"1.0\"?><?xml version=\"1.0\"?><a/>",
            "\uFEFF<a>\r\n\t<b attr=\"line\nbreak\">&#x10FFFF;</b>\r</a>",
            "<a><b><c><d><e><f><g><h><i><j>deep</j></i></h></g></f></e></d></c></b></a>",
        ];

        for (var i = 0; i < documents.Length; i++)
        {
            yield return FuzzSeed.FromText(FormattableString.Invariant($"document-{i}"), documents[i]);
        }
    }

    private sealed class CountingWalker() : XmlSyntaxWalker(SyntaxWalkerDepth.Trivia)
    {
        public int TokenCount { get; private set; }

        public int TriviaCount { get; private set; }

        public override void VisitToken(SyntaxToken token)
        {
            TokenCount++;
            base.VisitToken(token);
        }

        public override void VisitTrivia(SyntaxTrivia trivia) => TriviaCount++;
    }
}
