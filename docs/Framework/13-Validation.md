---
title: Validation attributes
description: Validate that a string is an absolute URI, and validate the items of a collection, with data annotations.
---

The [Meziantou.Framework.DataAnnotations](cref:Meziantou.Framework.DataAnnotations) namespace contains two validation attributes. They work wherever data
annotations are checked: `Validator.TryValidateObject`, ASP.NET Core model validation, or options validation with
`ValidateDataAnnotations`.

## Absolute URIs

[\[AbsoluteUri\]](cref:Meziantou.Framework.DataAnnotations.AbsoluteUriAttribute) checks that a string property holds an absolute URI:

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

- A string is valid when `Uri.TryCreate(value, UriKind.Absolute, out _)` succeeds and the value starts with a scheme. The
  attribute accepts any scheme, not only `http` and `https`: check the scheme in your code, for instance with
  [IsHttpOrHttps](cref:Meziantou.Framework.UriExtensions.IsHttpOrHttps) (see [Other helpers](15-OtherHelpers.md#uris)).
- File paths, such as `/images/logo.png`, `C:\images\logo.png` or `\\server\share`, are not valid, although `Uri` reads
  them as `file://` URIs. Write `file:///images/logo.png` to accept a file.
- `null` and values that are not strings are valid. Add `[Required]` to reject a missing value.
- The default error message is `Uri '<value>' is not an absolute URI`. A message passed to the constructor
  (`[AbsoluteUri("{0} must be an absolute URI")]`), to `ErrorMessage`, or through `ErrorMessageResourceName` replaces it;
  `{0}` is the display name of the property.
- The result has the name of the property in `MemberNames`, so ASP.NET Core and Blazor show the error next to the field.

## Collection items

Data annotations do not validate the objects referenced by a property. [\[ValidateCollectionItems\]](cref:Meziantou.Framework.DataAnnotations.ValidateCollectionItemsAttribute) validates each item
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

// The result is the first error, and its member name tells which item is invalid
Assert.False(isValid);
var error = Assert.Single(results);
Assert.Equal("The Name field is required.", error.ErrorMessage);
Assert.Equal(["Items[1].Name"], error.MemberNames);
```
<!-- endref -->

- Each item is validated with `Validator.TryValidateObject` and all its properties. The validation context of each item
  has the service provider and the `Items` of the context of the model.
- Every item is validated. The attribute reports one [CollectionItemsValidationResult](cref:Meziantou.Framework.DataAnnotations.CollectionItemsValidationResult): its message and member
  name are those of the first error, and its [Errors](cref:Meziantou.Framework.DataAnnotations.CollectionItemsValidationResult.Errors) property holds the errors of all the items.
- Member names are prefixed with the name of the collection and the index of the item, such as `Items[1].Name`. An
  error without a member name, such as one from `IValidatableObject`, gets the path of the item, such as `Items[1]`.
- With a custom message (`[ValidateCollectionItems(ErrorMessage = "{0} contains invalid items")]`), the result has
  this message and the member names of all the errors.
- A `null` collection is valid, and `null` items are skipped. Add `[Required]` to reject a missing collection.
- The items are validated one level deep: a collection inside an item needs its own [\[ValidateCollectionItems\]](cref:Meziantou.Framework.DataAnnotations.ValidateCollectionItemsAttribute).

[ValidateCollectionItemsAttribute](cref:Meziantou.Framework.DataAnnotations.ValidateCollectionItemsAttribute) discovers the properties of the items at run time, so it is not compatible with
trimming.
