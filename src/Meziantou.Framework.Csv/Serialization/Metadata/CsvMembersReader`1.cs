using System.ComponentModel;

namespace Meziantou.Framework.Csv.Serialization.Metadata;

/// <summary>Sets the members of an instance from the values of the current record of UTF-16 data. The source generator writes one for each type.</summary>
/// <typeparam name="T">The type of the instance.</typeparam>
/// <param name="obj">The instance.</param>
/// <param name="values">The values of the record.</param>
/// <remarks>
/// The method calls <see cref="CsvMemberValues{T}.TryRead{TValue}"/> for each member that it sets, in the order of
/// <see cref="CsvObjectInfoValues{T}.Properties"/>, and sets the member when it returns <see langword="true"/>.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate void CsvMembersReader<T>(ref T obj, ref CsvMemberValues<T> values);
