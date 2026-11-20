using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Fuzz;

/// <summary>
/// The input is a list of operations, one per line (<c>op</c> or <c>op:argument</c>): queue an answer (<c>a</c> accepts,
/// with the text of the argument when there is one; <c>d</c> dismisses; <c>n</c> queues a handler that answers nothing),
/// toggle the handler of all the dialogs (<c>h</c>), or show a dialog (<c>A:message</c>, <c>C:message</c>,
/// <c>P:message|default</c>). The queued handlers must be used once, in order, before the handler of all the dialogs, then
/// the default answers of a browser without dialogs; the values returned to the script and the history must agree with the
/// answers, the unexpected dialogs must be flagged, and the messages are normalized.
/// </summary>
public static class DialogFuzzTargets
{
    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("aspnetcore-testing-dialogs", Dialogs)
        {
            Seeds = GetSeeds,
            Dictionary = ["\n", ":", "|", "a", "a:", "d", "n", "h", "A:", "C:", "P:", "\r", "\r\n", "Delete?", "Name"],
        },
    ];

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        yield return FuzzSeed.FromText("defaults", "A:Hello\nC:Delete?\nP:Name|Ada");
        yield return FuzzSeed.FromText("queued", "d\nC:Delete?\na:Invoice.pdf\nP:New name|Report.pdf\na\nC:Delete?\nA:Deleted");
        yield return FuzzSeed.FromText("handler", "h\nP:Name|ada\nC:Sure?\na\nh\nC:Again?");
        yield return FuzzSeed.FromText("newlines", "C:Line 1\r\nLine 2\rLine 3\nP:Name|a\rb");
        yield return FuzzSeed.FromText("unused-answers", "a\nd\na:x");
        yield return FuzzSeed.FromText("no-answer-handler", "n\nC:Delete?\nh\nn\nP:Name|x");
    }

    private static void Dialogs(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        using var page = new FuzzPage();
        page.Dialogs.Mode = DialogMode.Loose;
        var window = page.BrowsingContext.Window;
        var expectedAnswers = new Queue<UserPromptResult?>();
        var handlerEnabled = false;
        var expectedCount = 0;
        foreach (var line in text.Split('\n'))
        {
            if (line.Length == 0)
                continue;

            var argument = line.Length > 1 && line[1] == ':' ? line[2..] : null;
            switch (line[0])
            {
                case 'a':
                    page.Dialogs.AcceptNext(argument);
                    expectedAnswers.Enqueue(UserPromptResult.Accept(argument));
                    break;

                case 'd':
                    page.Dialogs.DismissNext();
                    expectedAnswers.Enqueue(UserPromptResult.Dismiss);
                    break;

                case 'n':
                    page.Dialogs.HandleNext(_ => { });
                    expectedAnswers.Enqueue(null);
                    break;

                case 'h':
                    handlerEnabled = !handlerEnabled;
                    page.Dialogs.HandleAll(handlerEnabled ? dialog => dialog.Accept(dialog.DefaultValue.ToUpperInvariant()) : null);
                    break;

                case 'A' or 'C' or 'P':
                    var message = argument ?? "";
                    var defaultValue = "";
                    if (line[0] == 'P' && message.IndexOf('|', StringComparison.Ordinal) is var separator and >= 0)
                    {
                        defaultValue = message[(separator + 1)..];
                        message = message[..separator];
                    }

                    var type = line[0] switch
                    {
                        'A' => UserPromptType.Alert,
                        'C' => UserPromptType.Confirm,
                        _ => UserPromptType.Prompt,
                    };

                    // The answer that the page must give: the queued handler, the handler of all the dialogs, else the default
                    var queued = expectedAnswers.TryDequeue(out var next) ? next : null;
                    var unexpected = queued is null && !handlerEnabled;
                    var expected = queued
                        ?? (handlerEnabled ? (type is UserPromptType.Prompt ? UserPromptResult.Accept(Normalize(defaultValue).ToUpperInvariant()) : UserPromptResult.Accept()) : null)
                        ?? (type is UserPromptType.Alert ? UserPromptResult.Accept() : UserPromptResult.Dismiss);
                    var expectedText = type is UserPromptType.Prompt && expected.Accepted ? expected.Text ?? Normalize(defaultValue) : null;

                    switch (type)
                    {
                        case UserPromptType.Alert:
                            window.Alert(message);
                            break;

                        case UserPromptType.Confirm:
                            FuzzAssert.Equal(expected.Accepted, window.Confirm(message), "confirm() returns whether the dialog is accepted");
                            break;

                        default:
                            // prompt() returns the text of the answer, or the default value as the script passed it
                            var actual = window.Prompt(message, defaultValue);
                            FuzzAssert.Equal(expected.Accepted ? expected.Text ?? defaultValue : null, actual, "prompt() returns the text of the answer, the default value, or null");
                            break;
                    }

                    expectedCount++;
                    var dialogs = page.Dialogs.History;
                    FuzzAssert.Equal(expectedCount, dialogs.Count, "Each dialog is recorded once");
                    var dialog = dialogs[^1];
                    FuzzAssert.Equal(type, dialog.Type, "The type of the recorded dialog");
                    FuzzAssert.Equal(Normalize(message), dialog.Message, "The message of the recorded dialog is normalized");
                    FuzzAssert.Equal(type is UserPromptType.Prompt ? Normalize(defaultValue) : "", dialog.DefaultValue, "The default value of the recorded dialog");
                    FuzzAssert.Equal(expected.Accepted, dialog.Accepted, "The recorded answer");
                    FuzzAssert.Equal(expectedText, dialog.Text, "The recorded text is the value returned by prompt()");
                    FuzzAssert.Equal(unexpected, dialog.IsUnexpected, "A dialog that no handler answers is unexpected");
                    FuzzAssert.True(dialog.IsAnswered, "A recorded dialog has an answer");
                    FuzzAssert.True(ReferenceEquals(page, dialog.Page), "The page of the dialog");
                    FuzzAssert.False(dialog.Message.Contains('\r', StringComparison.Ordinal), "The message has no carriage return");
                    break;
            }

            FuzzAssert.Equal(expectedAnswers.Count, page.Dialogs.PendingCount, "The queued handlers are used once, in order");
        }
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private sealed class FuzzPage : TestPage
    {
        public FuzzPage()
            : base(new TestPageSettings())
        {
        }
    }
}
