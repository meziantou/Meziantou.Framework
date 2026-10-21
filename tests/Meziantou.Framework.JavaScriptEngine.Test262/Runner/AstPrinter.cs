using System.Globalization;
using System.Text;

using Meziantou.Framework.JavaScriptEngine.Ast;

namespace Meziantou.Framework.JavaScriptEngine.Test262.Runner;

/// <summary>Prints a syntax tree in a canonical text form, without the positions (e.g. to compare two trees).</summary>
internal static class AstPrinter
{
    public static string Print(Node? node)
    {
        var builder = new StringBuilder();
        Print(builder, node);
        return builder.ToString();
    }

    private static void AppendList<T>(StringBuilder builder, string name, List<T> nodes)
        where T : Node?
    {
        builder.Append(' ').Append(name).Append("=[");
        for (var i = 0; i < nodes.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            Print(builder, nodes[i]);
        }

        builder.Append(']');
    }

    private static void AppendString(StringBuilder builder, string name, string? value)
    {
        builder.Append(' ').Append(name).Append('=');
        if (value is null)
        {
            builder.Append("null");
            return;
        }

        builder.Append('"');
        foreach (var c in value)
        {
            if (c is < ' ' or > '~' or '"' or '\\')
            {
                builder.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(c);
            }
        }

        builder.Append('"');
    }

