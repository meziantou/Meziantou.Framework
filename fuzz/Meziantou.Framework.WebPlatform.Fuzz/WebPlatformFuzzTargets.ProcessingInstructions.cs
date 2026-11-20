using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Fuzz;

// The pseudo-attributes of the processing instructions (https://dom.spec.whatwg.org/#concept-pi-update-attributes-from-data)
public static partial class WebPlatformFuzzTargets
{
    /// <summary>The tokens of the pseudo-attributes target (a property: the static fields of the partial files are initialized in an unspecified order).</summary>
    private static string[] ProcessingInstructionAttributesTokens =>
    [
        "=", "\"", "'", " ", "\t", "\n", "\r", "&amp;", "&lt;", "&gt;", "&quot;", "&apos;", "&#", "&#x", ";", "<", ">", "\0",
        "href", "type", "media", "title", "alternate", "charset", "xml:", "_", "-", ".", "\u00A0", "\uD83D\uDE00", "\uD800",
    ];

    /// <summary>
    /// Parses the data of a processing instruction into its pseudo-attributes and checks the result: the names are unique
    /// and valid attribute names, and the data that a processing instruction serializes from the attributes parses to the
    /// same attributes, in the same order.
    /// </summary>
    public static void ProcessingInstructionAttributes(string input)
    {
        var attributes = PseudoAttributes.Parse(input);
        var document = new Document();
        var pi = document.CreateProcessingInstruction("t", input.Replace("?>", "", StringComparison.Ordinal));
        if (attributes is null)
            return;

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, _) in attributes)
        {
            FuzzAssert.True(names.Add(name), $"The data {FuzzAssert.Format(input)} has the pseudo-attribute {FuzzAssert.Format(name)} twice");
        }

        var serialized = document.CreateProcessingInstruction("t", "");
        foreach (var (name, value) in attributes)
        {
            serialized.SetAttribute(name, value);
        }

        var reparsed = PseudoAttributes.Parse(serialized.Data);
        FuzzAssert.True(reparsed is not null, $"The data {FuzzAssert.Format(serialized.Data)} serialized from the attributes of {FuzzAssert.Format(input)} has no attributes");
        FuzzAssert.Equal(Describe(attributes), Describe(reparsed), $"The attributes of {FuzzAssert.Format(input)} do not round-trip through {FuzzAssert.Format(serialized.Data)}");
        if (!input.Contains("?>", StringComparison.Ordinal))
        {
            FuzzAssert.Equal(Describe(attributes), Describe(pi.GetAttributeNames().Select(name => new KeyValuePair<string, string>(name, pi.GetAttribute(name)!)).ToList()), $"The processing instruction of {FuzzAssert.Format(input)} does not expose its pseudo-attributes");
        }

        static string Describe(List<KeyValuePair<string, string>> attributes)
        {
            var result = new StringBuilder();
            foreach (var (name, value) in attributes)
            {
                result.Append(name).Append('=').Append(value).Append('\u0001');
            }

            return result.ToString();
        }
    }

    /// <summary>The seeds of the pseudo-attributes target: the data of the unit tests and of the web-platform-tests.</summary>
    private static IEnumerable<FuzzSeed> GetProcessingInstructionAttributesSeeds()
    {
        string[] inputs =
        [
            "",
            "a=\"b\" x=\"yy\"",
            "a=b x=\"yy\"",
            "name=\"p\" name=\"q\"",
            "name=\"p\" junk",
            "junk name=\"p\"",
            "name=\"p\"other=\"q\"",
            "name=\"p\" =\"q\"",
            "name=\"p\" other=",
            "name=\"p",
            "name=\"p\" other=\"q",
            "_x=\"yy\"",
            "attr=\"value\"",
            "a='b'   c = \"d\"",
            "type=\"text/css\" href=\"style.css\"",
            "name=\"a\0b\"",
            "\uFFFD=\"c\"",
            "attr=\"&amp;&lt;&gt;&quot;&apos;&#65;&#x42;\"",
            "attr=\"&#0;\"",
            "attr=\"&unknown;\"",
            "blabla=\"\"",
            "href=\"a.css\" alternate=\"yes\" title=\"t\" media=\"print\"",
        ];

        for (var i = 0; i < inputs.Length; i++)
        {
            yield return new FuzzSeed("pi-attributes-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), Encoding.UTF8.GetBytes(inputs[i]));
        }
    }
}
