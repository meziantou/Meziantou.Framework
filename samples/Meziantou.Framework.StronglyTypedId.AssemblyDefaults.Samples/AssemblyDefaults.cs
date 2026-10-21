// The readme shows the assembly-level defaults and the types they apply to together
#pragma warning disable MA0048 // File name must match type name
using Meziantou.Framework.Annotations;

#region StronglyTypedId_AssemblyDefaults
[assembly: StronglyTypedIdDefaults(GuidGenerationStrategy = GuidGenerationStrategy.Version7,
                                   GenerateNewtonsoftJsonConverter = false,
                                   GenerateMongoDBBsonSerialization = false,
                                   GenerateSystemComponentModelTypeConverter = false,
                                   GenerateSystemTextJsonConverter = true,
                                   AddCodeGeneratedAttribute = true,
                                   StringComparison = StringComparison.Ordinal,
                                   GenerateToStringAsRecord = true)]

namespace Meziantou.Framework.StronglyTypedId.AssemblyDefaults.Samples;

[StronglyTypedId<Guid>]
public partial struct ProjectId { } // New() uses Guid.CreateVersion7()

[StronglyTypedId<Guid>(GuidGenerationStrategy = GuidGenerationStrategy.Version4)]
public partial struct CustomerId { } // New() uses Guid.NewGuid()
#endregion
