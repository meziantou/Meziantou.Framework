namespace Meziantou.Framework.Language.Shell;

/// <summary>Identifies the grammar family a <see cref="ShellDialect"/> belongs to. The family determines which parser runs.</summary>
public enum ShellDialectFamily
{
    /// <summary>POSIX-style shells: <c>sh</c>, <c>bash</c>, and <c>zsh</c>.</summary>
    Posix,

    /// <summary>Windows PowerShell and PowerShell Core.</summary>
    PowerShell,

    /// <summary>The Windows command interpreter (<c>cmd.exe</c>).</summary>
    Cmd,
}
