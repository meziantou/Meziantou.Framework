using System;

namespace Meziantou.Framework.CodeDom
{
    partial class CSharpCodeGenerator
    {
        protected virtual void Write(IndentedTextWriter writer, CodeStatement statement)
        {
            switch (statement)
            {
                case CodeConditionStatement o:
                    Write(writer, o);
                    break;

                case CodeReturnStatement o:
                    Write(writer, o);
                    break;

                case CodeAssignStatement o:
                    Write(writer, o);
                    break;

                case CodeExpressionStatement o:
                    Write(writer, o);
                    break;

                case CodeThrowStatement o:
                    Write(writer, o);
                    break;

                case CodeUsingStatement o:
                    Write(writer, o);
                    break;

                case CodeVariableDeclarationStatement o:
                    Write(writer, o);
                    break;

                case CodeWhileStatement o:
                    Write(writer, o);
                    break;

                case CodeIterationStatement o:
                    Write(writer, o);
                    break;

                case CodeExpressionCollectionStatement o:
                    Write(writer, o);
                    break;

                case CodeGotoNextLoopIterationStatement o:
                    Write(writer, o);
                    break;

                case CodeExitLoopStatement o:
                    Write(writer, o);
                    break;

                case CodeSnippetStatement o:
                    Write(writer, o);
                    break;

                case CodeTryCatchFinallyStatement o:
                    Write(writer, o);
                    break;

                default:
                    throw new NotSupportedException();
            }
        }

        protected virtual void Write(IndentedTextWriter writer, CodeTryCatchFinallyStatement statement)
        {
            writer.WriteLine("try");
            WriteStatementsOrEmptyBlock(writer, statement.Try);

            Write(writer, statement.Catch);

            if (statement.Finally != null)
            {
                writer.WriteLine("finally");
                Write(writer, statement.Finally);
            }
        }

        protected virtual void Write(IndentedTextWriter writer, CodeSnippetStatement statement)
        {
            writer.WriteLine(statement.Statement);
        }

        protected virtual void Write(IndentedTextWriter writer, CodeGotoNextLoopIterationStatement statement)
        {
            writer.WriteLine("continue;");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeExitLoopStatement statement)
        {
            writer.WriteLine("break;");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeReturnStatement statement)
        {
            writer.Write("return");
            if (statement.Expression != null)
            {
                writer.Write(" ");
                Write(writer, statement.Expression);
            }
            writer.WriteLine(";");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeConditionStatement statement)
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

        protected virtual void Write(IndentedTextWriter writer, CodeAssignStatement statement)
        {
            Write(writer, statement.LeftExpression);
            writer.Write(" = ");
            Write(writer, statement.RightExpression);
            writer.WriteLine(";");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeExpressionStatement statement)
        {
            Write(writer, statement.Expression);
            writer.WriteLine(";");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeThrowStatement statement)
        {
            writer.Write("throw");
            if (statement.Expression != null)
            {
                writer.Write(" ");
                Write(writer, statement.Expression);
            }
            writer.WriteLine(";");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeUsingStatement statement)
        {
            writer.Write("using (");
            Write(statement.Statement);
            writer.Write(")");
            writer.WriteLine();
            WriteStatementsOrEmptyBlock(writer, statement.Body);
        }

        protected virtual void Write(IndentedTextWriter writer, CodeVariableDeclarationStatement statement)
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

            writer.WriteLine(";");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeWhileStatement statement)
        {
            writer.Write("while (");
            Write(writer, statement.Condition);
            writer.Write(")");
            writer.WriteLine();

            WriteStatementsOrEmptyBlock(writer, statement.Body);
        }

        protected virtual void Write(IndentedTextWriter writer, CodeIterationStatement statement)
        {
            writer.Write("for (");
            Write(writer, statement.Initialization);
            writer.Write("; ");
            Write(writer, statement.Condition);
            writer.Write("; ");
            Write(writer, statement.IncrementStatement);
            writer.Write(")");
            writer.WriteLine();
            WriteStatementsOrEmptyBlock(writer, statement.Body);
        }

        protected virtual void Write(IndentedTextWriter writer, CodeExpressionCollectionStatement statement)
        {
            Write(writer, statement, ", ");
        }
    }
}
