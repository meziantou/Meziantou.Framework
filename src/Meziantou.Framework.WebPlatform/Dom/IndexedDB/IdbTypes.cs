using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://w3c.github.io/IndexedDB/#enumdef-idbtransactionmode</summary>
[IdlInterface("IDBTransactionMode", "https://w3c.github.io/IndexedDB/#enumdef-idbtransactionmode")]
public enum IdbTransactionMode
{
    /// <summary>The transaction only reads the records.</summary>
    [IdlMember("readonly", "https://w3c.github.io/IndexedDB/#dom-idbtransactionmode-readonly")]
    ReadOnly,

    /// <summary>The transaction reads and writes the records.</summary>
    [IdlMember("readwrite", "https://w3c.github.io/IndexedDB/#dom-idbtransactionmode-readwrite")]
    ReadWrite,

    /// <summary>The transaction changes the schema of the database (an upgrade transaction), and reads and writes the records.</summary>
    [IdlMember("versionchange", "https://w3c.github.io/IndexedDB/#dom-idbtransactionmode-versionchange")]
    VersionChange,
}

/// <summary>https://w3c.github.io/IndexedDB/#enumdef-idbtransactiondurability</summary>
[IdlInterface("IDBTransactionDurability", "https://w3c.github.io/IndexedDB/#enumdef-idbtransactiondurability")]
public enum IdbTransactionDurability
{
    /// <summary>The default durability of the storage.</summary>
    [IdlMember("default", "https://w3c.github.io/IndexedDB/#dom-idbtransactiondurability-default")]
    Default,

    /// <summary>The changes are flushed to the storage before the transaction completes.</summary>
    [IdlMember("strict", "https://w3c.github.io/IndexedDB/#dom-idbtransactiondurability-strict")]
    Strict,

    /// <summary>The changes are written to the operating system before the transaction completes.</summary>
    [IdlMember("relaxed", "https://w3c.github.io/IndexedDB/#dom-idbtransactiondurability-relaxed")]
    Relaxed,
}

/// <summary>https://w3c.github.io/IndexedDB/#enumdef-idbcursordirection</summary>
[IdlInterface("IDBCursorDirection", "https://w3c.github.io/IndexedDB/#enumdef-idbcursordirection")]
public enum IdbCursorDirection
{
    /// <summary>The records in increasing order of keys, including the duplicates.</summary>
    [IdlMember("next", "https://w3c.github.io/IndexedDB/#dom-idbcursordirection-next")]
    Next,

    /// <summary>The records in increasing order of keys, without the duplicate keys.</summary>
    [IdlMember("nextunique", "https://w3c.github.io/IndexedDB/#dom-idbcursordirection-nextunique")]
    NextUnique,

    /// <summary>The records in decreasing order of keys, including the duplicates.</summary>
    [IdlMember("prev", "https://w3c.github.io/IndexedDB/#dom-idbcursordirection-prev")]
    Prev,

    /// <summary>The records in decreasing order of keys, without the duplicate keys.</summary>
    [IdlMember("prevunique", "https://w3c.github.io/IndexedDB/#dom-idbcursordirection-prevunique")]
    PrevUnique,
}

/// <summary>https://w3c.github.io/IndexedDB/#enumdef-idbrequestreadystate</summary>
[IdlInterface("IDBRequestReadyState", "https://w3c.github.io/IndexedDB/#enumdef-idbrequestreadystate")]
public enum IdbRequestReadyState
{
    /// <summary>The request is not complete.</summary>
    [IdlMember("pending", "https://w3c.github.io/IndexedDB/#dom-idbrequestreadystate-pending")]
    Pending,

    /// <summary>The request is complete.</summary>
    [IdlMember("done", "https://w3c.github.io/IndexedDB/#dom-idbrequestreadystate-done")]
    Done,
}

/// <summary>https://w3c.github.io/IndexedDB/#dictdef-idbdatabaseinfo</summary>
[IdlInterface("IDBDatabaseInfo", "https://w3c.github.io/IndexedDB/#dictdef-idbdatabaseinfo")]
public sealed class IdbDatabaseInfo
{
    /// <summary>Gets the name of the database.</summary>
    [IdlMember("name", "https://w3c.github.io/IndexedDB/#dom-idbdatabaseinfo-name")]
    public string? Name { get; init; }

    /// <summary>Gets the version of the database.</summary>
    [IdlMember("version", "https://w3c.github.io/IndexedDB/#dom-idbdatabaseinfo-version")]
    public ulong? Version { get; init; }
}

/// <summary>https://w3c.github.io/IndexedDB/#dictdef-idbtransactionoptions</summary>
[IdlInterface("IDBTransactionOptions", "https://w3c.github.io/IndexedDB/#dictdef-idbtransactionoptions")]
public sealed class IdbTransactionOptions
{
    /// <summary>Gets the durability hint of the transaction.</summary>
    [IdlMember("durability", "https://w3c.github.io/IndexedDB/#dom-idbtransactionoptions-durability")]
    public IdbTransactionDurability Durability { get; init; }
}

/// <summary>https://w3c.github.io/IndexedDB/#dictdef-idbobjectstoreparameters</summary>
[IdlInterface("IDBObjectStoreParameters", "https://w3c.github.io/IndexedDB/#dictdef-idbobjectstoreparameters")]
public sealed class IdbObjectStoreParameters
{
    /// <summary>Gets the key path of the object store: a string, a list of strings, or null for the out-of-line keys.</summary>
    [IdlMember("keyPath", "https://w3c.github.io/IndexedDB/#dom-idbobjectstoreparameters-keypath")]
    public object? KeyPath { get; init; }

