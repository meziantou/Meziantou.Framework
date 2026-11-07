using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meziantou.Framework.Tests;

// Pins the overload each delegate shape binds to for the exception and event assertions. Adding an overload to these
// method groups can silently change the binding of existing call sites (for instance an async lambda converting to both
// Func<Task> and Func<ValueTask>).
public sealed class ExceptionAssertionOverloadResolutionTests : AssertionsAnalyzerTestBase
{
    private static readonly string[] Shapes =
    [
        "() => { }",
        "() => { throw new InvalidOperationException(); }",
        "() => throw new InvalidOperationException()",
        "() => VoidMethod()",
        "() => IntMethod()",
        "() => ObjectMethod()",
        "() => TaskMethod()",
        "() => TaskIntMethod()",
        "() => TaskObjectMethod()",
        "() => ValueTaskMethod()",
        "() => ValueTaskIntMethod()",
        "() => ValueTaskObjectMethod()",
        "async () => { await Task.Yield(); }",
        "async () => { await Task.Yield(); throw new InvalidOperationException(); }",
        "async () => await TaskMethod()",
        "async () => await ValueTaskMethod()",
        "async () => await TaskIntMethod()",
        "async () => await ValueTaskIntMethod()",
        "VoidMethod",
        "IntMethod",
        "ObjectMethod",
        "TaskMethod",
        "TaskIntMethod",
        "TaskObjectMethod",
        "ValueTaskMethod",
        "ValueTaskIntMethod",
        "ValueTaskObjectMethod",
        "actionVariable",
        "funcTaskVariable",
        "funcValueTaskVariable",
    ];

