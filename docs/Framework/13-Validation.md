---
title: Validation attributes
description: Validate that a string is an absolute URI, and validate the items of a collection, with data annotations.
---

The [Meziantou.Framework.DataAnnotations](cref:Meziantou.Framework.DataAnnotations) namespace contains two validation attributes. They work wherever data
annotations are checked: `Validator.TryValidateObject`, ASP.NET Core model validation, or options validation with
`ValidateDataAnnotations`.

## Absolute URIs

`[AbsoluteUri]` checks that a string property holds an absolute URI:

<!-- ref:../../tests/Meziantou.Framework.Tests/DataAnnotations/AbsoluteUriAttributeTests.cs;region=AbsoluteUriAttribute_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class MyModel
{
    [AbsoluteUri]
    public string? Url { get; set; }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Tests/DataAnnotations/AbsoluteUriAttributeTests.cs;region=AbsoluteUriAttribute_Validate;format=md-fenced-code-block;dedent=true -->
```csharp
var attribute = new AbsoluteUriAttribute();

Assert.True(attribute.IsValid("https://www.example.com/"));
Assert.False(attribute.IsValid("images/logo.png"));

// null and values that are not strings are valid: add [Required] to reject null
Assert.True(attribute.IsValid(null));

var result = attribute.GetValidationResult("images/logo.png", new ValidationContext(new object()));
Assert.NotNull(result);
Assert.Equal("Uri 'images/logo.png' is not an absolute URI", result.ErrorMessage);
```
<!-- endref -->

- A string is valid when `Uri.TryCreate(value, UriKind.Absolute, out _)` succeeds. The attribute accepts any scheme, not
  only `http` and `https`: check the scheme in your code, for instance with `IsHttpOrHttps` (see
  [Other helpers](15-OtherHelpers.md#uris)).
- `null` and values that are not strings are valid. Add `[Required]` to reject a missing value.
- The error message is `Uri '<value>' is not an absolute URI`. The message passed to the constructor
  (`[AbsoluteUri("...")]`) is not used by `Validator`.

On Linux and macOS, a path that starts with `/`, such as `/images/logo.png`, is an absolute `file://` URI, so it is valid.

## Collection items

Data annotations do not validate the objects referenced by a property. `[ValidateCollectionItems]` validates each item
of a collection with its own data annotations:

<!-- ref:../../tests/Meziantou.Framework.Tests/DataAnnotations/ValidateCollectionItemsAttributeTests.cs;region=ValidateCollectionItemsAttribute_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class MyModel
{
    // Validates each item using the data annotations of ChildModel
    [ValidateCollectionItems]
    public List<ChildModel>? Items { get; set; }
}

public sealed class ChildModel
{
    [Required]
    public string? Name { get; set; }
}
```
<!-- endref -->

With these classes, an item without a name makes the model invalid:

<!-- ref:../../tests/Meziantou.Framework.Tests/DataAnnotations/ValidateCollectionItemsAttributeTests.cs;region=ValidateCollectionItemsAttribute_TryValidateObject;format=md-fenced-code-block;dedent=true -->
```csharp
var model = new MyModel
{
    Items = [new ChildModel { Name = "valid" }, new ChildModel { Name = null }],
};

var results = new List<ValidationResult>();
var isValid = Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);

// The result is the first error of the first invalid item
Assert.False(isValid);
var error = Assert.Single(results);
Assert.Equal("The Name field is required.", error.ErrorMessage);
```
<!-- endref -->

- Each item is validated with `Validator.TryValidateObject` and all its properties.
- The validation stops at the first invalid item, and reports its first error, with the message of the attribute that
  failed. It does not tell which item is invalid.
- A `null` collection is valid. Add `[Required]` to reject it.
- The items are validated one level deep: a collection inside an item needs its own `[ValidateCollectionItems]`.

`ValidateCollectionItemsAttribute` discovers the properties of the items at run time, so it is not compatible with
trimming.
