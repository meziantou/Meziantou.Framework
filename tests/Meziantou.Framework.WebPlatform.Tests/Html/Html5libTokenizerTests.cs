using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.Html.Parsing;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>html5lib-tests/tokenizer (https://github.com/html5lib/html5lib-tests/tree/master/tokenizer).</summary>
public sealed class Html5libTokenizerTests
{
    /// <summary>
    /// Returns why a test is wrong upstream (it contradicts the HTML Standard or the other tests of the suite), or <see langword="null"/>.
    /// The processing instruction tests were added by html5lib/html5lib-tests@c777c408 (https://github.com/whatwg/html/pull/10939).
    /// </summary>
    private static string? GetUpstreamTestBug(JsonNode test, string input)
    {
        var errors = test["errors"]?.AsArray() ?? [];

        // test3.test: "<?a a", "<?Z Z?"... expect the error at column 4 (after "<?a") instead of at the end of the input,
        // where every other EOF error of the suite is reported (e.g. "<?Z" expects it at column 4 = input length + 1)
        if (!input.Contains('\n', StringComparison.Ordinal) && errors.Any(e => (string)e!["code"]! == "eof-in-processing-instruction" && (int)e["col"]! != input.Length + 1))
            return "Upstream bug: eof-in-processing-instruction is expected before the end of the input";

        // test3.test: the control character is reported when the input stream is preprocessed, so before the error of the
        // processing instruction target, as for "<\u000B" (control-character-in-input-stream then invalid-first-character-of-tag-name)
        if (input == "<?\u000B")
            return "Upstream bug: control-character-in-input-stream is expected after invalid-first-character-of-processing-instruction-target";

        // domjs.test: "<?\r"... still expect the error code that the processing instruction change replaced by
        // invalid-first-character-of-processing-instruction-target
        if (errors.Any(e => (string)e!["code"]! == "unexpected-question-mark-instead-of-tag-name"))
            return "Upstream bug: unexpected-question-mark-instead-of-tag-name no longer exists, \"<?\" starts a processing instruction";

        return null;
    }

    public static TheoryData<string, int, string> Tests()
    {
        var data = new TheoryData<string, int, string>();
        foreach (var file in Directory.GetFiles(TestPaths.External("html5lib-tests", "tokenizer"), "*.test").Order(StringComparer.Ordinal))
        {
            var json = JsonNode.Parse(File.ReadAllText(file))!;
            var tests = json["tests"]?.AsArray();
            if (tests is null)
                continue;

            for (var i = 0; i < tests.Count; i++)
            {
                var states = tests[i]!["initialStates"]?.AsArray().Select(s => (string)s!).ToArray() ?? ["Data state"];
                foreach (var state in states)
                {
                    data.Add(Path.GetFileName(file), i, state);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Tests))]
    public void Tokenize(string file, int index, string initialState)
    {
        var test = TestJson.ToNode(TestJson.Load(TestPaths.External("html5lib-tests", "tokenizer", file)).GetProperty("tests")[index])!;
        var doubleEscaped = test["doubleEscaped"]?.GetValue<bool>() == true;
        var input = Unescape((string)test["input"]!, doubleEscaped);
        TestContext.Current.TestOutputHelper?.WriteLine((string?)test["description"] ?? "");
        Assert.SkipWhen(file == "xmlViolation.test", "XML violation mode is not implemented (the HTML Standard does not define it)");
        var upstreamTestBug = GetUpstreamTestBug(test, input);
        Assert.SkipWhen(upstreamTestBug is not null, upstreamTestBug ?? "");

        var errors = new List<HtmlParseError>();
        var tokenizer = new HtmlTokenizer(input, error => errors.Add(error.ToParseError()), new NameTable())
        {
            State = initialState switch
            {
                "Data state" => TokenizerState.Data,
                "PLAINTEXT state" => TokenizerState.Plaintext,
                "RCDATA state" => TokenizerState.Rcdata,
                "RAWTEXT state" => TokenizerState.Rawtext,
                "Script data state" => TokenizerState.ScriptData,
                "CDATA section state" => TokenizerState.CdataSection,
                _ => throw new InvalidOperationException(initialState),
            },
            LastStartTagName = (string?)test["lastStartTag"],
        };

        var actual = new JsonArray();
        while (tokenizer.NextToken() != HtmlTokenType.EndOfFile)
        {
            switch (tokenizer.TokenType)
            {
                case HtmlTokenType.Characters:
                    // Merge with the previous character token (html5lib coalesces adjacent character tokens)
                    var characters = tokenizer.Characters.ToString();
                    if (actual.Count > 0 && actual[^1] is JsonArray previous && (string)previous[0]! == "Character")
                    {
                        previous[1] = (string)previous[1]! + characters;
                    }
                    else
                    {
                        actual.Add(new JsonArray("Character", characters));
                    }

                    break;
                case HtmlTokenType.StartTag:
                {
                    var attributes = new JsonObject();
                    foreach (var attribute in tokenizer.Tag!.Attributes)
                    {
                        attributes[attribute.Name] = attribute.Value;
                    }

                    var token = new JsonArray("StartTag", tokenizer.Tag.Name, attributes);
                    if (tokenizer.Tag.SelfClosing)
                    {
                        token.Add(true);
                    }

                    actual.Add(token);
                    break;
                }

                case HtmlTokenType.EndTag:
                    actual.Add(new JsonArray("EndTag", tokenizer.Tag!.Name));
                    break;
                case HtmlTokenType.Comment:
                    actual.Add(new JsonArray("Comment", tokenizer.CommentData));
                    break;
                case HtmlTokenType.Doctype:
                    actual.Add(new JsonArray("DOCTYPE", tokenizer.DoctypeName, tokenizer.DoctypePublicId, tokenizer.DoctypeSystemId, !tokenizer.DoctypeForceQuirks));
                    break;
                case HtmlTokenType.ProcessingInstruction:
                    actual.Add(new JsonArray("ProcessingInstruction", tokenizer.ProcessingInstructionTarget, tokenizer.ProcessingInstructionData));
                    break;
            }
        }

        var expected = new JsonArray();
        foreach (var token in test["output"]!.AsArray())
        {
            var copy = JsonNode.Parse(token!.ToJsonString())!.AsArray();
            if (doubleEscaped)
            {
                for (var i = 0; i < copy.Count; i++)
                {
                    if (copy[i] is JsonValue value && value.TryGetValue<string>(out var s))
                    {
                        copy[i] = Unescape(s, doubleEscaped: true);
                    }
                }
            }

            // Merge adjacent character tokens in the expected output as well
            if ((string)copy[0]! == "Character" && expected.Count > 0 && expected[^1] is JsonArray previous && (string)previous[0]! == "Character")
            {
                previous[1] = (string)previous[1]! + (string)copy[1]!;
                continue;
            }

            expected.Add(copy);
        }

        Assert.Equal(Normalize(expected), Normalize(actual));

        var expectedErrors = test["errors"]?.AsArray().Select(e => $"{(string)e!["code"]!}@{(int)e["line"]!}:{(int)e["col"]!}").ToArray() ?? [];
        var actualErrors = errors.Select(e => $"{e.Code}@{e.Line}:{e.Column}").ToArray();
        Assert.Equal(expectedErrors, actualErrors);
    }

    private static string Normalize(JsonArray tokens)
    {
        // Attributes are compared regardless of their order
        foreach (var token in tokens)
        {
            if (token is JsonArray array && (string)array[0]! == "StartTag" && array[2] is JsonObject attributes)
            {
                var sorted = new JsonObject();
                foreach (var property in attributes.OrderBy(p => p.Key, StringComparer.Ordinal).ToList())
                {
                    attributes.Remove(property.Key);
                    sorted[property.Key] = property.Value;
                }

                array[2] = sorted;
            }
        }

        return tokens.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true });
    }

    private static string Unescape(string value, bool doubleEscaped)
    {
        if (!doubleEscaped)
            return value;

        var builder = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 5 < value.Length + 0 && value[i + 1] == 'u')
            {
                builder.Append((char)int.Parse(value.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                i += 5;
                continue;
            }

            builder.Append(value[i]);
        }

        return builder.ToString();
    }
}
