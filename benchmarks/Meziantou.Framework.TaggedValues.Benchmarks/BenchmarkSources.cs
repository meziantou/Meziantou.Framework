using System.Globalization;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.TaggedValues.Benchmarks;

/// <summary>
/// Generates the code the analyzer runs on: a business application with entities, repositories and services that use LINQ,
/// dictionaries, async methods, pattern matching, tuples and records, with or without <c>[ValueTag]</c> annotations.
/// </summary>
internal static partial class BenchmarkSources
{
    private const string AttributeSource = """
        namespace Meziantou.Framework.TaggedValues
        {
            [System.AttributeUsage(System.AttributeTargets.Field | System.AttributeTargets.Property | System.AttributeTargets.Parameter | System.AttributeTargets.ReturnValue | System.AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
            public sealed class ValueTagAttribute : System.Attribute
            {
                public ValueTagAttribute(params string[] tags) => Tags = tags;

                public ValueTagAttribute(System.Type type, string memberName, params string[] tags) => (Type, MemberName, Tags) = (type, memberName, tags);

                public System.Collections.Generic.IReadOnlyList<string> Tags { get; }

                public System.Type? Type { get; }

                public string? MemberName { get; }

                public string? Key { get; set; }

                public string? Value { get; set; }
            }
        }
        """;

    /// <summary>
    /// Returns the files of the application: the attribute, then <paramref name="fileCount"/> modules of about 170 lines.
    /// </summary>
    /// <param name="fileCount">The number of modules.</param>
    /// <param name="tagged">Whether the code is annotated with <c>[ValueTag]</c> attributes and comments, with a few mismatches.</param>
    public static IReadOnlyList<string> Create(int fileCount, bool tagged)
    {
        var result = new List<string>(fileCount + 1) { AttributeSource };
        for (var i = 0; i < fileCount; i++)
        {
            var source = GetModule(i, fileCount);
            if (!tagged)
            {
                source = ValueTagAttributeRegex().Replace(source, "");
                source = ValueTagCommentRegex().Replace(source, "");
                source = MismatchRegex().Replace(source, "");
            }

            result.Add(source);
        }

        return result;
    }

