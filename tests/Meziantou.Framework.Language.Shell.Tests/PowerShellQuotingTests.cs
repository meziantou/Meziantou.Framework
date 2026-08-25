namespace Meziantou.Framework.Language.Shell.Tests;

/// <summary>Quoting, escaping, and literal semantics for the PowerShell family.</summary>
public sealed class PowerShellQuotingTests
{
    private static ShellWordSyntax FirstArgument(string argumentText, ShellDialect? dialect = null)
    {
        var statement = ShellSyntaxTree.ParseCommand("Write-Output " + argumentText, dialect ?? ShellDialect.PowerShellCore);

        return Assert.IsType<ShellCommandSyntax>(statement).Arguments[0];
    }

    [Theory]
    // Verbatim strings: nothing is special except a doubled quote.
    [InlineData("'plain'", "plain")]
    [InlineData("'it''s'", "it's")]
    [InlineData("'$x'", "$x")]
    [InlineData("'a`nb'", "a`nb")]
    [InlineData("'a\"b'", "a\"b")]
    [InlineData("''", "")]
    [InlineData("''''", "'")]
    // Expandable strings: the backtick escapes, and a doubled quote is one quote.
    [InlineData("\"plain\"", "plain")]
    [InlineData("\"a\"\"b\"", "a\"b")]
    [InlineData("\"a`\"b\"", "a\"b")]
    [InlineData("\"a`$b\"", "a$b")]
    [InlineData("\"a``b\"", "a`b")]
    [InlineData("\"a'b\"", "a'b")]
    [InlineData("\"\"", "")]
    public void Value_MatchesPowerShellSemantics(string argumentText, string expected)
    {
        Assert.Equal(expected, FirstArgument(argumentText).Value);
    }

    [Theory]
    [InlineData("\"a`nb\"", "a\nb")]
    [InlineData("\"a`tb\"", "a\tb")]
    [InlineData("\"a`rb\"", "a\rb")]
    [InlineData("\"a`0b\"", "a\0b")]
    public void ExpandableString_ResolvesBacktickEscapes(string argumentText, string expected)
    {
        Assert.Equal(expected, FirstArgument(argumentText).Value);
    }

    [Fact]
    public void ExpandableString_WithAnExpansion_HasNoStaticValue()
    {
        Assert.Null(FirstArgument("\"$x\"").Value);
        Assert.Null(FirstArgument("\"a$($x.Y)b\"").Value);
        Assert.Null(FirstArgument("\"${weird name}\"").Value);
    }

    [Fact]
    public void ExpandableString_KeepsExpansionsAsChildNodes()
    {
        var word = FirstArgument("\"user $name ran $($cmd.Line)\"");
        var embedded = Assert.IsType<ShellEmbeddedExpressionSyntax>(Assert.Single(word.Parts));
        var text = Assert.IsType<PowerShellExpandableStringSyntax>(embedded.Expression);

        Assert.Contains(text.Parts, part => part is PowerShellVariableExpressionSyntax);
        Assert.Contains(text.Parts, part => part is PowerShellSubExpressionSyntax);
    }

    [Fact]
    public void QuotedWhitespace_DoesNotSplitTheWord()
    {
        var command = Assert.IsType<ShellCommandSyntax>(
            ShellSyntaxTree.ParseCommand("Write-Output \"a b\" 'c d'", ShellDialect.PowerShellCore));

        Assert.Equal(["a b", "c d"], command.Arguments.Select(argument => argument.Value));
    }

    [Fact]
    public void UnterminatedQuotes_ReportShell0003()
    {
        foreach (var text in new[] { "Write-Output 'x", "Write-Output \"x" })
        {
            var tree = ShellSyntaxTree.ParseText(text, ShellDialect.PowerShellCore);

            Assert.Contains(tree.Diagnostics, diagnostic => diagnostic.Id == "SHELL0003");
            Assert.Equal(text, tree.Root.ToFullString());
        }
    }

