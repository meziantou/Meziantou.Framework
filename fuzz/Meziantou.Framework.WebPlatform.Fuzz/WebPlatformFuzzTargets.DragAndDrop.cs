using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using FileSystemEntry = Meziantou.Framework.WebPlatform.Dom.FileApi.FileSystemEntry;

namespace Meziantou.Framework.WebPlatform.Fuzz;

public static partial class WebPlatformFuzzTargets
{
    // The operations of the drag-and-drop target: an operation byte, then its arguments (one byte each)
    private const byte DragMove = 0; // x, y
    private const byte DragDown = 1;
    private const byte DragUp = 2;
    private const byte DragEscape = 3;
    private const byte DragBegin = 4; // content
    private const byte DragDrop = 5; // x, y, content
    private const int DragOperationCount = 6;

    /// <summary>The maximum number of operations of an input of the drag-and-drop target.</summary>
    private const int DragMaxOperations = 64;

    /// <summary>
    /// The page of the drag-and-drop target, in a 200x200 grid of 100px boxes: a draggable element, a drop zone that accepts
    /// everything (and reads the dropped directories with the Entries API), a file input with webkitdirectory, a textarea.
    /// </summary>
    private const string DragDocumentMarkup = """
        <!DOCTYPE html>
        <style>body { margin: 0; display: grid; grid-template-columns: 100px 100px } body > * { display: block; width: 100px; height: 100px; margin: 0; padding: 0; border: 0 }</style>
        <div id="source" draggable="true">drag</div><div id="zone"></div>
        <input id="files" type="file" webkitdirectory><textarea id="text"></textarea>
        """;

    private static string[] DragTokens => ["\u0000  ", "\u0000\u00A0\u00A0", "\u0001", "\u0002", "\u0003", "\u0004\u0000", "\u0004\u0003", "\u0005\u00A0 \u0001"];

    private static string[] EntriesPathTokens => ["/", "//", ".", "..", "../", "./", "a", "sub", "\0", "\n"];

    /// <summary>
    /// Resolves a path of the Entries API (the text after the first line feed) against the full path of a directory (the
    /// first line), and checks the result: it has no empty, <c>.</c> or <c>..</c> name, resolving its full path gives it back,
    /// and a relative path resolves as the absolute path made of the directory and the path.
    /// </summary>
    public static void EntriesPathResolution(string text)
    {
        var separator = text.IndexOf('\n', StringComparison.Ordinal);
        var directory = separator < 0 ? "/" : text[..separator];
        var path = separator < 0 ? text : text[(separator + 1)..];
        if (!EntriesPath.IsValid(path) || !EntriesPath.IsValid(directory) || (directory.Length > 0 && directory[0] != '/'))
            return;

        var directoryNames = EntriesPath.Resolve("", directory);
        var directoryFullPath = EntriesPath.ToFullPath(directoryNames);
        var result = EntriesPath.Resolve(directoryFullPath, path);
        foreach (var name in result)
        {
            FuzzAssert.True(name is not ("" or "." or ".."), $"The resolved path of {FuzzAssert.Format(path)} has the name {FuzzAssert.Format(name)}");
        }

        var fullPath = EntriesPath.ToFullPath(result);
        FuzzAssert.True(EntriesPath.IsValid(fullPath), $"The full path {FuzzAssert.Format(fullPath)} is not valid");
        FuzzAssert.Equal(fullPath, EntriesPath.ToFullPath(EntriesPath.Resolve(directoryFullPath, fullPath)), "Resolving a full path gives it back");
        FuzzAssert.Equal(fullPath, EntriesPath.ToFullPath(EntriesPath.Resolve("/", fullPath)), "A full path does not depend on the directory");
        if (path.Length > 0 && path[0] != '/')
        {
            FuzzAssert.Equal(fullPath, EntriesPath.ToFullPath(EntriesPath.Resolve("/", directoryFullPath + "/" + path)), "A relative path resolves from the directory");
            if (!path.Split('/').Contains("..", StringComparer.Ordinal))
            {
                FuzzAssert.True(fullPath.StartsWith(directoryFullPath, StringComparison.Ordinal), "A relative path without .. stays in the directory");
            }
        }
    }

