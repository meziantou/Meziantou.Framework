# Meziantou.Framework.HumanReadableSerializer

One-way serializer to an invariant human-readable format. You can use `HumanReadableSerializer.Serialize` to convert an object to a string:

````c#
var obj = new { Name = "Gérald Barré", Nickname = "meziantou", Age = 32, MultiLineString = "line1\nline2" };
var output = HumanReadableSerializer.Serialize(obj);
````

`output` contains the following value:

````
Name: Gérald Barré
Nickname: meziantou
Age: 32
MultiLineString:
  line1
  line2
````

You can customize the serializer using an instance of `HumanReadableSerializerOptions` or by using attributes:

````c#
var options = new HumanReadableSerializerOptions()
{
    IncludeFields = true,
    DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingNull,
};
HumanReadableSerializer.Serialize(obj, options);
````

Available attributes:
- `[HumanReadableIgnore]` allows to ignore a property or a field
- `[HumanReadableIncludeAttribute]` allows to include a property or a field
- `[HumanReadablePropertyNameAttribute]` allows to set the name of a property or a field when serializing the value
- `[HumanReadablePropertyOrderAttribute]` allows to order the properties when serializing an object
- `[HumanReadableConverterAttribute]` allows to set the converter to use to serialize a property, a field, or a type

You can customize a type you don't own by using the following:

````c#
var options = new HumanReadableSerializerOptions();

// Add the HumanReadableIgnoreAttribute to the User.DisplayName property
options.AddAttribute(typeof(User), "DisplayName", new HumanReadableIgnoreAttribute());

options.IgnoreMember<User>(x => x.DisplayName);
options.IgnoreMember<User>(x => new { x.DisplayName, x.CreatedAt });
````
