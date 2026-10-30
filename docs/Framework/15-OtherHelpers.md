---
title: Other helpers
description: Deterministic GUIDs, optional values, placeholder text, safe handles, tuples, URIs, certificates, encodings, environment variables, hash codes, regular expressions and UTF-8 runes.
---

## Deterministic GUIDs

`DeterministicGuid.Create` computes a name-based GUID (RFC 4122 version 3 or 5): the same namespace and name always give
the same GUID, on every machine. Use it to derive a stable identifier from a natural key, such as a URL or a file path:

<!-- ref:../../tests/Meziantou.Framework.Tests/DeterministicGuidTests.cs;region=DeterministicGuid_Create;format=md-fenced-code-block;dedent=true -->
```csharp
// Generate a GUID from a DNS name
var guid = DeterministicGuid.Create(DeterministicGuid.DnsNamespace, "example.com", DeterministicGuidVersion.Version5);
Assert.Equal(Guid.Parse("cfbff0d1-9375-5685-968c-48ce8b15ae17"), guid);

// The same input will always generate the same GUID
var guid2 = DeterministicGuid.Create(DeterministicGuid.DnsNamespace, "example.com", DeterministicGuidVersion.Version5);
Assert.Equal(guid, guid2);
```
<!-- endref -->

- `DnsNamespace`, `UrlNamespace`, `OidNamespace` and `X500Namespace` are the standard namespaces. Any `Guid` can be a
  namespace: create one for your application to keep its identifiers apart.
- `DeterministicGuidVersion.Version5` hashes with SHA-1, `Version3` with MD5. Prefer version 5, unless you must match
  identifiers created with version 3.
- The name is a string, encoded in UTF-8, or bytes.

## Optional values

`Optional<T>` tells a missing value from a value that is `null`, for instance for the fields of a partial update:

<!-- ref:../../tests/Meziantou.Framework.Tests/TupleExtensionsTests.cs;region=Optional_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var notSet = new Optional<string?>();
var setToNull = new Optional<string?>(null);

// Unlike null, Optional tells a missing value from a value that is null
Assert.False(notSet.HasValue);
Assert.True(setToNull.HasValue);
Assert.NotEqual(notSet, setToNull);

var (hasValue, value) = setToNull;
Assert.True(hasValue);
Assert.Null(value);
```
<!-- endref -->

`new Optional<T>()` and `default` have no value; `new Optional<T>(value)` has one, even when it is `null`. Two optional
values are equal when neither has a value, or when both have equal values. `ToString` returns an empty string when there
is no value.

## Placeholder text

`LoremIpsumGenerator` generates random "lorem ipsum" text, for mock-ups and test data:

<!-- ref:../../tests/Meziantou.Framework.Tests/LoremIpsumGeneratorTests.cs;region=LoremIpsumGenerator_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var sentence = LoremIpsumGenerator.Sentence(wordCount: 5);
Assert.HasCount(5, sentence.Split(' '));
Assert.EndsWith(".", sentence);

// 3 sentences of 8 words
var paragraph = LoremIpsumGenerator.Paragraph(wordCount: 8, sentenceCount: 3);
Assert.HasCount(24, paragraph.Split(' '));

var paragraphs = LoremIpsumGenerator.Paragraphs(wordsPerSentence: 8, sentencesPerParagraph: 3, paragraphCount: 2);
Assert.HasCount(2, paragraphs);
```
<!-- endref -->

A sentence starts with an upper-case letter and ends with a period. A paragraph is sentences separated by spaces, and
`Paragraphs` returns a lazy sequence of paragraphs. The words are random, so the text changes on every call.

## Safe handles in native calls

`CreateHandleScope` gives the raw handle of a `SafeHandle` and keeps the `SafeHandle` from being released until the scope
is disposed, so the handle stays valid while native code uses it:

<!-- ref:../../tests/Meziantou.Framework.Tests/SafeHandleExtensionsTests.cs;region=SafeHandle_CreateHandleScope;format=md-fenced-code-block;dedent=true -->
```csharp
using var safeHandle = File.OpenHandle(Path.GetTempFileName(), FileMode.Open, FileAccess.Read, FileShare.None, FileOptions.DeleteOnClose);

// The SafeHandle cannot be released while the scope is alive
using var scope = safeHandle.CreateHandleScope();
nint handle = scope.Value;
Assert.NotEqual(0, handle); // Use handle safely, for instance in a P/Invoke call
```
<!-- endref -->

`SafeHandleValue` converts implicitly to `nint`. `HasValue` is `true` until the scope is disposed; `Value` throws an
`ObjectDisposedException` afterwards. Creating a scope for an invalid handle throws an `ArgumentException`.

## Tuples

`ToArray` copies the elements of a tuple to an `object?[]`:

<!-- ref:../../tests/Meziantou.Framework.Tests/TupleExtensionsTests.cs;region=TupleExtensions_ToArray;format=md-fenced-code-block;dedent=true -->
```csharp
var tuple = (1, "hello", true);
object?[] array = tuple.ToArray();
Assert.Equal([1, "hello", true], array);
```
<!-- endref -->

## URIs

<!-- ref:../../tests/Meziantou.Framework.Tests/X509CertificateExtensionsTests.cs;region=UriExtensions_IsHttpOrHttps;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.True(new Uri("https://www.example.com/").IsHttpOrHttps());
Assert.False(new Uri("ftp://www.example.com/").IsHttpOrHttps());