    [Fact]
    public async Task Throws_BindsEachDelegateShapeToTheExpectedOverload()
    {
        var actual = await GetBindings(
            "Throws<InvalidOperationException>({0})",
            "Throws(typeof(InvalidOperationException), {0})",
            "ThrowsAny<InvalidOperationException>({0})",
            "ThrowsAny(typeof(InvalidOperationException), {0})",
            "Throws<ArgumentException>(\"paramName\", {0})");

        // A ValueTask<int> converts neither to ValueTask<object?> nor to Task, and the exception type argument cannot be
        // combined with an inferred result type argument, so "() => ValueTaskIntMethod()" still binds to Func<object?>
        Assert.Equal("""
            () => { } => Action
            () => { throw new InvalidOperationException(); } => Func<Task<object?>>
            () => throw new InvalidOperationException() => Func<Task<object?>>
            () => VoidMethod() => Action
            () => IntMethod() => Func<object?>
            () => ObjectMethod() => Func<object?>
            () => TaskMethod() => Func<Task>
            () => TaskIntMethod() => Func<Task>
            () => TaskObjectMethod() => Func<Task<object?>>
            () => ValueTaskMethod() => Func<ValueTask>
            () => ValueTaskIntMethod() => Func<object?>
            () => ValueTaskObjectMethod() => Func<ValueTask<object?>>
            async () => { await Task.Yield(); } => Func<Task>
            async () => { await Task.Yield(); throw new InvalidOperationException(); } => Func<Task>
            async () => await TaskMethod() => Func<Task>
            async () => await ValueTaskMethod() => Func<Task>
            async () => await TaskIntMethod() => Func<Task<object?>>
            async () => await ValueTaskIntMethod() => Func<Task<object?>>
            VoidMethod => Action
            IntMethod => compilation error
            ObjectMethod => Func<object?>
            TaskMethod => Func<Task>
            TaskIntMethod => Func<Task>
            TaskObjectMethod => Func<Task<object?>>
            ValueTaskMethod => Func<ValueTask>
            ValueTaskIntMethod => compilation error
            ValueTaskObjectMethod => Func<ValueTask<object?>>
            actionVariable => Action
            funcTaskVariable => Func<Task>
            funcValueTaskVariable => Func<ValueTask>
            """, actual, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public async Task DoesNotThrow_BindsEachDelegateShapeToTheExpectedOverload()
    {
        var actual = await GetBindings(
            "DoesNotThrow({0})",
            "DoesNotThrow<InvalidOperationException>({0})",
            "DoesNotThrow(typeof(InvalidOperationException), {0})",
            "DoesNotThrowAny<InvalidOperationException>({0})",
            "DoesNotThrowAny(typeof(InvalidOperationException), {0})");

        // A ValueTask<T> converts neither to ValueTask nor to Task, so "() => ValueTaskIntMethod()" binds to Func<object?> and
        // the ValueTask is never awaited (MFAS0057 reports it)
        Assert.Equal("""
            () => { } => Action
            () => { throw new InvalidOperationException(); } => Func<Task>
            () => throw new InvalidOperationException() => Func<Task>
            () => VoidMethod() => Action
            () => IntMethod() => Func<object?>
            () => ObjectMethod() => Func<object?>
            () => TaskMethod() => Func<Task>
            () => TaskIntMethod() => Func<Task>
            () => TaskObjectMethod() => Func<Task>
            () => ValueTaskMethod() => Func<ValueTask>
            () => ValueTaskIntMethod() => Func<object?>
            () => ValueTaskObjectMethod() => Func<object?>
            async () => { await Task.Yield(); } => Func<Task>
            async () => { await Task.Yield(); throw new InvalidOperationException(); } => Func<Task>
            async () => await TaskMethod() => Func<Task>
            async () => await ValueTaskMethod() => Func<Task>
            async () => await TaskIntMethod() => Func<Task>
            async () => await ValueTaskIntMethod() => Func<Task>
            VoidMethod => Action
            IntMethod => compilation error
            ObjectMethod => Func<object?>
            TaskMethod => Func<Task>
            TaskIntMethod => Func<Task>
            TaskObjectMethod => Func<Task>
            ValueTaskMethod => Func<ValueTask>
            ValueTaskIntMethod => compilation error
            ValueTaskObjectMethod => compilation error
            actionVariable => Action
            funcTaskVariable => Func<Task>
            funcValueTaskVariable => Func<ValueTask>
            """, actual, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public async Task EventAssertions_BindEachDelegateShapeToTheExpectedOverload()
    {
        var actual = await GetBindings(
            "Raise(handler => Changed += handler, handler => Changed -= handler, {0})",
            "Raise<EventArgs>(handler => GenericChanged += handler, handler => GenericChanged -= handler, {0})",
            "RaiseAny(handler => Changed += handler, handler => Changed -= handler, {0})",
            "RaiseAny<EventArgs>(handler => GenericChanged += handler, handler => GenericChanged -= handler, {0})",
            "DoesNotRaise(handler => Changed += handler, handler => Changed -= handler, {0})",
            "DoesNotRaise<EventArgs>(handler => GenericChanged += handler, handler => GenericChanged -= handler, {0})",
            "DoesNotRaiseAny(handler => Changed += handler, handler => Changed -= handler, {0})",
            "DoesNotRaiseAny<EventArgs>(handler => GenericChanged += handler, handler => GenericChanged -= handler, {0})");

        // A ValueTask<T> converts neither to ValueTask nor to Task, so "() => ValueTaskIntMethod()" binds to Action and the
        // ValueTask is never awaited (MFAS0057 reports it)
        Assert.Equal("""
            () => { } => Action
            () => { throw new InvalidOperationException(); } => Func<Task>
            () => throw new InvalidOperationException() => Func<Task>
            () => VoidMethod() => Action
            () => IntMethod() => Action
            () => ObjectMethod() => Action
            () => TaskMethod() => Func<Task>
            () => TaskIntMethod() => Func<Task>
            () => TaskObjectMethod() => Func<Task>
            () => ValueTaskMethod() => Func<ValueTask>
            () => ValueTaskIntMethod() => Action
            () => ValueTaskObjectMethod() => Action
            async () => { await Task.Yield(); } => Func<Task>
            async () => { await Task.Yield(); throw new InvalidOperationException(); } => Func<Task>
            async () => await TaskMethod() => Func<Task>
            async () => await ValueTaskMethod() => Func<Task>
            async () => await TaskIntMethod() => Func<Task>
            async () => await ValueTaskIntMethod() => Func<Task>
            VoidMethod => Action
            IntMethod => compilation error
            ObjectMethod => compilation error
            TaskMethod => Func<Task>
            TaskIntMethod => Func<Task>
            TaskObjectMethod => Func<Task>
            ValueTaskMethod => Func<ValueTask>
            ValueTaskIntMethod => compilation error
            ValueTaskObjectMethod => compilation error
            actionVariable => Action
            funcTaskVariable => Func<Task>
            funcValueTaskVariable => Func<ValueTask>
            """, actual, ignoreLineEndingDifferences: true);
    }

    // Every call of a family has the same overload structure, so they must all bind to the same delegate type
    private static async Task<string> GetBindings(params string[] callFormats)
    {
        var references = await Net11.ResolveAsync(LanguageNames.CSharp, XunitCancellationToken);
        var result = new StringBuilder();
        foreach (var shape in Shapes)
        {
            var bindings = new List<string>();
            foreach (var callFormat in callFormats)
            {
                bindings.Add(GetBinding(string.Format(CultureInfo.InvariantCulture, callFormat, shape), references));
            }

            var distinctBindings = bindings.Distinct(StringComparer.Ordinal).ToArray();
            if (result.Length > 0)
            {
                result.Append('\n');
            }

            result.Append(shape).Append(" => ").Append(distinctBindings.Length is 1 ? distinctBindings[0] : string.Join(" | ", bindings));
        }

        return result.ToString();
    }

    private static string GetBinding(string call, IEnumerable<MetadataReference> references)
    {
        var source = $$"""
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            public static class Sample
            {
                public static async Task M(Action actionVariable, Func<Task> funcTaskVariable, Func<ValueTask> funcValueTaskVariable)
                {
                    await Task.Yield();
                    Assert.{{call}};
                }

                private static event EventHandler? Changed;
                private static event EventHandler<EventArgs>? GenericChanged;

                private static void VoidMethod() { }
                private static int IntMethod() => 0;
                private static object ObjectMethod() => new();
                private static Task TaskMethod() => Task.CompletedTask;
                private static Task<int> TaskIntMethod() => Task.FromResult(0);
                private static Task<object?> TaskObjectMethod() => Task.FromResult<object?>(null);
                private static ValueTask ValueTaskMethod() => default;
                private static ValueTask<int> ValueTaskIntMethod() => default;
                private static ValueTask<object?> ValueTaskObjectMethod() => default;
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: XunitCancellationToken);
        var compilation = CSharpCompilation.Create(
            "Sample",
            [syntaxTree],
            [.. references, GetAssertionsMetadataReference()],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var invocation = syntaxTree.GetRoot(XunitCancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>()
            .First(node => node.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "Assert" } });

        var errors = compilation.GetDiagnostics(XunitCancellationToken)
            .Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length > 0)
            return "compilation error";

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var method = (IMethodSymbol)semanticModel.GetSymbolInfo(invocation, XunitCancellationToken).Symbol!;
        var delegateParameter = method.OriginalDefinition.Parameters.Single(parameter => parameter.Name is "action");
        return delegateParameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }
}
