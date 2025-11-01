namespace Meziantou.Framework.Annotations;

/// <summary>
/// Indicates that the type should be treated as a strongly-typed identifier and generates code to wrap a primitive value type.
/// The source generator creates methods for conversion, comparison, serialization, and other common operations.
/// </summary>
/// <example>
/// <code>
/// [StronglyTypedId(typeof(int))]
/// public partial struct UserId
/// {
/// }
///
/// // Usage:
/// var userId = UserId.FromInt32(42);
/// var json = JsonSerializer.Serialize(userId);
/// </code>
/// </example>
[System.Diagnostics.Conditional("StronglyTypedId_Attributes")]
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false)]
public sealed class StronglyTypedIdAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="StronglyTypedIdAttribute"/> class.</summary>
    /// <param name="idType">The underlying type of the strongly-typed identifier (e.g., <see cref="int"/>, <see cref="Guid"/>, <see cref="string"/>).</param>
    /// <param name="generateSystemTextJsonConverter">Specifies whether a System.Text.Json.Serialization.JsonConverter should be generated for JSON serialization.</param>
    /// <param name="generateNewtonsoftJsonConverter">Specifies whether a Newtonsoft.Json.JsonConverter should be generated for JSON serialization.</param>
    /// <param name="generateSystemComponentModelTypeConverter">Specifies whether a System.ComponentModel.TypeConverter should be generated for type conversion.</param>
    /// <param name="generateMongoDBBsonSerialization">Specifies whether a MongoDB.Bson.Serialization.Serializers.SerializerBase{T} should be generated for MongoDB BSON serialization.</param>
    /// <param name="addCodeGeneratedAttribute">Specifies whether to add <see cref="System.CodeDom.Compiler.GeneratedCodeAttribute"/> to the generated members.</param>
    public StronglyTypedIdAttribute(Type idType,
                                    bool generateSystemTextJsonConverter = true,
                                    bool generateNewtonsoftJsonConverter = true,
                                    bool generateSystemComponentModelTypeConverter = true,
                                    bool generateMongoDBBsonSerialization = true,
                                    bool addCodeGeneratedAttribute = true)
    {
        IdType = idType;
        GenerateSystemTextJsonConverter = generateSystemTextJsonConverter;
        GenerateNewtonsoftJsonConverter = generateNewtonsoftJsonConverter;
        GenerateSystemComponentModelTypeConverter = generateSystemComponentModelTypeConverter;
        GenerateMongoDBBsonSerialization = generateMongoDBBsonSerialization;
        AddCodeGeneratedAttribute = addCodeGeneratedAttribute;
    }

    /// <summary>Gets the underlying type of the strongly-typed identifier.</summary>
    public Type IdType { get; }

    /// <summary>Gets a value indicating whether a System.Text.Json converter is generated.</summary>
    public bool GenerateSystemTextJsonConverter { get; }

    /// <summary>Gets a value indicating whether a Newtonsoft.Json converter is generated.</summary>
    public bool GenerateNewtonsoftJsonConverter { get; }

    /// <summary>Gets a value indicating whether a System.ComponentModel.TypeConverter is generated.</summary>
    public bool GenerateSystemComponentModelTypeConverter { get; }

    /// <summary>Gets a value indicating whether a MongoDB BSON serializer is generated.</summary>
    public bool GenerateMongoDBBsonSerialization { get; }

    /// <summary>Gets a value indicating whether generated members are marked with GeneratedCodeAttribute.</summary>
    public bool AddCodeGeneratedAttribute { get; }

    /// <summary>Gets or sets the string comparison method to use when the underlying type is <see cref="string"/>.</summary>
    public StringComparison StringComparison { get; set; }

    /// <summary>Gets or sets a value indicating whether the ToString() method should generate output in record format.</summary>
    public bool GenerateToStringAsRecord { get; set; }
}

#if NET7_0_OR_GREATER
/// <summary>
/// Indicates that the type should be treated as a strongly-typed identifier with compile-time type safety.
/// This generic variant provides type checking at compile time and generates code to wrap the specified value type.
/// </summary>
/// <typeparam name="T">The underlying type of the strongly-typed identifier (e.g., <see cref="int"/>, <see cref="Guid"/>, <see cref="string"/>).</typeparam>
/// <example>
/// <code>
/// [StronglyTypedId&lt;Guid&gt;]
/// public partial struct ProductId
/// {
/// }
///
/// // Usage:
/// var productId = ProductId.New();
/// var json = JsonSerializer.Serialize(productId);
/// </code>
/// </example>
[System.Diagnostics.Conditional("StronglyTypedId_Attributes")]
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false)]
public sealed class StronglyTypedIdAttribute<T> : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="StronglyTypedIdAttribute{T}"/> class.</summary>
    /// <param name="generateSystemTextJsonConverter">Specifies whether a System.Text.Json.Serialization.JsonConverter should be generated for JSON serialization.</param>
    /// <param name="generateNewtonsoftJsonConverter">Specifies whether a Newtonsoft.Json.JsonConverter should be generated for JSON serialization.</param>
    /// <param name="generateSystemComponentModelTypeConverter">Specifies whether a System.ComponentModel.TypeConverter should be generated for type conversion.</param>
    /// <param name="generateMongoDBBsonSerialization">Specifies whether a MongoDB.Bson.Serialization.Serializers.SerializerBase{T} should be generated for MongoDB BSON serialization.</param>
    /// <param name="addCodeGeneratedAttribute">Specifies whether to add <see cref="System.CodeDom.Compiler.GeneratedCodeAttribute"/> to the generated members.</param>
    public StronglyTypedIdAttribute(bool generateSystemTextJsonConverter = true,
                                    bool generateNewtonsoftJsonConverter = true,
                                    bool generateSystemComponentModelTypeConverter = true,
                                    bool generateMongoDBBsonSerialization = true,
                                    bool addCodeGeneratedAttribute = true)
    {
        IdType = typeof(T);
        GenerateSystemTextJsonConverter = generateSystemTextJsonConverter;
        GenerateNewtonsoftJsonConverter = generateNewtonsoftJsonConverter;
        GenerateSystemComponentModelTypeConverter = generateSystemComponentModelTypeConverter;
        GenerateMongoDBBsonSerialization = generateMongoDBBsonSerialization;
        AddCodeGeneratedAttribute = addCodeGeneratedAttribute;
    }

    /// <summary>Gets the underlying type of the strongly-typed identifier.</summary>
    public Type IdType { get; }

    /// <summary>Gets a value indicating whether a System.Text.Json converter is generated.</summary>
    public bool GenerateSystemTextJsonConverter { get; }

    /// <summary>Gets a value indicating whether a Newtonsoft.Json converter is generated.</summary>
    public bool GenerateNewtonsoftJsonConverter { get; }

    /// <summary>Gets a value indicating whether a System.ComponentModel.TypeConverter is generated.</summary>
    public bool GenerateSystemComponentModelTypeConverter { get; }

    /// <summary>Gets a value indicating whether a MongoDB BSON serializer is generated.</summary>
    public bool GenerateMongoDBBsonSerialization { get; }

    /// <summary>Gets a value indicating whether generated members are marked with GeneratedCodeAttribute.</summary>
    public bool AddCodeGeneratedAttribute { get; }

    /// <summary>Gets or sets the string comparison method to use when the underlying type is <see cref="string"/>.</summary>
    public StringComparison StringComparison { get; set; }

    /// <summary>Gets or sets a value indicating whether the ToString() method should generate output in record format.</summary>
    public bool GenerateToStringAsRecord { get; set; }
}
#endif