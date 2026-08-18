namespace Meziantou.Framework.Annotations;

/// <summary>
/// Configures the default values used by the strongly-typed identifier source generator for all the types of the assembly.
/// Options set on <see cref="StronglyTypedIdAttribute"/> or <c>StronglyTypedIdAttribute&lt;T&gt;</c> take precedence over the values defined by this attribute.
/// </summary>
/// <example>
/// <code>
/// [assembly: StronglyTypedIdDefaults(GuidGenerationStrategy = GuidGenerationStrategy.Version7, GenerateNewtonsoftJsonConverter = false)]
///
/// [StronglyTypedId&lt;Guid&gt;]
/// public partial struct ProductId
/// {
/// }
///
/// // Usage:
/// var productId = ProductId.New(); // Uses Guid.CreateVersion7()
/// </code>
/// </example>
[System.Diagnostics.Conditional("StronglyTypedId_Attributes")]
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class StronglyTypedIdDefaultsAttribute : Attribute
{
    /// <summary>Gets or sets the default value indicating whether a System.Text.Json.Serialization.JsonConverter is generated. When the property is not set, the converter is generated.</summary>
    public bool GenerateSystemTextJsonConverter { get; set; }

    /// <summary>Gets or sets the default value indicating whether a Newtonsoft.Json.JsonConverter is generated. When the property is not set, the converter is generated.</summary>
    public bool GenerateNewtonsoftJsonConverter { get; set; }

    /// <summary>Gets or sets the default value indicating whether a System.ComponentModel.TypeConverter is generated. When the property is not set, the converter is generated.</summary>
    public bool GenerateSystemComponentModelTypeConverter { get; set; }

    /// <summary>Gets or sets the default value indicating whether a MongoDB.Bson.Serialization.Serializers.SerializerBase{T} is generated. When the property is not set, the serializer is generated.</summary>
    public bool GenerateMongoDBBsonSerialization { get; set; }

    /// <summary>Gets or sets the default value indicating whether generated members are marked with <see cref="System.CodeDom.Compiler.GeneratedCodeAttribute"/>. When the property is not set, the attribute is added.</summary>
    public bool AddCodeGeneratedAttribute { get; set; }

    /// <summary>Gets or sets the default string comparison method to use when the underlying type is <see cref="string"/>. When the property is not set, <see cref="System.StringComparison.Ordinal"/> is used.</summary>
    public StringComparison StringComparison { get; set; }

    /// <summary>Gets or sets the default value indicating whether the ToString() method generates output in record format. When the property is not set, the record format is used.</summary>
    public bool GenerateToStringAsRecord { get; set; }

    /// <summary>Gets or sets the default strategy used by the generated <c>New()</c> method when the underlying type is <see cref="Guid"/>. When the property is not set, <see cref="GuidGenerationStrategy.Version4"/> is used.</summary>
    public GuidGenerationStrategy GuidGenerationStrategy { get; set; }
}
