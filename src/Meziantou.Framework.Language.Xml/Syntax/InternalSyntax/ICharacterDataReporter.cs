namespace Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

/// <summary>Receives what is wrong with a run of character data, and says which entities the document declares.</summary>
internal interface ICharacterDataReporter
{
    bool IsEntityDeclared(string name);

    void Report(int start, int length, DiagnosticDescriptor descriptor, params object?[] arguments);
}