    /// <summary>
    /// Plays a sequence of moves and presses of the mouse, Escape, and drags of files and directories from outside the page
    /// (see the operations above), and checks the drag-and-drop events: dragstart and dragend are balanced, every dragenter
    /// is followed by a dragleave or a drop, nothing remains of the drag once the button is released, and the files that the
    /// page reads with the Entries API are the dropped files.
    /// </summary>
    public static void DragAndDrop(ReadOnlySpan<byte> data)
    {
        using var context = new BrowsingContext(new BrowsingContextOptions { TimeProvider = FrozenTimeProvider.Instance });
        var document = context.Navigate("https://example.com/", _ => DragDocumentMarkup);
        context.EventLoop.RunUntilIdle();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var type in (string[])["dragstart", "dragend", "dragenter", "dragleave", "drop"])
        {
            document.AddEventListener(type, e =>
            {
                FuzzAssert.True(e.IsTrusted, $"The {e.Type} event is not trusted");
                counts[e.Type] = counts.GetValueOrDefault(e.Type) + 1;
            }, new AddEventListenerOptions { Capture = true });
        }

        var zone = document.GetElementById("zone")!;
        zone.AddEventListener("dragover", e => e.PreventDefault());
        var expectedFiles = new List<string>();
        var readFiles = new List<string>();
        zone.AddEventListener("drop", e =>
        {
            e.PreventDefault();
            foreach (var item in ((DragEvent)e).DataTransfer!.Items)
            {
                if (item.WebkitGetAsEntry() is { } entry)
                {
                    ReadEntry(entry);
                }
            }
        });

        var input = context.Input;
        var mouse = input.Mouse;
        for (var operation = 0; operation < DragMaxOperations && !data.IsEmpty; operation++)
        {
            switch (FuzzInput.ReadByte(ref data) % DragOperationCount)
            {
                case DragMove:
                    mouse.Move(ReadDragCoordinate(ref data), ReadDragCoordinate(ref data));
                    break;

                case DragDown:
                    mouse.Down();
                    break;

                case DragUp:
                    mouse.Up();
                    break;

                case DragEscape:
                    input.Keyboard.Press("Escape");
                    break;

                case DragBegin:
                    var dragData = CreateDragData(FuzzInput.ReadByte(ref data));
                    try
                    {
                        mouse.BeginDrag(dragData);
                    }
                    catch (InvalidOperationException)
                    {
                        // A button is pressed
                        continue;
                    }

                    AddExpectedFiles(dragData);
                    break;

                case DragDrop:
                    var (x, y) = (ReadDragCoordinate(ref data), ReadDragCoordinate(ref data));
                    var droppedData = CreateDragData(FuzzInput.ReadByte(ref data));
                    try
                    {
                        input.Drop(x, y, droppedData);
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }

                    AddExpectedFiles(droppedData);
                    break;
            }

            var dragging = mouse.Buttons != 0;
            var open = counts.GetValueOrDefault("dragenter") - counts.GetValueOrDefault("dragleave") - counts.GetValueOrDefault("drop");
            FuzzAssert.True(open is 0 or 1, $"{open} dragenter events have no dragleave or drop");
            FuzzAssert.True(dragging || open == 0, "A dragenter event has no dragleave or drop after the release");
            var started = counts.GetValueOrDefault("dragstart") - counts.GetValueOrDefault("dragend");
            FuzzAssert.True(started is 0 or 1, $"{started} dragstart events have no dragend");
            FuzzAssert.True(dragging || started == 0, "A dragstart event has no dragend after the release");
        }

        mouse.Up();
        context.EventLoop.RunUntilIdle();
        foreach (var file in readFiles)
        {
            FuzzAssert.True(expectedFiles.Contains(file, StringComparer.Ordinal), $"The file {FuzzAssert.Format(file)} read by the page was not dropped");
        }

