namespace Meziantou.AspNetCore.Components;

internal interface IGenericForm
{
    object? Model { get; }

    public bool EnableFieldValidation { get; }

    public string? EditorClass { get; }

    public string? BaseEditorId { get; }
}
