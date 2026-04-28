using System.Reflection;
using System.Runtime.CompilerServices;
using Meziantou.Framework;
using Meziantou.Framework.InlineSnapshotTesting;
using Meziantou.Framework.PublicApiGenerator;
using Meziantou.Framework.PublicApiGenerator.Tool;
using Xunit.Sdk;

namespace Meziantou.Framework.PublicApiGenerator.Tests;

//[Collection("Tool")] // Ensure tests run sequentially
public sealed class PublicApiGeneratorTests
{
    [Fact]
    public async Task EmptyClass()
    {
        await Validate("""
            public class Sample
            {
            }
            """, """
            #nullable enable

            public class Sample
            {
            }
            """);
    }

    [Fact]
    public async Task Namespace_EmptyClass()
    {
        await Validate("""
            namespace Demo;

            public class Sample
            {
            }
            """, """
            #nullable enable

            namespace Demo
            {
                public class Sample
                {
                }
            }
            """);
    }

    [Fact]
    public async Task Namespace_ConflictsWithSystem_UsesGlobalQualifier()
    {
        await Validate("""
            namespace Sample.System;

            public class SampleType
            {
                public global::System.Collections.Generic.List<int> M(global::System.Collections.Generic.Dictionary<string, int> value) => new();
            }
            """, """
            #nullable enable

            namespace Sample.System
            {
                public class SampleType
                {
                    public global::System.Collections.Generic.List<int> M(global::System.Collections.Generic.Dictionary<string, int> value) => throw null;
                }
            }
            """);
    }

    [Fact]
    public async Task Namespace_ConflictsWithGlobalNamespace_UsesGlobalQualifier()
    {
        await Validate("""
            namespace Sample.Dummy
            {
                public class A
                {
                    public A(global::Dummy.B value)
                    {
                    }
                }
            }

            namespace Dummy
            {
                public class B
                {
                }
            }
            """, """
            #nullable enable

            namespace Dummy
            {
                public class B
                {
                }
            }
            namespace Sample.Dummy
            {
                public class A
                {
                    public A(global::Dummy.B value) { }
                }
            }
            """);
    }

    [Fact]
    public async Task NestedTypes_Basic()
    {
        await Validate("""
            public class Outer
            {
                public class Inner
                {
                    public int M() => 0;
                }
            }
            """, """
            #nullable enable

            public class Outer
            {
                public class Inner
                {
                    public int M() => throw null;
                }
            }
            """);
    }

    [Fact]
    public async Task EmptyInterface()
    {
        await Validate("""
            public interface ISample
            {
            }
            """, """
            #nullable enable

            public interface ISample
            {
            }
            """);
    }

    [Fact]
    public async Task Struct_Empty()
    {
        await Validate("""
            public struct Sample
            {
            }
            """, """
            #nullable enable

            public struct Sample
            {
            }
            """);
    }

    [Fact]
    public async Task Delegate_Basic()
    {
        await Validate("""
            public delegate int SampleDelegate(string value);
            """, """
            #nullable enable

            public delegate int SampleDelegate(string value);
            """);
    }

    [Fact]
    public async Task Method_Pointer()
    {
        await Validate("""
            public class Sample
            {
                public unsafe int* M(int* value) => value;
            }
            """, """
            #nullable enable

            public class Sample
            {
                public unsafe int* M(int* value) => throw null;
            }
            """);
    }

    [Fact]
    public async Task RefStruct_Empty()
    {
        await Validate("""
            public ref struct Sample
            {
            }
            """, """
            #nullable enable

            public ref struct Sample
            {
            }
            """);
    }

    [Fact]
    public async Task RefStruct_RefFields()
    {
        await Validate("""
            public ref struct Sample
            {
                public ref int A;
                public readonly ref int B;
                public ref readonly int C;
                public readonly ref readonly int D;
            }
            """, """
            #nullable enable

            public ref struct Sample
            {
                public ref int A;
                public readonly ref int B;
                public ref readonly int C;
                public readonly ref readonly int D;
            }
            """);
    }

    [Fact]
    public async Task EmptyClass_WithoutAutoGeneratedComment()
    {
        await Validate("""
            public class Sample
            {
            }
            """, """
            // <auto-generated/>
            #nullable enable

            public class Sample
            {
            }
            """, new PublicApiOptions
        {
            IncludeAutoGeneratedComment = true,
        });
    }

