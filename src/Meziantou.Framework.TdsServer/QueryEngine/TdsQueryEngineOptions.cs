using System.Collections.ObjectModel;
using System.Xml;
using System.Xml.Schema;
using Meziantou.Framework.Tds.Handler;

namespace Meziantou.Framework.Tds.QueryEngine;

/// <summary>Configures the built-in TDS query engine.</summary>
/// <remarks>
/// Configure the instance fully before passing it to <see cref="TdsQueryEngine.CreateQueryHandler"/>. The
/// collections are snapshotted at that point, so later additions are ignored, and mutating them afterwards races
/// with queries running on other connections.
/// </remarks>
public sealed class TdsQueryEngineOptions
{
    private static readonly TdsQueryEngineAuthorizationHandler DefaultIsAuthorized = static (context, resourceKind, resourceName) =>
    {
        _ = context;
        _ = resourceKind;
        _ = resourceName;
        return true;
    };

    /// <summary>Initializes a new instance of the <see cref="TdsQueryEngineOptions"/> class with the default scalar functions.</summary>
    [RequiresUnreferencedCode(TdsQueryEngine.RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(TdsQueryEngine.RequiresDynamicCodeMessage)]
    public TdsQueryEngineOptions()
    {
        ScalarFunctions = SqlFunctions.CreateDefaultScalarFunctions();
    }

    /// <summary>Gets the stored procedures available to RPC requests.</summary>
    /// <remarks>Stored procedure delegates can declare parameters matching RPC parameters, plus optional injected <see cref="TdsQueryContext" /> and <see cref="CancellationToken" /> parameters.</remarks>
    public IDictionary<string, Delegate> StoredProcedures { get; } = new Dictionary<string, Delegate>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the query roots available to SQL text queries.</summary>
    public Collection<TdsQueryRoot> QueryRoots { get; } = [];

    /// <summary>Gets or sets the materializer used to enumerate translated text queries.</summary>
    public TdsQueryMaterializer MaterializeAsync { get; set; } = DefaultMaterializeAsync;

    /// <summary>
    /// Gets or sets the maximum number of rows a recursive common table expression can return, or <see langword="null"/>
    /// for no limit. A recursive common table expression that returns more rows fails the statement.
    /// </summary>
    /// <remarks>
    /// <c>OPTION (MAXRECURSION n)</c> limits the number of recursion levels, as in SQL Server. This limit also bounds
    /// recursions whose rows grow exponentially, or that run without limit with <c>MAXRECURSION 0</c>.
    /// </remarks>
    public int? MaxRecursiveCommonTableExpressionRows
    {
        get;
        set
        {
            if (value is not null)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value.Value, nameof(value));
            }

            field = value;
        }
    }

    /// <summary>Gets the scalar SQL function mappings used by the query translator.</summary>
    public IDictionary<string, TdsQueryScalarFunction> ScalarFunctions { get; }

    /// <summary>Gets or sets the authorization callback used for stored procedures and query roots.</summary>
    public TdsQueryEngineAuthorizationHandler IsAuthorized { get; set; } = DefaultIsAuthorized;

    /// <summary>Gets or sets the factory used to build permission-denied errors.</summary>
    public Func<TdsQueryContext, TdsQueryEngineResourceKind, string, TdsQueryError> NotAuthorizedErrorFactory { get; set; } = DefaultNotAuthorizedErrorFactory;

    /// <summary>Gets the XML schema collections available to typed XML casts.</summary>
    public IDictionary<string, XmlSchemaSet> XmlSchemaCollections { get; } = new Dictionary<string, XmlSchemaSet>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds a query root factory resolved from the current query request context.</summary>
    public TdsQueryEngineOptions AddQueryRoot<T>(string name, Func<TdsQueryContext, IQueryable<T>> queryFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(queryFactory);

        QueryRoots.Add(new TdsQueryRoot(name, CreateQueryFactory(name, queryFactory)));
        return this;
    }

    /// <summary>
    /// Adds a query root factory resolved from the current query request context, and the handlers of the
    /// <c>INSERT</c>, <c>UPDATE</c> and <c>DELETE</c> statements that target it.
    /// </summary>
    public TdsQueryEngineOptions AddQueryRoot<T>(string name, Func<TdsQueryContext, IQueryable<T>> queryFactory, TdsQueryRootWriteHandlers<T> writeHandlers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(queryFactory);
        ArgumentNullException.ThrowIfNull(writeHandlers);

        QueryRoots.Add(new TdsQueryRoot(name, CreateQueryFactory(name, queryFactory))
        {
            Writer = TdsQueryRootWriter.Create(writeHandlers),
        });
        return this;
    }

    private static Func<TdsQueryContext, IQueryable> CreateQueryFactory<T>(string name, Func<TdsQueryContext, IQueryable<T>> queryFactory)
    {
        return context =>
        {
            ArgumentNullException.ThrowIfNull(context);

            var query = queryFactory(context);
            return query ?? throw new InvalidOperationException($"The query root '{name}' returned null.");
        };
    }

    /// <summary>Adds or replaces a scalar SQL function mapping.</summary>
    public TdsQueryEngineOptions AddScalarFunction(string name, TdsQueryScalarFunction function)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(function);

        ScalarFunctions[name] = function;
        return this;
    }

    /// <summary>Adds or replaces an XML schema collection from an XSD definition string.</summary>
    public TdsQueryEngineOptions AddXmlSchemaCollection(string name, string schemaDefinition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaDefinition);

        using var reader = XmlReader.Create(new StringReader(schemaDefinition));
        var schema = XmlSchema.Read(reader, ValidationCallback)
            ?? throw new TdsQueryEngineException($"Invalid XML schema collection definition for '{name}'.");
        var schemaSet = new XmlSchemaSet();
        schemaSet.Add(schema);
        schemaSet.Compile();

        XmlSchemaCollections[name] = schemaSet;
        return this;
    }

    /// <summary>Adds or replaces an XML schema collection.</summary>
    public TdsQueryEngineOptions AddXmlSchemaCollection(string name, XmlSchemaSet schemaSet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(schemaSet);

        XmlSchemaCollections[name] = schemaSet;
        return this;
    }

    private static ValueTask<IReadOnlyList<object?>> DefaultMaterializeAsync(IQueryable query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var rows = new List<object?>();
        foreach (var row in query)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(row);
        }

        return ValueTask.FromResult<IReadOnlyList<object?>>(rows);
    }

    private static void ValidationCallback(object? sender, ValidationEventArgs args)
    {
        throw new TdsQueryEngineException($"Invalid XML schema collection definition: {args.Message}");
    }

    private static TdsQueryError DefaultNotAuthorizedErrorFactory(TdsQueryContext context, TdsQueryEngineResourceKind resourceKind, string resourceName)
    {
        _ = context;
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        var permission = resourceKind switch
        {
            TdsQueryEngineResourceKind.StoredProcedure => "EXECUTE",
            TdsQueryEngineResourceKind.QueryRoot => "SELECT",
            TdsQueryEngineResourceKind.QueryRootInsert => "INSERT",
            TdsQueryEngineResourceKind.QueryRootUpdate => "UPDATE",
            TdsQueryEngineResourceKind.QueryRootDelete => "DELETE",
            _ => throw new InvalidOperationException($"Unsupported resource kind '{resourceKind}'."),
        };

        return new TdsQueryError
        {
            Number = 229,
            State = 5,
            Class = 14,
            Message = $"The {permission} permission was denied on the object '{resourceName}'.",
        };
    }
}
