---
title: Composing templates
description: Share a header, a footer or a layout between templates with partial templates, layouts and sections.
---

A template can render other templates:

- a **partial template** is rendered where the `render` directive is, with the arguments the directive passes to it;
- a **layout** wraps the template that declares it with the `layout` directive: the layout writes the shared text, and
  renders the body and the sections of the template where it chooses.

| Directive | Where | Effect |
| --- | --- | --- |
| `render name arguments` | Any template | Renders the partial template `name` |
| `parameter type name` | Partial, layout | Declares a parameter, like a C# parameter: `int Count = 1` |
| `layout name arguments` | Template, layout | Renders the template in the layout `name` |
| `renderbody` | Layout | Renders the body of the template |
| `section name` … `endsection` | Template, layout | Defines a section for the layout |
| `rendersection name` | Layout | Renders a section of the template; add `optional` when the template may not define it |

## Find the templates

The template finds the other templates with its [TemplateLoader](cref:Meziantou.Framework.Templating.Template.TemplateLoader). [InMemoryTemplateLoader](cref:Meziantou.Framework.Templating.InMemoryTemplateLoader) holds them in
memory, by name:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_RenderPartial;format=md-fenced-code-block;dedent=true -->
```csharp
var loader = new InMemoryTemplateLoader
{
    { "greeting", "Dear <%= Name %>," },
};

using var template = new Template { TemplateLoader = loader };
template.Arguments.Add(new TemplateArgument("Name", typeof(string)));
template.Load("<%@ render greeting %> your order has shipped.");

var result = template.Run("Meziantou");
Assert.Equal("Dear Meziantou, your order has shipped.", result);
```
<!-- endref -->

[FileSystemTemplateLoader](cref:Meziantou.Framework.Templating.FileSystemTemplateLoader) reads files. A name is a path relative to the directory of the template that contains
the directive, so set [SourceFileName](cref:Meziantou.Framework.Templating.Template.SourceFileName) to the path of the template you load, relative to the root directory of the
loader or absolute:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_FileSystemTemplateLoader;format=md-fenced-code-block;dedent=true -->
```csharp
// emails/welcome.txt renders emails/shared/footer.txt
var templatesDirectory = Directory.CreateTempSubdirectory("templates").FullName;
Directory.CreateDirectory(Path.Combine(templatesDirectory, "emails", "shared"));
File.WriteAllText(Path.Combine(templatesDirectory, "emails", "shared", "footer.txt"), "-- The team");
File.WriteAllText(Path.Combine(templatesDirectory, "emails", "welcome.txt"), "Welcome! <%@ render shared/footer.txt %>");

using var template = new Template
{
    TemplateLoader = new FileSystemTemplateLoader(templatesDirectory),
    SourceFileName = Path.Combine("emails", "welcome.txt"),
};
template.Load(File.ReadAllText(Path.Combine(templatesDirectory, template.SourceFileName)));

var result = template.Run();
Assert.Equal("Welcome! -- The team", result);
```
<!-- endref -->

To load templates from somewhere else, such as a database or the resources of an assembly, derive from
[TemplateLoader](cref:Meziantou.Framework.Templating.TemplateLoader): [GetText](cref:Meziantou.Framework.Templating.TemplateLoader.GetText) returns the text of a template, or `null` when it does not exist, and
[GetFullName](cref:Meziantou.Framework.Templating.TemplateLoader.GetFullName) turns the name written in a directive into the name that identifies the template, for instance to
resolve relative names.

A name that contains spaces is written between double quotes: `<%@ render "order line" %>`.

## Render a partial template

A partial template can use the arguments of the template that renders it, as `Name` above. To give it values of its
own, declare its parameters with the `parameter` directive, and pass the values after the name in the `render`
directive. The parameters and the arguments are written in C#: the arguments can be named or positional, and a
parameter can have a default value:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_RenderPartialWithParameters;format=md-fenced-code-block;dedent=true -->
```csharp
var loader = new InMemoryTemplateLoader
{
    {
        "line", """
        <%@ parameter string Product %>
        <%@ parameter int Quantity = 1 %>
        - <%= Product %> x<%= Quantity %>

        """
    },
};

using var template = new Template { TemplateLoader = loader };
template.Load("""
    Order:
    <%@ render line Product: "Apples", Quantity: 3 %>
    <%@ render line Product: "Pears" %>
    """);

var result = template.Run();
Assert.Equal("""
    Order:
    - Apples x3
    - Pears x1

    """, result);
```
<!-- endref -->