// A relative URI has no scheme
Assert.False(new Uri("images/logo.png", UriKind.Relative).IsHttpOrHttps());
```
<!-- endref -->

`IsHttpOrHttps` returns `true` for an absolute URI whose scheme is `http` or `https`, in any case.

## Certificate dates

`X509Certificate2.NotBefore` and `NotAfter` are local times. The `NotBeforeUtc` and `NotAfterUtc` extension properties
return them as `DateTimeOffset` values in UTC, which compare the same way on every machine:

<!-- ref:../../tests/Meziantou.Framework.Tests/X509CertificateExtensionsTests.cs;region=X509CertificateExtensions_UtcDates;format=md-fenced-code-block;dedent=true -->
```csharp
using var rsa = RSA.Create(2048);
var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
using var certificate = request.CreateSelfSigned(
    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));

// NotBefore and NotAfter are local times; NotBeforeUtc and NotAfterUtc do not depend on the time zone
Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), certificate.NotBeforeUtc);
Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), certificate.NotAfterUtc);
```
<!-- endref -->

## UTF-8 without a byte order mark

`Encoding.UTF8` writes a byte order mark at the start of files and streams. `Encoding.UTF8WithoutPreamble`, a static
extension property of `Encoding`, returns a shared UTF-8 encoding that does not:

<!-- ref:../../tests/Meziantou.Framework.Tests/EncodingTests.cs;region=EncodingExtensions_UTF8WithoutPreamble;format=md-fenced-code-block;dedent=true -->
```csharp
var path = Path.GetTempFileName();

// Encoding.UTF8 would write the byte order mark (EF BB BF) before the text
File.WriteAllText(path, "hello", Encoding.UTF8WithoutPreamble);
Assert.Equal("hello"u8.ToArray(), File.ReadAllBytes(path));

File.Delete(path);
```
<!-- endref -->

## Environment variables

`Environment.GetRequiredEnvironmentVariable` and `Environment.GetEnvironmentVariableOrDefault` are static extension
methods of `Environment`:

<!-- ref:../../tests/Meziantou.Framework.Tests/EncodingTests.cs;region=EnvironmentExtensions_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// Static extension members of Environment
var logLevel = Environment.GetEnvironmentVariableOrDefault("SAMPLE_APP_LOG_LEVEL", "Information");
Assert.Equal("Information", logLevel);

// Throws an InvalidOperationException that names the missing variable
Assert.Throws<InvalidOperationException>(() => Environment.GetRequiredEnvironmentVariable("SAMPLE_APP_API_KEY"));
```
<!-- endref -->

## Exceptions with their inner exceptions

`exception.ToString(includeInnerException: true)` returns the `ToString` of the exception followed by the `ToString` of
each inner exception, one per line, so the stack trace of each one is written in full. With `false`, it returns
`exception.ToString()`.

## Hash codes of collections

`AddValues` adds every element of an array, a span or an enumerable to a `HashCode`, in order, optionally with an
equality comparer. Use it in the `GetHashCode` of a type that holds a collection:

<!-- ref:../../tests/Meziantou.Framework.Tests/HashCodeExtensionsTests.cs;region=HashCodeExtensions_AddValues;format=md-fenced-code-block;dedent=true -->
```csharp
var first = new HashCode();
first.Add("order", StringComparer.Ordinal);
first.AddValues(new[] { 1, 2, 3 });

var second = new HashCode();
second.Add("order", StringComparer.Ordinal);
second.AddValues(new List<int> { 1, 2, 3 });

// Each value is added in order, so equal sequences give equal hash codes
Assert.Equal(first.ToHashCode(), second.ToHashCode());
```
<!-- endref -->

A `null` array adds 0, so it does not hash like an empty array.

## Asynchronous regex replacement

`ReplaceAsync` replaces the matches of a regular expression with the result of an asynchronous function, for
replacements that need I/O:

<!-- ref:../../tests/Meziantou.Framework.Tests/RegexExtensionsTests.cs;region=RegexExtensions_ReplaceAsync;format=md-fenced-code-block;dedent=true -->
```csharp
var regex = new Regex(@"\{(?<name>\w+)\}", RegexOptions.None, TimeSpan.FromSeconds(1));

var result = await regex.ReplaceAsync("Hello {name}, you have {count} messages", async match =>
{
    await Task.Yield(); // For instance, read the value from a database
    return match.Groups["name"].Value switch
    {
        "name" => "Ada",
        "count" => "3",
        _ => match.Value,
    };
});

Assert.Equal("Hello Ada, you have 3 messages", result);
```
<!-- endref -->

The function is called for each match, one after the other: the next match waits for the previous replacement.

## Runes of UTF-8 bytes

`EnumerateRunesFromUtf8`, in the [Meziantou.Framework.Text](cref:Meziantou.Framework.Text) namespace, decodes UTF-8 bytes into `Rune` values without
converting them to a string first:

<!-- ref:../../tests/Meziantou.Framework.Tests/Text/Utf8ExtensionsTests.cs;region=Utf8Extensions_EnumerateRunesFromUtf8;format=md-fenced-code-block;dedent=true -->
```csharp
ReadOnlySpan<byte> utf8 = [0x41, 0xC3, 0xA9, 0xFF, 0x42]; // A, then U+00E9 on 2 bytes, an invalid byte, B

var runes = new List<Rune>();
foreach (var rune in utf8.EnumerateRunesFromUtf8())
{
    runes.Add(rune);
}

// Invalid bytes are returned as the replacement character, and the enumeration continues
Assert.Equal([new Rune('A'), new Rune(0xE9), Rune.ReplacementChar, new Rune('B')], runes);
```
<!-- endref -->

Invalid and incomplete sequences are returned as `Rune.ReplacementChar`, and the enumeration continues after them.
