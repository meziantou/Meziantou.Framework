namespace Meziantou.Framework.Mermaid.Tests;

public sealed class RailroadDiagramParserTests
{
    private static RailroadDiagram Parse(string text) => (RailroadDiagram)MermaidDocument.Parse(text).Content;

    [Fact]
    public void Ebnf()
    {
        #region Mermaid_RailroadDiagram
        var diagram = (RailroadDiagram)MermaidDocument.Parse("""
            railroad-ebnf-beta
            title "Numbers"
            number = [ "+" | "-" ] digit+ ( "." digit* )? ;
            digit = ? a digit from 0 to 9 ? ;
            """).Content;
        #endregion

        Assert.Equal((RailroadNotation.Ebnf, "Numbers"), (diagram.Notation, diagram.Title));
        var number = Assert.IsType<RailroadSequence>(diagram.Rules[0].Definition);
        var sign = Assert.IsType<RailroadChoice>(Assert.IsType<RailroadOptional>(number.Items[0]).Expression);
        Assert.Equal(["+", "-"], sign.Alternatives.Select(item => ((RailroadTerminal)item).Value));
        var digits = Assert.IsType<RailroadRepetition>(number.Items[1]);
        Assert.Equal((1, (int?)null, "digit"), (digits.Minimum, digits.Maximum, ((RailroadNonterminal)digits.Expression).Name));
        Assert.IsType<RailroadSequence>(Assert.IsType<RailroadOptional>(number.Items[2]).Expression);
        Assert.Equal("a digit from 0 to 9", Assert.IsType<RailroadSpecial>(diagram.Rules[1].Definition).Text);
    }

    [Fact]
    public void EbnfIsoStyleCommentsAndExceptions()
    {
        var diagram = Parse("""
            railroad-ebnf-beta
            (* ISO 14977 *)
            list ::= item , { "," , item } ; /* W3C comment */
            item = letter - "x" | 'a\'b\n' ;
            quirk = b? c? ;
            """);
        var list = Assert.IsType<RailroadSequence>(diagram.Rules[0].Definition);
        Assert.Equal(0, Assert.IsType<RailroadRepetition>(list.Items[1]).Minimum);
        var item = Assert.IsType<RailroadChoice>(diagram.Rules[1].Definition);
        var exception = Assert.IsType<RailroadSequence>(item.Alternatives[0]);
        Assert.Equal("-", ((RailroadTerminal)exception.Items[1]).Value);
        Assert.Equal("a'b\n", ((RailroadTerminal)item.Alternatives[1]).Value);

        // As in Mermaid, "? c?" is a special sequence, not an optional c
        var quirk = Assert.IsType<RailroadSequence>(diagram.Rules[2].Definition);
        Assert.Equal("c", Assert.IsType<RailroadSpecial>(quirk.Items[1]).Text);
    }

    [Fact]
    public void Abnf()
    {
        var diagram = Parse("""
            railroad-abnf-beta
            title "Repetitions"
            ; a comment
            rule = 2*4a 3b *c 1*d *1e 2*f [ g ] ; another comment
              / %x30-39 / "text"
            ;
            """);
        Assert.Equal(RailroadNotation.Abnf, diagram.Notation);
        var choice = Assert.IsType<RailroadChoice>(diagram.Rules[0].Definition);
        var sequence = Assert.IsType<RailroadSequence>(choice.Alternatives[0]);
        Assert.Equal(
            [(2, (int?)4), (3, 3), (0, null), (1, null)],
            sequence.Items.Take(4).Select(item => (((RailroadRepetition)item).Minimum, ((RailroadRepetition)item).Maximum)));
        Assert.IsType<RailroadOptional>(sequence.Items[4]);
        Assert.Equal((2, (int?)null), (((RailroadRepetition)sequence.Items[5]).Minimum, ((RailroadRepetition)sequence.Items[5]).Maximum));
        Assert.IsType<RailroadOptional>(sequence.Items[6]);
        Assert.Equal("%x30-39", ((RailroadTerminal)choice.Alternatives[1]).Value);
        Assert.Equal("text", ((RailroadTerminal)choice.Alternatives[2]).Value);
    }

