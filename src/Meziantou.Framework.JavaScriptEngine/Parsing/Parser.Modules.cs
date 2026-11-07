using Meziantou.Framework.JavaScriptEngine.Ast;

namespace Meziantou.Framework.JavaScriptEngine.Parsing;

internal sealed partial class Parser
{
    private HashSet<string>? _exportedNames;
    private List<Identifier>? _localExports;

    private ImportDeclaration ParseImportDeclaration(int start)
    {
        Next();
        var node = new ImportDeclaration();
        if (_token.Kind == TokenKind.StringLiteral)
        {
            node.Source = ParseModuleSpecifier();
        }
        else if (_token.Kind == TokenKind.Identifier && _token.Value == "defer" && !_token.IsEscaped && PeekToken().Kind == TokenKind.Star)
        {
            // import defer * as ns from '...' (https://tc39.es/proposal-defer-import-eval/): only a namespace import can be deferred
            Next();
            var specifierStart = _token.Start;
            Next();
            ExpectContextual("as");
            var local = ParseImportedBinding();
            node.Specifiers.Add(Finish(new ImportNamespaceSpecifier { Local = local }, specifierStart));
            node.Phase = Modules.ModulePhase.Defer;
            ExpectContextual("from");
            node.Source = ParseModuleSpecifier();
        }
        else if (_token.Kind == TokenKind.Identifier && _token.Value == "source" && !_token.IsEscaped && PeekToken().Kind == TokenKind.Identifier)
        {
            // import source x from '...' (https://tc39.es/proposal-source-phase-imports/), unless it is the default import
            // of a binding named "source" (import source from '...'; import source from from '...' is a source import)
            var sourceToken = _token;
            Next();
            if (_token.Value == "from" && !_token.IsEscaped && PeekToken().Kind != TokenKind.Identifier)
            {
                var binding = FinishAt(new Identifier { Name = "source" }, sourceToken.Start, sourceToken.End);
                CheckLValSimple(binding, BindingKind.Lexical);
                node.Specifiers.Add(FinishAt(new ImportDefaultSpecifier { Local = binding }, sourceToken.Start, sourceToken.End));
            }
            else
            {
                var specifierStart = _token.Start;
                var local = ParseImportedBinding();
                node.Specifiers.Add(Finish(new ImportDefaultSpecifier { Local = local }, specifierStart));
                node.Phase = Modules.ModulePhase.Source;
            }

            ExpectContextual("from");
            node.Source = ParseModuleSpecifier();
        }
        else
        {
            var needsNamedImports = true;
            if (_token.Kind == TokenKind.Identifier)
            {
                var specifierStart = _token.Start;
                var local = ParseImportedBinding();
                node.Specifiers.Add(Finish(new ImportDefaultSpecifier { Local = local }, specifierStart));
                needsNamedImports = Eat(TokenKind.Comma);
            }

            if (needsNamedImports)
            {
                if (_token.Kind == TokenKind.Star)
                {
                    var specifierStart = _token.Start;
                    Next();
                    ExpectContextual("as");
                    var local = ParseImportedBinding();
                    node.Specifiers.Add(Finish(new ImportNamespaceSpecifier { Local = local }, specifierStart));
                }
                else if (_token.Kind == TokenKind.LeftBrace)
                {
                    Next();
                    var first = true;
                    while (!Eat(TokenKind.RightBrace))
                    {
                        if (!first)
                        {
                            Expect(TokenKind.Comma);
                            if (Eat(TokenKind.RightBrace))
                                break;
                        }
                        else
                        {
                            first = false;
                        }

                        node.Specifiers.Add(ParseImportSpecifier());
                    }
                }
                else
                {
                    Unexpected();
                }
            }

            ExpectContextual("from");
            if (_token.Kind != TokenKind.StringLiteral)
                Unexpected();

            node.Source = ParseModuleSpecifier();
        }

        ParseImportAttributes(node.Attributes);
        Semicolon();
        return Finish(node, start);
    }

    private ImportSpecifier ParseImportSpecifier()
    {
        var start = _token.Start;
        if (_token.Kind == TokenKind.StringLiteral)
        {
            var imported = ParseModuleExportName();
            ExpectContextual("as");
            var local = ParseImportedBinding();
            return Finish(new ImportSpecifier { Imported = imported, Local = local }, start);
        }

        var importedToken = _token;
        var importedName = ParseIdentifierName();
        if (EatContextual("as"))
        {
            var local = ParseImportedBinding();
            return Finish(new ImportSpecifier { Imported = importedName, Local = local }, start);
        }

        // import { x } from '...': x is also the binding
        if (importedToken.Kind != TokenKind.Identifier)
            Unexpected(importedToken);

        CheckUnreserved(importedName.Name, importedName.Start);
        var binding = FinishAt(new Identifier { Name = importedName.Name }, importedName.Start, importedName.End);
        CheckLValSimple(binding, BindingKind.Lexical);
        return Finish(new ImportSpecifier { Imported = importedName, Local = binding }, start);
    }

