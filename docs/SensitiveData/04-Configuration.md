---
title: Configuration
description: Bind secrets from the configuration to SensitiveData properties, and convert strings with the type converter.
---

[SensitiveData\<char\>](cref:Meziantou.Framework.SensitiveData{T}) has a type converter that creates it from a string. The configuration binder of
`Microsoft.Extensions.Configuration` uses it, so a secret read from `appsettings.json`, an environment variable or a
vault binds directly to a [SensitiveData\<char\>](cref:Meziantou.Framework.SensitiveData{T}) property.

## Bind an options class

Declare the secret as a [SensitiveData\<char\>](cref:Meziantou.Framework.SensitiveData{T}) property:

<!-- ref:../../tests/Meziantou.Framework.SensitiveData.Tests/SensitiveDataTests.cs;region=SensitiveData_ConfigurationOptions;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class PaymentOptions
{
    public SensitiveData<char>? ApiKey { get; set; }
}
```
<!-- endref -->

Then bind the section as usual. This example uses an in-memory configuration; `Bind`, `Get<T>` and the options pattern
(`services.Configure<PaymentOptions>(section)`) work the same with any configuration source:

<!-- ref:../../tests/Meziantou.Framework.SensitiveData.Tests/SensitiveDataTests.cs;region=SensitiveData_Configuration;format=md-fenced-code-block;dedent=true -->
```csharp
var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["Payment:ApiKey"] = "my-api-key",
    })
    .Build();

var options = configuration.GetSection("Payment").Get<PaymentOptions>();
Assert.NotNull(options);
Assert.NotNull(options.ApiKey);
Assert.Equal("my-api-key", options.ApiKey.RevealToString());
```
<!-- endref -->

Logging or serializing the options object no longer shows the key. The configuration providers still hold the
original string: [SensitiveData\<char\>](cref:Meziantou.Framework.SensitiveData{T}) protects the copy your application passes around, not the
configuration source.

The options object owns the [SensitiveData\<char\>](cref:Meziantou.Framework.SensitiveData{T}) instances it holds. With the options pattern, the options usually live as
long as the application, and so do the secrets they hold.

## Use the type converter

The converter only converts in one direction:

<!-- ref:../../tests/Meziantou.Framework.SensitiveData.Tests/SensitiveDataTests.cs;region=SensitiveData_TypeConverter;format=md-fenced-code-block;dedent=true -->
```csharp
var converter = TypeDescriptor.GetConverter(typeof(SensitiveData<char>));

// A string converts to SensitiveData<char>...
using var secret = (SensitiveData<char>)converter.ConvertFromInvariantString("my-password")!;
Assert.Equal("my-password", secret.RevealToString());

// ...but SensitiveData<char> never converts back to a string
Assert.Throws<NotSupportedException>(() => converter.ConvertToInvariantString(secret));
```
<!-- endref -->

- A string converts to a [SensitiveData\<char\>](cref:Meziantou.Framework.SensitiveData{T}).
- Converting a [SensitiveData\<T\>](cref:Meziantou.Framework.SensitiveData{T}) to a string throws a `NotSupportedException`, so a component that converts values
  to strings for display cannot reveal the contents by accident.
- Only [SensitiveData\<char\>](cref:Meziantou.Framework.SensitiveData{T}) converts from a string. The converter of another element type, such as
  [SensitiveData\<byte\>](cref:Meziantou.Framework.SensitiveData{T}), reports that it cannot convert from a string.
