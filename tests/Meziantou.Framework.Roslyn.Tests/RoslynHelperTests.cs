#nullable enable
using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Meziantou.Framework.Roslyn;

namespace Meziantou.Framework.Roslyn.Tests;

public sealed class RoslynHelperTests
{
    private static readonly CSharpParseOptions DefaultParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);
    private static readonly CSharpCompilationOptions DefaultCompilationOptions = new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);

    [Fact]
    public void IsNet9OrGreater_ReturnsResultFromCoreAssemblyVersion()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);

        Assert.Equal(typeof(object).Assembly.GetName().Version!.Major >= 9, compilation.IsNet9OrGreater());
    }

    [Fact]
    public void GetBestTypeByMetadataName_ReturnsSourceTypeBeforeReferencedType()
    {
        var referenceCompilation = CreateCompilation("""
            public class Sample;
            """, assemblyName: "Reference");
        var compilation = CreateCompilation("""
            public class Sample;
            """, additionalReferences: [referenceCompilation.ToMetadataReference()]);

        var type = compilation.GetBestTypeByMetadataName("Sample");

        Assert.True(SymbolEqualityComparer.Default.Equals(compilation.GetTypeByMetadataName("Sample"), type));
    }

    [Fact]
    public void GetBestTypeByMetadataName_ReturnsNullForAmbiguousReferencedTypes()
    {
        var referenceCompilation1 = CreateCompilation("""
            public class Sample;
            """, assemblyName: "Reference1");
        var referenceCompilation2 = CreateCompilation("""
            public class Sample;
            """, assemblyName: "Reference2");
        var compilation = CreateCompilation("""
            public class Consumer;
            """, additionalReferences: [referenceCompilation1.ToMetadataReference(), referenceCompilation2.ToMetadataReference()]);

        var type = compilation.GetBestTypeByMetadataName("Sample");

        Assert.Null(type);
    }

    [Fact]
    public void GetCSharpLanguageVersion_FromOperation_ReturnsParseOptionsLanguageVersion()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M()
                {
                    var value = 1;
                }
            }
            """, parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11));
        var semanticModel = GetSemanticModel(compilation);
        var operation = GetInitializerOperation(semanticModel, "value");

        Assert.Equal(LanguageVersion.CSharp11, operation.GetCSharpLanguageVersion());
    }

    [Fact]
    public void GetCSharpLanguageVersion_FromSyntaxNode_ReturnsParseOptionsLanguageVersion()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
            }
            """, parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp10));
        var typeDeclaration = compilation.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();

        Assert.Equal(LanguageVersion.CSharp10, typeDeclaration.GetCSharpLanguageVersion());
    }

    [Fact]
    public void GetCSharpLanguageVersion_FromSyntaxTree_ReturnsParseOptionsLanguageVersion()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
            }
            """, parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
        var syntaxTree = compilation.SyntaxTrees.Single();

        Assert.Equal(LanguageVersion.CSharp9, syntaxTree.GetCSharpLanguageVersion());
    }

    [Fact]
    public void GetCSharpLanguageVersion_FromCompilation_ReturnsFirstSyntaxTreeLanguageVersion()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
            }
            """, parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp8));

        Assert.Equal(LanguageVersion.CSharp8, compilation.GetCSharpLanguageVersion());
    }

    [Fact]
    public void IsCSharp8OrGreater_ReturnsTrueOnlyForCSharp8AndLater()
    {
        Assert.False(LanguageVersion.CSharp7_3.IsCSharp8OrGreater());
        Assert.True(LanguageVersion.CSharp8.IsCSharp8OrGreater());
    }

    [Fact]
    public void IsCSharp9OrGreater_ReturnsTrueOnlyForCSharp9AndLater()
    {
        Assert.False(LanguageVersion.CSharp8.IsCSharp9OrGreater());
        Assert.True(LanguageVersion.CSharp9.IsCSharp9OrGreater());
    }

    [Fact]
    public void IsCSharp10OrGreater_ReturnsTrueOnlyForCSharp10AndLater()
    {
        Assert.False(LanguageVersion.CSharp9.IsCSharp10OrGreater());
        Assert.True(LanguageVersion.CSharp10.IsCSharp10OrGreater());
    }

    [Fact]
    public void IsCSharp11OrGreater_ReturnsTrueOnlyForCSharp11AndLater()
    {
        Assert.False(LanguageVersion.CSharp10.IsCSharp11OrGreater());
        Assert.True(LanguageVersion.CSharp11.IsCSharp11OrGreater());
    }

    [Fact]
    public void IsCSharp12OrGreater_ReturnsTrueOnlyForCSharp12AndLater()
    {
        Assert.False(LanguageVersion.CSharp11.IsCSharp12OrGreater());
        Assert.True(LanguageVersion.CSharp12.IsCSharp12OrGreater());
    }

    [Fact]
    public void IsCSharp13OrGreater_ReturnsTrueOnlyForCSharp13AndLater()
    {
        Assert.False(LanguageVersion.CSharp12.IsCSharp13OrGreater());
        Assert.True(((LanguageVersion)1300).IsCSharp13OrGreater());
    }

    [Fact]
    public void IsCSharp14OrGreater_ReturnsTrueOnlyForCSharp14AndLater()
    {
        Assert.False(((LanguageVersion)1300).IsCSharp14OrGreater());
        Assert.True(((LanguageVersion)1400).IsCSharp14OrGreater());
    }

    [Fact]
    public void IsCSharp15OrGreater_ReturnsValueBasedOnAvailableRoslynConstants()
    {
#if ROSLYN_5_6_OR_GREATER
        Assert.True(LanguageVersion.Preview.IsCSharp15OrGreater());
#else
        Assert.False(LanguageVersion.Preview.IsCSharp15OrGreater());
#endif
    }

    [Fact]
    public void IsNamespace_MatchesTheFullNamespaceChain()
    {
        var compilation = CreateCompilation("""
            namespace Demo.Inner;

            public class Sample;
            """);
        var type = GetRequiredType(compilation, "Demo.Inner.Sample");

        Assert.True(type.ContainingNamespace.MatchesNamespace(["Demo", "Inner"]));
        Assert.False(type.ContainingNamespace.MatchesNamespace(["Inner"]));
    }

    [Fact]
    public void TryFindNode_ReturnsTheNodeAtTheDiagnosticLocation()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M(int value)
                {
                }
            }
            """);
        var root = compilation.SyntaxTrees.Single().GetRoot();
        var parameter = root.DescendantNodes().OfType<ParameterSyntax>().Single();
        var descriptor = CreateDescriptor();
        var diagnostic = Diagnostic.Create(descriptor, parameter.GetLocation());

        Assert.Same(parameter, diagnostic.FindNode(default));
    }

    [Fact]
    public void ReportDiagnostic_IsAvailableAsDiagnosticReporterExtensionMethod()
    {
        var reportDiagnosticMethods = typeof(ContextExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static);

        Assert.Contains(reportDiagnosticMethods, method => method.Name == "ReportDiagnostic");
    }

    [Fact]
    public void GetActualType_FollowsLocalAssignments()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M()
                {
                    var value = 41;
                    value = 42;
                    object boxed = value;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.Equal(SpecialType.System_Int32, boxed.GetActualType(default)?.SpecialType);
    }

    [Fact]
    public void TryGetConstantValue_FollowsLocalAssignmentsAndMemberInitializers()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                private readonly object _field = 3;
                private object Property { get; } = "text";

                public void M()
                {
                    var value = 41;
                    value = 42;
                    object boxedLocal = value;
                    object boxedField = _field;
                    object boxedProperty = Property;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxedLocal = GetInitializerOperation(semanticModel, "boxedLocal");
        var boxedField = GetInitializerOperation(semanticModel, "boxedField");
        var boxedProperty = GetInitializerOperation(semanticModel, "boxedProperty");

        Assert.True(boxedLocal.TryGetConstantValue(out var localValue, default));
        Assert.Equal(42, localValue);
        Assert.True(boxedField.TryGetConstantValue(out var fieldValue, default));
        Assert.Equal(3, fieldValue);
        Assert.True(boxedProperty.TryGetConstantValue(out var propertyValue, default));
        Assert.Equal("text", propertyValue);
    }

    [Fact]
    public void GetActualType_WithoutDataFlowAnalysis_OnlyUnwrapsConversions()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M()
                {
                    object value = 42;
                    object boxed = value;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.Equal(SpecialType.System_Int32, boxed.GetActualType(useDataFlowAnalysis: true, default)?.SpecialType);
        Assert.Equal(SpecialType.System_Object, boxed.GetActualType(useDataFlowAnalysis: false, default)?.SpecialType);
    }

    [Fact]
    public void TryGetConstantValue_WithoutDataFlowAnalysis_OnlyUsesTheUnwrappedOperationValue()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M()
                {
                    var value = 42;
                    object boxed = value;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.True(boxed.TryGetConstantValue(useDataFlowAnalysis: true, out var flowValue, default));
        Assert.Equal(42, flowValue);
        Assert.False(boxed.TryGetConstantValue(useDataFlowAnalysis: false, out var unwrappedValue, default));
        Assert.Null(unwrappedValue);
    }

    [Fact]
    public void IsPrimaryConstructor_ReturnsTrueForPrimaryClassConstructor()
    {
        var compilation = CreateCompilation("""
            public class Customer(string name);
            """);
        var type = GetRequiredType(compilation, "Customer");
        var constructor = type.InstanceConstructors.Single(method => method.Parameters.Length == 1);

        Assert.True(constructor.IsPrimaryConstructor(default));
    }

    [Fact]
    public void IsInterfaceImplementation_ReturnsTrueForMethodsPropertiesAndEvents()
    {
        var compilation = CreateCompilation("""
            using System;

            public interface ISample
            {
                void M();
                string Property { get; }
                event EventHandler? Changed;
            }

            public class Sample : ISample
            {
                public void M() { }
                string ISample.Property => "";
                event EventHandler? ISample.Changed { add { } remove { } }
                public void Other() { }
            }
            """);
        var type = GetRequiredType(compilation, "Sample");
        var method = GetRequiredMethod(type, "M");
        var property = type.GetMembers().OfType<IPropertySymbol>().Single(symbol => symbol.ExplicitInterfaceImplementations.Length == 1);
        var @event = type.GetMembers().OfType<IEventSymbol>().Single(symbol => symbol.ExplicitInterfaceImplementations.Length == 1);
        var other = GetRequiredMethod(type, "Other");

        Assert.True(method.IsInterfaceImplementation());
        Assert.True(property.IsInterfaceImplementation());
        Assert.True(@event.IsInterfaceImplementation());
        Assert.False(other.IsInterfaceImplementation());
    }

    [Fact]
    public void GetImplementingInterfaceSymbol_ReturnsImplementedMethodPropertyAndEvent()
    {
        var compilation = CreateCompilation("""
            using System;

            public interface ISample
            {
                void M();
                string Property { get; }
                event EventHandler? Changed;
            }

            public class Sample : ISample
            {
                public void M() { }
                string ISample.Property => "";
                event EventHandler? ISample.Changed { add { } remove { } }
            }
            """);
        var type = GetRequiredType(compilation, "Sample");
        var method = GetRequiredMethod(type, "M");
        var property = type.GetMembers().OfType<IPropertySymbol>().Single(symbol => symbol.ExplicitInterfaceImplementations.Length == 1);
        var @event = type.GetMembers().OfType<IEventSymbol>().Single(symbol => symbol.ExplicitInterfaceImplementations.Length == 1);

        Assert.Equal("M", method.GetImplementedInterfaceMember()?.Name);
        Assert.Equal("Property", property.GetImplementedInterfaceMember()?.Name);
        Assert.Equal("Changed", @event.GetImplementedInterfaceMember()?.Name);
    }

    [Fact]
    public void IsOrOverrideMethod_ReturnsTrueForTheMethodAndItsOverrides()
    {
        var compilation = CreateCompilation("""
            public class Base
            {
                public virtual string M() => "";
                public void Other() { }
            }

            public class Derived : Base
            {
                public override string M() => "";
            }
            """);
        var baseType = GetRequiredType(compilation, "Base");
        var derivedType = GetRequiredType(compilation, "Derived");
        var baseMethod = GetRequiredMethod(baseType, "M");
        var derivedMethod = GetRequiredMethod(derivedType, "M");
        var other = GetRequiredMethod(baseType, "Other");

        Assert.True(derivedMethod.IsOrOverrides(baseMethod));
        Assert.True(baseMethod.IsOrOverrides(baseMethod));
        Assert.False(other.IsOrOverrides(baseMethod));
    }

    [Fact]
    public void Override_ReturnsTrueWhenMethodOverridesBaseSymbol()
    {
        var compilation = CreateCompilation("""
            public class Base
            {
                public virtual string M() => "";
                public void Other() { }
            }

            public class Derived : Base
            {
                public override string M() => "";
            }
            """);
        var baseType = GetRequiredType(compilation, "Base");
        var derivedType = GetRequiredType(compilation, "Derived");
        var baseMethod = GetRequiredMethod(baseType, "M");
        var derivedMethod = GetRequiredMethod(derivedType, "M");
        var other = GetRequiredMethod(baseType, "Other");

        Assert.True(derivedMethod.Overrides(baseMethod));
        Assert.False(other.Overrides(baseMethod));
    }

    [Fact]
    public void GetReturnTypeAttribute_ReturnsMatchingReturnAttribute()
    {
        var compilation = CreateCompilation("""
            using System;

            public class BaseAttribute : Attribute;
            public sealed class SpecialAttribute : BaseAttribute;

            public class Sample
            {
                [return: Special]
                public string M() => "";
            }
            """);
        var type = GetRequiredType(compilation, "Sample");
        var baseAttribute = GetRequiredType(compilation, "BaseAttribute");
        var method = GetRequiredMethod(type, "M");

        Assert.NotNull(method.GetReturnTypeAttribute(baseAttribute));
        Assert.Null(method.GetReturnTypeAttribute(baseAttribute, inherits: false));
    }

    [Fact]
    public void HasReturnTypeAttribute_ReturnsTrueWhenReturnAttributeMatches()
    {
        var compilation = CreateCompilation("""
            using System;

            public class BaseAttribute : Attribute;
            public sealed class SpecialAttribute : BaseAttribute;

            public class Sample
            {
                [return: Special]
                public string M() => "";
            }
            """);
        var type = GetRequiredType(compilation, "Sample");
        var baseAttribute = GetRequiredType(compilation, "BaseAttribute");
        var method = GetRequiredMethod(type, "M");

        Assert.True(method.HasReturnTypeAttribute(baseAttribute));
        Assert.False(method.HasReturnTypeAttribute(baseAttribute, inherits: false));
    }

    [Fact]
    public void Ancestors_ReturnsOperationParents()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M()
                {
                    var value = 1;
                    _ = nameof(value);
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var argument = GetNameofArgumentOperation(semanticModel);

        Assert.Contains(argument.Ancestors(), operation => operation.Kind == OperationKind.NameOf);
    }

    [Fact]
    public void IsInNameofOperation_ReturnsTrueForNameofArgument()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M()
                {
                    var value = 1;
                    var other = value;
                    _ = nameof(value);
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var argument = GetNameofArgumentOperation(semanticModel);
        var other = GetInitializerOperation(semanticModel, "other");

        Assert.True(argument.IsInNameofOperation());
        Assert.False(other.IsInNameofOperation());
    }

    [Fact]
    public void UnwrapImplicitConversions_RemovesImplicitConversionsOnly()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M()
                {
                    int value = 42;
                    object boxed = value;
                    object explicitBoxed = (object)42;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");
        var explicitCast = GetInitializerOperation(semanticModel, "explicitBoxed");

        Assert.IsAssignableTo<ILocalReferenceOperation>(boxed.UnwrapImplicitConversions());
        Assert.Same(explicitCast, explicitCast.UnwrapImplicitConversions());
    }

    [Fact]
    public void UnwrapConversions_RemovesImplicitAndExplicitConversions()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M()
                {
                    object boxed = (object)42;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var boxed = GetInitializerOperation(semanticModel, "boxed");

        Assert.IsAssignableTo<ILiteralOperation>(boxed.UnwrapConversions());
    }

    [Fact]
    public void UnwrapLabels_ReturnsTheLabeledOperationBody()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public object M()
                {
                label:
                    return (object)42;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var label = GetRequiredOperation<ILabeledOperation>(semanticModel);

        Assert.IsAssignableTo<IReturnOperation>(label.UnwrapLabels());
    }

    [Fact]
    public void GetContainingMethod_ReturnsNearestMethodDeclarationSymbol()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public void M()
                {
                    var value = 1;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var value = GetInitializerOperation(semanticModel, "value");

        Assert.Equal("M", value.GetContainingMethod(default)?.Name);
    }

    [Fact]
    public void GetAttributes_ReturnsAttributesMatchingBaseTypeWhenInheritanceIsEnabled()
    {
        var compilation = CreateCompilation("""
            using System;

            public class BaseAttribute : Attribute;
            public sealed class SpecialAttribute : BaseAttribute;

            [Special]
            public class Sample;
            """);
        var type = GetRequiredType(compilation, "Sample");
        var baseAttribute = GetRequiredType(compilation, "BaseAttribute");

        var attribute = Assert.Single(type.GetAttributes(baseAttribute));
        Assert.Equal("SpecialAttribute", attribute.AttributeClass?.Name);
    }

    [Fact]
    public void GetFirstAttribute_ReturnsFirstMatchingAttribute()
    {
        var compilation = CreateCompilation("""
            using System;

            public class BaseAttribute : Attribute;
            public sealed class SpecialAttribute : BaseAttribute;

            [Special]
            public class Sample;
            """);
        var type = GetRequiredType(compilation, "Sample");
        var baseAttribute = GetRequiredType(compilation, "BaseAttribute");

        Assert.Equal("SpecialAttribute", type.GetFirstAttribute(baseAttribute)?.AttributeClass?.Name);
        Assert.Null(type.GetFirstAttribute(baseAttribute, inherits: false));
    }

    [Fact]
    public void HasAttribute_ReturnsTrueWhenSymbolHasMatchingAttribute()
    {
        var compilation = CreateCompilation("""
            using System;

            public class BaseAttribute : Attribute;
            public sealed class SpecialAttribute : BaseAttribute;

            [Special]
            public class Sample;
            """);
        var type = GetRequiredType(compilation, "Sample");
        var baseAttribute = GetRequiredType(compilation, "BaseAttribute");
        var specialAttribute = GetRequiredType(compilation, "SpecialAttribute");

        Assert.True(type.HasAttribute(baseAttribute));
        Assert.False(type.HasAttribute(baseAttribute, inherits: false));
        Assert.True(type.HasAttribute(specialAttribute, inherits: false));
    }

    [Fact]
    public void IsVisibleOutsideOfAssembly_ReturnsTrueForPublicAndProtectedSymbolChains()
    {
        var compilation = CreateCompilation("""
            public class Sample
            {
                public class PublicNested;
                protected class ProtectedNested;
                internal class InternalNested;
                private class PrivateNested;
            }
            """);
        var type = GetRequiredType(compilation, "Sample");

        Assert.True(type.GetTypeMembers("PublicNested").Single().IsVisibleOutsideOfAssembly());
        Assert.True(type.GetTypeMembers("ProtectedNested").Single().IsVisibleOutsideOfAssembly());
        Assert.False(type.GetTypeMembers("InternalNested").Single().IsVisibleOutsideOfAssembly());
        Assert.False(type.GetTypeMembers("PrivateNested").Single().IsVisibleOutsideOfAssembly());
    }

    [Fact]
    public void IsOverrideOrInterfaceImplementation_ReturnsTrueForOverridesAndInterfaceMembers()
    {
        var compilation = CreateCompilation("""
            public interface ISample
            {
                void InterfaceMethod();
            }

            public class Base
            {
                public virtual string Property => "";
            }

            public class Sample : Base, ISample
            {
                public override string Property => "";
                public void InterfaceMethod() { }
                public void Other() { }
            }
            """);
        var type = GetRequiredType(compilation, "Sample");
        var property = GetRequiredProperty(type, "Property");
        var interfaceMethod = GetRequiredMethod(type, "InterfaceMethod");
        var other = GetRequiredMethod(type, "Other");

        Assert.True(property.IsOverrideOrInterfaceImplementation());
        Assert.True(interfaceMethod.IsOverrideOrInterfaceImplementation());
        Assert.False(other.IsOverrideOrInterfaceImplementation());
    }

    [Fact]
    public void GetSymbolType_ReturnsTheDeclaredTypeForSupportedSymbolKinds()
    {
        var compilation = CreateCompilation("""
            public class Sample<T>
            {
                public string Field = "";
                public string Property { get; } = "";
                public int M(string parameter)
                {
                    string local = parameter;
                    return local.Length;
                }
            }
            """);
        var semanticModel = GetSemanticModel(compilation);
        var type = GetRequiredType(compilation, "Sample`1");
        var field = GetRequiredField(type, "Field");
        var property = GetRequiredProperty(type, "Property");
        var method = GetRequiredMethod(type, "M");
        var parameter = method.Parameters.Single();
        var local = GetRequiredLocal(semanticModel, "local");
        var typeParameter = type.TypeParameters.Single();

        Assert.Same(field.Type, field.GetSymbolType());
        Assert.Same(property.Type, property.GetSymbolType());
        Assert.Same(method.ReturnType, method.GetSymbolType());
        Assert.Same(parameter.Type, parameter.GetSymbolType());
        Assert.Same(local.Type, local.GetSymbolType());
        Assert.Same(type, type.GetSymbolType());
        Assert.Same(typeParameter, typeParameter.GetSymbolType());
    }

    [Fact]
    public void GetFirstSourceLocation_ReturnsFirstLocationDeclaredInSource()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);
        var type = GetRequiredType(compilation, "Sample");

        var location = type.GetFirstSourceLocation();

        Assert.NotNull(location);
        Assert.True(location!.IsInSource);
        Assert.EndsWith("Tests.cs", location.SourceTree?.FilePath);
    }

    [Fact]
    public void GetAllInterfacesIncludingThis_IncludesTheInterfaceWhenSymbolIsAnInterface()
    {
        var compilation = CreateCompilation("""
            public interface ISample;
            """);
        var type = GetRequiredType(compilation, "ISample");

        Assert.Contains(type, type.GetAllInterfacesIncludingSelf(), SymbolEqualityComparer.Default);
    }

    [Fact]
    public void GetAllInterfacesIncludingThis_DoesNotIncludeTheTypeWhenSymbolIsNotAnInterface()
    {
        var compilation = CreateCompilation("""
            public interface ISample;
            public class Sample : ISample;
            """);
        var type = GetRequiredType(compilation, "Sample");
        var interfaceType = GetRequiredType(compilation, "ISample");

        var interfaces = type.GetAllInterfacesIncludingSelf();

        Assert.Contains(interfaceType, interfaces, SymbolEqualityComparer.Default);
        Assert.DoesNotContain(type, interfaces, SymbolEqualityComparer.Default);
    }

    [Fact]
    public void GetAllInterfacesIncludingThis_DoesNotDuplicateSelfWhenAlreadyPresent()
    {
        var compilation = CreateCompilation("""
            public interface IBase;
            public interface ISample : IBase;
            """);
        var type = GetRequiredType(compilation, "ISample");

        var interfaces = type.GetAllInterfacesIncludingSelf();

        Assert.Equal(1, interfaces.Count(i => SymbolEqualityComparer.Default.Equals(i, type)));
    }

    [Fact]
    public void GetAllMembers_ReturnsMembersFromBaseTypes()
    {
        var compilation = CreateCompilation("""
            public class Base
            {
                public void BaseOnly() { }
            }

            public class Sample : Base
            {
                public void DerivedOnly() { }
            }
            """);
        var type = GetRequiredType(compilation, "Sample");
        var baseType = GetRequiredType(compilation, "Base");
        var baseOnly = GetRequiredMethod(baseType, "BaseOnly");

        Assert.Contains(baseOnly, type.GetAllMembers());
    }

    [Fact]
    public void GetAllMembers_WithName_ReturnsMatchingMembersFromBaseTypes()
    {
        var compilation = CreateCompilation("""
            public class Base
            {
                public void BaseOnly() { }
            }

            public class Sample : Base;
            """);
        var type = GetRequiredType(compilation, "Sample");
        var baseType = GetRequiredType(compilation, "Base");
        var baseOnly = GetRequiredMethod(baseType, "BaseOnly");

        Assert.Contains(baseOnly, type.GetAllMembers("BaseOnly"));
    }

    [Fact]
    public void InheritsFrom_ReturnsTrueForBaseTypesAndConstrainedTypeParameters()
    {
        var compilation = CreateCompilation("""
            public class Base;
            public class Sample : Base
            {
                public T M<T>(T value) where T : Sample => value;
            }
            """);
        var baseType = GetRequiredType(compilation, "Base");
        var sampleType = GetRequiredType(compilation, "Sample");
        var typeParameter = GetRequiredMethod(sampleType, "M").TypeParameters.Single();

        Assert.True(sampleType.InheritsFrom(baseType));
        Assert.True(typeParameter.InheritsFrom(baseType));
        Assert.False(baseType.InheritsFrom(sampleType));
    }

    [Fact]
    public void Implements_ReturnsTrueForImplementedInterfacesAndConstrainedTypeParameters()
    {
        var compilation = CreateCompilation("""
            public interface ISample;
            public class Sample : ISample
            {
                public T M<T>(T value) where T : ISample => value;
            }
            """);
        var interfaceType = GetRequiredType(compilation, "ISample");
        var sampleType = GetRequiredType(compilation, "Sample");
        var typeParameter = GetRequiredMethod(sampleType, "M").TypeParameters.Single();

        Assert.True(sampleType.Implements(interfaceType));
        Assert.True(typeParameter.Implements(interfaceType));
        Assert.False(interfaceType.Implements(interfaceType));
    }

    [Fact]
    public void ImplementsGenericInterface_ReturnsTrueForConstructedGenericInterfaces()
    {
        var compilation = CreateCompilation("""
            public interface ISample<T>;
            public class Sample : ISample<string>;
            """);
        var interfaceType = GetRequiredType(compilation, "ISample`1");
        var sampleType = GetRequiredType(compilation, "Sample");

        Assert.True(sampleType.ImplementsGenericInterface(interfaceType));
        Assert.False(interfaceType.ImplementsGenericInterface(interfaceType));
    }

    [Fact]
    public void IsOrImplements_ReturnsTrueForMatchingInterfaceOrImplementation()
    {
        var compilation = CreateCompilation("""
            public interface ISample;
            public class Sample : ISample;
            public class Other;
            """);
        var interfaceType = GetRequiredType(compilation, "ISample");
        var sampleType = GetRequiredType(compilation, "Sample");
        var otherType = GetRequiredType(compilation, "Other");

        Assert.True(interfaceType.IsOrImplements(interfaceType));
        Assert.True(sampleType.IsOrImplements(interfaceType));
        Assert.False(otherType.IsOrImplements(interfaceType));
    }

    [Fact]
    public void IsOrInheritFrom_ReturnsTrueForMatchingTypeOrBaseType()
    {
        var compilation = CreateCompilation("""
            public class Base;
            public class Sample : Base;
            """);
        var baseType = GetRequiredType(compilation, "Base");
        var sampleType = GetRequiredType(compilation, "Sample");

        Assert.True(baseType.IsOrInheritsFrom(baseType));
        Assert.True(sampleType.IsOrInheritsFrom(baseType));
        Assert.False(baseType.IsOrInheritsFrom(sampleType));
    }

    [Fact]
    public void IsEqualToAny_ReturnsTrueForAnyMatchingExpectedType()
    {
        var compilation = CreateCompilation("""
            public class Base;
            public class Sample;
            public interface ISample;
            """);
        var baseType = GetRequiredType(compilation, "Base");
        var sampleType = GetRequiredType(compilation, "Sample");
        var interfaceType = GetRequiredType(compilation, "ISample");
        ReadOnlySpan<ITypeSymbol?> expectedTypes = [baseType, sampleType];

        Assert.True(sampleType.IsEqualToAny(sampleType));
        Assert.True(sampleType.IsEqualToAny(baseType, sampleType));
        Assert.True(sampleType.IsEqualToAny(baseType, interfaceType, sampleType));
        Assert.True(sampleType.IsEqualToAny(expectedTypes));
        Assert.False(baseType.IsEqualToAny(interfaceType));
    }

    [Fact]
    public void IsObject_ReturnsTrueOnlyForSystemObject()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);

        Assert.True(compilation.GetSpecialType(SpecialType.System_Object).IsObject());
        Assert.False(compilation.GetSpecialType(SpecialType.System_String).IsObject());
    }

    [Fact]
    public void IsString_ReturnsTrueOnlyForSystemString()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);

        Assert.True(compilation.GetSpecialType(SpecialType.System_String).IsString());
        Assert.False(compilation.GetSpecialType(SpecialType.System_Object).IsString());
    }

    [Fact]
    public void IsChar_ReturnsTrueOnlyForSystemChar()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);

        Assert.True(compilation.GetSpecialType(SpecialType.System_Char).IsChar());
        Assert.False(compilation.GetSpecialType(SpecialType.System_String).IsChar());
    }

    [Fact]
    public void IsInt32_ReturnsTrueOnlyForSystemInt32()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);

        Assert.True(compilation.GetSpecialType(SpecialType.System_Int32).IsInt32());
        Assert.False(compilation.GetSpecialType(SpecialType.System_Int64).IsInt32());
    }

    [Fact]
    public void IsBoolean_ReturnsTrueOnlyForSystemBoolean()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);

        Assert.True(compilation.GetSpecialType(SpecialType.System_Boolean).IsBoolean());
        Assert.False(compilation.GetSpecialType(SpecialType.System_Int32).IsBoolean());
    }

    [Fact]
    public void IsDateTime_ReturnsTrueOnlyForSystemDateTime()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);
        var dateTime = GetRequiredType(compilation, "System.DateTime");

        Assert.True(dateTime.IsDateTime());
        Assert.False(compilation.GetSpecialType(SpecialType.System_String).IsDateTime());
    }

    [Fact]
    public void IsEnum_ReturnsTrueOnlyForEnums()
    {
        var compilation = CreateCompilation("""
            public enum Sample
            {
                Value,
            }

            public class Other;
            """);
        var enumType = GetRequiredType(compilation, "Sample");
        var otherType = GetRequiredType(compilation, "Other");

        Assert.True(enumType.IsEnum());
        Assert.False(otherType.IsEnum());
    }

    [Fact]
    public void GetEnumType_ReturnsEnumUnderlyingType()
    {
        var compilation = CreateCompilation("""
            public enum Sample
            {
                Value,
            }

            public class Other;
            """);
        var enumType = GetRequiredType(compilation, "Sample");
        var otherType = GetRequiredType(compilation, "Other");

        Assert.Equal(SpecialType.System_Int32, enumType.GetEnumUnderlyingType()?.SpecialType);
        Assert.Null(otherType.GetEnumUnderlyingType());
    }

    [Fact]
    public void IsNumberType_ReturnsTrueForNumericSpecialTypes()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);

        Assert.True(compilation.GetSpecialType(SpecialType.System_Int32).IsNumberType());
        Assert.True(compilation.GetSpecialType(SpecialType.System_Decimal).IsNumberType());
        Assert.False(compilation.GetSpecialType(SpecialType.System_String).IsNumberType());
    }

    [Fact]
    public void IsBlittableType_ReturnsTrueForPrimitiveEnumsAndBlittableStructs()
    {
        var compilation = CreateCompilation("""
            public readonly struct Blittable
            {
                public readonly int X;
                public readonly long Y;
            }

            public struct NotBlittable
            {
                public string Text;
            }

            public enum SampleEnum
            {
                Value,
            }
            """);
        var blittable = GetRequiredType(compilation, "Blittable");
        var notBlittable = GetRequiredType(compilation, "NotBlittable");
        var sampleEnum = GetRequiredType(compilation, "SampleEnum");

        Assert.True(compilation.GetSpecialType(SpecialType.System_Int32).IsBlittableType());
        Assert.True(sampleEnum.IsBlittableType());
        Assert.True(blittable.IsBlittableType());
        Assert.False(notBlittable.IsBlittableType());
    }

    [Fact]
    public void GetUnderlyingNullableTypeOrSelf_ReturnsUnderlyingTypeForNullableValueTypes()
    {
        var compilation = CreateCompilation("""
            public class Sample;
            """);
        var nullableInt = compilation.GetSpecialType(SpecialType.System_Nullable_T).Construct(compilation.GetSpecialType(SpecialType.System_Int32));

        Assert.Equal(SpecialType.System_Int32, nullableInt.GetUnderlyingNullableTypeOrSelf().SpecialType);
        Assert.Equal(SpecialType.System_String, compilation.GetSpecialType(SpecialType.System_String).GetUnderlyingNullableTypeOrSelf().SpecialType);
    }

    [Fact]
    public void GetLineSpan_ReturnsLineSpanForNodesTokensTriviaAndNodeOrToken()
    {
        var compilation = CreateCompilation("""
            namespace Demo;
            public class Sample
            {
                // comment
                public void M(
                    int value)
                {
                }
            }
            """);
        var root = compilation.SyntaxTrees.Single().GetRoot();
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var comment = root.DescendantTrivia().Single(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia));

        Assert.True(method.GetLineSpan(default)?.Path.EndsWith("Tests.cs", StringComparison.Ordinal));
        Assert.Equal(4, method.Identifier.GetLineSpan(default)?.StartLinePosition.Line);
        Assert.Equal(3, comment.GetLineSpan(default)?.StartLinePosition.Line);
        Assert.Equal(4, ((SyntaxNodeOrToken)method).GetLineSpan(default)?.StartLinePosition.Line);
    }

    [Fact]
    public void GetLine_ReturnsStartLineForNodesTokensAndTrivia()
    {
        var compilation = CreateCompilation("""
            namespace Demo;
            public class Sample
            {
                // comment
                public void M()
                {
                }
            }
            """);
        var root = compilation.SyntaxTrees.Single().GetRoot();
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var comment = root.DescendantTrivia().Single(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia));

        Assert.Equal(4, method.GetLine(default));
        Assert.Equal(4, method.Identifier.GetLine(default));
        Assert.Equal(3, comment.GetLine(default));
    }

    [Fact]
    public void GetEndLine_ReturnsEndLineForNodesTokensAndTrivia()
    {
        var compilation = CreateCompilation("""
            namespace Demo;
            public class Sample
            {
                // comment
                public void M()
                {
                }
            }
            """);
        var root = compilation.SyntaxTrees.Single().GetRoot();
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var comment = root.DescendantTrivia().Single(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia));

        Assert.Equal(6, method.GetEndLine(default));
        Assert.Equal(4, method.Identifier.GetEndLine(default));
        Assert.Equal(3, comment.GetEndLine(default));
    }

    [Fact]
    public void SpansMultipleLines_ReturnsTrueWhenNodeOrTriviaCoversMultipleLines()
    {
        var compilation = CreateCompilation("""
            namespace Demo;
            public class Sample
            {
                // comment
                public void M()
                {
                }

                /*
                 * multi-line
                 */
            }
            """);
        var root = compilation.SyntaxTrees.Single().GetRoot();
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var singleLineComment = root.DescendantTrivia().Single(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia));
        var multiLineComment = root.DescendantTrivia().Single(trivia => trivia.IsKind(SyntaxKind.MultiLineCommentTrivia));

        Assert.True(method.SpansMultipleLines(default));
        Assert.True(multiLineComment.SpansMultipleLines(default));
        Assert.False(singleLineComment.SpansMultipleLines(default));
    }

    [Theory]
    [InlineData("Sample.g.cs")]
    [InlineData("Sample.G.CS")]
    [InlineData("Sample.designer.cs")]
    [InlineData("Sample.Designer.cs")]
    [InlineData("Sample.generated.cs")]
    [InlineData("Sample.g.i.cs")]
    [InlineData("TemporaryGeneratedFile_1234.cs")]
    [InlineData("temporarygeneratedfile_1234.vb")]
    [InlineData(".g.cs")]
    public void IsGeneratedCodeFile_ReturnsTrueForWellKnownGeneratedFileNames(string filePath)
    {
        Assert.True(GeneratedCodeExtensions.IsGeneratedCodeFile(filePath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Sample.cs")]
    [InlineData("Generated.cs")]
    [InlineData("Designer.cs")]
    [InlineData("Sample.g")]
    [InlineData("Sample.generator.cs")]
    [InlineData("Sample.g.")]
    public void IsGeneratedCodeFile_ReturnsFalseForRegularFileNames(string? filePath)
    {
        Assert.False(GeneratedCodeExtensions.IsGeneratedCodeFile(filePath));
    }

    [Theory]
    [InlineData(@"C:\project\Sample.g.cs", true)]
    [InlineData("/project/Sample.g.cs", true)]
    [InlineData(@"Generator\Namespace.Generator\Hint.g.cs", true)]
    [InlineData(@"C:\project.g\Sample.cs", false)]
    [InlineData("/project.g/Sample.cs", false)]
    public void IsGeneratedCodeFile_UsesTheFileNameWhateverTheDirectorySeparator(string filePath, bool expected)
    {
        Assert.Equal(expected, GeneratedCodeExtensions.IsGeneratedCodeFile(filePath));
    }

    [Fact]
    public void IsGeneratedCode_ReturnsTrueWhenTheFileNameIndicatesGeneratedCode()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("public class Sample;", DefaultParseOptions, path: "Sample.g.cs");

        Assert.True(syntaxTree.IsGeneratedCode(default));
    }

    [Fact]
    public void IsGeneratedCode_ReturnsFalseForRegularFile()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("public class Sample;", DefaultParseOptions, path: "Sample.cs");

        Assert.False(syntaxTree.IsGeneratedCode(default));
    }

    [Theory]
    [InlineData("// <auto-generated/>")]
    [InlineData("// <auto-generated>")]
    [InlineData("//<autogenerated/>")]
    [InlineData("/* <auto-generated/> */")]
    public void IsGeneratedCode_ReturnsTrueWhenTheFileStartsWithAnAutoGeneratedComment(string comment)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText($$"""
            {{comment}}
            namespace Demo;

            public class Sample;
            """, DefaultParseOptions, path: "Sample.cs");

        Assert.True(syntaxTree.IsGeneratedCode(default));
    }

    [Fact]
    public void IsGeneratedCode_IgnoresDocumentationComments()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("""
            /// <auto-generated/>
            public class Sample;
            """, DefaultParseOptions, path: "Sample.cs");

        Assert.False(syntaxTree.IsGeneratedCode(default));
    }

    [Fact]
    public void IsGeneratedCode_IgnoresAutoGeneratedCommentsThatAreNotAtTheBeginningOfTheFile()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("""
            namespace Demo;

            // <auto-generated/>
            public class Sample;
            """, DefaultParseOptions, path: "Sample.cs");

        Assert.False(syntaxTree.IsGeneratedCode(default));
    }

    [Fact]
    public void HasAutoGeneratedComment_ReportsTheLeadingTriviaOfTheNode()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("""
            namespace Demo;

            // <auto-generated/>
            public class Sample;
            """, DefaultParseOptions, path: "Sample.cs");
        var root = syntaxTree.GetRoot();
        var type = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();

        Assert.False(root.HasAutoGeneratedComment());
        Assert.True(type.HasAutoGeneratedComment());
    }

    [Theory]
    [InlineData("Sample.g.cs", "false", false)]
    [InlineData("Sample.cs", "true", true)]
    [InlineData("Sample.cs", "TRUE", true)]
    public void IsGeneratedCode_UsesTheGeneratedCodeOptionWhenConfigured(string filePath, string optionValue, bool expected)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("public class Sample;", DefaultParseOptions, path: filePath);
        var optionsProvider = new GeneratedCodeOptionProvider(optionValue);

        Assert.Equal(expected, syntaxTree.IsGeneratedCode(optionsProvider, default));
    }

    [Theory]
    [InlineData("Sample.g.cs", null, true)]
    [InlineData("Sample.g.cs", "invalid", true)]
    [InlineData("Sample.cs", null, false)]
    [InlineData("Sample.cs", "invalid", false)]
    public void IsGeneratedCode_FallsBackToTheHeuristicsWhenTheGeneratedCodeOptionIsNotUsable(string filePath, string? optionValue, bool expected)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("public class Sample;", DefaultParseOptions, path: filePath);
        var optionsProvider = new GeneratedCodeOptionProvider(optionValue);

        Assert.Equal(expected, syntaxTree.IsGeneratedCode(optionsProvider, default));
    }

    [Fact]
    public void IsGeneratedCode_UsesTheGeneratedCodeOptionFromAnalyzerOptions()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("public class Sample;", DefaultParseOptions, path: "Sample.cs");
        var analyzerOptions = new AnalyzerOptions([], new GeneratedCodeOptionProvider("true"));

        Assert.True(syntaxTree.IsGeneratedCode(analyzerOptions, default));
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        string assemblyName = "Tests",
        IReadOnlyCollection<MetadataReference>? additionalReferences = null,
        CSharpParseOptions? parseOptions = null,
        int? dotnetMajorVersion = null,
        bool allowInvalidCode = false)
    {
        var references = CreateMetadataReferences(dotnetMajorVersion);
        if (additionalReferences is not null)
        {
            references.AddRange(additionalReferences);
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, parseOptions ?? DefaultParseOptions, path: assemblyName + ".cs")],
            references,
            DefaultCompilationOptions);

        if (!allowInvalidCode)
        {
            var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            Assert.Empty(errors);
        }

        return compilation;
    }

    private static List<MetadataReference> CreateMetadataReferences(int? dotnetMajorVersion)
    {
        if (dotnetMajorVersion is not null && TryGetReferenceAssemblyDirectory(dotnetMajorVersion.Value) is { } referenceAssemblyDirectory)
        {
            return Directory
                .EnumerateFiles(referenceAssemblyDirectory, "*.dll")
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToList<MetadataReference>();
        }

        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        Assert.NotNull(trustedPlatformAssemblies);

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList<MetadataReference>();
    }

    private static string? TryGetReferenceAssemblyDirectory(int dotnetMajorVersion)
    {
        foreach (var dotnetRoot in GetDotNetRoots())
        {
            var packRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
            if (!Directory.Exists(packRoot))
                continue;

            foreach (var versionDirectory in Directory.EnumerateDirectories(packRoot).OrderByDescending(path => path, StringComparer.Ordinal))
            {
                var version = Path.GetFileName(versionDirectory);
                if (!version.StartsWith(dotnetMajorVersion + ".", StringComparison.Ordinal))
                    continue;

                var referenceAssemblyDirectory = Path.Combine(versionDirectory, "ref", "net" + dotnetMajorVersion + ".0");
                if (Directory.Exists(referenceAssemblyDirectory))
                    return referenceAssemblyDirectory;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetDotNetRoots()
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
            yield return dotnetRoot;

        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (runtimeDirectory is not null)
            yield return Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", ".."));
    }

    private static SemanticModel GetSemanticModel(Compilation compilation)
    {
        return compilation.GetSemanticModel(compilation.SyntaxTrees.Single());
    }

    private static INamedTypeSymbol GetRequiredType(Compilation compilation, string metadataName)
    {
        var type = compilation.GetTypeByMetadataName(metadataName);
        Assert.NotNull(type);

        return type;
    }

    private static IMethodSymbol GetRequiredMethod(INamedTypeSymbol type, string name)
    {
        return type.GetMembers(name).OfType<IMethodSymbol>().Single(method => method.MethodKind is MethodKind.Ordinary);
    }

    private static IPropertySymbol GetRequiredProperty(INamedTypeSymbol type, string name)
    {
        return type.GetMembers(name).OfType<IPropertySymbol>().Single();
    }

    private static IFieldSymbol GetRequiredField(INamedTypeSymbol type, string name)
    {
        return type.GetMembers(name).OfType<IFieldSymbol>().Single();
    }

    private static ILocalSymbol GetRequiredLocal(SemanticModel semanticModel, string name)
    {
        var variable = semanticModel.SyntaxTree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(node => node.Identifier.ValueText == name);
        var symbol = semanticModel.GetDeclaredSymbol(variable);
        Assert.NotNull(symbol);

        return (ILocalSymbol)symbol;
    }

    private static IOperation GetInitializerOperation(SemanticModel semanticModel, string variableName)
    {
        var variable = semanticModel.SyntaxTree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(node => node.Identifier.ValueText == variableName);
        var value = variable.Initializer?.Value;
        Assert.NotNull(value);
        var operation = semanticModel.GetOperation(value);
        Assert.NotNull(operation);

        return operation;
    }

    private static IOperation GetNameofArgumentOperation(SemanticModel semanticModel)
    {
        var argument = semanticModel.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression.ToString() == "nameof")
            .ArgumentList
            .Arguments
            .Single()
            .Expression;
        var operation = semanticModel.GetOperation(argument);
        Assert.NotNull(operation);

        return operation;
    }

    private static TOperation GetRequiredOperation<TOperation>(SemanticModel semanticModel)
        where TOperation : class, IOperation
    {
        var operation = semanticModel.SyntaxTree.GetRoot()
            .DescendantNodes()
            .Select(node => semanticModel.GetOperation(node))
            .OfType<TOperation>()
            .FirstOrDefault();
        Assert.NotNull(operation);

        return operation;
    }

    private static DiagnosticDescriptor CreateDescriptor()
    {
        return new DiagnosticDescriptor("MFTEST001", "Title", "Message", "Category", DiagnosticSeverity.Warning, isEnabledByDefault: true);
    }

    private sealed class GeneratedCodeOptionProvider(string? generatedCode) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(generatedCode: null);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Options(generatedCode);

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Options(generatedCode: null);

        private sealed class Options(string? generatedCode) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                if (generatedCode is not null && key is "generated_code")
                {
                    value = generatedCode;
                    return true;
                }

                value = null;
                return false;
            }
        }
    }
}