A partial template is a method: it does not see the local variables of the template that renders it, only its
parameters and the arguments of the template. The types of the parameters follow the rules of the
[types of the arguments](04-PassingArguments.md#pass-values-of-your-own-types): reference their assembly, and write their
full name or import their namespace.

A partial template can render other partial templates, and itself. Rendering a tree is a partial template that renders
itself for each child:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_MenuItemType;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed record MenuItem(string Title, IReadOnlyList<MenuItem> Children);
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_RenderRecursivePartial;format=md-fenced-code-block;dedent=true -->
```csharp
var loader = new InMemoryTemplateLoader
{
    {
        "menu-item", """
        <%@ parameter Meziantou.Framework.Templating.Tests.MenuItem Item %>
        <%@ parameter int Depth %>
        <%= new string(' ', Depth * 2) %>- <%= Item.Title %>
        <% foreach (var child in Item.Children) { %>
        <%@ render menu-item Item: child, Depth: Depth + 1 %>
        <% } %>
        """
    },
};

using var template = new Template { TemplateLoader = loader };
template.AssemblyReferences.Add(typeof(MenuItem));
template.Arguments.Add(new TemplateArgument("Menu", typeof(MenuItem)));
template.Load("<%@ render menu-item Item: Menu, Depth: 0 %>");

var menu = new MenuItem("Home", [new MenuItem("Products", [new MenuItem("Laptops", [])]), new MenuItem("Contact", [])]);
var result = template.Run(menu);
Assert.Equal("""
    - Home
      - Products
        - Laptops
      - Contact

    """, result);
```
<!-- endref -->

The recursion stops when your code stops rendering, here when an item has no children: like a recursive method, a
partial template that always renders itself never ends.

## Use a layout

The `layout` directive renders the template in a layout. The layout writes its own text, renders the body of the
template with `renderbody`, and renders its sections with `rendersection`. The body is everything the template writes
outside its sections:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_Layout;format=md-fenced-code-block;dedent=true -->
```csharp
var loader = new InMemoryTemplateLoader
{
    {
        "layout", """
        <html>
        <head><title><%@ rendersection title %></title></head>
        <body>
        <%@ renderbody %>
        <%@ rendersection footer optional %>
        </body>
        </html>
        """
    },
};

using var template = new Template { TemplateLoader = loader };
template.Load("""
    <%@ layout layout %>
    <p>Hello!</p>
    <%@ section title %>Welcome<%@ endsection %>
    """);

var result = template.Run();
Assert.Equal("""
    <html>
    <head><title>Welcome</title></head>
    <body>
    <p>Hello!</p>
    </body>
    </html>
    """, result);
```
<!-- endref -->

- A section is required, unless the `rendersection` directive ends with `optional`: building a template that does not
  define a required section throws a [TemplateException](cref:Meziantou.Framework.Templating.TemplateException). So does a section that the layout never renders, which is
  often a typo in its name.
- The layout renders the body and the sections where its directives are, while it runs: the body does not run before the
  layout. To give a value to the layout, such as the title of the page, use a section or a parameter of the layout.
- The body and each section are separate methods: a section cannot use a local variable declared in the body.
- A layout can have a layout. Its sections are rendered by its own layout, and its sections can render the sections of
  the template.

A layout declares parameters like a partial template, and the `layout` directive passes their values:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_LayoutWithParameters;format=md-fenced-code-block;dedent=true -->
```csharp
var loader = new InMemoryTemplateLoader
{
    { "layout", "<%@ parameter string Title %><h1><%= Title %></h1><%@ renderbody %>" },
};

using var template = new Template { TemplateLoader = loader };
template.Arguments.Add(new TemplateArgument("UserName", typeof(string)));
template.Load("""<%@ layout layout Title: "Welcome " + UserName %><p>Your account is ready.</p>""");

var result = template.Run("Meziantou");
Assert.Equal("<h1>Welcome Meziantou</h1><p>Your account is ready.</p>", result);
```
<!-- endref -->

## HTML emails

The partial templates and the layouts of an [HtmlEmailTemplate](cref:Meziantou.Framework.Templating.HtmlEmailTemplate) are HTML email templates too: they use its `{{ }}`
delimiters and its encoding blocks, and a layout can capture the subject from a section of the template:

<!-- ref:../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_LayoutAndPartial;format=md-fenced-code-block;dedent=true -->
```csharp
var loader = new InMemoryTemplateLoader
{
    { "layout", "<html><head><title>{{@begin_section title}}{{@rendersection title}}{{@end_section}}</title></head><body>{{@renderbody}}{{@render footer}}</body></html>" },
    { "footer", "<p>Sent to {{#html email}}</p>" },
};

using var template = new HtmlEmailTemplate { TemplateLoader = loader };
template.Arguments.Add(new TemplateArgument("email", typeof(string)));
template.Load("{{@layout layout}}<p>Welcome!</p>{{@section title}}Fish & Chips{{@endsection}}");

var result = template.Run(out var metadata, new Dictionary<string, object?>(StringComparer.Ordinal) { ["email"] = "<john@example.com>" });
Assert.Equal("<html><head><title>Fish & Chips</title></head><body><p>Welcome!</p><p>Sent to &lt;john@example.com&gt;</p></body></html>", result);
Assert.Equal("Fish & Chips", metadata?.Title);
```
<!-- endref -->

More generally, the other templates are read with the delimiters and the block types of the template that renders them.

## Compilation

The partial templates and the layouts are compiled with the template, into its class: the loader is called when the
template is built, once for each template it renders, and a partial template rendered several times is compiled once.
Their directives apply to the whole class: a `using` directive of a partial template imports the namespace for the
template too.

Each template that renders a partial template compiles its own copy, so a change to a partial template is only seen by
the templates built after it. Like for any template, keep the built [Template](cref:Meziantou.Framework.Templating.Template) instances and run them many times; see
[Compilation and memory](08-CompilationAndMemory.md).

## Errors

The compiler errors in a partial template or a layout give its full name (its path, with [FileSystemTemplateLoader](cref:Meziantou.Framework.Templating.FileSystemTemplateLoader)),
and the line and the column in it. An error in the arguments of a `render` or a `layout` directive gives the position of
the argument.

A template that cannot be found, a layout that renders itself through its layouts, or a directive used where it is not
allowed (sections in a partial template, `renderbody` outside of a layout, `parameter` in the template being built)
makes the build throw a [TemplateException](cref:Meziantou.Framework.Templating.TemplateException) that gives the position of the directive:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_MissingPartial;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template
{
    TemplateLoader = new InMemoryTemplateLoader(),
    SourceFileName = "order.txt",
};
template.Load("Order:\n<%@ render header %>");

var exception = Assert.Throws<TemplateException>(() => template.Build(CancellationToken.None));
Assert.Equal("order.txt(2,4): The template 'header' was not found.", exception.Message);
```
<!-- endref -->
