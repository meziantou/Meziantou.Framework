using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Fuzz;

public static partial class WebPlatformFuzzTargets
{
    /// <summary>The maximum length of the input of the find-in-page target (a query and a page).</summary>
    private const int WindowFindMaxLength = 4096;

    /// <summary>The maximum number of successive searches of the find-in-page target.</summary>
    private const int WindowFindMaxSearches = 32;

    /// <summary>The maximum number of keys of an input of the date and time fields target.</summary>
    private const int DateTimeFieldsMaxKeys = 64;

    // The keys of the date and time fields target: a byte selects one of them (the first byte selects the type of the control)
    private static readonly string[] DateTimeFieldsKeys = ["0", "1", "2", "3", "5", "9", "a", "p", "Tab", "Backspace", "Delete", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight"];

    private static readonly string[] DateTimeFieldsTypes = ["date", "month", "week", "time", "datetime-local"];

    private static string[] DateTimeFieldsTokens => ["\u0000", "\u0001", "\u0002", "\u0003", "\u0004"];

    private static string[] WindowFindTokens => ["\n", "<p>", "<b>", "</b>", "<div inert>", "</div>", "<span style='display:none'>", "<br>", " ", "\u00A0", "&nbsp;", "a", "A", "ab", "é"];

    /// <summary>
    /// Searches a text (the first line of the input) in a page (the rest of the input) with <c>window.find()</c>, repeatedly,
    /// and checks the matches: the selected text is at least as long as the searched text (once the white space is
    /// collapsed), and the search that wraps around finds a match when the first search found one.
    /// </summary>
    public static void WindowFind(string text)
    {
        var separator = text.IndexOf('\n', StringComparison.Ordinal);
        var query = separator < 0 ? text : text[..separator];
        var markup = separator < 0 ? "" : text[(separator + 1)..];
        using var context = new BrowsingContext(new BrowsingContextOptions { TimeProvider = FrozenTimeProvider.Instance });
        var document = context.Navigate("https://example.com/", _ => markup);
        context.EventLoop.RunUntilIdle();
        var window = document.DefaultView!;
        var selection = document.GetSelection()!;
#pragma warning disable WEBPLATEXP0028 // window.find() is not specified
        var foundFirst = false;
        var expectedLength = CollapseWhiteSpace(query).Length;
        for (var i = 0; i < WindowFindMaxSearches; i++)
        {
            if (!window.Find(query))
                break;

            foundFirst = true;
            if (selection.RangeCount == 0)
                continue;

            // The match may also contain the text that is not rendered (e.g. an element with display: none between two words)
            var matched = CollapseWhiteSpace(selection.GetRangeAt(0).ToString());
            FuzzAssert.True(matched.Length >= expectedLength, $"The selected match {FuzzAssert.Format(matched)} is shorter than the searched text {FuzzAssert.Format(query)}");
        }

        if (foundFirst)
        {
            FuzzAssert.True(window.Find(query, wrapAround: true), "The search that wraps around finds a match");
        }

        window.Find(query, backwards: true, wrapAround: true, wholeWord: true, caseSensitive: true);
#pragma warning restore WEBPLATEXP0028

        static string CollapseWhiteSpace(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                if (!char.IsWhiteSpace(c))
                {
                    builder.Append(c);
                }
                else if (builder.Length == 0 || builder[^1] != ' ')
                {
                    builder.Append(' ');
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// Types keys (see <see cref="DateTimeFieldsKeys"/>) in a date and time control (the type is selected by the first byte),
    /// and checks its value after each key: the empty string or a valid value of the type (setting it again keeps it).
    /// </summary>
    public static void DateTimeFields(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        var type = DateTimeFieldsTypes[FuzzInput.ReadByte(ref data) % DateTimeFieldsTypes.Length];
        using var context = new BrowsingContext(new BrowsingContextOptions { TimeProvider = FrozenTimeProvider.Instance });
        var document = context.Navigate("https://example.com/", _ => $"<!DOCTYPE html><input id=control type={type}><input id=next>");
        context.EventLoop.RunUntilIdle();
        var control = (HtmlInputElement)document.GetElementById("control")!;
        control.Focus();
        var keyboard = context.Input.Keyboard;
        for (var i = 0; i < DateTimeFieldsMaxKeys && !data.IsEmpty; i++)
        {
            var key = DateTimeFieldsKeys[FuzzInput.ReadByte(ref data) % DateTimeFieldsKeys.Length];
            keyboard.Press(key);
            if (!ReferenceEquals(document.ActiveElement, control))
            {
                control.Focus();
            }

            var value = control.Value;
            if (value.Length > 0)
            {
                control.Value = value;
                FuzzAssert.Equal(value, control.Value, "The value of the fields is a valid value of the control");
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetWindowFindSeeds()
    {
        string[] inputs =
        [
            "me\n<p>Find me, then find ME</p>",
            "abcd\n<p>a<b>bc</b>d</p><div inert>abcd</div>",
            "word\n<p>wordy word</p><p>word</p>",
            "inside shadowroot\n<div><template shadowrootmode=open><dialog open>inside shadowroot<slot></slot></dialog></template>slotted</div>",
            "MATCH\n<div>MATCH-1<div style='interactivity: inert'>MATCH-2<div style='interactivity: auto'>MATCH-3</div></div></div>",
        ];
        for (var i = 0; i < inputs.Length; i++)
        {
            yield return new FuzzSeed("find-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), Encoding.UTF8.GetBytes(inputs[i]));
        }
    }

    private static IEnumerable<FuzzSeed> GetDateTimeFieldsSeeds()
    {
        static byte Key(string key) => (byte)Array.IndexOf(DateTimeFieldsKeys, key);
        byte[][] scenarios =
        [
            // A date typed field by field, then a field cleared (as css/selectors/user-valid.html and user-invalid.html)
            [0, Key("1"), Key("Tab"), Key("1"), Key("Tab"), Key("1"), Key("2"), Key("3"), Key("5"), Key("Backspace")],

            // A date whose fields are filled by their digits, then stepped
            [0, Key("1"), Key("2"), Key("3"), Key("1"), Key("2"), Key("0"), Key("2"), Key("5"), Key("ArrowUp"), Key("ArrowLeft"), Key("ArrowDown")],

            // A time in the afternoon, a month, a week, and a local date and time
            [3, Key("0"), Key("9"), Key("3"), Key("0"), Key("p")],
            [1, Key("1"), Key("2"), Key("2"), Key("0"), Key("2"), Key("5")],
            [2, Key("5"), Key("3"), Key("1"), Key("9"), Key("9"), Key("9")],
            [4, Key("2"), Key("2"), Key("9"), Key("2"), Key("0"), Key("2"), Key("5"), Key("1"), Key("2"), Key("0"), Key("0"), Key("a")],
        ];

        for (var i = 0; i < scenarios.Length; i++)
        {
            yield return new FuzzSeed("date-time-fields-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), scenarios[i]);
        }
    }
}
