using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.DevTools.Targets;

// The Input domain: the events are dispatched with UserInput (in the coordinates of the viewport of the page)
internal sealed partial class PageSession
{
    private readonly Dictionary<int, PointerInput> _touchPointers = [];
    private bool _interceptDrags;
    private bool _dragIntercepted;

    private Task<JsonObject?> InvokeInputAsync(string method, JsonElement parameters)
    {
        switch (method)
        {
            case "Input.dispatchMouseEvent":
                DispatchMouseEvent(parameters);
                return Task.FromResult<JsonObject?>(null);

            case "Input.dispatchKeyEvent":
                DispatchKeyEvent(parameters);
                return Task.FromResult<JsonObject?>(null);

            case "Input.insertText":
                Target.EnsureLive();
                Target.BrowsingContext!.Input.Keyboard.InsertText(parameters.GetString("text") ?? "");
                return Task.FromResult<JsonObject?>(null);

            case "Input.dispatchTouchEvent":
                DispatchTouchEvent(parameters);
                return Task.FromResult<JsonObject?>(null);

            case "Input.setInterceptDrags":
                _interceptDrags = parameters.GetBoolean("enabled", defaultValue: false);
                _dragIntercepted = false;
                return Task.FromResult<JsonObject?>(null);

            case "Input.dispatchDragEvent":
                DispatchDragEvent(parameters);
                return Task.FromResult<JsonObject?>(null);

            case "Input.setIgnoreInputEvents":
                return Task.FromResult<JsonObject?>(null);

            default:
                throw ProtocolException.NotFound(method);
        }
    }

    private UserInput GetInput(JsonElement parameters)
    {
        Target.EnsureLive();
        var input = Target.BrowsingContext!.Input;
        if (parameters.GetInt32("modifiers") is { } modifiers)
        {
            // The modifiers of the protocol: Alt=1, Ctrl=2, Meta=4, Shift=8
            var result = InputModifiers.None;
            if ((modifiers & 1) != 0)
            {
                result |= InputModifiers.Alt;
            }

            if ((modifiers & 2) != 0)
            {
                result |= InputModifiers.Control;
            }

            if ((modifiers & 4) != 0)
            {
                result |= InputModifiers.Meta;
            }

            if ((modifiers & 8) != 0)
            {
                result |= InputModifiers.Shift;
            }

            input.Modifiers = result;
        }

        return input;
    }

    private void DispatchMouseEvent(JsonElement parameters)
    {
        var input = GetInput(parameters);
        var mouse = input.Mouse;
        var x = parameters.GetRequiredDouble("x");
        var y = parameters.GetRequiredDouble("y");
        var button = GetButton(parameters.GetString("button"));
        switch (parameters.GetRequiredString("type"))
        {
            case "mouseMoved":
                mouse.Move(x, y);
                if (_interceptDrags && !_dragIntercepted && mouse.DragOperation is not null)
                {
                    // The drag started by the move is given to the client, which then sends the drag events (Input.dispatchDragEvent)
                    _dragIntercepted = true;
                    SendEvent("Input.dragIntercepted", new JsonObject { ["data"] = new JsonObject { ["items"] = new JsonArray(), ["dragOperationsMask"] = 65535 } });
                }

                break;

            case "mousePressed":
                if (mouse.X != x || mouse.Y != y)
                {
                    mouse.Move(x, y);
                }

                if (button >= 0)
                {
                    mouse.Down(button);
                }

                break;

            case "mouseReleased":
                if (mouse.X != x || mouse.Y != y)
                {
                    mouse.Move(x, y);
                }

                if (button >= 0)
                {
                    mouse.Up(button);
                }

                break;

            case "mouseWheel":
                input.Wheel(x, y, parameters.GetDouble("deltaX") ?? 0, parameters.GetDouble("deltaY") ?? 0);
                break;

            default:
                throw ProtocolException.InvalidParameters("type: unknown mouse event type");
        }
    }

    private static int GetButton(string? button) => button switch
    {
        "left" => 0,
        "middle" => 1,
        "right" => 2,
        "back" => 3,
        "forward" => 4,
        _ => -1,
    };

    private void DispatchKeyEvent(JsonElement parameters)
    {
        var keyboard = GetInput(parameters).Keyboard;
        var type = parameters.GetRequiredString("type");
        var text = parameters.GetString("text") ?? "";
        if (type == "char")
        {
            if (text.Length > 0)
            {
                keyboard.InsertText(text);
            }

            return;
        }

        var key = parameters.GetString("key") ?? "";
        if (key.Length == 0)
        {
            key = text.Length > 0 ? text : "Unidentified";
        }

        var keyboardKey = new KeyboardKey(key)
        {
            Code = parameters.GetString("code") ?? "",
            KeyCode = (uint)Math.Max(0, parameters.GetInt32("windowsVirtualKeyCode") ?? parameters.GetInt32("nativeVirtualKeyCode") ?? 0),
            Location = (uint)Math.Max(0, parameters.GetInt32("location") ?? 0),
            Text = type == "rawKeyDown" ? "" : text,
        };
        switch (type)
        {
            case "keyDown":
            case "rawKeyDown":
                keyboard.Down(keyboardKey);
                break;

            case "keyUp":
                keyboard.Up(keyboardKey);
                break;

            default:
                throw ProtocolException.InvalidParameters("type: unknown key event type");
        }
    }