    [Fact]
    public async Task FileLayout_SingleFile()
    {
        var files = await BuildFiles("""
            public class GlobalType
            {
            }

            namespace Demo
            {
                public class Other
                {
                }

                public class Sample
                {
                }
            }
            """, new PublicApiOptions
        {
            FileLayout = PublicApiFileLayout.SingleFile,
            IncludeAutoGeneratedComment = false,
        });

        var file = Assert.Single(files);
        Assert.Equal("PublicApi.g.cs", file.RelativePath);
        Assert.Contains("public class GlobalType", file.Content, StringComparison.Ordinal);
        Assert.Contains("namespace Demo", file.Content, StringComparison.Ordinal);
        Assert.Contains("public class Other", file.Content, StringComparison.Ordinal);
        Assert.Contains("public class Sample", file.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileLayout_OneFilePerNamespace()
    {
        var files = await BuildFiles("""
            public class GlobalType
            {
            }

            namespace Demo
            {
                public class Other
                {
                }

                public class Sample
                {
                }
            }
            """, new PublicApiOptions
        {
            FileLayout = PublicApiFileLayout.OneFilePerNamespace,
            IncludeAutoGeneratedComment = false,
        });

        Assert.Equal(["Demo.g.cs", "GlobalNamespace.g.cs"], files.Select(static file => file.RelativePath).OrderBy(static value => value, StringComparer.Ordinal));
        var demoFile = files.Single(file => file.RelativePath == "Demo.g.cs");
        var globalNamespaceFile = files.Single(file => file.RelativePath == "GlobalNamespace.g.cs");
        Assert.Contains("public class Other", demoFile.Content, StringComparison.Ordinal);
        Assert.Contains("public class Sample", demoFile.Content, StringComparison.Ordinal);
        Assert.Contains("public class GlobalType", globalNamespaceFile.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileLayout_OneFilePerType()
    {
        var files = await BuildFiles("""
            public class GlobalType
            {
            }

            namespace Demo
            {
                public class Other
                {
                }

                public class Sample
                {
                }
            }
            """, new PublicApiOptions
        {
            FileLayout = PublicApiFileLayout.OneFilePerType,
            IncludeAutoGeneratedComment = false,
        });

        Assert.Equal(["Demo.Other.g.cs", "Demo.Sample.g.cs", "GlobalType.g.cs"], files.Select(static file => file.RelativePath));
        Assert.Contains("public class Other", files.Single(static file => file.RelativePath == "Demo.Other.g.cs").Content, StringComparison.Ordinal);
        Assert.Contains("public class Sample", files.Single(static file => file.RelativePath == "Demo.Sample.g.cs").Content, StringComparison.Ordinal);
        Assert.Contains("public class GlobalType", files.Single(static file => file.RelativePath == "GlobalType.g.cs").Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultiTarget_MemberOnlyInOneTargetFramework()
    {
        var files = await BuildFiles(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["netstandard2.0"] = """
                    public class Sample
                    {
                        public void A()
                        {
                        }
                    }
                    """,
                ["net8.0"] = """
                    public class Sample
                    {
                        public void A()
                        {
                        }

                        public void B()
                        {
                        }
                    }
                    """,
            },
            new PublicApiOptions
            {
                FileLayout = PublicApiFileLayout.SingleFile,
                IncludeAutoGeneratedComment = false,
            });

        InlineSnapshot.Validate(Assert.Single(files).Content.TrimEnd('\r', '\n'), """
            #nullable enable

            public class Sample
            {
                public void A() { }
                #if NET8_0
                public void B() { }
                #endif
            }
            """);
    }

    [Fact]
    public async Task MultiTarget_MemberOnlyInOneTargetFramework_AutoDetectTFM()
    {
        var files = await BuildFilesWithAutoDetectedTargetFramework(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["netstandard2.0"] = """
                    public class Sample
                    {
                        public void A()
                        {
                        }
                    }
                    """,
                ["net8.0"] = """
                    public class Sample
                    {
                        public void A()
                        {
                        }

                        public void B()
                        {
                        }
                    }
                    """,
            },
            new PublicApiOptions
            {
                FileLayout = PublicApiFileLayout.SingleFile,
                IncludeAutoGeneratedComment = false,
            });

        InlineSnapshot.Validate(Assert.Single(files).Content.TrimEnd('\r', '\n'), """
            #nullable enable

            public class Sample
            {
                public void A() { }
                #if NET8_0
                public void B() { }
                #endif
            }
            """);
    }

    [Theory]
    [InlineData(".NETFramework,Version=v4.6.2", "NET462")]
    [InlineData(".NETStandard,Version=v2.0", "NETSTANDARD2_0")]
    [InlineData(".NETCoreApp,Version=v3.1", "NETCOREAPP3_1")]
    [InlineData(".NETCoreApp,Version=v5.0", "NET5_0")]
    [InlineData(".NETCoreApp,Version=v10.0", "NET10_0")]
    public async Task MultiTarget_TargetFrameworkMoniker_MapsToExpectedSymbol(string targetFrameworkMoniker, string expectedSymbol)
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var specializedAssembly = await CompileSource(temporaryDirectory, "specialized", "net8.0", """
            public class Sample
            {
                public void A()
                {
                }

                public void B()
                {
                }
            }
            """);
        var baselineAssembly = await CompileSource(temporaryDirectory, "baseline", "net8.0", """
            public class Sample
            {
                public void A()
                {
                }
            }
            """);

        var files = PublicApi.Generate(
            [
                new AssemblySource(specializedAssembly.ToString(), targetFrameworkMoniker),
                new AssemblySource(baselineAssembly.ToString(), ".NETCoreApp,Version=v8.0"),
            ],
            new PublicApiOptions
            {
                FileLayout = PublicApiFileLayout.SingleFile,
                IncludeAutoGeneratedComment = false,
            });

        var content = Assert.Single(files).Content;
        Assert.Contains($"#if {expectedSymbol}", content, StringComparison.Ordinal);
        Assert.Contains("public void B() { }", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultiTarget_MemberSignatureDiffers()
    {
        var files = await BuildFiles(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["netstandard2.0"] = """
                    public class Sample
                    {
                        public int B() => 0;
                    }
                    """,
                ["net8.0"] = """
                    public class Sample
                    {
                        public long B() => 0;
                    }
                    """,
            },
            new PublicApiOptions
            {
                FileLayout = PublicApiFileLayout.SingleFile,
                IncludeAutoGeneratedComment = false,
            });

        InlineSnapshot.Validate(Assert.Single(files).Content.TrimEnd('\r', '\n'), """
            #nullable enable

            public class Sample
            {
                #if NET8_0
                public long B() => throw null;
                #elif NETSTANDARD2_0
                public int B() => throw null;
                #endif
            }
            """);
    }

    [Fact]
    public async Task MultiTarget_TypeOnlyInOneTargetFramework()
    {
        var files = await BuildFiles(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["netstandard2.0"] = """
                    public class Marker
                    {
                    }
                    """,
                ["net8.0"] = """
                    public class Marker
                    {
                    }

                    public class Net8Only
                    {
                    }
                    """,
            },
            new PublicApiOptions
            {
                FileLayout = PublicApiFileLayout.SingleFile,
                IncludeAutoGeneratedComment = false,
            });

        InlineSnapshot.Validate(Assert.Single(files).Content.TrimEnd('\r', '\n'), """
            #nullable enable

            public class Marker
            {
            }


            #if NET8_0
            public class Net8Only
            {
            }
            #endif
            """);
    }

    [Fact]
    public async Task Tool_MultiInput_InfersTargetFrameworkFromAssembly()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var netstandardAssembly = await CompileSource(temporaryDirectory, "netstandard", "netstandard2.0", """
            public class Sample
            {
                public void A()
                {
                }
            }
            """);
        var net8Assembly = await CompileSource(temporaryDirectory, "net8", "net8.0", """
            public class Sample
            {
                public void A()
                {
                }

                public void B()
                {
                }
            }
            """);

        var outputDirectory = temporaryDirectory / "output";
        var exitCode = await Program.MainImpl(
            [
                "--input", netstandardAssembly.ToString(),
                "--input", net8Assembly.ToString(),
                "--output", outputDirectory.ToString(),
                "--omit-auto-generated-comment",
            ],
            configure: null);

        Assert.Equal(0, exitCode);
        InlineSnapshot.Validate(File.ReadAllText(outputDirectory / "PublicApi.g.cs").TrimEnd('\r', '\n'), """
            #nullable enable

            public class Sample
            {
                public void A() { }
                #if NET8_0
                public void B() { }
                #endif
            }
            """);
    }

    [Fact]
    public async Task Methods_InstanceAndStatic()
    {
        await Validate("""
            public class Sample
            {
                public int GetValue() => 42;
                public static void Reset() { }
            }
            """, """
            #nullable enable

            public class Sample
            {
                public int GetValue() => throw null;
                public static void Reset() { }
            }
            """);
    }

    [Fact]
    public async Task Operators_ImplicitExplicitEqualityAddition()
    {
        await Validate("""
            public readonly struct Sample
            {
                public static implicit operator int(Sample value) => default;
                public static explicit operator Sample(int value) => default;
                public static Sample operator +(Sample left, Sample right) => default;
                public static bool operator ==(Sample left, Sample right) => default;
                public static bool operator !=(Sample left, Sample right) => default;
            }
            """, """
            #nullable enable

            public readonly struct Sample
            {
                public static implicit operator int(Sample value) => throw null;
                public static explicit operator Sample(int value) => throw null;
                public static Sample operator +(Sample left, Sample right) => throw null;
                public static bool operator ==(Sample left, Sample right) => throw null;
                public static bool operator !=(Sample left, Sample right) => throw null;
            }
            """);
    }

    [Fact]
    public async Task Operators_FullOverloadableSet()
    {
        await Validate("""
            public struct Sample
            {
                public static Sample operator +(Sample x) => x;
                public static Sample operator -(Sample x) => x;
                public static Sample operator !(Sample x) => x;
                public static Sample operator ~(Sample x) => x;
                public static Sample operator ++(Sample x) => x;
                public static Sample operator --(Sample x) => x;
                public static bool operator true(Sample x) => true;
                public static bool operator false(Sample x) => false;
                public static Sample operator +(Sample x, Sample y) => x;
                public static Sample operator -(Sample x, Sample y) => x;
                public static Sample operator *(Sample x, Sample y) => x;
                public static Sample operator /(Sample x, Sample y) => x;
                public static Sample operator %(Sample x, Sample y) => x;
                public static Sample operator &(Sample x, Sample y) => x;
                public static Sample operator |(Sample x, Sample y) => x;
                public static Sample operator ^(Sample x, Sample y) => x;
                public static Sample operator <<(Sample x, int y) => x;
                public static Sample operator >>(Sample x, int y) => x;
                public static Sample operator >>>(Sample x, int y) => x;
                public static bool operator ==(Sample x, Sample y) => true;
                public static bool operator !=(Sample x, Sample y) => true;
                public static bool operator <(Sample x, Sample y) => true;
                public static bool operator >(Sample x, Sample y) => true;
                public static bool operator <=(Sample x, Sample y) => true;
                public static bool operator >=(Sample x, Sample y) => true;
                public static implicit operator int(Sample x) => 0;
                public static explicit operator Sample(int x) => default;
                public void operator +=(Sample x) { }
                public void operator -=(Sample x) { }
                public void operator *=(Sample x) { }
                public void operator /=(Sample x) { }
                public void operator %=(Sample x) { }
                public void operator &=(Sample x) { }
                public void operator |=(Sample x) { }
                public void operator ^=(Sample x) { }
                public void operator <<=(int x) { }
                public void operator >>=(int x) { }
                public void operator >>>=(int x) { }
                public void operator ++() { }
                public void operator --() { }
                public override bool Equals([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] object? obj) => false;
                public override int GetHashCode() => 0;
            }
            """, """
            #nullable enable

            public struct Sample
            {
                public static Sample operator +(Sample x) => throw null;
                public static Sample operator -(Sample x) => throw null;
                public static Sample operator !(Sample x) => throw null;
                public static Sample operator ~(Sample x) => throw null;
                public static Sample operator ++(Sample x) => throw null;
                public static Sample operator --(Sample x) => throw null;
                public static bool operator true(Sample x) => throw null;
                public static bool operator false(Sample x) => throw null;
                public static Sample operator +(Sample x, Sample y) => throw null;
                public static Sample operator -(Sample x, Sample y) => throw null;
                public static Sample operator *(Sample x, Sample y) => throw null;
                public static Sample operator /(Sample x, Sample y) => throw null;
                public static Sample operator %(Sample x, Sample y) => throw null;
                public static Sample operator &(Sample x, Sample y) => throw null;
                public static Sample operator |(Sample x, Sample y) => throw null;
                public static Sample operator ^(Sample x, Sample y) => throw null;
                public static Sample operator <<(Sample x, int y) => throw null;
                public static Sample operator >>(Sample x, int y) => throw null;
                public static Sample operator >>>(Sample x, int y) => throw null;
                public static bool operator ==(Sample x, Sample y) => throw null;
                public static bool operator !=(Sample x, Sample y) => throw null;
                public static bool operator <(Sample x, Sample y) => throw null;
                public static bool operator >(Sample x, Sample y) => throw null;
                public static bool operator <=(Sample x, Sample y) => throw null;
                public static bool operator >=(Sample x, Sample y) => throw null;
                public static implicit operator int(Sample x) => throw null;
                public static explicit operator Sample(int x) => throw null;
                public void operator +=(Sample x) { }
                public void operator -=(Sample x) { }
                public void operator *=(Sample x) { }
                public void operator /=(Sample x) { }
                public void operator %=(Sample x) { }
                public void operator &=(Sample x) { }
                public void operator |=(Sample x) { }
                public void operator ^=(Sample x) { }
                public void operator <<=(int x) { }
                public void operator >>=(int x) { }
                public void operator >>>=(int x) { }
                public void operator ++() { }
                public void operator --() { }
                public override bool Equals([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] object? obj) => throw null;
                public override int GetHashCode() => throw null;
            }
            """);
    }

    [Fact]
    public async Task ExtensionMethod_ThisModifier()
    {
        await Validate("""
            public static class SampleExtensions
            {
                public static int Length2(this string value) => value.Length;
            }
            """, """
            #nullable enable

            public static class SampleExtensions
            {
                public static int Length2(this string value) => throw null;
            }
            """);
    }

#if NET10_0_OR_GREATER
    [Fact]
    public async Task ExtensionMembers_CSharp14()
    {
        await Validate("""
            using System.Collections.Generic;
            using System.Linq;

            public static class SampleExtensions
            {
                extension(IEnumerable<int> values)
                {
                    public int CountPlusOne => values.Count() + 1;
                    public IEnumerable<int> Add(int value) => values.Select(item => item + value);
                }
            }
            """, """
            #nullable enable

            public static class SampleExtensions
            {
                public static System.Collections.Generic.IEnumerable<int> Add(this System.Collections.Generic.IEnumerable<int> values, int value) => throw null;
                extension(System.Collections.Generic.IEnumerable<int> values)
                {
                    public int CountPlusOne { get => throw null; }
                }
            }
            """, compilerOptions: new CompilerOptions
        {
            TargetFramework = "net10.0",
        });
    }
#endif

    [Fact]
    public async Task Method_ParameterModifiers()
    {
        await Validate("""
            public class Sample
            {
                public void M(in int p0, ref int p1, out int p2, ref readonly int p3)
                {
                    p2 = default;
                }
            }
            """, """
            #nullable enable

            public class Sample
            {
                public void M(in int p0, ref int p1, out int p2, ref readonly int p3) => throw null;
            }
            """);
    }

    [Fact]
    public async Task Method_Parameter_ReadonlyRefReadonly()
    {
        await Validate("""
            public class Sample
            {
                public void M(ref readonly int value)
                {
                }
            }
            """, """
            #nullable enable

            public class Sample
            {
                public void M(ref readonly int value) { }
            }
            """);
    }

    [Fact]
    public async Task Method_ParamsReadOnlySpanAndObjectArray()
    {
        await Validate("""
            using System;

            public class Sample
            {
                public void M1(params ReadOnlySpan<int> values)
                {
                }

                public void M2(params object[] values)
                {
                }
            }
            """, """
            #nullable enable

            public class Sample
            {
                public void M1(params System.ReadOnlySpan<int> values) { }
                public void M2(params object[] values) { }
            }
            """);
    }

    [Fact]
    public async Task Method_NullableReferenceTypes()
    {
        await Validate("""
            public class Sample
            {
                public string? A(string? value) => value;
            }
            """, """
            #nullable enable

            public class Sample
            {
                public string? A(string? value) => throw null;
            }
            """);
    }

    [Fact]
    public async Task Method_NullableGenericArguments()
    {
        await Validate("""
            using System.Collections.Generic;

            public class Sample
            {
                public Dictionary<object, string?> A() => null;
            }
            """, """
            #nullable enable

            public class Sample
            {
                public System.Collections.Generic.Dictionary<object, string?> A() => throw null;
            }
            """);
    }

    [Fact]
    public async Task Method_NestedNullableGenericArguments()
    {
        await Validate("""
            using System.Collections.Generic;

            public class Sample
            {
                public Dictionary<object, HashSet<string?>?> A() => null;
            }
            """, """
            #nullable enable

            public class Sample
            {
                public System.Collections.Generic.Dictionary<object, System.Collections.Generic.HashSet<string?>?> A() => throw null;
            }
            """);
    }

    [Fact]
    public async Task Event_Basic()
    {
        await Validate("""
            using System;

            public class Sample
            {
                public event EventHandler? Changed;
            }
            """, """
            #nullable enable

            public class Sample
            {
                public event System.EventHandler? Changed;
            }
            """);
    }

    [Fact]
    public async Task Properties_GetSetInitRequired()
    {
        await Validate("""
            public class Sample
            {
                public int GetOnly { get; }
                public int SetOnly { set { } }
                public int InitOnly { get; init; }
                public required int RequiredValue { get; set; }
            }
            """, """
            #nullable enable

            public class Sample
            {
                public int GetOnly { get => throw null; }
                public int SetOnly { set { } }
                public int InitOnly { get => throw null; init { } }
                public required int RequiredValue { get => throw null; set { } }
            }
            """);
    }

    [Fact]
    public async Task Indexer_OneParameter_GetOnly()
    {
        await Validate("""
            public class Sample
            {
                public int this[int index] => index;
            }
            """, """
            #nullable enable

            public class Sample
            {
                public int this[int index] { get => throw null; }
            }
            """);
    }

    [Fact]
    public async Task Indexer_MultipleParameters_SetOnly()
    {
        await Validate("""
            public class Sample
            {
                public int this[int index1, int index2]
                {
                    set
                    {
                    }
                }
            }
            """, """
            #nullable enable

            public class Sample
            {
                public int this[int index1, int index2] { set { } }
            }
            """);
    }

    [Fact]
    public async Task Indexer_MultipleParameters_GetSet()
    {
        await Validate("""
            public class Sample
            {
                public int this[int index1, int index2]
                {
                    get => 0;
                    set
                    {
                    }
                }
            }
            """, """
            #nullable enable

            public class Sample
            {
                public int this[int index1, int index2] { get => throw null; set { } }
            }
            """);
    }

    [Fact]
    public async Task Fields_InstanceAndStaticReadonly()
    {
        await Validate("""
            public class Sample
            {
                public int Field;
                public static readonly int StaticReadonlyField = 42;
            }
            """, """
            #nullable enable

            public class Sample
            {
                public int Field;
                public static readonly int StaticReadonlyField;
            }
            """);
    }

    [Fact]
    public async Task InterfaceMembers_StaticAndDefault()
    {
        await Validate("""
            public interface ISample
            {
                void M();
                static abstract int Counter { get; set; }
                static virtual int StaticMethod() => 1;
                int DefaultMethod() => 42;
            }
            """, """
            #nullable enable

            public interface ISample
            {
                static int Counter { get; set; }
                void M();
                public static int StaticMethod() => throw null;
                public int DefaultMethod() => throw null;
            }
            """);
    }

    [Fact]
    public async Task Interface_WithProperty()
    {
        await Validate("""
            public interface ISample
            {
                int Value { get; }
            }
            """, """
            #nullable enable

            public interface ISample
            {
                int Value { get; }
            }
            """);
    }

    [Fact]
    public async Task Interface_VisibilityModifiers()
    {
        await Validate("""
            public interface ISample
            {
                void AbstractImplicit();
                public void PublicDefault() { }
                private void PrivateDefault() { }
                protected void ProtectedDefault() { }
                internal void InternalDefault() { }
            }
            """, """
            #nullable enable

            public interface ISample
            {
                void AbstractImplicit();
                public void PublicDefault() { }
                protected void ProtectedDefault() { }
            }
            """);
    }

    [Fact]
    public async Task Constructor_Explicit()
    {
        await Validate("""
            public class Sample
            {
                public Sample()
                {
                }
            }
            """, """
            #nullable enable

            public class Sample
            {
            }
            """);
    }

    [Fact]
    public async Task Constructor_NonDefault()
    {
        await Validate("""
            public class Sample
            {
                public Sample(int value)
                {
                }
            }
            """, """
            #nullable enable

            public class Sample
            {
                public Sample(int value) { }
            }
            """);
    }

    [Fact]
    public async Task Constructor_NonDefaultBaseAndDerived()
    {
        await Validate("""
            public class SampleBase
            {
                public SampleBase(int value)
                {
                }
            }

            public class SampleDerived : SampleBase
            {
                public SampleDerived(int value) : base(value)
                {
                }
            }
            """, """
            #nullable enable

            public class SampleBase
            {
                public SampleBase(int value) { }
            }


            public class SampleDerived : SampleBase
            {
                public SampleDerived(int value) : base(default(int)) { }
            }
            """);
    }

    [Fact]
    public async Task Constructor_BaseWithoutDefaultConstructor()
    {
        await Validate("""
            public class SampleBaseClass
            {
                public SampleBaseClass(int value)
                {
                }
            }

            public class Sample : SampleBaseClass
            {
                public Sample() : base(default(int))
                {
                }
            }
            """, """
            #nullable enable

            public class Sample : SampleBaseClass
            {
                public Sample() : base(default(int)) { }
            }


            public class SampleBaseClass
            {
                public SampleBaseClass(int value) { }
            }
            """);
    }

    [Fact]
    public async Task Destructor_Explicit()
    {
        await Validate("""
            public class Sample
            {
                ~Sample()
                {
                }
            }
            """, """
            #nullable enable

            public class Sample
            {
                ~Sample() { }
            }
            """);
    }

    [Fact]
    public async Task Class_ImplementsInterface()
    {
        await Validate("""
            using System;

            public class Sample : IDisposable
            {
                public void Dispose()
                {
                }
            }
            """, """
            #nullable enable

            public class Sample : System.IDisposable
            {
                public void Dispose() { }
            }
            """);
    }

    [Fact]
    public async Task MethodParameter_DefaultValues()
    {
        await Validate("""
            public class Sample
            {
                public void M(int value = 42, string? text = null)
                {
                }
            }
            """, """
            #nullable enable

            public class Sample
            {
                public void M(int value = 42, string? text = null) { }
            }
            """);
    }

    [Fact]
    public async Task Method_WithCLSCompliantAttribute()
    {
        await Validate("""
            public class Sample
            {
                [System.CLSCompliantAttribute(false)]
                public void M()
                {
                }
            }
            """, """
            #nullable enable

            public class Sample
            {
                [System.CLSCompliant(false)]
                public void M() { }
            }
            """);
    }

    [Fact]
    public async Task Method_WithUnsupportedOSPlatformAttribute()
    {
        await Validate("""
            public class Sample
            {
                [System.Runtime.Versioning.UnsupportedOSPlatformAttribute("browser")]
                public void M()
                {
                }
            }
            """, """
            #nullable enable

            public class Sample
            {
                [System.Runtime.Versioning.UnsupportedOSPlatform("browser")]
                public void M() { }
            }
            """);
    }

    [Fact]
    public async Task Nullable_DisabledAndRestored()
    {
        await Validate("""
            #nullable enable

            public class Sample
            {
            #nullable disable
                public string M(string value) => value;
            #nullable restore
                public string? N(string? value) => value;
            }
            """, """
            #nullable enable

            public class Sample
            {
                #nullable disable
                public string M(string value) => throw null;
                #nullable restore
                public string? N(string? value) => throw null;
            }
            """);
    }

    [Fact]
    public async Task Nullable_DisabledInParameterList()
    {
        await Validate("""
            #nullable enable

            public class SampleType
            {
                public string? Sample(
            #nullable disable
                    string a
            #nullable restore
                    ) => throw null;
            }
            """, """
            #nullable enable

            public class SampleType
            {
                public string? Sample(
                #nullable disable
                    string a
                #nullable restore
                    ) => throw null;
            }
            """);
    }

    [Fact]
    public async Task Nullable_DisabledAtCompilation()
    {
        await Validate("""
            public class Sample
            {
                public string M(string value) => value;
            }
            """, """
            #nullable enable

            public class Sample
            {
                #nullable disable
                public string M(string value) => throw null;
                #nullable restore
            }
            """, compilerOptions: new CompilerOptions
        {
            Nullable = false,
        });
    }

    [Fact]
    public async Task Attributes_SkippedAndPreserved()
    {
        await Validate("""
            using System.CodeDom.Compiler;

            public class Sample
            {
                [GeneratedCode("generator", "1.0")]
                [System.CLSCompliantAttribute(false)]
                public void M()
                {
                }
            }
            """, """
            #nullable enable

            public class Sample
            {
                [System.CLSCompliant(false)]
                public void M() { }
            }
            """);
    }

    [Fact]
    public async Task Enum_Basic()
    {
        await Validate("""
            public enum Sample
            {
                A,
                B,
            }
            """, """
            #nullable enable

            public enum Sample
            {
                A = 0,
                B = 1
            }
            """);
    }

    [Fact]
    public async Task Enum_ExplicitValues()
    {
        await Validate("""
            public enum Sample
            {
                A = 1,
                B = 3,
            }
            """, """
            #nullable enable

            public enum Sample
            {
                A = 1,
                B = 3
            }
            """);
    }

    [Fact]
    public async Task Enum_WithFlagsAttribute()
    {
        await Validate("""
            [System.FlagsAttribute]
            public enum Sample
            {
                A = 1,
                B = 2,
            }
            """, """
            #nullable enable

            [System.Flags]
            public enum Sample
            {
                A = 1,
                B = 2
            }
            """);
    }

    [Fact]
    public async Task NullableInt_SystemNullable()
    {
        await Validate("""
            public class Sample
            {
                public System.Nullable<int> M(System.Nullable<int> value) => value;
            }
            """, """
            #nullable enable

            public class Sample
            {
                public int? M(int? value) => throw null;
            }
            """);
    }

    [Fact]
    public async Task GenericClass()
    {
        await Validate("""
            public class Sample<T0>
            {
                public T0 Value => default;
            }
            """, """
            #nullable enable

            public class Sample<T0>
            {
                public T0 Value { get => throw null; }
            }
            """);
    }

    [Fact]
    public async Task GenericClass_ImplementsGenericInterface()
    {
        await Validate("""
            using System.Collections;
            using System.Collections.Generic;

            public class Sample<T0> : IEnumerable<T0>
            {
                public IEnumerator<T0> GetEnumerator() => null;
                IEnumerator IEnumerable.GetEnumerator() => null;
            }
            """, """
            #nullable enable

            public class Sample<T0> : System.Collections.Generic.IEnumerable<T0>, System.Collections.IEnumerable
            {
                public System.Collections.Generic.IEnumerator<T0> GetEnumerator() => throw null;
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw null;
            }
            """);
    }

    [Fact]
    public async Task GenericMembers()
    {
        await Validate("""
            public class Sample
            {
                public TMethod0 M<TMethod0>(TMethod0 value) => value;
            }
            """, """
            #nullable enable

            public class Sample
            {
                public TMethod0 M<TMethod0>(TMethod0 value) => throw null;
            }
            """);
    }

    [Fact]
    public async Task GenericMembers_WithConstraints()
    {
        await Validate("""
            public class SampleBaseClass
            {
            }

            public class Sample
            {
                public void M<TAllowNull, TNew, TStruct, TClass, TEnum, TBase>()
                    where TAllowNull : class?
                    where TNew : new()
                    where TStruct : struct
                    where TClass : class
                    where TEnum : System.Enum
                    where TBase : SampleBaseClass
                {
                }
            }
            """, """
            #nullable enable

            public class Sample
            {
                public void M<TAllowNull, TNew, TStruct, TClass, TEnum, TBase>() where TAllowNull : class where TNew : new() where TStruct : struct where TClass : class where TEnum : System.Enum where TBase : SampleBaseClass { }
            }


            public class SampleBaseClass
            {
            }
            """);
    }

    [Fact]
    public async Task Member_WithObsoleteAttribute()
    {
        await Validate("""
            public class Sample
            {
                [System.ObsoleteAttribute("Use M2 instead")]
                public void M()
                {
                }
            }
            """, """
            #nullable enable

            public class Sample
            {
                [System.Obsolete("Use M2 instead")]
                public void M() { }
            }
            """);
    }

#if NET10_0_OR_GREATER
    [Fact]
    public async Task GenericConstraint_AllowsRefStruct()
    {
        await Validate("""
            public class Sample<T0>
                where T0 : allows ref struct
            {
            }
            """, """
            #nullable enable

            public class Sample<T0> where T0 : allows ref struct
            {
            }
            """, compilerOptions: new CompilerOptions
        {
            TargetFramework = "net10.0",
        });
    }
#endif

    [InlineSnapshotAssertion(nameof(expected))]
    private static async Task Validate(string source, string expected, PublicApiOptions? options = null, CompilerOptions? compilerOptions = null, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1)
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        compilerOptions ??= new CompilerOptions();
        options ??= new PublicApiOptions
        {
            FileLayout = PublicApiFileLayout.SingleFile,
            IncludeAutoGeneratedComment = false,
        };

        // Build the project
        var sourceProjectDirectory = temporaryDirectory / "source";
        temporaryDirectory.CreateTextFile(sourceProjectDirectory / "project.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{{compilerOptions.TargetFramework}}</TargetFramework>
                <LangVersion>preview</LangVersion>
                <Nullable>{{(compilerOptions.Nullable ? "enable" : "disable")}}</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
              </PropertyGroup>
            </Project>
            """);
        temporaryDirectory.CreateTextFile(sourceProjectDirectory / "Sample.cs", source);

        var assemblyPath = await Compile(sourceProjectDirectory);

        // Generate the API files using both reflection and metadata
        var reflectionFiles = PublicApi.Generate(
            Assembly.LoadFile(assemblyPath),
            options);
        var metadataFiles = PublicApi.Generate(
            assemblyPath,
            options);

        var reflectionContent = SerializeFiles(reflectionFiles);
        var metadataContent = SerializeFiles(metadataFiles);
        Assert.Equal(reflectionContent, metadataContent);
        InlineSnapshot.Validate(reflectionContent, expected, filePath, lineNumber);

        // Ensure the generated files are compilable
        var generatedDirectory = temporaryDirectory / "generated";
        temporaryDirectory.CreateTextFile(generatedDirectory / "project.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{{compilerOptions.TargetFramework}}</TargetFramework>
                <LangVersion>preview</LangVersion>
                <Nullable>{{(compilerOptions.Nullable ? "enable" : "disable")}}</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
              </PropertyGroup>
            </Project>
            """);
        foreach (var file in reflectionFiles)
        {
            temporaryDirectory.CreateTextFile(generatedDirectory / file.RelativePath, file.Content);
        }

        await Compile(generatedDirectory);

        static string SerializeFiles(IReadOnlyList<PublicApiFile> files)
        {
            return string.Join(
                "\n\n",
                files
                    .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                    .Select(file => file.Content.TrimEnd('\r', '\n')));
        }
    }

    private static async Task<IReadOnlyList<PublicApiFile>> BuildFiles(string source, PublicApiOptions options, CompilerOptions? compilerOptions = null)
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        compilerOptions ??= new CompilerOptions();

        var sourceProjectDirectory = temporaryDirectory / "source";
        temporaryDirectory.CreateTextFile(sourceProjectDirectory / "project.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{{compilerOptions.TargetFramework}}</TargetFramework>
                <LangVersion>preview</LangVersion>
                <Nullable>{{(compilerOptions.Nullable ? "enable" : "disable")}}</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
              </PropertyGroup>
            </Project>
            """);
        temporaryDirectory.CreateTextFile(sourceProjectDirectory / "Sample.cs", source);

        var assemblyPath = await Compile(sourceProjectDirectory);
        return PublicApi.Generate(assemblyPath, options);
    }

    private static async Task<IReadOnlyList<PublicApiFile>> BuildFiles(Dictionary<string, string> sourcesByTargetFramework, PublicApiOptions options)
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();

        var assemblySources = new List<AssemblySource>(sourcesByTargetFramework.Count);
        foreach (var sourceByTargetFramework in sourcesByTargetFramework)
        {
            var projectDirectoryName = sourceByTargetFramework.Key.Replace('.', '_').Replace('-', '_');
            var assemblyPath = await CompileSource(temporaryDirectory, projectDirectoryName, sourceByTargetFramework.Key, sourceByTargetFramework.Value);
            assemblySources.Add(new AssemblySource(assemblyPath.ToString(), ToTargetFrameworkMoniker(sourceByTargetFramework.Key)));
        }

        return PublicApi.Generate(assemblySources, options);
    }

    private static string ToTargetFrameworkMoniker(string targetFramework)
    {
        return targetFramework switch
        {
            "net8.0" => ".NETCoreApp,Version=v8.0",
            "netstandard2.0" => ".NETStandard,Version=v2.0",
            _ => targetFramework,
        };
    }

    private static async Task<IReadOnlyList<PublicApiFile>> BuildFilesWithAutoDetectedTargetFramework(Dictionary<string, string> sourcesByTargetFramework, PublicApiOptions options)
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();

        var assemblySources = new List<AssemblySource>(sourcesByTargetFramework.Count);
        foreach (var sourceByTargetFramework in sourcesByTargetFramework)
        {
            var projectDirectoryName = sourceByTargetFramework.Key.Replace('.', '_').Replace('-', '_');
            var assemblyPath = await CompileSource(temporaryDirectory, projectDirectoryName, sourceByTargetFramework.Key, sourceByTargetFramework.Value);
            assemblySources.Add(assemblyPath.ToString());
        }

        return PublicApi.Generate(assemblySources, options);
    }

    private static async Task<FullPath> CompileSource(TemporaryDirectory temporaryDirectory, string projectDirectoryName, string targetFramework, string source)
    {
        var sourceProjectDirectory = temporaryDirectory / projectDirectoryName;
        temporaryDirectory.CreateTextFile(sourceProjectDirectory / "project.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{{targetFramework}}</TargetFramework>
                <LangVersion>preview</LangVersion>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
              </PropertyGroup>
            </Project>
            """);
        temporaryDirectory.CreateTextFile(sourceProjectDirectory / "Sample.cs", source);
        return await Compile(sourceProjectDirectory);
    }

