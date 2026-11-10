namespace Meziantou.Framework.Language.Ini.Syntax.InternalSyntax;

/// <summary>An error the lexer found in a token, at its position in the text.</summary>
internal readonly record struct LexerDiagnostic(int Position, int Width, DiagnosticDescriptor Descriptor, object?[] Arguments);
