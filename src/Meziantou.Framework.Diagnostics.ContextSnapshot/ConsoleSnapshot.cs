using Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of the console state at a specific point in time.</summary>
public sealed class ConsoleSnapshot
{
    /// <summary>Gets a value indicating whether output has been redirected from the standard output stream.</summary>
    public bool IsOutputRedirected { get; } = Console.IsOutputRedirected;
    /// <summary>Gets a value indicating whether error has been redirected from the standard error stream.</summary>
    public bool IsErrorRedirected { get; } = Console.IsErrorRedirected;
    /// <summary>Gets a value indicating whether input has been redirected from the standard input stream.</summary>
    public bool IsInputRedirected { get; } = Console.IsInputRedirected;

    /// <summary>Gets the encoding used for console output.</summary>
    public EncodingSnapshot OutEncoding { get; } = new EncodingSnapshot(Console.OutputEncoding);
    /// <summary>Gets the encoding used for console input.</summary>
    public EncodingSnapshot InputEncoding { get; } = new EncodingSnapshot(Console.InputEncoding);

    /// <summary>Gets the height of the console buffer in rows.</summary>
    public int BufferHeight { get; } = Utils.SafeGet(() => Console.BufferHeight);
    /// <summary>Gets the width of the console buffer in columns.</summary>
    public int BufferWidth { get; } = Utils.SafeGet(() => Console.BufferWidth);
    /// <summary>Gets the maximum number of console window rows.</summary>
    public int LargestWindowHeight { get; } = Utils.SafeGet(() => Console.LargestWindowHeight);
    /// <summary>Gets the maximum number of console window columns.</summary>
    public int LargestWindowWidth { get; } = Utils.SafeGet(() => Console.LargestWindowWidth);
    /// <summary>Gets the height of the console window in rows.</summary>
    public int WindowHeight { get; } = Utils.SafeGet(() => Console.WindowHeight);
    /// <summary>Gets the width of the console window in columns.</summary>
    public int WindowWidth { get; } = Utils.SafeGet(() => Console.WindowWidth);
    /// <summary>Gets the topmost position of the console window relative to the buffer.</summary>
    public int WindowTop { get; } = Utils.SafeGet(() => Console.WindowTop);
    /// <summary>Gets the leftmost position of the console window relative to the buffer.</summary>
    public int WindowLeft { get; } = Utils.SafeGet(() => Console.WindowLeft);

    /// <summary>Gets the foreground color of the console.</summary>
    public ConsoleColor ForegroundColor { get; } = Console.ForegroundColor;
    /// <summary>Gets the background color of the console.</summary>
    public ConsoleColor BackgroundColor { get; } = Console.BackgroundColor;

    /// <summary>Gets the title of the console window.</summary>
    public string? Title { get; } = OperatingSystem.IsWindows() ? Console.Title : null;
}
