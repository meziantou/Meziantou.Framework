namespace Meziantou.Framework.CodeDom;

/// <summary>Base class for all expressions (literals, operators, method calls, etc.).</summary>
/// <example>
/// <code>
/// Expression expr1 = new LiteralExpression(42);
/// Expression expr2 = new BinaryExpression(BinaryOperator.Add, expr1, 10);
/// Expression expr3 = new MethodInvokeExpression(new MemberReferenceExpression(typeof(Console), "WriteLine"), expr2);
/// </code>
/// </example>
public abstract class Expression : CodeObject, ICommentable
{
    public CommentCollection CommentsAfter { get; }
    public CommentCollection CommentsBefore { get; }

    protected Expression()
    {
        CommentsBefore = new CommentCollection(this, CommentType.InlineComment);
        CommentsAfter = new CommentCollection(this, CommentType.InlineComment);
    }

    public static implicit operator Expression(MemberDeclaration memberDeclaration) => new MemberReferenceExpression(memberDeclaration);

    public static implicit operator Expression(Enum value)
    {
        var type = value.GetType();
        if (Enum.IsDefined(type, value))
        {
            var name = value.ToString();
            return new MemberReferenceExpression(new TypeReference(type), name);
        }
        else
        {
            var underlyingType = Enum.GetUnderlyingType(type);
            var typedValue = Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
            return new CastExpression(new LiteralExpression(typedValue), new TypeReference(type));
        }
    }

    [return: NotNullIfNotNull(nameof(variableDeclarationStatement))]
    public static implicit operator Expression?(VariableDeclarationStatement? variableDeclarationStatement)
    {
        if (variableDeclarationStatement is null)
            return null;

        return new VariableReferenceExpression(variableDeclarationStatement);
    }

    [return: NotNullIfNotNull(nameof(argument))]
    public static implicit operator Expression?(MethodArgumentDeclaration? argument)
    {
        if (argument is null)
            return null;

        return new ArgumentReferenceExpression(argument);
    }

    public static implicit operator Expression(TypeReference typeReference) => new TypeReferenceExpression(typeReference);

    public static implicit operator Expression(byte value) => new LiteralExpression(value);
    public static implicit operator Expression(sbyte value) => new LiteralExpression(value);
    public static implicit operator Expression(short value) => new LiteralExpression(value);
    public static implicit operator Expression(ushort value) => new LiteralExpression(value);
    public static implicit operator Expression(int value) => new LiteralExpression(value);
    public static implicit operator Expression(uint value) => new LiteralExpression(value);
    public static implicit operator Expression(long value) => new LiteralExpression(value);
    public static implicit operator Expression(ulong value) => new LiteralExpression(value);
    public static implicit operator Expression(float value) => new LiteralExpression(value);
    public static implicit operator Expression(double value) => new LiteralExpression(value);
    public static implicit operator Expression(decimal value) => new LiteralExpression(value);
    public static implicit operator Expression(string value) => new LiteralExpression(value);
    public static implicit operator Expression(bool value) => new LiteralExpression(value);

    /// <summary>Creates a null literal expression.</summary>
    public static LiteralExpression Null() => new(value: null);

    /// <summary>Creates a true literal expression.</summary>
    public static LiteralExpression True() => new(value: true);

    /// <summary>Creates a false literal expression.</summary>
    public static LiteralExpression False() => new(value: false);

    /// <summary>Creates a member reference expression for a static member.</summary>
    /// <param name="type">The type containing the member.</param>
    /// <param name="name">The member name.</param>
    /// <param name="names">Additional nested member names.</param>
    public static MemberReferenceExpression Member(Type type, string name, params string[] names) => new TypeReferenceExpression(type).Member(name, names);

    public static BinaryExpression ReferenceEqualsNull(Expression expression) => new(BinaryOperator.Equals, new TypeReferenceExpression(typeof(object)).Member(nameof(object.ReferenceEquals)).InvokeMethod(expression, Null()), new LiteralExpression(value: true));
    public static MethodInvokeExpression IsNullOrEmpty(Expression expression) => new TypeReferenceExpression(typeof(string)).InvokeMethod(nameof(string.IsNullOrEmpty), expression);
    public static MethodInvokeExpression IsNullOrWhitespace(Expression expression) => new TypeReferenceExpression(typeof(string)).InvokeMethod(nameof(string.IsNullOrWhiteSpace), expression);
    public static BinaryExpression EqualsNull(Expression expr) => new(BinaryOperator.Equals, expr, Null());
    public static BinaryExpression NotEqualsNull(Expression expr) => new(BinaryOperator.NotEquals, leftExpression: expr, Null());

    public static BinaryExpression Add(Expression expr1, Expression expr2, params Expression[] expressions) => Create(BinaryOperator.Add, expr1, expr2, expressions);
    public static BinaryExpression And(Expression expr1, Expression expr2, params Expression[] expressions) => Create(BinaryOperator.And, expr1, expr2, expressions);
    public static BinaryExpression Or(Expression expr1, Expression expr2, params Expression[] expressions) => Create(BinaryOperator.Or, expr1, expr2, expressions);

    private static BinaryExpression Create(BinaryOperator op, Expression expr1, Expression expr2, params Expression[] expressions)
    {
        var result = new BinaryExpression(op, expr1, expr2);
        foreach (var expr in expressions)
        {
            result = new BinaryExpression(op, result, expr);
        }

        return result;
    }
}
