---
title: Input components
description: Edit dates with a time, enums, GUIDs and URLs in a Blazor EditForm, with validation of what the user types.
---

The input components derive from `InputBase<TValue>`: use them in an `EditForm` with `@bind-Value`, like the inputs of
Blazor. A value that cannot be parsed shows a validation message.

<!-- ref:../../../samples/BlazorAppSample/Documentation/InputComponentsExample.razor;format=md-fenced-code-block -->
```razor
<EditForm Model="model">
    <DataAnnotationsValidator />

    <InputDateTime @bind-Value="model.MeetingStart" />
    <InputEnumSelect @bind-Value="model.Priority" EmptyOptionText="(none)" />
    <InputGuid @bind-Value="model.ExternalId" ParsingErrorMessage="{0} must be a GUID such as 3f2504e0-4f89-11d3-9a0c-0305e82c3301." />
    <InputUrl @bind-Value="model.Website" />

    <ValidationSummary />
</EditForm>

@code {
    private readonly Meeting model = new();

    public sealed class Meeting
    {
        public DateTime MeetingStart { get; set; } = new(2025, 3, 14, 9, 30, 0);
        public Priority? Priority { get; set; }
        public Guid ExternalId { get; set; }
        public Uri? Website { get; set; }
    }

    public enum Priority
    {
        Low,
        Normal,
        High,
    }
}
```
<!-- endref -->

## Dates with a time

`InputDateTime<TValue>` renders `<input type="datetime-local">` for a `DateTime`, a `DateTimeOffset`, or their
nullable versions. The browser input has no seconds, so values are truncated to the minute.

A `datetime-local` input has no time zone. For a `DateTimeOffset`, set `Offset` to say in which offset the user types;
without it, the offset of the machine that runs the component is used, which is the offset of the **server** with
Blazor Server. Get the user's offset with [`TimeZoneService`](09-TimeZones.md):

<!-- ref:../../../samples/BlazorAppSample/Documentation/InputDateTimeOffsetExample.razor;format=md-fenced-code-block -->
```razor
@inject TimeZoneService TimeZoneService

<EditForm Model="model">
    <InputDateTime @bind-Value="model.StartsAt" Offset="userOffset" />
</EditForm>

@code {
    private readonly Appointment model = new();
    private TimeSpan? userOffset;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Interpret the value the user types in their own time zone, not in the time zone of the server
            userOffset = await TimeZoneService.GetOffsetAsync();
            StateHasChanged();
        }
    }

    public sealed class Appointment
    {
        public DateTimeOffset? StartsAt { get; set; }
    }
}
```
<!-- endref -->

`Offset` is only used to read what the user types; it has no effect on a `DateTime`.

## Enums

`InputEnumSelect<TEnum>` renders a `<select>` with an option per enum value. The text of an option is the
`[Display(Name = "...")]` of the member, or its name.

For a nullable enum, an empty option comes first, so `null` can be selected; its text is `EmptyOptionText` (empty by
default).

## GUIDs

`InputGuid<TValue>` renders a text input for a `Guid` or a `Guid?`. A value that is not a GUID shows
`ParsingErrorMessage`, `The {0} field must be a valid GUID.` by default, where `{0}` is the field name.

## URLs

`InputUrl<TValue>` renders `<input type="url">` for a `string` or a `Uri`. A `Uri` accepts absolute and relative URLs.
A value that cannot be parsed shows `ParsingErrorMessage`, `The {0} field must be a valid URL.` by default.

URLs with the `javascript:`, `vbscript:` or `data:` scheme are always rejected, even when the value is set without the
browser's validation. A value from this input can be rendered in an `href` without becoming a script.

`InputGuid` and `InputUrl` expose the rendered `<input>` as `Element`, for instance to give it the focus.
