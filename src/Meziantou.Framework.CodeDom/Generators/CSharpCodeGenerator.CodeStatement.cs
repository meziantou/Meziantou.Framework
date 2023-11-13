namespace Meziantou.Framework.CodeDom;

public partial class CSharpCodeGenerator
{
    protected class WriteStatementOptions
    {
        public bool EndStatement { get; set; } = true;

        public void WriteEnd(IndentedTextWriter writer)
        {
            if (EndStatement)
            {
                writer.WriteLine(";");
            }
        }
    }

    private readonly WriteStatementOptions _defaultWriteStatementOptions = new();
    private readonly WriteStatementOptions _inlineStatementWriteStatementOptions = new() { EndStatement = false };

    protected virtual void WriteStatement(IndentedTextWriter writer, Statement statement)
    {
        WriteStatement(writer, statement, _defaultWriteStatementOptions);
    }

    protected virtual void WriteStatement(IndentedTextWriter writer, Statement? statement, WriteStatementOptions options)
    {
        if (statement is null)
            return;

        if (options is null)
            throw new ArgumentNullException(nameof(options));

        WriteNullableContextBefore(writer, statement);
        WriteBeforeComments(writer, statement);
        switch (statement)
        {
            case ConditionStatement o:
                WriteConditionStatement(writer, o, options);
                break;

            case ReturnStatement o:
                WriteReturnStatement(writer, o, options);
                break;

            case YieldReturnStatement o:
                WriteYieldReturnStatement(writer, o, options);
                break;

            case YieldBreakStatement o:
                WriteYieldBreakStatement(writer, o, options);
                break;

            case AssignStatement o:
                WriteAssignStatement(writer, o, options);
                break;

            case ExpressionStatement o:
                WriteExpressionStatement(writer, o, options);
                break;

            case ThrowStatement o:
                WriteThrowStatement(writer, o, options);
                break;

            case UsingStatement o:
                WriteUsingStatement(writer, o, options);
                break;

            case VariableDeclarationStatement o:
                WriteVariableDeclarationStatement(writer, o, options);
                break;

            case WhileStatement o:
                WriteWhileStatement(writer, o, options);
                break;

            case IterationStatement o:
                WriteIterationStatement(writer, o, options);
                break;

            case ExpressionCollectionStatement o:
                WriteExpressionCollectionStatement(writer, o, options);
                break;

            case GotoNextLoopIterationStatement o:
                WriteGotoNextLoopIterationStatement(writer, o, options);
                break;

            case ExitLoopStatement o:
                WriteExitLoopStatement(writer, o, options);
                break;

            case SnippetStatement o:
                WriteSnippetStatement(writer, o, options);
                break;

            case TryCatchFinallyStatement o:
                WriteTryCatchFinallyStatement(writer, o, options);
                break;

            case AddEventHandlerStatement o:
                WriteAddEventHandlerStatement(writer, o, options);
                break;

            case RemoveEventHandlerStatement o:
                WriteRemoveEventHandlerStatement(writer, o, options);
                break;

            case CommentStatement o:
                WriteCommentStatement(writer, o, options);
                break;

            default:
                throw new NotSupportedException();
        }

        WriteAfterComments(writer, statement);
        WriteNullableContextAfter(writer, statement);
    }

    protected virtual void WriteTryCatchFinallyStatement(IndentedTextWriter writer, TryCatchFinallyStatement statement, WriteStatementOptions options)
    {
        writer.WriteLine("try");
        WriteStatements(writer, statement.Try);

        if (statement.Catch is not null)
        {
            WriteCatchClauseCollection(writer, statement.Catch);
        }

        if (statement.Finally is not null)
        {
            writer.WriteLine("finally");
            WriteStatements(writer, statement.Finally);
        }
    }

    protected virtual void WriteSnippetStatement(IndentedTextWriter writer, SnippetStatement statement, WriteStatementOptions options)
    {
        writer.WriteLine(statement.Statement);
    }

    protected virtual void WriteGotoNextLoopIterationStatement(IndentedTextWriter writer, GotoNextLoopIterationStatement statement, WriteStatementOptions options)
    {
        writer.WriteLine("continue;");
    }

    protected virtual void WriteExitLoopStatement(IndentedTextWriter writer, ExitLoopStatement statement, WriteStatementOptions options)
    {
        writer.WriteLine("break;");
    }

    protected virtual void WriteReturnStatement(IndentedTextWriter writer, ReturnStatement statement, WriteStatementOptions options)
    {
        writer.Write("return");
        if (statement.Expression is not null)
        {
            writer.Write(" ");
            WriteExpression(writer, statement.Expression);
        }
        writer.WriteLine(";");
    }

    protected virtual void WriteYieldReturnStatement(IndentedTextWriter writer, YieldReturnStatement statement, WriteStatementOptions options)
    {
        writer.Write("yield return");
        if (statement.Expression is not null)
        {
            writer.Write(" ");
            WriteExpression(writer, statement.Expression);
        }
        writer.WriteLine(";");
    }

