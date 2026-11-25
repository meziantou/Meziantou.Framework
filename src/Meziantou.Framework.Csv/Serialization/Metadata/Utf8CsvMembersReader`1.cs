using System.ComponentModel;

namespace Meziantou.Framework.Csv.Serialization.Metadata;

/// <summary>Sets the members of an instance from the values of the current record of UTF-8 data. The source generator writes one for each type.</summary>
/// <typeparam name="T">The type of the instance.</typeparam>
/// <param name="obj">The instance.</param>
/// <param name="values">The values of the record.</param>
/// <remarks>This is <see cref="CsvMembersReader{T}"/> for UTF-8 data.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate void Utf8CsvMembersReader<T>(ref T obj, ref Utf8CsvMemberValues<T> values);
