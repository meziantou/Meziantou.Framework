using System.Globalization;

namespace Meziantou.Framework.CodeDom;

public partial class CSharpCodeGenerator
{
    protected virtual void WriteExpression(IndentedTextWriter writer, Expression? expression)
    {
        if (expression is null)
            return;

        WriteBeforeComments(writer, expression);
        switch (expression)
        {
            case BinaryExpression o:
                WriteBinaryExpression(writer, o);
                break;

            case UnaryExpression o:
                WriteUnaryExpression(writer, o);
                break;

            case LiteralExpression o:
                WriteLiteralExpression(writer, o);
                break;

            case ArgumentReferenceExpression o:
                WriteArgumentReferenceExpression(writer, o);
                break;

            case MemberReferenceExpression o:
                WriteMemberReferenceExpression(writer, o);
                break;

            case MethodInvokeExpression o:
                WriteMethodInvokeExpression(writer, o);
                break;

            case ThisExpression o:
                WriteThisExpression(writer, o);
                break;

            case MethodInvokeArgumentExpression o:
                WriteMethodInvokeArgumentExpression(writer, o);
                break;

            case ArrayIndexerExpression o:
                WriteArrayIndexerExpression(writer, o);
                break;

            case BaseExpression o:
                WriteBaseExpression(writer, o);
                break;

            case DefaultValueExpression o:
                WriteDefaultValueExpression(writer, o);
                break;

            case NameofExpression o:
                WriteNameofExpression(writer, o);
                break;

            case NewObjectExpression o:
                WriteNewObjectExpression(writer, o);
                break;

            case SnippetExpression o:
                WriteSnippetExpression(writer, o);
                break;

            case ValueArgumentExpression o:
                WriteValueArgumentExpression(writer, o);
                break;

            case CastExpression o:
                WriteCastExpression(writer, o);
                break;

            case ConvertExpression o:
                WriteConvertExpression(writer, o);
                break;

            case TypeOfExpression o:
                WriteTypeOfExpression(writer, o);
                break;

            case VariableReferenceExpression o:
                WriteVariableReferenceExpression(writer, o);
                break;

            case TypeReferenceExpression o:
                WriteTypeReferenceExpression(writer, o);
                break;

            case AwaitExpression o:
                WriteAwaitExpression(writer, o);
                break;

            case NewArrayExpression o:
                WriteNewArrayExpression(writer, o);
                break;

            case IsInstanceOfTypeExpression o:
                WriteIsInstanceOfTypeExpression(writer, o);
                break;

            default:
                throw new NotSupportedException();
        }

        WriteAfterComments(writer, expression);
    }

    protected virtual void WriteIsInstanceOfTypeExpression(IndentedTextWriter writer, IsInstanceOfTypeExpression expression)
    {
        writer.Write("(");
        WriteExpression(writer, expression.Expression);
        writer.Write(" is ");
        WriteTypeReference(writer, expression.Type);
        writer.Write(")");
    }

    protected virtual void WriteTypeReferenceExpression(IndentedTextWriter writer, TypeReferenceExpression expression)
    {
        WriteTypeReference(writer, expression.Type);
    }

    protected virtual void WriteThisExpression(IndentedTextWriter writer, ThisExpression expression)
    {
        writer.Write("this");
    }

    protected virtual void WriteBaseExpression(IndentedTextWriter writer, BaseExpression expression)
    {
        writer.Write("base");
    }

    protected virtual void WriteMethodInvokeExpression(IndentedTextWriter writer, MethodInvokeExpression expression)
    {
        WriteExpression(writer, expression.Method);
        WriteGenericParameters(writer, expression.Parameters);
        writer.Write("(");
        Write(writer, expression.Arguments, ", ");
        writer.Write(")");
    }

    protected virtual void WriteMethodInvokeArgumentExpression(IndentedTextWriter writer, MethodInvokeArgumentExpression expression)
    {
        WriteDirection(writer, expression.Direction);

        if (!string.IsNullOrEmpty(expression.Name))
        {
            WriteIdentifier(writer, expression.Name);
            writer.Write(": ");
        }

        WriteExpression(writer, expression.Value);
    }

