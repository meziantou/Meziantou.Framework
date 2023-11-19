using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meziantou.Framework.StronglyTypedId;

[Generator]
public sealed partial class StronglyTypedIdSourceGenerator : IIncrementalGenerator
{
    private static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
    private static readonly string GeneratedCodeAttribute = $"[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"Meziantou.Framework.StronglyTypedId\", \"{Version}\")]";

    private const string FieldName = "_value";
    private const string PropertyName = "Value";
    private const string PropertyAsStringName = "ValueAsString";

    private static readonly DiagnosticDescriptor UnsupportedType = new(
        id: "MFSTID0001",
        title: "Not supported type",
        messageFormat: "The type '{0}' is not supported",
        category: "StronglyTypedId",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var types = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Meziantou.Framework.Annotations.StronglyTypedIdAttribute",
                predicate: static (syntax, cancellationToken) => IsSyntaxTargetForGeneration(syntax),
                transform: static (ctx, cancellationToken) => GetSemanticTargetForGeneration(ctx, cancellationToken))
            .Where(static m => m is not null)
            .WithTrackingName("Syntax");

        context.RegisterSourceOutput(types, (context, attribute) => Execute(context, attribute!));

        static bool IsSyntaxTargetForGeneration(SyntaxNode syntax)
        {
            return (syntax.IsKind(SyntaxKind.StructDeclaration) || syntax.IsKind(SyntaxKind.ClassDeclaration) || syntax.IsKind(SyntaxKind.RecordDeclaration) || syntax.IsKind(SyntaxKind.RecordStructDeclaration)) &&
                   HasAttribute((TypeDeclarationSyntax)syntax);

            // Ensure there is at least one attribute with one parameter
            static bool HasAttribute(TypeDeclarationSyntax syntax)
            {
                if (syntax.AttributeLists.Count == 0)
                    return false;

                foreach (var attributeList in syntax.AttributeLists)
                {
                    foreach (var attribute in attributeList.Attributes)
                    {
                        if (attribute.ArgumentList is not null && attribute.ArgumentList.Arguments.Count > 0)
                            return true;
                    }
                }

                return false;
            }
        }

        static object? GetSemanticTargetForGeneration(GeneratorAttributeSyntaxContext ctx, CancellationToken cancellationToken)
        {
            var semanticModel = ctx.SemanticModel;
            var compilation = semanticModel.Compilation;
            var typeDeclaration = (TypeDeclarationSyntax)ctx.TargetNode;

            foreach (var attribute in ctx.Attributes)
            {
                var arguments = attribute.ConstructorArguments;
                if (arguments.Length != 6)
                    continue;

                var idTypeArgument = arguments[0];
                if (idTypeArgument.Value is not ITypeSymbol type)
                    continue;

                var converters = StronglyTypedIdConverters.None;
                AddConverter(arguments[1], StronglyTypedIdConverters.System_Text_Json);
                AddConverter(arguments[2], StronglyTypedIdConverters.Newtonsoft_Json);
                AddConverter(arguments[3], StronglyTypedIdConverters.System_ComponentModel_TypeConverter);
                AddConverter(arguments[4], StronglyTypedIdConverters.MongoDB_Bson_Serialization);
                void AddConverter(TypedConstant value, StronglyTypedIdConverters converterValue)
                {
                    if (value.Value is bool argumentValue && argumentValue)
                    {
                        converters |= converterValue;
                    }
                }

                var addCodeGeneratedAttribute = false;
                if (arguments[5].Value is bool addCodeGeneratedAttributeValue)
                {
                    addCodeGeneratedAttribute = addCodeGeneratedAttributeValue;
                }

                var attributeSyntax = attribute.ApplicationSyntaxReference!.GetSyntax(cancellationToken);
                var idType = GetIdType(semanticModel.Compilation, type);

                if (idType == IdType.Unknown)
                {
                    return new DiagnosticInfo()
                    {
                        Descriptor = UnsupportedType,
                        Location = attributeSyntax.GetLocation(),
                        MessageArgs = new[] { type.ToDisplayString() },
                    };
                }

                return new AttributeInfo(semanticModel.Compilation, attributeSyntax, (INamedTypeSymbol)ctx.TargetSymbol, idType, type, converters, addCodeGeneratedAttribute);
            }

