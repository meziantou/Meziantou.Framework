---
title: Generated forms
description: Generate the editors of a form from the properties of a model, control labels, order and editors with attributes, and customize the layout.
---

`GenericForm<TModel>` generates a label, an editor and a validation message for each property of a model. Put it in
an `EditForm`:

<!-- ref:../../../samples/BlazorAppSample/Documentation/GenericFormExample.razor;format=md-fenced-code-block -->
```razor
@using System.ComponentModel
@using System.ComponentModel.DataAnnotations

<EditForm Model="contact" OnValidSubmit="Save">
    <DataAnnotationsValidator />
    <GenericForm @bind-Model="contact" EditorClass="form-control" />
    <button type="submit">Save</button>
</EditForm>

@code {
    private Contact contact = new();

    private void Save()
    {
        // contact contains the values the user entered
    }

    public sealed class Contact
    {
        [Required]
        [Display(Name = "Full name", Order = 1, Prompt = "Jane Doe")]
        public string? Name { get; set; }

        [DataType(DataType.EmailAddress)]
        [Display(Order = 2)]
        public string? Email { get; set; }

        [DataType(DataType.Date)]
        public DateTime? BirthDate { get; set; }

        public ContactKind Kind { get; set; }

        public bool Subscribed { get; set; }

        [Editor(typeof(InputTextArea), typeof(InputBase<>))]
        public string? Notes { get; set; }

        [Editable(false)]
        public Guid Id { get; set; } = Guid.NewGuid(); // Not shown in the form

        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow; // Read-only: not shown either
    }

    public enum ContactKind
    {
        Personal,
        [Display(Name = "Work contact")]
        Work,
    }
}
```
<!-- endref -->

- `@bind-Model` (or `Model` and `ModelChanged`) gives the instance to edit. `ModelChanged` is raised each time a field
  changes.
- `EditorClass` is the CSS class of every editor, such as `form-control`.
- `EnableFieldValidation` (`true` by default) renders a `ValidationMessage` after each editor. The validation messages
  need the `EditContext` of an `EditForm`.

The form edits the instance in place: a property of `contact` is updated as soon as the user changes its editor.

## Which properties become fields

Every public instance property with a setter becomes a field, including inherited ones. A property is left out when it
has no setter or has `[Editable(false)]`.

| Attribute | Effect |
| --- | --- |
| `[Display(Name = "...")]` or `[DisplayName("...")]` | The label. The property name is used otherwise |
| `[Display(Order = n)]` | The position of the field. Fields without an order come after the ordered ones whose order is below 10000 |
| `[Display(Prompt = "...")]` | The `placeholder` of the editor |
| `[Display(Description = "...")]` or `[Description("...")]` | Available to a custom template as `field.Description` |
| `[Editable(false)]` | Removes the field |
| `[Editor(typeof(MyEditor), typeof(InputBase<>))]` | Uses `MyEditor` as the editor |

## Which editor is used

The editor comes from an `[Editor]` attribute on the property, then from an `[Editor]` attribute on the type of the
property, then from the property type:

| Property type | Editor |
| --- | --- |
| `bool` | `InputCheckbox` |
| `string` | `InputText`. With `[DataType]`: `type="date"`, `"datetime-local"`, `"email"`, `"password"`, `"tel"`, `"time"` or `"url"`, and `InputTextArea` for `DataType.MultilineText` |
| `short`, `int`, `long`, `float`, `double`, `decimal` | `InputNumber` |
| `DateTime`, `DateTimeOffset` | [`InputDateTime`](06-InputComponents.md#dates-with-a-time), or `InputDate` with `[DataType(DataType.Date)]` |
| `Guid` | [`InputGuid`](06-InputComponents.md#guids) |
| `Uri` | [`InputUrl`](06-InputComponents.md#urls) |
| An enum without `[Flags]` | [`InputEnumSelect`](06-InputComponents.md#enums) |
| Anything else, including `[Flags]` enums | A text input |

Nullable value types (`int?`, `DateTime?`, `Guid?`, enums) get the same editor as the type they wrap.

The editor of an `[Editor]` attribute is any component that derives from `InputBase<T>`, for the type of the property.
It receives `Value`, `ValueChanged`, `ValueExpression`, `id`, `class` and `placeholder`.

## Customize the layout

By default, each field renders a `<div>` with the `<label>`, the editor and the validation message. `FieldTemplate`
renders each field yourself; its context is a `GenericFormField<TModel>`:

<!-- ref:../../../samples/BlazorAppSample/Documentation/GenericFormFieldTemplateExample.razor;format=md-fenced-code-block -->
```razor
@using System.ComponentModel.DataAnnotations

<EditForm Model="settings">
    <DataAnnotationsValidator />
    <GenericForm Model="settings" EditorClass="form-control">
        <FieldTemplate Context="field">
            <div class="mb-3">
                <label class="form-label" for="@field.EditorId">@field.DisplayName</label>
                @field.EditorTemplate
                @if (field.Description is not null)
                {
                    <div class="form-text">@field.Description</div>
                }
                @field.FieldValidationTemplate
            </div>
        </FieldTemplate>
    </GenericForm>
</EditForm>

@code {
    private readonly Settings settings = new();

    public sealed class Settings
    {
        [Range(1, 100)]
        [Display(Name = "Page size", Description = "Number of items shown on each page")]
        public int PageSize { get; set; } = 20;

        [Display(Description = "Address of the API, for instance https://api.example.com")]
        public Uri? ApiUrl { get; set; }
    }
}
```
<!-- endref -->

| Member | Content |
| --- | --- |
| `DisplayName`, `Description`, `Prompt`, `DisplayOrder` | The metadata of the attributes above |
| `EditorId` | The `id` of the editor, for the `for` attribute of a label |
| `EditorTemplate` | The editor |
| `FieldValidationTemplate` | The validation message, or `null` when `EnableFieldValidation` is `false` |
| `Property`, `PropertyType`, `Value`, `Owner` | The property, its type, its current value and the model |

The fields are discovered with reflection over `TModel`, and they are rebuilt only when another model instance is
assigned.