    private static async Task<FullPath> Compile(FullPath temporaryDirectory)
    {
        var projectPath = Assert.Single(Directory.EnumerateFiles(temporaryDirectory, "*.csproj", SearchOption.TopDirectoryOnly));

        var outputPath = temporaryDirectory / "bin";

        await RunDotNetAsync(temporaryDirectory, ["restore", projectPath, "-nologo", "--disable-build-servers"]);
        await RunDotNetAsync(temporaryDirectory, ["build", projectPath, "-nologo", "--disable-build-servers", "--no-restore", "--output", outputPath, "/p:AssemblyName=Source"]);
        return outputPath / "Source.dll";

        static async Task RunDotNetAsync(string workingDirectory, IReadOnlyList<string> arguments)
        {
            var processResult = await ProcessWrapper.Create("dotnet")
                .WithArguments(arguments)
                .WithWorkingDirectory(workingDirectory)
                .WithEnvironmentVariables(env => env.Set("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1"))
                .WithValidation(ProcessValidationMode.None)
                .ExecuteBufferedAsync(XunitCancellationToken);

            if (!processResult.ExitCode.IsSuccess)
            {
                var standardOutput = string.Join('\n', processResult.Output.StandardOutput.Select(line => line.Text));
                var standardError = string.Join('\n', processResult.Output.StandardError.Select(line => line.Text));
                throw new XunitException($"Command failed: dotnet {string.Join(' ', arguments)}\nstdout:\n{standardOutput}\nstderr:\n{standardError}");
            }
        }
    }

    public sealed class CompilerOptions
    {
        public bool Nullable { get; set; } = true;
        public string TargetFramework { get; set; } = "net8.0";
    }
}
