using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Xml.Linq;
using Meziantou.Framework.InlineSnapshotTesting;
using Meziantou.Framework.PublicApiGenerator.Tool;
using Xunit.Sdk;

namespace Meziantou.Framework.PublicApiGenerator.Tests;

public sealed class PublicApiGeneratorTests
{
    private const string CompilationCacheVersion = "v1";
    private const string CompilationCacheDirectoryEnvironmentVariable = "MEZIANTOU_PUBLIC_API_TEST_CACHE_DIRECTORY";
    private static readonly Lazy<Task<string>> DotNetSdkVersion = new(GetDotNetSdkVersionAsync);

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
    public async Task Namespace_DoesNotOverQualifyWithGlobalQualifier()
    {
        await Validate("""
            namespace Demo.Sample;

            public sealed class B
            {
            }

            public sealed class A
            {
                public Demo.Sample.B Parent => null;
            }
            """, """
            #nullable enable

            namespace Demo.Sample
            {
                public sealed class A
                {
                    public Demo.Sample.B Parent { get => throw null; }
                }

                public sealed class B
                {
                }
            }
            """);
    }

    [Fact]
    public async Task ExplicitInterfaceMethod_StripsRedundantGlobalQualifier()
    {
        await Validate("""
            namespace Meziantou.Framework.FixedStringBuilder;

            public interface IFixedString
            {
                void M();
            }

            public struct S : IFixedString
            {
                void global::Meziantou.Framework.FixedStringBuilder.IFixedString.M()
                {
                }
            }
            """, """
            #nullable enable

            namespace Meziantou.Framework.FixedStringBuilder
            {
                public interface IFixedString
                {
                    void M();
                }

                public struct S : Meziantou.Framework.FixedStringBuilder.IFixedString
                {
                    void Meziantou.Framework.FixedStringBuilder.IFixedString.M() { }
                }
            }
            """);
    }

    [Fact]
    public async Task ExplicitInterfaceMethod_KeepsGlobalQualifierWhenNamespaceConflicts()
    {
        await Validate("""
            namespace Sample.System;

            public sealed class C : global::System.IDisposable
            {
                void global::System.IDisposable.Dispose()
                {
                }
            }
            """, """
            #nullable enable

            namespace Sample.System
            {
                public sealed class C : global::System.IDisposable
                {
                    void global::System.IDisposable.Dispose() { }
                }
            }
            """);
    }

