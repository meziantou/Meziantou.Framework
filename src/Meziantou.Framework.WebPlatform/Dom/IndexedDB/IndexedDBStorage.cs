namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The operations of IndexedDB on the values of the realm of a global object.</summary>
internal static class IndexedDBStorage
{
    /// <summary>Returns the operations on the values of the realm of a global object.</summary>
    public static IIndexedDBValueOperations GetOperations(GlobalScope? global)
        => global?.ScriptEngine?.GetIndexedDBValueOperations(global) ?? DotNetIndexedDBValueOperations.Instance;

    /// <summary>Returns the backend and the origin of the databases of a global object, or null when its origin is opaque.</summary>
    public static (IndexedDBBackend Backend, string Origin)? Resolve(GlobalScope global)
        => StorageEndpoints.Resolve(global, global.Options.IndexedDB, static context => context.IndexedDBBackend, static () => new InMemoryIndexedDBBackend());
}
