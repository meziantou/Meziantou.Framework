using System;
using System.Globalization;
using System.Linq;

namespace Meziantou.Framework.CodeDom
{
    public partial class CSharpCodeGenerator
    {
        protected virtual void Write(IndentedTextWriter writer, Expression expression)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));

            WriteBeforeComments(writer, expression);
            switch (expression)
            {
                case BinaryExpression o:
                    Write(writer, o);
                    break;

                case UnaryExpression o:
                    Write(writer, o);
                    break;

                case LiteralExpression o:
                    Write(writer, o);
                    break;

                case ArgumentReferenceExpression o:
                    Write(writer, o);
                    break;

                case MemberReferenceExpression o:
                    Write(writer, o);
                    break;

                case MethodInvokeExpression o:
                    Write(writer, o);
                    break;

                case ThisExpression o:
                    Write(writer, o);
                    break;

                case MethodInvokeArgumentExpression o:
                    Write(writer, o);
                    break;

                case ArrayIndexerExpression o:
                    Write(writer, o);
                    break;

                case BaseExpression o:
                    Write(writer, o);
                    break;

                case DefaultValueExpression o:
                    Write(writer, o);
                    break;

                case NameofExpression o:
                    Write(writer, o);
                    break;

                case NewObjectExpression o:
                    Write(writer, o);
                    break;

                case SnippetExpression o:
                    Write(writer, o);
                    break;

                case ValueArgumentExpression o:
                    Write(writer, o);
                    break;

                case CastExpression o:
                    Write(writer, o);
                    break;

                case ConvertExpression o:
                    Write(writer, o);
                    break;

                case TypeOfExpression o:
                    Write(writer, o);
                    break;

                case VariableReference o:
                    Write(writer, o);
                    break;

                case TypeReference o:
                    Write(writer, o);
                    break;

                case AwaitExpression o:
                    Write(writer, o);
                    break;

                default:
                    throw new NotSupportedException();
            }

            WriteAfterComments(writer, expression);
        }

        protected virtual void Write(IndentedTextWriter writer, ThisExpression expression)
        {
            writer.Write("this");
        }

        protected virtual void Write(IndentedTextWriter writer, BaseExpression expression)
        {
            writer.Write("base");
        }

        protected virtual void Write(IndentedTextWriter writer, MethodInvokeExpression expression)
        {
            Write(writer, expression.Method);
            writer.Write("(");
            Write(writer, expression.Arguments, ", ");
            writer.Write(")");
        }

        protected virtual void Write(IndentedTextWriter writer, MethodInvokeArgumentExpression expression)
        {
            if (!string.IsNullOrEmpty(expression.Name))
            {
                WriteIdentifier(writer, expression.Name);
                writer.Write(": ");
            }

            Write(writer, expression.Value);
        }

        protected virtual void Write(IndentedTextWriter writer, ArgumentReferenceExpression expression)
        {
            WriteIdentifier(writer, expression.Name);
        }

        protected virtual void Write(IndentedTextWriter writer, MemberReferenceExpression expression)
        {
            if (expression.TargetObject != null)
            {
                Write(writer, expression.TargetObject);
                writer.Write(".");
            }

            WriteIdentifier(writer, expression.Name);
        }

        protected virtual void Write(IndentedTextWriter writer, LiteralExpression expression)
        {
            switch (expression.Value)
            {
                case null:
                    WriteNull(writer);
                    return;

                case bool value:
                    Write(writer, value);
                    break;

                case sbyte value:
                    Write(writer, value);
                    return;

                case byte value:
                    Write(writer, value);
                    return;

                case short value:
                    Write(writer, value);
                    return;

                case ushort value:
                    Write(writer, value);
                    return;

                case int value:
                    Write(writer, value);
                    return;

                case uint value:
                    Write(writer, value);
                    return;

                case long value:
                    Write(writer, value);
                    return;

                case ulong value:
                    Write(writer, value);
                    return;

                case float value:
                    Write(writer, value);
                    return;

                case double value:
                    Write(writer, value);
                    return;

                case decimal value:
                    Write(writer, value);
                    return;

                case char value:
                    Write(writer, value);
                    break;

                case string value:
                    Write(writer, value);
                    return;

                default:
                    throw new NotSupportedException();
            }
        }

        protected virtual void WriteNull(IndentedTextWriter writer)
        {
            writer.Write("null");
        }

        protected virtual void Write(IndentedTextWriter writer, bool value)
        {
            if (value)
            {
                writer.Write("true");
            }
            else
            {
                writer.Write("false");
            }
        }

        protected virtual void Write(IndentedTextWriter writer, sbyte value)
        {
            writer.Write(value.ToString(CultureInfo.InvariantCulture));
        }

        protected virtual void Write(IndentedTextWriter writer, byte value)
        {
            writer.Write(value.ToString(CultureInfo.InvariantCulture));
        }

        protected virtual void Write(IndentedTextWriter writer, short value)
        {
            writer.Write(value.ToString(CultureInfo.InvariantCulture));
        }

        protected virtual void Write(IndentedTextWriter writer, ushort value)
        {
            writer.Write(value.ToString(CultureInfo.InvariantCulture));
        }

        protected virtual void Write(IndentedTextWriter writer, int value)
        {
            writer.Write(value.ToString(CultureInfo.InvariantCulture));
        }

        protected virtual void Write(IndentedTextWriter writer, uint value)
        {
            writer.Write(value.ToString(CultureInfo.InvariantCulture));
            writer.Write("u");
        }

        protected virtual void Write(IndentedTextWriter writer, long value)
        {
            writer.Write(value.ToString(CultureInfo.InvariantCulture));
            writer.Write("L");
        }

        protected virtual void Write(IndentedTextWriter writer, ulong value)
        {
            writer.Write(value.ToString(CultureInfo.InvariantCulture));
            writer.Write("uL");
        }

        protected virtual void Write(IndentedTextWriter writer, string value)
        {
            writer.Write("\"");
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"':
                        writer.Write("\\\"");
                        break;

                    case '\t':
                        writer.Write(@"\t");
                        break;

                    case '\r':
                        writer.Write(@"\r");
                        break;

                    case '\n':
                        writer.Write(@"\n");
                        break;

                    case '\a':
                        writer.Write(@"\a");
                        break;

                    case '\b':
                        writer.Write(@"\b");
                        break;

                    case '\f':
                        writer.Write(@"\f");
                        break;

                    case '\v':
                        writer.Write(@"\v");
                        break;

                    case '\0':
                        writer.Write(@"\0");
                        break;

                    case '\\':
                        writer.Write(@"\\");
                        break;

                    default:
                        writer.Write(c);
                        break;
                }
            }
            writer.Write("\"");
        }

        protected virtual void Write(IndentedTextWriter writer, char value)
        {
            writer.Write('\'');
            writer.Write(value);
            writer.Write('\'');
        }

        protected virtual void Write(IndentedTextWriter writer, decimal value)
        {
            writer.Write(value.ToString(CultureInfo.InvariantCulture));
            writer.Write("m");
        }

        protected virtual void Write(IndentedTextWriter writer, float value)
        {
            if (float.IsNaN(value))
            {
                writer.Write("float.NaN");
            }
            else if (float.IsNegativeInfinity(value))
            {
                writer.Write("float.NegativeInfinity");
            }
            else if (float.IsPositiveInfinity(value))
            {
                writer.Write("float.PositiveInfinity");
            }
            else
            {
                writer.Write(value.ToString(CultureInfo.InvariantCulture));
                writer.Write("F");
            }
        }

        protected virtual void Write(IndentedTextWriter writer, double value)
        {
            if (double.IsNaN(value))
            {
                writer.Write("double.NaN");
            }
            else if (double.IsNegativeInfinity(value))
            {
                writer.Write("double.NegativeInfinity");
            }
            else if (double.IsPositiveInfinity(value))
            {
                writer.Write("double.PositiveInfinity");
            }
            else
            {
                writer.Write(value.ToString("R", CultureInfo.InvariantCulture));
                writer.Write("D");
            }
        }

        protected virtual void Write(IndentedTextWriter writer, BinaryExpression expression)
        {
            writer.Write("(");
            Write(writer, expression.LeftExpression);
            writer.Write(" ");
            writer.Write(Write(expression.Operator));
            writer.Write(" ");
            Write(writer, expression.RightExpression);
            writer.Write(")");
        }

        protected virtual void Write(IndentedTextWriter writer, UnaryExpression expression)
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

        protected virtual void Write(IndentedTextWriter writer, ArrayIndexerExpression expression)
        {
            Write(writer, expression.ArrayExpression);
            writer.Write("[");
            Write(writer, expression.Indices, ", ");
            writer.Write("]");
        }

        protected virtual void Write(IndentedTextWriter writer, DefaultValueExpression expression)
        {
            writer.Write("default");
            if (expression.Type != null)
            {
                writer.Write("(");
                Write(writer, expression.Type);
                writer.Write(")");
            }
        }

        protected virtual void Write(IndentedTextWriter writer, NameofExpression expression)
        {
            writer.Write("nameof(");
            Write(writer, expression.Expression);
            writer.Write(")");
        }

        protected virtual void Write(IndentedTextWriter writer, NewObjectExpression expression)
        {
            writer.Write("new ");
            Write(writer, expression.Type);
            writer.Write("(");
            Write(writer, expression.Arguments, ", ");
            writer.Write(")");
        }

        protected virtual void Write(IndentedTextWriter writer, SnippetExpression expression)
        {
            writer.Write(expression.Expression);
        }

        protected virtual void Write(IndentedTextWriter writer, ValueArgumentExpression expression)
        {
            writer.Write("value");
        }

        protected virtual void Write(IndentedTextWriter writer, CastExpression expression)
        {
            writer.Write("(");
            writer.Write("(");
            Write(writer, expression.Type);
            writer.Write(")");
            Write(writer, expression.Expression);
            writer.Write(")");
        }

        protected virtual void Write(IndentedTextWriter writer, ConvertExpression expression)
        {
            writer.Write("(");
            Write(writer, expression.Expression);
            writer.Write(" as ");
            Write(writer, expression.Type);
            writer.Write(")");
        }

        protected virtual void Write(IndentedTextWriter writer, TypeOfExpression expression)
        {
            writer.Write("typeof(");
            Write(writer, expression.Type);
            writer.Write(")");
        }

        protected virtual void Write(IndentedTextWriter writer, VariableReference expression)
        {
            WriteIdentifier(writer, expression.Name);
        }

        protected virtual void Write(IndentedTextWriter writer, TypeReference type)
        {
            if (_predefinedTypes.TryGetValue(type.ClrFullTypeName, out var keyword))
            {
                writer.Write(keyword);
                return;
            }

            if (type.Namespace != null)
            {
                writer.Write(type.Namespace);
                writer.Write('.');
            }

            if (type.Name != null)
            {
                writer.Write(type.Name.Replace('+', '.'));
            }

            if (type.Parameters.Any())
            {
                writer.Write('<');
                var first = true;
                foreach (var parameter in type.Parameters)
                {
                    if (!first)
                    {
                        writer.Write(',');
                    }

                    Write(writer, parameter);
                    first = false;
                }

                writer.Write('>');
            }
        }

        protected virtual void Write(IndentedTextWriter writer, AwaitExpression expression)
        {
            writer.Write("await ");
            if (expression.Expression != null)
            {
                Write(writer, expression.Expression);
            }
        }
    }
}