    /// <summary>Gets whether the object store has a key generator.</summary>
    [IdlMember("autoIncrement", "https://w3c.github.io/IndexedDB/#dom-idbobjectstoreparameters-autoincrement")]
    public bool AutoIncrement { get; init; }
}

/// <summary>https://w3c.github.io/IndexedDB/#dictdef-idbindexparameters</summary>
[IdlInterface("IDBIndexParameters", "https://w3c.github.io/IndexedDB/#dictdef-idbindexparameters")]
public sealed class IdbIndexParameters
{
    /// <summary>Gets whether the keys of the index are unique.</summary>
    [IdlMember("unique", "https://w3c.github.io/IndexedDB/#dom-idbindexparameters-unique")]
    public bool Unique { get; init; }

    /// <summary>Gets whether an array key adds a record per item.</summary>
    [IdlMember("multiEntry", "https://w3c.github.io/IndexedDB/#dom-idbindexparameters-multientry")]
    public bool MultiEntry { get; init; }
}

/// <summary>https://w3c.github.io/IndexedDB/#dictdef-idbgetalloptions</summary>
[IdlInterface("IDBGetAllOptions", "https://w3c.github.io/IndexedDB/#dictdef-idbgetalloptions")]
public sealed class IdbGetAllOptions
{
    /// <summary>Gets the key or the key range of the records (an IDL <c>any</c> value: null is undefined).</summary>
    [IdlMember("query", "https://w3c.github.io/IndexedDB/#dom-idbgetalloptions-query")]
    public object? Query { get; init; } = NullValue.Instance;

    /// <summary>Gets the maximum number of records, or null.</summary>
    [IdlMember("count", "https://w3c.github.io/IndexedDB/#dom-idbgetalloptions-count")]
    public uint? Count { get; init; }

    /// <summary>Gets the direction of the records.</summary>
    [IdlMember("direction", "https://w3c.github.io/IndexedDB/#dom-idbgetalloptions-direction")]
    public IdbCursorDirection Direction { get; init; }
}

/// <summary>https://w3c.github.io/IndexedDB/#dictdef-idbversionchangeeventinit</summary>
[IdlInterface("IDBVersionChangeEventInit", "https://w3c.github.io/IndexedDB/#dictdef-idbversionchangeeventinit")]
public sealed class IdbVersionChangeEventInit : EventInit
{
    /// <summary>Gets the previous version of the database.</summary>
    [IdlMember("oldVersion", "https://w3c.github.io/IndexedDB/#dom-idbversionchangeeventinit-oldversion")]
    public ulong OldVersion { get; init; }

    /// <summary>Gets the new version of the database, or null when it is deleted.</summary>
    [IdlMember("newVersion", "https://w3c.github.io/IndexedDB/#dom-idbversionchangeeventinit-newversion")]
    public ulong? NewVersion { get; init; }
}

/// <summary>https://w3c.github.io/IndexedDB/#idbversionchangeevent</summary>
[IdlInterface("IDBVersionChangeEvent", "https://w3c.github.io/IndexedDB/#idbversionchangeevent")]
public sealed class IdbVersionChangeEvent : Event
{
    /// <summary>Creates a version change event with the given type and initialization dictionary.</summary>
    [IdlMember("constructor", "https://w3c.github.io/IndexedDB/#dom-idbversionchangeevent-idbversionchangeevent")]
    public IdbVersionChangeEvent(string type, IdbVersionChangeEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        OldVersion = eventInitDict?.OldVersion ?? 0;
        NewVersion = eventInitDict?.NewVersion;
    }

    /// <summary>Gets the previous version of the database.</summary>
    [IdlMember("oldVersion", "https://w3c.github.io/IndexedDB/#dom-idbversionchangeevent-oldversion")]
    public ulong OldVersion { get; }

    /// <summary>Gets the new version of the database, or null when it is deleted.</summary>
    [IdlMember("newVersion", "https://w3c.github.io/IndexedDB/#dom-idbversionchangeevent-newversion")]
    public ulong? NewVersion { get; }
}

/// <summary>A record snapshot (https://w3c.github.io/IndexedDB/#record-snapshot), returned by <c>getAllRecords()</c>.</summary>
[IdlInterface("IDBRecord", "https://w3c.github.io/IndexedDB/#idbrecord")]
public sealed class IdbRecord
{
    private readonly IIndexedDBValueOperations _operations;
    private readonly IdbKey _key;
    private readonly IdbKey _primaryKey;

    internal IdbRecord(IIndexedDBValueOperations operations, IdbKey key, IdbKey primaryKey, object? value)
    {
        _operations = operations;
        _key = key;
        _primaryKey = primaryKey;
        Value = value;
    }

    /// <summary>Gets the key of the record (a new value each time).</summary>
    [IdlMember("key", "https://w3c.github.io/IndexedDB/#dom-idbrecord-key")]
    public object? Key => _operations.ConvertToValue(_key);

    /// <summary>Gets the key of the record of the object store (a new value each time).</summary>
    [IdlMember("primaryKey", "https://w3c.github.io/IndexedDB/#dom-idbrecord-primarykey")]
    public object? PrimaryKey => _operations.ConvertToValue(_primaryKey);

    /// <summary>Gets the value of the record.</summary>
    [IdlMember("value", "https://w3c.github.io/IndexedDB/#dom-idbrecord-value")]
    public object? Value { get; }
}
