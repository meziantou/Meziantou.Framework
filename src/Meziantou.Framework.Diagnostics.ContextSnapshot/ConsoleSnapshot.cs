using Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

public sealed class ConsoleSnapshot
{
    public bool IsOutputRedirected { get; } = Console.IsOutputRedirected;
    public bool IsErrorRedirected { get; } = Console.IsErrorRedirected;
    public bool IsInputRedirected { get; } = Console.IsInputRedirected;

    public EncodingSnapshot OutEncoding { get; } = new EncodingSnapshot(Console.OutputEncoding);
    public EncodingSnapshot InputEncoding { get; } = new EncodingSnapshot(Console.InputEncoding);

    public int BufferHeight { get; } = Utils.SafeGet(() => Console.BufferHeight);
    public int BufferWidth { get; } = Utils.SafeGet(() => Console.BufferWidth);
    public int LargestWindowHeight { get; } = Utils.SafeGet(() => Console.LargestWindowHeight);
    public int LargestWindowWidth { get; } = Utils.SafeGet(() => Console.LargestWindowWidth);
    public int WindowHeight { get; } = Utils.SafeGet(() => Console.WindowHeight);
    public int WindowWidth { get; } = Utils.SafeGet(() => Console.WindowWidth);
    public int WindowTop { get; } = Utils.SafeGet(() => Console.WindowTop);
    public int WindowLeft { get; } = Utils.SafeGet(() => Console.WindowLeft);

    public ConsoleColor ForegroundColor { get; } = Console.ForegroundColor;
    public ConsoleColor BackgroundColor { get; } = Console.BackgroundColor;

    public string? Title { get; } = OperatingSystem.IsWindows() ? Console.Title : null;
}
