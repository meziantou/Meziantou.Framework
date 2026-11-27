using System.Globalization;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Fuzz;

// The MIDI messages of Web MIDI (https://webaudio.github.io/web-midi-api/): the validation of the data of MIDIOutput.send(),
// and the splitting of the bytes that an input port receives into the messages of the midimessage events
public static partial class WebPlatformFuzzTargets
{
    /// <summary>
    /// Validates the bytes as the data of <c>send()</c>, and splits them as the bytes of an input port, at once and in two
    /// calls. Checks that the splitter returns valid messages, one message each, whatever the bytes, the same messages when the
    /// bytes are split, and that the messages of valid data are its bytes (the System Real Time messages are returned before
    /// the message that contains them).
    /// </summary>
    public static void MidiMessagesTarget(ReadOnlySpan<byte> data)
    {
        var error = MidiMessages.Validate(data, out var containsSysEx);

        var messages = new List<byte[]>();
        new MidiMessageSplitter().Feed(data, messages);
        foreach (var message in messages)
        {
            FuzzAssert.True(MidiMessages.Validate(message, out _) is null, "The splitter returned an invalid message");
            FuzzAssert.True(message[0] == MidiMessages.SysEx || message.Length == MidiMessages.GetLength(message[0]), "The splitter returned a message of the wrong length");
        }

        if (!data.IsEmpty)
        {
            var split = data[0] % data.Length;
            var splitMessages = new List<byte[]>();
            var splitter = new MidiMessageSplitter();
            splitter.Feed(data[..split], splitMessages);
            splitter.Feed(data[split..], splitMessages);
            FuzzAssert.Equal(messages.Count, splitMessages.Count, "The messages depend on how the bytes are split");
            for (var i = 0; i < messages.Count; i++)
            {
                FuzzAssert.True(messages[i].AsSpan().SequenceEqual(splitMessages[i]), "The messages depend on how the bytes are split");
            }
        }

        if (error is null)
        {
            FuzzAssert.Equal(data.Length, messages.Sum(static message => message.Length), "The messages of valid data are not its bytes");
            FuzzAssert.Equal(containsSysEx, messages.Exists(static message => message[0] == MidiMessages.SysEx), "The system exclusive messages of valid data are not reported");
            var concatenation = messages.SelectMany(static message => message).ToArray();
            FuzzAssert.True(MidiMessages.Validate(concatenation, out _) is null, "The messages of valid data are not valid data");
        }
    }

    private static IEnumerable<FuzzSeed> GetMidiMessagesSeeds()
    {
        byte[][] seeds =
        [
            // A note on and a note off
            [0x90, 0x3C, 0x7F, 0x80, 0x3C, 0x00],

            // The running status (invalid for send(), valid on the wire)
            [0x90, 0x3C, 0x7F, 0x3C, 0x00, 0x40, 0x7F],

            // A system exclusive message with a clock inside it, a song position and a tune request
            [0xF0, 0x7E, 0x00, 0xF8, 0x06, 0x01, 0xF7, 0xF2, 0x10, 0x20, 0xF6],

            // A program change, a channel pressure, a pitch bend and an active sensing inside a control change
            [0xC0, 0x05, 0xD0, 0x40, 0xE0, 0x00, 0x40, 0xB0, 0x07, 0xFE, 0x64],

            // The errors of send(): a status byte inside a message, a reserved status byte, an unterminated system exclusive message
            [0x90, 0x3C, 0x80, 0x3C, 0x00],
            [0xF4, 0xF5, 0xF9, 0xFD],
            [0xF0, 0x01, 0x02],
            [0xF7, 0x3C, 0xF0, 0x01, 0x90, 0x3C, 0x7F],
            [],
        ];

        for (var i = 0; i < seeds.Length; i++)
        {
            yield return new FuzzSeed("midi-messages-" + i.ToString(CultureInfo.InvariantCulture), seeds[i]);
        }
    }
}
