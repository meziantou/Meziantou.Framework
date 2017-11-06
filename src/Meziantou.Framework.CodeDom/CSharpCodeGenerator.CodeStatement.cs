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

        protected virtual void Write(IndentedTextWriter writer, CodeStatement statement, WriteStatementOptions options)
        {
            if (statement == null)
                throw new ArgumentNullException(nameof(statement));
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            WriteBeforeComments(writer, statement);
            switch (statement)
            {
                case CodeConditionStatement o:
                    Write(writer, o, options);
                    break;

                case CodeReturnStatement o:
                    Write(writer, o, options);
                    break;

                case CodeYieldReturnStatement o:
                    Write(writer, o, options);
                    break;

                case CodeYieldBreakStatement o:
                    Write(writer, o, options);
                    break;

                case CodeAssignStatement o:
                    Write(writer, o, options);
                    break;

                case CodeExpressionStatement o:
                    Write(writer, o, options);
                    break;

                case CodeThrowStatement o:
                    Write(writer, o, options);
                    break;

                case CodeUsingStatement o:
                    Write(writer, o, options);
                    break;

                case CodeVariableDeclarationStatement o:
                    Write(writer, o, options);
                    break;

                case CodeWhileStatement o:
                    Write(writer, o, options);
                    break;

                case CodeIterationStatement o:
                    Write(writer, o, options);
                    break;

                case CodeExpressionCollectionStatement o:
                    Write(writer, o, options);
                    break;

                case CodeGotoNextLoopIterationStatement o:
                    Write(writer, o, options);
                    break;

                case CodeExitLoopStatement o:
                    Write(writer, o, options);
                    break;

                case CodeSnippetStatement o:
                    Write(writer, o, options);
                    break;

                case CodeTryCatchFinallyStatement o:
                    Write(writer, o, options);
                    break;

                case CodeAddEventHandlerStatement o:
                    Write(writer, o, options);
                    break;

                case CodeRemoveEventHandlerStatement o:
                    Write(writer, o, options);
                    break;

                case CodeCommentStatement o:
                    Write(writer, o, options);
                    break;

                default:
                    throw new NotSupportedException();
            }

            WriteAfterComments(writer, statement);
        }

        protected virtual void Write(IndentedTextWriter writer, CodeTryCatchFinallyStatement statement, WriteStatementOptions options)
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

        protected virtual void Write(IndentedTextWriter writer, CodeSnippetStatement statement, WriteStatementOptions options)
        {
            writer.WriteLine(statement.Statement);
        }

        protected virtual void Write(IndentedTextWriter writer, CodeGotoNextLoopIterationStatement statement, WriteStatementOptions options)
        {
            writer.WriteLine("continue;");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeExitLoopStatement statement, WriteStatementOptions options)
        {
            writer.WriteLine("break;");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeReturnStatement statement, WriteStatementOptions options)
        {
            writer.Write("return");
            if (statement.Expression != null)
            {
                writer.Write(" ");
                Write(writer, statement.Expression);
            }
            writer.WriteLine(";");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeYieldReturnStatement statement, WriteStatementOptions options)
        {
            writer.Write("yield return");
            if (statement.Expression != null)
            {
                writer.Write(" ");
                Write(writer, statement.Expression);
            }
            writer.WriteLine(";");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeYieldBreakStatement statement, WriteStatementOptions options)
        {
            writer.WriteLine("yield break;");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeConditionStatement statement, WriteStatementOptions options)
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

        protected virtual void Write(IndentedTextWriter writer, CodeAssignStatement statement, WriteStatementOptions options)
        {
            Write(writer, statement.LeftExpression);
            writer.Write(" = ");
            Write(writer, statement.RightExpression);
            options.WriteEnd(writer);
        }

        protected virtual void Write(IndentedTextWriter writer, CodeExpressionStatement statement, WriteStatementOptions options)
        {
            Write(writer, statement.Expression);
            options.WriteEnd(writer);
        }

        protected virtual void Write(IndentedTextWriter writer, CodeThrowStatement statement, WriteStatementOptions options)
        {
            writer.Write("throw");
            if (statement.Expression != null)
            {
                writer.Write(" ");
                Write(writer, statement.Expression);
            }
            writer.WriteLine(";");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeUsingStatement statement, WriteStatementOptions options)
        {
            writer.Write("using (");
            Write(writer, statement.Statement, _inlineStatementWriteStatementOptions);
            writer.Write(")");
            writer.WriteLine();
            WriteStatementsOrEmptyBlock(writer, statement.Body);
        }

        protected virtual void Write(IndentedTextWriter writer, CodeVariableDeclarationStatement statement, WriteStatementOptions options)
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

        protected virtual void Write(IndentedTextWriter writer, CodeWhileStatement statement, WriteStatementOptions options)
        {
            writer.Write("while (");
            Write(writer, statement.Condition);
            writer.Write(")");
            writer.WriteLine();

            WriteStatementsOrEmptyBlock(writer, statement.Body);
        }

        protected virtual void Write(IndentedTextWriter writer, CodeIterationStatement statement, WriteStatementOptions options)
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

        protected virtual void Write(IndentedTextWriter writer, CodeAddEventHandlerStatement statement, WriteStatementOptions options)
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

        protected virtual void Write(IndentedTextWriter writer, CodeRemoveEventHandlerStatement statement, WriteStatementOptions options)
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

        protected virtual void Write(IndentedTextWriter writer, CodeExpressionCollectionStatement statement, WriteStatementOptions options)
        {
            Write(writer, statement, ", ");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeCommentStatement statement, WriteStatementOptions options)
        {
            WriteLineComment(writer, statement.Content);
        }
    }
}
