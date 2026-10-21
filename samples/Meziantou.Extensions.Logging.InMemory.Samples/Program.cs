using Microsoft.AspNetCore.Builder;

// A minimal web application, so the readme examples (src/Meziantou.Extensions.Logging.InMemory/readme.md) can create a
// WebApplicationFactory<Program>. The examples are compiled in CI to keep the documentation in sync with the API.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/", () => "Hello world");
app.Run();
