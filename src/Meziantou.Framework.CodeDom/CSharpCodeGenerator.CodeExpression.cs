using System;
using System.Globalization;

namespace Meziantou.Framework.CodeDom
{
    partial class CSharpCodeGenerator
    {
        protected virtual void Write(IndentedTextWriter writer, CodeExpression expression)
        {
            switch (expression)
            {
                case CodeBinaryExpression o:
                    Write(writer, o);
                    break;

                case CodeUnaryExpression o:
                    Write(writer, o);
                    break;

                case CodeLiteralExpression o:
                    Write(writer, o);
                    break;

                case CodeArgumentReferenceExpression o:
                    Write(writer, o);
                    break;

                case CodeMemberReferenceExpression o:
                    Write(writer, o);
                    break;

                case CodeMethodInvokeExpression o:
                    Write(writer, o);
                    break;

                case CodeThisExpression o:
                    Write(writer, o);
                    break;

                case CodeMethodInvokeArgumentExpression o:
                    Write(writer, o);
                    break;

                case CodeArrayIndexerExpression o:
                    Write(writer, o);
                    break;

                case CodeBaseExpression o:
                    Write(writer, o);
                    break;

                case CodeDefaultValueExpression o:
                    Write(writer, o);
                    break;

                case CodeNameofExpression o:
                    Write(writer, o);
                    break;

                case CodeNewObjectExpression o:
                    Write(writer, o);
                    break;

                case CodeSnippetExpression o:
                    Write(writer, o);
                    break;

                case CodeValueArgumentExpression o:
                    Write(writer, o);
                    break;

                case CodeCastExpression o:
                    Write(writer, o);
                    break;

                case CodeConvertExpression o:
                    Write(writer, o);
                    break;

                case CodeTypeOfExpression o:
                    Write(writer, o);
                    break;

                default:
                    throw new NotSupportedException();
            }
        }

        protected virtual void Write(IndentedTextWriter writer, CodeThisExpression expression)
        {
            writer.Write("this");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeBaseExpression expression)
        {
            writer.Write("base");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeMethodInvokeExpression expression)
        {
            Write(writer, expression.Method);
            writer.Write("(");
            Write(writer, expression.Arguments, ", ");
            writer.Write(")");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeMethodInvokeArgumentExpression expression)
        {
            if (!string.IsNullOrEmpty(expression.Name))
            {
                WriteIdentifier(writer, expression.Name);
                writer.Write(": ");
            }

            Write(writer, expression.Value);
        }

        protected virtual void Write(IndentedTextWriter writer, CodeArgumentReferenceExpression expression)
        {
            WriteIdentifier(writer, expression.Name);
        }

        protected virtual void Write(IndentedTextWriter writer, CodeMemberReferenceExpression expression)
        {
            if (expression.TargetObject != null)
            {
                Write(writer, expression.TargetObject);
                writer.Write(".");
            }

            WriteIdentifier(writer, expression.Name);
        }

