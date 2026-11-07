using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meziantou.Framework.Toml.Samples;

// Examples of src/Meziantou.Framework.Toml/readme.md that read files or need a web server. CI compiles them but does not
// run them; the other examples of the readme are tests of tests/Meziantou.Framework.Toml.Tests.
public static class ReadmeSnippets
{
    public static void ReadAndWriteFiles()
    {
        #region TomlSerializer_Files
        using var stream = File.OpenRead("config.toml");
        var config = TomlSerializer.Deserialize<ServerConfig>(stream);

        using var writer = new StreamWriter("output.toml");
        TomlSerializer.Serialize(writer, config);
        #endregion
    }

    public static void MapConfigurationEndpoint(WebApplication app)
    {
        app.MapPut("/configuration", async (HttpRequest request, HttpResponse response, CancellationToken cancellationToken) =>
        {
            #region TomlSerializer_Async
            var config = await TomlSerializer.DeserializeAsync<ServerConfig>(request.Body, cancellationToken: cancellationToken);
            await TomlSerializer.SerializeAsync(response.Body, config, cancellationToken: cancellationToken);
            #endregion
        });
    }
}
