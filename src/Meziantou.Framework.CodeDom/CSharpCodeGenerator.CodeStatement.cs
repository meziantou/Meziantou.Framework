using System;

namespace Meziantou.Framework.CodeDom
{
    partial class CSharpCodeGenerator
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

        protected readonly WriteStatementOptions _defaultWriteStatementOptions = new WriteStatementOptions();
        protected readonly WriteStatementOptions _inlineStatementWriteStatementOptions = new WriteStatementOptions() { EndStatement = false };

        protected virtual void Write(IndentedTextWriter writer, Statement statement, WriteStatementOptions options)
        {
            if (statement == null)
                throw new ArgumentNullException(nameof(statement));
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            WriteBeforeComments(writer, statement);
            switch (statement)
            {
                case ConditionStatement o:
                    Write(writer, o, options);
                    break;

                case ReturnStatement o:
                    Write(writer, o, options);
                    break;

                case YieldReturnStatement o:
                    Write(writer, o, options);
                    break;

                case YieldBreakStatement o:
                    Write(writer, o, options);
                    break;

                case AssignStatement o:
                    Write(writer, o, options);
                    break;

                case ExpressionStatement o:
                    Write(writer, o, options);
                    break;

                case ThrowStatement o:
                    Write(writer, o, options);
                    break;

                case UsingStatement o:
                    Write(writer, o, options);
                    break;

                case VariableDeclarationStatement o:
                    Write(writer, o, options);
                    break;

                case WhileStatement o:
                    Write(writer, o, options);
                    break;

                case IterationStatement o:
                    Write(writer, o, options);
                    break;

                case ExpressionCollectionStatement o:
                    Write(writer, o, options);
                    break;

                case GotoNextLoopIterationStatement o:
                    Write(writer, o, options);
                    break;

                case ExitLoopStatement o:
                    Write(writer, o, options);
                    break;

                case SnippetStatement o:
                    Write(writer, o, options);
                    break;

                case TryCatchFinallyStatement o:
                    Write(writer, o, options);
                    break;

                case AddEventHandlerStatement o:
                    Write(writer, o, options);
                    break;

                case RemoveEventHandlerStatement o:
                    Write(writer, o, options);
                    break;

                case CommentStatement o:
                    Write(writer, o, options);
                    break;

                default:
                    throw new NotSupportedException();
            }

            WriteAfterComments(writer, statement);
        }

        protected virtual void Write(IndentedTextWriter writer, TryCatchFinallyStatement statement, WriteStatementOptions options)
        {
            writer.WriteLine("try");
            WriteStatementsOrEmptyBlock(writer, statement.Try);

            if (statement.Catch != null)
            {
                Write(writer, statement.Catch);
            }

            if (statement.Finally != null)
            {
                writer.WriteLine("finally");
                Write(writer, statement.Finally);
            }
        }

        protected virtual void Write(IndentedTextWriter writer, SnippetStatement statement, WriteStatementOptions options)
        {
            writer.WriteLine(statement.Statement);
        }

        protected virtual void Write(IndentedTextWriter writer, GotoNextLoopIterationStatement statement, WriteStatementOptions options)
        {
            writer.WriteLine("continue;");
        }

        protected virtual void Write(IndentedTextWriter writer, ExitLoopStatement statement, WriteStatementOptions options)
        {
            writer.WriteLine("break;");
        }

        protected virtual void Write(IndentedTextWriter writer, ReturnStatement statement, WriteStatementOptions options)
        {
            writer.Write("return");
            if (statement.Expression != null)
            {
                writer.Write(" ");
                Write(writer, statement.Expression);
            }
            writer.WriteLine(";");
        }

        protected virtual void Write(IndentedTextWriter writer, YieldReturnStatement statement, WriteStatementOptions options)
        {
            writer.Write("yield return");
            if (statement.Expression != null)
            {
                writer.Write(" ");
                Write(writer, statement.Expression);
            }
            writer.WriteLine(";");
        }

        protected virtual void Write(IndentedTextWriter writer, YieldBreakStatement statement, WriteStatementOptions options)
        {
            writer.WriteLine("yield break;");
        }

