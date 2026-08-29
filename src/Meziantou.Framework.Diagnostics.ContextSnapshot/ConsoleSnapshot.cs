using Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of the console state including redirection status, encoding, buffer size, and colors.</summary>
public sealed class ConsoleSnapshot
{
    public bool IsOutputRedirected { get; } = Console.IsOutputRedirected;
    public bool IsErrorRedirected { get; } = Console.IsErrorRedirected;
    public bool IsInputRedirected { get; } = Console.IsInputRedirected;

    public EncodingSnapshot? OutEncoding { get; } = Utils.SafeGet(() => new EncodingSnapshot(Console.OutputEncoding));
    public EncodingSnapshot? InputEncoding { get; } = Utils.SafeGet(() => new EncodingSnapshot(Console.InputEncoding));

    public int BufferHeight { get; } = Utils.SafeGet(() => Console.BufferHeight);
    public int BufferWidth { get; } = Utils.SafeGet(() => Console.BufferWidth);
    public int LargestWindowHeight { get; } = Utils.SafeGet(() => Console.LargestWindowHeight);
    public int LargestWindowWidth { get; } = Utils.SafeGet(() => Console.LargestWindowWidth);
    public int WindowHeight { get; } = Utils.SafeGet(() => Console.WindowHeight);
    public int WindowWidth { get; } = Utils.SafeGet(() => Console.WindowWidth);
    public int WindowTop { get; } = Utils.SafeGet(() => Console.WindowTop);
    public int WindowLeft { get; } = Utils.SafeGet(() => Console.WindowLeft);

    public ConsoleColor? ForegroundColor { get; } = GetColor(() => Console.ForegroundColor);
    public ConsoleColor? BackgroundColor { get; } = GetColor(() => Console.BackgroundColor);

    public string? Title { get; } = GetTitle();

    private static string? GetTitle()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        // Console.Title throws IOException when the process has no attached console.
        try
        {
            return Console.Title;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ConsoleColor? GetColor(Func<ConsoleColor> getColor)
    {
        // Console returns (ConsoleColor)(-1) when the color is unknown, which is not a defined enum value
        // and serializes as "-1".
        var color = Utils.SafeGet(getColor);
        return Enum.IsDefined(color) ? color : null;
    }
}
