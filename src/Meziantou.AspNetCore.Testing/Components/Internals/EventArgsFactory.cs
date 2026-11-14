using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using ErrorEventArgs = Microsoft.AspNetCore.Components.Web.ErrorEventArgs;

namespace Meziantou.AspNetCore.Testing.Components.Internals;

/// <summary>
/// Creates the <see cref="EventArgs"/> of an event handler from a DOM event, as <c>EventTypes.ts</c> of Blazor does in the
/// browser. The type is the one the handler expects (<c>Renderer.GetEventArgsType</c>), so the custom event types work.
/// </summary>
internal static class EventArgsFactory
{
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "The event args types are referenced by the event handlers")]
    public static EventArgs Create(Event domEvent, Type expectedType, IReadOnlyDictionary<string, Func<Event, EventArgs>> customFactories)
    {
        if (customFactories.TryGetValue(domEvent.Type, out var customFactory))
            return customFactory(domEvent);

        if (expectedType == typeof(EventArgs))
            return EventArgs.Empty;

        if (expectedType == typeof(ChangeEventArgs))
            return CreateChangeEventArgs(domEvent);

        if (expectedType == typeof(PointerEventArgs))
            return FillMouseEventArgs(CreatePointerEventArgs(domEvent), domEvent);

        if (expectedType == typeof(WheelEventArgs))
        {
            var args = new WheelEventArgs();
            if (domEvent is WheelEvent wheel)
            {
                args.DeltaX = wheel.DeltaX;
                args.DeltaY = wheel.DeltaY;
                args.DeltaZ = wheel.DeltaZ;
                args.DeltaMode = (long)wheel.DeltaMode;
            }

            return FillMouseEventArgs(args, domEvent);
        }

        if (expectedType == typeof(DragEventArgs))
        {
            var args = new DragEventArgs();
            if (domEvent is DragEvent { DataTransfer: { } dataTransfer })
            {
                args.DataTransfer = new Microsoft.AspNetCore.Components.Web.DataTransfer
                {
                    DropEffect = dataTransfer.DropEffect,
                    EffectAllowed = dataTransfer.EffectAllowed,
                    Types = [.. dataTransfer.Types],
                    Files = [],
                    Items = [],
                };
            }

            return FillMouseEventArgs(args, domEvent);
        }

        if (expectedType == typeof(MouseEventArgs))
            return FillMouseEventArgs(new MouseEventArgs(), domEvent);

        if (expectedType == typeof(KeyboardEventArgs))
        {
            var args = new KeyboardEventArgs { Type = domEvent.Type };
            if (domEvent is KeyboardEvent keyboard)
            {
                args.Key = keyboard.Key;
                args.Code = keyboard.Code;
                args.Location = keyboard.Location;
                args.Repeat = keyboard.Repeat;
                args.CtrlKey = keyboard.CtrlKey;
                args.ShiftKey = keyboard.ShiftKey;
                args.AltKey = keyboard.AltKey;
                args.MetaKey = keyboard.MetaKey;
                args.IsComposing = keyboard.IsComposing;
            }

            return args;
        }

        if (expectedType == typeof(FocusEventArgs))
            return new FocusEventArgs { Type = domEvent.Type };

        if (expectedType == typeof(TouchEventArgs))
        {
            var args = new TouchEventArgs { Type = domEvent.Type };
            if (domEvent is TouchEvent touch)
            {
                args.Detail = touch.Detail;
                args.Touches = ToTouchPoints(touch.Touches);
                args.TargetTouches = ToTouchPoints(touch.TargetTouches);
                args.ChangedTouches = ToTouchPoints(touch.ChangedTouches);
                args.CtrlKey = touch.CtrlKey;
                args.ShiftKey = touch.ShiftKey;
                args.AltKey = touch.AltKey;
                args.MetaKey = touch.MetaKey;
            }

            return args;
        }

        if (expectedType == typeof(ClipboardEventArgs))
            return new ClipboardEventArgs { Type = domEvent.Type };

        if (expectedType == typeof(ProgressEventArgs))
        {
            var args = new ProgressEventArgs { Type = domEvent.Type };
            if (domEvent is ProgressEvent progress)
            {
                args.LengthComputable = progress.LengthComputable;
                args.Loaded = (long)progress.Loaded;
                args.Total = (long)progress.Total;
            }

            return args;
        }

        if (expectedType == typeof(ErrorEventArgs))
        {
            var args = new ErrorEventArgs { Type = domEvent.Type };
            if (domEvent is ErrorEvent error)
            {
                args.Message = error.Message;
                args.Filename = error.Filename;
                args.Lineno = (int)error.Lineno;
                args.Colno = (int)error.Colno;
            }

            return args;
        }

        // A custom event type: Blazor deserializes the JSON object created by the browser; without a factory, the
        // handler gets an instance with its default values
        return (EventArgs?)Activator.CreateInstance(expectedType) ?? EventArgs.Empty;
    }

    private static PointerEventArgs CreatePointerEventArgs(Event domEvent)
    {
        var args = new PointerEventArgs();
        if (domEvent is PointerEvent pointer)
        {
            args.PointerId = pointer.PointerId;
            args.Width = (float)pointer.Width;
            args.Height = (float)pointer.Height;
            args.Pressure = pointer.Pressure;
            args.TiltX = pointer.TiltX;
            args.TiltY = pointer.TiltY;
            args.PointerType = pointer.PointerType;
            args.IsPrimary = pointer.IsPrimary;
        }

        return args;
    }

    private static T FillMouseEventArgs<T>(T args, Event domEvent)
        where T : MouseEventArgs
    {
        args.Type = domEvent.Type;
        if (domEvent is MouseEvent mouse)
        {
            args.Detail = mouse.Detail;
            args.ScreenX = mouse.ScreenX;
            args.ScreenY = mouse.ScreenY;
            args.ClientX = mouse.ClientX;
            args.ClientY = mouse.ClientY;
            args.OffsetX = mouse.OffsetX;
            args.OffsetY = mouse.OffsetY;
            args.PageX = mouse.PageX;
            args.PageY = mouse.PageY;
            args.MovementX = mouse.MovementX;
            args.MovementY = mouse.MovementY;
            args.Button = mouse.Button;
            args.Buttons = mouse.Buttons;
            args.CtrlKey = mouse.CtrlKey;
            args.ShiftKey = mouse.ShiftKey;
            args.AltKey = mouse.AltKey;
            args.MetaKey = mouse.MetaKey;
        }

        return args;
    }

    private static ChangeEventArgs CreateChangeEventArgs(Event domEvent)
    {
        object? value = domEvent.Target switch
        {
            HtmlInputElement { Type: "checkbox" } checkbox => checkbox.Checked,
            HtmlInputElement input => input.Value,
            HtmlSelectElement { Multiple: true } select => select.QuerySelectorAll<HtmlOptionElement>("option").Where(option => option.Selected).Select(option => option.Value).ToArray(),
            HtmlSelectElement select => select.Value,
            HtmlTextAreaElement textArea => textArea.Value,
            Element element when element.HasAttribute("contenteditable") => element.TextContent,
            _ => null,
        };

        return new ChangeEventArgs { Value = value };
    }

    private static TouchPoint[] ToTouchPoints(TouchList touches)
    {
        var result = new TouchPoint[touches.Count];
        for (var i = 0; i < touches.Count; i++)
        {
            var touch = touches[i];
            result[i] = new TouchPoint
            {
                Identifier = touch.Identifier,
                ScreenX = touch.ScreenX,
                ScreenY = touch.ScreenY,
                ClientX = touch.ClientX,
                ClientY = touch.ClientY,
                PageX = touch.PageX,
                PageY = touch.PageY,
            };
        }

        return result;
    }
}
