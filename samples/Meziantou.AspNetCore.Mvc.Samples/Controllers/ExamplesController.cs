using Meziantou.AspNetCore.Mvc.Samples.Models;
using Microsoft.AspNetCore.Mvc;

namespace Meziantou.AspNetCore.Mvc.Samples.Controllers;

public sealed class ExamplesController : Controller
{
    public IActionResult InlineImg() => View();

    public IActionResult InlineScript() => View();

    public IActionResult InlineStyle() => View();

    public IActionResult RenderOnPageLoad() => View();

    public IActionResult ShowIf(bool hasErrors) => View(new FormModel { HasErrors = hasErrors });

    public IActionResult Time() => View();
}
