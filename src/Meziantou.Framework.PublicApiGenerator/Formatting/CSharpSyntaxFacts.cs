namespace Meziantou.Framework.PublicApiGenerator;

internal static class CSharpSyntaxFacts
{
    private const string CompilerGeneratedRefStructObsoleteMessage = "Types with embedded references are not supported in this version of your compiler.";
    private const string CompilerGeneratedRequiredMembersObsoleteMessage = "Constructors of types with required members are not supported in this version of your compiler.";

    // Attributes that only matter to the compiler or to the runtime, or that are represented by the C# syntax
    private static readonly HashSet<string> IrrelevantAttributes = new(StringComparer.Ordinal)
    {
        "System.CodeDom.Compiler.GeneratedCodeAttribute",
        "System.ComponentModel.EditorBrowsableAttribute",
        "System.Runtime.CompilerServices.AsyncStateMachineAttribute",
        "System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute",
        "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
        "System.Runtime.CompilerServices.CompilationRelaxationsAttribute",
        "System.Runtime.CompilerServices.ExtensionAttribute",
        "System.Runtime.CompilerServices.ExtensionMarkerAttribute",
        "System.Runtime.CompilerServices.RuntimeCompatibilityAttribute",
        "System.Runtime.CompilerServices.IteratorStateMachineAttribute",
        "System.Runtime.CompilerServices.IsReadOnlyAttribute",
        "System.Runtime.CompilerServices.IsByRefLikeAttribute",
        "System.Runtime.CompilerServices.CompilerFeatureRequiredAttribute",
        "System.Runtime.CompilerServices.NullableAttribute",
        "System.Runtime.CompilerServices.NullableContextAttribute",
        "System.Runtime.CompilerServices.IsUnmanagedAttribute",
        "System.Reflection.DefaultMemberAttribute",
        "System.Diagnostics.DebuggableAttribute",
        "System.Diagnostics.DebuggerNonUserCodeAttribute",
        "System.Diagnostics.DebuggerStepThroughAttribute",
        "System.Runtime.InteropServices.DefaultParameterValueAttribute",
        "System.Runtime.InteropServices.OptionalAttribute",
        "System.Runtime.InteropServices.InAttribute",
        "System.Runtime.InteropServices.OutAttribute",
        "System.Runtime.CompilerServices.RequiresLocationAttribute",
        "System.ParamArrayAttribute",
        "System.Runtime.CompilerServices.ParamCollectionAttribute",
        "System.Runtime.CompilerServices.ScopedRefAttribute",
        "System.Runtime.CompilerServices.ClosedAttribute",
        "System.Runtime.CompilerServices.IsClosedTypeAttribute",
        PublicApiMetadataReader.MemorySafetyRulesAttributeFullName,
        PublicApiMetadataReader.RequiresUnsafeAttributeFullName,
        "System.Reflection.AssemblyCompanyAttribute",
        "System.Reflection.AssemblyConfigurationAttribute",
        "System.Reflection.AssemblyCopyrightAttribute",
        "System.Reflection.AssemblyDescriptionAttribute",
        "System.Reflection.AssemblyFileVersionAttribute",
        "System.Reflection.AssemblyInformationalVersionAttribute",
        "System.Reflection.AssemblyProductAttribute",
        "System.Reflection.AssemblyTitleAttribute",
        "System.Reflection.AssemblyTrademarkAttribute",
        "System.Runtime.CompilerServices.RequiredMemberAttribute",

        // A P/Invoke is written as any other method: the attribute read by the interop source generator is an implementation detail,
        // and so is the attribute that the generator adds to skip the initialization of the locals
        "System.Runtime.InteropServices.LibraryImportAttribute",
        "System.Runtime.CompilerServices.SkipLocalsInitAttribute",
    };

    // Attributes that are represented with the C# syntax: tuple element names, dynamic, nint and decimal default values.
    // The compiler rejects most of them when they are written explicitly.
    private static readonly HashSet<string> SyntaxAttributes = new(StringComparer.Ordinal)
    {
        "System.Runtime.CompilerServices.TupleElementNamesAttribute",
        "System.Runtime.CompilerServices.DynamicAttribute",
        "System.Runtime.CompilerServices.NativeIntegerAttribute",
        "System.Runtime.CompilerServices.DecimalConstantAttribute",
    };

    public static bool IsDisplayedAttribute(PublicApiAttribute attribute, PublicApiDeclarationStyle style)
    {
        var attributeType = attribute.AttributeType;
        if (attributeType.ContainingType is not null || !attributeType.TypeArguments.IsEmpty)
            return false;

        var fullName = attributeType.FullName;
        if (!fullName.StartsWith("System.", StringComparison.Ordinal))
            return false;

        if (IrrelevantAttributes.Contains(fullName))
            return false;

        if (SyntaxAttributes.Contains(fullName))
            return false;

        // The Compilable style has always written the attribute the compiler adds to the constructors of types with required members
        return !IsCompilerGeneratedObsoleteAttribute(attribute, CompilerGeneratedRefStructObsoleteMessage) &&
               !(style == PublicApiDeclarationStyle.Declaration && IsCompilerGeneratedObsoleteAttribute(attribute, CompilerGeneratedRequiredMembersObsoleteMessage));
    }

