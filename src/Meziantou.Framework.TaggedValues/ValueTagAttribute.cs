namespace Meziantou.Framework;

/// <summary>
/// Tags a value so the Meziantou.Framework.TaggedValues analyzer can report when values with different tags are mixed,
/// for instance when an order id is compared with a project id.
/// </summary>
/// <remarks>
/// <para>
/// On a field, a property, a parameter, or a return value (<c>[return: ValueTag("OrderId")]</c>), the tags describe the
/// value. When the declared type is a collection, a <see cref="Nullable{T}"/>, a <see cref="System.Threading.Tasks.Task{TResult}"/>
/// or a <c>ValueTask&lt;TResult&gt;</c>, the tags describe the element or the result. Use
/// <see cref="Key"/> and <see cref="Value"/> for dictionaries.
/// </para>
/// <para>
/// Several tags declare a union: the value is compatible with any value that shares at least one tag.
/// </para>
/// <para>
/// On the assembly, <see cref="ValueTagAttribute(Type, string, string[])"/> tags a property or a field of a type you do not own,
/// for instance <c>[assembly: ValueTag(typeof(Process), nameof(Process.Id), "ProcessId")]</c>.
/// </para>
/// <para>
/// Local variables cannot have attributes. Use a comment instead: <c>Guid /* ValueTag=OrderId */ id = ...;</c>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.ReturnValue | AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class ValueTagAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="ValueTagAttribute"/> class.</summary>
    /// <param name="tags">The tags of the value. Several tags declare a union.</param>
    public ValueTagAttribute(params string[] tags)
    {
        Tags = tags;
    }

    /// <summary>Initializes a new instance of the <see cref="ValueTagAttribute"/> class that tags a member of another type.</summary>
    /// <param name="type">The type that declares the member. The tags also apply to the member when it is accessed through a derived type.</param>
    /// <param name="memberName">The name of the property or the field to tag.</param>
    /// <param name="tags">The tags of the value. Several tags declare a union.</param>
    public ValueTagAttribute(Type type, string memberName, params string[] tags)
    {
        Type = type;
        MemberName = memberName;
        Tags = tags;
    }

    /// <summary>Gets the tags of the value.</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>Gets the type that declares the tagged member, when the attribute is applied to the assembly.</summary>
    public Type? Type { get; }

    /// <summary>Gets the name of the tagged member, when the attribute is applied to the assembly.</summary>
    public string? MemberName { get; }

    /// <summary>Gets or sets the tag of the keys of a dictionary.</summary>
    public string? Key { get; set; }

    /// <summary>Gets or sets the tag of the values of a dictionary.</summary>
    public string? Value { get; set; }
}
