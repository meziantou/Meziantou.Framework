namespace Meziantou.Framework.StronglyTypedId;

public partial class StronglyTypedIdSourceGenerator
{
    private const string TomlNamespace = "global::Meziantou.Framework.Toml";
    private const string TomlSerializationNamespace = "global::Meziantou.Framework.Toml.Serialization";

    private static void GenerateTomlConverter(CSharpGeneratedFileWriter writer, AttributeInfo context)
    {
        if (!context.CanGenerateTomlConverter())
            return;

        var idType = context.IdType;
        var valueType = context.ValueTypeCSharpTypeName;

        WriteNewMember(writer, context, addNewLine: true, [XmlSummary("Converts ", XmlSeeCref(context.TypeName), " to and from TOML with Meziantou.Framework.Toml.")]);
        using (writer.BeginBlock($"{context.GetConverterModifiers(StronglyTypedIdConverters.Meziantou_Framework_Toml)}partial class {context.TomlConverterTypeName} : {TomlSerializationNamespace}.TomlConverter<{context.TypeName}>"))
        {
            WriteNewMember(writer, context, addNewLine: false, InheritDocComment);
            using (writer.BeginBlock($"public override {context.CSharpNullableTypeName} Read({TomlSerializationNamespace}.TomlReader reader)"))
            {
                WriteRead();
                writer.WriteLine("reader.Read();");
                writer.WriteLine($"return new {context.TypeName}(value);");
            }

            WriteNewMember(writer, context, addNewLine: true, InheritDocComment);
            using (writer.BeginBlock($"public override void Write({TomlSerializationNamespace}.TomlWriter writer, {context.TypeName} value)"))
            {
                // TOML has no null value
                if (context.IsReferenceType)
                {
                    using (writer.BeginBlock("if (value is null)"))
                    {
                        writer.WriteLine($"throw new {TomlNamespace}.TomlException(\"A null {context.TypeName} cannot be written: TOML has no null value.\");");
                    }
                }

                WriteWrite();
            }

            if (idType is IdType.System_DateTime)
            {
                // DateTimeOffset.LocalDateTime clamps a value whose local time is out of range, which changes the instant
                writer.WriteLine();
                using (writer.BeginBlock($"private static global::System.DateTime ToLocalDateTime({TomlSerializationNamespace}.TomlReader reader, global::System.DateTimeOffset value)"))
                {
                    writer.WriteLine("var utc = value.UtcDateTime;");
                    writer.WriteLine("var localTicks = utc.Ticks + global::System.TimeZoneInfo.Local.GetUtcOffset(utc).Ticks;");
                    using (writer.BeginBlock("if (localTicks < global::System.DateTime.MinValue.Ticks || localTicks > global::System.DateTime.MaxValue.Ticks)"))
                    {
                        writer.WriteLine("throw reader.CreateException(\"The date-time \" + value.ToString(\"O\", global::System.Globalization.CultureInfo.InvariantCulture) + \" cannot be represented as a local DateTime in the time zone of the machine.\");");
                    }

                    writer.WriteLine("return value.LocalDateTime;");
                }
            }

            if (idType is IdType.System_DateTime or IdType.System_DateTimeOffset)
            {
                // Same precision as the built-in converters: the number of significant digits of the fraction of a second
                writer.WriteLine();
                using (writer.BeginBlock("private static int GetSecondPrecision(long ticks)"))
                {
                    writer.WriteLine("var fraction = ticks % global::System.TimeSpan.TicksPerSecond;");
                    using (writer.BeginBlock("if (fraction == 0)"))
                    {
                        writer.WriteLine("return 0;");
                    }

                    writer.WriteLine("var precision = 7;");
                    using (writer.BeginBlock("while (fraction % 10 == 0)"))
                    {
                        writer.WriteLine("fraction /= 10;");
                        writer.WriteLine("precision--;");
                    }

                    writer.WriteLine("return precision;");
                }
            }
        }

        // Reads the current token into a 'value' local of the underlying type, without advancing the reader
        void WriteRead()
        {
            switch (idType)
            {
                case IdType.System_Boolean:
                    writer.WriteLine("var value = reader.GetBoolean();");
                    break;

                case IdType.System_Byte or IdType.System_SByte or IdType.System_Int16 or IdType.System_UInt16 or IdType.System_Int32 or IdType.System_UInt32:
                    writer.WriteLine("var integer = reader.GetInt64();");
                    using (writer.BeginBlock($"if (integer < {valueType}.MinValue || integer > {valueType}.MaxValue)"))
                    {
                        writer.WriteLine("throw reader.CreateException(\"TOML integer value \" + integer.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \" is out of range.\");");
                    }

                    writer.WriteLine($"var value = ({valueType})integer;");
                    break;

                case IdType.System_Int64:
                    writer.WriteLine("var value = reader.GetInt64();");
                    break;

                // TOML integers are 64-bit signed integers. Larger values are written as strings.
                case IdType.System_UInt64 or IdType.System_Int128 or IdType.System_UInt128 or IdType.System_Numerics_BigInteger:
                    writer.WriteLine($"{valueType} value;");
                    using (writer.BeginBlock($"if (reader.TokenType == {TomlSerializationNamespace}.TomlTokenType.String)"))
                    {
                        writer.WriteLine("var text = reader.GetString();");
                        using (writer.BeginBlock($"if (!{valueType}.TryParse(text, global::System.Globalization.NumberStyles.Integer, global::System.Globalization.CultureInfo.InvariantCulture, out value))"))
                        {
                            writer.WriteLine("throw reader.CreateException(\"Invalid integer value '\" + text + \"'.\");");
                        }
                    }

                    using (writer.BeginBlock("else"))
                    {
                        writer.WriteLine("var integer = reader.GetInt64();");
                        if (idType is IdType.System_UInt64 or IdType.System_UInt128)
                        {
                            using (writer.BeginBlock("if (integer < 0)"))
                            {
                                writer.WriteLine("throw reader.CreateException(\"TOML integer value \" + integer.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \" is out of range.\");");
                            }

                            writer.WriteLine("value = (ulong)integer;");
                        }
                        else
                        {
                            writer.WriteLine("value = integer;");
                        }
                    }

                    break;

                case IdType.System_Half or IdType.System_Single:
                    writer.WriteLine("var number = reader.GetDouble();");
                    writer.WriteLine($"var value = ({valueType})number;");

                    // Values slightly above MaxValue round to it; only values that overflow are out of range
                    using (writer.BeginBlock($"if ({(idType is IdType.System_Half ? "global::System.Half" : "float")}.IsInfinity(value) && !double.IsInfinity(number))"))
                    {
                        writer.WriteLine("throw reader.CreateException(\"TOML numeric value \" + number.ToString(\"R\", global::System.Globalization.CultureInfo.InvariantCulture) + \" is out of range.\");");
                    }

                    break;

                case IdType.System_Double:
                    writer.WriteLine("var value = reader.GetDouble();");
                    break;

                case IdType.System_Decimal:
                    writer.WriteLine("var value = reader.GetDecimal();");
                    break;

                case IdType.System_String:
                    writer.WriteLine("var value = reader.GetString();");
                    break;

                case IdType.System_Guid or IdType.MongoDB_Bson_ObjectId:
                    writer.WriteLine("var text = reader.GetString();");
                    using (writer.BeginBlock($"if (!{valueType}.TryParse(text, out var value))"))
                    {
                        writer.WriteLine($"throw reader.CreateException(\"Invalid {context.ValueTypeShortName} value '\" + text + \"'.\");");
                    }

                    break;

                // Like the built-in converter, Z gives a UTC value and a numeric offset a local value
                case IdType.System_DateTime:
                    writer.WriteLine("var dateTime = reader.GetTomlDateTime();");
                    WriteSwitchExpression("var value = dateTime.Kind switch",
                        $"{TomlNamespace}.TomlDateTimeKind.OffsetDateTimeByZ => dateTime.DateTime.UtcDateTime,",
                        $"{TomlNamespace}.TomlDateTimeKind.OffsetDateTimeByNumber => ToLocalDateTime(reader, dateTime.DateTime),",
                        $"{TomlNamespace}.TomlDateTimeKind.LocalDateTime or {TomlNamespace}.TomlDateTimeKind.LocalDate => dateTime.DateTime.DateTime,",
                        "_ => throw reader.CreateException(\"TOML \" + dateTime.Kind.ToString() + \" cannot be converted to DateTime.\"),");

                    break;

                case IdType.System_DateTimeOffset:
                    writer.WriteLine("var dateTime = reader.GetTomlDateTime();");
                    using (writer.BeginBlock($"if (dateTime.Kind is not ({TomlNamespace}.TomlDateTimeKind.OffsetDateTimeByZ or {TomlNamespace}.TomlDateTimeKind.OffsetDateTimeByNumber))"))
                    {
                        writer.WriteLine("throw reader.CreateException(\"TOML \" + dateTime.Kind.ToString() + \" cannot be converted to DateTimeOffset.\");");
                    }

                    writer.WriteLine("var value = dateTime.DateTime;");
                    break;

                default:
                    throw new InvalidOperationException($"Type '{idType}' is not supported");
            }
        }

        void WriteSwitchExpression(string header, params string[] arms)
        {
            writer.WriteLine(header);
            writer.WriteLine('{');
            writer.Indentation++;
            foreach (var arm in arms)
            {
                writer.WriteLine(arm);
            }

            writer.Indentation--;
            writer.WriteLine("};");
        }

        void WriteWrite()
        {
            switch (idType)
            {
                case IdType.System_Boolean:
                    writer.WriteLine("writer.WriteBooleanValue(value.Value);");
                    break;

                case IdType.System_Byte or IdType.System_SByte or IdType.System_Int16 or IdType.System_UInt16 or IdType.System_Int32 or IdType.System_UInt32 or IdType.System_Int64:
                    writer.WriteLine("writer.WriteIntegerValue(value.Value);");
                    break;

                case IdType.System_UInt64 or IdType.System_Int128 or IdType.System_UInt128 or IdType.System_Numerics_BigInteger:
                    var condition = idType switch
                    {
                        IdType.System_UInt64 => "value.Value <= long.MaxValue",
                        IdType.System_UInt128 => "value.Value <= (global::System.UInt128)long.MaxValue",
                        _ => "value.Value >= long.MinValue && value.Value <= long.MaxValue",
                    };
                    using (writer.BeginBlock($"if ({condition})"))
                    {
                        writer.WriteLine(idType is IdType.System_UInt128 ? "writer.WriteIntegerValue((long)(ulong)value.Value);" : "writer.WriteIntegerValue((long)value.Value);");
                    }

                    using (writer.BeginBlock("else"))
                    {
                        writer.WriteLine("writer.WriteStringValue(value.Value.ToString(global::System.Globalization.CultureInfo.InvariantCulture));");
                    }

                    break;

                case IdType.System_Half:
                    writer.WriteLine("writer.WriteFloatValue((float)value.Value);");
                    break;

                case IdType.System_Single or IdType.System_Double:
                    writer.WriteLine("writer.WriteFloatValue(value.Value);");
                    break;

                case IdType.System_Decimal:
                    // Older versions of Meziantou.Framework.Toml cannot write a decimal without converting it to a double
                    writer.WriteLine(context.SupportTomlWriteDecimalValue ? "writer.WriteDecimalValue(value.Value);" : "writer.WriteFloatValue((double)value.Value);");
                    break;

                case IdType.System_String:
                    using (writer.BeginBlock("if (value.Value is null)"))
                    {
                        writer.WriteLine($"throw new {TomlNamespace}.TomlException(\"A {context.TypeName} whose value is null cannot be written: TOML has no null value.\");");
                    }

                    writer.WriteLine("writer.WriteStringValue(value.Value);");
                    break;

                case IdType.System_Guid:
                    writer.WriteLine("writer.WriteStringValue(value.Value.ToString(\"D\"));");
                    break;

                case IdType.MongoDB_Bson_ObjectId:
                    writer.WriteLine("writer.WriteStringValue(value.Value.ToString());");
                    break;

                case IdType.System_DateTime:
                    writer.WriteLine("var dateTime = value.Value;");
                    WriteSwitchExpression("var kind = dateTime.Kind switch",
                        $"global::System.DateTimeKind.Utc => {TomlNamespace}.TomlDateTimeKind.OffsetDateTimeByZ,",
                        $"global::System.DateTimeKind.Local => {TomlNamespace}.TomlDateTimeKind.OffsetDateTimeByNumber,",
                        $"_ => {TomlNamespace}.TomlDateTimeKind.LocalDateTime,");

                    writer.WriteLine("var offset = dateTime.Kind == global::System.DateTimeKind.Local ? global::System.TimeZoneInfo.Local.GetUtcOffset(dateTime) : global::System.TimeSpan.Zero;");

                    // Near MinValue or MaxValue, the offset of the machine can move the UTC instant out of range
                    writer.WriteLine("var utcTicks = dateTime.Ticks - offset.Ticks;");
                    using (writer.BeginBlock("if (utcTicks < global::System.DateTime.MinValue.Ticks || utcTicks > global::System.DateTime.MaxValue.Ticks)"))
                    {
                        writer.WriteLine($"throw new {TomlNamespace}.TomlException(\"The local date-time \" + dateTime.ToString(\"O\", global::System.Globalization.CultureInfo.InvariantCulture) + \" cannot be written as an offset date-time because its UTC offset \" + offset.ToString() + \" moves it out of range.\");");
                    }

                    writer.WriteLine("var dateTimeOffset = new global::System.DateTimeOffset(global::System.DateTime.SpecifyKind(dateTime, global::System.DateTimeKind.Unspecified), offset);");
                    writer.WriteLine($"writer.WriteDateTimeValue(new {TomlNamespace}.TomlDateTime(dateTimeOffset, GetSecondPrecision(dateTime.Ticks), kind));");
                    break;

                case IdType.System_DateTimeOffset:
                    writer.WriteLine($"var kind = value.Value.Offset == global::System.TimeSpan.Zero ? {TomlNamespace}.TomlDateTimeKind.OffsetDateTimeByZ : {TomlNamespace}.TomlDateTimeKind.OffsetDateTimeByNumber;");
                    writer.WriteLine($"writer.WriteDateTimeValue(new {TomlNamespace}.TomlDateTime(value.Value, GetSecondPrecision(value.Value.Ticks), kind));");
                    break;

                default:
                    throw new InvalidOperationException($"Type '{idType}' is not supported");
            }
        }
    }
}
