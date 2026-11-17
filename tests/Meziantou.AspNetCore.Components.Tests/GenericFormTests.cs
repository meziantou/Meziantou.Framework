using Meziantou.AspNetCore.Components.Tests.TestComponents;
using Meziantou.AspNetCore.Testing;
using Meziantou.AspNetCore.Testing.Components;
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

    [Fact]
    public async Task Interactive_EditsTheModelAndValidatesIt()
    {
        await using var host = new ComponentTestHost();
        var model = new FormModel();
        var page = await host.RenderAsync<GenericFormPage>(parameters => parameters.Add(c => c.Model, model));

        // The editors are inferred from the property types, with the labels of the display names, in display order
        Assert.Equal(["Full name", "Age", "Subscribe to the newsletter", "Day", "Comment"], page.FindAll("label").Select(label => label.TextContent));
        Assert.IsType<Meziantou.Framework.WebPlatform.Dom.HtmlTextAreaElement>(page.GetByLabel("Comment").GetElement());
        Assert.Equal("checkbox", page.GetByLabel("Subscribe to the newsletter").GetAttribute("type"));

        await page.GetByRole("button", "Save").ClickAsync();
        Assert.Equal(0, page.Instance.SubmitCount);
        Assert.Equal(["The Full name field is required.", "The field Age must be between 1 and 120."], GetValidationMessages(page));

        await page.GetByLabel("Full name").FillAsync("Ada Lovelace");
        await page.GetByLabel("Age").FillAsync("36");
        await page.GetByLabel("Subscribe to the newsletter").CheckAsync();
        await page.GetByLabel("Day").SelectOptionAsync("Friday");
        await page.GetByLabel("Comment").FillAsync("First programmer");
        await page.GetByRole("button", "Save").ClickAsync();

        Assert.Equal(1, page.Instance.SubmitCount);
        Assert.True(page.Instance.ChangeCount >= 5);
        Assert.Empty(GetValidationMessages(page));
        Assert.Equal("Ada Lovelace", model.Name);
        Assert.Equal(36, model.Age);
        Assert.True(model.Subscribe);
        Assert.Equal(DayOfWeek.Friday, model.Day);
        Assert.Equal("First programmer", model.Comment);
    }

    // Since .NET 11, ValidationMessage also renders an empty message element when the field is valid
    private static string?[] GetValidationMessages<TComponent>(RenderedComponent<TComponent> component)
        where TComponent : IComponent
        => [.. component.FindAll(".validation-message").Select(message => message.TextContent).Where(text => !string.IsNullOrEmpty(text))];
}
