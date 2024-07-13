using System.Xml.Linq;

namespace Meziantou.Framework.StronglyTypedId;

public partial class StronglyTypedIdSourceGenerator
{
    private static readonly XNode[] InheritDocComment = [new XElement("inheritdoc")];

    private static XElement XmlSeeCref(string type) => new("see", new XAttribute("cref", type));
    private static XElement XmlSummary(params object[] description) => new("summary", description);
    private static XElement XmlReturn(params object[] description) => new("return", description);
    private static XElement XmlParam(string name, params object[] description) => new("param", new XAttribute("name", name), description);
    private static XElement XmlParamRef(string name) => new("paramref", new XAttribute("name", name));
    private static XElement XmlSeeLangword(string name) => new("see", new XAttribute("langword", name));

    private static void GenerateTypeMembers(CSharpGeneratedFileWriter writer, AttributeInfo context)
    {
        var isFirstMember = true;
        void WriteNewMember(params XNode[]? xmlDocumentation)
        {
            StronglyTypedIdSourceGenerator.WriteNewMember(writer, context, addNewLine: !isFirstMember, xmlDocumentation);
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
            WriteNewMember(
                XmlSummary("Get the value contained in this instance."),
                XmlReturn("The value contained in this instance."));
            writer.WriteLine($"public {context.ValueTypeCSharpTypeName} {PropertyName} => {FieldName};");
        }

        // ValueAsString
        if (!context.IsValueAsStringDefined)
        {
            WriteNewMember(
                XmlSummary("Get the value contained in this instance converted to string using the ", new XElement("see", XmlSeeCref("global::System.Globalization.CultureInfo.InvariantCulture")), "."),
                XmlReturn("The value converted to string using the ", new XElement("see", XmlSeeCref("global::System.Globalization.CultureInfo.InvariantCulture")), "."));
            writer.WriteLine($"public string {PropertyAsStringName} => {ValueToStringExpression()};");

            string ValueToStringExpression()
            {
                if (context.IdType is IdType.System_String)
                    return PropertyName;

                if (context.IdType is IdType.System_Boolean or IdType.System_Guid or IdType.MongoDB_Bson_ObjectId)
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
            WriteNewMember(
                XmlSummary("Initializes a new instance of the ", XmlSeeCref(context.TypeName), " using the specified value."),
                XmlParam("value", "The value to create the instance."));
            using (writer.BeginBlock($"{GetPrivateOrProtectedModifier(context)} {context.TypeName}({context.ValueTypeCSharpTypeName} value)"))
            {
                writer.WriteLine($"{FieldName} = value;");
            }
        }

        // From
        WriteNewMember(
            XmlSummary("Initializes a new instance of the ", XmlSeeCref(context.TypeName), " from an ", XmlSeeCref(context.ValueTypeCSharpTypeName), " value."),
            XmlParam("value", "The value to create the instance."),
            XmlReturn("A new instance of ", XmlSeeCref(context.TypeName), "."));
        writer.WriteLine($"public static {context.TypeName} From{context.ValueTypeShortName}({context.ValueTypeCSharpTypeName} value) => new {context.TypeName}(value);");

        // ToString
        if (!context.IsToStringDefined)
        {
            WriteNewMember(InheritDocComment);
            using (writer.BeginBlock("public override string ToString()"))
            {
                if (context.GenerateToStringAsRecord)
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
                else
                {
                    writer.WriteLine($"return {PropertyAsStringName} ?? \"\";");
                }
            }
        }

        // if Guid => New
        if (context.IdType is IdType.System_Guid)
        {
            WriteNewMember(
                 XmlSummary("Initializes a new instance of the ", XmlSeeCref(context.TypeName), " using a new ", XmlSeeCref("global::System.Guid"), "."),
                 XmlReturn("A new instance of ", XmlSeeCref(context.TypeName), "."));
            writer.WriteLine($"public static {context.TypeName} New() => new {context.TypeName}(global::System.Guid.NewGuid());");
        }

        // GetHashCode
        if (!context.IsGetHashcodeDefined)
        {
            WriteNewMember(InheritDocComment);
            if (context.StringComparison != StringComparison.Ordinal && context.IdType == IdType.System_String)
            {
                writer.WriteLine($"public override int GetHashCode() => {PropertyName} == null ? 0 : {GetStringComparer(context.StringComparison)}.GetHashCode({PropertyName});");
            }
            else if (context.IsValueTypeNullable)
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
            WriteNewMember(InheritDocComment);
            if (context is { StringComparison: not StringComparison.Ordinal, IdType: IdType.System_String })
            {
                if (context.IsReferenceType)
                {
                    writer.WriteLine($"public bool Equals({context.TypeName}? other) => other != null && {GetStringComparer(context.StringComparison)}.Equals({PropertyName}, other.{PropertyName});");
                }
                else
                {
                    writer.WriteLine($"public bool Equals({context.TypeName} other) => {GetStringComparer(context.StringComparison)}.Equals({PropertyName}, other.{PropertyName});");
                }
            }
            else if (context.IsReferenceType)
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
            WriteNewMember(InheritDocComment);
            writer.WriteLine($"public override bool Equals(object? other) => other is {context.TypeName} value && Equals(value);");
        }

        // Operator ==
        if (!context.IsOpEqualsDefined)
        {
            WriteNewMember(
                XmlSummary("Indicates whether the values of two specified ", XmlSeeCref(context.TypeName), " are equal."),
                XmlParam("a", "The first object to compare"),
                XmlParam("b", "The second object to compare"),
                XmlReturn(XmlSeeLangword("true"), " if ", XmlParamRef("a"), " and ", XmlParamRef("b"), " are equal; otherwise, ", XmlSeeLangword("false"), "."));
            writer.WriteLine($"public static bool operator ==({context.CSharpNullableTypeName} a, {context.CSharpNullableTypeName} b) => global::System.Collections.Generic.EqualityComparer<{context.CSharpNullableTypeName}>.Default.Equals(a, b);");
        }

        // Operator !=
        if (!context.IsOpNotEqualsDefined)
        {
            WriteNewMember(
                 XmlSummary("Indicates whether the values of two specified ", XmlSeeCref(context.TypeName), " are not equal."),
                 XmlParam("a", "The first object to compare"),
                 XmlParam("b", "The second object to compare"),
                 XmlReturn(XmlSeeLangword("true"), " if ", XmlParamRef("a"), " and ", XmlParamRef("b"), " are not equal; otherwise, ", XmlSeeLangword("false"), "."));
            writer.WriteLine($"public static bool operator !=({context.CSharpNullableTypeName} a, {context.CSharpNullableTypeName} b) => !(a == b);");
        }

        // Compare
        if (context.MustImplementComparable())
        {
            if (!context.ImplementsIComparable_CompareTo)
            {
                WriteNewMember(InheritDocComment);
                writer.WriteLine($"public int CompareTo(object? other) => other == null ? 1 : CompareTo(({context.TypeName})other);");
            }

            if (!context.ImplementsIComparableOfT_CompareTo)
            {
                WriteNewMember(InheritDocComment);
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
                WriteNewMember(
                    XmlSummary("Compares two value values to determine which is less."),
                    XmlParam("left", "The value compare with ", XmlParamRef("right"), "."),
                    XmlParam("right", "The value to compare with ", XmlParamRef("left"), "."),
                    XmlReturn(XmlSeeLangword("true"), " if ", XmlParamRef("left"), " is less than ", XmlParamRef("right"), "; otherwise, ", XmlSeeLangword("false"), "."));
                writer.WriteLine($"public static bool operator <({context.CSharpNullableTypeName} left, {context.CSharpNullableTypeName} right) => global::System.Collections.Generic.Comparer<{context.TypeName}>.Default.Compare(left, right) < 0;");
            }

            if (!context.IsOpLessThanOrEqualDefined)
            {
                WriteNewMember(
                    XmlSummary("Compares two value values to determine which is less or equal."),
                    XmlParam("left", "The value compare with ", XmlParamRef("right"), "."),
                    XmlParam("right", "The value to compare with ", XmlParamRef("left"), "."),
                    XmlReturn(XmlSeeLangword("true"), " if ", XmlParamRef("left"), " is less than or equal to ", XmlParamRef("right"), "; otherwise, ", XmlSeeLangword("false"), "."));
                writer.WriteLine($"public static bool operator <=({context.CSharpNullableTypeName} left, {context.CSharpNullableTypeName} right) => global::System.Collections.Generic.Comparer<{context.TypeName}>.Default.Compare(left, right) <= 0;");
            }

            if (!context.IsOpGreaterThanDefined)
            {
                WriteNewMember(
                    XmlSummary("Compares two value values to determine which is greater."),
                    XmlParam("left", "The value compare with ", XmlParamRef("right"), "."),
                    XmlParam("right", "The value to compare with ", XmlParamRef("left"), "."),
                    XmlReturn(XmlSeeLangword("true"), " if ", XmlParamRef("left"), " is greater than or equal to ", XmlParamRef("right"), "; otherwise, ", XmlSeeLangword("false"), "."));
                writer.WriteLine($"public static bool operator >({context.CSharpNullableTypeName} left, {context.CSharpNullableTypeName} right) => global::System.Collections.Generic.Comparer<{context.TypeName}>.Default.Compare(left, right) > 0;");
            }

            if (!context.IsOpGreaterThanOrEqualDefined)
            {
                WriteNewMember(
                    XmlSummary("Compares two value values to determine which is greater or equal."),
                    XmlParam("left", "The value compare with ", XmlParamRef("right"), "."),
                    XmlParam("right", "The value to compare with ", XmlParamRef("left"), "."),
                    XmlReturn(XmlSeeLangword("true"), " if ", XmlParamRef("left"), " is greater than ", XmlParamRef("right"), "; otherwise, ", XmlSeeLangword("false"), "."));
                writer.WriteLine($"public static bool operator >=({context.CSharpNullableTypeName} left, {context.CSharpNullableTypeName} right) => global::System.Collections.Generic.Comparer<{context.TypeName}>.Default.Compare(left, right) >= 0;");
            }
        }

        // Parse / TryParse
        if (context.SupportReadOnlySpanChar && context.ValueTypeHasParseReadOnlySpan)
        {
            // TryParse(ReadOnlySpan<char>)
            if (!context.IsTryParseDefined_ReadOnlySpan)
            {
                GenerateTryParseMethod(writer, context, isReadOnlySpan: true);
            }

            // Parse(ReadOnlySpan<char>)
            if (!context.IsParseDefined_ReadOnlySpan)
            {
                WriteNewMember(
                    XmlSummary("Converts the read-only character span that represents of a ", XmlSeeCref(context.TypeName), " to the equivalent ", XmlSeeCref(context.TypeName), " type."),
                    XmlParam("value", "The read-only character span to convert."),
                    XmlReturn("A new instance that contains the value that was parsed"));
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
        WriteNewMember(
            XmlSummary("Converts the string representation of a ", XmlSeeCref(context.TypeName), " to the equivalent ", XmlSeeCref(context.TypeName), " type."),
            XmlParam("value", "The string to convert."),
            XmlReturn("A new instance that contains the value that was parsed"));
        using (writer.BeginBlock($"public static {context.TypeName} Parse(string value)"))
        {
            using (writer.BeginBlock($"if (TryParse(value, out var result))"))
            {
                if (!context.SupportNotNullWhenAttribute)
                {
                    writer.WriteLine($"#nullable disable");
                }

                writer.WriteLine($"return result;");

                if (!context.SupportNotNullWhenAttribute)
                {
                    writer.WriteLine($"#nullable enable");
                }
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

                    WriteNewMember(InheritDocComment);
                    using (writer.BeginBlock($"static bool System.ISpanParsable<{context.TypeName}>.TryParse(global::System.ReadOnlySpan<char> value, global::System.IFormatProvider? provider, {returnType} result)"))
                    {
                        writer.WriteLine("return TryParse(value, out result);");
                    }
                }

                // Parse
                {
                    WriteNewMember(InheritDocComment);
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

                WriteNewMember(InheritDocComment);
                using (writer.BeginBlock($"static bool System.IParsable<{context.TypeName}>.TryParse(string? value, global::System.IFormatProvider? provider, {returnType} result)"))
                {
                    writer.WriteLine("return TryParse(value, out result);");
                }

                // Parse
                WriteNewMember(InheritDocComment);
                using (writer.BeginBlock($"static {context.TypeName} System.IParsable< {context.TypeName}>.Parse(string value, global::System.IFormatProvider? provider)"))
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

            WriteNewMember(
                XmlSummary($"Tries to parse the {(isReadOnlySpan ? "read-only character span" : "string")} representation of a ", XmlSeeCref(context.TypeName), " to the equivalent ", XmlSeeCref(context.TypeName), " type."),
                XmlParam("value", $"The {(isReadOnlySpan ? "read-only character span" : "string")} to convert."),
                XmlParam("result", "When this method returns, contains the result of successfully parsing s or an undefined value on failure."),
                XmlReturn("A new instance that contains the value that was parsed"));
            using (writer.BeginBlock($"public static bool TryParse({type} value, {returnType} result)"))
            {
                if (!isReadOnlySpan && context.ValueTypeHasParseReadOnlySpan)
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

                            case IdType.MongoDB_Bson_ObjectId:
                                writer.WriteLine($"if (MongoDB.Bson.ObjectId.TryParse(value, out var parsedValue))");
                                break;

                            default:
                                throw new InvalidOperationException($"Type '{context.IdType}' not supported");
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

        [SuppressMessage("ApiDesign", "RS0030:Do not use banned APIs")]
        static string GetStringComparer(StringComparison stringComparison)
        {
            return stringComparison switch
            {
                StringComparison.CurrentCulture => "global::System.StringComparer.CurrentCulture",
                StringComparison.CurrentCultureIgnoreCase => "global::System.StringComparer.CurrentCultureIgnoreCase",
                StringComparison.InvariantCulture => "global::System.StringComparer.InvariantCulture",
                StringComparison.InvariantCultureIgnoreCase => "global::System.StringComparer.InvariantCultureIgnoreCase",
                StringComparison.Ordinal => "global::System.StringComparer.Ordinal",
                StringComparison.OrdinalIgnoreCase => "global::System.StringComparer.OrdinalIgnoreCase",
                _ => throw new ArgumentOutOfRangeException(nameof(stringComparison)),
            };
        }
    }
}
