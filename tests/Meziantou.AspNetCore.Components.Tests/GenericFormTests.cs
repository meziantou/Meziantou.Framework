using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace Meziantou.AspNetCore.Components.Tests;

public sealed class GenericFormTests
{
    // Field validation is enabled by default and ValidationMessage needs an EditContext,
    // so the form is rendered the way it is meant to be used
    private static Task<string> RenderAsync<TModel>(TModel model)
        => BlazorTestRenderer.RenderAsync<FormHost<TModel>>((nameof(FormHost<TModel>.Model), model));

    internal sealed class FormHost<TModel> : ComponentBase
    {
        [Parameter] public TModel? Model { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<EditForm>(0);
            builder.AddComponentParameter(1, nameof(EditForm.Model), Model);
            builder.AddComponentParameter(2, nameof(EditForm.ChildContent), (RenderFragment<EditContext>)(_ => inner =>
            {
                inner.OpenComponent<GenericForm<TModel>>(0);
                inner.AddComponentParameter(1, nameof(GenericForm<TModel>.Model), Model);
                inner.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    // Every type here has no dedicated editor and used to fall back to InputText, which is an InputBase<string>.
    // The value, the change callback and the value expression are built from the property type, so binding them
    // to InputText threw an InvalidCastException and took the whole form down.
    public sealed class UnmappedTypesModel
    {
        public TimeSpan Duration { get; set; }
        public char Initial { get; set; }
        public uint Count { get; set; }
        public byte Level { get; set; }
        public DateOnly Day { get; set; }
        public TimeOnly Time { get; set; }
    }

    [Fact]
    public async Task PropertiesWithoutADedicatedEditorRenderATextInput()
    {
        var html = await RenderAsync(new UnmappedTypesModel { Duration = TimeSpan.FromMinutes(90), Initial = 'a', Count = 3 });

        Assert.Contains("<label for=\"", html);
        Assert.Equal(6, html.Split("type=\"text\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("value=\"01:30:00\"", html);
        Assert.Contains("value=\"a\"", html);
        Assert.Contains("value=\"3\"", html);
    }

    public sealed class NullableEnumModel
    {
        public DayOfWeek? Day { get; set; }
    }

    [Fact]
    public async Task NullableEnumUsesTheEnumSelect()
    {
        var html = await RenderAsync(new NullableEnumModel());

        Assert.Contains("<select", html);
        Assert.Contains("<option value=\"Monday\">Monday</option>", html);
        Assert.Contains("<option value=\"\">", html);
    }

    public sealed class FlagsModel
    {
        public FileAccess Access { get; set; }
    }

    [Fact]
    public async Task FlagsEnumRendersATextInput()
    {
        // A select cannot represent a combination of flags, so these keep using a text editor
        var html = await RenderAsync(new FlagsModel { Access = FileAccess.ReadWrite });

        Assert.Contains("type=\"text\"", html);
        Assert.Contains("value=\"ReadWrite\"", html);
        Assert.DoesNotContain("<select", html);
    }

    public sealed class MappedTypesModel
    {
        public string? Name { get; set; }
        public bool Enabled { get; set; }
        public int Count { get; set; }
        public Guid Id { get; set; }
        public DayOfWeek Day { get; set; }
    }

    [Fact]
    public async Task MappedTypesKeepTheirDedicatedEditors()
    {
        var html = await RenderAsync(new MappedTypesModel());

        Assert.Contains("type=\"checkbox\"", html);
        Assert.Contains("type=\"number\"", html);
        Assert.Contains("<select", html);
    }

    [Fact]
    public async Task NoModelRendersNothing()
    {
        var html = await BlazorTestRenderer.RenderAsync<GenericForm<MappedTypesModel?>>(
            (nameof(GenericForm<MappedTypesModel?>.Model), null));

        Assert.Empty(html.Trim());
    }
}
