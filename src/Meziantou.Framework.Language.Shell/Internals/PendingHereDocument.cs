using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using GreenFactory = Meziantou.Framework.Language.Shell.Syntax.InternalSyntax.SyntaxFactory;
using Red = Meziantou.Framework.Language.Shell;

namespace Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

/// <summary>A here-document whose body has been announced by a redirection but not yet read.</summary>
internal sealed record PendingHereDocument(ShellRedirectionSyntax Redirection, string Delimiter);