    [Fact]
    public void Peg()
    {
        var diagram = Parse("""
            railroad-peg-beta
            Identifier <- !Keyword &"a" Letter (Letter / "0")* . ; # a comment
            Keyword <- "if" / "else" ;
            Nested <- !(a b) &(!c) ;
            """);
        var identifier = Assert.IsType<RailroadSequence>(diagram.Rules[0].Definition);
        Assert.Equal(["!Keyword", "&\"a\""], identifier.Items.Take(2).Select(item => ((RailroadSpecial)item).Text));
        Assert.Equal(".", ((RailroadSpecial)identifier.Items[^1]).Text);
        var nested = Assert.IsType<RailroadSequence>(diagram.Rules[2].Definition);
        Assert.Equal(["!(...)", "&!c"], nested.Items.Select(item => ((RailroadSpecial)item).Text));
    }

    [Fact]
    public void Primitives()
    {
        var diagram = Parse("""
            railroad-beta
            accTitle: Expressions
            expression = sequence(
                nonterminal("term"),
                zeroOrMore(sequence(choice(terminal("+"), terminal("-")), nonterminal("term"))),
                optional(special("comment")),
                oneOrMore(sequence(terminal("x")))
            ) ;
            """);
        Assert.Equal("Expressions", diagram.AccessibleTitle);
        var expression = Assert.IsType<RailroadSequence>(diagram.Rules[0].Definition);
        Assert.HasCount(4, expression.Items);

        // A sequence of one expression is the expression
        Assert.IsType<RailroadTerminal>(((RailroadRepetition)expression.Items[3]).Expression);
    }

