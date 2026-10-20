using System.Net;
using System.Security.Claims;
using Meziantou.Framework.Tds;
using Meziantou.Framework.Tds.Handler;
using Meziantou.Framework.Tds.Hosting;
using Meziantou.Framework.Tds.QueryEngine;
using Microsoft.AspNetCore.Builder;

namespace TdsServerSamples;

internal static class ReadmeExamples
{
    public static void Run(string[] args)
    {
        #region TdsServer_Usage
        var builder = WebApplication.CreateBuilder(args);
        builder.AddTdsServer(options =>
        {
            options.AddTcpListener(port: 14330, bindAddress: IPAddress.Loopback);
        });

        var app = builder.Build();
        app.MapTdsHandlers(
            authenticate: (context, cancellationToken) =>
            {
                if (context.UserName == "sa" && context.Password == "Password123!")
                {
                    var identity = new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "42"),
                        new Claim(ClaimTypes.Name, context.UserName ?? "sa"),
                    ],
                    authenticationType: "password");
                    return ValueTask.FromResult(TdsAuthenticationResult.Success(userContext: new ClaimsPrincipal(identity)));
                }

                return ValueTask.FromResult(TdsAuthenticationResult.Fail("Login failed"));
            },
            query: (context, cancellationToken) =>
            {
                var user = context.UserContext;

                var resultSet = new TdsResultSet();
                resultSet.Columns.Add(new TdsColumn("Message", TdsColumnType.NVarChar));
                resultSet.Rows.Add(["Hello from TDS server"]);

                var result = new TdsQueryResult();
                result.ResultSets.Add(resultSet);
                return ValueTask.FromResult(result);
            });

        app.Run();
        #endregion
    }

    public static TdsQueryEngineOptions QueryEngine(WebApplication app)
    {
        #region TdsServer_QueryEngine
        var customers = new[]
        {
            new Customer(1, "Alice"),
            new Customer(2, "Bob"),
        }.AsQueryable();

        var queryEngineOptions = new TdsQueryEngineOptions();
        queryEngineOptions.AddQueryRoot(
            "customers",
            context =>
            {
                if (int.TryParse(context.UserContext?.FindFirstValue(ClaimTypes.NameIdentifier), CultureInfo.InvariantCulture, out var userId))
                {
                    return customers.Where(customer => customer.Id == userId);
                }

                return customers;
            });
        queryEngineOptions.StoredProcedures.Add(
            "GetCustomer",
            (TdsQueryContext context, int id) =>
            {
                var userIdClaim = context.UserContext?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!int.TryParse(userIdClaim, CultureInfo.InvariantCulture, out var userId))
                {
                    return Array.Empty<Customer>().AsQueryable();
                }

                return customers.Where(customer => customer.Id == id && customer.Id == userId);
            });

        app.MapTdsAuthenticationHandler((context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success()));
        app.MapTdsQueryEngine(queryEngineOptions);
        #endregion

        return queryEngineOptions;
    }

    public static void Authorization(TdsQueryEngineOptions queryEngineOptions)
    {
        #region TdsServer_Authorization
        queryEngineOptions.IsAuthorized = (context, resourceKind, resourceName) =>
        {
            var userId = context.UserContext?.FindFirstValue(ClaimTypes.NameIdentifier);

            if (resourceKind == TdsQueryEngineResourceKind.StoredProcedure &&
                string.Equals(resourceName, "AdminOnlyProc", StringComparison.OrdinalIgnoreCase))
            {
                return userId == "1";
            }

            if (resourceKind == TdsQueryEngineResourceKind.QueryRoot &&
                string.Equals(resourceName, "admin_customers", StringComparison.OrdinalIgnoreCase))
            {
                return userId == "1";
            }

            return true;
        };
        #endregion
    }

    public static void AccessRequest(WebApplication app)
    {
        #region TdsServer_AccessRequest
        app.MapTdsHandlers(
            authenticate: (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success()),
            query: (context, cancellationToken) =>
            {
                if (context.RequestType == TdsQueryRequestType.SqlBatch)
                {
                    var commandText = context.CommandText; // e.g. "SELECT * FROM Users WHERE Id = @id"
                }
                else if (context.RequestType == TdsQueryRequestType.Rpc)
                {
                    var procedureName = context.ProcedureName; // e.g. "sp_executesql" or your procedure name
                }

                foreach (var parameter in context.Parameters)
                {
                    var parameterName = parameter.Name;
                    var rawValue = parameter.Value; // DBNull.Value for SQL NULL
                    var columnType = parameter.Type;
                    var stringValue = parameter.AsString();
                    var intValue = parameter.AsInt32();
                    var json = parameter.AsJson();
                    var xml = parameter.AsXml();
                }

                var user = context.UserContext; // ClaimsPrincipal from authentication result, if any

                return ValueTask.FromResult(new TdsQueryResult());
            });
        #endregion
    }

    public static void TlsPfx(WebApplicationBuilder builder)
    {
        #region TdsServer_TlsPfx
        builder.AddTdsServer(options =>
        {
            options.AddTcpListener(port: 14330, bindAddress: IPAddress.Loopback);
            options.TlsPfxPath = "certificates/server.pfx";
            options.TlsPfxPassword = "Password123!";
        });
        #endregion
    }

    public static void TlsPem(WebApplicationBuilder builder)
    {
        #region TdsServer_TlsPem
        builder.AddTdsServer(options =>
        {
            options.AddTcpListener(port: 14330, bindAddress: IPAddress.Loopback);
            options.TlsPemCertificatePath = "certificates/server.crt.pem";
            options.TlsPemPrivateKeyPath = "certificates/server.key.pem";
        });
        #endregion
    }
}
