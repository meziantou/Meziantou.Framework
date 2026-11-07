# Meziantou.Framework.HumanReadableSerializer

One-way serializer to an invariant human-readable format. You can use `HumanReadableSerializer.Serialize` (in the `Meziantou.Framework.HumanReadable` namespace) to convert an object to a string:

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/HumanReadableSerializerOptionsTests.cs;region=HumanReadableSerializer_Serialize;format=md-fenced-code-block;dedent=true -->
```csharp
var obj = new { Name = "Gérald Barré", Nickname = "meziantou", Age = 32, MultiLineString = "line1\nline2" };
var output = HumanReadableSerializer.Serialize(obj);
```
<!-- endref -->

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

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/HumanReadableSerializerOptionsTests.cs;region=HumanReadableSerializer_Options;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HumanReadableSerializerOptions()
{
    IncludeFields = true,
    DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingNull,
};
HumanReadableSerializer.Serialize(obj, options);
```
<!-- endref -->

Lines are separated by `\n` on every platform, including line breaks inside multi-line values. Set `NewLine = "\r\n"` to use Windows line endings instead.

Available attributes:
- `[HumanReadableIgnore]` allows to ignore a property or a field
- `[HumanReadableIncludeAttribute]` allows to include a property or a field
- `[HumanReadablePropertyNameAttribute]` allows to set the name of a property or a field when serializing the value
- `[HumanReadablePropertyOrderAttribute]` allows to order the properties when serializing an object
- `[HumanReadableConverterAttribute]` allows to set the converter to use to serialize a property, a field, or a type

You can customize a type you don't own, such as a `User` class with `DisplayName` and `CreatedAt` properties, by using the following:

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/HumanReadableSerializerOptionsTests.cs;region=HumanReadableSerializer_TypeYouDoNotOwn;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HumanReadableSerializerOptions();

// Add the HumanReadableIgnoreAttribute to the User.DisplayName property
options.AddAttribute(typeof(User), "DisplayName", new HumanReadableIgnoreAttribute());

options.IgnoreMember<User>(x => x.DisplayName);
options.IgnoreMember<User>(x => new { x.DisplayName, x.CreatedAt });
```
<!-- endref -->