    [Theory]
    [InlineData("railroad-ebnf-beta\na = ;", "an expression is expected")]
    [InlineData("railroad-ebnf-beta\na = b", "';' is expected at the end of the rule 'a'.")]
    [InlineData("railroad-ebnf-beta\na = ( b ;", "'(' is not closed by ')'.")]
    [InlineData("railroad-ebnf-beta\na = titlex ;", "starts with the keyword 'title'")]
    [InlineData("railroad-ebnf-beta\na = b (* c ;", "The comment is not closed")]
    [InlineData("railroad-abnf-beta\na = b; c = d;", "';' is expected at the end of the rule 'a'.")]
    [InlineData("railroad-abnf-beta\na_b = c ;", "'=' is expected")]
    [InlineData("railroad-abnf-beta\na = 2* ;", "An expression is expected after the repetition.")]
    [InlineData("railroad-abnf-beta\na = %q1 ;", "A numeric value is written")]
    [InlineData("railroad-peg-beta\na = b ;", "'<-' is expected")]
    [InlineData("railroad-peg-beta\na <- ! ;", "An expression is expected after '!'.")]
    [InlineData("railroad-beta\na = term(\"x\") ;", "'term' is not an expression")]
    [InlineData("railroad-beta\nchoice = terminal(\"x\") ;", "'choice' is a keyword")]
    [InlineData("railroad-beta\na = terminal(\"x) ;", "The string is not closed.")]
    public void InvalidRailroadDiagrams(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpressionsNestedTooDeeplyAreRejected()
    {
        var text = "railroad-beta\na = " + string.Concat(Enumerable.Repeat("optional(", 100)) + "terminal(\"x\")" + new string(')', 100) + " ;";
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains("nested deeper", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("?")]
    [InlineData("+")]
    [InlineData(" - b")]
    public void ChainedEbnfOperatorsNestedTooDeeplyAreRejected(string op)
    {
        var text = "railroad-ebnf-beta\na = b" + string.Concat(Enumerable.Repeat(op, 150)) + " ;";
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains("nested deeper", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChainedEbnfOperatorsWithinTheMaximumDepthRoundTrip()
    {
        var text = "railroad-ebnf-beta\na = b" + new string('*', 20) + " c? ;";
        var document = MermaidDocument.Parse(text);
        var written = MermaidWriter.Write(document.Content);
        Assert.Equal(written, MermaidWriter.Write(MermaidDocument.Parse(written).Content));
    }

    [Theory]
    [InlineData(RailroadNotation.Primitives)]
    [InlineData(RailroadNotation.Ebnf)]
    [InlineData(RailroadNotation.Abnf)]
    [InlineData(RailroadNotation.Peg)]
    public void Write(RailroadNotation notation)
    {
        var diagram = new RailroadDiagram { Notation = notation, Title = "Grammar" };
        diagram.AddRule("rule", RailroadExpression.Choice(
            RailroadExpression.Sequence(RailroadExpression.Terminal("a"), RailroadExpression.Nonterminal("other-rule")),
            RailroadExpression.Optional(RailroadExpression.Choice(RailroadExpression.Terminal("b"), RailroadExpression.Sequence(RailroadExpression.Terminal("c")))),
            RailroadExpression.ZeroOrMore(RailroadExpression.Sequence(RailroadExpression.Terminal("d"), RailroadExpression.Terminal("e"))),
            RailroadExpression.OneOrMore(RailroadExpression.OneOrMore(RailroadExpression.Nonterminal("f")))));

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Content));
        var result = (RailroadDiagram)parsed.Content;
        Assert.Equal((notation, "Grammar"), (result.Notation, result.Title));
        var choice = Assert.IsType<RailroadChoice>(result.Rules[0].Definition);
        Assert.HasCount(4, choice.Alternatives);
        Assert.Equal("c", ((RailroadTerminal)((RailroadChoice)((RailroadOptional)choice.Alternatives[1]).Expression).Alternatives[1]).Value);
    }

    [Fact]
    public void WriteSpecificForms()
    {
        var ebnf = new RailroadDiagram { Notation = RailroadNotation.Ebnf };
        ebnf.AddRule("a", RailroadExpression.Sequence(RailroadExpression.Terminal("q\"\\\n"), RailroadExpression.Special("text")));
        var ebnfText = MermaidWriter.Write(ebnf);
        Assert.Contains("a = \"q\\\"\\\\\\n\" ? text ? ;", ebnfText, StringComparison.Ordinal);
        Assert.Equal("q\"\\\n", ((RailroadTerminal)((RailroadSequence)((RailroadDiagram)MermaidDocument.Parse(ebnfText).Content).Rules[0].Definition).Items[0]).Value);

        var abnf = new RailroadDiagram { Notation = RailroadNotation.Abnf };
        abnf.AddRule("a", RailroadExpression.Sequence(new RailroadRepetition(RailroadExpression.Terminal("%x41"), 2, 5), new RailroadRepetition(RailroadExpression.Nonterminal("b"), 0, 1)));
        Assert.Contains("a = 2*5%x41 [ b ] ;", MermaidWriter.Write(abnf), StringComparison.Ordinal);

        var peg = new RailroadDiagram { Notation = RailroadNotation.Peg };
        peg.AddRule("A", RailroadExpression.Sequence(RailroadExpression.Special("!Keyword"), RailroadExpression.Special("&\"x\""), RailroadExpression.Special("!(...)"), RailroadExpression.Special("&!b"), RailroadExpression.Special(".")));
        var pegText = MermaidWriter.Write(peg);
        Assert.Contains("A <- !Keyword &\"x\" !(. .) &(!b) . ;", pegText, StringComparison.Ordinal);
        Assert.Equal(pegText, MermaidWriter.Write(MermaidDocument.Parse(pegText).Content));
    }

    [Fact]
    public void WriteRejectsWhatTheNotationCannotExpress()
    {
        var diagram = new RailroadDiagram { Notation = RailroadNotation.Ebnf };
        diagram.AddRule("a", new RailroadRepetition(RailroadExpression.Terminal("x"), 2, 3));
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
        diagram.Notation = RailroadNotation.Abnf;
        diagram.Rules[0].Definition = RailroadExpression.Special("text");
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
        diagram.Notation = RailroadNotation.Peg;
        diagram.Rules[0].Definition = RailroadExpression.Nonterminal("not valid");
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
        diagram.Notation = RailroadNotation.Primitives;
        diagram.Rules[0].Name = "choice";
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
    }
}