    protected virtual void WriteArgumentReferenceExpression(IndentedTextWriter writer, ArgumentReferenceExpression expression)
    {
        WriteIdentifier(writer, expression.Name);
    }

    protected virtual void WriteMemberReferenceExpression(IndentedTextWriter writer, MemberReferenceExpression expression)
    {
        if (expression.TargetObject is not null)
        {
            WriteExpression(writer, expression.TargetObject);
            writer.Write(".");
        }

        WriteIdentifier(writer, expression.Name);
    }

    protected virtual void WriteLiteralExpression(IndentedTextWriter writer, LiteralExpression expression)
    {
        switch (expression.Value)
        {
            case null:
                WriteNullLiteral(writer);
                return;

            case bool value:
                WriteBooleanLiteral(writer, value);
                break;

            case sbyte value:
                WriteSByteLiteral(writer, value);
                return;

            case byte value:
                WriteByteLiteral(writer, value);
                return;

            case short value:
                WriteInt16Literal(writer, value);
                return;

            case ushort value:
                WriteUInt16Literal(writer, value);
                return;

            case int value:
                WriteInt32Literal(writer, value);
                return;

            case uint value:
                WriteUint32Literal(writer, value);
                return;

            case long value:
                Int64Literal(writer, value);
                return;

            case ulong value:
                WriteUInt64Literal(writer, value);
                return;

            case float value:
                WriteSingleLiteral(writer, value);
                return;

            case double value:
                WriteDoubleLiteral(writer, value);
                return;

            case decimal value:
                WriteDecimalLiteral(writer, value);
                return;

            case char value:
                WriteCharLiteral(writer, value);
                break;

            case string value:
                WriteStringLiteral(writer, value);
                return;

            default:
                throw new NotSupportedException();
        }
    }

    protected virtual void WriteNullLiteral(IndentedTextWriter writer)
    {
        writer.Write("null");
    }

    protected virtual void WriteBooleanLiteral(IndentedTextWriter writer, bool value)
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

    protected virtual void WriteSByteLiteral(IndentedTextWriter writer, sbyte value)
    {
        writer.Write(value.ToString(CultureInfo.InvariantCulture));
    }

    protected virtual void WriteByteLiteral(IndentedTextWriter writer, byte value)
    {
        writer.Write(value.ToString(CultureInfo.InvariantCulture));
    }

    protected virtual void WriteInt16Literal(IndentedTextWriter writer, short value)
    {
        writer.Write(value.ToString(CultureInfo.InvariantCulture));
    }

    protected virtual void WriteUInt16Literal(IndentedTextWriter writer, ushort value)
    {
        writer.Write(value.ToString(CultureInfo.InvariantCulture));
    }

    protected virtual void WriteInt32Literal(IndentedTextWriter writer, int value)
    {
        writer.Write(value.ToString(CultureInfo.InvariantCulture));
    }

    protected virtual void WriteUint32Literal(IndentedTextWriter writer, uint value)
    {
        writer.Write(value.ToString(CultureInfo.InvariantCulture));
        writer.Write("u");
    }

    protected virtual void Int64Literal(IndentedTextWriter writer, long value)
    {
        writer.Write(value.ToString(CultureInfo.InvariantCulture));
        writer.Write("L");
    }

    protected virtual void WriteUInt64Literal(IndentedTextWriter writer, ulong value)
    {
        writer.Write(value.ToString(CultureInfo.InvariantCulture));
        writer.Write("uL");
    }

    protected virtual void WriteStringLiteral(IndentedTextWriter writer, string value)
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

    protected virtual void WriteCharLiteral(IndentedTextWriter writer, char value)
    {
        writer.Write('\'');
        writer.Write(value);
        writer.Write('\'');
    }

    protected virtual void WriteDecimalLiteral(IndentedTextWriter writer, decimal value)
    {
        writer.Write(value.ToString(CultureInfo.InvariantCulture));
        writer.Write("m");
    }