    private Identifier ParseImportedBinding()
    {
        var local = ParseBindingIdentifier();
        CheckLValSimple(local, BindingKind.Lexical);
        return local;
    }

    private StringLiteral ParseModuleSpecifier()
    {
        if (_token.Kind != TokenKind.StringLiteral)
            Unexpected();

        var start = _token.Start;
        var value = _token.Value!;
        Next();
        return Finish(new StringLiteral { Value = value }, start);
    }

    /// <summary>Parses a ModuleExportName: an IdentifierName or a string literal (which must be well-formed Unicode).</summary>
    private Node ParseModuleExportName()
    {
        if (_token.Kind == TokenKind.StringLiteral)
        {
            var literal = ParseModuleSpecifier();
            if (!IsWellFormedUnicode(literal.Value))
                Raise(literal.Start, "An export name cannot include a lone surrogate");

            return literal;
        }

        return ParseIdentifierName();
    }

    private static bool IsWellFormedUnicode(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                    return false;

                i++;
            }
            else if (char.IsLowSurrogate(c))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetModuleExportName(Node node) => node switch
    {
        Identifier identifier => identifier.Name,
        StringLiteral literal => literal.Value,
        _ => throw new InvalidOperationException("Unexpected export name"),
    };

    private void ParseImportAttributes(List<ImportAttribute> attributes)
    {
        if (_token.Kind != TokenKind.With)
            return;

        Next();
        Expect(TokenKind.LeftBrace);
        HashSet<string>? keys = null;
        var first = true;
        while (!Eat(TokenKind.RightBrace))
        {
            if (!first)
            {
                Expect(TokenKind.Comma);
                if (Eat(TokenKind.RightBrace))
                    break;
            }
            else
            {
                first = false;
            }

            var start = _token.Start;
            Node key = _token.Kind == TokenKind.StringLiteral ? ParseModuleSpecifier() : ParseIdentifierName();
            var keyName = GetModuleExportName(key);
            keys ??= new HashSet<string>(StringComparer.Ordinal);
            if (!keys.Add(keyName))
                Raise(key.Start, "Import attribute has duplicate key '" + keyName + "'");

            Expect(TokenKind.Colon);
            if (_token.Kind != TokenKind.StringLiteral)
                Unexpected();

            var value = ParseModuleSpecifier();
            attributes.Add(Finish(new ImportAttribute { Key = key, Value = value }, start));
        }
    }

    private void AddExportedName(string name, int position)
    {
        _exportedNames ??= new HashSet<string>(StringComparer.Ordinal);
        if (!_exportedNames.Add(name))
            Raise(position, "Duplicate export of '" + name + "'");
    }