    private void DispatchTouchEvent(JsonElement parameters)
    {
        var input = GetInput(parameters);
        var type = parameters.GetRequiredString("type");
        var points = parameters.GetArray("touchPoints").ToList();
        switch (type)
        {
            case "touchStart":
            case "touchMove":
                for (var i = 0; i < points.Count; i++)
                {
                    var pointId = points[i].GetInt32("id") ?? i;
                    var x = points[i].GetDouble("x") ?? 0;
                    var y = points[i].GetDouble("y") ?? 0;
                    if (!_touchPointers.TryGetValue(pointId, out var pointer))
                    {
                        pointer = input.CreatePointer("touch");
                        _touchPointers[pointId] = pointer;
                        pointer.Move(x, y);
                        pointer.Down();
                    }
                    else
                    {
                        pointer.Move(x, y);
                    }
                }

                break;

            case "touchEnd":
            case "touchCancel":
                // The points of a touchEnd event are the points that remain
                var remaining = new HashSet<int>(points.Select((point, index) => point.GetInt32("id") ?? index));
                foreach (var (pointId, pointer) in _touchPointers.ToArray())
                {
                    if (remaining.Contains(pointId))
                        continue;

                    if (type == "touchEnd")
                    {
                        pointer.Up();
                    }
                    else
                    {
                        pointer.Cancel();
                    }

                    _touchPointers.Remove(pointId);
                }

                break;

            default:
                throw ProtocolException.InvalidParameters("type: unknown touch event type");
        }
    }

    /// <summary>
    /// The drag events sent by the client: the events of a drag intercepted by the client (the drag operation of the page
    /// continues with the moves of the mouse), or of a drag from outside the page with the data of the event (the files are
    /// read from the disk of the server).
    /// </summary>
    private void DispatchDragEvent(JsonElement parameters)
    {
        var input = GetInput(parameters);
        var mouse = input.Mouse;
        var x = parameters.GetRequiredDouble("x");
        var y = parameters.GetRequiredDouble("y");
        var type = parameters.GetRequiredString("type");
        if (type is "dragEnter" or "dragOver" or "drop" && mouse.DragOperation is null)
        {
            if (!parameters.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                throw ProtocolException.InvalidParameters("data: the drag data is required when no drag is in progress");

            input.BeginDragAt(mouse, x, y, ReadDragData(data, static (dragData, path) => dragData.AddFromDisk(path)));
            if (type == "drop")
            {
                mouse.Up(0);
            }

            return;
        }

        switch (type)
        {
            case "dragEnter":
            case "dragOver":
                mouse.Move(x, y);
                break;

            case "drop":
                mouse.Move(x, y);
                mouse.Up(0);
                break;

            case "dragCancel":
                input.CancelDrag(mouse);
                break;

            default:
                throw ProtocolException.InvalidParameters("type: unknown drag event type");
        }
    }

    /// <summary>
    /// Converts the drag data of the protocol (<c>Input.DragData</c>): the strings of its items, its files (added by
    /// <paramref name="addFile"/>, from the disk of the server), and the allowed operations.
    /// </summary>
    internal static DragData ReadDragData(JsonElement data, Action<DragData, string> addFile)
    {
        var result = new DragData
        {
            EffectAllowed = GetEffectAllowed(data.GetInt32("dragOperationsMask") ?? 0xFFFF),
        };

        foreach (var item in data.GetArray("items"))
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw ProtocolException.InvalidParameters("items: an item is not an object");

            result.SetString(item.GetRequiredString("mimeType"), item.GetRequiredString("data"));
        }

        foreach (var file in data.GetArray("files"))
        {
            var path = file.ValueKind == JsonValueKind.String ? file.GetString()! : throw ProtocolException.InvalidParameters("files: a path is not a string");
            try
            {
                addFile(result, path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                throw new ProtocolException("Cannot read the file " + path + ": " + ex.Message, ex);
            }
        }

        return result;
    }

    /// <summary>The effectAllowed of the operations of the protocol (<c>dragOperationsMask</c>): Copy = 1, Link = 2, Move = 16.</summary>
    private static string GetEffectAllowed(int mask)
    {
        var copy = (mask & 1) != 0;
        var link = (mask & 2) != 0;
        var move = (mask & 16) != 0;
        return (copy, link, move) switch
        {
            (true, true, true) => "all",
            (true, true, false) => "copyLink",
            (true, false, true) => "copyMove",
            (false, true, true) => "linkMove",
            (true, false, false) => "copy",
            (false, true, false) => "link",
            (false, false, true) => "move",
            _ => "none",
        };
    }
}