    protected virtual void WriteSingleLiteral(IndentedTextWriter writer, float value)
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

    protected virtual void WriteDoubleLiteral(IndentedTextWriter writer, double value)
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

    protected virtual void WriteBinaryExpression(IndentedTextWriter writer, BinaryExpression expression)
    {
        writer.Write("(");
        WriteExpression(writer, expression.LeftExpression);
        writer.Write(" ");
        writer.Write(WriteBinaryOperator(expression.Operator));
        writer.Write(" ");
        WriteExpression(writer, expression.RightExpression);
        writer.Write(")");
    }

    protected virtual void WriteUnaryExpression(IndentedTextWriter writer, UnaryExpression expression)
    {
        writer.Write("(");
        if (IsPrefixOperator(expression.Operator))
        {
            writer.Write(WriteUnaryOperator(expression.Operator));
            WriteExpression(writer, expression.Expression);
        }
        else
        {
            WriteExpression(writer, expression.Expression);
            writer.Write(WriteUnaryOperator(expression.Operator));
        }
        writer.Write(")");
    }

    protected virtual void WriteArrayIndexerExpression(IndentedTextWriter writer, ArrayIndexerExpression expression)
    {
        WriteExpression(writer, expression.ArrayExpression);
        writer.Write("[");
        Write(writer, expression.Indices, ", ");
        writer.Write("]");
    }

    protected virtual void WriteDefaultValueExpression(IndentedTextWriter writer, DefaultValueExpression expression)
    {
        writer.Write("default");
        if (expression.Type is not null)
        {
            writer.Write("(");
            WriteTypeReference(writer, expression.Type);
            writer.Write(")");
        }
    }

    protected virtual void WriteNameofExpression(IndentedTextWriter writer, NameofExpression expression)
    {
        writer.Write("nameof(");
        WriteExpression(writer, expression.Expression);
        writer.Write(")");
    }

    protected virtual void WriteNewObjectExpression(IndentedTextWriter writer, NewObjectExpression expression)
    {
        writer.Write("new ");
        WriteTypeReference(writer, expression.Type);
        writer.Write("(");
        Write(writer, expression.Arguments, ", ");
        writer.Write(")");
    }

    protected virtual void WriteNewArrayExpression(IndentedTextWriter writer, NewArrayExpression expression)
    {
        writer.Write("new ");
        WriteTypeReference(writer, expression.Type);
        writer.Write("[");
        Write(writer, expression.Arguments, ", ");
        writer.Write("]");
    }

    protected virtual void WriteSnippetExpression(IndentedTextWriter writer, SnippetExpression expression)
    {
        writer.Write(expression.Expression);
    }

    protected virtual void WriteValueArgumentExpression(IndentedTextWriter writer, ValueArgumentExpression expression)
    {
        writer.Write("value");
    }

    protected virtual void WriteCastExpression(IndentedTextWriter writer, CastExpression expression)
    {
        writer.Write("(");
        writer.Write("(");
        WriteTypeReference(writer, expression.Type);
        writer.Write(")");
        WriteExpression(writer, expression.Expression);
        writer.Write(")");
    }

    protected virtual void WriteConvertExpression(IndentedTextWriter writer, ConvertExpression expression)
    {
        writer.Write("(");
        WriteExpression(writer, expression.Expression);
        writer.Write(" as ");
        WriteTypeReference(writer, expression.Type);
        writer.Write(")");
    }

    protected virtual void WriteTypeOfExpression(IndentedTextWriter writer, TypeOfExpression expression)
    {
        writer.Write("typeof(");
        WriteTypeReference(writer, expression.Type);
        writer.Write(")");
    }

    protected virtual void WriteVariableReferenceExpression(IndentedTextWriter writer, VariableReferenceExpression expression)
    {
        WriteIdentifier(writer, expression.Name);
    }

    protected virtual void WriteAwaitExpression(IndentedTextWriter writer, AwaitExpression expression)
    {
        writer.Write("await ");
        if (expression.Expression is not null)
        {
            WriteExpression(writer, expression.Expression);
        }
    }
}
