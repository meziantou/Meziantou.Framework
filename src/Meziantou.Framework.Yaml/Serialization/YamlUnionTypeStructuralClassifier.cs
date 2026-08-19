namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Classifies YAML payloads into union case types by comparing their YAML shape and, for mappings, their keys.
/// </summary>
/// <remarks>
/// <para>
/// The default union classification only distinguishes cases that use different YAML shapes, such as a scalar and a
/// sequence. This classifier adds structural classification for cases that serialize as YAML mappings.
/// </para>
/// <para>
/// To classify a mapping, the classifier starts with every object case as a candidate. For each key at the level of
/// the current mapping, it eliminates candidates that do not declare a matching key. Values and nested content are
/// not examined, and key order does not affect the result. Name matching honors
/// <see cref="YamlSerializerOptions.PropertyNameCaseInsensitive"/>.
/// </para>
/// <para>
/// After reading the mapping, candidates missing a required key are eliminated. Missing optional keys have no effect.
/// A key that is not declared by any case eliminates only cases configured with
/// <see cref="YamlUnmappedMemberHandling.Disallow"/>.
/// </para>
/// <para>
/// The classifier selects the case when exactly one candidate remains. Classification fails when no candidates or
/// several candidates remain. Configurations containing a case that can never be selected uniquely are rejected when
/// the classifier is created.
/// </para>
/// </remarks>
public sealed class YamlUnionTypeStructuralClassifier : YamlTypeClassifierFactory
{
    /// <inheritdoc/>
    public override bool CanClassify(YamlTypeClassifierContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Kind is YamlTypeClassifierKind.Union;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="context"/> does not describe a union type.</exception>
    /// <exception cref="NotSupportedException">The union declares cases that cannot be told apart.</exception>
    public override YamlTypeClassifier CreateYamlClassifier(YamlTypeClassifierContext context, YamlSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        if (context.Kind is not YamlTypeClassifierKind.Union)
        {
            throw new InvalidOperationException($"'{nameof(YamlUnionTypeStructuralClassifier)}' can only classify union types, but '{context.DeclaringType}' is not one.");
        }

        var classifier = StructuralClassifier.Create(context, options.PropertyNameCaseInsensitive);
        return classifier.Classify;
    }

    private sealed class StructuralClassifier
    {
        private readonly Dictionary<YamlUnionCaseShape, Type> _shapeCases;
        private readonly ObjectCase[] _objectCases;
        private readonly StringComparer _comparer;

        private StructuralClassifier(Dictionary<YamlUnionCaseShape, Type> shapeCases, ObjectCase[] objectCases, StringComparer comparer)
        {
            _shapeCases = shapeCases;
            _objectCases = objectCases;
            _comparer = comparer;
        }

        public static StructuralClassifier Create(YamlTypeClassifierContext context, bool caseInsensitive)
        {
            var comparer = caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var shapeCases = new Dictionary<YamlUnionCaseShape, Type>();
            var objectCases = new List<ObjectCase>();

            foreach (var unionCase in context.UnionCases)
            {
                if (unionCase is { Shape: YamlUnionCaseShape.Mapping, HasObjectProperties: true })
                {
                    objectCases.Add(new ObjectCase(unionCase, comparer));
                    continue;
                }

                // A shape is the only discriminator for non-object cases, so at most one case can claim each shape.
                if (!shapeCases.TryAdd(unionCase.Shape, unionCase.CaseType))
                {
                    throw CreateAmbiguousCasesException(context.DeclaringType, shapeCases[unionCase.Shape], unionCase.CaseType);
                }
            }

            // A mapping case without declared keys, such as a dictionary, accepts every mapping, so it cannot coexist
            // with object cases.
            if (objectCases.Count > 0 && shapeCases.TryGetValue(YamlUnionCaseShape.Mapping, out var dictionaryCase))
            {
                throw CreateAmbiguousCasesException(context.DeclaringType, objectCases[0].CaseType, dictionaryCase);
            }

            // Reject a case when every mapping it accepts also satisfies another case, since it can never be selected
            // uniquely. This quadratic check runs only once, and is bounded by the cases declared on the union.
            for (var i = 0; i < objectCases.Count; i++)
            {
                for (var j = 0; j < objectCases.Count; j++)
                {
                    if (i != j && objectCases[i].IsShadowedBy(objectCases[j]))
                    {
                        throw new NotSupportedException($"Union type '{context.DeclaringType}' declares case '{objectCases[i].CaseType}', which cannot be told apart from case '{objectCases[j].CaseType}'.");
                    }
                }
            }

            return new StructuralClassifier(shapeCases, [.. objectCases], comparer);
        }

        private static NotSupportedException CreateAmbiguousCasesException(Type unionType, Type first, Type second)
            => new($"Union type '{unionType}' declares cases '{first}' and '{second}', which are represented by the same YAML shape.");

        public Type? Classify(YamlReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);

            if (reader.TokenType is YamlTokenType.StartMapping && _objectCases.Length > 0)
            {
                return ClassifyMapping(reader) ?? GetCatchAllCase();
            }

            var shape = GetShape(reader);
            if (shape is not null && _shapeCases.TryGetValue(shape.Value, out var caseType))
            {
                return caseType;
            }

            return GetCatchAllCase();
        }

        /// <summary>Gets the case accepting any YAML shape, which is selected when no other case matches.</summary>
        private Type? GetCatchAllCase()
            => _shapeCases.TryGetValue(YamlUnionCaseShape.Any, out var caseType) ? caseType : null;

        private static YamlUnionCaseShape? GetShape(YamlReader reader)
        {
            if (reader.TokenType is YamlTokenType.StartMapping)
            {
                return YamlUnionCaseShape.Mapping;
            }

            if (reader.TokenType is YamlTokenType.StartSequence)
            {
                return YamlUnionCaseShape.Sequence;
            }

            if (reader.TokenType is not YamlTokenType.Scalar)
            {
                return null;
            }

            return YamlScalar.ResolveObject(reader) switch
            {
                null => null,
                bool => YamlUnionCaseShape.Boolean,
                sbyte or byte or short or ushort or int or uint or long or ulong or nint or nuint or float or double or decimal or Half or Int128 or UInt128 => YamlUnionCaseShape.Number,
                _ => YamlUnionCaseShape.Text,
            };
        }

        private Type? ClassifyMapping(YamlReader reader)
        {
            // Key order and duplicates do not affect the result, so collect the keys of the current mapping first and
            // then evaluate every case against that set.
            var keys = new HashSet<string>(_comparer);
            reader.Read();
            while (reader.TokenType is not YamlTokenType.EndMapping)
            {
                if (reader.TokenType is not YamlTokenType.Scalar)
                {
                    // A non-scalar key never matches a declared member, and the object converter rejects it anyway.
                    reader.Skip();
                    reader.Skip();
                    continue;
                }

                var key = reader.ScalarValue ?? string.Empty;
                reader.Read();
                reader.Skip();

                // A merge key carries the keys of another mapping rather than a member of the case.
                if (!string.Equals(key, "<<", StringComparison.Ordinal))
                {
                    keys.Add(key);
                }
            }

            // Begin with every object case as a candidate and eliminate cases using keys only.
            var isCandidate = new bool[_objectCases.Length];
            Array.Fill(isCandidate, value: true);

            foreach (var key in keys)
            {
                var isKnownKey = false;
                for (var i = 0; i < _objectCases.Length; i++)
                {
                    if (_objectCases[i].DeclaresProperty(key))
                    {
                        isKnownKey = true;
                        break;
                    }
                }

                for (var i = 0; i < _objectCases.Length; i++)
                {
                    // A key some case declares retains only the cases declaring it; an unknown key eliminates only the
                    // cases that reject unmapped keys.
                    isCandidate[i] &= isKnownKey
                        ? _objectCases[i].DeclaresProperty(key)
                        : !_objectCases[i].DisallowUnmappedProperties;
                }
            }

            Type? selected = null;
            for (var i = 0; i < _objectCases.Length; i++)
            {
                if (!isCandidate[i] || !_objectCases[i].HasAllRequiredProperties(keys))
                {
                    continue;
                }

                if (selected is not null)
                {
                    return null;
                }

                selected = _objectCases[i].CaseType;
            }

            return selected;
        }
    }

