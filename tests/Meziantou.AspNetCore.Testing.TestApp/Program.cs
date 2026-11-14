using Meziantou.AspNetCore.Testing.TestApp;

// A small application: HTML pages, a form that sets a cookie and redirects, a JSON endpoint called by a script
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<GreetingService>();
var app = builder.Build();

app.MapGet("/", () => Html("Home", """
    <h1>Home</h1>
    <a href="/sign-in">Sign in</a>
    """));
app.MapGet("/sign-in", () => Html("Sign in", """
    <form method="post" action="/sign-in">
      <label for="name">Name</label>
      <input id="name" name="name">
      <button type="submit">Sign in</button>
    </form>
    """));
app.MapPost("/sign-in", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();
    context.Response.Cookies.Append("user", form["name"].ToString());
    return Results.Redirect("/profile");
});
app.MapGet("/profile", (HttpContext context, GreetingService greeting) => Html("Profile", $"<p id=greeting>{System.Net.WebUtility.HtmlEncode(greeting.Greet(context.Request.Cookies["user"] ?? ""))}</p>"));
app.MapGet("/api/count", () => Results.Json(new { value = 42 }));
app.MapGet("/client", () => Html("Client", """
    <button id="load">Load</button>
    <output></output>
    <script>
      document.getElementById('load').addEventListener('click', async () => {
        const response = await fetch('/api/count');
        const data = await response.json();
        document.querySelector('output').textContent = 'Count: ' + data.value;
      });
    </script>
    """));

app.Run();

static IResult Html(string title, string body)
    => Results.Content($"<!DOCTYPE html><html><head><title>{title}</title></head><body>{body}</body></html>", "text/html; charset=utf-8");
