namespace Meziantou.Framework.Annotations;

[System.Diagnostics.Conditional("StronglyTypedId_Attributes")]
[System.AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false)]
public sealed class StronglyTypedIdAttribute : System.Attribute
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
    public StronglyTypedIdAttribute(System.Type idType,
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

    public Type IdType { get; }
    public bool GenerateSystemTextJsonConverter { get; }
    public bool GenerateNewtonsoftJsonConverter { get; }
    public bool GenerateSystemComponentModelTypeConverter { get; }
    public bool GenerateMongoDBBsonSerialization { get; }
    public bool AddCodeGeneratedAttribute { get; }
    public StringComparison StringComparison { get; set; }
    public bool GenerateToStringAsRecord { get; set; }
}

#if NET7_0_OR_GREATER
/// <summary>
/// Indicate the type is a strongly-typed id
/// </summary>
/// <typeparam name="T">Type of the generated Value</typeparam>
[System.Diagnostics.Conditional("StronglyTypedId_Attributes")]
[System.AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false)]
public sealed class StronglyTypedIdAttribute<T> : System.Attribute
{
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

    public Type IdType { get; }
    public bool GenerateSystemTextJsonConverter { get; }
    public bool GenerateNewtonsoftJsonConverter { get; }
    public bool GenerateSystemComponentModelTypeConverter { get; }
    public bool GenerateMongoDBBsonSerialization { get; }
    public bool AddCodeGeneratedAttribute { get; }
    public StringComparison StringComparison { get; set; }
    public bool GenerateToStringAsRecord { get; set; }
}
#endif