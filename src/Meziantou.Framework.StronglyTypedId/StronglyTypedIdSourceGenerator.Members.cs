using Microsoft.CodeAnalysis.CSharp;
using System.Xml.Linq;

namespace Meziantou.Framework.StronglyTypedId;

public partial class StronglyTypedIdSourceGenerator
{
    private static readonly XNode[] InheritDocComment = [new XElement("inheritdoc")];

    private static XElement XmlSeeCref(string type) => new("see", new XAttribute("cref", type));
    private static XElement XmlSummary(params object[] description) => new("summary", description);
    private static XElement XmlReturns(params object[] description) => new("returns", description);
    private static XElement XmlParam(string name, params object[] description) => new("param", new XAttribute("name", name), description);
    private static XElement XmlParamRef(string name) => new("paramref", new XAttribute("name", name));
    private static XElement XmlSeeLangword(string name) => new("see", new XAttribute("langword", name));

    private const string TryParseUtf8MethodName = "TryParseUtf8";
    private const string InvariantCultureExpression = "global::System.Globalization.CultureInfo.InvariantCulture";

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
                XmlReturns("The value contained in this instance."));
            writer.WriteLine($"public {context.ValueTypeCSharpTypeName} {PropertyName} => {FieldName};");
        }

        // ValueAsString
        if (!context.IsValueAsStringDefined)
        {
            WriteNewMember(
                XmlSummary("Get the value contained in this instance converted to string using the ", XmlSeeCref("global::System.Globalization.CultureInfo.InvariantCulture"), "."),
                XmlReturns("The value converted to string using the ", XmlSeeCref("global::System.Globalization.CultureInfo.InvariantCulture"), "."));
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

        // UnderlyingType
        if (context.SupportIStronglyTypedId_UnderlyingType)
        {
            WriteNewMember(
                XmlSummary("Gets the underlying type of the strongly typed ID."),
                XmlReturns("The underlying type of the strongly typed ID."));
            writer.WriteLine($"global::System.Type global::Meziantou.Framework.IStronglyTypedId.UnderlyingType => typeof({context.ValueTypeCSharpTypeName});");
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
            XmlReturns("A new instance of ", XmlSeeCref(context.TypeName), "."));
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
        if (context.IdType is IdType.System_Guid && !context.IsNewDefined && context.GuidGenerationStrategy is not GuidGenerationStrategy.None)
        {
            WriteNewMember(
                 XmlSummary("Initializes a new instance of the ", XmlSeeCref(context.TypeName), " using a new ", context.UseGuidVersion7 ? "version 7 " : "", XmlSeeCref("global::System.Guid"), "."),
                 XmlReturns("A new instance of ", XmlSeeCref(context.TypeName), "."));
            var factoryMethod = context.UseGuidVersion7 ? "CreateVersion7" : "NewGuid";
            writer.WriteLine($"public static {context.TypeName} New() => new {context.TypeName}(global::System.Guid.{factoryMethod}());");
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
                XmlReturns(XmlSeeLangword("true"), " if ", XmlParamRef("a"), " and ", XmlParamRef("b"), " are equal; otherwise, ", XmlSeeLangword("false"), "."));
            writer.WriteLine($"public static bool operator ==({context.CSharpNullableTypeName} a, {context.CSharpNullableTypeName} b) => global::System.Collections.Generic.EqualityComparer<{context.CSharpNullableTypeName}>.Default.Equals(a, b);");
        }

        // Operator !=
        if (!context.IsOpNotEqualsDefined)
        {
            WriteNewMember(
                 XmlSummary("Indicates whether the values of two specified ", XmlSeeCref(context.TypeName), " are not equal."),
                 XmlParam("a", "The first object to compare"),
                 XmlParam("b", "The second object to compare"),
                 XmlReturns(XmlSeeLangword("true"), " if ", XmlParamRef("a"), " and ", XmlParamRef("b"), " are not equal; otherwise, ", XmlSeeLangword("false"), "."));
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
                    XmlReturns(XmlSeeLangword("true"), " if ", XmlParamRef("left"), " is less than ", XmlParamRef("right"), "; otherwise, ", XmlSeeLangword("false"), "."));
                writer.WriteLine($"public static bool operator <({context.CSharpNullableTypeName} left, {context.CSharpNullableTypeName} right) => global::System.Collections.Generic.Comparer<{context.TypeName}>.Default.Compare(left, right) < 0;");
            }

            if (!context.IsOpLessThanOrEqualDefined)
            {
                WriteNewMember(
                    XmlSummary("Compares two value values to determine which is less or equal."),
                    XmlParam("left", "The value compare with ", XmlParamRef("right"), "."),
                    XmlParam("right", "The value to compare with ", XmlParamRef("left"), "."),
                    XmlReturns(XmlSeeLangword("true"), " if ", XmlParamRef("left"), " is less than or equal to ", XmlParamRef("right"), "; otherwise, ", XmlSeeLangword("false"), "."));
                writer.WriteLine($"public static bool operator <=({context.CSharpNullableTypeName} left, {context.CSharpNullableTypeName} right) => global::System.Collections.Generic.Comparer<{context.TypeName}>.Default.Compare(left, right) <= 0;");
            }

            if (!context.IsOpGreaterThanDefined)
            {
                WriteNewMember(
                    XmlSummary("Compares two value values to determine which is greater."),
                    XmlParam("left", "The value compare with ", XmlParamRef("right"), "."),
                    XmlParam("right", "The value to compare with ", XmlParamRef("left"), "."),
                    XmlReturns(XmlSeeLangword("true"), " if ", XmlParamRef("left"), " is greater than or equal to ", XmlParamRef("right"), "; otherwise, ", XmlSeeLangword("false"), "."));
                writer.WriteLine($"public static bool operator >({context.CSharpNullableTypeName} left, {context.CSharpNullableTypeName} right) => global::System.Collections.Generic.Comparer<{context.TypeName}>.Default.Compare(left, right) > 0;");
            }

            if (!context.IsOpGreaterThanOrEqualDefined)
            {
                WriteNewMember(
                    XmlSummary("Compares two value values to determine which is greater or equal."),
                    XmlParam("left", "The value compare with ", XmlParamRef("right"), "."),
                    XmlParam("right", "The value to compare with ", XmlParamRef("left"), "."),
                    XmlReturns(XmlSeeLangword("true"), " if ", XmlParamRef("left"), " is greater than ", XmlParamRef("right"), "; otherwise, ", XmlSeeLangword("false"), "."));
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
                    XmlReturns("A new instance that contains the value that was parsed"));
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
        if (!context.IsParseDefined_String)
        {
            WriteNewMember(
                XmlSummary("Converts the string representation of a ", XmlSeeCref(context.TypeName), " to the equivalent ", XmlSeeCref(context.TypeName), " type."),
                XmlParam("value", "The string to convert."),
                XmlReturns("A new instance that contains the value that was parsed"));
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
        }

        // TryParse
        if (!context.IsTryParseDefined_String)
        {
            GenerateTryParseMethod(writer, context, isReadOnlySpan: false);
        }

        // TryParse (UTF-8)
        // Only the IUtf8SpanParsable members are public. A public Parse(ReadOnlySpan<byte>) overload would make Parse(null) ambiguous.
        if (context.CanImplementIUtf8SpanParsable() && !context.IsTryParseDefined_Utf8)
        {
            var returnType = "out " + (context.IsReferenceType ? $"{context.TypeName}?" : context.TypeName);
            if (context.SupportNotNullWhenAttribute)
            {
                returnType = "[global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] " + returnType;
            }

            WriteNewMember(
                XmlSummary("Tries to parse the UTF-8 representation of a ", XmlSeeCref(context.TypeName), " to the equivalent ", XmlSeeCref(context.TypeName), " type."),
                XmlParam("utf8Text", "The UTF-8 text to convert."),
                XmlParam("result", "When this method returns, contains the result of successfully parsing utf8Text or an undefined value on failure."),
                XmlReturns(XmlSeeLangword("true"), " if ", XmlParamRef("utf8Text"), " was converted successfully; otherwise, ", XmlSeeLangword("false"), "."));
            using (writer.BeginBlock($"private static bool {TryParseUtf8MethodName}(global::System.ReadOnlySpan<byte> utf8Text, {returnType} result)"))
            {
                // Parsing the UTF-16 representation ensures both representations accept the same values
                writer.WriteLine("char[]? rentedBuffer = null;");
                using (writer.BeginBlock("try"))
                {
                    // A UTF-8 sequence never decodes to more UTF-16 code units than it has bytes
                    writer.WriteLine("global::System.Span<char> buffer = utf8Text.Length <= 256 ? stackalloc char[256] : (rentedBuffer = global::System.Buffers.ArrayPool<char>.Shared.Rent(utf8Text.Length));");
                    using (writer.BeginBlock("if (global::System.Text.Unicode.Utf8.ToUtf16(utf8Text, buffer, out _, out var charsWritten, replaceInvalidSequences: false) == global::System.Buffers.OperationStatus.Done)"))
                    {
                        if (context.ValueTypeHasParseReadOnlySpan)
                        {
                            writer.WriteLine("return TryParse((global::System.ReadOnlySpan<char>)buffer.Slice(0, charsWritten), out result);");
                        }
                        else
                        {
                            writer.WriteLine("return TryParse(buffer.Slice(0, charsWritten).ToString(), out result);");
                        }
                    }
                }

                using (writer.BeginBlock("finally"))
                {
                    using (writer.BeginBlock("if (rentedBuffer != null)"))
                    {
                        writer.WriteLine("global::System.Buffers.ArrayPool<char>.Shared.Return(rentedBuffer);");
                    }
                }

                writer.WriteLine("result = default;");
                writer.WriteLine("return false;");
            }
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
                    using (writer.BeginBlock($"static bool global::System.ISpanParsable<{context.TypeName}>.TryParse(global::System.ReadOnlySpan<char> value, global::System.IFormatProvider? provider, {returnType} result)"))
                    {
                        writer.WriteLine("return TryParse(value, out result);");
                    }
                }

                // Parse
                {
                    WriteNewMember(InheritDocComment);
                    using (writer.BeginBlock($"static {context.TypeName} global::System.ISpanParsable<{context.TypeName}>.Parse(global::System.ReadOnlySpan<char> value, global::System.IFormatProvider? provider)"))
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
                using (writer.BeginBlock($"static bool global::System.IParsable<{context.TypeName}>.TryParse(string? value, global::System.IFormatProvider? provider, {returnType} result)"))
                {
                    writer.WriteLine("return TryParse(value, out result);");
                }

                // Parse
                WriteNewMember(InheritDocComment);
                using (writer.BeginBlock($"static {context.TypeName} global::System.IParsable<{context.TypeName}>.Parse(string value, global::System.IFormatProvider? provider)"))
                {
                    writer.WriteLine("return Parse(value);");
                }
            }

            // IUtf8SpanParsable
            if (context.CanImplementIUtf8SpanParsable())
            {
                // TryParse
                var returnType = "out " + (context.IsReferenceType ? $"{context.TypeName}?" : context.TypeName);
                if (context.SupportNotNullWhenAttribute)
                {
                    returnType = "[global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] " + returnType;
                }

                var tryParseMethodName = context.IsTryParseDefined_Utf8 ? "TryParse" : TryParseUtf8MethodName;
                WriteNewMember(InheritDocComment);
                using (writer.BeginBlock($"static bool global::System.IUtf8SpanParsable<{context.TypeName}>.TryParse(global::System.ReadOnlySpan<byte> utf8Text, global::System.IFormatProvider? provider, {returnType} result)"))
                {
                    writer.WriteLine($"return {tryParseMethodName}(utf8Text, out result);");
                }

                // Parse
                WriteNewMember(InheritDocComment);
                using (writer.BeginBlock($"static {context.TypeName} global::System.IUtf8SpanParsable<{context.TypeName}>.Parse(global::System.ReadOnlySpan<byte> utf8Text, global::System.IFormatProvider? provider)"))
                {
                    if (context.IsParseDefined_Utf8)
                    {
                        writer.WriteLine("return Parse(utf8Text);");
                    }
                    else
                    {
                        using (writer.BeginBlock($"if ({tryParseMethodName}(utf8Text, out var result))"))
                        {
                            if (!context.SupportNotNullWhenAttribute)
                            {
                                writer.WriteLine("#nullable disable");
                            }

                            writer.WriteLine("return result;");

                            if (!context.SupportNotNullWhenAttribute)
                            {
                                writer.WriteLine("#nullable enable");
                            }
                        }

                        writer.WriteLine("throw new global::System.FormatException($\"value '{(global::System.Text.Encoding.UTF8.GetString(utf8Text))}' is not valid\");");
                    }
                }
            }
        }

        // IFormattable, ISpanFormattable, IUtf8SpanFormattable
        // Formatting is culture-invariant, as is parsing, so the members are explicit implementations and the provider is ignored.
        // Public overloads with an IFormatProvider parameter would be misleading and would make ToString() calls report MA0011.
        if (context.CanImplementIFormattable() && !context.IsToStringFormatDefined)
        {
            WriteNewMember(InheritDocComment);
            using (writer.BeginBlock("string global::System.IFormattable.ToString(string? format, global::System.IFormatProvider? formatProvider)"))
            {
                using (writer.BeginBlock("if (string.IsNullOrEmpty(format))"))
                {
                    writer.WriteLine(context.IsToStringDefined ? "return ToString() ?? \"\";" : "return ToString();");
                }

                writer.WriteLine($"return {GetFormattedValueStringExpression(context, "format")};");
            }
        }

        if (context.CanImplementISpanFormattable() && !context.IsTryFormatDefined_Char)
        {
            WriteNewMember(InheritDocComment);
            GenerateTryFormatMethod(writer, context, utf8: false);
        }

        if (context.CanImplementIUtf8SpanFormattable() && !context.IsTryFormatDefined_Utf8)
        {
            WriteNewMember(InheritDocComment);
            GenerateTryFormatMethod(writer, context, utf8: true);
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
                XmlReturns("A new instance that contains the value that was parsed"));
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
                            // The constructor may be defined by the user and may throw when the value is not valid
                            using (writer.BeginBlock("try"))
                            {
                                writer.WriteLine($"result = new {context.TypeName}(value.ToString());");
                                writer.WriteLine("return true;");
                            }

                            using (writer.BeginBlock("catch"))
                            {
                            }

                            writer.WriteLine("result = default;");
                            writer.WriteLine("return false;");
                        }
                        else
                        {
                            using (writer.BeginBlock($"if (value != null)"))
                            {
                                using (writer.BeginBlock("try"))
                                {
                                    writer.WriteLine($"result = new {context.TypeName}(value);");
                                    writer.WriteLine("return true;");
                                }

                                using (writer.BeginBlock("catch"))
                                {
                                }
                            }

                            writer.WriteLine($"result = default;");
                            writer.WriteLine("return false;");
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
                                writer.WriteLine($"if (global::MongoDB.Bson.ObjectId.TryParse(value, out var parsedValue))");
                                break;

                            default:
                                throw new InvalidOperationException($"Type '{context.IdType}' not supported");
                        }

                        using (writer.BeginBlock())
                        {
                            using (writer.BeginBlock("try"))
                            {
                                writer.WriteLine($"result = new {context.TypeName}(parsedValue);");
                                writer.WriteLine("return true;");
                            }

                            using (writer.BeginBlock("catch"))
                            {
                            }
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

    /// <summary>
    /// Gets the expression that formats <see cref="PropertyName"/> using the format and the invariant culture. The format is ignored when the underlying type is not formattable.
    /// </summary>
    private static string GetFormattedValueStringExpression(AttributeInfo context, string formatExpression)
    {
        if (context.IdType is IdType.System_String)
            return $"{PropertyAsStringName} ?? \"\"";

        if (context.ValueTypeHasToStringFormat)
            return $"{PropertyName}.ToString({formatExpression}, {InvariantCultureExpression})";

        return PropertyAsStringName;
    }

    /// <summary>
    /// Generates <c>TryFormat</c> for <c>ISpanFormattable</c> or <c>IUtf8SpanFormattable</c>.
    /// Without a format, the result is the same as <c>ToString()</c>. Otherwise, the format is applied to the value using the invariant culture.
    /// </summary>
    private static void GenerateTryFormatMethod(CSharpGeneratedFileWriter writer, AttributeInfo context, bool utf8)
    {
        var destination = utf8 ? "utf8Destination" : "destination";
        var written = utf8 ? "bytesWritten" : "charsWritten";
        var valueTryFormatSignature = utf8 ? context.ValueTypeTryFormat_Utf8 : context.ValueTypeTryFormat_Char;
        var canUseValueTryFormat = context.IdType is not IdType.System_String && valueTryFormatSignature is not TryFormatSignature.None;

        using (writer.BeginBlock($"bool global::System.{(utf8 ? "IUtf8SpanFormattable" : "ISpanFormattable")}.TryFormat(global::System.Span<{(utf8 ? "byte" : "char")}> {destination}, out int {written}, global::System.ReadOnlySpan<char> format, global::System.IFormatProvider? provider)"))
        {
            writer.WriteLine($"{written} = 0;");
            writer.WriteLine("var length = 0;");
            using (writer.BeginBlock("if (format.IsEmpty)"))
            {
                WriteSteps(GetDefaultSteps());
            }

            using (writer.BeginBlock("else"))
            {
                if (canUseValueTryFormat)
                {
                    WriteSteps([FormatStep.TryFormat(PropertyName, "format", valueTryFormatSignature is TryFormatSignature.FormatAndProvider ? InvariantCultureExpression : null)]);
                }
                else
                {
                    WriteSteps([FormatStep.Text(GetFormattedValueStringExpression(context, "format.ToString()"))]);
                }
            }

            writer.WriteLine($"{written} = length;");
            writer.WriteLine("return true;");
        }

        // Writes the same value as the generated ToString() method, without allocating when possible
        FormatStep[] GetDefaultSteps()
        {
            if (context.IsToStringDefined)
                return [FormatStep.Text("ToString()")];

            FormatStep valueStep;
            if (context.IdType is IdType.System_String)
            {
                valueStep = FormatStep.Text(context.GenerateToStringAsRecord ? $"{PropertyName} == null ? \"<null>\" : {PropertyAsStringName}" : PropertyAsStringName);
            }
            else if (canUseValueTryFormat && !context.IsValueAsStringDefined)
            {
                // Must match the ValueAsString property
                var target = context.IdType is IdType.System_DateTimeOffset ? $"{PropertyName}.UtcDateTime" : PropertyName;
                var format = context.IdType is IdType.System_DateTime or IdType.System_DateTimeOffset ? "\"o\"" : "default";
                valueStep = FormatStep.TryFormat(target, format, valueTryFormatSignature is TryFormatSignature.FormatAndProvider ? InvariantCultureExpression : null);
            }
            else
            {
                valueStep = FormatStep.Text(PropertyAsStringName);
            }

            if (!context.GenerateToStringAsRecord)
                return [valueStep];

            return [FormatStep.Literal(context.TypeName + " { Value = "), valueStep, FormatStep.Literal(" }")];
        }

        void WriteSteps(FormatStep[] steps)
        {
            for (var i = 0; i < steps.Length; i++)
            {
                var step = steps[i];
                if (step.Kind is FormatStepKind.TryFormat)
                {
                    var provider = step.Provider is null ? "" : ", " + step.Provider;
                    WriteReturnFalseIf($"!{step.Expression}.TryFormat({destination}.Slice(length), out var written{i}, {step.Format}{provider})");
                    writer.WriteLine($"length += written{i};");
                }
                else if (utf8)
                {
                    WriteReturnFalseIf($"global::System.Text.Unicode.Utf8.FromUtf16({step.Expression}, {destination}.Slice(length), out _, out var written{i}) != global::System.Buffers.OperationStatus.Done");
                    writer.WriteLine($"length += written{i};");
                }
                else if (step.Kind is FormatStepKind.Literal)
                {
                    WriteReturnFalseIf($"!global::System.MemoryExtensions.AsSpan({step.Expression}).TryCopyTo({destination}.Slice(length))");
                    writer.WriteLine($"length += {step.LiteralLength};");
                }
                else
                {
                    writer.WriteLine($"var text{i} = global::System.MemoryExtensions.AsSpan({step.Expression});");
                    WriteReturnFalseIf($"!text{i}.TryCopyTo({destination}.Slice(length))");
                    writer.WriteLine($"length += text{i}.Length;");
                }
            }
        }

        void WriteReturnFalseIf(string condition)
        {
            using (writer.BeginBlock($"if ({condition})"))
            {
                writer.WriteLine("return false;");
            }
        }
    }

    private enum FormatStepKind
    {
        Literal,
        Text,
        TryFormat,
    }

    /// <summary>A part of the formatted value written by the generated <c>TryFormat</c> method.</summary>
    private sealed record FormatStep(FormatStepKind Kind, string Expression, int LiteralLength = 0, string? Format = null, string? Provider = null)
    {
        public static FormatStep Literal(string value) => new(FormatStepKind.Literal, SymbolDisplay.FormatLiteral(value, quote: true), LiteralLength: value.Length);

        /// <summary>Writes the value of an expression of type <see cref="string"/>. A <see langword="null"/> value writes nothing.</summary>
        public static FormatStep Text(string expression) => new(FormatStepKind.Text, expression);

        /// <summary>Calls the <c>TryFormat</c> method of the expression.</summary>
        public static FormatStep TryFormat(string expression, string format, string? provider) => new(FormatStepKind.TryFormat, expression, Format: format, Provider: provider);
    }
}