    protected virtual void WriteYieldBreakStatement(IndentedTextWriter writer, YieldBreakStatement statement, WriteStatementOptions options)
    {
        writer.WriteLine("yield break;");
    }

    protected virtual void WriteConditionStatement(IndentedTextWriter writer, ConditionStatement statement, WriteStatementOptions options)
    {
        writer.Write("if (");
        WriteExpression(writer, statement.Condition);
        writer.WriteLine(")");
        WriteStatements(writer, statement.TrueStatements);
        if (statement.FalseStatements is not null)
        {
            writer.WriteLine("else");
            WriteStatements(writer, statement.FalseStatements);
        }
    }

    protected virtual void WriteAssignStatement(IndentedTextWriter writer, AssignStatement statement, WriteStatementOptions options)
    {
        WriteExpression(writer, statement.LeftExpression);
        writer.Write(" = ");
        WriteExpression(writer, statement.RightExpression);
        options.WriteEnd(writer);
    }

    protected virtual void WriteExpressionStatement(IndentedTextWriter writer, ExpressionStatement statement, WriteStatementOptions options)
    {
        WriteExpression(writer, statement.Expression);
        options.WriteEnd(writer);
    }

    protected virtual void WriteThrowStatement(IndentedTextWriter writer, ThrowStatement statement, WriteStatementOptions options)
    {
        writer.Write("throw");
        if (statement.Expression is not null)
        {
            writer.Write(" ");
            WriteExpression(writer, statement.Expression);
        }
        writer.WriteLine(";");
    }

    protected virtual void WriteUsingStatement(IndentedTextWriter writer, UsingStatement statement, WriteStatementOptions options)
    {
        writer.Write("using (");
        WriteStatement(writer, statement.Statement, _inlineStatementWriteStatementOptions);
        writer.Write(")");
        writer.WriteLine();
        WriteStatements(writer, statement.Body);
    }

    protected virtual void WriteVariableDeclarationStatement(IndentedTextWriter writer, VariableDeclarationStatement statement, WriteStatementOptions options)
    {
        if (statement.Type is not null)
        {
            WriteTypeReference(writer, statement.Type);
            writer.Write(" ");
        }
        else
        {
            writer.Write("var ");
        }

        WriteIdentifier(writer, statement.Name);
        if (statement.InitExpression is not null)
        {
            writer.Write(" = ");
            WriteExpression(writer, statement.InitExpression);
        }

        options.WriteEnd(writer);
    }

    protected virtual void WriteWhileStatement(IndentedTextWriter writer, WhileStatement statement, WriteStatementOptions options)
    {
        writer.Write("while (");
        WriteExpression(writer, statement.Condition);
        writer.Write(")");
        writer.WriteLine();

        WriteStatements(writer, statement.Body);
    }

    protected virtual void WriteIterationStatement(IndentedTextWriter writer, IterationStatement statement, WriteStatementOptions options)
    {
        writer.Write("for (");
        if (statement.Initialization is not null)
        {
            WriteStatement(writer, statement.Initialization, _inlineStatementWriteStatementOptions);
        }
        writer.Write("; ");
        if (statement.Condition is not null)
        {
            WriteExpression(writer, statement.Condition);
        }
        writer.Write("; ");
        if (statement.IncrementStatement is not null)
        {
            WriteStatement(writer, statement.IncrementStatement, _inlineStatementWriteStatementOptions);
        }
        writer.Write(")");
        writer.WriteLine();
        WriteStatements(writer, statement.Body);
    }

    protected virtual void WriteAddEventHandlerStatement(IndentedTextWriter writer, AddEventHandlerStatement statement, WriteStatementOptions options)
    {
        if (statement.LeftExpression is not null)
        {
            WriteExpression(writer, statement.LeftExpression);
        }

        writer.Write(" += ");

        if (statement.RightExpression is not null)
        {
            WriteExpression(writer, statement.RightExpression);
        }

        writer.WriteLine(";");
    }

    protected virtual void WriteRemoveEventHandlerStatement(IndentedTextWriter writer, RemoveEventHandlerStatement statement, WriteStatementOptions options)
    {
        if (statement.LeftExpression is not null)
        {
            WriteExpression(writer, statement.LeftExpression);
        }

        writer.Write(" -= ");

        if (statement.RightExpression is not null)
        {
            WriteExpression(writer, statement.RightExpression);
        }

        writer.WriteLine(";");
    }

    protected virtual void WriteExpressionCollectionStatement(IndentedTextWriter writer, ExpressionCollectionStatement statement, WriteStatementOptions options)
    {
        Write(writer, statement, ", ");
    }

    protected virtual void WriteCommentStatement(IndentedTextWriter writer, CommentStatement statement, WriteStatementOptions options)
    {
        WriteLineComment(writer, statement.Content);
    }
}
