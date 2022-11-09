# Meziantou.Framework.StronglyTypedId

The source generator generates ctor / properties / equality members / JSON converters

````csharp
[StronglyTypedId(typeof(int))]
public partial struct ProjectId { }
````

`StronglyTypedId` generates the following code:

````csharp
[System.ComponentModel.TypeConverterAttribute(typeof(ProjectIdTypeConverter))]
[System.Text.Json.Serialization.JsonConverterAttribute(typeof(ProjectIdJsonConverter))]
[Newtonsoft.Json.JsonConverterAttribute(typeof(ProjectIdNewtonsoftJsonConverter))]
[MongoDB.Bson.Serialization.Attributes.BsonSerializerAttribute(typeof(ProjectIdMongoDBBsonSerializer))]
public partial struct ProjectId : System.IEquatable<ProjectId>
{
    public int Value { get; }
    public string ValueAsString { get; }

    private ProjectId(int value);

    public static ProjectId FromInt32(int value);
    public static ProjectId Parse(string value);
    public static ProjectId Parse(ReadOnlySpan<char> value);
    public static bool TryParse(string value, out ProjectId result);
    public static bool TryParse(ReadOnlySpan<char> value, out ProjectId result);
    public override int GetHashCode();
    public override bool Equals(object? other);
    public bool Equals(ProjectId other);
    public static bool operator ==(ProjectId a, ProjectId b);
    public static bool operator !=(ProjectId a, ProjectId b);
    public override string ToString();

    private partial class CustomerIdTypeConverter : System.ComponentModel.TypeConverter
    {
        public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType);
        public override object? ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value);
        public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType);
        public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType);
    }

    // Generated only when System.Text.Json is accessible
    private partial class CustomerIdJsonConverter : System.Text.Json.Serialization.JsonConverter<ProjectId>
    {
        public override void Write(System.Text.Json.Utf8JsonWriter writer, ProjectId value, System.Text.Json.JsonSerializerOptions options);
        public override ProjectId Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options);
    }

    // Generated only when Newtonsoft.Json is accessible
    private partial class CustomerIdNewtonsoftJsonConverter : Newtonsoft.Json.JsonConverter
    {
        public override bool CanRead { get; }
        public override bool CanWrite { get; }
        public override bool CanConvert(System.Type type);
        public override void WriteJson(Newtonsoft.Json.JsonWriter writer, object? value, Newtonsoft.Json.JsonSerializer serializer);
        public override object ReadJson(Newtonsoft.Json.JsonReader reader, System.Type objectType, object? existingValue, Newtonsoft.Json.JsonSerializer serializer);
    }

    // Generated only when MongoDB.Bson.Serialization.Serializers.SerializerBase is accessible
    private partial class ProjectIdMongoDBBsonSerializer : MongoDB.Bson.Serialization.Serializers.SerializerBase<Meziantou.Framework.StronglyTypedId.GeneratorTests.StronglyTypedIdTests.IdBoolean>
    {
        public override ProjectId Deserialize(MongoDB.Bson.Serialization.BsonDeserializationContext context, MongoDB.Bson.Serialization.BsonDeserializationArgs args);
        public override void Serialize(MongoDB.Bson.Serialization.BsonSerializationContext context, MongoDB.Bson.Serialization.BsonSerializationArgs args, ProjectId value);
    }
}
````

# Configuration

You can configure the code generation using the `[StronglyTypedIdAttribute]` attribute:

````c#
[StronglyTypedId(idType: typeof(long),
                 generateSystemTextJsonConverter: true,
                 generateNewtonsoftJsonConverter: true,
                 generateSystemComponentModelTypeConverter: true,
                 generateMongoDBBsonSerialization: true,
                 addCodeGeneratedAttribute: true
                 )]
public partial struct ProjectId { }
````

# Additional resources

- [Strongly-typed Ids using C# Source Generators](https://www.meziantou.net/strongly-typed-ids-with-csharp-source-generators.htm)