    private static string GetModule(int index, int fileCount)
    {
        var i = index.ToString(CultureInfo.InvariantCulture);
        var other = ((index + 1) % fileCount).ToString(CultureInfo.InvariantCulture);
        return $$"""
            using System;
            using System.Collections.Generic;
            using System.Globalization;
            using System.Linq;
            using System.Text;
            using System.Threading;
            using System.Threading.Tasks;
            using Meziantou.Framework.TaggedValues;

            namespace Benchmark.Module{{i}};

            public sealed record Entity{{i}}Dto([ValueTag("Entity{{i}}Id")] Guid Id, [ValueTag("CustomerId")] Guid CustomerId, string Name, decimal Amount);

            public sealed class Line{{i}}
            {
                public int Index { get; set; }
                public decimal Price { get; set; }
                public int Quantity { get; set; }
                [ValueTag("ProductId")] public Guid ProductId { get; set; }
            }

            public sealed class Entity{{i}}
            {
                [ValueTag("Entity{{i}}Id")] public Guid Id { get; set; }
                [ValueTag("CustomerId")] public Guid CustomerId { get; set; }
                [ValueTag("Entity{{i}}Id")] public Guid? ParentId { get; set; }
                public string Name { get; set; } = "";
                public decimal Amount { get; set; }
                public int Quantity { get; set; }
                public DateTime CreatedAt { get; set; }
                public List<Line{{i}}> Lines { get; } = [];
                [ValueTag(Key = "ProductId", Value = "CustomerId")] public Dictionary<Guid, Guid> Owners { get; } = new();
            }

            public interface IEntity{{i}}Repository
            {
                [return: ValueTag("Entity{{i}}Id")]
                Guid Add(Entity{{i}} entity);

                Entity{{i}}? Find([ValueTag("Entity{{i}}Id")] Guid id);

                IReadOnlyList<Entity{{i}}> FindByCustomer([ValueTag("CustomerId")] Guid customerId);
            }

            public sealed class Entity{{i}}Repository : IEntity{{i}}Repository
            {
                private readonly Dictionary<Guid, Entity{{i}}> _items = new();
                private readonly List<Entity{{i}}> _list = [];
                private int _version;

                public int Count => _items.Count;

                public Guid Add(Entity{{i}} entity)
                {
                    ArgumentNullException.ThrowIfNull(entity);
                    _items[entity.Id] = entity;
                    _list.Add(entity);
                    _version++;
                    return entity.Id;
                }

                public Entity{{i}}? Find(Guid id) => _items.TryGetValue(id, out var entity) ? entity : null;

                public IReadOnlyList<Entity{{i}}> FindByCustomer(Guid customerId)
                {
                    return _list.Where(entity => entity.CustomerId == customerId).OrderBy(entity => entity.Name, StringComparer.Ordinal).ToList();
                }

                public bool Remove(Guid id)
                {
                    if (!_items.Remove(id, out var removed))
                        return false;

                    _list.RemoveAll(entity => entity.Id == removed.Id);
                    _version++;
                    return true;
                }
            }

            public sealed class Entity{{i}}Service(IEntity{{i}}Repository repository)
            {
                private static readonly CompositeFormat DescriptionFormat = CompositeFormat.Parse("{0} ({1}): {2}");

                public async Task<decimal> ComputeTotalAsync(Guid id, CancellationToken cancellationToken)
                {
                    await Task.Delay(0, cancellationToken).ConfigureAwait(false);
                    var entity = repository.Find(id);
                    if (entity is null)
                        return 0;

                    decimal total = 0;
                    foreach (var line in entity.Lines)
                    {
                        // Lines without a quantity or a price do not count
                        if (line.Quantity > 0 && line.Price != 0m)
                        {
                            total += line.Price * line.Quantity;
                        }
                    }

                    /* The discount depends on the amount of the entity */
                    var discount = entity.Amount > 1000 ? 0.1m : entity.Amount > 100 ? 0.05m : 0m;
                    return total - (total * discount);
                }

                public string Describe(Entity{{i}} entity)
                {
                    var builder = new StringBuilder();
                    builder.Append(entity.Name).Append(':').Append(entity.Amount.ToString(CultureInfo.InvariantCulture));
                    for (var index = 0; index < entity.Lines.Count; index++)
                    {
                        var line = entity.Lines[index];
                        builder.Append(index).Append('=').Append(line.Price.ToString(CultureInfo.InvariantCulture)).Append(';');
                    }

                    return string.Format(CultureInfo.InvariantCulture, DescriptionFormat, entity.Id, entity.CreatedAt, builder);
                }

                public Dictionary<Guid, List<Guid>> GroupByCustomer(IEnumerable<Entity{{i}}> entities)
                {
                    var /* ValueTag Key=CustomerId */ result = new Dictionary<Guid, List<Guid>>();
                    foreach (var entity in entities)
                    {
                        if (!result.TryGetValue(entity.CustomerId, out var ids))
                        {
                            ids = [];
                            result.Add(entity.CustomerId, ids);
                        }

                        ids.Add(entity.Id);
                    }

                    return result;
                }

                public bool IsSame(Entity{{i}} left, Entity{{i}}Dto right)
                {
                    return left.Id == right.Id && left.CustomerId.Equals(right.CustomerId) && string.Equals(left.Name, right.Name, StringComparison.Ordinal) && left.Amount == right.Amount;
                }

                public Entity{{i}}Dto ToDto(Entity{{i}} entity) => new(entity.Id, entity.CustomerId, entity.Name, entity.Amount);

                public IEnumerable<Guid> GetDistinctIds(IEnumerable<Entity{{i}}> entities)
                {
                    Guid /* ValueTag=Entity{{i}}Id */ last = Guid.Empty;
                    foreach (var entity in entities)
                    {
                        if (entity.Id == last)
                            continue;

                        last = entity.Id;
                        yield return entity.Id;
                    }
                }

                public string Classify(Entity{{i}} entity) => entity switch
                {
                    { Amount: > 1000 } => "large",
                    { Quantity: 0 } => "empty",
                    { ParentId: { } parentId } when parentId != entity.Id => "child",
                    _ => "default",
                };

                public (Guid Id, decimal Amount) Max(IReadOnlyList<Entity{{i}}> entities)
                {
                    var best = (Id: Guid.Empty, Amount: 0m);
                    foreach (var (id, amount) in entities.Select(entity => (entity.Id, entity.Amount)))
                    {
                        if (amount > best.Amount)
                        {
                            best = (id, amount);
                        }
                    }

                    return best;
                }

                public IEnumerable<Guid> GetProducts(Entity{{i}} entity, Guid customerId)
                {
                    return entity.Owners.Where(pair => pair.Value == customerId).Select(pair => pair.Key).Concat(entity.Lines.Select(line => line.ProductId)).Distinct();
                }

                public int CountMatches(IReadOnlyList<Entity{{i}}> entities, Entity{{i}}Dto dto)
                {
                    var count = 0;
                    for (var index = 0; index < entities.Count; index++)
                    {
                        var entity = entities[index];
                        if (entity.Id == dto.Id || entity.ParentId == dto.Id || entity.CustomerId == dto.CustomerId)
                        {
                            count++;
                        }
                    }

                    return count + (int)Math.Round(dto.Amount, MidpointRounding.AwayFromZero);
                }

                public void Mismatch(Entity{{i}} entity)
                {
                    _ = repository.Find(entity.CustomerId); // MISMATCH
                    _ = entity.Id == entity.CustomerId; // MISMATCH
                }

                public Benchmark.Module{{other}}.Entity{{other}}Dto? Convert(Entity{{i}} entity, Benchmark.Module{{other}}.Entity{{other}} target)
                {
                    if (entity.CustomerId != target.CustomerId)
                        return null;

                    return new Benchmark.Module{{other}}.Entity{{other}}Dto(target.Id, entity.CustomerId, entity.Name + target.Name, entity.Amount + target.Amount);
                }
            }
            """;
    }

    [GeneratedRegex("""\[(?:return: )?ValueTag\([^\]]*\)\] ?\s*""", RegexOptions.None, matchTimeoutMilliseconds: 10000)]
    private static partial Regex ValueTagAttributeRegex();

    [GeneratedRegex("""/\* ValueTag[^*]*\*/ ?""", RegexOptions.None, matchTimeoutMilliseconds: 10000)]
    private static partial Regex ValueTagCommentRegex();

    [GeneratedRegex(""".*// MISMATCH\r?\n""", RegexOptions.None, matchTimeoutMilliseconds: 10000)]
    private static partial Regex MismatchRegex();
}
