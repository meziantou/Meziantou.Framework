# Meziantou.Framework.StronglyTypedId

The source generator generates constructors, properties, equality members, common interfaces, converters for multiple serializers

````csharp
[StronglyTypedId<int>]
public partial struct ProjectId { }
````

or using `typeof` if you're not using a version of C# that supports [generic attributes](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-11#generic-attributes)
````csharp
[StronglyTypedId(typeof(int))]
public partial struct ProjectId { }
````

`StronglyTypedId` generates the following code:

<!-- generated code -->

````csharp
[System.ComponentModel.TypeConverterAttribute(typeof(ProjectIdTypeConverter))]
[System.Text.Json.Serialization.JsonConverterAttribute(typeof(ProjectIdJsonConverter))]
[Newtonsoft.Json.JsonConverterAttribute(typeof(ProjectIdNewtonsoftJsonConverter))]
[MongoDB.Bson.Serialization.Attributes.BsonSerializerAttribute(typeof(ProjectIdMongoDBBsonSerializer))]
public partial struct ProjectId :
    System.IEquatable<ProjectId>,
    System.IParsable<ProjectId>,        // .NET 7+
    System.ISpanParsable<ProjectId>,    // .NET 7+
    IStronglyTypedId,                   // When Meziantou.Framework.StronglyTypedId.Interfaces is referenced
    IStronglyTypedId<ProjectId>,        // When Meziantou.Framework.StronglyTypedId.Interfaces is referenced
    IComparable, IComparable<ProjectId> // When at least one of the interface is explicitly defined by the user
{
    public int Value { get; }
    public string ValueAsString { get; } // Value formatted using InvariantCulture

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
    private partial class ProjectIdMongoDBBsonSerializer : MongoDB.Bson.Serialization.Serializers.SerializerBase<ProjectId>
    {
        public override ProjectId Deserialize(MongoDB.Bson.Serialization.BsonDeserializationContext context, MongoDB.Bson.Serialization.BsonDeserializationArgs args);
        public override void Serialize(MongoDB.Bson.Serialization.BsonSerializationContext context, MongoDB.Bson.Serialization.BsonSerializationArgs args, ProjectId value);
    }
}
````

<!-- generated code -->

If the `Meziantou.Framework.StronglyTypedId.Interfaces` NuGet package is present, the generator will implements `IStronglyTypedId` and `IStronglyTypedId<T>`.

# Supported types

<!-- supported types -->

- `System.Boolean`
- `System.Byte`
- `System.DateTime`
- `System.DateTimeOffset`
- `System.Decimal`
- `System.Double`
- `System.Guid`
- `System.Half`
- `System.Int16`
- `System.Int32`
- `System.Int64`
- `System.Int128`
- `System.Numerics.BigInteger`
- `System.SByte`
- `System.Single`
- `System.String`
- `System.Uint16`
- `System.Uint32`
- `System.Uint64`
- `System.Uint128`
- `MongoDB.Bson.ObjectId`

<!-- supported types -->

# Configuration

<!-- configuration -->

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

You can generate `IComparable`, `IComparable<T>` and comparison operators by adding one interface:

````c#
[StronglyTypedId<int>]
public partial struct ProjectId : IComparable { }

// Generated by the source generator
public partial struct ProjectId : IComparable<ProjectId>
{
	public int CompareTo(object? other);
	public int CompareTo(ProjectId? other);
	public static bool operator <(ProjectId? left, ProjectId? right);
	public static bool operator <=(IdInt32Comparable? left, IdInt32Comparable? right);
	public static bool operator >(IdInt32Comparable? left, IdInt32Comparable? right);
	public static bool operator >=(IdInt32Comparable? left, IdInt32Comparable? right);
}
````

<!-- configuration -->

# Additional resources

- [Strongly-typed Ids using C# Source Generators](https://www.meziantou.net/strongly-typed-ids-with-csharp-source-generators.htm)
