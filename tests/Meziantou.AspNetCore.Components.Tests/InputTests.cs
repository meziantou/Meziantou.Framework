using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace Meziantou.AspNetCore.Components.Tests;

public sealed class InputTests
{
    private static Task<string> RenderInputAsync<TComponent, TValue>(TValue value, Expression<Func<TValue>> valueExpression)
        where TComponent : IComponent
        => BlazorTestRenderer.RenderAsync<TComponent>(
            ("Value", value),
            ("ValueExpression", valueExpression));

    private sealed class Holder
    {
        public Guid Id { get; set; }
        public DayOfWeek? Day { get; set; }
        public Uri? Url { get; set; }
        public string? Text { get; set; }
    }

    [Fact]
    public async Task InputGuidRendersATextInput()
    {
        // A GUID is not a URL, so type="url" made the browser mark every correctly filled field as invalid
        var holder = new Holder { Id = Guid.Empty };
        var html = await RenderInputAsync<InputGuid<Guid>, Guid>(holder.Id, () => holder.Id);

        Assert.Contains("type=\"text\"", html);
        Assert.DoesNotContain("type=\"url\"", html);
        Assert.Contains("value=\"00000000-0000-0000-0000-000000000000\"", html);
    }

    [Fact]
    public void InputGuidHasANonEmptyParsingErrorMessage()
    {
        // Formatting an empty message produced an empty validation error, so the field turned red with no explanation
        using var input = new InputGuid<Guid>();
        var message = input.ParsingErrorMessage;

        Assert.NotEmpty(message);
        Assert.Contains("{0}", message);
    }

    [Fact]
    public void InputUrlHasANonEmptyParsingErrorMessage()
    {
        using var input = new InputUrl<Uri>();
        var message = input.ParsingErrorMessage;

        Assert.NotEmpty(message);
        Assert.Contains("{0}", message);
    }

    [Fact]
    public async Task InputEnumSelectRendersAnEmptyOptionForNullableEnums()
    {
        var holder = new Holder();
        var html = await RenderInputAsync<InputEnumSelect<DayOfWeek?>, DayOfWeek?>(holder.Day, () => holder.Day);

        Assert.Contains("<option value=\"\">", html);
        Assert.Contains("<option value=\"Sunday\">Sunday</option>", html);
    }

    [Fact]
    public async Task InputEnumSelectHasNoEmptyOptionForNonNullableEnums()
    {
        var day = DayOfWeek.Monday;
        var html = await BlazorTestRenderer.RenderAsync<InputEnumSelect<DayOfWeek>>(
            ("Value", DayOfWeek.Monday),
            ("ValueExpression", (Expression<Func<DayOfWeek>>)(() => day)));

        Assert.DoesNotContain("<option value=\"\">", html);
        Assert.Equal(7, html.Split("<option", StringSplitOptions.None).Length - 1);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("  javascript:alert(1)")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    public void InputUrlRejectsSchemesThatExecuteScript(string value)
    {
        Assert.False(TryParse<InputUrl<Uri>, Uri>(value, out _));
        Assert.False(TryParse<InputUrl<string>, string>(value, out _));
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("http://example.com/a?b=c")]
    [InlineData("/relative/path")]
    public void InputUrlAcceptsOrdinaryUrls(string value)
    {
        Assert.True(TryParse<InputUrl<Uri>, Uri>(value, out var parsed));
        Assert.Equal(value, parsed?.ToString());
    }

    // TryParseValueFromString is protected, so reach it the way the framework does
    private static bool TryParse<TComponent, TValue>(string value, out TValue? result)
        where TComponent : IDisposable, new()
    {
        using var component = new TComponent();
        var method = typeof(TComponent).GetMethod("TryParseValueFromString", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var args = new object?[] { value, null, null };
        var success = (bool)method.Invoke(component, args)!;
        result = success ? (TValue?)args[1] : default;
        return success;
    }
}
