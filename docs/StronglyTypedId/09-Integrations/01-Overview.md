---
title: Integrations
description: Serialize, store and bind IDs with System.Text.Json, Newtonsoft.Json, MongoDB, TypeConverter, ASP.NET Core and Entity Framework Core.
---

An ID is read and written as its underlying value everywhere: a number or a string in JSON, a value in a BSON document
or a database column, a segment of a URL. The generator writes a converter for each serializer, nested in the ID and
registered by an attribute on it, so most libraries use them without configuration.

| Library | What the generator provides | Configuration |
| --- | --- | --- |
| [System.Text.Json](02-SystemTextJson.md) | A `JsonConverter<T>`, also for dictionary keys | None, except with a source-generated `JsonSerializerContext` |
| [Newtonsoft.Json](03-NewtonsoftJson.md) | A `JsonConverter`, when the project references Newtonsoft.Json | None |
| [MongoDB](04-MongoDB.md) | A BSON serializer, when the project references MongoDB.Bson | None |
| [TypeConverter and configuration](05-TypeConverterAndConfiguration.md) | A `TypeConverter`, used by `Microsoft.Extensions.Configuration` and UI frameworks | None |
| [ASP.NET Core](06-AspNetCore.md) | `TryParse` and `IParsable<T>` for route and query values, and the JSON converter for bodies | None |
| [Entity Framework Core](07-EntityFrameworkCore.md) | Nothing specific | A value converter that you write once per ID type |

Each converter can be turned off with an [option](../07-Options.md#choose-the-converters).
