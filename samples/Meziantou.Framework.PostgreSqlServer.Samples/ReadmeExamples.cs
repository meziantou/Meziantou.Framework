using Meziantou.Framework.PostgreSql;
using Meziantou.Framework.PostgreSql.Handler;
using Meziantou.Framework.PostgreSql.Hosting;
using Microsoft.AspNetCore.Builder;

namespace PostgreSqlServerSamples;

internal static class ReadmeExamples
{
    public static PostgreSqlServerOptions Tls()
    {
        #region PostgreSqlServer_Tls
        var options = new PostgreSqlServerOptions
        {
            RequireEncryption = true,
            TlsPfxPath = "server.pfx",
            TlsPfxPassword = "…",
        };
        #endregion

        return options;
    }

    public static async Task QuickstartAsync()
    {
        #region PostgreSqlServer_Quickstart
        var options = new PostgreSqlServerOptions
        {
            AuthenticationMethod = PostgreSqlAuthenticationMethod.ScramSha256,
        };
        options.AddTcpListener(port: 5432);

        using var server = new PostgreSqlServer(
            options,
            authenticationHandler: (context, cancellationToken) =>
            {
                // ValidatePassword must be called for SCRAM-SHA-256: it is what computes the server signature.
                var isValid = context.UserName == "app" && context.ValidatePassword("Password123!");
                return ValueTask.FromResult(isValid
                    ? PostgreSqlAuthenticationResult.Success()
                    : PostgreSqlAuthenticationResult.Fail("invalid credentials"));
            },
            queryHandler: (context, cancellationToken) =>
            {
                var resultSet = new PostgreSqlResultSet();
                resultSet.Columns.Add(new PostgreSqlColumn("id", PostgreSqlColumnType.Int32));
                resultSet.Columns.Add(new PostgreSqlColumn("name", PostgreSqlColumnType.Text));

                // A Describe request only needs the columns; the rows are ignored.
                if (context.RequestType != PostgreSqlQueryRequestType.Describe)
                {
                    resultSet.Rows.Add([1, "Meziantou"]);
                }

                var result = new PostgreSqlQueryResult();
                result.ResultSets.Add(resultSet);
                return ValueTask.FromResult(result);
            },
            logger: null);

        await server.StartAsync();
        Console.WriteLine($"Listening on port {server.Ports[0]}");
        Console.ReadLine();
        await server.StopAsync();
        #endregion
    }

    public static void Hosting(string[] args)
    {
        #region PostgreSqlServer_Hosting
        var builder = WebApplication.CreateBuilder(args);
        builder.AddPostgreSqlServer(options =>
        {
            options.AuthenticationMethod = PostgreSqlAuthenticationMethod.ScramSha256;
            options.AddTcpListener(port: 5432);
        });

        var app = builder.Build();
        app.MapPostgreSqlAuthenticationHandler((context, cancellationToken) => ValueTask.FromResult(context.ValidatePassword("Password123!")
            ? PostgreSqlAuthenticationResult.Success()
            : PostgreSqlAuthenticationResult.Fail("invalid credentials")));
        app.MapPostgreSqlQueryHandler((context, cancellationToken) => ValueTask.FromResult(new PostgreSqlQueryResult()));

        app.Run();
        #endregion
    }
}