    /// <summary>Parses an export declaration; <paramref name="decorators"/> are the decorators before 'export' (only a class can follow).</summary>
    private Node ParseExportDeclaration(int start, List<Node>? decorators = null)
    {
        Next();
        if (decorators is not null && _token.Kind == TokenKind.At)
            Raise(_token.Start, "Decorators may not appear both before and after 'export'");

        if (decorators is not null && _token.Kind is not TokenKind.Class and not TokenKind.Default)
            Unexpected();

        if (_token.Kind == TokenKind.Star)
        {
            Next();
            var node = new ExportAllDeclaration();
            if (EatContextual("as"))
            {
                node.Exported = ParseModuleExportName();
                AddExportedName(GetModuleExportName(node.Exported), node.Exported.Start);
            }

            ExpectContextual("from");
            node.Source = ParseModuleSpecifier();
            ParseImportAttributes(node.Attributes);
            Semicolon();
            return Finish(node, start);
        }

        if (_token.Kind == TokenKind.Default)
        {
            AddExportedName("default", _token.Start);
            Next();
            Node declaration;
            var declarationStart = _token.Start;
            if (decorators is not null || _token.Kind == TokenKind.At)
            {
                if (decorators is not null && _token.Kind == TokenKind.At)
                    Raise(_token.Start, "Decorators may not appear both before and after 'export'");

                declaration = ParseClassDeclaration(declarationStart, allowAnonymous: true, decorators ?? ParseDecorators());
            }
            else if (_token.Kind == TokenKind.Function)
            {
                declaration = ParseFunctionStatement(declarationStart, isAsync: false, isHanging: false, isAnnexB: false, allowAnonymous: true);
            }
            else if (IsContextual("async") && PeekToken() is { Kind: TokenKind.Function, NewLineBefore: false })
            {
                Next();
                declaration = ParseFunctionStatement(declarationStart, isAsync: true, isHanging: false, isAnnexB: false, allowAnonymous: true);
            }
            else if (_token.Kind == TokenKind.Class)
            {
                declaration = ParseClassDeclaration(declarationStart, allowAnonymous: true);
            }
            else
            {
                declaration = ParseAssignment();
                Semicolon();
            }

            return Finish(new ExportDefaultDeclaration { Declaration = declaration }, start);
        }

        if (decorators is not null || _token.Kind is TokenKind.Var or TokenKind.Const or TokenKind.Function or TokenKind.Class or TokenKind.At || IsContextual("let") || (IsContextual("async") && PeekToken() is { Kind: TokenKind.Function, NewLineBefore: false }))
        {
            var declaration = decorators is not null ? ParseClassDeclaration(_token.Start, allowAnonymous: false, decorators) : ParseStatementListItem();
            switch (declaration)
            {
                case VariableDeclaration variable:
                    foreach (var declarator in variable.Declarations)
                    {
                        AddBoundNamesAsExports(declarator.Id);
                    }

                    break;
                case FunctionDeclaration function:
                    AddExportedName(function.Function.Id!.Name, function.Function.Id.Start);
                    break;
                case ClassDeclaration classDeclaration:
                    AddExportedName(classDeclaration.Class.Id!.Name, classDeclaration.Class.Id.Start);
                    break;
                default:
                    Unexpected(declaration.Start);
                    break;
            }

            return Finish(new ExportNamedDeclaration { Declaration = declaration }, start);
        }

        var named = new ExportNamedDeclaration();
        Expect(TokenKind.LeftBrace);
        var first = true;
        var localTokens = new List<Token>();
        while (!Eat(TokenKind.RightBrace))
        {
            if (!first)
            {
                Expect(TokenKind.Comma);
                if (Eat(TokenKind.RightBrace))
                    break;
            }
            else
            {
                first = false;
            }

            var specifierStart = _token.Start;
            localTokens.Add(_token);
            var local = ParseModuleExportName();
            var exported = EatContextual("as") ? ParseModuleExportName() : local;
            named.Specifiers.Add(Finish(new ExportSpecifier { Local = local, Exported = exported }, specifierStart));
        }

        if (EatContextual("from"))
        {
            named.Source = ParseModuleSpecifier();
            ParseImportAttributes(named.Attributes);
        }
        else
        {
            for (var i = 0; i < named.Specifiers.Count; i++)
            {
                var local = named.Specifiers[i].Local;
                if (local is not Identifier identifier)
                    throw CreateError(_source, local.Start, "A string literal cannot be used as an exported binding without 'from'");

                if (localTokens[i].Kind != TokenKind.Identifier)
                    Unexpected(localTokens[i]);

                CheckUnreserved(identifier.Name, identifier.Start);
                (_localExports ??= []).Add(identifier);
            }
        }

        Semicolon();
        foreach (var specifier in named.Specifiers)
        {
            AddExportedName(GetModuleExportName(specifier.Exported), specifier.Exported.Start);
        }

        return Finish(named, start);
    }

    private void AddBoundNamesAsExports(Node pattern)
    {
        switch (pattern)
        {
            case Identifier identifier:
                AddExportedName(identifier.Name, identifier.Start);
                break;
            case ObjectPattern obj:
                foreach (var property in obj.Properties)
                {
                    AddBoundNamesAsExports(property);
                }

                break;
            case ArrayPattern array:
                foreach (var element in array.Elements)
                {
                    if (element is not null)
                    {
                        AddBoundNamesAsExports(element);
                    }
                }

                break;
            case Property property:
                AddBoundNamesAsExports(property.Value);
                break;
            case AssignmentPattern assignment:
                AddBoundNamesAsExports(assignment.Left);
                break;
            case RestElement rest:
                AddBoundNamesAsExports(rest.Argument);
                break;
        }
    }

    /// <summary>Checks that the local names of <c>export { x }</c> are declared in the module.</summary>
    private void CheckLocalExports()
    {
        if (_localExports is null)
            return;

        var scope = TopScope;
        foreach (var local in _localExports)
        {
            if (!Scope.Contains(scope.Var, local.Name) && !Scope.Contains(scope.Lexical, local.Name) && !Scope.Contains(scope.Functions, local.Name))
                Raise(local.Start, "Export '" + local.Name + "' is not defined in module");
        }
    }
}