    private static bool IsCompilerGeneratedObsoleteAttribute(PublicApiAttribute attribute, string compilerMessage)
    {
        if (!attribute.AttributeType.IsSystemType("ObsoleteAttribute"))
            return false;

        var arguments = attribute.ConstructorArguments;
        return arguments.Length >= 2 &&
               arguments[0].Value is string message &&
               string.Equals(message, compilerMessage, StringComparison.Ordinal) &&
               arguments[1].Value is true;
    }

    public static string? GetOperatorToken(string methodName)
    {
        return methodName switch
        {
            "op_UnaryPlus" => "+",
            "op_UnaryNegation" => "-",
            "op_LogicalNot" => "!",
            "op_OnesComplement" => "~",
            "op_Increment" => "++",
            "op_Decrement" => "--",
            "op_True" => "true",
            "op_False" => "false",
            "op_Implicit" => "implicit",
            "op_Explicit" => "explicit",
            "op_Addition" => "+",
            "op_Subtraction" => "-",
            "op_Multiply" => "*",
            "op_Multiplication" => "*",
            "op_Division" => "/",
            "op_Modulus" => "%",
            "op_BitwiseAnd" => "&",
            "op_BitwiseOr" => "|",
            "op_ExclusiveOr" => "^",
            "op_LeftShift" => "<<",
            "op_RightShift" => ">>",
            "op_UnsignedRightShift" => ">>>",
            "op_Equality" => "==",
            "op_Inequality" => "!=",
            "op_LessThan" => "<",
            "op_GreaterThan" => ">",
            "op_LessThanOrEqual" => "<=",
            "op_GreaterThanOrEqual" => ">=",
            "op_AdditionAssignment" => "+=",
            "op_SubtractionAssignment" => "-=",
            "op_MultiplyAssignment" => "*=",
            "op_MultiplicationAssignment" => "*=",
            "op_DivisionAssignment" => "/=",
            "op_ModulusAssignment" => "%=",
            "op_BitwiseAndAssignment" => "&=",
            "op_BitwiseOrAssignment" => "|=",
            "op_ExclusiveOrAssignment" => "^=",
            "op_LeftShiftAssignment" => "<<=",
            "op_RightShiftAssignment" => ">>=",
            "op_UnsignedRightShiftAssignment" => ">>>=",
            "op_IncrementAssignment" => "++",
            "op_DecrementAssignment" => "--",
            _ => null,
        };
    }

    public static string GetAccessibilityText(PublicApiAccessibility accessibility)
    {
        return accessibility switch
        {
            PublicApiAccessibility.Public => "public",
            PublicApiAccessibility.Protected => "protected",
            PublicApiAccessibility.ProtectedInternal => "protected internal",
            PublicApiAccessibility.Internal => "internal",
            PublicApiAccessibility.PrivateProtected => "private protected",
            _ => "private",
        };
    }

    public static string? GetKeywordTypeName(PublicApiNamedTypeReference type)
    {
        if (type.ContainingType is not null || !type.TypeArguments.IsEmpty || type.Namespace is not "System")
            return null;

        return type.MetadataName switch
        {
            "Void" => "void",
            "Boolean" => "bool",
            "Char" => "char",
            "SByte" => "sbyte",
            "Byte" => "byte",
            "Int16" => "short",
            "UInt16" => "ushort",
            "Int32" => "int",
            "UInt32" => "uint",
            "Int64" => "long",
            "UInt64" => "ulong",
            "Single" => "float",
            "Double" => "double",
            "Decimal" => "decimal",
            "String" => "string",
            "Object" => "object",
            "IntPtr" => "nint",
            "UIntPtr" => "nuint",
            _ => null,
        };
    }

    // Returns the elements of a tuple, including the ones stored in the nested tuple of the last type argument of a tuple of more than 7 elements
    public static bool TryGetTupleElements(PublicApiNamedTypeReference type, out List<(PublicApiTypeReference Type, string? Name)> elements)
    {
        elements = [];
        var current = type;
        while (true)
        {
            if (!current.IsTupleType)
                return false;

            var arity = current.TypeArguments.Length;
            if (arity < 8)
            {
                for (var i = 0; i < arity; i++)
                {
                    elements.Add((current.TypeArguments[i], i < current.TupleElementNames.Length ? current.TupleElementNames[i] : null));
                }

                // A tuple has at least two elements
                return elements.Count >= 2;
            }

            for (var i = 0; i < 7; i++)
            {
                elements.Add((current.TypeArguments[i], i < current.TupleElementNames.Length ? current.TupleElementNames[i] : null));
            }

            if (current.TypeArguments[7] is not PublicApiNamedTypeReference rest)
                return false;

            current = rest;
        }
    }

    public static bool ContainsPointer(PublicApiTypeReference type)
    {
        return type switch
        {
            PublicApiPointerTypeReference or PublicApiFunctionPointerTypeReference => true,
            PublicApiArrayTypeReference array => ContainsPointer(array.ElementType),
            PublicApiNamedTypeReference named => named.GetAllTypeArguments().Any(ContainsPointer),
            _ => false,
        };
    }
}
