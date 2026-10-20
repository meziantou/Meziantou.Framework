namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a namespace declaration containing types, nested namespaces, and using directives.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs" ref:region="NamespaceDeclaration_Example" ref:dedent="true">
/// var ns = new NamespaceDeclaration("MyNamespace");
/// ns.Usings.Add(new UsingDirective("System"));
/// ns.AddType(new ClassDeclaration("MyClass"));
/// var code = ns.ToCsharpString();
///
/// Assert.StartsWith("namespace MyNamespace", code);
/// Assert.Contains("using System;", code);
/// Assert.Contains("class MyClass", code);
/// </code>
/// </example>
public class NamespaceDeclaration : CodeObject, ITypeDeclarationContainer, INamespaceDeclarationContainer, IUsingDirectiveContainer, ICommentable
{
    public string? Name { get; set; }

    public CodeObjectCollection<TypeDeclaration> Types { get; }
    public CodeObjectCollection<UsingDirective> Usings { get; }
    public CodeObjectCollection<NamespaceDeclaration> Namespaces { get; }
    public CommentCollection CommentsBefore { get; }
    public CommentCollection CommentsAfter { get; }

    public NamespaceDeclaration()
        : this(name: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="NamespaceDeclaration"/> class with the specified name.</summary>
    /// <param name="name">The namespace name.</param>
    public NamespaceDeclaration(string? name)
    {
        Name = name;
        Types = new CodeObjectCollection<TypeDeclaration>(this);
        Usings = new CodeObjectCollection<UsingDirective>(this);
        Namespaces = new CodeObjectCollection<NamespaceDeclaration>(this);
        CommentsBefore = new CommentCollection(this);
        CommentsAfter = new CommentCollection(this);
    }
}