    [Fact]
    public async Task ExplicitInterfaceImplementations()
    {
        await Validate("""
            using System;
            using System.Collections.Generic;

            namespace Demo;

            public interface IMembers
            {
                int Value { get; set; }
                int this[int index] { get; }
                event EventHandler? Changed;
                void M();
            }

            public interface IMembers<T>
            {
                T Value { get; set; }
                T this[T index] { get; set; }
                event EventHandler<T>? Changed;
                T M(T value);
                void Generic<U>(U value) where U : T;
            }

            public class Sample : IMembers, IMembers<string>, IEnumerable<int>, IComparable<Sample[]>
            {
                int IMembers.Value { get => 0; set { } }
                int IMembers.this[int index] => 0;
                event EventHandler? IMembers.Changed { add { } remove { } }
                void IMembers.M() { }

                string IMembers<string>.Value { get => ""; set { } }
                string IMembers<string>.this[string index] { get => ""; set { } }
                event EventHandler<string>? IMembers<string>.Changed { add { } remove { } }
                string IMembers<string>.M(string value) => value;
                void IMembers<string>.Generic<U>(U value) { }

                IEnumerator<int> IEnumerable<int>.GetEnumerator() => null!;
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null!;
                int IComparable<Sample[]>.CompareTo(Sample[]? other) => 0;
            }

            public class GenericSample<T> : IMembers<T>, IMembers<List<T[]>>
            {
                T IMembers<T>.Value { get => default!; set { } }
                T IMembers<T>.this[T index] { get => default!; set { } }
                event EventHandler<T>? IMembers<T>.Changed { add { } remove { } }
                T IMembers<T>.M(T value) => value;
                void IMembers<T>.Generic<U>(U value) { }

                List<T[]> IMembers<List<T[]>>.Value { get => null!; set { } }
                List<T[]> IMembers<List<T[]>>.this[List<T[]> index] { get => null!; set { } }
                event EventHandler<List<T[]>>? IMembers<List<T[]>>.Changed { add { } remove { } }
                List<T[]> IMembers<List<T[]>>.M(List<T[]> value) => value;
                void IMembers<List<T[]>>.Generic<U>(U value) { }
            }

            public struct SampleStruct : IMembers
            {
                readonly int IMembers.Value { get => 0; set { } }
                int IMembers.this[int index] => 0;
                event EventHandler? IMembers.Changed { add { } remove { } }
                void IMembers.M() { }
            }

            public interface IDerived<T> : IMembers
            {
                int IMembers.Value { get => 0; set { } }
                void IMembers.M() { }
            }
            """, """
            #nullable enable

            namespace Demo
            {
                public class GenericSample<T> : global::Demo.IMembers<System.Collections.Generic.List<T[]>>, global::Demo.IMembers<T>
                {
                    T global::Demo.IMembers<T>.Value { get => throw null; set { } }
                    T global::Demo.IMembers<T>.this[T index] { get => throw null; set { } }
                    System.Collections.Generic.List<T[]> global::Demo.IMembers<System.Collections.Generic.List<T[]>>.Value { get => throw null; set { } }
                    System.Collections.Generic.List<T[]> global::Demo.IMembers<System.Collections.Generic.List<T[]>>.this[System.Collections.Generic.List<T[]> index] { get => throw null; set { } }
                    event System.EventHandler<T>? global::Demo.IMembers<T>.Changed { add { } remove { } }
                    event System.EventHandler<System.Collections.Generic.List<T[]>>? global::Demo.IMembers<System.Collections.Generic.List<T[]>>.Changed { add { } remove { } }
                    T global::Demo.IMembers<T>.M(T value) => throw null;
                    void global::Demo.IMembers<T>.Generic<U>(U value) { }
                    System.Collections.Generic.List<T[]> global::Demo.IMembers<System.Collections.Generic.List<T[]>>.M(System.Collections.Generic.List<T[]> value) => throw null;
                    void global::Demo.IMembers<System.Collections.Generic.List<T[]>>.Generic<U>(U value) { }
                }

                public interface IDerived<T> : global::Demo.IMembers
                {
                    int global::Demo.IMembers.Value { get => throw null; set { } }
                    void global::Demo.IMembers.M() { }
                }

                public interface IMembers
                {
                    int Value { get; set; }
                    int this[int index] { get; }
                    public event System.EventHandler? Changed;
                    void M();
                }

                public interface IMembers<T>
                {
                    T Value { get; set; }
                    T this[T index] { get; set; }
                    public event System.EventHandler<T>? Changed;
                    T M(T value);
                    void Generic<U>(U value) where U : T;
                }

                public class Sample : global::Demo.IMembers, global::Demo.IMembers<string>, System.Collections.Generic.IEnumerable<int>, System.Collections.IEnumerable, System.IComparable<global::Demo.Sample[]>
                {
                    int global::Demo.IMembers.Value { get => throw null; set { } }
                    int global::Demo.IMembers.this[int index] { get => throw null; }
                    string global::Demo.IMembers<System.String>.Value { get => throw null; set { } }
                    string global::Demo.IMembers<System.String>.this[string index] { get => throw null; set { } }
                    event System.EventHandler? global::Demo.IMembers.Changed { add { } remove { } }
                    event System.EventHandler<string>? global::Demo.IMembers<System.String>.Changed { add { } remove { } }
                    void global::Demo.IMembers.M() { }
                    string global::Demo.IMembers<System.String>.M(string value) => throw null;
                    void global::Demo.IMembers<System.String>.Generic<U>(U value) { }
                    System.Collections.Generic.IEnumerator<int> System.Collections.Generic.IEnumerable<System.Int32>.GetEnumerator() => throw null;
                    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw null;
                    int System.IComparable<global::Demo.Sample[]>.CompareTo(global::Demo.Sample[]? other) => throw null;
                }

                public struct SampleStruct : global::Demo.IMembers
                {
                    readonly int global::Demo.IMembers.Value { get => throw null; set { } }
                    int global::Demo.IMembers.this[int index] { get => throw null; }
                    event System.EventHandler? global::Demo.IMembers.Changed { add { } remove { } }
                    void global::Demo.IMembers.M() { }
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
    public async Task NestedTypes_NonGenericTypeInGenericType()
    {
        await Validate("""
            namespace Demo;

            public class Base<T>
            {
                public class Nested { }
            }
            """, """
            #nullable enable

            namespace Demo
            {
                public class Base<T>
                {
                    public class Nested
                    {
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task NestedTypes_InGenericType_DeclareOnlyTheirOwnGenericParameters()
    {
        await Validate("""
            namespace Demo;

            public class Base<T> where T : class
            {
                public class Nested
                {
                    public T Value => default!;

                    public class Deep { }
                }

                public class NestedGeneric<U> where U : struct
                {
                    public T M(U value) => default!;

                    public class Deep { }
                    public class DeepGeneric<V> { }
                }

                public struct NestedStruct { }
                public interface INested { }
                public enum NestedEnum { A }
                public delegate T NestedDelegate(T value);
                public delegate T NestedGenericDelegate<U>(U value) where U : struct;
            }
            """, """
            #nullable enable

            namespace Demo
            {
                public class Base<T> where T : class
                {
                    public interface INested
                    {
                    }
                    public class Nested
                    {
                        public T Value { get => throw null; }
                        public class Deep
                        {
                        }
                    }
                    public delegate T NestedDelegate(T value);
                    public enum NestedEnum
                    {
                        A = 0,
                    }
                    public delegate T NestedGenericDelegate<U>(U value) where U : struct;
                    public class NestedGeneric<U> where U : struct
                    {
                        public T M(U value) => throw null;
                        public class Deep
                        {
                        }
                        public class DeepGeneric<V>
                        {
                        }
                    }
                    public struct NestedStruct
                    {
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task NestedTypes_References()
    {
        await Validate("""
            using System.Collections.Generic;

            namespace Demo;

            public class Outer
            {
                public class Inner
                {
                    public class Deep { }

                    public Deep CreateDeep() => null!;
                }

                public class InnerGeneric<U> { }
                public interface IInner { void M(); }
                public enum InnerEnum { A, B }
                public struct InnerStruct { }
                public delegate void InnerHandler(Inner value);

                public Inner Create() => null!;
                public InnerGeneric<int> CreateGeneric() => null!;
            }

            [System.Diagnostics.DebuggerTypeProxy(typeof(Outer.Inner.Deep))]
            public class Sample : Outer.Inner, Outer.IInner
            {
                public Outer.InnerStruct? Field;

                [System.ComponentModel.DefaultValue(Outer.InnerEnum.B)]
                public Outer.InnerEnum Property { get; set; }
                public event Outer.InnerHandler? Changed;

                public Outer.Inner.Deep[] A(Outer.InnerGeneric<Outer.Inner?> value) => null!;
                public Dictionary<string, Outer.Inner>.Enumerator B() => default;
                public List<Outer.Inner?>.Enumerator C() => default;
                public (Outer.Inner, Outer.InnerEnum) D() => default;
                public void E<T>(T value) where T : Outer.Inner, Outer.IInner { }
                void Outer.IInner.M() { }
            }

            public static class Extensions
            {
                public static void Extend(this Outer.Inner value) { }
            }
            """, """
            #nullable enable

            namespace Demo
            {
                public static class Extensions
                {
                    public static void Extend(this global::Demo.Outer.Inner value) { }
                }

                public class Outer
                {
                    public global::Demo.Outer.Inner Create() => throw null;
                    public global::Demo.Outer.InnerGeneric<int> CreateGeneric() => throw null;
                    public interface IInner
                    {
                        void M();
                    }
                    public class Inner
                    {
                        public global::Demo.Outer.Inner.Deep CreateDeep() => throw null;
                        public class Deep
                        {
                        }
                    }
                    public enum InnerEnum
                    {
                        A = 0,
                        B = 1,
                    }
                    public class InnerGeneric<U>
                    {
                    }
                    public delegate void InnerHandler(global::Demo.Outer.Inner value);
                    public struct InnerStruct
                    {
                    }
                }

                [System.Diagnostics.DebuggerTypeProxy(typeof(global::Demo.Outer.Inner.Deep))]
                public class Sample : global::Demo.Outer.Inner, global::Demo.Outer.IInner
                {
                    public global::Demo.Outer.InnerStruct? Field;
                    [System.ComponentModel.DefaultValue(global::Demo.Outer.InnerEnum.B)]
                    public global::Demo.Outer.InnerEnum Property { get => throw null; set { } }
                    public event global::Demo.Outer.InnerHandler? Changed;
                    public global::Demo.Outer.Inner.Deep[] A(global::Demo.Outer.InnerGeneric<global::Demo.Outer.Inner?> value) => throw null;
                    public System.Collections.Generic.Dictionary<string, global::Demo.Outer.Inner>.Enumerator B() => throw null;
                    public System.Collections.Generic.List<global::Demo.Outer.Inner?>.Enumerator C() => throw null;
                    public System.ValueTuple<global::Demo.Outer.Inner, global::Demo.Outer.InnerEnum> D() => throw null;
                    public void E<T>(T value) where T : global::Demo.Outer.Inner, global::Demo.Outer.IInner { }
                    void global::Demo.Outer.IInner.M() { }
                }
            }
            """);
    }

    [Fact]
    public async Task NestedTypes_InGenericType_References()
    {
        await Validate("""
            namespace Demo;

            public class Base<T>
            {
                public class Nested
                {
                    public Nested Self => this;
                }

                public class NestedGeneric<U>
                {
                    public class Deep { }
                    public class DeepGeneric<V> { }

                    public Deep CreateDeep() => null!;
                }

                public struct NestedStruct { }
                public enum NestedEnum { A, B }
                public interface INested { T M(); }
                public delegate T NestedDelegate(T value);

                public event NestedDelegate? Changed;
                public Nested Create() => null!;
                public NestedGeneric<int> CreateGeneric() => null!;
                public Base<int>.Nested CreateConstructed() => null!;
            }

            [System.Diagnostics.DebuggerTypeProxy(typeof(Base<Sample>.NestedGeneric<Sample>.Deep))]
            public class Sample : Base<string>.Nested, Base<int>.INested
            {
                public Base<int>.NestedStruct? Field;
                public Base<Sample>.NestedDelegate? Handler;

                [System.ComponentModel.DefaultValue(Base<Sample>.NestedEnum.B)]
                public Base<string>.NestedEnum Property { get; set; }

                public int M() => 0;
                public Base<int>.Nested A() => null!;
                public Base<string?>.NestedGeneric<long>? B() => null;
                public Base<int>.NestedGeneric<long>.Deep C() => null!;
                public Base<int>.NestedGeneric<long>.DeepGeneric<string?>[] D() => null!;
                public (Base<int>.Nested, Base<string>.NestedEnum) E() => default;
                public void F<TArg>(TArg value) where TArg : Base<TArg>.Nested { }
            }

            public class Derived<T> : Base<T>.NestedGeneric<int>.DeepGeneric<T[]> { }

            public static class Extensions
            {
                public static void Extend(this Base<int>.Nested value) { }
            }
            """, """
            #nullable enable

            namespace Demo
            {
                public class Base<T>
                {
                    public event global::Demo.Base<T>.NestedDelegate? Changed;
                    public global::Demo.Base<T>.Nested Create() => throw null;
                    public global::Demo.Base<T>.NestedGeneric<int> CreateGeneric() => throw null;
                    public global::Demo.Base<int>.Nested CreateConstructed() => throw null;
                    public interface INested
                    {
                        T M();
                    }
                    public class Nested
                    {
                        public global::Demo.Base<T>.Nested Self { get => throw null; }
                    }
                    public delegate T NestedDelegate(T value);
                    public enum NestedEnum
                    {
                        A = 0,
                        B = 1,
                    }
                    public class NestedGeneric<U>
                    {
                        public global::Demo.Base<T>.NestedGeneric<U>.Deep CreateDeep() => throw null;
                        public class Deep
                        {
                        }
                        public class DeepGeneric<V>
                        {
                        }
                    }
                    public struct NestedStruct
                    {
                    }
                }

                public class Derived<T> : global::Demo.Base<T>.NestedGeneric<int>.DeepGeneric<T[]>
                {
                }

                public static class Extensions
                {
                    public static void Extend(this global::Demo.Base<int>.Nested value) { }
                }

                [System.Diagnostics.DebuggerTypeProxy(typeof(global::Demo.Base<global::Demo.Sample>.NestedGeneric<global::Demo.Sample>.Deep))]
                public class Sample : global::Demo.Base<string>.Nested, global::Demo.Base<int>.INested
                {
                    public global::Demo.Base<int>.NestedStruct? Field;
                    public global::Demo.Base<global::Demo.Sample>.NestedDelegate? Handler;
                    [System.ComponentModel.DefaultValue(global::Demo.Base<global::Demo.Sample>.NestedEnum.B)]
                    public global::Demo.Base<string>.NestedEnum Property { get => throw null; set { } }
                    public int M() => throw null;
                    public global::Demo.Base<int>.Nested A() => throw null;
                    public global::Demo.Base<string?>.NestedGeneric<long>? B() => throw null;
                    public global::Demo.Base<int>.NestedGeneric<long>.Deep C() => throw null;
                    public global::Demo.Base<int>.NestedGeneric<long>.DeepGeneric<string?>[] D() => throw null;
                    public System.ValueTuple<global::Demo.Base<int>.Nested, global::Demo.Base<string>.NestedEnum> E() => throw null;
                    public void F<TArg>(TArg value) where TArg : global::Demo.Base<TArg>.Nested { }
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
    public async Task Inheritance_BaseTypeIsFirstAndInterfacesAreSorted()
    {
        await Validate("""
            public interface IZeta { }
            public interface IAlpha { }
            public interface IMiddle { }

            public abstract class TypeDeclaration { }

            public class Sample : TypeDeclaration, IZeta, IMiddle, IAlpha
            {
            }

            public interface IDerived : IZeta, IMiddle, IAlpha
            {
            }
            """, """
            #nullable enable

            public interface IAlpha
            {
            }


            public interface IDerived : IAlpha, IMiddle, IZeta
            {
            }


            public interface IMiddle
            {
            }


            public interface IZeta
            {
            }


            public class Sample : TypeDeclaration, IAlpha, IMiddle, IZeta
            {
            }


            public abstract class TypeDeclaration
            {
            }
            """);
    }

    [Fact]
    public async Task Inheritance_NonVisibleInterfacesAreExcluded()
    {
        await Validate("""
            public interface IPublic { }
            public interface IPublicGeneric<T> { }
            internal interface IInternal { }
            internal interface IInternalGeneric<T> { }

            internal static class Container
            {
                public interface INested { }
            }

            public sealed class Sample : IInternal, IPublic, IInternalGeneric<int>, IPublicGeneric<string>, Container.INested
            {
            }
            """, """
            #nullable enable

            public interface IPublic
            {
            }


            public interface IPublicGeneric<T>
            {
            }


            public sealed class Sample : IPublic, IPublicGeneric<string>
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
    public async Task Delegate_FollowedByClass()
    {
        await Validate("""
            public delegate int SimpleDelegate(int value);

            public class Sample
            {
                public int Method(int value) => value;
            }
            """, """
            #nullable enable

            public class Sample
            {
                public int Method(int value) => throw null;
            }


            public delegate int SimpleDelegate(int value);
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
    public async Task MemorySafetyRules_UnsafeMembers()
    {
        await Validate("""
            public class Sample
            {
                public unsafe int Field;

                public unsafe Sample() { }

                public unsafe void Method() { }

                public unsafe int Property { get => 0; set { } }

                public int PropertyWithUnsafeGetter { unsafe get => 0; set { } }

                public unsafe event System.Action? Event;

                public static unsafe int operator +(Sample left, Sample right) => 0;
            }
            """, """
            #nullable enable

            public class Sample
            {
                public unsafe int Field;
                public unsafe int Property { get => throw null; set { } }
                public int PropertyWithUnsafeGetter { unsafe get => throw null; set { } }
                public unsafe event System.Action? Event;
                public unsafe Sample() { }
                public unsafe void Method() { }
                public static unsafe int operator +(Sample left, Sample right) => throw null;
            }
            """, compilerOptions: new CompilerOptions
        {
            UpdatedMemorySafetyRules = true,
        });
    }

    [Fact]
    public async Task MemorySafetyRules_SafeMembersWithPointers()
    {
        await Validate("""
            public class Sample
            {
                public int* Field;

                public int* Property { get => null; set { } }

                public int* Method(int* value) => value;
            }
            """, """
            #nullable enable

            public class Sample
            {
                public int* Field;
                public int* Property { get => throw null; set { } }
                public int* Method(int* value) => throw null;
            }
            """, compilerOptions: new CompilerOptions
        {
            UpdatedMemorySafetyRules = true,
        });
    }

    [Fact]
    public async Task MemorySafetyRules_Delegate_Pointer()
    {
        await Validate("""
            public delegate int* PointerDelegate(int* value);
            """, """
            #nullable enable

            public delegate int* PointerDelegate(int* value);
            """, compilerOptions: new CompilerOptions
        {
            UpdatedMemorySafetyRules = true,
        });
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
        Assert.Equal("Source.cs", file.RelativePath);
        Assert.Contains("public class GlobalType", file.Content);
        Assert.Contains("namespace Demo", file.Content);
        Assert.Contains("public class Other", file.Content);
        Assert.Contains("public class Sample", file.Content);
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
        Assert.Contains("public class Other", demoFile.Content);
        Assert.Contains("public class Sample", demoFile.Content);
        Assert.Contains("public class GlobalType", globalNamespaceFile.Content);
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
        Assert.Contains("public class Other", files.Single(static file => file.RelativePath == "Demo.Other.g.cs").Content);
        Assert.Contains("public class Sample", files.Single(static file => file.RelativePath == "Demo.Sample.g.cs").Content);
        Assert.Contains("public class GlobalType", files.Single(static file => file.RelativePath == "GlobalType.g.cs").Content);
    }

    [Fact]
    public async Task FileLayout_OneFilePerType_ThrowsWhenFileNamesCollide()
    {
        // Foo and foo are distinct types, but they map to the same file name on a case-insensitive file system
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => BuildFiles("""
            namespace Demo;

            public class Foo
            {
            }

            public class foo
            {
            }
            """, new PublicApiOptions
        {
            FileLayout = PublicApiFileLayout.OneFilePerType,
            IncludeAutoGeneratedComment = false,
        }));

        Assert.Contains("Demo.foo.g.cs", exception.Message);
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
            // Target Frameworks: net8.0, netstandard2.0
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
            // Target Frameworks: net8.0, netstandard2.0
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
        Assert.Contains($"#if {expectedSymbol}", content);
        Assert.Contains("public void B() { }", content);
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
            // Target Frameworks: net8.0, netstandard2.0
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
    public async Task MultiTarget_ConditionalBlocks_AreDeterministicallySorted()
    {
        var files = await BuildFiles(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["net8.0"] = """
                    public class Sample
                    {
                        public long B() => 0;
                    }
                    """,
                ["netstandard2.0"] = """
                    public class Sample
                    {
                        public int B() => 0;
                    }
                    """,
                ["net10.0"] = """
                    public class Sample
                    {
                        public short B() => 0;
                    }
                    """,
            },
            new PublicApiOptions
            {
                FileLayout = PublicApiFileLayout.SingleFile,
                IncludeAutoGeneratedComment = false,
            });

        InlineSnapshot.Validate(Assert.Single(files).Content.TrimEnd('\r', '\n'), """
            // Target Frameworks: net10.0, net8.0, netstandard2.0
            #nullable enable

            public class Sample
            {
                #if NET10_0
                public short B() => throw null;
                #elif NET8_0
                public long B() => throw null;
                #elif NETSTANDARD2_0
                public int B() => throw null;
                #endif
            }
            """);
    }

    [Fact]
    public async Task MultiTarget_TypeAttributes_Differs()
    {
        var files = await BuildFiles(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["net10.0"] = """
                    [System.Obsolete("Use TextWriter.CreateBroadcasting", true)]
                    public sealed class TeeTextWriter
                    {
                        public System.Text.Encoding Encoding { get; }
                        public void Flush() { }
                    }
                    """,
                ["net8.0"] = """
                    public sealed class TeeTextWriter
                    {
                        public System.Text.Encoding Encoding { get; }
                        public void Flush() { }
                    }
                    """,
            },
            new PublicApiOptions
            {
                FileLayout = PublicApiFileLayout.SingleFile,
                IncludeAutoGeneratedComment = false,
            });

        InlineSnapshot.Validate(Assert.Single(files).Content.TrimEnd('\r', '\n'), """
            // Target Frameworks: net10.0, net8.0
            #nullable enable

            #if NET10_0
            [System.Obsolete("Use TextWriter.CreateBroadcasting", true)]
            #endif
            public sealed class TeeTextWriter
            {
                public System.Text.Encoding Encoding { get => throw null; }
                public void Flush() { }
            }
            """);
    }

    [Fact]
    public async Task MultiTarget_AssemblyRequiresPreviewFeaturesAttribute_Differs()
    {
        var files = await BuildFiles(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["net10.0"] = """
                    [assembly: System.Runtime.Versioning.RequiresPreviewFeaturesAttribute("Preview assembly")]

                    public class Sample
                    {
                    }
                    """,
                ["net8.0"] = """
                    public class Sample
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
            // Target Frameworks: net10.0, net8.0
            #if NET10_0
            [assembly: System.Runtime.Versioning.RequiresPreviewFeatures("Preview assembly")]
            #endif
            #nullable enable

            public class Sample
            {
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
            // Target Frameworks: net8.0, netstandard2.0
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
    public async Task MultiTarget_InterfaceOnlyInOneTargetFramework()
    {
        var files = await BuildFiles(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["netstandard2.0"] = """
                    public interface IMarker { }

                    public sealed class Sample : IMarker
                    {
                        public void A() { }
                    }
                    """,
                ["net8.0"] = """
                    public interface IMarker { }

                    public sealed class Sample : IMarker, System.IDisposable
                    {
                        public void A() { }
                        public void Dispose() { }
                    }
                    """,
            },
            new PublicApiOptions
            {
                FileLayout = PublicApiFileLayout.SingleFile,
                IncludeAutoGeneratedComment = false,
            });

        InlineSnapshot.Validate(Assert.Single(files).Content.TrimEnd('\r', '\n'), """
            // Target Frameworks: net8.0, netstandard2.0
            #nullable enable

            public interface IMarker
            {
            }


            public sealed class Sample : IMarker
            #if NET8_0
                , System.IDisposable
            #endif
            {
                public void A() { }
                #if NET8_0
                public void Dispose() { }
                #endif
            }
            """);
    }

    [Fact]
    public async Task MultiTarget_BaseTypeListOnlyInOneTargetFramework()
    {
        var files = await BuildFiles(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["netstandard2.0"] = """
                    public sealed class Sample
                    {
                        public void A() { }
                    }
                    """,
                ["net8.0"] = """
                    public sealed class Sample : System.IDisposable
                    {
                        public void A() { }
                        public void Dispose() { }
                    }
                    """,
            },
            new PublicApiOptions
            {
                FileLayout = PublicApiFileLayout.SingleFile,
                IncludeAutoGeneratedComment = false,
            });

        InlineSnapshot.Validate(Assert.Single(files).Content.TrimEnd('\r', '\n'), """
            // Target Frameworks: net8.0, netstandard2.0
            #nullable enable

            public sealed class Sample
            #if NET8_0
                : System.IDisposable
            #endif
            {
                public void A() { }
                #if NET8_0
                public void Dispose() { }
                #endif
            }
            """);
    }

    [Fact]
    public async Task MultiTarget_BaseTypeListDiffersFromItsFirstEntry()
    {
        var files = await BuildFiles(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["netstandard2.0"] = """
                    public interface IAlpha { }
                    public interface IZeta { }

                    public sealed class Sample : IZeta
                    {
                    }
                    """,
                ["net8.0"] = """
                    public interface IAlpha { }
                    public interface IZeta { }

                    public sealed class Sample : IAlpha, IZeta
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
            // Target Frameworks: net8.0, netstandard2.0
            #nullable enable

            public interface IAlpha
            {
            }


            public interface IZeta
            {
            }


            public sealed class Sample
            #if NET8_0
                : IAlpha, IZeta
            #elif NETSTANDARD2_0
                : IZeta
            #endif
            {
            }
            """);
    }

    [Fact]
    public async Task MultiTarget_BaseTypeListDiffers_GenericConstraintsMoveAfterTheBaseTypes()
    {
        var files = await BuildFiles(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["netstandard2.0"] = """
                    public interface IMarker { }

                    public sealed class Sample<T> : IMarker where T : struct
                    {
                    }
                    """,
                ["net8.0"] = """
                    public interface IMarker { }

                    public sealed class Sample<T> : IMarker, System.IDisposable where T : struct
                    {
                        public void Dispose() { }
                    }
                    """,
            },
            new PublicApiOptions
            {
                FileLayout = PublicApiFileLayout.SingleFile,
                IncludeAutoGeneratedComment = false,
            });

        InlineSnapshot.Validate(Assert.Single(files).Content.TrimEnd('\r', '\n'), """
            // Target Frameworks: net8.0, netstandard2.0
            #nullable enable

            public interface IMarker
            {
            }


            public sealed class Sample<T> : IMarker
            #if NET8_0
                , System.IDisposable
            #endif
                where T : struct
            {
                #if NET8_0
                public void Dispose() { }
                #endif
            }
            """);
    }

    [Fact]
    public async Task MultiTarget_TypeModifiersDiffer_KeepsTheWholeTypeConditional()
    {
        var files = await BuildFiles(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["netstandard2.0"] = """
                    public class Sample
                    {
                    }
                    """,
                ["net8.0"] = """
                    public sealed class Sample
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
            // Target Frameworks: net8.0, netstandard2.0
            #nullable enable

            #if NET8_0
            public sealed class Sample
            {
            }
            #elif NETSTANDARD2_0
            public class Sample
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
        InlineSnapshot.Validate(File.ReadAllText(outputDirectory / "Source.cs").TrimEnd('\r', '\n'), """
            // Target Frameworks: net8.0, netstandard2.0
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
    public async Task Tool_SingleFile_AllowsCustomOutputFileName()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var assemblyPath = await CompileSource(temporaryDirectory, "net8", "net8.0", """
            public class Sample
            {
                public void A()
                {
                }
            }
            """);

        var outputFilePath = temporaryDirectory / "output" / "CustomPublicApi.cs";
        var exitCode = await Program.MainImpl(
            [
                "--input", assemblyPath.ToString(),
                "--output-file", outputFilePath.ToString(),
                "--file-layout", nameof(PublicApiFileLayout.SingleFile),
                "--omit-auto-generated-comment",
            ],
            configure: null);

        Assert.Equal(0, exitCode);
        InlineSnapshot.Validate(File.ReadAllText(outputFilePath).TrimEnd('\r', '\n'), """
            // Target Frameworks: net8.0
            #nullable enable

            public class Sample
            {
                public void A() { }
            }
            """);
    }

    [Fact]
    public async Task Tool_SingleFile_AllowsOutputDirectory()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var assemblyPath = await CompileSource(temporaryDirectory, "net8", "net8.0", """
            public class Sample
            {
                public void A()
                {
                }
            }
            """);

        var outputDirectory = temporaryDirectory / "output";
        var exitCode = await Program.MainImpl(
            [
                "--input", assemblyPath.ToString(),
                "--output", outputDirectory.ToString(),
                "--file-layout", nameof(PublicApiFileLayout.SingleFile),
                "--omit-auto-generated-comment",
            ],
            configure: null);

        Assert.Equal(0, exitCode);
        InlineSnapshot.Validate(File.ReadAllText(outputDirectory / "Source.cs").TrimEnd('\r', '\n'), """
            // Target Frameworks: net8.0
            #nullable enable

            public class Sample
            {
                public void A() { }
            }
            """);
    }

    [Fact]
    public async Task Tool_SingleFile_OutputAndOutputFile_AreMutuallyExclusive()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var assemblyPath = await CompileSource(temporaryDirectory, "net8", "net8.0", """
            public class Sample
            {
            }
            """);

        var outputDirectory = temporaryDirectory / "output";
        var outputFilePath = outputDirectory / "CustomPublicApi.cs";
        var exitCode = await Program.MainImpl(
            [
                "--input", assemblyPath.ToString(),
                "--output", outputDirectory.ToString(),
                "--output-file", outputFilePath.ToString(),
                "--file-layout", nameof(PublicApiFileLayout.SingleFile),
                "--omit-auto-generated-comment",
            ],
            configure: null);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Tool_NonSingleFile_DoesNotAllowOutputFile()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var assemblyPath = await CompileSource(temporaryDirectory, "net8", "net8.0", """
            namespace Demo;
            public class Sample
            {
            }
            """);

        var outputFilePath = temporaryDirectory / "output" / "CustomPublicApi.cs";
        var exitCode = await Program.MainImpl(
            [
                "--input", assemblyPath.ToString(),
                "--output-file", outputFilePath.ToString(),
                "--file-layout", nameof(PublicApiFileLayout.OneFilePerNamespace),
                "--omit-auto-generated-comment",
            ],
            configure: null);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Tool_VerifyNoChangeSingleFile_SucceedsWhenFileIsUpToDate()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var assemblyPath = await CompileSource(temporaryDirectory, "net8", "net8.0", """
            public class Sample
            {
                public void A()
                {
                }
            }
            """);

        var outputFilePath = temporaryDirectory / "output" / "PublicApi.g.cs";
        var generateExitCode = await Program.MainImpl(
            [
                "--input", assemblyPath.ToString(),
                "--output-file", outputFilePath.ToString(),
                "--file-layout", nameof(PublicApiFileLayout.SingleFile),
                "--omit-auto-generated-comment",
            ],
            configure: null);

        Assert.Equal(0, generateExitCode);

        var verifyNoChangeExitCode = await Program.MainImpl(
            [
                "--input", assemblyPath.ToString(),
                "--output-file", outputFilePath.ToString(),
                "--file-layout", nameof(PublicApiFileLayout.SingleFile),
                "--omit-auto-generated-comment",
                "--verify-no-change",
            ],
            configure: null);

        Assert.Equal(0, verifyNoChangeExitCode);
    }

    [Fact]
    public async Task Tool_VerifyNoChangeSingleFile_FailsWhenFileIsOutOfDate()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var assemblyPath = await CompileSource(temporaryDirectory, "net8", "net8.0", """
            public class Sample
            {
                public void A()
                {
                }
            }
            """);

        var outputFilePath = temporaryDirectory / "output" / "PublicApi.g.cs";
        var generateExitCode = await Program.MainImpl(
            [
                "--input", assemblyPath.ToString(),
                "--output-file", outputFilePath.ToString(),
                "--file-layout", nameof(PublicApiFileLayout.SingleFile),
                "--omit-auto-generated-comment",
            ],
            configure: null);

        Assert.Equal(0, generateExitCode);

        File.WriteAllText(outputFilePath, "// stale");

        var verifyNoChangeExitCode = await Program.MainImpl(
            [
                "--input", assemblyPath.ToString(),
                "--output-file", outputFilePath.ToString(),
                "--file-layout", nameof(PublicApiFileLayout.SingleFile),
                "--omit-auto-generated-comment",
                "--verify-no-change",
            ],
            configure: null);

        Assert.Equal(1, verifyNoChangeExitCode);
        Assert.Equal("// stale", File.ReadAllText(outputFilePath));
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
    public async Task Method_ParameterName_Keyword_IsEscaped()
    {
        await Validate("""
            public static class Sample
            {
                public static System.Guid Create(System.Guid @namespace) => default;
            }
            """, """
            #nullable enable

            public static class Sample
            {
                public static System.Guid Create(System.Guid @namespace) => throw null;
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
    public async Task Method_Parameter_ScopedModifiers()
    {
        await Validate("""
            using System;

            public static class SampleExtensions
            {
                public static void M(this scoped ref int receiver, scoped Span<int> p0, scoped in int p1, scoped ref int p2, scoped ref readonly int p3)
                {
                }
            }
            """, """
            #nullable enable

            public static class SampleExtensions
            {
                public static void M(this scoped ref int receiver, scoped System.Span<int> p0, scoped in int p1, scoped ref int p2, scoped ref readonly int p3) { }
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
    public async Task Method_NullableAnnotationsOfValueTypesAndArrays()
    {
        await Validate("""
            using System;
            using System.Collections.Generic;

            [System.Diagnostics.DebuggerTypeProxy(typeof(Sample.DebugView))]
            public class Sample
            {
                public Dictionary<int, string?> A() => null!;
                public object?[]? B(object?[] values) => null;
                public ReadOnlyMemory<byte>? C() => null;
                public int[,] D() => null!;
                public unsafe void* E() => null;
            #nullable disable
                public string F(string value) => value;
            #nullable enable
                public string G(string value) => value;

                public sealed class DebugView
                {
                }
            }
            """, """
            #nullable enable

            [System.Diagnostics.DebuggerTypeProxy(typeof(Sample.DebugView))]
            public class Sample
            {
                public System.Collections.Generic.Dictionary<int, string?> A() => throw null;
                public object?[]? B(object?[] values) => throw null;
                public System.ReadOnlyMemory<byte>? C() => throw null;
                public int[,] D() => throw null;
                public unsafe void* E() => throw null;
                #nullable disable
                public string F(string value) => throw null;
                #nullable restore
                public string G(string value) => throw null;
                public sealed class DebugView
                {
                }
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
    public async Task Generate_FromReflection_SupportsConcurrentCalls()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var assemblyPath = await CompileSource(temporaryDirectory, "concurrent", "net8.0", """
            namespace Demo;

            public class Sample
            {
                public string? Text { get; set; }
                public System.Collections.Generic.Dictionary<string, string?>? Values { get; set; }
                public string?[]? Items;
                public event System.EventHandler<string?>? Changed;
                public string? Get(string? key, System.Collections.Generic.IReadOnlyList<string?>? fallbacks = null) => null;
                public void Set(string key, out string? value) => value = null;
            }
            """);

        var assembly = Assembly.LoadFile(assemblyPath);
        var options = new PublicApiOptions { IncludeAutoGeneratedComment = false };

        // The concurrent calls must be the very first ones: a shared context whose cache is already warm hides the race
        var results = new string[64];
        Parallel.For(0, results.Length, index => results[index] = Assert.Single(PublicApi.Generate(assembly, options)).Content);

        var expected = Assert.Single(PublicApi.Generate(assembly, options)).Content;
        Assert.All(results, content => Assert.Equal(expected, content));
    }

    [Fact]
    public async Task FunctionPointers_AndWellKnownValueTypes()
    {
        await Validate("""
            public unsafe class Sample
            {
                public delegate*<int, void> Managed;
                public delegate*<string, int, long> WithArguments;
                public delegate* unmanaged<int, void> Unmanaged;
                public decimal DecimalValue;
                public nint NativeInteger;

                public delegate*<int, void> Method(delegate*<long, void> callback) => null;
            }
            """, """
            #nullable enable

            public class Sample
            {
                public delegate*<int, void> Managed;
                public delegate*<string, int, long> WithArguments;
                public delegate* unmanaged<int, void> Unmanaged;
                public decimal DecimalValue;
                public nint NativeInteger;
                public unsafe delegate*<int, void> Method(delegate*<long, void> callback) => throw null;
            }
            """);
    }

    [Fact]
    public async Task Constants_EscapeSequencesAndTypeSuffixes()
    {
        await Validate("""
            public class Sample
            {
                public const string Text = "line\r\n\t tab \"quote\" \\ back\0null";
                public const string Separators = "\u0085\u2028\u2029";
                public const char NewLine = '\n';
                public const char Apostrophe = '\'';
                public const float Single = 1.5f;
                public const float SingleNaN = float.NaN;
                public const double Double = 1.5;
                public const double DoubleInfinity = double.NegativeInfinity;
                public const long Int64 = 10;
                public const ulong UInt64 = ulong.MaxValue;
                public const uint UInt32 = uint.MaxValue;

                public void Defaults(string text = "a\nb", char separator = '\t')
                {
                }
            }
            """, """
            #nullable enable

            public class Sample
            {
                public const string Text = "line\r\n\t tab \"quote\" \\ back\0null";
                public const string Separators = "\u0085\u2028\u2029";
                public const char NewLine = '\n';
                public const char Apostrophe = '\'';
                public const float Single = 1.5f;
                public const float SingleNaN = float.NaN;
                public const double Double = 1.5d;
                public const double DoubleInfinity = double.NegativeInfinity;
                public const long Int64 = 10L;
                public const ulong UInt64 = 18446744073709551615UL;
                public const uint UInt32 = 4294967295U;
                public void Defaults(string text = "a\nb", char separator = '\t') { }
            }
            """);
    }

    [Fact]
    public async Task StructMembers_ReadOnlyModifier()
    {
        await Validate("""
            public struct Mutable
            {
                public int Field;
                public readonly int Get() => 0;
                public readonly override string ToString() => "";
                public void Mutate() { }
                public int Auto { get; set; }
                public readonly int ReadOnlyProperty => 0;
            }

            public readonly struct AlreadyReadOnly
            {
                public int Get() => 0;
                public override string ToString() => "";
                public int Auto { get; }
            }
            """, """
            #nullable enable

            public readonly struct AlreadyReadOnly
            {
                public int Auto { get => throw null; }
                public int Get() => throw null;
                public override string ToString() => throw null;
            }


            public struct Mutable
            {
                public int Field;
                public int Auto { readonly get => throw null; set { } }
                public readonly int ReadOnlyProperty { get => throw null; }
                public readonly int Get() => throw null;
                public readonly override string ToString() => throw null;
                public void Mutate() { }
            }
            """);
    }

    [Fact]
    public async Task PropertiesAndEvents_InheritanceModifiers()
    {
        await Validate("""
            public abstract class AbstractBase
            {
                public abstract int Abstract { get; }
                public virtual int Virtual { get; set; }
                public abstract event System.EventHandler AbstractEvent;
                public virtual event System.EventHandler? VirtualEvent;
                public abstract void AbstractMethod();
            }

            public class Impl : AbstractBase
            {
                public override int Abstract => 0;
                public sealed override int Virtual { get => 0; set { } }
                public override event System.EventHandler AbstractEvent { add { } remove { } }
                public override void AbstractMethod() { }
            }
            """, """
            #nullable enable

            public abstract class AbstractBase
            {
                public abstract int Abstract { get; }
                public virtual int Virtual { get => throw null; set { } }
                public abstract event System.EventHandler AbstractEvent;
                public virtual event System.EventHandler? VirtualEvent;
                public abstract void AbstractMethod();
            }


            public class Impl : AbstractBase
            {
                public override int Abstract { get => throw null; }
                public sealed override int Virtual { get => throw null; set { } }
                public override event System.EventHandler AbstractEvent;
                public override void AbstractMethod() { }
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
    public async Task InterfaceMethod_NullableParameter()
    {
        await Validate("""
            using System.Globalization;

            public interface ILocalizationProvider
            {
                string GetString(string name, CultureInfo? culture);
            }
            """, """
            #nullable enable

            public interface ILocalizationProvider
            {
                string GetString(string name, System.Globalization.CultureInfo? culture);
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
    public async Task MethodParameter_EnumDefaultValues()
    {
        await Validate("""
            namespace Demo;

            public enum ByteEnum : byte { A, B }
            public enum LongEnum : long { A, B = 5000000000 }

            [System.Flags]
            public enum FlagsEnum { None = 0, A = 1, B = 2, C = 4, AB = 3 }

            public class Outer
            {
                public enum InnerEnum { A, B }
            }

            public class Sample
            {
                public void Member(ByteEnum value = ByteEnum.B) { }
                public void DefaultMember(ByteEnum value = default) { }
                public void UndefinedValue(ByteEnum value = (ByteEnum)42) { }
                public void Nullable(ByteEnum? value = ByteEnum.B, ByteEnum? other = null) { }
                public void LongValue(LongEnum value = LongEnum.B) { }
                public void Nested(Outer.InnerEnum value = Outer.InnerEnum.B) { }
                public void Flags(FlagsEnum value = FlagsEnum.A | FlagsEnum.C, FlagsEnum undefined = (FlagsEnum)64) { }
                public void External(System.StringComparison value = System.StringComparison.Ordinal) { }
                public void ExternalFlags(System.IO.FileAttributes value = System.IO.FileAttributes.ReadOnly | System.IO.FileAttributes.Hidden) { }
            }

            public delegate void SampleDelegate(ByteEnum value = ByteEnum.B);
            """, """
            #nullable enable

            namespace Demo
            {
                public enum ByteEnum : byte
                {
                    A = 0,
                    B = 1,
                }

                [System.Flags]
                public enum FlagsEnum
                {
                    None = 0,
                    A = 1,
                    B = 2,
                    C = 4,
                    AB = 3,
                }

                public enum LongEnum : long
                {
                    A = 0L,
                    B = 5000000000L,
                }

                public class Outer
                {
                    public enum InnerEnum
                    {
                        A = 0,
                        B = 1,
                    }
                }

                public class Sample
                {
                    public void Member(global::Demo.ByteEnum value = global::Demo.ByteEnum.B) { }
                    public void DefaultMember(global::Demo.ByteEnum value = global::Demo.ByteEnum.A) { }
                    public void UndefinedValue(global::Demo.ByteEnum value = (global::Demo.ByteEnum)42) { }
                    public void Nullable(global::Demo.ByteEnum? value = global::Demo.ByteEnum.B, global::Demo.ByteEnum? other = null) { }
                    public void LongValue(global::Demo.LongEnum value = global::Demo.LongEnum.B) { }
                    public void Nested(global::Demo.Outer.InnerEnum value = global::Demo.Outer.InnerEnum.B) { }
                    public void Flags(global::Demo.FlagsEnum value = global::Demo.FlagsEnum.A | global::Demo.FlagsEnum.C, global::Demo.FlagsEnum undefined = (global::Demo.FlagsEnum)64) { }
                    public void External(System.StringComparison value = System.StringComparison.Ordinal) { }
                    public void ExternalFlags(System.IO.FileAttributes value = System.IO.FileAttributes.ReadOnly | System.IO.FileAttributes.Hidden) { }
                }

                public delegate void SampleDelegate(global::Demo.ByteEnum value = global::Demo.ByteEnum.B);
            }
            """);
    }

    [Fact]
    public async Task Field_EnumConstants()
    {
        await Validate("""
            namespace Demo;

            public enum ByteEnum : byte { A, B }

            [System.Flags]
            public enum FlagsEnum { None = 0, A = 1, B = 2, C = 4 }

            public class Sample
            {
                public const ByteEnum Member = ByteEnum.B;
                public const ByteEnum UndefinedValue = (ByteEnum)42;
                public const FlagsEnum Flags = FlagsEnum.A | FlagsEnum.C;
                public const System.StringComparison External = System.StringComparison.Ordinal;
                public const int Number = 5;
            }
            """, """
            #nullable enable

            namespace Demo
            {
                public enum ByteEnum : byte
                {
                    A = 0,
                    B = 1,
                }

                [System.Flags]
                public enum FlagsEnum
                {
                    None = 0,
                    A = 1,
                    B = 2,
                    C = 4,
                }

                public class Sample
                {
                    public const global::Demo.ByteEnum Member = global::Demo.ByteEnum.B;
                    public const global::Demo.ByteEnum UndefinedValue = (global::Demo.ByteEnum)42;
                    public const global::Demo.FlagsEnum Flags = global::Demo.FlagsEnum.A | global::Demo.FlagsEnum.C;
                    public const System.StringComparison External = System.StringComparison.Ordinal;
                    public const int Number = 5;
                }
            }
            """);
    }

    [Fact]
    public async Task Method_ReturnAttributes()
    {
        await Validate("""
            using System.Diagnostics.CodeAnalysis;

            public class Sample
            {
                [return: NotNullIfNotNull(nameof(value))]
                public string? A(string? value) => value;

                [return: MaybeNull]
                public T B<T>() => default;

                [System.Obsolete("message")]
                [return: NotNullIfNotNull(nameof(value))]
                public static string? C(string? value) => value;
            }
            """, """
            #nullable enable

            public class Sample
            {
                [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull("value")]
                public string? A(string? value) => throw null;
                [return: System.Diagnostics.CodeAnalysis.MaybeNull]
                public T B<T>() => throw null;
                [System.Obsolete("message")]
                [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull("value")]
                public static string? C(string? value) => throw null;
            }
            """);
    }

    [Fact]
    public async Task MethodParameter_DefaultValues_ValueTypesAndTypeParameters()
    {
        await Validate("""
            using System;
            using System.Collections.Generic;
            using System.Threading;

            namespace Demo;

            public struct SampleStruct { }

            public class Sample
            {
                public void Structs(SampleStruct value = default, CancellationToken cancellationToken = default, DateTime date = default, ReadOnlySpan<char> span = default, (int, int) tuple = default) { }
                public void NullableStructs(SampleStruct? value = null, Guid? guid = null, int? number = null, int? other = 5) { }
                public void TypeParameters<T, TStruct, TClass>(T value = default!, TStruct structValue = default, TClass? classValue = null, TStruct? nullableValue = null) where TStruct : struct where TClass : class { }
                public void References(List<int>? list = null, object? value = null, int[]? array = null) { }
                public void NativeIntegers(nint a = 0, nuint b = 5, IntPtr c = default) { }
            }

            public delegate void SampleDelegate(CancellationToken cancellationToken = default);
            """, """
            #nullable enable

            namespace Demo
            {
                public class Sample
                {
                    public void Structs(global::Demo.SampleStruct value = default, System.Threading.CancellationToken cancellationToken = default, System.DateTime date = default, System.ReadOnlySpan<char> span = default, System.ValueTuple<int, int> tuple = default) { }
                    public void NullableStructs(global::Demo.SampleStruct? value = null, System.Guid? guid = null, int? number = null, int? other = 5) { }
                    public void TypeParameters<T, TStruct, TClass>(T value = default, TStruct structValue = default, TClass? classValue = default, TStruct? nullableValue = null) where TStruct : struct where TClass : class { }
                    public void References(System.Collections.Generic.List<int>? list = null, object? value = null, int[]? array = null) { }
                    public void NativeIntegers(nint a = 0, nuint b = 5U, nint c = 0) { }
                }

                public delegate void SampleDelegate(System.Threading.CancellationToken cancellationToken = default);

                public struct SampleStruct
                {
                }
            }
            """);
    }

    [Fact]
    public async Task MethodParameter_DecimalAndDateTimeDefaultValues()
    {
        await Validate("""
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            public class Sample
            {
                public const decimal Constant = 1.5m;
                public static readonly decimal ReadOnly = 1.5m;

                public void Decimals(decimal a = 1.5m, decimal? b = 2.5m, decimal c = 0, decimal max = 79228162514264337593543950335m, decimal negative = -0.001m) { }
                public void DateTimes([Optional, DateTimeConstant(636517440000000000L)] DateTime date) { }
            }
            """, """
            #nullable enable

            public class Sample
            {
                public const decimal Constant = 1.5m;
                public static readonly decimal ReadOnly;
                public void Decimals(decimal a = 1.5m, decimal? b = 2.5m, decimal c = 0m, decimal max = 79228162514264337593543950335m, decimal negative = -0.001m) { }
                public void DateTimes([System.Runtime.InteropServices.Optional] [System.Runtime.CompilerServices.DateTimeConstant(636517440000000000L)] System.DateTime date) { }
            }
            """);
    }

    [Fact]
    public async Task Tuples_NamedElementsUseTupleSyntax()
    {
        await Validate("""
            using System;
            using System.Collections.Generic;

            namespace Demo;

            public class Sample : List<(int Id, string Name)>
            {
                public (int First, string Second) Field;
                public (int First, string? Second)? NullableField;
                public KeyValuePair<string?, int>? NullableGenericStruct;
                public (int First, string Second) Property { get; set; }
                public event Action<(int First, string Second)>? Changed;

                public (int First, string Second) Method((int A, int B) value) => default;
                public (int, string) Unnamed() => default;
                public ValueTuple<int> SingleElement() => default;
                public (int A, int)? PartiallyNamed() => default;
                public (int A, (string B, int C) D, int) Nested() => default;
                public (int A, int B, int C, int D, int E, int F, int G, int H, int I) Long() => default;
                public (int A, (int X, int Y) B, int C, int D, int E, int F, int G, int H, int I) LongWithNestedTuple() => default;
                public (int, int, int, int, int, int, int, int, int) LongUnnamed() => default;
                public Dictionary<(int X, int Y), (string Name, object? Value)[]> InTypeArguments() => null!;
                public void RefParameters(ref (int A, int B) a, out (int C, int D) b, in (int E, int F) c) => throw null!;
            }

            public delegate (int A, int B) SampleDelegate((int C, int D) value);
            """, """
            #nullable enable

            namespace Demo
            {
                public class Sample : System.Collections.Generic.List<(int Id, string Name)>
                {
                    public (int First, string Second) Field;
                    public (int First, string? Second)? NullableField;
                    public System.Collections.Generic.KeyValuePair<string?, int>? NullableGenericStruct;
                    public (int First, string Second) Property { get => throw null; set { } }
                    public event System.Action<(int First, string Second)>? Changed;
                    public (int First, string Second) Method((int A, int B) value) => throw null;
                    public System.ValueTuple<int, string> Unnamed() => throw null;
                    public System.ValueTuple<int> SingleElement() => throw null;
                    public (int A, int)? PartiallyNamed() => throw null;
                    public (int A, (string B, int C) D, int) Nested() => throw null;
                    public (int A, int B, int C, int D, int E, int F, int G, int H, int I) Long() => throw null;
                    public (int A, (int X, int Y) B, int C, int D, int E, int F, int G, int H, int I) LongWithNestedTuple() => throw null;
                    public System.ValueTuple<int, int, int, int, int, int, int, System.ValueTuple<int, int>> LongUnnamed() => throw null;
                    public System.Collections.Generic.Dictionary<(int X, int Y), (string Name, object? Value)[]> InTypeArguments() => throw null;
                    public void RefParameters(ref (int A, int B) a, out (int C, int D) b, in (int E, int F) c) => throw null;
                }

                public delegate (int A, int B) SampleDelegate((int C, int D) value);
            }
            """);
    }

    [Fact]
    public async Task Tuples_ElementNamesOfInterfacesAndConstraints()
    {
        await Validate("""
            using System;
            using System.Collections.Generic;

            namespace Demo;

            public interface IBase<T>
            {
                T Value { get; }
            }

            public interface IDerived : IBase<(int X, int Y)>
            {
            }

            public class Sample<T> : IEquatable<(int X, string? Y)>, IComparable<(int, int)>, IDerived, IEnumerable<(T Item, int Index)>
                where T : IEquatable<(int A, int B)>
            {
                public (int X, int Y) Value => default;
                public bool Equals((int X, string? Y) other) => false;
                public int CompareTo((int, int) other) => 0;
                public IEnumerator<(T Item, int Index)> GetEnumerator() => null!;
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null!;
            }

            public struct SampleStruct : IEquatable<(int A, (string B, int C) D)>
            {
                public bool Equals((int A, (string B, int C) D) other) => false;
            }

            public class Inherited : Sample<(int A, int B)>, IBase<(string Name, int Count)>
            {
                public new (string Name, int Count) Value => default;
            }
            """, """
            #nullable enable

            namespace Demo
            {
                public interface IBase<T>
                {
                    T Value { get; }
                }

                public interface IDerived : global::Demo.IBase<(int X, int Y)>
                {
                }

                public class Inherited : global::Demo.Sample<(int A, int B)>, global::Demo.IBase<(string Name, int Count)>
                {
                    public (string Name, int Count) Value { get => throw null; }
                }

                public struct SampleStruct : System.IEquatable<(int A, (string B, int C) D)>
                {
                    public bool Equals((int A, (string B, int C) D) other) => throw null;
                }

                public class Sample<T> : global::Demo.IBase<(int X, int Y)>, global::Demo.IDerived, System.Collections.Generic.IEnumerable<(T Item, int Index)>, System.Collections.IEnumerable, System.IComparable<System.ValueTuple<int, int>>, System.IEquatable<(int X, string? Y)> where T : System.IEquatable<(int A, int B)>
                {
                    public (int X, int Y) Value { get => throw null; }
                    public bool Equals((int X, string? Y) other) => throw null;
                    public int CompareTo(System.ValueTuple<int, int> other) => throw null;
                    public System.Collections.Generic.IEnumerator<(T Item, int Index)> GetEnumerator() => throw null;
                    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw null;
                }
            }
            """);
    }

    [Fact]
    public async Task Dynamic_UsesDynamicKeyword()
    {
        await Validate("""
            using System.Collections.Generic;

            public class Sample : List<dynamic>
            {
                public dynamic Field = null!;
                public dynamic? NullableField;
                public dynamic Property { get; set; } = null!;
                public dynamic this[dynamic index] => index;

                public dynamic Method(dynamic value, List<dynamic> list, dynamic[] array, Dictionary<string, dynamic?> map, object notDynamic) => value;
                public void RefParameters(ref dynamic a, out dynamic b, in dynamic c) => throw null!;
                public ref dynamic RefReturn() => throw null!;
                public (dynamic A, object B) InTuple() => default;
                public Dictionary<object, List<dynamic[]>>.Enumerator InNestedType() => default;
            }

            public delegate dynamic SampleDelegate(dynamic value);
            """, """
            #nullable enable

            public class Sample : System.Collections.Generic.List<dynamic>
            {
                public dynamic Field;
                public dynamic? NullableField;
                public dynamic Property { get => throw null; set { } }
                public dynamic this[dynamic index] { get => throw null; }
                public dynamic Method(dynamic value, System.Collections.Generic.List<dynamic> list, dynamic[] array, System.Collections.Generic.Dictionary<string, dynamic?> map, object notDynamic) => throw null;
                public void RefParameters(ref dynamic a, out dynamic b, in dynamic c) => throw null;
                public ref dynamic RefReturn() => throw null;
                public (dynamic A, object B) InTuple() => throw null;
                public System.Collections.Generic.Dictionary<object, System.Collections.Generic.List<dynamic[]>>.Enumerator InNestedType() => throw null;
            }


            public delegate dynamic SampleDelegate(dynamic value);
            """);
    }

    [Fact]
    public async Task ExplicitInterfaceImplementations_NonVisibleInterfacesAreExcluded()
    {
        await Validate("""
            using System;

            namespace Demo;

            internal interface IHidden { void M(); int Property { get; } event EventHandler Changed; }
            internal interface IHidden<T> { void M(T value); }
            public interface IVisible { void M(); }

            public class Sample : IHidden, IHidden<int>, IVisible
            {
                void IHidden.M() { }
                int IHidden.Property => 0;
                event EventHandler IHidden.Changed { add { } remove { } }
                void IHidden<int>.M(int value) { }
                void IVisible.M() { }
            }

            public class Outer<T>
            {
                private interface IPrivate { void M(); }
                protected interface IProtected { void M(); }

                public class Nested : IPrivate, IProtected
                {
                    void IPrivate.M() { }
                    void IProtected.M() { }
                }
            }
            """, """
            #nullable enable

            namespace Demo
            {
                public interface IVisible
                {
                    void M();
                }

                public class Outer<T>
                {
                    protected interface IProtected
                    {
                        void M();
                    }
                    public class Nested : global::Demo.Outer<T>.IProtected
                    {
                        void global::Demo.Outer<T>.IProtected.M() { }
                    }
                }

                public class Sample : global::Demo.IVisible
                {
                    void global::Demo.IVisible.M() { }
                }
            }
            """);
    }

    [Fact]
    public async Task Records_AreWrittenAsTypesWithTheirMembers()
    {
        await Validate("""
            namespace Demo;

            public record Base(int A);
            public record Derived(int A, string B) : Base(A);
            public sealed record Sealed(int A)
            {
                public int Extra { get; init; }
            }

            public abstract record Abstract
            {
                public abstract int Value { get; }
            }

            public record struct Struct(int A);
            public readonly record struct ReadOnlyStruct(int A, string B);
            """, """
            #nullable enable

            namespace Demo
            {
                public abstract class Abstract : System.IEquatable<global::Demo.Abstract>
                {
                    protected virtual System.Type EqualityContract { get => throw null; }
                    public abstract int Value { get; }
                    public override string ToString() => throw null;
                    protected virtual bool PrintMembers(System.Text.StringBuilder builder) => throw null;
                    public static bool operator !=(global::Demo.Abstract? left, global::Demo.Abstract? right) => throw null;
                    public static bool operator ==(global::Demo.Abstract? left, global::Demo.Abstract? right) => throw null;
                    public override int GetHashCode() => throw null;
                    public override bool Equals(object? obj) => throw null;
                    public virtual bool Equals(global::Demo.Abstract? other) => throw null;
                    protected Abstract(global::Demo.Abstract original) { }
                }

                public class Base : System.IEquatable<global::Demo.Base>
                {
                    protected virtual System.Type EqualityContract { get => throw null; }
                    public int A { get => throw null; init { } }
                    public Base(int A) { }
                    public override string ToString() => throw null;
                    protected virtual bool PrintMembers(System.Text.StringBuilder builder) => throw null;
                    public static bool operator !=(global::Demo.Base? left, global::Demo.Base? right) => throw null;
                    public static bool operator ==(global::Demo.Base? left, global::Demo.Base? right) => throw null;
                    public override int GetHashCode() => throw null;
                    public override bool Equals(object? obj) => throw null;
                    public virtual bool Equals(global::Demo.Base? other) => throw null;
                    protected Base(global::Demo.Base original) { }
                    public void Deconstruct(out int A) => throw null;
                }

                public class Derived : global::Demo.Base, System.IEquatable<global::Demo.Derived>
                {
                    protected override System.Type EqualityContract { get => throw null; }
                    public string B { get => throw null; init { } }
                    public Derived(int A, string B) : base(default(int)) { }
                    public override string ToString() => throw null;
                    protected override bool PrintMembers(System.Text.StringBuilder builder) => throw null;
                    public static bool operator !=(global::Demo.Derived? left, global::Demo.Derived? right) => throw null;
                    public static bool operator ==(global::Demo.Derived? left, global::Demo.Derived? right) => throw null;
                    public override int GetHashCode() => throw null;
                    public override bool Equals(object? obj) => throw null;
                    public sealed override bool Equals(global::Demo.Base? other) => throw null;
                    public virtual bool Equals(global::Demo.Derived? other) => throw null;
                    protected Derived(global::Demo.Derived original) : base(default(int)) { }
                    public void Deconstruct(out int A, out string B) => throw null;
                }

                public readonly struct ReadOnlyStruct : System.IEquatable<global::Demo.ReadOnlyStruct>
                {
                    public int A { get => throw null; init { } }
                    public string B { get => throw null; init { } }
                    public ReadOnlyStruct(int A, string B) { }
                    #nullable disable
                    public override string ToString() => throw null;
                    #nullable restore
                    public static bool operator !=(global::Demo.ReadOnlyStruct left, global::Demo.ReadOnlyStruct right) => throw null;
                    public static bool operator ==(global::Demo.ReadOnlyStruct left, global::Demo.ReadOnlyStruct right) => throw null;
                    public override int GetHashCode() => throw null;
                    #nullable disable
                    public override bool Equals(object obj) => throw null;
                    #nullable restore
                    public bool Equals(global::Demo.ReadOnlyStruct other) => throw null;
                    public void Deconstruct(out int A, out string B) => throw null;
                }

                public sealed class Sealed : System.IEquatable<global::Demo.Sealed>
                {
                    public int A { get => throw null; init { } }
                    public int Extra { get => throw null; init { } }
                    public Sealed(int A) { }
                    public override string ToString() => throw null;
                    public static bool operator !=(global::Demo.Sealed? left, global::Demo.Sealed? right) => throw null;
                    public static bool operator ==(global::Demo.Sealed? left, global::Demo.Sealed? right) => throw null;
                    public override int GetHashCode() => throw null;
                    public override bool Equals(object? obj) => throw null;
                    public bool Equals(global::Demo.Sealed? other) => throw null;
                    public void Deconstruct(out int A) => throw null;
                }

                public struct Struct : System.IEquatable<global::Demo.Struct>
                {
                    public int A { readonly get => throw null; set { } }
                    public Struct(int A) { }
                    #nullable disable
                    public readonly override string ToString() => throw null;
                    #nullable restore
                    public static bool operator !=(global::Demo.Struct left, global::Demo.Struct right) => throw null;
                    public static bool operator ==(global::Demo.Struct left, global::Demo.Struct right) => throw null;
                    public readonly override int GetHashCode() => throw null;
                    #nullable disable
                    public readonly override bool Equals(object obj) => throw null;
                    #nullable restore
                    public readonly bool Equals(global::Demo.Struct other) => throw null;
                    public readonly void Deconstruct(out int A) => throw null;
                }
            }
            """);
    }

    [Fact]
    public async Task Members_ConstructorsAndMethodsAreInDeclarationOrder()
    {
        await Validate("""
            public class Sample
            {
                public void First() { }
                public Sample() { }
                public void Second() { }
                public Sample(int value) { }
            }
            """, """
            #nullable enable

            public class Sample
            {
                public void First() { }
                public void Second() { }
                public Sample(int value) { }
            }
            """);
    }

    [Fact]
    public async Task Inheritance_InterfacesOfTheBaseTypeAreNotRepeated()
    {
        await Validate("""
            using System;
            using System.Collections.Generic;

            public class Base : IDisposable
            {
                public void Dispose() { }
            }

            public class Derived : Base, IComparable
            {
                public int CompareTo(object? obj) => 0;
            }

            public class DerivedList : List<int>, ICloneable
            {
                public object Clone() => this;
            }
            """, """
            #nullable enable

            public class Base : System.IDisposable
            {
                public void Dispose() { }
            }


            public class Derived : Base, System.IComparable
            {
                public int CompareTo(object? obj) => throw null;
            }


            public class DerivedList : System.Collections.Generic.List<int>, System.ICloneable
            {
                public object Clone() => throw null;
            }
            """);
    }

    [Fact]
    public async Task Inheritance_NullableAnnotations()
    {
        await Validate("""
            using System;
            using System.Collections.Generic;

            namespace Demo;

            public class Base<T> { }
            public interface IFoo<T> { }
            public interface IDerived : IFoo<string?[]> { }

            public class Sample : Base<string?>, IFoo<string?>, IFoo<int>, IFoo<object>, IEquatable<Sample?>, IEnumerable<KeyValuePair<string, object?>>
            {
                public bool Equals(Sample? other) => false;
                public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => null!;
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null!;
            }

            public class NotAnnotated : Base<string>, IFoo<string>, IFoo<List<string>> { }
            public class GenericSample<T> : Base<T?>, IFoo<T?[]>, IFoo<List<T?>?>, IFoo<T> where T : class { }
            public struct SampleStruct : IFoo<string?[]?>, IFoo<SampleStruct?>, IFoo<(string? Name, int Count)> { }
            public interface ISample<T> : IFoo<T?[]> where T : class { }
            public interface IOther : IFoo<string?>, IFoo<object> { }
            public class ThroughInterface : IDerived { }
            public class Inherited : Sample, IFoo<Dictionary<string, string?>?> { }
            public class WithDynamic : Base<dynamic?>, IFoo<List<object?>> { }

            // Most of the members are nullable, so the compiler stores an "annotated" context on the type
            public class AnnotatedContext : Base<string>, IFoo<object>, IFoo<string?>
            {
                public string? A;
                public string? B;
                public string? C;
            }

            // Most of the members are not nullable, so the compiler stores a "not annotated" context on the type
            public class NotAnnotatedContext : Base<string?>, IFoo<object?>, IFoo<string>
            {
                public string A = "";
                public string B = "";
                public string C = "";
            }

            public class Outer<T>
            {
                public class Nested : Base<T?>, IFoo<Outer<string?>.Nested?> { }
            }

            #nullable disable
            public class Oblivious : Base<string>, IFoo<string> { }
            """, """
            #nullable enable

            namespace Demo
            {
                public class AnnotatedContext : global::Demo.Base<string>, global::Demo.IFoo<object>, global::Demo.IFoo<string?>
                {
                    public string? A;
                    public string? B;
                    public string? C;
                }

                public class Base<T>
                {
                }

                public class GenericSample<T> : global::Demo.Base<T?>, global::Demo.IFoo<System.Collections.Generic.List<T?>?>, global::Demo.IFoo<T>, global::Demo.IFoo<T?[]> where T : class
                {
                }

                public interface IDerived : global::Demo.IFoo<string?[]>
                {
                }

                public interface IFoo<T>
                {
                }

                public interface IOther : global::Demo.IFoo<object>, global::Demo.IFoo<string?>
                {
                }

                public interface ISample<T> : global::Demo.IFoo<T?[]> where T : class
                {
                }

                public class Inherited : global::Demo.Sample, global::Demo.IFoo<System.Collections.Generic.Dictionary<string, string?>?>
                {
                }

                public class NotAnnotated : global::Demo.Base<string>, global::Demo.IFoo<System.Collections.Generic.List<string>>, global::Demo.IFoo<string>
                {
                }

                public class NotAnnotatedContext : global::Demo.Base<string?>, global::Demo.IFoo<object?>, global::Demo.IFoo<string>
                {
                    public string A;
                    public string B;
                    public string C;
                }

                public class Oblivious : global::Demo.Base<string>, global::Demo.IFoo<string>
                {
                }

                public class Outer<T>
                {
                    public class Nested : global::Demo.Base<T?>, global::Demo.IFoo<global::Demo.Outer<string?>.Nested?>
                    {
                    }
                }

                public class Sample : global::Demo.Base<string?>, global::Demo.IFoo<int>, global::Demo.IFoo<object>, global::Demo.IFoo<string?>, System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, object?>>, System.Collections.IEnumerable, System.IEquatable<global::Demo.Sample?>
                {
                    public bool Equals(global::Demo.Sample? other) => throw null;
                    public System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<string, object?>> GetEnumerator() => throw null;
                    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw null;
                }

                public struct SampleStruct : global::Demo.IFoo<(string? Name, int Count)>, global::Demo.IFoo<global::Demo.SampleStruct?>, global::Demo.IFoo<string?[]?>
                {
                }

                public class ThroughInterface : global::Demo.IDerived, global::Demo.IFoo<string?[]>
                {
                }

                public class WithDynamic : global::Demo.Base<dynamic?>, global::Demo.IFoo<System.Collections.Generic.List<object?>>
                {
                }
            }
            """);
    }

    [Fact]
    public async Task Inheritance_RedeclaredInterfaces()
    {
        await Validate("""
            using System;

            namespace Demo;

            public interface IDerived : IDisposable { }

            public class Base : IDisposable, IComparable
            {
                public void Dispose() { }
                public int CompareTo(object? obj) => 0;
            }

            public class NotRedeclared : Base { }
            public class Redeclared : Base, IComparable { }

            public class Reimplemented : Base, IDisposable
            {
                void IDisposable.Dispose() { }
            }

            // IDisposable is implied by IDerived, although Base already implements it
            public class ThroughInterface : Base, IDerived { }

            public class GenericBase<T> : IEquatable<T>
            {
                public bool Equals(T? other) => false;
            }

            public class GenericNotRedeclared : GenericBase<string> { }
            public class GenericRedeclared : GenericBase<string>, IEquatable<string> { }
            public class GenericOtherArgument : GenericBase<string>, IEquatable<int>
            {
                public bool Equals(int other) => false;
            }

            public class GenericDerived<T> : GenericBase<T>, IEquatable<T> { }
            """, """
            #nullable enable

            namespace Demo
            {
                public class Base : System.IComparable, System.IDisposable
                {
                    public void Dispose() { }
                    public int CompareTo(object? obj) => throw null;
                }

                public class GenericBase<T> : System.IEquatable<T>
                {
                    public bool Equals(T? other) => throw null;
                }

                public class GenericDerived<T> : global::Demo.GenericBase<T>, System.IEquatable<T>
                {
                }

                public class GenericNotRedeclared : global::Demo.GenericBase<string>
                {
                }

                public class GenericOtherArgument : global::Demo.GenericBase<string>, System.IEquatable<int>
                {
                    public bool Equals(int other) => throw null;
                }

                public class GenericRedeclared : global::Demo.GenericBase<string>, System.IEquatable<string>
                {
                }

                public interface IDerived : System.IDisposable
                {
                }

                public class NotRedeclared : global::Demo.Base
                {
                }

                public class Redeclared : global::Demo.Base, System.IComparable
                {
                }

                public class Reimplemented : global::Demo.Base, System.IDisposable
                {
                    void System.IDisposable.Dispose() { }
                }

                public class ThroughInterface : global::Demo.Base, global::Demo.IDerived, System.IDisposable
                {
                }
            }
            """);
    }

    [Fact]
    public async Task LibraryImport_MethodsAreWrittenAsOrdinaryMethods()
    {
        await Validate("""
            using System;
            using System.Runtime.InteropServices;

            namespace Demo;

            public static partial class Native
            {
                [LibraryImport("kernel32", EntryPoint = "GetTickCount64")]
                public static partial ulong GetTickCount();

                [LibraryImport("user32", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                public static partial bool SetText(IntPtr handle, string text, [MarshalAs(UnmanagedType.Bool)] bool flag, out int result);

                [LibraryImport("lib")]
                internal static partial int NotVisible(int value);

                [DllImport("kernel32")]
                public static extern int DllImport(int value);

                public static int Managed(int value) => value;
            }

            // A type with a P/Invoke that is not visible is not partial
            public partial class WithPrivateImport
            {
                [LibraryImport("lib")]
                private static partial int NotVisible(int value);
            }

            public partial class Outer
            {
                public partial struct Nested
                {
                    [LibraryImport("lib")]
                    public static partial void M();

                    [LibraryImport("lib")]
                    public static unsafe partial void* Pointer(int* value);
                }
            }

            public partial interface IWithStaticMethod
            {
                [LibraryImport("lib")]
                public static partial int M(int value);
            }
            """, """
            #nullable enable

            namespace Demo
            {
                public interface IWithStaticMethod
                {
                    public static int M(int value) => throw null;
                }

                public static class Native
                {
                    public static ulong GetTickCount() => throw null;
                    public static bool SetText(nint handle, string text, bool flag, out int result) => throw null;
                    public static int DllImport(int value) => throw null;
                    public static int Managed(int value) => throw null;
                }

                public class Outer
                {
                    public struct Nested
                    {
                        public static void M() { }
                        public static unsafe void* Pointer(int* value) => throw null;
                    }
                }

                public class WithPrivateImport
                {
                }
            }
            """);
    }

    [Fact]
    public async Task Attributes_PseudoAttributesAreNotWritten()
    {
        await Validate("""
            using System;
            using System.Runtime.InteropServices;

            [StructLayout(LayoutKind.Explicit, Size = 8)]
            public struct ExplicitLayout
            {
                [FieldOffset(0)] public int A;
                [FieldOffset(4)] public int B;
            }

            [Serializable]
            public class Sample
            {
                [NonSerialized] public int Field;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 10)] public string? Marshalled;

                [return: MarshalAs(UnmanagedType.Bool)]
                public bool Method([MarshalAs(UnmanagedType.LPWStr)] string value, [In, Out] int[] array, [In] ref int a, [In, Out] ref int b, [Out] out int c) { c = 0; return true; }

                [DllImport("kernel32", SetLastError = true)]
                public static extern int Native(int value);

                [PreserveSig]
                public int PreserveSignature() => 0;
            }
            """, """
            #nullable enable

            public struct ExplicitLayout
            {
                public int A;
                public int B;
            }


            public class Sample
            {
                public int Field;
                public string? Marshalled;
                public bool Method(string value, int[] array, ref int a, ref int b, out int c) => throw null;
                public static int Native(int value) => throw null;
                public int PreserveSignature() => throw null;
            }
            """);
    }

    [Fact]
    public async Task NullableFlowAttributes_KeepTheDeclaredAnnotation()
    {
        await Validate("""
            using System.Collections.Generic;
            using System.Diagnostics.CodeAnalysis;

            public class Sample
            {
                [MaybeNull] public string MaybeNullField = "";
                [NotNull] public string? NotNullField = "";
                [AllowNull, MaybeNull] public string AllowNullMaybeNullField = "";
                [NotNull, DisallowNull] public string? NotNullDisallowNullField = "";

                [AllowNull] public string AllowNullProperty { get; set; } = "";
                [DisallowNull] public string? DisallowNullProperty { get; set; }
                [MaybeNull] public string MaybeNullProperty { get; set; } = "";
                [NotNull] public string? NotNullProperty { get; set; } = "";
                [AllowNull, MaybeNull] public string AllowNullMaybeNullProperty { get; set; } = "";
                [NotNull, DisallowNull] public string? NotNullDisallowNullProperty { get; set; } = "";
                [MaybeNull] public string GetOnlyProperty => null;
                [AllowNull] public string SetOnlyProperty { set { } }
                [AllowNull] public string InitProperty { get; init; } = "";
                [MaybeNull] public static string StaticProperty { get; set; } = "";
                public string AccessorAttributesProperty { [return: MaybeNull] get => null; [param: AllowNull] set { } }
                [System.Obsolete("message"), MaybeNull] public string WithAnotherAttributeProperty { get; set; } = "";
                [NotNullIfNotNull(nameof(index))] public string? this[string? index] => index;

                [return: NotNull] public string? NotNullReturn() => "";
                [return: MaybeNull] public string MaybeNullReturn() => null;
                [return: MaybeNull] public T MaybeNullTypeParameter<T>() => default;
                [return: NotNull] public List<string?>? NotNullGeneric() => new();
                [return: NotNullIfNotNull(nameof(value))] public string? NotNullIfNotNull(string? value) => value;

                public void Parameters([AllowNull] string a, [DisallowNull] string? b, [NotNull] ref string? c, [MaybeNull] out string d) { d = null; }
                public bool TryGet(string key, [MaybeNullWhen(false)] out string value) { value = null; return false; }
                public bool TryGet<T>(string key, [MaybeNullWhen(false)] out T value) { value = default; return false; }
                public bool TryGetNotNull(string key, [NotNullWhen(true)] out string? value) { value = null; return false; }
            }
            """, """
            #nullable enable

            public class Sample
            {
                [System.Diagnostics.CodeAnalysis.MaybeNull]
                public string MaybeNullField;
                [System.Diagnostics.CodeAnalysis.NotNull]
                public string? NotNullField;
                [System.Diagnostics.CodeAnalysis.AllowNull]
                [System.Diagnostics.CodeAnalysis.MaybeNull]
                public string AllowNullMaybeNullField;
                [System.Diagnostics.CodeAnalysis.NotNull]
                [System.Diagnostics.CodeAnalysis.DisallowNull]
                public string? NotNullDisallowNullField;
                [System.Diagnostics.CodeAnalysis.AllowNull]
                public string AllowNullProperty { get => throw null; set { } }
                [System.Diagnostics.CodeAnalysis.DisallowNull]
                public string? DisallowNullProperty { get => throw null; set { } }
                [System.Diagnostics.CodeAnalysis.MaybeNull]
                public string MaybeNullProperty { get => throw null; set { } }
                [System.Diagnostics.CodeAnalysis.NotNull]
                public string? NotNullProperty { get => throw null; set { } }
                [System.Diagnostics.CodeAnalysis.AllowNull]
                [System.Diagnostics.CodeAnalysis.MaybeNull]
                public string AllowNullMaybeNullProperty { get => throw null; set { } }
                [System.Diagnostics.CodeAnalysis.DisallowNull]
                [System.Diagnostics.CodeAnalysis.NotNull]
                public string? NotNullDisallowNullProperty { get => throw null; set { } }
                [System.Diagnostics.CodeAnalysis.MaybeNull]
                public string GetOnlyProperty { get => throw null; }
                [System.Diagnostics.CodeAnalysis.AllowNull]
                public string SetOnlyProperty { set { } }
                [System.Diagnostics.CodeAnalysis.AllowNull]
                public string InitProperty { get => throw null; init { } }
                [System.Diagnostics.CodeAnalysis.MaybeNull]
                public static string StaticProperty { get => throw null; set { } }
                [System.Diagnostics.CodeAnalysis.AllowNull]
                [System.Diagnostics.CodeAnalysis.MaybeNull]
                public string AccessorAttributesProperty { get => throw null; set { } }
                [System.Obsolete("message")]
                [System.Diagnostics.CodeAnalysis.MaybeNull]
                public string WithAnotherAttributeProperty { get => throw null; set { } }
                [System.Diagnostics.CodeAnalysis.NotNullIfNotNull("index")]
                public string? this[string? index] { get => throw null; }
                [return: System.Diagnostics.CodeAnalysis.NotNull]
                public string? NotNullReturn() => throw null;
                [return: System.Diagnostics.CodeAnalysis.MaybeNull]
                public string MaybeNullReturn() => throw null;
                [return: System.Diagnostics.CodeAnalysis.MaybeNull]
                public T MaybeNullTypeParameter<T>() => throw null;
                [return: System.Diagnostics.CodeAnalysis.NotNull]
                public System.Collections.Generic.List<string?>? NotNullGeneric() => throw null;
                [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull("value")]
                public string? NotNullIfNotNull(string? value) => throw null;
                public void Parameters([System.Diagnostics.CodeAnalysis.AllowNull] string a, [System.Diagnostics.CodeAnalysis.DisallowNull] string? b, [System.Diagnostics.CodeAnalysis.NotNull] ref string? c, [System.Diagnostics.CodeAnalysis.MaybeNull] out string d) => throw null;
                public bool TryGet(string key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out string value) => throw null;
                public bool TryGet<T>(string key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out T value) => throw null;
                public bool TryGetNotNull(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value) => throw null;
            }
            """);
    }

    [Fact]
    public async Task EnumConstants_EnumDeclaredInAnotherAssembly_UsesTheRuntimeDirectory()
    {
        // The metadata of the sample does not contain the enum, and nothing loads System.Net.Mail in the test process
        var files = await BuildFiles("""
            public class Sample
            {
                public void M(System.Net.Mail.SmtpDeliveryMethod value = System.Net.Mail.SmtpDeliveryMethod.PickupDirectoryFromIis) { }
            }
            """, new PublicApiOptions { FileLayout = PublicApiFileLayout.SingleFile, IncludeAutoGeneratedComment = false });

        Assert.Contains("System.Net.Mail.SmtpDeliveryMethod value = System.Net.Mail.SmtpDeliveryMethod.PickupDirectoryFromIis", Assert.Single(files).Content);
    }

    [Fact]
    public async Task EnumConstants_EnumDeclaredInAnotherAssembly_UsesTheAssemblyNextToTheInspectedOne()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var assemblyPath = await CompileSource(temporaryDirectory, "sample", "net10.0", """
            public class Sample
            {
                public void M(System.Net.Mail.SmtpDeliveryMethod value = System.Net.Mail.SmtpDeliveryMethod.Network) { }
            }
            """);

        // The members of the enum are read from the file named after the referenced assembly, not from the runtime
        var referencedAssemblyPath = await CompileSource(temporaryDirectory, "referenced", "net10.0", """
            namespace System.Net.Mail;

            public enum SmtpDeliveryMethod { FromTheAssemblyNextToTheSample }
            """);

        var directory = temporaryDirectory / "output";
        Directory.CreateDirectory(directory);
        File.Copy(assemblyPath, directory / "Sample.dll");
        File.Copy(referencedAssemblyPath, directory / "System.Net.Mail.dll");

        var files = PublicApi.Generate((string)(directory / "Sample.dll"), new PublicApiOptions { FileLayout = PublicApiFileLayout.SingleFile, IncludeAutoGeneratedComment = false });
        Assert.Contains("System.Net.Mail.SmtpDeliveryMethod value = System.Net.Mail.SmtpDeliveryMethod.FromTheAssemblyNextToTheSample", Assert.Single(files).Content);
    }

    [Fact]
    public async Task MethodParameter_OptionalAttribute()
    {
        await Validate("""
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            namespace Demo;

            public struct SampleStruct { }
            public enum SampleEnum { A, B }

            public class Sample
            {
                public void OptionalOnly([Optional] int a, [Optional] string? b, [Optional] SampleStruct c) { }
                public void BeforeDefaultValue(int required, [Optional] object? a, int b = 5) { }
                public void DefaultValueBeforeOptional([Optional, DefaultParameterValue(5)] int a, [Optional] int b, [Optional, DefaultParameterValue("x")] string c, [Optional, DefaultParameterValue(null)] string? d, [Optional, DefaultParameterValue(SampleEnum.B)] SampleEnum e, [Optional] string f, int g = 3, params int[] rest) { }
                public void DateTimeConstant([Optional, DateTimeConstant(636517440000000000L)] DateTime date) { }
                public void Decimal([Optional] decimal a, decimal b = 1.5m) { }
                public void DecimalBeforeOptional([Optional, DecimalConstant(1, 128, 0u, 0u, 15u)] decimal a, [Optional] int b) { }
                public int this[[Optional] int index] => 0;
                public int this[string key, int index = 5] => 0;
            }

            public delegate void SampleDelegate([Optional] int a);
            """, """
            #nullable enable

            namespace Demo
            {
                public class Sample
                {
                    public int this[[System.Runtime.InteropServices.Optional] int index] { get => throw null; }
                    public int this[string key, int index = 5] { get => throw null; }
                    public void OptionalOnly([System.Runtime.InteropServices.Optional] int a, [System.Runtime.InteropServices.Optional] string? b, [System.Runtime.InteropServices.Optional] global::Demo.SampleStruct c) { }
                    public void BeforeDefaultValue(int required, [System.Runtime.InteropServices.Optional] object? a, int b = 5) { }
                    public void DefaultValueBeforeOptional([System.Runtime.InteropServices.Optional] [System.Runtime.InteropServices.DefaultParameterValue(5)] int a, [System.Runtime.InteropServices.Optional] int b, [System.Runtime.InteropServices.Optional] [System.Runtime.InteropServices.DefaultParameterValue("x")] string c, [System.Runtime.InteropServices.Optional] [System.Runtime.InteropServices.DefaultParameterValue(null)] string? d, [System.Runtime.InteropServices.Optional] [System.Runtime.InteropServices.DefaultParameterValue(global::Demo.SampleEnum.B)] global::Demo.SampleEnum e, [System.Runtime.InteropServices.Optional] string f, int g = 3, params int[] rest) { }
                    public void DateTimeConstant([System.Runtime.InteropServices.Optional] [System.Runtime.CompilerServices.DateTimeConstant(636517440000000000L)] System.DateTime date) { }
                    public void Decimal([System.Runtime.InteropServices.Optional] decimal a, decimal b = 1.5m) { }
                    public void DecimalBeforeOptional([System.Runtime.InteropServices.Optional] [System.Runtime.CompilerServices.DecimalConstant(1, 128, 0U, 0U, 15U)] decimal a, [System.Runtime.InteropServices.Optional] int b) { }
                }

                public delegate void SampleDelegate([System.Runtime.InteropServices.Optional] int a);

                public enum SampleEnum
                {
                    A = 0,
                    B = 1,
                }

                public struct SampleStruct
                {
                }
            }
            """);
    }

    [Fact]
    public async Task TypeParameters_NullableAnnotations()
    {
        await Validate("""
            using System;
            using System.Collections.Generic;
            using System.Diagnostics.CodeAnalysis;

            namespace Demo;

            public class Sample<T, TClass, TStruct, TNotNull, TNullableClass>
                where TClass : class
                where TStruct : struct
                where TNotNull : notnull
                where TNullableClass : class?
            {
                public T NotAnnotatedField = default!;
                public T? Field;
                public TClass? ClassField;
                public TStruct? StructField;
                public TNotNull? NotNullField;
                public TNullableClass NullableClassField = default!;
                public TNullableClass? AnnotatedNullableClassField;

                public T? Property { get; set; }
                public List<T?> InTypeArgument { get; set; } = null!;
                public T?[] InArray { get; set; } = null!;
                public T?[]? InNullableArray { get; set; }
                public Dictionary<TClass?, T?>? InSeveralTypeArguments { get; set; }
                public (T? First, TClass? Second) InTuple { get; set; }
                public event Action<T?>? Changed;

                public T? Method(T? value) => value;
                public TClass? DefaultValue(TClass? value = null) => value;
                public ref T? ByReference(ref T? value) => ref value;
                public U? MethodTypeParameter<U>(U? value) => value;
                public U? ClassConstraint<U>(U? value) where U : class => value;
                public U? StructConstraint<U>(U? value) where U : struct => value;
                public U NotNullConstraint<U>(U value) where U : notnull => value;
                public U? InterfaceConstraint<U>(U? value) where U : IDisposable => value;
                public Func<T?, U?> InDelegate<U>() => null!;

                [return: MaybeNull] public T MaybeNull() => default;
                public void AllowNull([AllowNull] T value) { }
                public bool MaybeNullWhen([MaybeNullWhen(false)] out T value) { value = default; return false; }
                public bool NotNullWhen([NotNullWhen(true)] out T? value) { value = default; return false; }
            }

            public delegate T? SampleDelegate<T>(T? value);
            """, """
            #nullable enable

            namespace Demo
            {
                public delegate T? SampleDelegate<T>(T? value);

                public class Sample<T, TClass, TStruct, TNotNull, TNullableClass> where TClass : class where TStruct : struct where TNotNull : notnull where TNullableClass : class?
                {
                    public T NotAnnotatedField;
                    public T? Field;
                    public TClass? ClassField;
                    public TStruct? StructField;
                    public TNotNull? NotNullField;
                    public TNullableClass NullableClassField;
                    public TNullableClass? AnnotatedNullableClassField;
                    public T? Property { get => throw null; set { } }
                    public System.Collections.Generic.List<T?> InTypeArgument { get => throw null; set { } }
                    public T?[] InArray { get => throw null; set { } }
                    public T?[]? InNullableArray { get => throw null; set { } }
                    public System.Collections.Generic.Dictionary<TClass?, T?>? InSeveralTypeArguments { get => throw null; set { } }
                    public (T? First, TClass? Second) InTuple { get => throw null; set { } }
                    public event System.Action<T?>? Changed;
                    public T? Method(T? value) => throw null;
                    public TClass? DefaultValue(TClass? value = default) => throw null;
                    public ref T? ByReference(ref T? value) => throw null;
                    public U? MethodTypeParameter<U>(U? value) => throw null;
                    public U? ClassConstraint<U>(U? value) where U : class => throw null;
                    public U? StructConstraint<U>(U? value) where U : struct => throw null;
                    public U NotNullConstraint<U>(U value) where U : notnull => throw null;
                    public U? InterfaceConstraint<U>(U? value) where U : System.IDisposable => throw null;
                    public System.Func<T?, U?> InDelegate<U>() => throw null;
                    [return: System.Diagnostics.CodeAnalysis.MaybeNull]
                    public T MaybeNull() => throw null;
                    public void AllowNull([System.Diagnostics.CodeAnalysis.AllowNull] T value) { }
                    public bool MaybeNullWhen([System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out T value) => throw null;
                    public bool NotNullWhen([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out T? value) => throw null;
                }
            }
            """);
    }

    [Fact]
    public async Task Method_InheritedConstraints()
    {
        await Validate("""
            using System;
            using System.Collections.Generic;

            namespace Demo;

            public abstract class Base
            {
                public abstract T? Unconstrained<T>(T? value);
                public abstract T NotAnnotated<T>(T value);
                public abstract T? Class<T>(T? value) where T : class;
                public abstract T? Struct<T>(T? value) where T : struct;
                public abstract T Interface<T>(T value) where T : IDisposable;
                public abstract T? AnnotatedInterface<T>(T? value) where T : IDisposable;
                public abstract T Several<T>(T value) where T : class, IDisposable, new();
                public virtual void InTypeArgument<T>(List<T?> value) { }
            }

            public class Derived : Base
            {
                public override T? Unconstrained<T>(T? value) where T : default => value;
                public override T NotAnnotated<T>(T value) => value;
                public override T? Class<T>(T? value) where T : class => value;
                public override T? Struct<T>(T? value) => value;
                public override T Interface<T>(T value) => value;
                public override T? AnnotatedInterface<T>(T? value) where T : default => value;
                public override T Several<T>(T value) => value;
                public override void InTypeArgument<T>(List<T?> value) where T : default { }
            }

            public interface ISample
            {
                T? Unconstrained<T>(T? value);
                T NotAnnotated<T>(T value);
                T? Class<T>(T? value) where T : class;
                T? Struct<T>(T? value) where T : struct;
                T? AnnotatedInterface<T>(T? value) where T : IDisposable;
            }

            public class Explicit : ISample
            {
                T? ISample.Unconstrained<T>(T? value) where T : default => value;
                T ISample.NotAnnotated<T>(T value) => value;
                T? ISample.Class<T>(T? value) where T : class => value;
                T? ISample.Struct<T>(T? value) => value;
                T? ISample.AnnotatedInterface<T>(T? value) where T : default => value;
            }
            """, """
            #nullable enable

            namespace Demo
            {
                public abstract class Base
                {
                    public abstract T? Unconstrained<T>(T? value);
                    public abstract T NotAnnotated<T>(T value);
                    public abstract T? Class<T>(T? value) where T : class;
                    public abstract T? Struct<T>(T? value) where T : struct;
                    public abstract T Interface<T>(T value) where T : System.IDisposable;
                    public abstract T? AnnotatedInterface<T>(T? value) where T : System.IDisposable;
                    public abstract T Several<T>(T value) where T : class, System.IDisposable, new();
                    public virtual void InTypeArgument<T>(System.Collections.Generic.List<T?> value) { }
                }

                public class Derived : global::Demo.Base
                {
                    public override T? Unconstrained<T>(T? value) where T : default => throw null;
                    public override T NotAnnotated<T>(T value) => throw null;
                    public override T? Class<T>(T? value) where T : class => throw null;
                    public override T? Struct<T>(T? value) where T : struct => throw null;
                    public override T Interface<T>(T value) => throw null;
                    public override T? AnnotatedInterface<T>(T? value) where T : default => throw null;
                    public override T Several<T>(T value) where T : class => throw null;
                    public override void InTypeArgument<T>(System.Collections.Generic.List<T?> value) where T : default { }
                }

                public class Explicit : global::Demo.ISample
                {
                    T? global::Demo.ISample.Unconstrained<T>(T? value) where T : default => throw null;
                    T global::Demo.ISample.NotAnnotated<T>(T value) => throw null;
                    T? global::Demo.ISample.Class<T>(T? value) where T : class => throw null;
                    T? global::Demo.ISample.Struct<T>(T? value) where T : struct => throw null;
                    T? global::Demo.ISample.AnnotatedInterface<T>(T? value) where T : default => throw null;
                }

                public interface ISample
                {
                    T? Unconstrained<T>(T? value);
                    T NotAnnotated<T>(T value);
                    T? Class<T>(T? value) where T : class;
                    T? Struct<T>(T? value) where T : struct;
                    T? AnnotatedInterface<T>(T? value) where T : System.IDisposable;
                }
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
    public async Task RequiresPreviewFeaturesAttribute_OnPublicDefinitions()
    {
        await Validate("""
            [assembly: System.Runtime.Versioning.RequiresPreviewFeaturesAttribute("Preview assembly", Url = "https://example.com/assembly")]

            [System.Runtime.Versioning.RequiresPreviewFeaturesAttribute("Preview delegate")]
            public delegate void SampleDelegate();

            [System.Runtime.Versioning.RequiresPreviewFeaturesAttribute("Preview class")]
            public class Sample
            {
                [System.Runtime.Versioning.RequiresPreviewFeaturesAttribute("Preview constructor")]
                public Sample()
                {
                }

                [System.Runtime.Versioning.RequiresPreviewFeaturesAttribute("Preview field")]
                public int Field;

                [System.Runtime.Versioning.RequiresPreviewFeaturesAttribute("Preview property")]
                public int Property { get; set; }

                [System.Runtime.Versioning.RequiresPreviewFeaturesAttribute("Preview event")]
                public event SampleDelegate Event;

                [System.Runtime.Versioning.RequiresPreviewFeaturesAttribute("Preview method")]
                public void Method()
                {
                }
            }
            """, """
            [assembly: System.Runtime.Versioning.RequiresPreviewFeatures("Preview assembly", Url = "https://example.com/assembly")]
            #nullable enable

            [System.Runtime.Versioning.RequiresPreviewFeatures("Preview class")]
            public class Sample
            {
                [System.Runtime.Versioning.RequiresPreviewFeatures("Preview field")]
                public int Field;
                [System.Runtime.Versioning.RequiresPreviewFeatures("Preview property")]
                public int Property { get => throw null; set { } }
                [System.Runtime.Versioning.RequiresPreviewFeatures("Preview event")]
                public event SampleDelegate Event;
                [System.Runtime.Versioning.RequiresPreviewFeatures("Preview constructor")]
                public Sample() { }
                [System.Runtime.Versioning.RequiresPreviewFeatures("Preview method")]
                public void Method() { }
            }


            [System.Runtime.Versioning.RequiresPreviewFeatures("Preview delegate")]
            public delegate void SampleDelegate();
            """);
    }

    [Fact]
    public async Task AssemblyRequiresPreviewFeaturesAttribute_OneFilePerType()
    {
        var files = await BuildFiles("""
            [assembly: System.Runtime.Versioning.RequiresPreviewFeaturesAttribute]

            public class A
            {
            }

            public class B
            {
            }
            """, new PublicApiOptions
        {
            FileLayout = PublicApiFileLayout.OneFilePerType,
            IncludeAutoGeneratedComment = false,
        });

        Assert.Equal(["AssemblyInfo.g.cs", "A.g.cs", "B.g.cs"], files.Select(file => file.RelativePath));
        var assemblyInfo = files[0].Content;
        Assert.Contains("[assembly: System.Runtime.Versioning.RequiresPreviewFeatures]", assemblyInfo);
        Assert.DoesNotContain("[assembly:", files[1].Content);
        Assert.DoesNotContain("[assembly:", files[2].Content);
    }

    [Fact]
    public async Task Property_WithJsonPropertyNameAttribute()
    {
        await Validate("""
            public class Sample
            {
                [System.Text.Json.Serialization.JsonPropertyNameAttribute("my_name")]
                public string Property { get; set; }
            }
            """, """
            #nullable enable

            public class Sample
            {
                [System.Text.Json.Serialization.JsonPropertyName("my_name")]
                public string Property { get => throw null; set { } }
            }
            """);
    }

    [Fact]
    public async Task Property_WithJsonConverterAttribute()
    {
        await Validate("""
            public class SampleJsonConverter : System.Text.Json.Serialization.JsonConverter<string>
            {
                public override string Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options) => throw null;
                public override void Write(System.Text.Json.Utf8JsonWriter writer, string value, System.Text.Json.JsonSerializerOptions options) { }
            }

            public class Sample
            {
                [System.Text.Json.Serialization.JsonConverterAttribute(typeof(SampleJsonConverter))]
                public string Property { get; set; }
            }
            """, """
            #nullable enable

            public class Sample
            {
                [System.Text.Json.Serialization.JsonConverter(typeof(SampleJsonConverter))]
                public string Property { get => throw null; set { } }
            }


            public class SampleJsonConverter : System.Text.Json.Serialization.JsonConverter<string>
            {
                public override string Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options) => throw null;
                public override void Write(System.Text.Json.Utf8JsonWriter writer, string value, System.Text.Json.JsonSerializerOptions options) { }
            }
            """);
    }

    [Fact]
    public async Task Attribute_TypeofArguments()
    {
        await Validate("""
            using System.Collections.Generic;
            using System.Diagnostics;

            namespace Demo;

            [DebuggerTypeProxy(typeof(int))] public class Keyword { }
            [DebuggerTypeProxy(typeof(string[]))] public class Array { }
            [DebuggerTypeProxy(typeof(int?))] public class NullableValue { }
            [DebuggerTypeProxy(typeof(object))] public class Object { }
            [DebuggerTypeProxy(typeof(nint))] public class NativeInteger { }
            [DebuggerTypeProxy(typeof(decimal))] public class Decimal { }
            [DebuggerTypeProxy(typeof(void))] public class Void { }
            [DebuggerTypeProxy(typeof(List<int>))] public class Constructed { }
            [DebuggerTypeProxy(typeof(Dictionary<string, object[]>))] public class ConstructedWithArray { }
            [DebuggerTypeProxy(typeof((int, string)))] public class Tuple { }
            [DebuggerTypeProxy(typeof(List<>))] public class Unbound { }
            [DebuggerTypeProxy(typeof(Dictionary<,>))] public class UnboundWithTwoParameters { }
            [DebuggerTypeProxy(typeof(Dictionary<,>.Enumerator))] public class UnboundNested { }
            [DebuggerTypeProxy(typeof(Generic<>.Nested<,>))] public class UnboundNestedGeneric { }
            [DebuggerTypeProxy(typeof(Generic<int>.Nested<string, long>))] public class ConstructedNestedGeneric { }

            public class Generic<T>
            {
                public class Nested<U, V> { }
            }
            """, """
            #nullable enable

            namespace Demo
            {
                [System.Diagnostics.DebuggerTypeProxy(typeof(string[]))]
                public class Array
                {
                }

                [System.Diagnostics.DebuggerTypeProxy(typeof(System.Collections.Generic.List<int>))]
                public class Constructed
                {
                }

                [System.Diagnostics.DebuggerTypeProxy(typeof(global::Demo.Generic<int>.Nested<string, long>))]
                public class ConstructedNestedGeneric
                {
                }

                [System.Diagnostics.DebuggerTypeProxy(typeof(System.Collections.Generic.Dictionary<string, object[]>))]
                public class ConstructedWithArray
                {
                }

                [System.Diagnostics.DebuggerTypeProxy(typeof(decimal))]
                public class Decimal
                {
                }

                public class Generic<T>
                {
                    public class Nested<U, V>
                    {
                    }
                }

                [System.Diagnostics.DebuggerTypeProxy(typeof(int))]
                public class Keyword
                {
                }

                [System.Diagnostics.DebuggerTypeProxy(typeof(nint))]
                public class NativeInteger
                {
                }

                [System.Diagnostics.DebuggerTypeProxy(typeof(int?))]
                public class NullableValue
                {
                }

                [System.Diagnostics.DebuggerTypeProxy(typeof(object))]
                public class Object
                {
                }

                [System.Diagnostics.DebuggerTypeProxy(typeof(System.ValueTuple<int, string>))]
                public class Tuple
                {
                }

                [System.Diagnostics.DebuggerTypeProxy(typeof(System.Collections.Generic.List<>))]
                public class Unbound
                {
                }

                [System.Diagnostics.DebuggerTypeProxy(typeof(System.Collections.Generic.Dictionary<,>.Enumerator))]
                public class UnboundNested
                {
                }

                [System.Diagnostics.DebuggerTypeProxy(typeof(global::Demo.Generic<>.Nested<,>))]
                public class UnboundNestedGeneric
                {
                }

                [System.Diagnostics.DebuggerTypeProxy(typeof(System.Collections.Generic.Dictionary<,>))]
                public class UnboundWithTwoParameters
                {
                }

                [System.Diagnostics.DebuggerTypeProxy(typeof(void))]
                public class Void
                {
                }
            }
            """);
    }

    [Fact]
    public async Task AttributeUsage_UsesEnumMemberNames()
    {
        var files = await BuildFiles(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["net8.0"] = """
                    [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
                    public sealed class SampleAttribute : System.Attribute
                    {
                    }
                    """,
            },
            new PublicApiOptions
            {
                FileLayout = PublicApiFileLayout.SingleFile,
                IncludeAutoGeneratedComment = false,
            });

        var content = Assert.Single(files).Content;
        Assert.Contains("[System.AttributeUsage(System.AttributeTargets.", content);
        Assert.Contains("System.AttributeTargets.Property", content);
        Assert.Contains("System.AttributeTargets.Field", content);
        Assert.DoesNotContain("[System.AttributeUsage((System.AttributeTargets)", content);
    }

    [Fact]
    public async Task Attribute_FlagsEnumArgument_UsesAscendingValueOrder()
    {
        await Validate("""
            namespace System.Diagnostics
            {
                [System.Flags]
                public enum SampleFlags
                {
                    None = 0,
                    First = 1,
                    Second = 2,
                    Third = 4,
                    All = 7,
                }

                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
                public sealed class SampleAttribute : System.Attribute
                {
                    public SampleAttribute(SampleFlags flags) { }
                }
            }

            [System.Diagnostics.Sample(System.Diagnostics.SampleFlags.First | System.Diagnostics.SampleFlags.Second)]
            public class Sample
            {
            }
            """, """
            #nullable enable

            [System.Diagnostics.Sample(System.Diagnostics.SampleFlags.First | System.Diagnostics.SampleFlags.Second)]
            public class Sample
            {
            }

            namespace System.Diagnostics
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
                public sealed class SampleAttribute : System.Attribute
                {
                    public SampleAttribute(System.Diagnostics.SampleFlags flags) { }
                }

                [System.Flags]
                public enum SampleFlags
                {
                    None = 0,
                    First = 1,
                    Second = 2,
                    Third = 4,
                    All = 7,
                }
            }
            """);
    }

    [Fact]
    public async Task Attribute_FlagsEnumArgument_WithSameValueMembers_UsesFirstMemberInOrdinalOrder()
    {
        await Validate("""
            namespace System.Diagnostics
            {
                [System.Flags]
                public enum SampleFlags
                {
                    None = 0,
                    Zebra = 1,
                    Alpha = 1,
                    Second = 2,
                }

                [System.AttributeUsage(System.AttributeTargets.All)]
                public sealed class SampleAttribute : System.Attribute
                {
                    public SampleAttribute(SampleFlags flags) { }
                }
            }

            [System.Diagnostics.Sample(System.Diagnostics.SampleFlags.Zebra | System.Diagnostics.SampleFlags.Second)]
            public class SampleCombined
            {
            }

            [System.Diagnostics.Sample(System.Diagnostics.SampleFlags.Zebra)]
            public class SampleSingle
            {
            }
            """, """
            #nullable enable

            [System.Diagnostics.Sample(System.Diagnostics.SampleFlags.Alpha | System.Diagnostics.SampleFlags.Second)]
            public class SampleCombined
            {
            }


            [System.Diagnostics.Sample(System.Diagnostics.SampleFlags.Alpha)]
            public class SampleSingle
            {
            }

            namespace System.Diagnostics
            {
                [System.AttributeUsage(System.AttributeTargets.All)]
                public sealed class SampleAttribute : System.Attribute
                {
                    public SampleAttribute(System.Diagnostics.SampleFlags flags) { }
                }

                [System.Flags]
                public enum SampleFlags
                {
                    None = 0,
                    Zebra = 1,
                    Alpha = 1,
                    Second = 2,
                }
            }
            """);
    }

    [Fact]
    public async Task AttributeTypeArgument_UsesCSharpGenericTypeSyntax()
    {
        var files = await BuildFiles(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["net8.0"] = """
                    public class SampleType
                    {
                    }

                    public class Sample
                    {
                        [System.Text.Json.Serialization.JsonConverterAttribute(typeof(System.Collections.Generic.IEnumerable<SampleType>))]
                        public string Property { get; set; }
                    }
                    """,
            },
            new PublicApiOptions
            {
                FileLayout = PublicApiFileLayout.SingleFile,
                IncludeAutoGeneratedComment = false,
            });

        var content = Assert.Single(files).Content;
        Assert.Contains("typeof(System.Collections.Generic.IEnumerable<SampleType>)", content);
        Assert.DoesNotContain("`1[", content);
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
                B = 1,
            }
            """);
    }

    [Fact]
    public async Task Enum_UnderlyingType()
    {
        await Validate("""
            public enum ByteEnum : byte { A, B }
            public enum LongEnum : long { A, B = 5000000000 }
            public enum IntEnum : int { A }

            public class Outer
            {
                public enum InnerEnum : ushort { A }
            }
            """, """
            #nullable enable

            public enum ByteEnum : byte
            {
                A = 0,
                B = 1,
            }


            public enum IntEnum
            {
                A = 0,
            }


            public enum LongEnum : long
            {
                A = 0L,
                B = 5000000000L,
            }


            public class Outer
            {
                public enum InnerEnum : ushort
                {
                    A = 0,
                }
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
                B = 3,
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
                B = 2,
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
    public async Task GenericClass_WithNonGenericTypeOfSameName()
    {
        await Validate("""
            public class Sample
            {
                public int Value => 0;
            }

            public class Sample<T0>
            {
                public T0 Value => default;
            }
            """, """
            #nullable enable

            public class Sample
            {
                public int Value { get => throw null; }
            }


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
    public async Task GenericMembers_WithCustomGenericParameterNames()
    {
        await Validate("""
            public class Sample<T>
            {
                public T[] RevealToArray() => null;
                public void RevealAndUse<TArg>(TArg arg, System.Buffers.ReadOnlySpanAction<T, TArg> spanAction) { }
            }
            """, """
            #nullable enable

            public class Sample<T>
            {
                public T[] RevealToArray() => throw null;
                public void RevealAndUse<TArg>(TArg arg, System.Buffers.ReadOnlySpanAction<T, TArg> spanAction) { }
            }
            """);
    }

    [Fact]
    public async Task GenericConstraints_NotNullAndNullableClass()
    {
        await Validate("""
            using System;

            namespace Demo;

            public class Sample<TUnconstrained, TNotNull, TClass, TNullableClass, TStruct, TInterface, TNotNullInterface>
                where TNotNull : notnull
                where TClass : class
                where TNullableClass : class?
                where TStruct : struct
                where TInterface : IDisposable
                where TNotNullInterface : notnull, IDisposable, new()
            {
                // Most of the members are not nullable, so the compiler stores a "not annotated" context on the type
                public string A = "";
                public string B = "";
                public string C = "";
                public TUnconstrained Value = default!;

                public void Method<UUnconstrained, UNotNull, UClass, UNullableClass, UInterface>(UUnconstrained a, UNotNull b, UClass c, UNullableClass d, UInterface e)
                    where UNotNull : notnull
                    where UClass : class
                    where UNullableClass : class?, IDisposable
                    where UInterface : IDisposable
                {
                }

                public class Nested<V, VNotNull> where VNotNull : notnull { }
            }

            public delegate void SampleDelegate<T, TNotNull, TNullableClass>(T a, TNotNull b, TNullableClass c) where TNotNull : notnull where TNullableClass : class?;

            public interface ISample<TNotNull> where TNotNull : notnull
            {
                void Method<UNotNull>(UNotNull value) where UNotNull : notnull;
            }

            public abstract class Base
            {
                public abstract void Method<T>(T value) where T : notnull;
                public abstract void NullableClass<T>(T value) where T : class?;
            }

            public class Derived : Base, ISample<string>
            {
                public override void Method<T>(T value) { }
                public override void NullableClass<T>(T value) { }
                void ISample<string>.Method<UNotNull>(UNotNull value) { }
            }

            #nullable disable
            public class Oblivious<T, TClass> where TClass : class
            {
                public void Method<U>(U value) { }
            }
            """, """
            #nullable enable

            namespace Demo
            {
                public abstract class Base
                {
                    public abstract void Method<T>(T value) where T : notnull;
                    public abstract void NullableClass<T>(T value) where T : class?;
                }

                public class Derived : global::Demo.Base, global::Demo.ISample<string>
                {
                    public override void Method<T>(T value) { }
                    public override void NullableClass<T>(T value) where T : class { }
                    void global::Demo.ISample<System.String>.Method<UNotNull>(UNotNull value) { }
                }

                public interface ISample<TNotNull> where TNotNull : notnull
                {
                    void Method<UNotNull>(UNotNull value) where UNotNull : notnull;
                }

                public class Oblivious<T, TClass> where TClass : class
                {
                    public void Method<U>(U value) { }
                }

                public delegate void SampleDelegate<T, TNotNull, TNullableClass>(T a, TNotNull b, TNullableClass c) where TNotNull : notnull where TNullableClass : class?;

                public class Sample<TUnconstrained, TNotNull, TClass, TNullableClass, TStruct, TInterface, TNotNullInterface> where TNotNull : notnull where TClass : class where TNullableClass : class? where TStruct : struct where TInterface : System.IDisposable where TNotNullInterface : notnull, System.IDisposable, new()
                {
                    public string A;
                    public string B;
                    public string C;
                    public TUnconstrained Value;
                    public void Method<UUnconstrained, UNotNull, UClass, UNullableClass, UInterface>(UUnconstrained a, UNotNull b, UClass c, UNullableClass d, UInterface e) where UNotNull : notnull where UClass : class where UNullableClass : class?, System.IDisposable where UInterface : System.IDisposable { }
                    public class Nested<V, VNotNull> where VNotNull : notnull
                    {
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task GenericConstraints_AnnotatedConstraintTypes()
    {
        await Validate("""
            using System;
            using System.Collections.Generic;

            namespace Demo;

            public interface IFoo { }
            public class BaseClass { }

            public class Sample<TNullableInterface, TInterface, TNullableBase, TGeneric, TArray, TTuple, TMixed, TOther, TTypeParameter>
                where TNullableInterface : IFoo?
                where TInterface : IFoo
                where TNullableBase : BaseClass?
                where TGeneric : IEquatable<TGeneric?>, IComparable<string?>, IEnumerable<string>
                where TArray : IEquatable<string?[]?>
                where TTuple : IEquatable<(int A, string? B)>
                where TMixed : class?, IFoo?, IDisposable, new()
                where TOther : class
                where TTypeParameter : TOther?
            {
                public void Method<U, V>(U a, V b)
                    where U : IFoo?, IDisposable
                    where V : notnull, IEquatable<V?>, IComparable<U?>
                {
                }
            }

            public delegate void SampleDelegate<T>(T value) where T : IFoo?, IEquatable<T?>;

            #nullable disable
            public class Oblivious<T> where T : IFoo, IEquatable<string>
            {
            }
            """, """
            #nullable enable

            namespace Demo
            {
                public class BaseClass
                {
                }

                public interface IFoo
                {
                }

                public class Oblivious<T> where T : global::Demo.IFoo, System.IEquatable<string>
                {
                }

                public delegate void SampleDelegate<T>(T value) where T : global::Demo.IFoo?, System.IEquatable<T?>;

                public class Sample<TNullableInterface, TInterface, TNullableBase, TGeneric, TArray, TTuple, TMixed, TOther, TTypeParameter> where TNullableInterface : global::Demo.IFoo? where TInterface : global::Demo.IFoo where TNullableBase : global::Demo.BaseClass? where TGeneric : System.IEquatable<TGeneric?>, System.IComparable<string?>, System.Collections.Generic.IEnumerable<string> where TArray : System.IEquatable<string?[]?> where TTuple : System.IEquatable<(int A, string? B)> where TMixed : class?, global::Demo.IFoo?, System.IDisposable, new() where TOther : class where TTypeParameter : TOther?
                {
                    public void Method<U, V>(U a, V b) where U : global::Demo.IFoo?, System.IDisposable where V : notnull, System.IEquatable<V?>, System.IComparable<U?> { }
                }
            }
            """);
    }

    [Fact]
    public async Task GenericConstraints_Unmanaged()
    {
        await Validate("""
            using System;

            namespace Demo;

            public unsafe class Sample<T, TStruct> where T : unmanaged where TStruct : struct
            {
                public T* Pointer;

                public static U* Method<U>(U* value) where U : unmanaged, IComparable<U> => value;
            }

            public delegate void SampleDelegate<T>(T value) where T : unmanaged;

            public abstract class Base
            {
                public abstract T? Method<T>(T? value) where T : unmanaged;
            }

            public class Derived : Base
            {
                public override T? Method<T>(T? value) => value;
            }
            """, """
            #nullable enable

            namespace Demo
            {
                public abstract class Base
                {
                    public abstract T? Method<T>(T? value) where T : unmanaged;
                }

                public class Derived : global::Demo.Base
                {
                    public override T? Method<T>(T? value) where T : struct => throw null;
                }

                public delegate void SampleDelegate<T>(T value) where T : unmanaged;

                public class Sample<T, TStruct> where T : unmanaged where TStruct : struct
                {
                    public T* Pointer;
                    public static unsafe U* Method<U>(U* value) where U : unmanaged, System.IComparable<U> => throw null;
                }
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
                public void M<TAllowNull, TNew, TStruct, TClass, TEnum, TBase>() where TAllowNull : class? where TNew : new() where TStruct : struct where TClass : class where TEnum : System.Enum where TBase : SampleBaseClass { }
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

    [Fact]
    public async Task Class_Closed()
    {
        await Validate("""
            namespace System.Runtime.CompilerServices
            {
                [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
                public sealed class ClosedAttribute : System.Attribute
                {
                }

                public sealed class IsClosedTypeAttribute : System.Attribute
                {
                }
            }

            public closed class Sample
            {
            }
            """, """
            #nullable enable

            public closed class Sample
            {
            }

            namespace System.Runtime.CompilerServices
            {
                [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
                public sealed class ClosedAttribute : System.Attribute
                {
                }

                public sealed class IsClosedTypeAttribute : System.Attribute
                {
                }
            }
            """, compilerOptions: new CompilerOptions
            {
                TargetFramework = "net10.0",
            });
    }

    [Fact]
    public async Task Struct_Union()
    {
        await Validate("""
            namespace System.Runtime.CompilerServices
            {
                public sealed class UnionAttribute : System.Attribute
                {
                }

                public interface IUnion
                {
                    object Value { get; }
                }
            }

            public class Cat
            {
            }

            public class Dog
            {
            }

            public union Pet(Cat, Dog);
            """, """
            #nullable enable

            public class Cat
            {
            }


            public class Dog
            {
            }


            public union Pet(Cat, Dog)
            {
            }

            namespace System.Runtime.CompilerServices
            {
                public interface IUnion
                {
                    object Value { get; }
                }

                public sealed class UnionAttribute : System.Attribute
                {
                }
            }
            """, compilerOptions: new CompilerOptions
            {
                TargetFramework = "net10.0",
            });
    }

    private const string ModelSource = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.ComponentModel;
        using System.Threading;

        namespace Demo;

        public class Outer<T> where T : class?
        {
            public Inner2<string> Create() => null!;

            public class Inner
            {
            }

            public class Inner2<U> where U : notnull
            {
                public U M<V>(T t, V v) where V : unmanaged => default!;
            }
        }

        public interface IVariant<in TIn, out TOut>
        {
            TOut Invoke(TIn value);
            static abstract int Create();
        }

        public readonly struct ReadOnlyPoint
        {
            public readonly int Y;
            public int X { get; init; }
        }

        public ref struct RefStruct
        {
            public ref int Value;
            public ref readonly int ReadOnlyValue;
        }

        [Flags]
        public enum Options : byte
        {
            None = 0,
            A = 1,
            B = 2,
        }

        public delegate TResult Transformer<in T, out TResult>(T value, params object?[] arguments) where T : notnull;

        public sealed record Person(string Name, int Age);

        public abstract class Base : IDisposable, IEnumerable<string>
        {
            public const string Constant = "text";
            public static readonly int StaticReadOnly;
            public volatile int Volatile;

            protected Base() { }

            public abstract int Value { get; protected set; }
            public required string Required { get; init; }
            public int this[int index, string? key = null] => 0;
            public virtual event EventHandler? Changed;

            [EditorBrowsable(EditorBrowsableState.Never)]
            [Obsolete("Use Value")]
            public int Hidden() => 0;
            public Dictionary<int, string?> Dictionary() => null!;
            public KeyValuePair<int?, string?>? NullablePair() => null;
            public (int Count, string? Name) Tuple() => default;
            public string?[]? Array(int[,] matrix, int[][] jagged) => null;
            public void RefKinds(ref int a, out int b, in int c, ref readonly int d) { b = 0; }
            public void Defaults(int value = 42, string? text = null, Options options = Options.A, CancellationToken cancellationToken = default) { }
            public T Generic<T>(T value) where T : struct, IComparable<T> => value;
        #nullable disable
            public string Oblivious(string value) => value;
        #nullable enable
            public static Base operator +(Base left, Base right) => left;
            public static implicit operator int(Base value) => 0;
            public static explicit operator Base(long value) => null!;
            public static explicit operator checked Base(long value) => null!;
            void IDisposable.Dispose() { }
            IEnumerator<string> IEnumerable<string>.GetEnumerator() => null!;
            IEnumerator IEnumerable.GetEnumerator() => null!;
            ~Base() { }
        }

        public static class Extensions
        {
            public static int Count(this IEnumerable<int> source) => 0;
        }
        """;

    [Fact]
    public async Task Model_ReadAssembly()
    {
        var assembly = await ReadAssembly(ModelSource);
        InlineSnapshot.Validate(DumpModel(assembly), """
            T:Demo.Base
                public abstract class Base : System.Collections.Generic.IEnumerable<string>, System.Collections.IEnumerable, System.IDisposable
            F:Demo.Base.Constant
                public const string Constant = "text"
            F:Demo.Base.StaticReadOnly
                public static readonly int StaticReadOnly
            F:Demo.Base.Volatile
                public volatile int Volatile
            P:Demo.Base.Value
                public abstract int Value { get; protected set; }
            P:Demo.Base.Required
                public required string Required { get; init; }
            P:Demo.Base.Item(System.Int32,System.String)
                public int this[int index, string? key = null] { get; }
            E:Demo.Base.Changed
                public virtual event System.EventHandler? Changed
            M:Demo.Base.#ctor
                protected Base()
            M:Demo.Base.Hidden
                [System.Obsolete("Use Value")]
                public int Hidden()
            M:Demo.Base.Dictionary
                public System.Collections.Generic.Dictionary<int, string?> Dictionary()
            M:Demo.Base.NullablePair
                public System.Collections.Generic.KeyValuePair<int?, string?>? NullablePair()
            M:Demo.Base.Tuple
                public (int Count, string? Name) Tuple()
            M:Demo.Base.Array(System.Int32[0:,0:],System.Int32[][])
                public string?[]? Array(int[,] matrix, int[][] jagged)
            M:Demo.Base.RefKinds(System.Int32@,System.Int32@,System.Int32@,System.Int32@)
                public void RefKinds(ref int a, out int b, in int c, ref readonly int d)
            M:Demo.Base.Defaults(System.Int32,System.String,Demo.Options,System.Threading.CancellationToken)
                public void Defaults(int value = 42, string? text = null, Demo.Options options = Demo.Options.A, System.Threading.CancellationToken cancellationToken = default)
            M:Demo.Base.Generic``1(``0)
                public T Generic<T>(T value) where T : struct, System.IComparable<T>
            M:Demo.Base.Oblivious(System.String)
                public string Oblivious(string value)
            M:Demo.Base.op_Addition(Demo.Base,Demo.Base)
                public static Demo.Base operator +(Demo.Base left, Demo.Base right)
            M:Demo.Base.op_Implicit(Demo.Base)~System.Int32
                public static implicit operator int(Demo.Base value)
            M:Demo.Base.op_Explicit(System.Int64)~Demo.Base
                public static explicit operator Demo.Base(long value)
            M:Demo.Base.op_CheckedExplicit(System.Int64)~Demo.Base
                public static explicit operator checked Demo.Base(long value)
            M:Demo.Base.System#IDisposable#Dispose
                void System.IDisposable.Dispose()
            M:Demo.Base.System#Collections#Generic#IEnumerable{System#String}#GetEnumerator
                System.Collections.Generic.IEnumerator<string> System.Collections.Generic.IEnumerable<string>.GetEnumerator()
            M:Demo.Base.System#Collections#IEnumerable#GetEnumerator
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            M:Demo.Base.Finalize
                ~Base()
            T:Demo.Extensions
                public static class Extensions
            M:Demo.Extensions.Count(System.Collections.Generic.IEnumerable{System.Int32})
                public static int Count(this System.Collections.Generic.IEnumerable<int> source)
            T:Demo.IVariant`2
                public interface IVariant<in TIn, out TOut>
            M:Demo.IVariant`2.Invoke(`0)
                TOut Invoke(TIn value)
            M:Demo.IVariant`2.Create
                public static abstract int Create()
            T:Demo.Options
                [System.Flags]
                public enum Options : byte
            F:Demo.Options.None
                None = 0
            F:Demo.Options.A
                A = 1
            F:Demo.Options.B
                B = 2
            T:Demo.Outer`1
                public class Outer<T> where T : class?
            M:Demo.Outer`1.Create
                public Demo.Outer<T>.Inner2<string> Create()
            M:Demo.Outer`1.#ctor
                public Outer()
            T:Demo.Outer`1.Inner
                public class Inner
            M:Demo.Outer`1.Inner.#ctor
                public Inner()
            T:Demo.Outer`1.Inner2`1
                public class Inner2<U> where U : notnull
            M:Demo.Outer`1.Inner2`1.M``1(`0,``0)
                public U M<V>(T t, V v) where V : unmanaged
            M:Demo.Outer`1.Inner2`1.#ctor
                public Inner2()
            T:Demo.Person
                public sealed class Person : System.IEquatable<Demo.Person>
            P:Demo.Person.Name
                public string Name { get; init; }
            P:Demo.Person.Age
                public int Age { get; init; }
            M:Demo.Person.#ctor(System.String,System.Int32)
                public Person(string Name, int Age)
            M:Demo.Person.ToString (compiler-generated)
                public override string ToString()
            M:Demo.Person.op_Inequality(Demo.Person,Demo.Person) (compiler-generated)
                public static bool operator !=(Demo.Person? left, Demo.Person? right)
            M:Demo.Person.op_Equality(Demo.Person,Demo.Person) (compiler-generated)
                public static bool operator ==(Demo.Person? left, Demo.Person? right)
            M:Demo.Person.GetHashCode (compiler-generated)
                public override int GetHashCode()
            M:Demo.Person.Equals(System.Object) (compiler-generated)
                public override bool Equals(object? obj)
            M:Demo.Person.Equals(Demo.Person) (compiler-generated)
                public bool Equals(Demo.Person? other)
            M:Demo.Person.Deconstruct(System.String@,System.Int32@) (compiler-generated)
                public void Deconstruct(out string Name, out int Age)
            T:Demo.ReadOnlyPoint
                public readonly struct ReadOnlyPoint
            F:Demo.ReadOnlyPoint.Y
                public readonly int Y
            P:Demo.ReadOnlyPoint.X
                public int X { get; init; }
            T:Demo.RefStruct
                public ref struct RefStruct
            F:Demo.RefStruct.Value
                public ref int Value
            F:Demo.RefStruct.ReadOnlyValue
                public ref readonly int ReadOnlyValue
            T:Demo.Transformer`2
                public delegate TResult Transformer<in T, out TResult>(T value, params object?[] arguments) where T : notnull
            M:Demo.Transformer`2.Invoke(`0,System.Object[])
                public virtual TResult Invoke(T value, params object?[] arguments)
            """);
    }

    [Fact]
    public async Task Model_TypesAndMembers()
    {
        var assembly = await ReadAssembly(ModelSource);

        var outer = Assert.Single(assembly.Types, type => type.Name is "Outer");
        Assert.Equal("Demo.Outer`1", outer.FullName);
        var inner2 = Assert.Single(outer.NestedTypes, type => type.Name is "Inner2");
        Assert.Same(outer, inner2.DeclaringType);
        Assert.Equal("Demo.Outer`1+Inner2`1", inner2.FullName);
        Assert.Equal("U", Assert.Single(inner2.GenericParameters).Name);
        Assert.Same(inner2, assembly.FindSymbolByDocumentationId("T:Demo.Outer`1.Inner2`1"));
        Assert.Same(inner2, assembly.FindType("Demo.Outer`1+Inner2`1"));

        var method = Assert.IsType<PublicApiMethod>(Assert.Single(inner2.Members, member => member.Name is "M"));
        Assert.Same(inner2, method.DeclaringType);
        Assert.Equal("M:Demo.Outer`1.Inner2`1.M``1(`0,``0)", method.DocumentationId);
        var typeParameter = Assert.IsType<PublicApiTypeParameterReference>(method.Parameters[0].Type);
        Assert.Equal(("T", 0, false), (typeParameter.Name, typeParameter.Ordinal, typeParameter.IsMethodTypeParameter));
        Assert.True(Assert.Single(method.GenericParameters).HasUnmanagedTypeConstraint);

        var create = Assert.IsType<PublicApiMethod>(Assert.Single(outer.Members, member => member.Name is "Create"));
        var returnType = Assert.IsType<PublicApiNamedTypeReference>(create.ReturnType);
        Assert.Equal("Inner2`1", returnType.MetadataName);
        Assert.Equal("Source", returnType.AssemblyName);
        Assert.Equal("T:Demo.Outer`1.Inner2`1", returnType.DocumentationId);
        Assert.IsType<PublicApiTypeParameterReference>(Assert.Single(returnType.ContainingType!.TypeArguments));
        Assert.Equal("String", Assert.IsType<PublicApiNamedTypeReference>(Assert.Single(returnType.TypeArguments)).MetadataName);

        var @base = assembly.FindType("Demo.Base")!;
        Assert.Equal(["T:System.Collections.Generic.IEnumerable`1", "T:System.Collections.IEnumerable", "T:System.IDisposable"], @base.Interfaces.Select(type => ((PublicApiNamedTypeReference)type).DocumentationId));

        var dictionary = (PublicApiMethod)assembly.FindSymbolByDocumentationId("M:Demo.Base.Dictionary")!;
        var dictionaryType = Assert.IsType<PublicApiNamedTypeReference>(dictionary.ReturnType);
        Assert.Equal(PublicApiNullableAnnotation.NotAnnotated, dictionaryType.NullableAnnotation);
        Assert.Equal([PublicApiNullableAnnotation.NotAnnotated, PublicApiNullableAnnotation.Annotated], dictionaryType.TypeArguments.Select(type => type.NullableAnnotation));

        var oblivious = (PublicApiMethod)assembly.FindSymbolByDocumentationId("M:Demo.Base.Oblivious(System.String)")!;
        Assert.Equal(PublicApiNullableAnnotation.Oblivious, oblivious.ReturnType.NullableAnnotation);
        Assert.Equal(PublicApiNullableAnnotation.Oblivious, oblivious.Parameters[0].Type.NullableAnnotation);

        var tuple = (PublicApiNamedTypeReference)((PublicApiMethod)assembly.FindSymbolByDocumentationId("M:Demo.Base.Tuple")!).ReturnType;
        Assert.True(tuple.IsTupleType);
        Assert.Equal(["Count", "Name"], tuple.TupleElementNames);

        var refKinds = (PublicApiMethod)assembly.FindSymbolByDocumentationId("M:Demo.Base.RefKinds(System.Int32@,System.Int32@,System.Int32@,System.Int32@)")!;
        Assert.Equal([PublicApiRefKind.Ref, PublicApiRefKind.Out, PublicApiRefKind.In, PublicApiRefKind.RefReadOnly], refKinds.Parameters.Select(parameter => parameter.RefKind));

        var defaults = (PublicApiMethod)assembly.FindSymbolByDocumentationId("M:Demo.Base.Defaults(System.Int32,System.String,Demo.Options,System.Threading.CancellationToken)")!;
        Assert.Equal([42, null, (byte)1, null], defaults.Parameters.Select(parameter => parameter.DefaultValue));
        Assert.All(defaults.Parameters, parameter => Assert.True(parameter.HasDefaultValue));

        var indexer = (PublicApiProperty)assembly.FindSymbolByDocumentationId("P:Demo.Base.Item(System.Int32,System.String)")!;
        Assert.True(indexer.IsIndexer);
        Assert.Equal(["index", "key"], indexer.Parameters.Select(parameter => parameter.Name));
        Assert.Null(indexer.SetMethod);
        Assert.Same(indexer, indexer.GetMethod!.AssociatedSymbol);
        Assert.Same(indexer.GetMethod, assembly.FindSymbolByDocumentationId("M:Demo.Base.get_Item(System.Int32,System.String)"));

        var value = (PublicApiProperty)assembly.FindSymbolByDocumentationId("P:Demo.Base.Value")!;
        Assert.Equal((PublicApiAccessibility.Public, true), (value.Accessibility, value.IsAbstract));
        Assert.Equal(PublicApiAccessibility.Protected, value.SetMethod!.Accessibility);
        Assert.True(((PublicApiProperty)assembly.FindSymbolByDocumentationId("P:Demo.Base.Required")!) is { IsRequired: true, IsInitOnly: true });

        // Attributes that are not displayed are still part of the model
        var hidden = assembly.FindSymbolByDocumentationId("M:Demo.Base.Hidden")!;
        Assert.Equal(["System.ComponentModel.EditorBrowsableAttribute", "System.ObsoleteAttribute"], hidden.Attributes.Select(attribute => attribute.AttributeType.FullName).Order(StringComparer.Ordinal));
        var editorBrowsable = hidden.Attributes.Single(attribute => attribute.AttributeType.Name is "EditorBrowsableAttribute");
        var editorBrowsableArgument = Assert.Single(editorBrowsable.ConstructorArguments);
        Assert.Equal(PublicApiAttributeArgumentKind.Enum, editorBrowsableArgument.Kind);
        Assert.Equal(1, editorBrowsableArgument.Value);
        Assert.Equal(["Never"], editorBrowsableArgument.EnumMemberNames);

        var dispose = (PublicApiMethod)assembly.FindSymbolByDocumentationId("M:Demo.Base.System#IDisposable#Dispose")!;
        Assert.True(dispose.IsExplicitInterfaceImplementation);
        Assert.Equal(PublicApiAccessibility.Private, dispose.Accessibility);
        Assert.Equal("M:System.IDisposable.Dispose", Assert.Single(dispose.ExplicitInterfaceImplementations).DocumentationId);

        var getEnumerator = (PublicApiMethod)assembly.FindSymbolByDocumentationId("M:Demo.Base.System#Collections#Generic#IEnumerable{System#String}#GetEnumerator")!;
        var implemented = Assert.Single(getEnumerator.ExplicitInterfaceImplementations);
        Assert.Equal("M:System.Collections.Generic.IEnumerable`1.GetEnumerator", implemented.DocumentationId);
        Assert.Equal("System.Collections.Generic.IEnumerable<string>", implemented.ContainingType.ToString());

        var options = assembly.FindType("Demo.Options")!;
        Assert.Equal("Byte", options.EnumUnderlyingType!.MetadataName);
        Assert.Equal([0, 1, 2], options.Members.Cast<PublicApiField>().Select(field => Convert.ToInt32(field.ConstantValue, CultureInfo.InvariantCulture)));

        var transformer = assembly.FindType("Demo.Transformer`2")!;
        Assert.Empty(transformer.Members);
        Assert.Equal("M:Demo.Transformer`2.Invoke(`0,System.Object[])", transformer.DelegateInvokeMethod!.DocumentationId);
        Assert.Equal([PublicApiVariance.Contravariant, PublicApiVariance.Covariant], transformer.GenericParameters.Select(parameter => parameter.Variance));

        // Compiler-generated members are part of the model, unless their name is unspeakable
        var person = assembly.FindType("Demo.Person")!;
        Assert.True(person.Members.Single(member => member.Name is "op_Equality").IsCompilerGenerated);
        Assert.DoesNotContain(person.Members, member => member.Name.Contains('<', StringComparison.Ordinal));
        Assert.Contains(person.Members, member => member.Name is "Deconstruct");
        Assert.True(((PublicApiProperty)person.Members.Single(member => member.Name is "Name")).GetMethod!.IsCompilerGenerated);
    }

    [Fact]
    public async Task Model_AttributeArgument_EnumNestedInGenericType()
    {
        var assembly = await ReadAssembly("""
            namespace Demo;

            public class Base<T>
            {
                public enum NestedEnum : byte { A, B }
            }

            public class Sample
            {
                [System.ComponentModel.DefaultValue(Base<int>.NestedEnum.B)]
                public int Property { get; set; }
            }
            """);

        var property = assembly.FindSymbolByDocumentationId("P:Demo.Sample.Property")!;
        var argument = Assert.Single(Assert.Single(property.Attributes).ConstructorArguments);
        Assert.Equal(PublicApiAttributeArgumentKind.Enum, argument.Kind);
        Assert.Equal((byte)1, argument.Value);
        Assert.Equal(["B"], argument.EnumMemberNames);
        Assert.Equal("Demo.Base<int>.NestedEnum", argument.Type.ToString());
    }

    [Fact]
    public async Task Model_DocumentationIds_MatchCompilerGeneratedXmlFile()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var assemblyPath = await CompileSource(temporaryDirectory, "source", "net10.0", """
            #nullable enable
            using System;
            using System.Collections;
            using System.Collections.Generic;

            /// <summary/>
            public class GlobalType
            {
                /// <summary/>
                public GlobalType() { }
            }

            namespace Demo
            {
                /// <summary/>
                public class Outer<T>
                {
                    /// <summary/>
                    public Outer() { }

                    /// <summary/>
                    public class Inner
                    {
                        /// <summary/>
                        public Inner() { }

                        /// <summary/>
                        public T? Value;
                    }

                    /// <summary/>
                    public class Inner2<U>
                    {
                        /// <summary/>
                        public Inner2() { }

                        /// <summary/>
                        public U M<V>(T t, V v, Inner2<U> self, Outer<int>.Inner2<string> other, Outer<T>.Inner inner) => default!;
                    }
                }

                /// <summary/>
                public interface IGeneric<TKey, TValue>
                {
                    /// <summary/>
                    void Add(TKey key, TValue value);

                    /// <summary/>
                    int Property { get; }

                    /// <summary/>
                    event EventHandler Event;

                    /// <summary/>
                    int this[TKey key] { get; }
                }

                /// <summary/>
                public delegate void Handler<T>(T value, ref int count);

                /// <summary/>
                public enum Kind
                {
                    /// <summary/>
                    First,

                    /// <summary/>
                    Second,
                }

                /// <summary/>
                public unsafe class Sample : IGeneric<string, int>, IEnumerable, IDisposable
                {
                    /// <summary/>
                    public Sample() { }

                    /// <summary/>
                    public Sample(int value) { }

                    /// <summary/>
                    public const int Constant = 1;

                    /// <summary/>
                    public event EventHandler? Changed;

                    /// <summary/>
                    public (int a, string? b) Tuple() => default;

                    /// <summary/>
                    public void Arrays(int[] a, int[,] b, int[][] c, int[,,][] d) { }

                    /// <summary/>
                    public void ByRef(ref int a, out int b, in int c, ref readonly int d, ref string e) { b = 0; }

                    /// <summary/>
                    public void Pointers(int* a, void** b, delegate*<int, ref int, void> c, delegate* unmanaged<int> d) { }

                    /// <summary/>
                    public void Generic<T1, T2>(T1 a, List<T2> b, Dictionary<T1, List<T2[]>> c) { }

                    /// <summary/>
                    public void Nullable(int? a, List<int?>? b, KeyValuePair<int?, string?>? c) { }

                    /// <summary/>
                    public void Misc(dynamic a, nint b, nuint c, object d, params string[] e) { }

                    /// <summary/>
                    public int this[int index, string key] => 0;

                    /// <summary/>
                    public string this[string key] { get => ""; set { } }

                    /// <summary/>
                    public static implicit operator int(Sample sample) => 0;

                    /// <summary/>
                    public static explicit operator Sample(int value) => null!;

                    /// <summary/>
                    public static explicit operator checked Sample(int value) => null!;

                    /// <summary/>
                    public static Sample operator +(Sample left, Sample right) => left;

                    /// <summary/>
                    public static Sample operator checked +(Sample left, Sample right) => left;

                    /// <summary/>
                    public static bool operator ==(Sample? left, Sample? right) => true;

                    /// <summary/>
                    public static bool operator !=(Sample? left, Sample? right) => false;

                    /// <summary/>
                    public override bool Equals(object? obj) => true;

                    /// <summary/>
                    public override int GetHashCode() => 0;

                    /// <summary/>
                    void IGeneric<string, int>.Add(string key, int value) { }

                    /// <summary/>
                    int IGeneric<string, int>.Property => 0;

                    /// <summary/>
                    event EventHandler IGeneric<string, int>.Event { add { } remove { } }

                    /// <summary/>
                    int IGeneric<string, int>.this[string key] => 0;

                    /// <summary/>
                    IEnumerator IEnumerable.GetEnumerator() => null!;

                    /// <summary/>
                    void IDisposable.Dispose() { }

                    /// <summary/>
                    ~Sample() { }

                    /// <summary/>
                    public struct NestedStruct
                    {
                        /// <summary/>
                        public static NestedStruct operator -(NestedStruct value) => value;
                    }
                }

                /// <summary/>
                public static class Extensions
                {
                    /// <summary/>
                    public static void Classic(this string value) { }
                }
            }
            """, generateDocumentationFile: true);

        var assembly = PublicApi.ReadAssembly(assemblyPath);
        var documentedIds = XDocument.Load(assemblyPath.ChangeExtension(".xml")).Descendants("member").Select(member => (string)member.Attribute("name")!).ToList();
        Assert.NotEmpty(documentedIds);

        // Every documented symbol can be found using the ID generated by the compiler
        Assert.All(documentedIds, id => Assert.Equal(id, assembly.FindSymbolByDocumentationId(id)?.DocumentationId));

        // Every symbol of the model has the ID generated by the compiler, except the ones that cannot have a documentation comment
        var undocumentedIds = assembly.GetAllSymbols()
            .Where(symbol => symbol is not PublicApiMethod { AssociatedSymbol: not null } and not PublicApiMethod { MethodKind: PublicApiMethodKind.DelegateInvoke })
            .Select(symbol => symbol.DocumentationId)
            .Except(documentedIds, StringComparer.Ordinal);
        Assert.Empty(undocumentedIds);

        var accessor = assembly.FindSymbolByDocumentationId("M:Demo.Sample.Demo#IGeneric{System#String,System#Int32}#get_Property");
        Assert.Equal("P:Demo.Sample.Demo#IGeneric{System#String,System#Int32}#Property", Assert.IsType<PublicApiMethod>(accessor).AssociatedSymbol!.DocumentationId);
        var explicitIndexer = (PublicApiProperty)assembly.FindSymbolByDocumentationId("P:Demo.Sample.Demo#IGeneric{System#String,System#Int32}#Item(System.String)")!;
        Assert.Equal("P:Demo.IGeneric`2.Item(`0)", Assert.Single(explicitIndexer.ExplicitInterfaceImplementations).DocumentationId);
        var explicitEvent = (PublicApiEvent)assembly.FindSymbolByDocumentationId("E:Demo.Sample.Demo#IGeneric{System#String,System#Int32}#Event")!;
        Assert.Equal("E:Demo.IGeneric`2.Event", Assert.Single(explicitEvent.ExplicitInterfaceImplementations).DocumentationId);
    }

    [Fact]
    public async Task Model_Provenance()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var assemblyPath = await CompileSource(temporaryDirectory, "net10.0", """
            namespace Demo;

            public class Sample
            {
                public int Field;
                public int Property { get; set; }
                public event System.EventHandler? Changed;
                public void Method() { }
                public class Nested { }
            }
            """);

        var assembly = PublicApi.ReadAssembly(assemblyPath);
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var metadataReader = peReader.GetMetadataReader();
        var moduleVersionId = metadataReader.GetGuid(metadataReader.GetModuleDefinition().Mvid);
        Assert.Equal(moduleVersionId, assembly.Module.ModuleVersionId);
        Assert.Equal("Source.dll", assembly.Module.Name);
        Assert.Contains(moduleVersionId.ToString("D"), assembly.Scope, StringComparison.Ordinal);
        Assert.NotEmpty(assembly.Module.PdbReferences);

        foreach (var symbol in assembly.GetAllSymbols())
        {
            var origin = symbol.Origin!;
            Assert.Equal(moduleVersionId, origin.ModuleVersionId);
            var handle = MetadataTokens.EntityHandle(origin.MetadataToken);
            var name = handle.Kind switch
            {
                HandleKind.TypeDefinition => metadataReader.GetString(metadataReader.GetTypeDefinition((TypeDefinitionHandle)handle).Name),
                HandleKind.MethodDefinition => metadataReader.GetString(metadataReader.GetMethodDefinition((MethodDefinitionHandle)handle).Name),
                HandleKind.FieldDefinition => metadataReader.GetString(metadataReader.GetFieldDefinition((FieldDefinitionHandle)handle).Name),
                HandleKind.PropertyDefinition => metadataReader.GetString(metadataReader.GetPropertyDefinition((PropertyDefinitionHandle)handle).Name),
                HandleKind.EventDefinition => metadataReader.GetString(metadataReader.GetEventDefinition((EventDefinitionHandle)handle).Name),
                _ => throw new InvalidOperationException("Unexpected handle kind " + handle.Kind),
            };

            Assert.Equal(symbol.MetadataName, name);
        }

        var property = (PublicApiProperty)assembly.FindSymbolByDocumentationId("P:Demo.Sample.Property")!;
        Assert.Equal(HandleKind.PropertyDefinition, MetadataTokens.EntityHandle(property.Origin!.MetadataToken).Kind);
        Assert.Equal("get_Property", metadataReader.GetString(metadataReader.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.EntityHandle(property.GetMethod!.Origin!.MetadataToken)).Name));
        Assert.Equal("set_Property", metadataReader.GetString(metadataReader.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.EntityHandle(property.SetMethod!.Origin!.MetadataToken)).Name));
        var @event = (PublicApiEvent)assembly.FindSymbolByDocumentationId("E:Demo.Sample.Changed")!;
        Assert.Equal("add_Changed", metadataReader.GetString(metadataReader.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.EntityHandle(@event.AddMethod.Origin!.MetadataToken)).Name));
        Assert.Equal("remove_Changed", metadataReader.GetString(metadataReader.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.EntityHandle(@event.RemoveMethod!.Origin!.MetadataToken)).Name));
    }

    [Fact]
    public async Task Model_ReadFromStream_RemainsUsableAfterTheStreamIsDisposed()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var assemblyPath = await CompileSource(temporaryDirectory, "net10.0", ModelSource);

        PublicApiAssembly assembly;
        var stream = new MemoryStream(File.ReadAllBytes(assemblyPath));
        await using (stream)
        {
            assembly = PublicApi.ReadAssembly(stream, new PublicApiReadOptions { TargetFramework = "net10.0", InputIdentity = "Demo/1.0.0/lib/net10.0/Source.dll" });
            Assert.True(stream.CanRead);
        }

        Assert.Equal("net10.0", assembly.TargetFramework);
        Assert.Equal(".NETCoreApp,Version=v10.0", assembly.TargetFrameworkMoniker);
        Assert.Equal("Demo/1.0.0/lib/net10.0/Source.dll", assembly.Scope);
        Assert.Equal(DumpModel(PublicApi.ReadAssembly(assemblyPath)), DumpModel(assembly));

        // Reading an assembly never loads it
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), loadedAssembly => !loadedAssembly.IsDynamic && string.Equals(loadedAssembly.Location, assemblyPath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Model_SymbolIdentity()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var assemblyPath = await CompileSource(temporaryDirectory, "net10.0", "public class Sample { }");

        var first = PublicApi.ReadAssembly(assemblyPath, new PublicApiReadOptions { InputIdentity = "first" });
        var second = PublicApi.ReadAssembly(assemblyPath, new PublicApiReadOptions { InputIdentity = "second" });
        var firstType = first.FindSymbolByDocumentationId("T:Sample")!;
        var secondType = second.FindSymbolByDocumentationId("T:Sample")!;
        Assert.Equal(firstType.DocumentationId, secondType.DocumentationId);
        Assert.NotEqual(firstType.Identity, secondType.Identity);
        Assert.Equal(new PublicApiSymbolIdentity("first", "T:Sample"), firstType.Identity);
        Assert.Equal(first.Module.ModuleVersionId, firstType.Origin!.ModuleVersionId);
    }

    [Fact]
    public async Task Format_Declaration()
    {
        var assembly = await ReadAssembly(ModelSource);
        var method = assembly.FindSymbolByDocumentationId("M:Demo.Outer`1.Create")!;
        var declaration = PublicApiFormatter.Format(method);
        Assert.Equal("public Demo.Outer<T>.Inner2<string> Create()", declaration.Text);
        Assert.Equal(declaration.Text, string.Concat(declaration.Segments.Select(segment => segment.Text)));

        var identifier = Assert.Single(declaration.Segments, segment => segment.Kind == PublicApiDeclarationSegmentKind.Identifier);
        Assert.Equal(("Create", method), (identifier.Text, identifier.Symbol));
        var typeNames = declaration.Segments.Where(segment => segment.Kind == PublicApiDeclarationSegmentKind.TypeName).ToList();
        Assert.Equal(["Demo.Outer", "Inner2", "string"], typeNames.Select(segment => segment.Text));
        Assert.Equal(["T:Demo.Outer`1", "T:Demo.Outer`1.Inner2`1", "T:System.String"], typeNames.Select(segment => ((PublicApiNamedTypeReference)segment.TypeReference!).DocumentationId));
        Assert.Same(assembly.FindSymbolByDocumentationId("T:Demo.Outer`1.Inner2`1"), assembly.FindSymbolByDocumentationId(((PublicApiNamedTypeReference)typeNames[1].TypeReference!).DocumentationId));

        var unqualified = new PublicApiFormattingOptions { QualifyTypeNames = false };
        Assert.Equal("public static int Count(this IEnumerable<int> source)", PublicApiFormatter.Format(assembly.FindSymbolByDocumentationId("M:Demo.Extensions.Count(System.Collections.Generic.IEnumerable{System.Int32})")!, unqualified).Text);

        var hidden = assembly.FindSymbolByDocumentationId("M:Demo.Base.Hidden")!;
        Assert.Equal("[System.Obsolete(\"Use Value\")]\npublic int Hidden()", PublicApiFormatter.Format(hidden, new PublicApiFormattingOptions { NewLine = "\n" }).Text);
        Assert.Equal("public int Hidden()", PublicApiFormatter.Format(hidden, new PublicApiFormattingOptions { IncludeAttributes = false }).Text);
        Assert.Equal("[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]", PublicApiFormatter.Format(hidden.Attributes.Single(attribute => attribute.AttributeType.Name is "EditorBrowsableAttribute")).Text);

        var setter = ((PublicApiProperty)assembly.FindSymbolByDocumentationId("P:Demo.Base.Value")!).SetMethod!;
        Assert.Equal("public abstract int Value { protected set; }", PublicApiFormatter.Format(setter).Text);
    }

    [Fact]
    public async Task Format_Compilable()
    {
        var assembly = await ReadAssembly(ModelSource);
        var options = new PublicApiFormattingOptions { Style = PublicApiDeclarationStyle.Compilable, NewLine = "\n" };
        Assert.Equal("public abstract int Value { get; protected set; }", PublicApiFormatter.Format(assembly.FindSymbolByDocumentationId("P:Demo.Base.Value")!, options).Text);
        Assert.Equal("public Person(string Name, int Age) { }", PublicApiFormatter.Format(assembly.FindSymbolByDocumentationId("M:Demo.Person.#ctor(System.String,System.Int32)")!, options).Text);
        Assert.Equal("""
            public class Inner2<U> where U : notnull
            {
                public U M<V>(T t, V v) where V : unmanaged => throw null;
            }

            """, PublicApiFormatter.Format(assembly.FindSymbolByDocumentationId("T:Demo.Outer`1.Inner2`1")!, options).Text);
    }

    [Fact]
    public async Task Aggregate_TargetFrameworks()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        const string Source = """
            using System;
            using System.Collections.Generic;

            namespace Demo;

            public interface IMarker { }

            public class Sample
            #if NET10_0_OR_GREATER
                : IMarker
            #endif
            {
                public void Shared() { }
            #if NET10_0_OR_GREATER
                public void NewMethod() { }
                public string? Nullability(string? value) => value;
                [Obsolete]
                public void Attribute() { }
                public void Constraint<T>() where T : class { }
                public void Signature(long value) { }
                public long ReturnType() => 0;
                public virtual void Modifier(in int value) { }
            #else
                public string Nullability(string value) => value;
                public void Attribute() { }
                public void Constraint<T>() { }
                public void Signature(int value) { }
                public int ReturnType() => 0;
                public void Modifier(ref int value) { }
            #endif

                public class Nested<T>
            #if NET10_0_OR_GREATER
                    where T : notnull
            #endif
                {
                }
            }

            #if NET10_0_OR_GREATER
            public class NewType { }
            #endif
            """;

        var net8 = PublicApi.ReadAssembly(await CompileSource(temporaryDirectory, "net8_0", "net8.0", Source));
        var net10 = PublicApi.ReadAssembly(await CompileSource(temporaryDirectory, "net10_0", "net10.0", Source));
        var aggregate = PublicApi.Aggregate([net8, net10]);

        Assert.Equal(["net8.0", "net10.0"], aggregate.TargetFrameworks);
        Assert.Same(net10, aggregate.GetAssembly("net10.0"));
        InlineSnapshot.Validate(DumpAggregate(aggregate), """
            T:Demo.IMarker [net8.0, net10.0]
                [net8.0, net10.0] public interface IMarker
            T:Demo.NewType [net10.0]
                [net10.0] public class NewType
            M:Demo.NewType.#ctor [net10.0]
                [net10.0] public NewType()
            T:Demo.Sample [net8.0, net10.0] differences: Inheritance
                [net8.0] public class Sample
                [net10.0] public class Sample : Demo.IMarker
            M:Demo.Sample.Shared [net8.0, net10.0]
                [net8.0, net10.0] public void Shared()
            M:Demo.Sample.Nullability(System.String) [net8.0, net10.0] differences: Nullability
                [net8.0] public string Nullability(string value)
                [net10.0] public string? Nullability(string? value)
            M:Demo.Sample.Attribute [net8.0, net10.0] differences: Attributes
                [net8.0] public void Attribute()
                [net10.0] [System.Obsolete] public void Attribute()
            M:Demo.Sample.Constraint``1 [net8.0, net10.0] differences: Constraints
                [net8.0] public void Constraint<T>()
                [net10.0] public void Constraint<T>() where T : class
            M:Demo.Sample.Signature(System.Int32) [net8.0]
                [net8.0] public void Signature(int value)
            M:Demo.Sample.ReturnType [net8.0, net10.0] differences: Signature
                [net8.0] public int ReturnType()
                [net10.0] public long ReturnType()
            M:Demo.Sample.Modifier(System.Int32@) [net8.0, net10.0] differences: Modifiers, Signature
                [net8.0] public void Modifier(ref int value)
                [net10.0] public virtual void Modifier(in int value)
            M:Demo.Sample.#ctor [net8.0, net10.0]
                [net8.0, net10.0] public Sample()
            M:Demo.Sample.NewMethod [net10.0]
                [net10.0] public void NewMethod()
            M:Demo.Sample.Signature(System.Int64) [net10.0]
                [net10.0] public void Signature(long value)
            T:Demo.Sample.Nested`1 [net8.0, net10.0] differences: Constraints
                [net8.0] public class Nested<T>
                [net10.0] public class Nested<T> where T : notnull
            M:Demo.Sample.Nested`1.#ctor [net8.0, net10.0]
                [net8.0, net10.0] public Nested()
            """);

        var shared = aggregate.FindSymbolByDocumentationId("M:Demo.Sample.Shared")!;
        Assert.Equal([net8.Module.ModuleVersionId, net10.Module.ModuleVersionId], Assert.Single(shared.Variants).Symbols.Select(symbol => symbol.Origin!.ModuleVersionId));
        Assert.Same(net8, shared.GetSymbol("net8.0")!.Assembly);
        Assert.Null(aggregate.FindSymbolByDocumentationId("T:Demo.NewType")!.GetSymbol("net8.0"));
    }

    [Fact]
    public async Task Aggregate_RejectsAmbiguousInputs()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var assemblyPath = await CompileSource(temporaryDirectory, "net10.0", "public class Sample { }");
        var first = PublicApi.ReadAssembly(assemblyPath, new PublicApiReadOptions { TargetFramework = "net10.0" });
        var second = PublicApi.ReadAssembly(assemblyPath, new PublicApiReadOptions { TargetFramework = "NET10.0" });
        var other = PublicApi.ReadAssembly(typeof(PublicApi).Assembly.Location, new PublicApiReadOptions { TargetFramework = "net8.0" });

        Assert.Throws<ArgumentException>(() => PublicApi.Aggregate([first, second]));
        Assert.Throws<ArgumentException>(() => PublicApi.Aggregate([first, other]));
        Assert.Throws<ArgumentException>(() => PublicApi.Aggregate([]));
    }

    private static async Task<PublicApiAssembly> ReadAssembly(string source)
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        return PublicApi.ReadAssembly(await CompileSource(temporaryDirectory, "net10.0", source));
    }

    private static Task<FullPath> CompileSource(TemporaryDirectory temporaryDirectory, string targetFramework, string source)
    {
        return CompileSource(temporaryDirectory, "source", targetFramework, source);
    }

    private static string DumpModel(PublicApiAssembly assembly)
    {
        var options = new PublicApiFormattingOptions { NewLine = "\n" };
        var sb = new StringBuilder();
        foreach (var symbol in assembly.GetAllSymbols())
        {
            if (symbol is PublicApiMethod { AssociatedSymbol: not null })
                continue;

            sb.Append(symbol.DocumentationId);
            if (symbol.IsCompilerGenerated)
            {
                sb.Append(" (compiler-generated)");
            }

            sb.Append('\n');
            foreach (var line in PublicApiFormatter.Format(symbol, options).Text.Split('\n'))
            {
                sb.Append("    ").Append(line).Append('\n');
            }
        }

        return sb.ToString().TrimEnd('\n');
    }

    private static string DumpAggregate(PublicApiAggregatedAssembly assembly)
    {
        var options = new PublicApiFormattingOptions { NewLine = "\n" };
        var sb = new StringBuilder();
        foreach (var type in assembly.Types)
        {
            Append(type);
        }

        return sb.ToString().TrimEnd('\n');

        void Append(PublicApiAggregatedSymbol symbol)
        {
            sb.Append(symbol.DocumentationId).Append(" [").AppendJoin(", ", symbol.TargetFrameworks).Append(']');
            if (symbol.Differences != PublicApiSymbolDifferences.None)
            {
                sb.Append(" differences: ").Append(symbol.Differences);
            }

            sb.Append('\n');
            foreach (var variant in symbol.Variants)
            {
                sb.Append("    [").AppendJoin(", ", variant.TargetFrameworks).Append("] ").Append(PublicApiFormatter.Format(variant.Symbol, options).Text.Replace("\n", " ", StringComparison.Ordinal)).Append('\n');
            }

            foreach (var member in symbol.Members)
            {
                Append(member);
            }

            foreach (var nestedType in symbol.NestedTypes)
            {
                Append(nestedType);
            }
        }
    }

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

        var features = compilerOptions.UpdatedMemorySafetyRules ? "\n    <Features>$(Features);updated-memory-safety-rules</Features>" : "";

        // Build the project
        var sourceProjectDirectory = temporaryDirectory / "source";
        temporaryDirectory.CreateTextFile(sourceProjectDirectory / "project.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{{compilerOptions.TargetFramework}}</TargetFramework>
                <LangVersion>preview</LangVersion>
                <Nullable>{{(compilerOptions.Nullable ? "enable" : "disable")}}</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>{{features}}
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
        var expectedWithTargetFrameworkHeader = AddTargetFrameworksHeader(expected, [compilerOptions.TargetFramework]);
        InlineSnapshot.Validate(reflectionContent, expectedWithTargetFrameworkHeader, filePath, lineNumber);

        // Ensure the generated files are compilable
        var generatedDirectory = temporaryDirectory / "generated";
        temporaryDirectory.CreateTextFile(generatedDirectory / "project.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{{compilerOptions.TargetFramework}}</TargetFramework>
                <LangVersion>preview</LangVersion>
                <Nullable>{{(compilerOptions.Nullable ? "enable" : "disable")}}</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>{{features}}
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
        var features = compilerOptions.UpdatedMemorySafetyRules ? "\n    <Features>$(Features);updated-memory-safety-rules</Features>" : "";

        var sourceProjectDirectory = temporaryDirectory / "source";
        temporaryDirectory.CreateTextFile(sourceProjectDirectory / "project.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{{compilerOptions.TargetFramework}}</TargetFramework>
                <LangVersion>preview</LangVersion>
                <Nullable>{{(compilerOptions.Nullable ? "enable" : "disable")}}</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>{{features}}
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

    private static string AddTargetFrameworksHeader(string content, IReadOnlyList<string> targetFrameworks)
    {
        var orderedTargetFrameworks = targetFrameworks
            .Select(NormalizeTargetFramework)
            .OrderBy(targetFramework => targetFramework, StringComparer.Ordinal)
            .ToArray();
        var header = "// Target Frameworks: " + string.Join(", ", orderedTargetFrameworks);
        var nullableEnableDirective = "#nullable enable";
        var nullableEnableDirectiveIndex = content.IndexOf(nullableEnableDirective, StringComparison.Ordinal);
        if (nullableEnableDirectiveIndex < 0)
        {
            return header + "\n" + content;
        }

        const string AssemblyAttributePrefix = "[assembly:";
        var assemblyAttributeIndex = content.IndexOf(AssemblyAttributePrefix, StringComparison.Ordinal);
        var insertionIndex = assemblyAttributeIndex >= 0 && assemblyAttributeIndex < nullableEnableDirectiveIndex
            ? assemblyAttributeIndex
            : nullableEnableDirectiveIndex;
        return string.Concat(content.AsSpan(0, insertionIndex), header, "\n", content.AsSpan(insertionIndex));
    }

    private static string NormalizeTargetFramework(string targetFramework)
    {
        var normalizedTargetFramework = targetFramework.Trim().ToLowerInvariant();
        var platformSeparatorIndex = normalizedTargetFramework.IndexOf('-', StringComparison.Ordinal);
        if (platformSeparatorIndex >= 0)
        {
            normalizedTargetFramework = normalizedTargetFramework[..platformSeparatorIndex];
        }

        return normalizedTargetFramework;
    }

    private static async Task<FullPath> CompileSource(TemporaryDirectory temporaryDirectory, string projectDirectoryName, string targetFramework, string source, bool generateDocumentationFile = false)
    {
        var documentation = generateDocumentationFile ? "\n    <GenerateDocumentationFile>true</GenerateDocumentationFile>" : "";
        var sourceProjectDirectory = temporaryDirectory / projectDirectoryName;
        temporaryDirectory.CreateTextFile(sourceProjectDirectory / "project.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{{targetFramework}}</TargetFramework>
                <LangVersion>preview</LangVersion>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>{{documentation}}
              </PropertyGroup>
            </Project>
            """);
        temporaryDirectory.CreateTextFile(sourceProjectDirectory / "Sample.cs", source);
        return await Compile(sourceProjectDirectory);
    }

    private static async Task<FullPath> Compile(FullPath temporaryDirectory)
    {
        var projectPath = FullPath.FromPath(Assert.Single(Directory.EnumerateFiles(temporaryDirectory, "*.csproj", SearchOption.TopDirectoryOnly)));
        var cacheDirectory = GetCompilationCacheDirectory();
        var cacheKey = ComputeCompilationCacheKey(temporaryDirectory, await DotNetSdkVersion.Value);
        var cachedAssemblyPath = cacheDirectory / cacheKey / "Source.dll";
        if (File.Exists(cachedAssemblyPath))
            return cachedAssemblyPath;

        await using var _ = await AcquireCompilationLockAsync(cacheDirectory, cacheKey);
        if (File.Exists(cachedAssemblyPath))
            return cachedAssemblyPath;

        var outputPath = temporaryDirectory / "bin";
        await RunDotNetAsync(temporaryDirectory, ["restore", projectPath, "-nologo", "--disable-build-servers"]);
        await RunDotNetAsync(temporaryDirectory, ["build", projectPath, "-nologo", "--disable-build-servers", "--no-restore", "--output", outputPath, "/p:AssemblyName=Source"]);

        var builtAssemblyPath = outputPath / "Source.dll";
        var builtDocumentationPath = outputPath / "Source.xml";
        if (File.Exists(builtDocumentationPath))
        {
            // Copied before the assembly, whose presence marks the cache entry as complete
            cachedAssemblyPath.CreateParentDirectory();
            File.Copy(builtDocumentationPath, cachedAssemblyPath.ChangeExtension(".xml"), overwrite: true);
        }

        var stagingAssemblyPath = cacheDirectory / "staging" / cacheKey / $"{Guid.NewGuid():N}.dll";
        stagingAssemblyPath.CreateParentDirectory();
        File.Copy(builtAssemblyPath, stagingAssemblyPath, overwrite: true);

        cachedAssemblyPath.CreateParentDirectory();
        if (!File.Exists(cachedAssemblyPath))
        {
            File.Move(stagingAssemblyPath, cachedAssemblyPath);
        }
        else
        {
            File.Delete(stagingAssemblyPath);
        }

        return cachedAssemblyPath;

        static async Task RunDotNetAsync(FullPath workingDirectory, IReadOnlyList<string> arguments)
        {
            const int MaxRetries = 5;

            for (var retryCount = 0; retryCount <= MaxRetries; retryCount++)
            {
                try
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

                    return;
                }
                catch when (retryCount < MaxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), XunitCancellationToken);
                }
            }
        }
    }

    private static FullPath GetCompilationCacheDirectory()
    {
        var configuredPath = Environment.GetEnvironmentVariable(CompilationCacheDirectoryEnvironmentVariable);
        if (!string.IsNullOrEmpty(configuredPath))
            return FullPath.FromPath(configuredPath);

        return FullPath.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) / "Meziantou.Framework" / "PublicApiGeneratorTests" / "CompilationCache";
    }

    private static async Task<string> GetDotNetSdkVersionAsync()
    {
        // The compiler ships with the SDK, so the cached assemblies must not be reused after an SDK update.
        // The sample projects are built in the temporary directory, so resolve the SDK from there to ignore the global.json of the repository.
        var processResult = await ProcessWrapper.Create("dotnet")
            .WithArguments(["--version"])
            .WithWorkingDirectory(FullPath.FromPath(Path.GetTempPath()))
            .WithEnvironmentVariables(env => env.Set("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1"))
            .WithValidation(ProcessValidationMode.None)
            .ExecuteBufferedAsync(XunitCancellationToken);

        if (!processResult.ExitCode.IsSuccess)
            throw new XunitException($"Command failed: dotnet --version\nstderr:\n{string.Join('\n', processResult.Output.StandardError.Select(line => line.Text))}");

        return string.Join('\n', processResult.Output.StandardOutput.Select(line => line.Text)).Trim();
    }

    private static string ComputeCompilationCacheKey(FullPath sourceProjectDirectory, string sdkVersion)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AddHashData(hash, CompilationCacheVersion);
        AddHashData(hash, sdkVersion);

        var files = Directory
            .EnumerateFiles(sourceProjectDirectory, "*", SearchOption.AllDirectories)
            .Select(FullPath.FromPath)
            .Select(path => (Path: path, RelativePath: path.MakePathRelativeTo(sourceProjectDirectory).Replace('\\', '/')))
            .OrderBy(path => path.RelativePath, StringComparer.Ordinal);

        foreach (var file in files)
        {
            AddHashData(hash, file.RelativePath);
            AddHashData(hash, File.ReadAllBytes(file.Path));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AddHashData(IncrementalHash hash, string value)
    {
        AddHashData(hash, Encoding.UTF8.GetBytes(value));
    }

    private static void AddHashData(IncrementalHash hash, byte[] value)
    {
        Span<byte> lengthBuffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, value.Length);
        hash.AppendData(lengthBuffer);
        hash.AppendData(value);
    }

    private static async Task<FileStream> AcquireCompilationLockAsync(FullPath cacheDirectory, string cacheKey)
    {
        var lockFilePath = cacheDirectory / "locks" / $"{cacheKey}.lock";
        lockFilePath.CreateParentDirectory();

        while (true)
        {
            try
            {
                return new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(50, XunitCancellationToken);
            }
        }
    }

    public sealed class CompilerOptions
    {
        public bool Nullable { get; set; } = true;
        public string TargetFramework { get; set; } = "net8.0";
        public bool UpdatedMemorySafetyRules { get; set; }
    }
}