        protected virtual void Write(IndentedTextWriter writer, ConditionStatement statement, WriteStatementOptions options)
        {
            writer.Write("if (");
            Write(writer, statement.Condition);
            writer.WriteLine(")");
            WriteStatementsOrEmptyBlock(writer, statement.TrueStatements);
            if (statement.FalseStatements != null)
            {
                writer.WriteLine("else");
                Write(writer, statement.FalseStatements);
            }
        }

        protected virtual void Write(IndentedTextWriter writer, AssignStatement statement, WriteStatementOptions options)
        {
            Write(writer, statement.LeftExpression);
            writer.Write(" = ");
            Write(writer, statement.RightExpression);
            options.WriteEnd(writer);
        }

        protected virtual void Write(IndentedTextWriter writer, ExpressionStatement statement, WriteStatementOptions options)
        {
            Write(writer, statement.Expression);
            options.WriteEnd(writer);
        }

        protected virtual void Write(IndentedTextWriter writer, ThrowStatement statement, WriteStatementOptions options)
        {
            writer.Write("throw");
            if (statement.Expression != null)
            {
                writer.Write(" ");
                Write(writer, statement.Expression);
            }
            writer.WriteLine(";");
        }

        protected virtual void Write(IndentedTextWriter writer, UsingStatement statement, WriteStatementOptions options)
        {
            writer.Write("using (");
            Write(writer, statement.Statement, _inlineStatementWriteStatementOptions);
            writer.Write(")");
            writer.WriteLine();
            WriteStatementsOrEmptyBlock(writer, statement.Body);
        }

        protected virtual void Write(IndentedTextWriter writer, VariableDeclarationStatement statement, WriteStatementOptions options)
        {
            if (statement.Type != null)
            {
                Write(writer, statement.Type);
                writer.Write(" ");
            }
            else
            {
                writer.Write("var ");
            }

            WriteIdentifier(writer, statement.Name);
            if (statement.InitExpression != null)
            {
                writer.Write(" = ");
                Write(writer, statement.InitExpression);
            }

            options.WriteEnd(writer);
        }

        protected virtual void Write(IndentedTextWriter writer, WhileStatement statement, WriteStatementOptions options)
        {
            writer.Write("while (");
            Write(writer, statement.Condition);
            writer.Write(")");
            writer.WriteLine();

            WriteStatementsOrEmptyBlock(writer, statement.Body);
        }

        protected virtual void Write(IndentedTextWriter writer, IterationStatement statement, WriteStatementOptions options)
        {
            writer.Write("for (");
            if (statement.Initialization != null)
            {
                Write(writer, statement.Initialization, _inlineStatementWriteStatementOptions);
            }
            writer.Write("; ");
            if (statement.Condition != null)
            {
                Write(writer, statement.Condition);
            }
            writer.Write("; ");
            if (statement.IncrementStatement != null)
            {
                Write(writer, statement.IncrementStatement, _inlineStatementWriteStatementOptions);
            }
            writer.Write(")");
            writer.WriteLine();
            WriteStatementsOrEmptyBlock(writer, statement.Body);
        }

        protected virtual void Write(IndentedTextWriter writer, AddEventHandlerStatement statement, WriteStatementOptions options)
        {
            if (statement.LeftExpression != null)
            {
                Write(writer, statement.LeftExpression);
            }

            writer.Write(" += ");

            if (statement.RightExpression != null)
            {
                Write(writer, statement.RightExpression);
            }

            writer.WriteLine(";");
        }

        protected virtual void Write(IndentedTextWriter writer, RemoveEventHandlerStatement statement, WriteStatementOptions options)
        {
            if (statement.LeftExpression != null)
            {
                Write(writer, statement.LeftExpression);
            }

            writer.Write(" -= ");

            if (statement.RightExpression != null)
            {
                Write(writer, statement.RightExpression);
            }

            writer.WriteLine(";");
        }

        protected virtual void Write(IndentedTextWriter writer, ExpressionCollectionStatement statement, WriteStatementOptions options)
        {
            Write(writer, statement, ", ");
        }

        protected virtual void Write(IndentedTextWriter writer, CommentStatement statement, WriteStatementOptions options)
        {
            WriteLineComment(writer, statement.Content);
        }
    }
}