    private static void Print(StringBuilder builder, Node? node)
    {
        switch (node)
        {
            case null:
                builder.Append("null");
                break;
            case Program n:
                builder.Append("(Program");
                AppendList(builder, "Body", n.Body);
                builder.Append(" IsModule=").Append(n.IsModule ? "true" : "false");
                builder.Append(" IsStrict=").Append(n.IsStrict ? "true" : "false");
                break;
            case ExpressionStatement n:
                builder.Append("(ExpressionStatement");
                builder.Append(" Expression=");
                Print(builder, n.Expression);
                AppendString(builder, "Directive", n.Directive);
                break;
            case BlockStatement n:
                builder.Append("(BlockStatement");
                AppendList(builder, "Body", n.Body);
                break;
            case EmptyStatement n:
                builder.Append("(EmptyStatement");
                break;
            case DebuggerStatement n:
                builder.Append("(DebuggerStatement");
                break;
            case WithStatement n:
                builder.Append("(WithStatement");
                builder.Append(" Object=");
                Print(builder, n.Object);
                builder.Append(" Body=");
                Print(builder, n.Body);
                break;
            case ReturnStatement n:
                builder.Append("(ReturnStatement");
                builder.Append(" Argument=");
                Print(builder, n.Argument);
                break;
            case LabeledStatement n:
                builder.Append("(LabeledStatement");
                builder.Append(" Label=");
                Print(builder, n.Label);
                builder.Append(" Body=");
                Print(builder, n.Body);
                break;
            case BreakStatement n:
                builder.Append("(BreakStatement");
                builder.Append(" Label=");
                Print(builder, n.Label);
                break;
            case ContinueStatement n:
                builder.Append("(ContinueStatement");
                builder.Append(" Label=");
                Print(builder, n.Label);
                break;
            case IfStatement n:
                builder.Append("(IfStatement");
                builder.Append(" Test=");
                Print(builder, n.Test);
                builder.Append(" Consequent=");
                Print(builder, n.Consequent);
                builder.Append(" Alternate=");
                Print(builder, n.Alternate);
                break;
            case SwitchStatement n:
                builder.Append("(SwitchStatement");
                builder.Append(" Discriminant=");
                Print(builder, n.Discriminant);
                AppendList(builder, "Cases", n.Cases);
                break;
            case SwitchCase n:
                builder.Append("(SwitchCase");
                builder.Append(" Test=");
                Print(builder, n.Test);
                AppendList(builder, "Consequent", n.Consequent);
                break;
            case ThrowStatement n:
                builder.Append("(ThrowStatement");
                builder.Append(" Argument=");
                Print(builder, n.Argument);
                break;
            case TryStatement n:
                builder.Append("(TryStatement");
                builder.Append(" Block=");
                Print(builder, n.Block);
                builder.Append(" Handler=");
                Print(builder, n.Handler);
                builder.Append(" Finalizer=");
                Print(builder, n.Finalizer);
                break;
            case CatchClause n:
                builder.Append("(CatchClause");
                builder.Append(" Param=");
                Print(builder, n.Param);
                builder.Append(" Body=");
                Print(builder, n.Body);
                break;
            case WhileStatement n:
                builder.Append("(WhileStatement");
                builder.Append(" Test=");
                Print(builder, n.Test);
                builder.Append(" Body=");
                Print(builder, n.Body);
                break;
            case DoWhileStatement n:
                builder.Append("(DoWhileStatement");
                builder.Append(" Body=");
                Print(builder, n.Body);
                builder.Append(" Test=");
                Print(builder, n.Test);
                break;
            case ForStatement n:
                builder.Append("(ForStatement");
                builder.Append(" Init=");
                Print(builder, n.Init);
                builder.Append(" Test=");
                Print(builder, n.Test);
                builder.Append(" Update=");
                Print(builder, n.Update);
                builder.Append(" Body=");
                Print(builder, n.Body);
                break;
            case ForInStatement n:
                builder.Append("(ForInStatement");
                builder.Append(" Left=");
                Print(builder, n.Left);
                builder.Append(" Right=");
                Print(builder, n.Right);
                builder.Append(" Body=");
                Print(builder, n.Body);
                break;
            case ForOfStatement n:
                builder.Append("(ForOfStatement");
                builder.Append(" Left=");
                Print(builder, n.Left);
                builder.Append(" Right=");
                Print(builder, n.Right);
                builder.Append(" Body=");
                Print(builder, n.Body);
                builder.Append(" IsAwait=").Append(n.IsAwait ? "true" : "false");
                break;
            case VariableDeclaration n:
                builder.Append("(VariableDeclaration");
                builder.Append(" Kind=").Append(n.Kind.ToString());
                AppendList(builder, "Declarations", n.Declarations);
                break;
            case VariableDeclarator n:
                builder.Append("(VariableDeclarator");
                builder.Append(" Id=");
                Print(builder, n.Id);
                builder.Append(" Init=");
                Print(builder, n.Init);
                break;
            case FunctionDeclaration n:
                builder.Append("(FunctionDeclaration");
                builder.Append(" Function=");
                Print(builder, n.Function);
                break;
            case ClassDeclaration n:
                builder.Append("(ClassDeclaration");
                builder.Append(" Class=");
                Print(builder, n.Class);
                break;
            case ImportDeclaration n:
                builder.Append("(ImportDeclaration");
                AppendList(builder, "Specifiers", n.Specifiers);
                builder.Append(" Source=");
                Print(builder, n.Source);
                AppendList(builder, "Attributes", n.Attributes);
                break;
            case ImportSpecifier n:
                builder.Append("(ImportSpecifier");
                builder.Append(" Imported=");
                Print(builder, n.Imported);
                builder.Append(" Local=");
                Print(builder, n.Local);
                break;
            case ImportDefaultSpecifier n:
                builder.Append("(ImportDefaultSpecifier");
                builder.Append(" Local=");
                Print(builder, n.Local);
                break;
            case ImportNamespaceSpecifier n:
                builder.Append("(ImportNamespaceSpecifier");
                builder.Append(" Local=");
                Print(builder, n.Local);
                break;
            case ImportAttribute n:
                builder.Append("(ImportAttribute");
                builder.Append(" Key=");
                Print(builder, n.Key);
                builder.Append(" Value=");
                Print(builder, n.Value);
                break;
            case ExportNamedDeclaration n:
                builder.Append("(ExportNamedDeclaration");
                builder.Append(" Declaration=");
                Print(builder, n.Declaration);
                AppendList(builder, "Specifiers", n.Specifiers);
                builder.Append(" Source=");
                Print(builder, n.Source);
                AppendList(builder, "Attributes", n.Attributes);
                break;
            case ExportSpecifier n:
                builder.Append("(ExportSpecifier");
                builder.Append(" Local=");
                Print(builder, n.Local);
                builder.Append(" Exported=");
                Print(builder, n.Exported);
                break;
            case ExportDefaultDeclaration n:
                builder.Append("(ExportDefaultDeclaration");
                builder.Append(" Declaration=");
                Print(builder, n.Declaration);
                break;
            case ExportAllDeclaration n:
                builder.Append("(ExportAllDeclaration");
                builder.Append(" Exported=");
                Print(builder, n.Exported);
                builder.Append(" Source=");
                Print(builder, n.Source);
                AppendList(builder, "Attributes", n.Attributes);
                break;
            case Identifier n:
                builder.Append("(Identifier");
                AppendString(builder, "Name", n.Name);
                break;
            case PrivateIdentifier n:
                builder.Append("(PrivateIdentifier");
                AppendString(builder, "Name", n.Name);
                break;
            case NullLiteral n:
                builder.Append("(NullLiteral");
                break;
            case BooleanLiteral n:
                builder.Append("(BooleanLiteral");
                builder.Append(" Value=").Append(n.Value ? "true" : "false");
                break;
            case NumericLiteral n:
                builder.Append("(NumericLiteral");
                builder.Append(" Value=").Append(n.Value.ToString("R", CultureInfo.InvariantCulture));
                break;
            case BigIntLiteral n:
                builder.Append("(BigIntLiteral");
                builder.Append(" Value=").Append(n.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case StringLiteral n:
                builder.Append("(StringLiteral");
                AppendString(builder, "Value", n.Value);
                break;
            case RegExpLiteral n:
                builder.Append("(RegExpLiteral");
                AppendString(builder, "Pattern", n.Pattern);
                AppendString(builder, "Flags", n.Flags);
                break;
            case TemplateLiteral n:
                builder.Append("(TemplateLiteral");
                AppendList(builder, "Quasis", n.Quasis);
                AppendList(builder, "Expressions", n.Expressions);
                break;
            case TemplateElement n:
                builder.Append("(TemplateElement");
                AppendString(builder, "Cooked", n.Cooked);
                AppendString(builder, "Raw", n.Raw);
                builder.Append(" Tail=").Append(n.Tail ? "true" : "false");
                break;
            case TaggedTemplateExpression n:
                builder.Append("(TaggedTemplateExpression");
                builder.Append(" Tag=");
                Print(builder, n.Tag);
                builder.Append(" Quasi=");
                Print(builder, n.Quasi);
                break;
            case ThisExpression n:
                builder.Append("(ThisExpression");
                break;
            case Super n:
                builder.Append("(Super");
                break;
            case ArrayExpression n:
                builder.Append("(ArrayExpression");
                AppendList(builder, "Elements", n.Elements);
                break;
            case ObjectExpression n:
                builder.Append("(ObjectExpression");
                AppendList(builder, "Properties", n.Properties);
                break;
            case Property n:
                builder.Append("(Property");
                builder.Append(" Key=");
                Print(builder, n.Key);
                builder.Append(" Value=");
                Print(builder, n.Value);
                builder.Append(" Kind=").Append(n.Kind.ToString());
                builder.Append(" Computed=").Append(n.Computed ? "true" : "false");
                builder.Append(" Method=").Append(n.Method ? "true" : "false");
                builder.Append(" Shorthand=").Append(n.Shorthand ? "true" : "false");
                break;
            case SpreadElement n:
                builder.Append("(SpreadElement");
                builder.Append(" Argument=");
                Print(builder, n.Argument);
                break;
            case FunctionExpression n:
                builder.Append("(FunctionExpression");
                builder.Append(" Function=");
                Print(builder, n.Function);
                break;
            case ArrowFunctionExpression n:
                builder.Append("(ArrowFunctionExpression");
                builder.Append(" Function=");
                Print(builder, n.Function);
                break;
            case ClassExpression n:
                builder.Append("(ClassExpression");
                builder.Append(" Class=");
                Print(builder, n.Class);
                break;
            case UnaryExpression n:
                builder.Append("(UnaryExpression");
                builder.Append(" Operator=").Append(n.Operator.ToString());
                builder.Append(" Argument=");
                Print(builder, n.Argument);
                break;
            case UpdateExpression n:
                builder.Append("(UpdateExpression");
                builder.Append(" Operator=").Append(n.Operator.ToString());
                builder.Append(" Prefix=").Append(n.Prefix ? "true" : "false");
                builder.Append(" Argument=");
                Print(builder, n.Argument);
                break;
            case BinaryExpression n:
                builder.Append("(BinaryExpression");
                builder.Append(" Operator=").Append(n.Operator.ToString());
                builder.Append(" Left=");
                Print(builder, n.Left);
                builder.Append(" Right=");
                Print(builder, n.Right);
                break;
            case LogicalExpression n:
                builder.Append("(LogicalExpression");
                builder.Append(" Operator=").Append(n.Operator.ToString());
                builder.Append(" Left=");
                Print(builder, n.Left);
                builder.Append(" Right=");
                Print(builder, n.Right);
                break;
            case AssignmentExpression n:
                builder.Append("(AssignmentExpression");
                builder.Append(" Operator=").Append(n.Operator.ToString());
                builder.Append(" Left=");
                Print(builder, n.Left);
                builder.Append(" Right=");
                Print(builder, n.Right);
                break;
            case ConditionalExpression n:
                builder.Append("(ConditionalExpression");
                builder.Append(" Test=");
                Print(builder, n.Test);
                builder.Append(" Consequent=");
                Print(builder, n.Consequent);
                builder.Append(" Alternate=");
                Print(builder, n.Alternate);
                break;
            case CallExpression n:
                builder.Append("(CallExpression");
                builder.Append(" Callee=");
                Print(builder, n.Callee);
                AppendList(builder, "Arguments", n.Arguments);
                builder.Append(" Optional=").Append(n.Optional ? "true" : "false");
                break;
            case NewExpression n:
                builder.Append("(NewExpression");
                builder.Append(" Callee=");
                Print(builder, n.Callee);
                AppendList(builder, "Arguments", n.Arguments);
                break;
            case MemberExpression n:
                builder.Append("(MemberExpression");
                builder.Append(" Object=");
                Print(builder, n.Object);
                builder.Append(" Property=");
                Print(builder, n.Property);
                builder.Append(" Computed=").Append(n.Computed ? "true" : "false");
                builder.Append(" Optional=").Append(n.Optional ? "true" : "false");
                break;
            case ChainExpression n:
                builder.Append("(ChainExpression");
                builder.Append(" Expression=");
                Print(builder, n.Expression);
                break;
            case SequenceExpression n:
                builder.Append("(SequenceExpression");
                AppendList(builder, "Expressions", n.Expressions);
                break;
            case YieldExpression n:
                builder.Append("(YieldExpression");
                builder.Append(" Argument=");
                Print(builder, n.Argument);
                builder.Append(" Delegate=").Append(n.Delegate ? "true" : "false");
                break;
            case AwaitExpression n:
                builder.Append("(AwaitExpression");
                builder.Append(" Argument=");
                Print(builder, n.Argument);
                break;
            case MetaProperty n:
                builder.Append("(MetaProperty");
                builder.Append(" Kind=").Append(n.Kind.ToString());
                break;
            case ImportExpression n:
                builder.Append("(ImportExpression");
                builder.Append(" Source=");
                Print(builder, n.Source);
                builder.Append(" Options=");
                Print(builder, n.Options);
                break;
            case ObjectPattern n:
                builder.Append("(ObjectPattern");
                AppendList(builder, "Properties", n.Properties);
                break;
            case ArrayPattern n:
                builder.Append("(ArrayPattern");
                AppendList(builder, "Elements", n.Elements);
                break;
            case AssignmentPattern n:
                builder.Append("(AssignmentPattern");
                builder.Append(" Left=");
                Print(builder, n.Left);
                builder.Append(" Right=");
                Print(builder, n.Right);
                break;
            case RestElement n:
                builder.Append("(RestElement");
                builder.Append(" Argument=");
                Print(builder, n.Argument);
                break;
            case ClassNode n:
                builder.Append("(ClassNode");
                builder.Append(" Id=");
                Print(builder, n.Id);
                builder.Append(" SuperClass=");
                Print(builder, n.SuperClass);
                AppendList(builder, "Body", n.Body);
                break;
            case MethodDefinition n:
                builder.Append("(MethodDefinition");
                builder.Append(" Key=");
                Print(builder, n.Key);
                builder.Append(" Value=");
                Print(builder, n.Value);
                builder.Append(" Kind=").Append(n.Kind.ToString());
                builder.Append(" Static=").Append(n.Static ? "true" : "false");
                builder.Append(" Computed=").Append(n.Computed ? "true" : "false");
                break;
            case PropertyDefinition n:
                builder.Append("(PropertyDefinition");
                builder.Append(" Key=");
                Print(builder, n.Key);
                builder.Append(" Value=");
                Print(builder, n.Value);
                builder.Append(" Static=").Append(n.Static ? "true" : "false");
                builder.Append(" Computed=").Append(n.Computed ? "true" : "false");
                break;
            case StaticBlock n:
                builder.Append("(StaticBlock");
                AppendList(builder, "Body", n.Body);
                break;
            case FunctionNode n:
                builder.Append("(FunctionNode");
                builder.Append(" Id=");
                Print(builder, n.Id);
                AppendList(builder, "Params", n.Params);
                builder.Append(" Body=");
                Print(builder, n.Body);
                builder.Append(" Kind=").Append(n.Kind.ToString());
                builder.Append(" IsAsync=").Append(n.IsAsync ? "true" : "false");
                builder.Append(" IsGenerator=").Append(n.IsGenerator ? "true" : "false");
                builder.Append(" IsExpressionBody=").Append(n.IsExpressionBody ? "true" : "false");
                builder.Append(" IsStrict=").Append(n.IsStrict ? "true" : "false");
                builder.Append(" HasSimpleParameterList=").Append(n.HasSimpleParameterList ? "true" : "false");
                builder.Append(" IsDerivedConstructor=").Append(n.IsDerivedConstructor ? "true" : "false");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(node), node.Type, "Unknown node type");
        }

        if (node is not null)
        {
            builder.Append(')');
        }
    }
}
