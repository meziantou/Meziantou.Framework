using System.Diagnostics;

namespace Meziantou.Framework.Language.Shell;

/// <summary>Identifies a shell dialect understood by <see cref="ShellSyntaxTree"/>.</summary>
/// <remarks>The set of dialects is closed. Use the static properties to obtain an instance.</remarks>
[DebuggerDisplay("{Name}")]
public sealed class ShellDialect
{
    /// <summary>What POSIX itself defines, so every dialect in the family has it.</summary>
    private const ShellDialectFeatures PosixFeatures = ShellDialectFeatures.ArithmeticExpansion;

    private const ShellDialectFeatures BashFeatures =
        PosixFeatures |
        ShellDialectFeatures.ExtendedTest |
        ShellDialectFeatures.ArithmeticCommand |
        ShellDialectFeatures.HereString |
        ShellDialectFeatures.DollarQuoting |
        ShellDialectFeatures.Arrays |
        ShellDialectFeatures.FunctionKeyword |
        ShellDialectFeatures.ProcessSubstitution |
        ShellDialectFeatures.Coproc |
        ShellDialectFeatures.SelectLoop |
        ShellDialectFeatures.ArithmeticExponentiation;

    private ShellDialect(string name, ShellDialectFamily family, ShellDialectFeatures features)
    {
        Name = name;
        Family = family;
        Features = features;
    }

    /// <summary>The POSIX shell (<c>sh</c>). The strict baseline of the POSIX family.</summary>
    public static ShellDialect Sh { get; } = new("sh", ShellDialectFamily.Posix, PosixFeatures);

    /// <summary>The GNU Bourne-Again shell (<c>bash</c>).</summary>
    public static ShellDialect Bash { get; } = new("bash", ShellDialectFamily.Posix, BashFeatures);

    /// <summary>The Z shell (<c>zsh</c>).</summary>
    public static ShellDialect Zsh { get; } = new("zsh", ShellDialectFamily.Posix, BashFeatures | ShellDialectFeatures.ZshExtensions);

    /// <summary>Windows PowerShell 5.1.</summary>
    public static ShellDialect PowerShell { get; } = new("powershell", ShellDialectFamily.PowerShell, ShellDialectFeatures.None);

    /// <summary>PowerShell Core 7 and later (<c>pwsh</c>).</summary>
    public static ShellDialect PowerShellCore { get; } = new(
        "pwsh",
        ShellDialectFamily.PowerShell,
        ShellDialectFeatures.PipelineChainOperators | ShellDialectFeatures.TernaryOperator | ShellDialectFeatures.NullCoalescing | ShellDialectFeatures.CleanBlock);

    /// <summary>The Windows command interpreter (<c>cmd.exe</c>).</summary>
    public static ShellDialect Cmd { get; } = new("cmd", ShellDialectFamily.Cmd, ShellDialectFeatures.DelayedExpansion);

    /// <summary>The canonical lowercase name of the dialect, such as <c>bash</c> or <c>pwsh</c>.</summary>
    public string Name { get; }

    /// <summary>The grammar family the dialect belongs to.</summary>
    public ShellDialectFamily Family { get; }

    /// <summary>The optional syntax constructs the dialect supports.</summary>
    public ShellDialectFeatures Features { get; }

    /// <summary>Returns <see langword="true"/> when the dialect supports every feature in <paramref name="feature"/>.</summary>
    public bool HasFeature(ShellDialectFeatures feature) => (Features & feature) == feature;

    /// <summary>Resolves a dialect from its name. Recognizes the canonical names and common aliases such as <c>powershell-core</c>.</summary>
    public static bool TryParse(string? name, [NotNullWhen(true)] out ShellDialect? dialect)
    {
        dialect = name?.Trim().ToLowerInvariant() switch
        {
            "sh" or "posix" or "dash" => Sh,
            "bash" => Bash,
            "zsh" => Zsh,
            "powershell" or "windowspowershell" or "powershell5" => PowerShell,
            "pwsh" or "powershellcore" or "powershell-core" or "powershell7" => PowerShellCore,
            "cmd" or "batch" or "cmd.exe" => Cmd,
            _ => null,
        };

        return dialect is not null;
    }

    public override string ToString() => Name;
}
