namespace Meziantou.Framework.Language.Shell.Internals;

/// <summary>A here-document whose body has been announced by a redirection but not yet read.</summary>
internal sealed record PendingHereDocument(ShellRedirectionSyntax Redirection, string Delimiter);
