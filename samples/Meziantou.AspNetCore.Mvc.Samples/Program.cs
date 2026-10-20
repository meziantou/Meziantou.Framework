// The views under Views/Examples are compiled in CI to keep the XML documentation of the tag helpers in sync with the API.
// Run the application and open /Examples/<name> (e.g. /Examples/ShowIf) to see the rendered HTML.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();

var app = builder.Build();
app.UseStaticFiles();
app.MapDefaultControllerRoute();
await app.RunAsync();