        protected virtual void Write(IndentedTextWriter writer, CodeLiteralExpression expression)
        {
            switch (expression.Value)
            {
                case null:
                    writer.Write("null");
                    return;

                case true:
                    writer.Write("true");
                    break;

                case false:
                    writer.Write("false");
                    break;

                case sbyte value:
                    writer.Write(value.ToString(CultureInfo.InvariantCulture));
                    return;

                case byte value:
                    writer.Write(value.ToString(CultureInfo.InvariantCulture));
                    return;

                case short value:
                    writer.Write(value.ToString(CultureInfo.InvariantCulture));
                    return;

                case ushort value:
                    writer.Write(value.ToString(CultureInfo.InvariantCulture));
                    return;

                case int value:
                    writer.Write(value.ToString(CultureInfo.InvariantCulture));
                    return;

                case uint value:
                    writer.Write(value.ToString(CultureInfo.InvariantCulture));
                    writer.Write("u");
                    return;

                case long value:
                    writer.Write(value.ToString(CultureInfo.InvariantCulture));
                    writer.Write("L");
                    return;

                case ulong value:
                    writer.Write(value.ToString(CultureInfo.InvariantCulture));
                    writer.Write("uL");
                    return;

                case float value:
                    writer.Write(value.ToString(CultureInfo.InvariantCulture));
                    writer.Write("f");
                    return;

                case double value:
                    writer.Write(value.ToString(CultureInfo.InvariantCulture));
                    writer.Write("d");
                    return;

                case decimal value:
                    writer.Write(value.ToString(CultureInfo.InvariantCulture));
                    writer.Write("m");
                    return;

                case string value:
                    writer.Write("\"");
                    foreach (var c in value)
                    {
                        switch (c)
                        {
                            case '"':
                                writer.Write("\\\"");
                                break;

                            case '\t':
                                writer.Write("\\\t");
                                break;

                            case '\r':
                                writer.Write("\\\r");
                                break;

                            case '\n':
                                writer.Write("\\\n");
                                break;

                            case '\a':
                                writer.Write("\\\a");
                                break;

                            case '\b':
                                writer.Write("\\\b");
                                break;

                            case '\f':
                                writer.Write("\\\f");
                                break;

                            case '\v':
                                writer.Write("\\\v");
                                break;

                            case '\0':
                                writer.Write("\\\0");
                                break;

                            case '\\':
                                writer.Write("\\\\");
                                break;

                            default:
                                break;

                        }
                    }

                    writer.Write("\"");
                    return;

                default:
                    throw new NotSupportedException();
            }
        }

        protected virtual void Write(IndentedTextWriter writer, CodeBinaryExpression expression)
        {
            writer.Write("(");
            Write(writer, expression.LeftExpression);
            writer.Write(" ");
            writer.Write(Write(expression.Operator));
            writer.Write(" ");
            Write(writer, expression.RightExpression);
            writer.Write(")");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeUnaryExpression expression)
        {
            writer.Write("(");
            if (IsPrefixOperator(expression.Operator))
            {
                writer.Write(Write(expression.Operator));
                Write(writer, expression.Expression);
            }
            else
            {
                Write(writer, expression.Expression);
                writer.Write(Write(expression.Operator));
            }
            writer.Write(")");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeArrayIndexerExpression expression)
        {
            Write(writer, expression.ArrayExpression);
            writer.Write("[");
            Write(writer, expression.Indices, ", ");
            writer.Write("]");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeDefaultValueExpression expression)
        {
            writer.Write("default");
            if (expression.Type != null)
            {
                writer.Write("(");
                Write(writer, expression.Type);
                writer.Write(")");
            }
        }

        protected virtual void Write(IndentedTextWriter writer, CodeNameofExpression expression)
        {
            writer.Write("nameof(");
            Write(writer, expression.Expression);
            writer.Write(")");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeNewObjectExpression expression)
        {
            writer.Write("new ");
            Write(writer, expression.Type);
            writer.Write("(");
            Write(writer, expression.Arguments, ", ");
            writer.Write(")");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeSnippetExpression expression)
        {
            writer.Write(expression.Expression);
        }

        protected virtual void Write(IndentedTextWriter writer, CodeValueArgumentExpression expression)
        {
            writer.Write("value");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeCastExpression expression)
        {
            writer.Write("(");
            writer.Write("(");
            Write(writer, expression.Type);
            writer.Write(")");
            Write(writer, expression.Expression);
            writer.Write(")");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeConvertExpression expression)
        {
            writer.Write("(");
            Write(writer, expression.Expression);
            writer.Write(" as ");
            Write(writer, expression.Type);
            writer.Write(")");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeTypeOfExpression expression)
        {
            writer.Write("typeof(");
            Write(writer, expression.Type);
            writer.Write(")");
        }
    }
}
