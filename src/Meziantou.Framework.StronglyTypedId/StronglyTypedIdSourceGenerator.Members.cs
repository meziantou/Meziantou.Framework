namespace Meziantou.Framework.StronglyTypedId;

public partial class StronglyTypedIdSourceGenerator
{
    private static void GenerateTypeMembers(CSharpGeneratedFileWriter writer, AttributeInfo context)
    {
        var isFirstMember = true;
        void WriteNewMember()
        {
            StronglyTypedIdSourceGenerator.WriteNewMember(writer, context, addNewLine: !isFirstMember);
            isFirstMember = false;
        }

        // Field
        if (!context.IsFieldDefined)
        {
            WriteNewMember();
            writer.WriteLine($"private readonly {context.ValueTypeCSharpTypeName} {FieldName};");
        }

        // Value
        if (!context.IsValueDefined)
        {
            WriteNewMember();
            writer.WriteLine($"public {context.ValueTypeCSharpTypeName} {PropertyName} => {FieldName};");
        }

        // ValueAsString
        if (!context.IsValueAsStringDefined)
        {
            WriteNewMember();
            writer.WriteLine($"public string {PropertyAsStringName} => {ValueToStringExpression()};");

            string ValueToStringExpression()
            {
                if (context.IdType is IdType.System_String)
                    return PropertyName;

                if (context.IdType is IdType.System_Boolean or IdType.System_Guid)
                    return $"{PropertyName}.ToString()";

                if (context.IdType is IdType.System_DateTime)
                    return $"{PropertyName}.ToString(\"o\", global::System.Globalization.CultureInfo.InvariantCulture)";

                if (context.IdType is IdType.System_DateTimeOffset)
                    return $"{PropertyName}.UtcDateTime.ToString(\"o\", global::System.Globalization.CultureInfo.InvariantCulture)";

                return $"{PropertyName}.ToString(global::System.Globalization.CultureInfo.InvariantCulture)";
            }
        }

        // ctor
        if (!context.IsCtorDefined)
        {
            WriteNewMember();
            using (writer.BeginBlock($"{GetPrivateOrProtectedModifier(context)} {context.TypeName}({context.ValueTypeCSharpTypeName} value)"))
            {
                writer.WriteLine($"{FieldName} = value;");
            }
        }

        // From
        WriteNewMember();
        writer.WriteLine($"public static {context.TypeName} From{context.ValueTypeShortName}({context.ValueTypeCSharpTypeName} value) => new {context.TypeName}(value);");

        // ToString
        if (!context.IsToStringDefined)
        {
            WriteNewMember();
            using (writer.BeginBlock("public override string ToString()"))
            {
                if (context.IsValueTypeNullable)
                {
                    using (writer.BeginBlock($"if ({PropertyName} == null)"))
                    {
                        writer.WriteLine($$"""return "{{context.TypeName}} { Value = <null> }";""");
                    }
                    using (writer.BeginBlock("else"))
                    {
                        writer.WriteLine($$"""return "{{context.TypeName}} { Value = " + {{PropertyAsStringName}} + " }";""");
                    }
                }
                else
                {
                    writer.WriteLine($$"""return "{{context.TypeName}} { Value = " + {{PropertyAsStringName}} + " }";""");
                }
            }
        }

        // if Guid => New
        if (context.IdType is IdType.System_Guid)
        {
            WriteNewMember();
            writer.WriteLine($"public static {context.TypeName} New() => new {context.TypeName}(global::System.Guid.NewGuid());");
        }

        // GetHashCode
        if (!context.IsGetHashcodeDefined)
        {
            WriteNewMember();
            if (context.IsValueTypeNullable)
            {
                writer.WriteLine($"public override int GetHashCode() => {PropertyName} == null ? 0 : {PropertyName}.GetHashCode();");
            }
            else
            {
                writer.WriteLine($"public override int GetHashCode() => {PropertyName}.GetHashCode();");
            }
        }

        // IEquatable<T>
        if (!context.IsIEquatableEqualsDefined)
        {
            WriteNewMember();
            if (context.IsReferenceType)
            {
                writer.WriteLine($"public bool Equals({context.TypeName}? other) => other != null && {PropertyName} == other.{PropertyName};");
            }
            else
            {
                writer.WriteLine($"public bool Equals({context.TypeName} other) => {PropertyName} == other.{PropertyName};");
            }
        }

        // Equals
        if (!context.IsEqualsDefined)
        {
            WriteNewMember();
            writer.WriteLine($"public override bool Equals(object? other) => other is {context.TypeName} value && Equals(value);");
        }

        // Operator ==
        if (!context.IsOpEqualsDefined)
        {
            WriteNewMember();
            writer.WriteLine($"public static bool operator ==({context.CSharpNullableTypeName} a, {context.CSharpNullableTypeName} b) => global::System.Collections.Generic.EqualityComparer<{context.TypeName}>.Default.Equals(a, b);");
        }

        // Operator !=
        if (!context.IsOpNotEqualsDefined)
        {
            WriteNewMember();
            writer.WriteLine($"public static bool operator !=({context.CSharpNullableTypeName} a, {context.CSharpNullableTypeName} b) => !(a == b);");
        }

        // Compare
        if (context.MustImplementComparable())
        {
            if (!context.ImplementsIComparable_CompareTo)
            {
                WriteNewMember();
                writer.WriteLine($"public int CompareTo(object? other) => other == null ? 1 : CompareTo(({context.TypeName})other);");
            }

            if (!context.ImplementsIComparableOfT_CompareTo)
            {
                WriteNewMember();
                if (context.IsReferenceType)
                {
                    writer.WriteLine($"public int CompareTo({context.TypeName}? other) => other == null ? 1 : global::System.Collections.Generic.Comparer<{context.ValueTypeCSharpTypeName}>.Default.Compare(Value, other.Value);");
                }
                else
                {
                    writer.WriteLine($"public int CompareTo({context.TypeName} other) => global::System.Collections.Generic.Comparer<{context.ValueTypeCSharpTypeName}>.Default.Compare(Value, other.Value);");
                }
            }

            if (!context.IsOpLessThanDefined)
            {
                WriteNewMember();
                writer.WriteLine($"public static bool operator <({context.CSharpNullableTypeName} left, {context.CSharpNullableTypeName} right) => global::System.Collections.Generic.Comparer<{context.TypeName}>.Default.Compare(left, right) < 0;");
            }

            if (!context.IsOpLessThanOrEqualDefined)
            {
                WriteNewMember();
                writer.WriteLine($"public static bool operator <=({context.CSharpNullableTypeName} left, {context.CSharpNullableTypeName} right) => global::System.Collections.Generic.Comparer<{context.TypeName}>.Default.Compare(left, right) <= 0;");
            }

            if (!context.IsOpGreaterThanDefined)
            {
                WriteNewMember();
                writer.WriteLine($"public static bool operator >({context.CSharpNullableTypeName} left, {context.CSharpNullableTypeName} right) => global::System.Collections.Generic.Comparer<{context.TypeName}>.Default.Compare(left, right) > 0;");
            }

            if (!context.IsOpGreaterThanOrEqualDefined)
            {
                WriteNewMember();
                writer.WriteLine($"public static bool operator >=({context.CSharpNullableTypeName} left, {context.CSharpNullableTypeName} right) => global::System.Collections.Generic.Comparer<{context.TypeName}>.Default.Compare(left, right) >= 0;");
            }
        }

        // Parse / TryParse
        if (context.SupportReadOnlySpanChar)
        {
            // TryParse(ReadOnlySpan<char>)
            if (!context.IsTryParseDefined_ReadOnlySpan)
            {
                GenerateTryParseMethod(writer, context, isReadOnlySpan: true);
            }

            // Parse(ReadOnlySpan<char>)
            if (!context.IsParseDefined_Span)
            {
                WriteNewMember();
                using (writer.BeginBlock($"public static {context.TypeName} Parse(global::System.ReadOnlySpan<char> value)"))
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
        using (writer.BeginBlock($"public static {context.TypeName} Parse(string value)"))
        {
            using (writer.BeginBlock($"if (TryParse(value, out var result))"))
            {
                writer.WriteLine($"return result;");
            }

            writer.WriteLine($"throw new global::System.FormatException($\"value '{{value}}' is not valid\");");
        }

        // TryParse
        if (!context.IsTryParseDefined_String)
        {
            GenerateTryParseMethod(writer, context, isReadOnlySpan: false);
        }

        if (context.SupportStaticInterfaces)
        {
            // ISpanParsable
            if (context.CanImplementISpanParsable())
            {
                // TryParse
                {
                    var returnType = "out " + (context.IsReferenceType ? $"{context.TypeName}?" : context.TypeName);
                    if (context.SupportNotNullWhenAttribute)
                    {
                        returnType = "[global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] " + returnType;
                    }

                    WriteNewMember();
                    using (writer.BeginBlock($"static bool System.ISpanParsable<{context.TypeName}>.TryParse(global::System.ReadOnlySpan<char> value, global::System.IFormatProvider? provider, {returnType} result)"))
                    {
                        writer.WriteLine("return TryParse(value, out result);");
                    }
                }

                // Parse
                {
                    WriteNewMember();
                    using (writer.BeginBlock($"static {context.TypeName} System.ISpanParsable<{context.TypeName}>.Parse(global::System.ReadOnlySpan<char> value, global::System.IFormatProvider? provider)"))
                    {
                        writer.WriteLine("return Parse(value);");
                    }
                }
            }

            // IParsable
            if (context.CanImplementIParsable())
            {
                // TryParse
                var returnType = "out " + (context.IsReferenceType ? $"{context.TypeName}?" : context.TypeName);
                if (context.SupportNotNullWhenAttribute)
                {
                    returnType = "[global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] " + returnType;
                }

                WriteNewMember();
                using (writer.BeginBlock($"static bool System.IParsable<{context.TypeName}>.TryParse(string? value, global::System.IFormatProvider? provider, {returnType} result)"))
                {
                    writer.WriteLine("return TryParse(value, out result);");
                }

                // Parse
                WriteNewMember();
                using (writer.BeginBlock($"static {context.TypeName}  System.IParsable< {context.TypeName}>.Parse(string value, global::System.IFormatProvider? provider)"))
                {
                    writer.WriteLine("return Parse(value);");
                }
            }
        }

        void GenerateTryParseMethod(CSharpGeneratedFileWriter writer, AttributeInfo context, bool isReadOnlySpan)
        {
            var type = isReadOnlySpan ? "global::System.ReadOnlySpan<char>" : "string?";
            var returnType = "out " + (context.IsReferenceType ? $"{context.TypeName}?" : context.TypeName);

            if (context.SupportNotNullWhenAttribute)
            {
                returnType = "[global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] " + returnType;
            }

            WriteNewMember();
            using (writer.BeginBlock($"public static bool TryParse({type} value, {returnType} result)"))
            {
                if (!isReadOnlySpan && context.SupportReadOnlySpanChar)
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
                    if (context.IdType is IdType.System_String)
                    {
                        if (isReadOnlySpan)
                        {
                            writer.WriteLine($"result = new {context.TypeName}(value.ToString());");
                            writer.WriteLine("return true;");
                        }
                        else
                        {
                            using (writer.BeginBlock($"if (value != null)"))
                            {
                                writer.WriteLine($"result = new {context.TypeName}(value);");
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
                        switch (context.IdType)
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
                                writer.WriteLine($"if ({context.ValueTypeCSharpTypeName}.TryParse(value, global::System.Globalization.NumberStyles.Any, global::System.Globalization.CultureInfo.InvariantCulture, out var parsedValue))");
                                break;

                            default:
                                throw new InvalidOperationException("Type not supported");
                        }

                        using (writer.BeginBlock())
                        {
                            writer.WriteLine($"result = new {context.TypeName}(parsedValue);");
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