        void ReadEntry(FileSystemEntry entry)
        {
            switch (entry)
            {
                case FileSystemFileEntry file:
                    file.File(f => readFiles.Add(file.FullPath + ":" + f.Size));
                    break;

                case FileSystemDirectoryEntry directory:
                    directory.CreateReader().ReadEntries(entries =>
                    {
                        foreach (var child in entries)
                        {
                            FuzzAssert.Equal(directory.FullPath.TrimEnd('/') + "/" + child.Name, child.FullPath, "The full path of a child entry");
                            ReadEntry(child);
                        }
                    });
                    break;
            }
        }

        void AddExpectedFiles(DragData dragData)
        {
            foreach (var entry in dragData.Entries)
            {
                AddFiles(entry, "/" + entry.Name);
            }

            void AddFiles(DragDataEntry entry, string path)
            {
                switch (entry)
                {
                    case DragDataFile file:
                        expectedFiles.Add(path + ":" + file.Content.Length);
                        break;

                    case DragDataDirectory directory:
                        foreach (var child in directory.Entries)
                        {
                            AddFiles(child, path + "/" + child.Name);
                        }

                        break;
                }
            }
        }
    }

    private static double ReadDragCoordinate(ref ReadOnlySpan<byte> data) => FuzzInput.ReadByte(ref data) * 220d / 255d;

    /// <summary>The data of a drag from outside the page: files, a directory with a subdirectory, and text, depending on the bits of <paramref name="content"/>.</summary>
    private static DragData CreateDragData(byte content)
    {
        var data = new DragData();
        if ((content & 1) != 0)
        {
            data.AddFile("a.txt", Encoding.UTF8.GetBytes("abc"));
        }

        if ((content & 2) != 0)
        {
            var directory = data.AddDirectory("folder");
            directory.AddFile("b.png", new byte[content >> 4]);
            directory.AddDirectory("sub").AddFile("c", ReadOnlyMemory<byte>.Empty);
        }

        if ((content & 4) != 0)
        {
            data.SetString("text/plain", "text");
        }

        if ((content & 8) != 0)
        {
            data.EffectAllowed = "move";
        }

        return data;
    }

    private static IEnumerable<FuzzSeed> GetEntriesPathSeeds()
    {
        string[] seeds = ["/folder\nsub/../sub/b.txt", "/folder\nmissing.txt", "/folder\n/", "/folder\n", "/\n..", "/a/b\n../../c/./d", "/a\n//b", "/a\nb/", "/\n/a/../../b"];
        foreach (var seed in seeds)
        {
            yield return new FuzzSeed(seed.Replace('\n', '_').Replace('/', '-'), Encoding.UTF8.GetBytes(seed));
        }
    }

    private static IEnumerable<FuzzSeed> GetDragAndDropSeeds()
    {
        byte[][] scenarios =
        [
            // The element is dragged to the drop zone and dropped
            [DragMove, 50, 50, DragDown, DragMove, 60, 60, DragMove, 160, 50, DragUp],

            // The drag of the element is canceled with Escape over the drop zone
            [DragMove, 50, 50, DragDown, DragMove, 160, 50, DragEscape, DragUp],

            // Files and directories dragged from outside over the page, then dropped on the zone
            [DragMove, 10, 10, DragBegin, 0x37, DragMove, 160, 50, DragMove, 160, 60, DragUp],

            // Files dropped at once on the file input, the textarea and the zone
            [DragDrop, 50, 160, 0x23, DragDrop, 160, 160, 0x05, DragDrop, 160, 50, 0x0B],

            // A drag from outside canceled with Escape, and a drag started while a button is pressed
            [DragBegin, 0x01, DragMove, 160, 50, DragEscape, DragDown, DragBegin, 0x02, DragUp],
        ];

        for (var i = 0; i < scenarios.Length; i++)
        {
            yield return new FuzzSeed($"scenario-{i}", scenarios[i]);
        }
    }
}
