namespace Meziantou.Framework.Annotations;

[System.Diagnostics.Conditional("StronglyTypedId_Attributes")]
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false)]
public sealed class StronglyTypedIdAttribute : Attribute
{
    /// <summary>
    /// Indicate the type is a strongly-typed id
    /// </summary>
    /// <param name="idType">Type of the generated Value</param>
    /// <param name="generateSystemTextJsonConverter">Specify if the System.Text.Json.Serialization.JsonConverter should be generated</param>
    /// <param name="generateNewtonsoftJsonConverter">Specify if the Newtonsoft.Json.JsonConverter should be generated</param>
    /// <param name="generateSystemComponentModelTypeConverter">Specify if the System.ComponentModel.TypeConverter should be generated</param>
    /// <param name="generateMongoDBBsonSerialization">Specify if the MongoDB.Bson.Serialization.Serializers.SerializerBase{T} should be generated</param>
    /// <param name="addCodeGeneratedAttribute">Add <see cref="System.CodeDom.Compiler.GeneratedCodeAttribute"/> to the generated members</param>
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

    /// <summary>
    /// Gets the type of the generated Value property.
    /// </summary>
    public Type IdType { get; }

    /// <summary>
    /// Gets a value indicating whether a System.Text.Json.Serialization.JsonConverter should be generated.
    /// </summary>
    public bool GenerateSystemTextJsonConverter { get; }

    /// <summary>
    /// Gets a value indicating whether a Newtonsoft.Json.JsonConverter should be generated.
    /// </summary>
    public bool GenerateNewtonsoftJsonConverter { get; }

    /// <summary>
    /// Gets a value indicating whether a System.ComponentModel.TypeConverter should be generated.
    /// </summary>
    public bool GenerateSystemComponentModelTypeConverter { get; }

    /// <summary>
    /// Gets a value indicating whether MongoDB.Bson.Serialization.Serializers.SerializerBase{T} should be generated.
    /// </summary>
    public bool GenerateMongoDBBsonSerialization { get; }

    /// <summary>
    /// Gets a value indicating whether to add <see cref="System.CodeDom.Compiler.GeneratedCodeAttribute"/> to the generated members.
    /// </summary>
    public bool AddCodeGeneratedAttribute { get; }

    /// <summary>
    /// Gets or sets the string comparison method to use for string-based strongly-typed IDs.
    /// </summary>
    public StringComparison StringComparison { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to generate ToString as a record-style output.
    /// </summary>
    public bool GenerateToStringAsRecord { get; set; }
}

#if NET7_0_OR_GREATER
/// <summary>
/// Indicate the type is a strongly-typed id
/// </summary>
/// <typeparam name="T">Type of the generated Value</typeparam>
[System.Diagnostics.Conditional("StronglyTypedId_Attributes")]
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false)]
public sealed class StronglyTypedIdAttribute<T> : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StronglyTypedIdAttribute{T}"/> class.
    /// </summary>
    /// <param name="generateSystemTextJsonConverter">Specify if the System.Text.Json.Serialization.JsonConverter should be generated</param>
    /// <param name="generateNewtonsoftJsonConverter">Specify if the Newtonsoft.Json.JsonConverter should be generated</param>
    /// <param name="generateSystemComponentModelTypeConverter">Specify if the System.ComponentModel.TypeConverter should be generated</param>
    /// <param name="generateMongoDBBsonSerialization">Specify if the MongoDB.Bson.Serialization.Serializers.SerializerBase{T} should be generated</param>
    /// <param name="addCodeGeneratedAttribute">Add <see cref="System.CodeDom.Compiler.GeneratedCodeAttribute"/> to the generated members</param>
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

    /// <summary>
    /// Gets the type of the generated Value property.
    /// </summary>
    public Type IdType { get; }

    /// <summary>
    /// Gets a value indicating whether a System.Text.Json.Serialization.JsonConverter should be generated.
    /// </summary>
    public bool GenerateSystemTextJsonConverter { get; }

    /// <summary>
    /// Gets a value indicating whether a Newtonsoft.Json.JsonConverter should be generated.
    /// </summary>
    public bool GenerateNewtonsoftJsonConverter { get; }

    /// <summary>
    /// Gets a value indicating whether a System.ComponentModel.TypeConverter should be generated.
    /// </summary>
    public bool GenerateSystemComponentModelTypeConverter { get; }

    /// <summary>
    /// Gets a value indicating whether MongoDB.Bson.Serialization.Serializers.SerializerBase{T} should be generated.
    /// </summary>
    public bool GenerateMongoDBBsonSerialization { get; }

    /// <summary>
    /// Gets a value indicating whether to add <see cref="System.CodeDom.Compiler.GeneratedCodeAttribute"/> to the generated members.
    /// </summary>
    public bool AddCodeGeneratedAttribute { get; }

    /// <summary>
    /// Gets or sets the string comparison method to use for string-based strongly-typed IDs.
    /// </summary>
    public StringComparison StringComparison { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to generate ToString as a record-style output.
    /// </summary>
    public bool GenerateToStringAsRecord { get; set; }
}
#endif