            return null;
        }
    }

    private static void Execute(SourceProductionContext context, object semanticContext)
    {
        if (semanticContext is DiagnosticInfo diagnostic)
        {
            context.ReportDiagnostic(diagnostic.CreateDiagnostic());
            return;
        }

        var attribute = (AttributeInfo)semanticContext;

        var writer = new CSharpGeneratedFileWriter();

        // Write debug info
        writer.WriteLine("// Id Type: " + attribute.IdType);
        writer.WriteLine("// TypeName: " + attribute.TypeName);

        writer.WriteLine("#nullable enable");

        var baseTypes = $"global::System.IEquatable<{attribute.TypeName}>";
        if (attribute.CanImplementIParsable())
        {
            baseTypes += $", global::System.IParsable<{attribute.TypeName}>";
        }

        if (attribute.CanImplementISpanParsable())
        {
            baseTypes += $", global::System.ISpanParsable<{attribute.TypeName}>";
        }

        if (attribute.SupportIStronglyTyped)
        {
            baseTypes += $", global::Meziantou.Framework.IStronglyTypedId";
        }

        if (attribute.SupportIStronglyTypedOfT)
        {
            baseTypes += $", global::Meziantou.Framework.IStronglyTypedId<{attribute.ValueTypeCSharpTypeName}>";
        }

        if (attribute.ImplementsIComparable && !attribute.ImplementsIComparableOfT)
        {
            baseTypes += $", global::System.IComparable<{attribute.TypeName}>";
        }

        if (!attribute.ImplementsIComparable && attribute.ImplementsIComparableOfT)
        {
            baseTypes += $", global::System.IComparable";
        }

        void WriteAttributes(CSharpGeneratedFileWriter writer)
        {
            if (attribute.CanGenerateTypeConverter())
            {
                writer.WriteLine($"[global::System.ComponentModel.TypeConverterAttribute(typeof({attribute.TypeConverterTypeName}))]");
            }

            if (attribute.CanGenerateSystemTextJsonConverter())
            {
                writer.WriteLine($"[global::System.Text.Json.Serialization.JsonConverterAttribute(typeof({attribute.SystemTextJsonConverterTypeName}))]");
            }

            if (attribute.CanGenerateMongoDbConverter())
            {
                writer.WriteLine($"[global::MongoDB.Bson.Serialization.Attributes.BsonSerializerAttribute(typeof({attribute.MongoDbConverterTypeName}))]");
            }

            if (attribute.CanGenerateNewtonsoftJsonConverter())
            {
                writer.WriteLine($"[global::Newtonsoft.Json.JsonConverterAttribute(typeof({attribute.NewtonsoftJsonConverterTypeName}))]");
            }
        }

        var indentation = BeginPartialContext(writer, attribute.PartialTypeContext, WriteAttributes, baseTypes);
        GenerateTypeMembers(writer, attribute);
        GenerateTypeConverter(writer, attribute);
        GenerateSystemTextJsonConverter(writer, attribute);
        GenerateMongoDBBsonSerializationConverter(writer, attribute);
        GenerateNewtonsoftJsonConverter(writer, attribute);
        for (var i = 0; i < indentation; i++)
        {
            writer.EndBlock();
        }

        Debug.Assert(writer.Indentation == 0);
        context.AddSource(attribute.TypeName + ".g.cs", writer.ToSourceText());
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed manually")]
    private static int BeginPartialContext(CSharpGeneratedFileWriter writer, PartialTypeContext context, Action<CSharpGeneratedFileWriter>? writeAttributes = null, string? baseTypes = null)
    {
        var initialIndentation = writer.Indentation;
        if (!string.IsNullOrWhiteSpace(context.Namespace))
        {
            writer.Write("namespace ");
            writer.WriteLine(context.Namespace);
            writer.BeginBlock();
        }

        WriteContainingTypes(context.Parent);
        writeAttributes?.Invoke(writer);
        WriteBeginType(context, baseTypes);
        return writer.Indentation - initialIndentation;

        void WriteContainingTypes(PartialTypeContext? context)
        {
            if (context == null)
                return;

            WriteContainingTypes(context.Parent);
            WriteBeginType(context, baseTypes: null);
        }

        void WriteBeginType(PartialTypeContext context, string? baseTypes)
        {
            writer.Write("partial ");
            writer.Write(context.Keyword);
            writer.Write(' ');
            writer.Write(context.Name);
            if (baseTypes is not null)
            {
                writer.Write(" : ");
                writer.Write(baseTypes);
            }

            writer.WriteLine();
            _ = writer.BeginBlock();
        }
    }

    private static void WriteNewMember(CSharpGeneratedFileWriter writer, AttributeInfo info, bool addNewLine, XNode[]? xmlDocumentation = null)
    {
        if (addNewLine)
        {
            writer.WriteLine();
        }

        if (xmlDocumentation is not null)
        {
            writer.WriteXmlComment(xmlDocumentation);
        }

        if (info.AddCodeGeneratedAttribute)
        {
            writer.WriteLine(GeneratedCodeAttribute);
        }
    }

    private static IdType GetIdType(Compilation compilation, ITypeSymbol symbol)
    {
        var result = symbol.SpecialType switch
        {
            SpecialType.System_Boolean => IdType.System_Boolean,
            SpecialType.System_Byte => IdType.System_Byte,
            SpecialType.System_DateTime => IdType.System_DateTime,
            SpecialType.System_Decimal => IdType.System_Decimal,
            SpecialType.System_Double => IdType.System_Double,
            SpecialType.System_Int16 => IdType.System_Int16,
            SpecialType.System_Int32 => IdType.System_Int32,
            SpecialType.System_Int64 => IdType.System_Int64,
            SpecialType.System_SByte => IdType.System_SByte,
            SpecialType.System_Single => IdType.System_Single,
            SpecialType.System_String => IdType.System_String,
            SpecialType.System_UInt16 => IdType.System_UInt16,
            SpecialType.System_UInt32 => IdType.System_UInt32,
            SpecialType.System_UInt64 => IdType.System_UInt64,
            _ => IdType.Unknown,
        };

        if (result != IdType.Unknown)
            return result;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.Guid")))
            return IdType.System_Guid;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.DateTimeOffset")))
            return IdType.System_DateTimeOffset;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.Numerics.BigInteger")))
            return IdType.System_Numerics_BigInteger;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.Half")))
            return IdType.System_Half;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.Int128")))
            return IdType.System_Int128;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.UInt128")))
            return IdType.System_UInt128;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("MongoDB.Bson.ObjectId")))
            return IdType.MongoDB_Bson_ObjectId;

        return IdType.Unknown;
    }

    private static string GetPrivateOrProtectedModifier(AttributeInfo type)
    {
        if (type.IsReferenceType && !type.IsSealed)
            return "protected";

        return "private";
    }

    private sealed class AttributeInfo : IEquatable<AttributeInfo>
    {
        public AttributeInfo(Compilation compilation, SyntaxNode attributeSyntax, INamedTypeSymbol typeSymbol, IdType idType, ITypeSymbol idTypeSymbol, StronglyTypedIdConverters converters, bool addCodeGeneratedAttribute)
        {
            Debug.Assert(idType != IdType.Unknown);

            AttributeSyntax = attributeSyntax;
            IdType = idType;
            Converters = converters;
            AddCodeGeneratedAttribute = addCodeGeneratedAttribute;
            TypeName = typeSymbol.Name;
            IsSealed = typeSymbol.IsSealed;
            IsReferenceType = typeSymbol.IsReferenceType;
            PartialTypeContext = GetContext(typeSymbol);

            var readOnlySpanSymbol = compilation.GetTypeByMetadataName("System.ReadOnlySpan`1");
            var readOnlySpanCharSymbol = readOnlySpanSymbol?.Construct(compilation.GetSpecialType(SpecialType.System_Char));
            foreach (var member in typeSymbol.GetMembers())
            {
                switch (member)
                {
                    case IMethodSymbol { IsStatic: true, Name: "op_Equality", ReturnType.SpecialType: SpecialType.System_Boolean, Parameters: [var param1, var param2] } when SymbolEqual(param1.Type, typeSymbol) && SymbolEqual(param2.Type, typeSymbol):
                        IsOpEqualsDefined = true;
                        break;

                    case IMethodSymbol { IsStatic: true, Name: "op_Inequality", ReturnType.SpecialType: SpecialType.System_Boolean, Parameters: [var param1, var param2] } when SymbolEqual(param1.Type, typeSymbol) && SymbolEqual(param2.Type, typeSymbol):
                        IsOpNotEqualsDefined = true;
                        break;

                    case IMethodSymbol { IsStatic: true, Name: "op_LessThan", ReturnType.SpecialType: SpecialType.System_Boolean, Parameters: [var param1, var param2] } when SymbolEqual(param1.Type, typeSymbol) && SymbolEqual(param2.Type, typeSymbol):
                        IsOpLessThanDefined = true;
                        break;

                    case IMethodSymbol { IsStatic: true, Name: "op_GreaterThan", ReturnType.SpecialType: SpecialType.System_Boolean, Parameters: [var param1, var param2] } when SymbolEqual(param1.Type, typeSymbol) && SymbolEqual(param2.Type, typeSymbol):
                        IsOpGreaterThanDefined = true;
                        break;

                    case IMethodSymbol { IsStatic: true, Name: "op_LessThanOrEqual", ReturnType.SpecialType: SpecialType.System_Boolean, Parameters: [var param1, var param2] } when SymbolEqual(param1.Type, typeSymbol) && SymbolEqual(param2.Type, typeSymbol):
                        IsOpLessThanOrEqualDefined = true;
                        break;

                    case IMethodSymbol { IsStatic: true, Name: "op_GreaterThanOrEqual", ReturnType.SpecialType: SpecialType.System_Boolean, Parameters: [var param1, var param2] } when SymbolEqual(param1.Type, typeSymbol) && SymbolEqual(param2.Type, typeSymbol):
                        IsOpGreaterThanOrEqualDefined = true;
                        break;

                    case IMethodSymbol { IsStatic: true, Name: "TryParse", ReturnType.SpecialType: SpecialType.System_Boolean, Parameters: [{ Type.SpecialType: SpecialType.System_String }, ..] }:
                        IsTryParseDefined_String = true;
                        break;

                    case IMethodSymbol { IsStatic: true, Name: "TryParse", ReturnType.SpecialType: SpecialType.System_Boolean, Parameters: [var param1, ..] } when SymbolEqual(param1.Type, readOnlySpanCharSymbol):
                        IsTryParseDefined_ReadOnlySpan = true;
                        break;

                    case IMethodSymbol { IsStatic: true, Name: "Parse", Parameters: [{ Type.SpecialType: SpecialType.System_String }, ..] }:
                        IsParseDefined_String = true;
                        break;

                    case IMethodSymbol { IsStatic: true, Name: "Parse", Parameters: [var param1, ..] } when SymbolEqual(param1.Type, readOnlySpanCharSymbol):
                        IsParseDefined_ReadOnlySpan = true;
                        break;

                    case IMethodSymbol { IsStatic: false, Name: ".ctor", Parameters: [var param1] } when SymbolEqual(param1.Type, idTypeSymbol):
                        IsCtorDefined = true;
                        break;

                    case IFieldSymbol { IsStatic: false, Name: FieldName }:
                        IsFieldDefined = true;
                        break;

                    case IPropertySymbol { IsStatic: false, Name: PropertyName }:
                        IsValueDefined = true;
                        break;

                    case IPropertySymbol { IsStatic: false, Name: PropertyAsStringName }:
                        IsValueAsStringDefined = true;
                        break;

                    case IMethodSymbol { IsStatic: false, Name: "ToString", Parameters: [], ReturnType.SpecialType: SpecialType.System_String }:
                        IsToStringDefined = true;
                        break;

                    case IMethodSymbol { IsStatic: false, Name: "GetHashCode", Parameters: [], ReturnType.SpecialType: SpecialType.System_Int32 }:
                        IsGetHashcodeDefined = true;
                        break;

                    case IMethodSymbol { IsStatic: false, Name: "Equals", Parameters: [{ Type.SpecialType: SpecialType.System_Object }], ReturnType.SpecialType: SpecialType.System_Boolean }:
                        IsEqualsDefined = true;
                        break;

                    case IMethodSymbol { IsStatic: false, Name: "Equals", Parameters: [var param1], ReturnType.SpecialType: SpecialType.System_Boolean } when SymbolEqual(typeSymbol, param1.Type):
                        IsIEquatableEqualsDefined = true;
                        break;
                }
            }

            SupportReadOnlySpanChar = readOnlySpanCharSymbol is not null;
            SupportIStronglyTyped = compilation.GetTypeByMetadataName("Meziantou.Framework.IStronglyTypedId") is not null;
            SupportIStronglyTypedOfT = compilation.GetTypeByMetadataName("Meziantou.Framework.IStronglyTypedId`1") is not null;
            SupportIParsable = compilation.GetTypeByMetadataName("System.IParsable`1") is not null;
            SupportISpanParsable = compilation.GetTypeByMetadataName("System.ISpanParsable`1") is not null;
            SupportTypeConverter = compilation.GetTypeByMetadataName("System.ComponentModel.TypeConverter") is not null;
            SupportSystemTextJsonConverter = compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonConverter`1") is not null;
            SupportNewtonsoftJsonConverter = compilation.GetTypeByMetadataName("Newtonsoft.Json.JsonConverter") is not null;
            SupportMongoDbConverter = compilation.GetTypeByMetadataName("MongoDB.Bson.Serialization.Serializers.SerializerBase`1") is not null;
            SupportNotNullWhenAttribute = compilation.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.NotNullWhenAttribute") is { } type && compilation.IsSymbolAccessibleWithin(type, compilation.Assembly);
            SupportStaticInterfaces = compilation.SyntaxTrees.FirstOrDefault()?.Options is CSharpParseOptions { LanguageVersion: >= (LanguageVersion)1100 };

            var icomparableSymbol = compilation.GetTypeByMetadataName("System.IComparable");
            var icomparableCompareToMember = icomparableSymbol?.GetMembers("CompareTo").FirstOrDefault();
            if (icomparableSymbol is not null && icomparableCompareToMember is not null)
            {
                ImplementsIComparable = Implements(typeSymbol, icomparableSymbol);
                ImplementsIComparable_CompareTo = typeSymbol.FindImplementationForInterfaceMember(icomparableCompareToMember) is not null;
            }

            var icomparableOfTSymbol = compilation.GetTypeByMetadataName("System.IComparable`1");
            var icomparableOfTypeSymbol = icomparableOfTSymbol?.Construct(typeSymbol);
            var icomparableOfTCompareToMember = icomparableOfTypeSymbol?.GetMembers("CompareTo").FirstOrDefault();
            if (icomparableOfTSymbol is not null && icomparableOfTCompareToMember is not null)
            {
                ImplementsIComparableOfT = Implements(typeSymbol, icomparableOfTypeSymbol);
                ImplementsIComparableOfT_CompareTo = typeSymbol.FindImplementationForInterfaceMember(icomparableOfTCompareToMember) is not null;
            }

            CSharpNullableTypeName = IsReferenceType ? (TypeName + "?") : TypeName;
            ValueTypeShortName = GetShortName(IdType);
            ValueTypeCSharpTypeName = GetCSharpTypeName(IdType);
            ValueTypeCSharpNullableTypeName = ValueTypeCSharpTypeName + (IsReferenceType ? "?" : "");

            static bool Implements(ITypeSymbol symbol, ITypeSymbol? interfaceSymbol)
            {
                if (interfaceSymbol is null)
                    return false;

                foreach (var iface in symbol.AllInterfaces)
                {
                    if (SymbolEqual(iface, interfaceSymbol))
                        return true;
                }

                return false;
            }

            static bool SymbolEqual(ITypeSymbol? left, ITypeSymbol? right) => SymbolEqualityComparer.Default.Equals(left, right);

            static PartialTypeContext GetContext(ITypeSymbol typeSymbol)
            {
                var ns = typeSymbol.ContainingNamespace?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted));
                var partialType = new PartialTypeContext(GetType(typeSymbol), ns, typeSymbol.Name);

                var current = partialType;
                var parentType = typeSymbol.ContainingSymbol as ITypeSymbol;
                while (parentType is not null)
                {
                    current.Parent = new PartialTypeContext(GetType(parentType), ns, parentType.Name);
                    current = current.Parent;
                    parentType = parentType.ContainingSymbol as ITypeSymbol;
                }

                return partialType;

                static string GetType(ITypeSymbol namedTypeSymbol)
                {
                    return namedTypeSymbol switch
                    {
                        { TypeKind: TypeKind.Interface } => "interface",
                        { IsRecord: false, IsValueType: false } => "class",
                        { IsRecord: false, IsValueType: true } => "struct",
                        { IsRecord: true, IsValueType: false } => "record",
                        { IsRecord: true, IsValueType: true } => "record struct",
                        _ => "",
                    };
                }
            }
        }

        // Only use to report diagnostic
        public SyntaxNode AttributeSyntax { get; }

        // Info provided by the attribute
        public PartialTypeContext PartialTypeContext { get; }
        public IdType IdType { get; }
        public StronglyTypedIdConverters Converters { get; }
        public bool AddCodeGeneratedAttribute { get; }

        // Computed info
        public string TypeName { get; }
        public bool IsSealed { get; }
        public bool IsReferenceType { get; }
        public bool IsCtorDefined { get; }
        public bool IsFieldDefined { get; }
        public bool IsValueDefined { get; }
        public bool IsValueAsStringDefined { get; }
        public bool IsToStringDefined { get; }
        public bool IsGetHashcodeDefined { get; }
        public bool IsEqualsDefined { get; }
        public bool IsIEquatableEqualsDefined { get; }
        public bool IsOpEqualsDefined { get; }
        public bool IsOpNotEqualsDefined { get; }
        public bool IsTryParseDefined_String { get; }
        public bool IsTryParseDefined_ReadOnlySpan { get; }
        public bool IsParseDefined_String { get; }
        public bool IsParseDefined_ReadOnlySpan { get; }
        public bool IsOpLessThanDefined { get; }
        public bool IsOpGreaterThanDefined { get; }
        public bool IsOpLessThanOrEqualDefined { get; }
        public bool IsOpGreaterThanOrEqualDefined { get; }
        public bool ImplementsIComparable { get; }
        public bool ImplementsIComparable_CompareTo { get; }
        public bool ImplementsIComparableOfT { get; }
        public bool ImplementsIComparableOfT_CompareTo { get; }

        public bool SupportStaticInterfaces { get; }
        public bool SupportIParsable { get; }
        public bool SupportISpanParsable { get; }
        public bool SupportTypeConverter { get; }
        public bool SupportSystemTextJsonConverter { get; }
        public bool SupportNewtonsoftJsonConverter { get; }
        public bool SupportMongoDbConverter { get; }
        public bool SupportNotNullWhenAttribute { get; }
        public bool SupportReadOnlySpanChar { get; }
        public bool SupportIStronglyTyped { get; }
        public bool SupportIStronglyTypedOfT { get; }

        public string TypeConverterTypeName => TypeName + "TypeConverter";
        public string SystemTextJsonConverterTypeName => TypeName + "JsonConverter";
        public string NewtonsoftJsonConverterTypeName => TypeName + "NewtonsoftJsonConverter";
        public string MongoDbConverterTypeName => TypeName + "BsonConverter";

        public string ValueTypeShortName { get; }
        public string ValueTypeCSharpTypeName { get; }
        public string ValueTypeCSharpNullableTypeName { get; }
        public string CSharpNullableTypeName { get; }

        public bool IsValueTypeNullable => IdType is IdType.System_String;
        public bool ValueTypeHasParseReadOnlySpan => IdType != IdType.MongoDB_Bson_ObjectId && SupportReadOnlySpanChar;

        public override bool Equals(object? obj) => Equals(obj as AttributeInfo);

        public bool Equals(AttributeInfo? other)
        {
            // Do not use TypeSymbol as it cannot be compared across Compilation.
            return other is not null
                && IsReferenceType == other.IsReferenceType
                && PartialTypeContext == other.PartialTypeContext
                && IdType == other.IdType
                && TypeName == other.TypeName
                && Converters == other.Converters
                && AddCodeGeneratedAttribute == other.AddCodeGeneratedAttribute
                && IsSealed == other.IsSealed
                && IsCtorDefined == other.IsCtorDefined
                && IsFieldDefined == other.IsFieldDefined
                && IsValueDefined == other.IsValueDefined
                && IsValueAsStringDefined == other.IsValueAsStringDefined
                && IsToStringDefined == other.IsToStringDefined
                && IsGetHashcodeDefined == other.IsGetHashcodeDefined
                && IsEqualsDefined == other.IsEqualsDefined
                && IsIEquatableEqualsDefined == other.IsIEquatableEqualsDefined
                && IsOpEqualsDefined == other.IsOpEqualsDefined
                && IsOpNotEqualsDefined == other.IsOpNotEqualsDefined
                && IsTryParseDefined_String == other.IsTryParseDefined_String
                && IsTryParseDefined_ReadOnlySpan == other.IsTryParseDefined_ReadOnlySpan
                && IsParseDefined_String == other.IsParseDefined_String
                && IsParseDefined_ReadOnlySpan == other.IsParseDefined_ReadOnlySpan
                && SupportStaticInterfaces == other.SupportStaticInterfaces
                && SupportIStronglyTyped == other.SupportIStronglyTyped
                && SupportIStronglyTypedOfT == other.SupportIStronglyTypedOfT
                && SupportIParsable == other.SupportIParsable
                && SupportISpanParsable == other.SupportISpanParsable
                && SupportTypeConverter == other.SupportTypeConverter
                && SupportSystemTextJsonConverter == other.SupportSystemTextJsonConverter
                && SupportNewtonsoftJsonConverter == other.SupportNewtonsoftJsonConverter
                && SupportMongoDbConverter == other.SupportMongoDbConverter
                && SupportNotNullWhenAttribute == other.SupportNotNullWhenAttribute
                && SupportReadOnlySpanChar == other.SupportReadOnlySpanChar
                && IsOpLessThanDefined == other.IsOpLessThanDefined
                && IsOpGreaterThanDefined == other.IsOpGreaterThanDefined
                && IsOpLessThanOrEqualDefined == other.IsOpLessThanOrEqualDefined
                && IsOpGreaterThanOrEqualDefined == other.IsOpGreaterThanOrEqualDefined
                && ImplementsIComparable == other.ImplementsIComparable
                && ImplementsIComparable_CompareTo == other.ImplementsIComparable_CompareTo
                && ImplementsIComparableOfT == other.ImplementsIComparableOfT
                && ImplementsIComparableOfT_CompareTo == other.ImplementsIComparableOfT_CompareTo;
        }

        public override int GetHashCode()
        {
            var hash = 0;
            hash = (hash * 397) ^ IsReferenceType.GetHashCode();
            hash = (hash * 397) ^ PartialTypeContext.GetHashCode();
            hash = (hash * 397) ^ IdType.GetHashCode();
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(TypeName);
            hash = (hash * 397) ^ Converters.GetHashCode();
            hash = (hash * 397) ^ AddCodeGeneratedAttribute.GetHashCode();
            hash = (hash * 397) ^ IsSealed.GetHashCode();
            hash = (hash * 397) ^ IsCtorDefined.GetHashCode();
            hash = (hash * 397) ^ IsFieldDefined.GetHashCode();
            hash = (hash * 397) ^ IsValueDefined.GetHashCode();
            hash = (hash * 397) ^ IsValueAsStringDefined.GetHashCode();
            hash = (hash * 397) ^ IsToStringDefined.GetHashCode();
            hash = (hash * 397) ^ IsGetHashcodeDefined.GetHashCode();
            hash = (hash * 397) ^ IsEqualsDefined.GetHashCode();
            hash = (hash * 397) ^ IsIEquatableEqualsDefined.GetHashCode();
            hash = (hash * 397) ^ IsOpEqualsDefined.GetHashCode();
            hash = (hash * 397) ^ IsOpNotEqualsDefined.GetHashCode();
            hash = (hash * 397) ^ IsTryParseDefined_String.GetHashCode();
            hash = (hash * 397) ^ IsTryParseDefined_ReadOnlySpan.GetHashCode();
            hash = (hash * 397) ^ IsParseDefined_String.GetHashCode();
            hash = (hash * 397) ^ IsParseDefined_ReadOnlySpan.GetHashCode();
            hash = (hash * 397) ^ SupportIStronglyTyped.GetHashCode();
            hash = (hash * 397) ^ SupportIStronglyTypedOfT.GetHashCode();
            hash = (hash * 397) ^ SupportStaticInterfaces.GetHashCode();
            hash = (hash * 397) ^ SupportIParsable.GetHashCode();
            hash = (hash * 397) ^ SupportISpanParsable.GetHashCode();
            hash = (hash * 397) ^ SupportTypeConverter.GetHashCode();
            hash = (hash * 397) ^ SupportSystemTextJsonConverter.GetHashCode();
            hash = (hash * 397) ^ SupportNewtonsoftJsonConverter.GetHashCode();
            hash = (hash * 397) ^ SupportMongoDbConverter.GetHashCode();
            hash = (hash * 397) ^ SupportNotNullWhenAttribute.GetHashCode();
            hash = (hash * 397) ^ SupportReadOnlySpanChar.GetHashCode();
            hash = (hash * 397) ^ IsOpLessThanDefined.GetHashCode();
            hash = (hash * 397) ^ IsOpGreaterThanDefined.GetHashCode();
            hash = (hash * 397) ^ IsOpLessThanOrEqualDefined.GetHashCode();
            hash = (hash * 397) ^ IsOpGreaterThanOrEqualDefined.GetHashCode();
            hash = (hash * 397) ^ ImplementsIComparable.GetHashCode();
            hash = (hash * 397) ^ ImplementsIComparable_CompareTo.GetHashCode();
            hash = (hash * 397) ^ ImplementsIComparableOfT.GetHashCode();
            hash = (hash * 397) ^ ImplementsIComparableOfT_CompareTo.GetHashCode();
            return hash;
        }

        public bool CanImplementISpanParsable()
        {
            return SupportStaticInterfaces && SupportISpanParsable && SupportReadOnlySpanChar && ValueTypeHasParseReadOnlySpan;
        }

        public bool CanImplementIParsable()
        {
            return SupportStaticInterfaces && SupportIParsable && SupportReadOnlySpanChar;
        }

        public bool CanGenerateTypeConverter()
        {
            return SupportTypeConverter && (Converters & StronglyTypedIdConverters.System_ComponentModel_TypeConverter) == StronglyTypedIdConverters.System_ComponentModel_TypeConverter;
        }

        public bool CanGenerateSystemTextJsonConverter()
        {
            return SupportSystemTextJsonConverter && (Converters & StronglyTypedIdConverters.System_Text_Json) == StronglyTypedIdConverters.System_Text_Json;
        }

        public bool CanGenerateNewtonsoftJsonConverter()
        {
            return SupportNewtonsoftJsonConverter && (Converters & StronglyTypedIdConverters.Newtonsoft_Json) == StronglyTypedIdConverters.Newtonsoft_Json;
        }

        public bool CanGenerateMongoDbConverter()
        {
            return SupportMongoDbConverter && (Converters & StronglyTypedIdConverters.MongoDB_Bson_Serialization) == StronglyTypedIdConverters.MongoDB_Bson_Serialization;
        }

        public bool MustImplementComparable()
        {
            return ImplementsIComparable || ImplementsIComparableOfT;
        }

        private static string GetCSharpTypeName(IdType type)
        {
            return type switch
            {
                IdType.System_Boolean => "bool",
                IdType.System_Byte => "byte",
                IdType.System_DateTime => "global::System.DateTime",
                IdType.System_DateTimeOffset => "global::System.DateTimeOffset",
                IdType.System_Decimal => "decimal",
                IdType.System_Double => "double",
                IdType.System_Guid => "global::System.Guid",
                IdType.System_Half => "global::System.Half",
                IdType.System_Int16 => "short",
                IdType.System_Int32 => "int",
                IdType.System_Int64 => "long",
                IdType.System_Int128 => "global::System.Int128",
                IdType.System_Numerics_BigInteger => "global::System.Numerics.BigInteger",
                IdType.System_SByte => "sbyte",
                IdType.System_Single => "float",
                IdType.System_String => "string",
                IdType.System_UInt16 => "ushort",
                IdType.System_UInt32 => "uint",
                IdType.System_UInt64 => "ulong",
                IdType.System_UInt128 => "global::System.UInt128",
                IdType.MongoDB_Bson_ObjectId => "global::MongoDB.Bson.ObjectId",
                _ => throw new ArgumentException($"Type '{type}' not supported", nameof(type)),
            };
        }

        private static string GetShortName(IdType type)
        {
            return type switch
            {
                IdType.System_Boolean => "Boolean",
                IdType.System_Byte => "Byte",
                IdType.System_DateTime => "DateTime",
                IdType.System_DateTimeOffset => "DateTimeOffset",
                IdType.System_Decimal => "Decimal",
                IdType.System_Double => "Double",
                IdType.System_Guid => "Guid",
                IdType.System_Half => "Half",
                IdType.System_Int16 => "Int16",
                IdType.System_Int32 => "Int32",
                IdType.System_Int64 => "Int64",
                IdType.System_Int128 => "Int128",
                IdType.System_Numerics_BigInteger => "BigInteger",
                IdType.System_SByte => "SByte",
                IdType.System_Single => "Single",
                IdType.System_String => "String",
                IdType.System_UInt16 => "UInt16",
                IdType.System_UInt32 => "UInt32",
                IdType.System_UInt64 => "UInt64",
                IdType.System_UInt128 => "UInt128",
                IdType.MongoDB_Bson_ObjectId => "ObjectId",
                _ => throw new ArgumentException($"Type '{type}' not supported", nameof(type)),
            };
        }

    }

    [Flags]
    private enum StronglyTypedIdConverters
    {
        None = 0x0,
        System_Text_Json = 0x1,
        Newtonsoft_Json = 0x2,
        System_ComponentModel_TypeConverter = 0x4,
        MongoDB_Bson_Serialization = 0x8,
    }

    private enum IdType
    {
        Unknown,
        System_Boolean,
        System_Byte,
        System_DateTime,
        System_DateTimeOffset,
        System_Decimal,
        System_Double,
        System_Guid,
        System_Half,
        System_Int16,
        System_Int32,
        System_Int64,
        System_Int128,
        System_Numerics_BigInteger,
        System_SByte,
        System_Single,
        System_String,
        System_UInt16,
        System_UInt32,
        System_UInt64,
        System_UInt128,
        MongoDB_Bson_ObjectId,
    }

    private sealed record PartialTypeContext(string Keyword, string? Namespace, string Name)
    {
        public PartialTypeContext? Parent { get; set; }
    }

    /// <summary>
    /// Descriptor for diagnostic instances using structural equality comparison.
    /// Provides a work-around for https://github.com/dotnet/roslyn/issues/68291.
    /// </summary>
    private readonly struct DiagnosticInfo : IEquatable<DiagnosticInfo>
    {
        public DiagnosticDescriptor Descriptor { get; init; }
        public object?[] MessageArgs { get; init; }
        public Location? Location { get; init; }

        public Diagnostic CreateDiagnostic()
            => Diagnostic.Create(Descriptor, Location, MessageArgs);

        public override readonly bool Equals(object? obj) => obj is DiagnosticInfo info && Equals(info);
        public readonly bool Equals(DiagnosticInfo other)
        {
            return Descriptor.Equals(other.Descriptor) &&
                MessageArgs.SequenceEqual(other.MessageArgs) &&
                Location == other.Location;
        }

        public override readonly int GetHashCode()
        {
            var hashCode = Descriptor.GetHashCode();
            foreach (var messageArg in MessageArgs)
            {
                hashCode = Combine(hashCode, messageArg?.GetHashCode() ?? 0);
            }

            hashCode = Combine(hashCode, Location?.GetHashCode() ?? 0);
            return hashCode;
        }

        private static int Combine(int h1, int h2)
        {
            var rol5 = ((uint)h1 << 5) | ((uint)h1 >> 27);
            return ((int)rol5 + h1) ^ h2;
        }
    }
}
