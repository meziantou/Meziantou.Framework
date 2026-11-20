---
title: MAPI properties
description: Read any MAPI property of a folder, a message, a recipient or an attachment, including the named properties.
---

Every object of a file is a set of MAPI properties, identified by a 16-bit identifier and typed: the members of
[PstMessage](cref:Meziantou.Framework.Mail.PstMessage) and of the other types read some of them.
[Properties](cref:Meziantou.Framework.Mail.PstMessage.Properties) gives all of them, as a
[PstPropertyCollection](cref:Meziantou.Framework.Mail.PstPropertyCollection):

<!-- ref:../../../tests/Meziantou.Framework.Mail.Tests/PstFileTests.cs;region=PstPropertyCollection_Read;format=md-fenced-code-block;dedent=true -->
```csharp
using var file = PstFile.Open(path);
var appointment = file.GetFolder("Calendar")!.EnumerateItems().Single();

// A property from its identifier (PidTagMessageClass, 0x001A), whatever its type
Assert.Equal("IPM.Appointment", appointment.Properties.GetValueOrDefault<string>(0x001A));

// A named property from its property set and its identifier (PidLidLocation of PSETID_Appointment)
var location = new PstNamedProperty(PstPropertySets.Appointment, 0x8208);
Assert.NotNull(appointment.Properties.Find(location));

// All the properties, with their tags, their names and their values
foreach (var property in appointment.Properties)
{
    Assert.NotEqual(PstPropertyType.Unspecified, property.Type);
}
```
<!-- endref -->

- [Find](cref:Meziantou.Framework.Mail.PstPropertyCollection.Find) returns a property from its identifier, its tag or its
  name; [GetValueOrDefault](cref:Meziantou.Framework.Mail.PstPropertyCollection.GetValueOrDefault) and
  [TryGetValue](cref:Meziantou.Framework.Mail.PstPropertyCollection.TryGetValue) convert its value.
- The [Value](cref:Meziantou.Framework.Mail.PstProperty.Value) of a property is read when it is first accessed, as the .NET
  type that [PstPropertyType](cref:Meziantou.Framework.Mail.PstPropertyType) documents: strings, dates in UTC, bytes for the
  binary values, arrays for the multi-valued properties. [OpenRead](cref:Meziantou.Framework.Mail.PstProperty.OpenRead) reads
  a large value block by block.
- The 8-bit strings of an ANSI file (and of some properties of the Unicode files) are decoded with the code page of the
  message (PidTagMessageCodepage), or 1252 when it does not have one.
- The subject a property stores can start with the characters 0x01 and the length of its prefix (such as `RE: `):
  [Subject](cref:Meziantou.Framework.Mail.PstMessage.Subject) removes them.

## Named properties

The identifiers from 0x8000 are named properties: the file maps each one to a name, made of a property set (a GUID) and
either a numeric identifier or a string. The identifiers differ from one file to another, so a named property is looked up
by its [PstNamedProperty](cref:Meziantou.Framework.Mail.PstNamedProperty), and
[NamedProperty](cref:Meziantou.Framework.Mail.PstProperty.NamedProperty) gives the name of a property.
[PstPropertySets](cref:Meziantou.Framework.Mail.PstPropertySets) lists the common property sets. The names and the types of
the properties Outlook uses are in the [MS-OXPROPS] specification.
