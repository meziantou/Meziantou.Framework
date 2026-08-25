namespace Meziantou.Framework.Language.Shell;

/// <summary>Describes the optional syntax constructs a <see cref="ShellDialect"/> supports.</summary>
[Flags]
public enum ShellDialectFeatures
{
    None = 0,

    /// <summary>The <c>[[ ... ]]</c> extended test construct.</summary>
    ExtendedTest = 1 << 0,

    /// <summary>The <c>(( ... ))</c> arithmetic command. POSIX defines no such command, so <c>sh</c> lacks it.</summary>
    ArithmeticCommand = 1 << 1,

    /// <summary>The <c>&lt;&lt;&lt;</c> here-string redirection operator.</summary>
    HereString = 1 << 2,

    /// <summary>ANSI-C quoting (<c>$'...'</c>) and locale-translated strings (<c>$"..."</c>).</summary>
    DollarQuoting = 1 << 3,

    /// <summary>Indexed and associative array assignments such as <c>a=(1 2 3)</c>.</summary>
    Arrays = 1 << 4,

    /// <summary>The <c>function</c> keyword form of a function definition.</summary>
    FunctionKeyword = 1 << 5,

    /// <summary>Process substitution: <c>&lt;(...)</c> and <c>&gt;(...)</c>.</summary>
    ProcessSubstitution = 1 << 6,

    /// <summary>The <c>coproc</c> keyword.</summary>
    Coproc = 1 << 7,

    /// <summary>The <c>select</c> loop.</summary>
    SelectLoop = 1 << 8,

    /// <summary>The zsh <c>foreach ... end</c> loop and anonymous functions.</summary>
    ZshExtensions = 1 << 9,

    /// <summary>The <c>&amp;&amp;</c> and <c>||</c> pipeline chain operators (PowerShell 7 and later).</summary>
    PipelineChainOperators = 1 << 10,

    /// <summary>The ternary <c>? :</c> operator (PowerShell 7 and later).</summary>
    TernaryOperator = 1 << 11,

    /// <summary>The <c>??</c> and <c>??=</c> null-coalescing operators (PowerShell 7 and later).</summary>
    NullCoalescing = 1 << 12,

    /// <summary>The <c>clean</c> block in an advanced function (PowerShell 7.3 and later).</summary>
    CleanBlock = 1 << 13,

    /// <summary>Delayed variable expansion using <c>!VAR!</c>.</summary>
    DelayedExpansion = 1 << 14,

    /// <summary>The <c>$(( ... ))</c> arithmetic expansion. POSIX defines it, so every dialect in the family has it.</summary>
    ArithmeticExpansion = 1 << 15,
}