    private sealed class ObjectCase
    {
        private readonly HashSet<string> _propertyNames;
        private readonly HashSet<string> _requiredPropertyNames;

        public ObjectCase(YamlUnionCaseInfo unionCase, StringComparer comparer)
        {
            CaseType = unionCase.CaseType;
            DisallowUnmappedProperties = unionCase.DisallowUnmappedProperties;

            _propertyNames = new HashSet<string>(comparer);
            _requiredPropertyNames = new HashSet<string>(comparer);
            foreach (var property in unionCase.Properties)
            {
                _propertyNames.Add(property.Name);
                if (property.IsRequired)
                {
                    _requiredPropertyNames.Add(property.Name);
                }
            }
        }

        public Type CaseType { get; }
        public bool DisallowUnmappedProperties { get; }

        public bool DeclaresProperty(string name) => _propertyNames.Contains(name);

        public bool HasAllRequiredProperties(HashSet<string> keys)
        {
            foreach (var name in _requiredPropertyNames)
            {
                if (!keys.Contains(name))
                {
                    return false;
                }
            }

            return true;
        }

        public bool IsShadowedBy(ObjectCase other)
        {
            // This case is shadowed when every mapping it accepts is also accepted by the other case: the other case
            // must be at least as permissive about unknown keys, declare every key this case declares, and require no
            // key that this case does not itself require.
            if (!DisallowUnmappedProperties && other.DisallowUnmappedProperties)
            {
                return false;
            }

            foreach (var name in _propertyNames)
            {
                if (!other._propertyNames.Contains(name))
                {
                    return false;
                }
            }

            foreach (var name in other._requiredPropertyNames)
            {
                if (!_requiredPropertyNames.Contains(name))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