    [Theory]
    [InlineData("@\"\nbody $x\n\"@", ShellSyntaxKind.PowerShellHereString)]
    [InlineData("@'\nbody $x\n'@", ShellSyntaxKind.PowerShellStringLiteral)]
    public void HereStrings_KeepTheirBodyAndKind(string text, ShellSyntaxKind expectedKind)
    {
        var tree = ShellSyntaxTree.ParseText("$a = " + text, ShellDialect.PowerShellCore);
        var hereString = Assert.Single(tree.Root.DescendantNodes().OfType<PowerShellExpandableStringSyntax>());

        Assert.Equal(expectedKind, hereString.Kind);
        Assert.Equal(text, tree.Text[hereString.Span.Start..hereString.Span.End]);
    }

    [Fact]
    public void HereString_TerminatorOnlyCountsAtLineStart()
    {
        const string Text = "$a = @\"\nnot \"@ inline\n\"@\n";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.PowerShellCore);
        var hereString = Assert.Single(tree.Root.DescendantNodes().OfType<PowerShellExpandableStringSyntax>());

        var body = Assert.IsType<ShellLiteralWordPartSyntax>(Assert.Single(hereString.Parts));

        // The raw text keeps the delimiter line breaks; the value does not, matching PowerShell.
        Assert.Equal("\nnot \"@ inline\n", body.TextToken.Text);
        Assert.Equal("not \"@ inline", body.Value);
        Assert.Equal(Text, tree.Root.ToFullString());
    }

    [Fact]
    public void UnterminatedHereString_ReportsShell0022()
    {
        var tree = ShellSyntaxTree.ParseText("$a = @\"\nbody\n", ShellDialect.PowerShellCore);

        Assert.Contains(tree.Diagnostics, diagnostic => diagnostic.Id == "SHELL0022");
    }

    [Theory]
    [InlineData("$x", "x")]
    [InlineData("$env:PATH", "env:PATH")]
    [InlineData("$script:count", "script:count")]
    [InlineData("$_", "_")]
    [InlineData("${weird name}", "weird name")]
    [InlineData("$global:x", "global:x")]
    public void VariableNames_AreResolved(string text, string expectedName)
    {
        var tree = ShellSyntaxTree.ParseText(text, ShellDialect.PowerShellCore);
        var variable = Assert.Single(tree.Root.DescendantNodes().OfType<PowerShellVariableExpressionSyntax>());

        Assert.Equal(expectedName, variable.Name);
        Assert.Equal(text, tree.Root.ToFullString());
    }

    [Theory]
    [InlineData("0xFF")]
    [InlineData("1.5")]
    [InlineData("1e3")]
    [InlineData("10kb")]
    [InlineData("100L")]
    [InlineData("0b1010")]
    public void NumberLiterals_AreOneToken(string text)
    {
        var tree = ShellSyntaxTree.ParseText("$a = " + text, ShellDialect.PowerShellCore);
        var literal = Assert.Single(tree.Root.DescendantNodes().OfType<PowerShellLiteralExpressionSyntax>(), node => node.Kind == ShellSyntaxKind.PowerShellNumberLiteral);

        Assert.Equal(text, literal.Token.Text);
    }

    [Fact]
    public void RangeOperator_IsNotConfusedWithADecimalPoint()
    {
        var tree = ShellSyntaxTree.ParseText("$a = 1..10", ShellDialect.PowerShellCore);
        var range = Assert.Single(tree.Root.DescendantNodes().OfType<PowerShellBinaryExpressionSyntax>());

        Assert.Equal(ShellSyntaxKind.PowerShellRangeExpression, range.Kind);
        Assert.Equal("..", range.OperatorToken.Text);
    }

    [Fact]
    public void EmptyCollectionLiterals_Parse()
    {
        var array = ShellSyntaxTree.ParseText("$a = @()", ShellDialect.PowerShellCore);
        Assert.Single(array.Root.DescendantNodes().OfType<PowerShellSubExpressionSyntax>());
        Assert.Empty(array.Diagnostics);

        var hash = ShellSyntaxTree.ParseText("$a = @{}", ShellDialect.PowerShellCore);
        Assert.Empty(Assert.Single(hash.Root.DescendantNodes().OfType<PowerShellHashLiteralSyntax>()).Entries);
        Assert.Empty(hash.Diagnostics);
    }

    [Fact]
    public void NestedHashLiteral_Parses()
    {
        var tree = ShellSyntaxTree.ParseText("$a = @{ outer = @{ inner = 1 } }", ShellDialect.PowerShellCore);

        Assert.HasCount(2, tree.Root.DescendantNodes().OfType<PowerShellHashLiteralSyntax>());
        Assert.Empty(tree.Diagnostics);
    }

    [Fact]
    public void MemberAccessAndIndexing_Chain()
    {
        var tree = ShellSyntaxTree.ParseText("$a = $x.Items[0].Name.ToUpper()", ShellDialect.PowerShellCore);

        Assert.Single(tree.Root.DescendantNodes().OfType<PowerShellInvocationExpressionSyntax>());
        Assert.Single(tree.Root.DescendantNodes().OfType<PowerShellIndexExpressionSyntax>());
        Assert.HasCount(3, tree.Root.DescendantNodes().OfType<PowerShellMemberAccessExpressionSyntax>());
    }

    [Fact]
    public void GenericTypeLiteral_KeepsItsBrackets()
    {
        var tree = ShellSyntaxTree.ParseText("$a = [System.Collections.Generic.List[string]]::new()", ShellDialect.PowerShellCore);
        var type = Assert.Single(tree.Root.DescendantNodes().OfType<PowerShellTypeLiteralSyntax>());

        Assert.Equal("System.Collections.Generic.List[string]", type.Name);
        Assert.Empty(tree.Diagnostics);
    }

    [Fact]
    public void CallOperator_StartsACommand()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("& $cmd arg", ShellDialect.PowerShellCore));

        Assert.Equal("&", command.NameValue);
        Assert.HasCount(2, command.Arguments);
    }

    [Fact]
    public void DotSource_StartsACommand()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand(". ./script.ps1", ShellDialect.PowerShellCore));

        Assert.Equal(".", command.NameValue);
    }

    [Fact]
    public void FormatOperator_BindsTighterThanTheSurroundingStatement()
    {
        var tree = ShellSyntaxTree.ParseText("$a = '{0}' -f $x", ShellDialect.PowerShellCore);

        Assert.Single(tree.Root.Statements.Statements);
        Assert.Empty(tree.Diagnostics);
        var assignment = Assert.IsType<PowerShellAssignmentExpressionSyntax>(
            Assert.IsType<PowerShellExpressionStatementSyntax>(tree.Root.Statements.Statements[0]).Expression);
        Assert.Equal("-f", Assert.IsType<PowerShellBinaryExpressionSyntax>(assignment.Value).OperatorToken.Text);
    }

    [Fact]
    public void MinusIsStillArithmeticWhenItIsNotAnOperatorName()
    {
        var tree = ShellSyntaxTree.ParseText("$a = 5 - 3", ShellDialect.PowerShellCore);
        var assignment = Assert.IsType<PowerShellAssignmentExpressionSyntax>(
            Assert.IsType<PowerShellExpressionStatementSyntax>(tree.Root.Statements.Statements[0]).Expression);

        Assert.Equal("-", Assert.IsType<PowerShellBinaryExpressionSyntax>(assignment.Value).OperatorToken.Text);
    }

    [Fact]
    public void UnaryNot_IsNotReadAsArithmeticMinus()
    {
        var tree = ShellSyntaxTree.ParseText("$a = -not $b", ShellDialect.PowerShellCore);
        var assignment = Assert.IsType<PowerShellAssignmentExpressionSyntax>(
            Assert.IsType<PowerShellExpressionStatementSyntax>(tree.Root.Statements.Statements[0]).Expression);

        Assert.Equal("-not", Assert.IsType<PowerShellUnaryExpressionSyntax>(assignment.Value).PrefixOperatorToken?.Text);
    }

    [Fact]
    public void SwitchWithoutParentheses_ParsesItsCondition()
    {
        var statement = Assert.IsType<PowerShellSwitchStatementSyntax>(
            ShellSyntaxTree.ParseCommand("switch -File data.txt { 'a' { 'x' } }", ShellDialect.PowerShellCore));

        Assert.Null(statement.OpenParenToken);
        Assert.Single(statement.ParameterTokens);
        Assert.Single(statement.Clauses);
    }

    [Fact]
    public void NamedAttributeArguments_Parse()
    {
        var statement = Assert.IsType<PowerShellParamBlockSyntax>(
            ShellSyntaxTree.ParseCommand("param([Parameter(Mandatory=$true, Position=0)][string]$N)", ShellDialect.PowerShellCore));

        var attribute = Assert.Single(statement.Parameters).Attributes[0];
        Assert.HasCount(2, attribute.Arguments);
        Assert.All(attribute.Arguments, argument => Assert.IsType<PowerShellAssignmentExpressionSyntax>(argument));
    }

    [Fact]
    public void MultiLineParamBlockInsideAFunction_Parses()
    {
        const string Text = """
            function f {
              param(
                [Parameter(Mandatory=$true)]
                [string]$N
              )
            }
            """;
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.PowerShellCore);

        Assert.Empty(tree.Diagnostics);
        Assert.Equal(Text, tree.Root.ToFullString());
        Assert.Single(tree.Root.DescendantNodes().OfType<PowerShellParamBlockSyntax>());
    }

    // The expectations below were produced by running the same inputs through pwsh 7 and comparing byte for byte.

    [Theory]
    [InlineData("\"a`u{41}b\"", "aAb")]
    [InlineData("\"caf`u{e9}\"", "caf\u00e9")]
    [InlineData("\"`u{1F600}\"", "\U0001F600")]
    [InlineData("\"a`u{7}b\"", "a\u0007b")]
    public void UnicodeEscapes_ResolveToCodePoints(string argumentText, string expected)
    {
        Assert.Equal(expected, FirstArgument(argumentText).Value);
    }

    [Theory]
    [InlineData("\"a`u{}b\"")]
    [InlineData("\"a`u{zz}b\"")]
    [InlineData("\"a`u{D800}b\"")]
    [InlineData("\"a`u{110000}b\"")]
    public void MalformedUnicodeEscape_IsKeptVerbatim(string argumentText)
    {
        var tree = ShellSyntaxTree.ParseText("Write-Output " + argumentText, ShellDialect.PowerShellCore);

        Assert.Equal("Write-Output " + argumentText, tree.Root.ToFullString());
    }

    [Fact]
    public void HereStringInArgumentPosition_IsNotASplattedVariable()
    {
        var word = FirstArgument("@\"\nbody text\n\"@");

        Assert.Equal("body text", word.Value);
        Assert.Empty(word.DescendantNodes().OfType<PowerShellVariableExpressionSyntax>());
    }

    [Fact]
    public void AtQuoteNotAtEndOfLine_IsASplattedVariable()
    {
        // `@"` only opens a here-string when it ends its line.
        var tree = ShellSyntaxTree.ParseText("Get-Thing @args", ShellDialect.PowerShellCore);

        Assert.True(Assert.Single(tree.Root.DescendantNodes().OfType<PowerShellVariableExpressionSyntax>()).IsSplatted);
    }

    [Theory]
    [InlineData("; break")]
    [InlineData(";; break")]
    [InlineData("\n; Get-Date")]
    public void EmptyStatementsAreLegal(string text)
    {
        var tree = ShellSyntaxTree.ParseText(text, ShellDialect.PowerShellCore);

        Assert.Empty(tree.Diagnostics);
        Assert.Equal(text, tree.Root.ToFullString());
        Assert.Contains(tree.Root.Statements.Statements, statement => statement is ShellEmptyStatementSyntax);
    }

    [Theory]
    [InlineData("param 1")]
    [InlineData("param")]
    [InlineData("clean -eq 2")]
    [InlineData("end -Path x")]
    [InlineData("data 1")]
    public void KeywordsWithoutTheirSyntax_AreOrdinaryCommands(string text)
    {
        var tree = ShellSyntaxTree.ParseText(text, ShellDialect.PowerShellCore);

        Assert.Empty(tree.Diagnostics);
        Assert.IsType<ShellCommandSyntax>(Assert.Single(tree.Root.Statements.Statements));
    }

    [Theory]
    [InlineData("param($a)")]
    [InlineData("end { 'x' }")]
    [InlineData("class A {}")]
    [InlineData("if ($a) {}")]
    public void KeywordsWithTheirSyntax_StillIntroduceTheirStatement(string text)
    {
        var tree = ShellSyntaxTree.ParseText(text, ShellDialect.PowerShellCore);

        Assert.Empty(tree.Diagnostics);
        Assert.IsNotType<ShellCommandSyntax>(Assert.Single(tree.Root.Statements.Statements));
    }

    [Theory]
    [InlineData(">")]
    [InlineData(">>")]
    [InlineData("in>")]
    [InlineData("trapexit>>")]
    public void RedirectionNeedsPrecedingWhitespaceAndACommand(string text)
    {
        var tree = ShellSyntaxTree.ParseText(text, ShellDialect.PowerShellCore);

        Assert.Empty(tree.Diagnostics);
        Assert.Empty(tree.Root.DescendantNodes().OfType<ShellRedirectionSyntax>());
    }

    [Fact]
    public void RedirectionAfterACommandStillParses()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("Get-Item > out.txt", ShellDialect.PowerShellCore));

        Assert.Equal("out.txt", Assert.Single(command.Redirections).Target?.Value);
    }

    [Theory]
    [InlineData(":lbl ;")]
    [InlineData(":lblparam")]
    [InlineData(".Prop;:lbl")]
    public void LabelWithoutALoop_IsAnOrdinaryWord(string text)
    {
        var tree = ShellSyntaxTree.ParseText(text, ShellDialect.PowerShellCore);

        Assert.Empty(tree.Diagnostics);
        Assert.Empty(tree.Root.DescendantNodes().OfType<PowerShellLabeledStatementSyntax>());
    }

    [Fact]
    public void LabelOnALoop_IsStillALabel()
    {
        var statement = Assert.IsType<PowerShellLabeledStatementSyntax>(
            ShellSyntaxTree.ParseCommand(":outer while ($true) { break }", ShellDialect.PowerShellCore));

        Assert.Equal("outer", statement.Label);
    }

    [Fact]
    public void NumberMayBeIndexed()
    {
        var tree = ShellSyntaxTree.ParseText("1[int]", ShellDialect.PowerShellCore);

        Assert.Empty(tree.Diagnostics);
        Assert.Single(tree.Root.DescendantNodes().OfType<PowerShellIndexExpressionSyntax>());
    }

    // The expectations below were produced by passing the same argument text to a pwsh 7 function and comparing the
    // UTF-8 bytes of `$args[0]`, so they record what PowerShell actually hands to a command.

    [Theory]
    // Backtick escapes recognized by name.
    [InlineData("\"a`ab\"", "a\ab")]
    [InlineData("\"a`bb\"", "a\bb")]
    [InlineData("\"a`fb\"", "a\fb")]
    [InlineData("\"a`vb\"", "a\vb")]
    [InlineData("\"a`eb\"", "a\u001bb")]
    // An unrecognized escape drops the backtick and keeps the character.
    [InlineData("\"a`zb\"", "azb")]
    [InlineData("\"a`_b\"", "a_b")]
    [InlineData("\"a` b\"", "a b")]
    [InlineData("\"a`'b\"", "a'b")]
    [InlineData("\"a`#b\"", "a#b")]
    [InlineData("\"a`(b\"", "a(b")]
    // Characters that are only special outside a string.
    [InlineData("\"a#b\"", "a#b")]
    [InlineData("\"100%\"", "100%")]
    [InlineData("\"[a]\"", "[a]")]
    [InlineData("\"{a}\"", "{a}")]
    [InlineData("\"a@b\"", "a@b")]
    public void ExpandableString_MatchesPwshByteForByte(string argumentText, string expected)
    {
        Assert.Equal(expected, FirstArgument(argumentText).Value);
    }

    [Theory]
    // A bare argument keeps characters that other shells would treat as syntax.
    [InlineData("a-b", "a-b")]
    [InlineData("a.b", "a.b")]
    [InlineData("a/b", "a/b")]
    [InlineData("a\\b", "a\\b")]
    [InlineData("C:\\path\\to\\file", "C:\\path\\to\\file")]
    [InlineData("--flag", "--flag")]
    [InlineData("/flag", "/flag")]
    [InlineData("a=b", "a=b")]
    [InlineData("a:b", "a:b")]
    [InlineData("a+b", "a+b")]
    [InlineData("a*b", "a*b")]
    [InlineData("a?b", "a?b")]
    [InlineData("a[0]", "a[0]")]
    [InlineData("a#b", "a#b")]
    [InlineData("a%b", "a%b")]
    [InlineData("a!b", "a!b")]
    [InlineData("a~b", "a~b")]
    [InlineData("a^b", "a^b")]
    [InlineData("0x10", "0x10")]
    // A quote that is not the first character keeps the argument going.
    [InlineData("a'b'c", "abc")]
    [InlineData("a\"b\"c", "abc")]
    [InlineData("prefix\"post\"", "prefixpost")]
    [InlineData("a'b'", "ab")]
    // A backtick escapes inside a bare argument too.
    [InlineData("a`nb", "a\nb")]
    [InlineData("a``b", "a`b")]
    [InlineData("a` b", "a b")]
    [InlineData("a`$b", "a$b")]
    public void BareArgument_MatchesPwshByteForByte(string argumentText, string expected)
    {
        Assert.Equal(expected, FirstArgument(argumentText).Value);
    }

    [Theory]
    [InlineData("Write-Output 'a'b", "a", "b")]
    [InlineData("Write-Output \"a\"b", "a", "b")]
    [InlineData("Write-Output 'a b'c", "a b", "c")]
    [InlineData("Write-Output \"pre\"suffix", "pre", "suffix")]
    [InlineData("Write-Output 'a'\"b\"", "a", "b")]
    [InlineData("Write-Output \"a\"'b'", "a", "b")]
    public void AQuoteThatOpensAnArgumentAlsoClosesIt(string text, string first, string second)
    {
        // PowerShell ends a quoted argument at its closing quote, so what follows starts a new argument. A quote in
        // the middle of a bare argument does not, which is why `a'b'c` is the single argument `abc`.
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand(text, ShellDialect.PowerShellCore));

        Assert.Equal([first, second], command.Arguments.Select(argument => argument.Value));
    }

    [Theory]
    [InlineData("Write-Output a,b", "a,b")]
    [InlineData("Write-Output 'a','b'", "'a','b'")]
    [InlineData("Write-Output $a,$b", "$a,$b")]
    public void ACommaKeepsTheArgumentGoing(string text, string expectedArgumentText)
    {
        // `a,b` is one command element in PowerShell: an array built from the two values around the comma.
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand(text, ShellDialect.PowerShellCore));
        var argument = Assert.Single(command.Arguments);

        Assert.Equal(expectedArgumentText, argument.ToFullString().TrimStart());
    }

    [Fact]
    public void ALoneDollarIsLiteralText()
    {
        // `$` only starts a variable when a name follows it.
        Assert.Equal("$", FirstArgument("$").Value);
        Assert.Equal("a$", FirstArgument("a$").Value);
        Assert.Empty(FirstArgument("$").DescendantNodes().OfType<PowerShellVariableExpressionSyntax>());
    }

    [Theory]
    [InlineData("$x")]
    [InlineData("$_")]
    [InlineData("$?")]
    [InlineData("$$")]
    [InlineData("${name}")]
    [InlineData("$env:PATH")]
    public void ADollarFollowedByANameIsAVariable(string argumentText)
    {
        var word = FirstArgument(argumentText);

        Assert.Null(word.Value);
        Assert.Single(word.DescendantNodes().OfType<PowerShellVariableExpressionSyntax>());
    }

    [Theory]
    [InlineData("'plain'", "plain")]
    [InlineData("'  spaced  '", "  spaced  ")]
    [InlineData("'a`u{41}b'", "a`u{41}b")]
    [InlineData("'#not a comment'", "#not a comment")]
    [InlineData("'a;b'", "a;b")]
    [InlineData("'-notAnOperator'", "-notAnOperator")]
    public void VerbatimString_KeepsEverything(string argumentText, string expected)
    {
        Assert.Equal(expected, FirstArgument(argumentText).Value);
    }
}
