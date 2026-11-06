namespace Meziantou.Framework.Charts.Tests;

#region Charts_DepartmentRecord
public sealed record Department(string Name, double People, IReadOnlyList<Department> Teams);
#endregion
