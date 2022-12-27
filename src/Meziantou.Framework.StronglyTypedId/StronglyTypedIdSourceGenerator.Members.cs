using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.StronglyTypedId;

public partial class StronglyTypedIdSourceGenerator
{
    private static void GenerateTypeMembers(CSharpGeneratedFileWriter writer, StronglyTypedIdInfo context)
    {
        var isFirstMember = true;
        void WriteNewMember()
        {
            StronglyTypedIdSourceGenerator.WriteNewMember(writer, context, addNewLine: !isFirstMember);
            isFirstMember = false;
        }

        var idType = context.AttributeInfo.IdType;
        var shortName = GetShortName(idType);

        // Field
        if (!context.IsFieldDefined())
        {
            WriteNewMember();
            writer.WriteLine($"private readonly {GetTypeReference(idType)} {FieldName};");
        }

        // Value
        if (!context.IsValueDefined())
        {
            WriteNewMember();
            writer.WriteLine($"public {GetTypeReference(idType)} {PropertyName} => {FieldName};");
        }

        // ValueAsString
        if (!context.IsValueAsStringDefined())
        {
            WriteNewMember();
            writer.WriteLine($"public string {PropertyAsStringName} => {ValueToStringExpression()};");

            string ValueToStringExpression()
            {
                if (idType == IdType.System_String)
                    return PropertyName;

                if (idType == IdType.System_Boolean || idType == IdType.System_Guid)
                    return $"{PropertyName}.ToString()";

                if (idType == IdType.System_DateTime)
                    return $"{PropertyName}.ToString(\"o\", global::System.Globalization.CultureInfo.InvariantCulture)";

                if (idType == IdType.System_DateTimeOffset)
                    return $"{PropertyName}.UtcDateTime.ToString(\"o\", global::System.Globalization.CultureInfo.InvariantCulture)";

                return $"{PropertyName}.ToString(global::System.Globalization.CultureInfo.InvariantCulture)";
            }
        }

        // ctor
        if (!context.IsCtorDefined())
        {
            WriteNewMember();
            using (writer.BeginBlock($"{GetPrivateOrProtectedModifier(context)} {context.Name}({GetTypeReference(idType)} value)"))
            {
                writer.WriteLine($"{FieldName} = value;");
            }
        }

        // From
        WriteNewMember();
        writer.WriteLine($"public static {context.Name} From{shortName}({GetTypeReference(idType)} value) => new {context.Name}(value);");

        // ToString
        if (!context.IsToStringDefined())
        {
            WriteNewMember();
            using (writer.BeginBlock("public override string ToString()"))
            {
                if (IsNullable(idType))
                {
                    using (writer.BeginBlock($"if ({PropertyName} == null)"))
                    {
                        writer.WriteLine($$"""return "{{context.Name}} { Value = " + {{PropertyAsStringName}} + " }";""");
                    }
                    using (writer.BeginBlock("else"))
                    {
                        writer.WriteLine($$"""return "{{context.Name}} { Value = <null> }";""");
                    }
                }
                else
                {
                    writer.WriteLine($$"""return "{{context.Name}} { Value = " + {{PropertyAsStringName}} + " }";""");
                }
            }
        }

        // if Guid => New
        if (idType == IdType.System_Guid)
        {
            WriteNewMember();
            writer.WriteLine($"public static {context.Name} New() => new {context.Name}(global::System.Guid.NewGuid());");
        }

        // GetHashCode
        if (!context.IsGetHashcodeDefined())
        {
            WriteNewMember();
            if (IsNullable(idType))
            {
                writer.WriteLine($"public override int GetHashCode() => {PropertyName} == null ? 0 : {PropertyName}.GetHashCode();");
            }
            else
            {
                writer.WriteLine($"public override int GetHashCode() => {PropertyName}.GetHashCode();");
            }
        }

        // IEquatable<T>
        if (!context.IsIEquatableEqualsDefined())
        {
            WriteNewMember();
            if (context.IsReferenceType)
            {
                writer.WriteLine($"public bool Equals({context.Name}? other) => other != null && {PropertyName} == other.{PropertyName};");
            }
            else
            {
                writer.WriteLine($"public bool Equals({context.Name} other) => {PropertyName} == other.{PropertyName};");
            }
        }

        // Equals
        if (!context.IsEqualsDefined())
        {
            WriteNewMember();
            writer.WriteLine($"public override bool Equals(object? other) => other is {context.Name} value && Equals(value);");
        }

        // Operator ==
        if (!context.IsOpEqualsDefined())
        {
            WriteNewMember();
            if (context.IsReferenceType)
            {
                writer.WriteLine($"public static bool operator ==({context.Name}? a, {context.Name}? b) => global::System.Collections.Generic.EqualityComparer<{context.Name}>.Default.Equals(a, b);");
            }
            else
            {
                writer.WriteLine($"public static bool operator ==({context.Name} a, {context.Name} b) => global::System.Collections.Generic.EqualityComparer<{context.Name}>.Default.Equals(a, b);");
            }
        }

        // Operator !=
        if (!context.IsOpNotEqualsDefined())
        {
            WriteNewMember();
            if (context.IsReferenceType)
            {
                writer.WriteLine($"public static bool operator !=({context.Name}? a, {context.Name}? b) => !(a == b);");
            }
            else
            {
                writer.WriteLine($"public static bool operator !=({context.Name} a, {context.Name} b) => !(a == b);");
            }
        }

        // Parse / TryParse
        if (context.SupportReadOnlySpan())
        {
            // TryParse(ReadOnlySpan<char>)
            if (!context.IsTryParseDefined_ReadOnlySpan())
            {
                GenerateTryParseMethod(writer, context, idType, isReadOnlySpan: true);
            }

            // Parse(ReadOnlySpan<char>)
            if (!context.IsParseDefined_Span())
            {
                WriteNewMember();
                using (writer.BeginBlock($"public static {context.Name} Parse(global::System.ReadOnlySpan<char> value)"))
                {
                    using (writer.BeginBlock($"if (TryParse(value, out var result))"))
                    {
                        writer.WriteLine($"return result;");
                    }

                    writer.WriteLine($"throw new global::System.FormatException($\"value '{{value.ToString()}}' is not valid\");");
                }
            }
        }

        // Parse
        WriteNewMember();
        using (writer.BeginBlock($"public static {context.Name} Parse(string value)"))
        {
            using (writer.BeginBlock($"if (TryParse(value, out var result))"))
            {
                writer.WriteLine($"return result;");
            }

            writer.WriteLine($"throw new global::System.FormatException($\"value '{{value}}' is not valid\");");
        }

        // TryParse
        if (!context.IsTryParseDefined_String())
        {
            GenerateTryParseMethod(writer, context, idType, isReadOnlySpan: false);
        }

        if (context.CompilationInfo.SupportStaticInterfaces)
        {
            // ISpanParsable
            if (context.CanImplementISpanParsable())
            {
                // TryParse
                {
                    var returnType = "out " + (context.IsReferenceType ? $"{context.Name}?" : context.Name);
                    if (context.CompilationInfo.NotNullWhenAttribute != null)
                    {
                        returnType = "[global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] " + returnType;
                    }

                    WriteNewMember();
                    using (writer.BeginBlock($"static bool System.ISpanParsable<{context.Name}>.TryParse(global::System.ReadOnlySpan<char> value, global::System.IFormatProvider? provider, {returnType} result)"))
                    {
                        writer.WriteLine("return TryParse(value, out result);");
                    }
                }

                // Parse
                {
                    WriteNewMember();
                    using (writer.BeginBlock($"static {context.Name} System.ISpanParsable<{context.Name}>.Parse(global::System.ReadOnlySpan<char> value, global::System.IFormatProvider? provider)"))
                    {
                        writer.WriteLine("return Parse(value);");
                    }
                }
            }

            // IParsable
            if (context.CanImplementIParsable())
            {
                // TryParse
                var returnType = "out " + (context.IsReferenceType ? $"{context.Name}?" : context.Name);
                if (context.CompilationInfo.NotNullWhenAttribute != null)
                {
                    returnType = "[global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] " + returnType;
                }

                WriteNewMember();
                using (writer.BeginBlock($"static bool System.IParsable<{context.Name}>.TryParse(string? value, global::System.IFormatProvider? provider, {returnType} result)"))
                {
                    writer.WriteLine("return TryParse(value, out result);");
                }

                // Parse
                WriteNewMember();
                using (writer.BeginBlock($"static {context.Name} System.IParsable<{context.Name}>.Parse(string value, global::System.IFormatProvider? provider)"))
                {
                    writer.WriteLine("return Parse(value);");
                }
            }
        }

        void GenerateTryParseMethod(CSharpGeneratedFileWriter writer, StronglyTypedIdInfo context, IdType idType, bool isReadOnlySpan)
        {
            var type = isReadOnlySpan ? "global::System.ReadOnlySpan<char>" : "string?";
            var returnType = "out " + (context.IsReferenceType ? $"{context.Name}?" : context.Name);

            if (context.CompilationInfo.NotNullWhenAttribute != null)
            {
                returnType = "[global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] " + returnType;
            }

            WriteNewMember();
            using (writer.BeginBlock($"public static bool TryParse({type} value, {returnType} result)"))
            {
                if (!isReadOnlySpan && context.SupportReadOnlySpan())
                {
                    using (writer.BeginBlock("if (value == null)"))
                    {
                        writer.WriteLine("result = default;");
                        writer.WriteLine("return false;");
                    }
                    using (writer.BeginBlock("else"))
                    {
                        writer.WriteLine("return TryParse(global::System.MemoryExtensions.AsSpan(value), out result);");
                    }
                }
                else
                {
                    if (idType == IdType.System_String)
                    {
                        if (isReadOnlySpan)
                        {
                            writer.WriteLine($"result = new {context.Name}(value.ToString());");
                            writer.WriteLine("return true;");
                        }
                        else
                        {
                            using (writer.BeginBlock($"if (value != null)"))
                            {
                                writer.WriteLine($"result = new {context.Name}(value);");
                                writer.WriteLine("return true;");
                            }

                            using (writer.BeginBlock("else"))
                            {
                                writer.WriteLine($"result = default;");
                                writer.WriteLine("return false;");
                            }
                        }
                    }
                    else
                    {
                        switch (idType)
                        {
                            case IdType.System_Boolean:
                                writer.WriteLine($"if (bool.TryParse(value, out var parsedValue))");
                                break;
                            case IdType.System_DateTime:
                                writer.WriteLine($"if (global::System.DateTime.TryParse(value, global::System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsedValue))");
                                break;

                            case IdType.System_DateTimeOffset:
                                writer.WriteLine($"if (global::System.DateTimeOffset.TryParse(value, global::System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsedValue))");
                                break;

                            case IdType.System_Guid:
                                writer.WriteLine($"if (global::System.Guid.TryParse(value, out var parsedValue))");
                                break;

                            case IdType.System_Half:
                            case IdType.System_Single:
                            case IdType.System_Double:
                            case IdType.System_Decimal:
                            case IdType.System_Byte:
                            case IdType.System_SByte:
                            case IdType.System_Int16:
                            case IdType.System_Int32:
                            case IdType.System_Int64:
                            case IdType.System_Int128:
                            case IdType.System_UInt16:
                            case IdType.System_UInt32:
                            case IdType.System_UInt64:
                            case IdType.System_UInt128:
                            case IdType.System_Numerics_BigInteger:
                                writer.WriteLine($"if ({GetTypeReference(idType)}.TryParse(value, global::System.Globalization.NumberStyles.Any, global::System.Globalization.CultureInfo.InvariantCulture, out var parsedValue))");
                                break;

                            default:
                                throw new InvalidOperationException("Type not supported");
                        }

                        using (writer.BeginBlock())
                        {
                            writer.WriteLine($"result = new {context.Name}(parsedValue);");
                            writer.WriteLine("return true;");
                        }

                        writer.WriteLine("result = default;");
                        writer.WriteLine("return false;");
                    }
                }
            }
        }
    }
}
