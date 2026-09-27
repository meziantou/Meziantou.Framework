using System.Collections;
using System.Reflection;
using Meziantou.Framework.Yaml.Model;

namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlObjectConverter<T> : YamlConverter<T?>, IYamlUnionCaseShapeProvider
{
    private Contract? _contract;

    IReadOnlyList<YamlUnionCaseProperty>? IYamlUnionCaseShapeProvider.GetUnionCaseProperties(YamlReaderWriterBase readerWriter, out bool disallowUnmappedProperties)
    {
        disallowUnmappedProperties = false;

        var contract = _contract ??= Contract.Create(typeof(T), readerWriter);

        // A polymorphic type is deserialized through its discriminator, so its own members do not describe the payload.
        if (contract.Polymorphism is not null)
        {
            return null;
        }

        // An extension data member accepts any key, so the type never rejects unmapped keys.
        disallowUnmappedProperties = contract.ExtensionData is null && contract.UnmappedMemberHandling is YamlUnmappedMemberHandling.Disallow;

        var members = contract.MembersDeclaration;
        var properties = new YamlUnionCaseProperty[members.Length];
        for (var i = 0; i < members.Length; i++)
        {
            properties[i] = new YamlUnionCaseProperty(members[i].Name, members[i].IsRequired);
        }

        return properties;
    }

    public override bool CanPopulate(Type typeToConvert) => typeToConvert == typeof(T);

    public override T? Read(YamlReader reader)
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return YamlThrowHelper.CastAliasValue<T>(reader, rootAliasValue);
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, $"Aliases are not supported when deserializing into '{typeof(T)}' unless ReferenceHandling is Preserve.");
        }

        // A null scalar cannot be read into a value type, as for any other non-nullable value type.
        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader) && !typeof(T).IsValueType)
        {
            reader.Read();
            return default;
        }

        if (reader.TokenType != YamlTokenType.StartMapping)
        {
            throw YamlThrowHelper.ThrowExpectedMapping(reader);
        }

        Contract contract;
        try
        {
            contract = _contract ??= Contract.Create(typeof(T), reader);
            contract.EnsureConstructor(reader);
        }
        catch (YamlException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, exception.Message, exception);
        }
        if (contract.Polymorphism is not null)
        {
            return ReadPolymorphic(reader, contract);
        }

        return ReadObjectCore(reader, contract);
    }

    public override object? Populate(YamlReader reader, Type typeToConvert, object existingValue)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(typeToConvert);
        ArgumentNullException.ThrowIfNull(existingValue);

        if (typeToConvert != typeof(T))
        {
            throw new InvalidOperationException($"Converter '{GetType()}' cannot populate '{typeToConvert}'.");
        }

        if (reader.TryReadAlias(out var aliasValue))
        {
            return aliasValue;
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, $"Aliases are not supported when deserializing into '{typeof(T)}' unless ReferenceHandling is Preserve.");
        }

        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            reader.Read();
            return default;
        }

        if (reader.TokenType != YamlTokenType.StartMapping)
        {
            throw YamlThrowHelper.ThrowExpectedMapping(reader);
        }

        Contract contract;
        try
        {
            contract = _contract ??= Contract.Create(typeof(T), reader);
            contract.EnsureConstructor(reader);
        }
        catch (YamlException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, exception.Message, exception);
        }

        if (contract.Polymorphism is not null)
        {
            var runtimeValue = ReadPolymorphic(reader, contract);
            return runtimeValue;
        }

        if (typeof(T).IsValueType)
        {
            object boxed = existingValue;
            PopulateObjectCore(reader, contract, boxed);
            return (T)boxed;
        }

        PopulateObjectCore(reader, contract, existingValue);
        return existingValue;
    }

    public override void Write(YamlWriter writer, T? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        var contract = _contract ??= Contract.Create(typeof(T), writer);

        // The runtime type decides whether the value is tracked: a struct declared as an interface is boxed, and the
        // box is not an identity worth preserving.
        if (!typeof(T).IsValueType && writer.TryWriteReference(value))
        {
            return;
        }

        // Box a value type once, so a mutation made by a lifecycle callback is visible to the members written next.
        object boxedValue = value;
        if (boxedValue is IYamlOnSerializing onSerializing && writer.ShouldInvokeOnSerializing(boxedValue))
        {
            try
            {
                onSerializing.OnSerializing();
            }
            catch (YamlException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new YamlException(Mark.Empty, Mark.Empty, $"An error occurred while invoking '{nameof(IYamlOnSerializing)}.{nameof(IYamlOnSerializing.OnSerializing)}' on '{value.GetType()}'.", exception);
            }
        }

        // A registered type is written with its discriminator, even when it is the declared type itself.
        var runtimeType = boxedValue.GetType();
        if (contract.Polymorphism is not null && (runtimeType != typeof(T) || contract.Polymorphism.TryGetDerivedTypeInfo(runtimeType, out _)))
        {
            YamlObjectConverter<T>.WritePolymorphic(writer, boxedValue, runtimeType, contract);
        }
        else
        {
            YamlObjectConverter<T>.WriteObjectCore(writer, boxedValue, contract);
        }

        if (boxedValue is IYamlOnSerialized onSerialized && !writer.IsCollectingReferences)
        {
            try
            {
                onSerialized.OnSerialized();
            }
            catch (YamlException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new YamlException(Mark.Empty, Mark.Empty, $"An error occurred while invoking '{nameof(IYamlOnSerialized)}.{nameof(IYamlOnSerialized.OnSerialized)}' on '{value.GetType()}'.", exception);
            }
        }
    }

    private T? ReadObjectCore(YamlReader reader, Contract contract)
    {
        if (contract.Constructor is not null)
        {
            return ReadObjectCoreWithConstructor(reader, contract);
        }

        // The instance is kept boxed while its members are assigned: a value type would otherwise be copied by each
        // assignment and lifecycle callback, and the deserialized value would lose every member.
        object instance;
        try
        {
            instance = contract.CreateInstance();
        }
        catch (YamlException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, exception.Message, exception);
        }

        if (reader.ReferenceReader is not null && reader.Anchor is not null)
        {
            reader.ReferenceReader.Register(reader.Anchor, instance!);
        }

        if (instance is IYamlOnDeserializing onDeserializing)
        {
            try
            {
                onDeserializing.OnDeserializing();
            }
            catch (YamlException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new YamlException(reader.SourceName, reader.Start, reader.End, $"An error occurred while invoking '{nameof(IYamlOnDeserializing)}.{nameof(IYamlOnDeserializing.OnDeserializing)}' on '{typeof(T)}'.", exception);
            }
        }

        var options = reader.Options;
        var mergeEnabled = YamlMergeKey.IsEnabled(options);
        HashSet<string>? explicitKeys = mergeEnabled ? new HashSet<string>(reader.PropertyNameComparer) : null;
        HashSet<string>? seenKeys = options.DuplicateKeyHandling == YamlDuplicateKeyHandling.LastWins ? null : new HashSet<string>(reader.PropertyNameComparer);
        var mappingStart = reader.Start;
        var requiredSeen = contract.RequiredMembers.Length == 0 ? null : new bool[contract.RequiredMembers.Length];

        reader.Read();
        while (reader.TokenType != YamlTokenType.EndMapping)
        {
            if (reader.TokenType != YamlTokenType.Scalar)
            {
                throw YamlThrowHelper.ThrowExpectedScalarKey(reader);
            }

            var keyStart = reader.Start;
            var keyEnd = reader.End;
            var isMergeKey = YamlMergeKey.IsMergeKey(reader);
            var key = reader.ScalarValue ?? string.Empty;
            reader.Read();

            if (isMergeKey)
            {
                ReadAndApplyMergeToInstance(reader, instance!, contract, explicitKeys!, requiredSeen);
                continue;
            }

            explicitKeys?.Add(key);

            // A duplicate key is detected whether it maps to a member, to the extension data, or to nothing, as for a
            // type deserialized through its constructor.
            var wasSeen = seenKeys is not null && !seenKeys.Add(key);
            if (wasSeen && options.DuplicateKeyHandling == YamlDuplicateKeyHandling.Error)
            {
                throw new YamlException(reader.SourceName, keyStart, keyEnd, $"Duplicate mapping key '{key}'.");
            }

            if (wasSeen && options.DuplicateKeyHandling == YamlDuplicateKeyHandling.FirstWins)
            {
                reader.Skip();
                continue;
            }

            if (!contract.TryGetMember(key, out var member))
            {
                if (contract.ExtensionData is null)
                {
                    SkipOrThrowUnmappedMember(reader, contract, key);
                    continue;
                }

                ReadExtensionData(reader, instance!, contract.ExtensionData, key, keyStart, keyEnd);
                continue;
            }

            if (requiredSeen is not null && member.RequiredIndex >= 0)
            {
                requiredSeen[member.RequiredIndex] = true;
            }

            if (member.ShouldIgnoreOnRead)
            {
                reader.Skip();
                continue;
            }

            ReadAndApplyMemberValue(reader, instance!, contract, member, key, keyStart, keyEnd);
        }

        if (requiredSeen is not null)
        {
            List<string>? missing = null;
            for (var i = 0; i < requiredSeen.Length; i++)
            {
                if (!requiredSeen[i])
                {
                    missing ??= new List<string>();
                    missing.Add(contract.RequiredMembers[i].Name);
                }
            }

            if (missing is not null)
            {
                throw YamlThrowHelper.ThrowMissingRequiredMembers(reader, mappingStart, typeof(T), missing);
            }
        }

        if (instance is IYamlOnDeserialized onDeserialized)
        {
            try
            {
                onDeserialized.OnDeserialized();
            }
            catch (YamlException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new YamlException(reader.SourceName, reader.Start, reader.End, $"An error occurred while invoking '{nameof(IYamlOnDeserialized)}.{nameof(IYamlOnDeserialized.OnDeserialized)}' on '{typeof(T)}'.", exception);
            }
        }

        reader.Read();

        return (T)instance;
    }

    private static void PopulateObjectCore(YamlReader reader, Contract contract, object instance)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(instance);

        if (instance is IYamlOnDeserializing onDeserializing)
        {
            try
            {
                onDeserializing.OnDeserializing();
            }
            catch (YamlException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new YamlException(reader.SourceName, reader.Start, reader.End, $"An error occurred while invoking '{nameof(IYamlOnDeserializing)}.{nameof(IYamlOnDeserializing.OnDeserializing)}' on '{instance.GetType()}'.", exception);
            }
        }

        var options = reader.Options;
        var mergeEnabled = YamlMergeKey.IsEnabled(options);
        HashSet<string>? explicitKeys = mergeEnabled ? new HashSet<string>(reader.PropertyNameComparer) : null;
        HashSet<string>? seenKeys = options.DuplicateKeyHandling == YamlDuplicateKeyHandling.LastWins ? null : new HashSet<string>(reader.PropertyNameComparer);
        var mappingStart = reader.Start;
        var requiredSeen = contract.RequiredMembers.Length == 0 ? null : new bool[contract.RequiredMembers.Length];

        reader.Read();
        while (reader.TokenType != YamlTokenType.EndMapping)
        {
            if (reader.TokenType != YamlTokenType.Scalar)
            {
                throw YamlThrowHelper.ThrowExpectedScalarKey(reader);
            }

            var keyStart = reader.Start;
            var keyEnd = reader.End;
            var isMergeKey = YamlMergeKey.IsMergeKey(reader);
            var key = reader.ScalarValue ?? string.Empty;
            reader.Read();

            if (isMergeKey)
            {
                ReadAndApplyMergeToPopulatedInstance(reader, instance, contract, explicitKeys!, requiredSeen);
                continue;
            }

            explicitKeys?.Add(key);

            // A duplicate key is detected whether it maps to a member, to the extension data, or to nothing, as for a
            // type deserialized through its constructor.
            var wasSeen = seenKeys is not null && !seenKeys.Add(key);
            if (wasSeen && options.DuplicateKeyHandling == YamlDuplicateKeyHandling.Error)
            {
                throw new YamlException(reader.SourceName, keyStart, keyEnd, $"Duplicate mapping key '{key}'.");
            }

            if (wasSeen && options.DuplicateKeyHandling == YamlDuplicateKeyHandling.FirstWins)
            {
                reader.Skip();
                continue;
            }

            if (!contract.TryGetMember(key, out var member))
            {
                if (contract.ExtensionData is null)
                {
                    SkipOrThrowUnmappedMember(reader, contract, key);
                    continue;
                }

                ReadExtensionData(reader, instance, contract.ExtensionData, key, keyStart, keyEnd);
                continue;
            }

            if (requiredSeen is not null && member.RequiredIndex >= 0)
            {
                requiredSeen[member.RequiredIndex] = true;
            }

            if (member.ShouldIgnoreOnRead)
            {
                reader.Skip();
                continue;
            }

            ReadAndApplyMemberValue(reader, instance, contract, member, key, keyStart, keyEnd);
        }

        if (requiredSeen is not null)
        {
            List<string>? missing = null;
            for (var i = 0; i < requiredSeen.Length; i++)
            {
                if (!requiredSeen[i])
                {
                    missing ??= new List<string>();
                    missing.Add(contract.RequiredMembers[i].Name);
                }
            }

            if (missing is not null)
            {
                throw YamlThrowHelper.ThrowMissingRequiredMembers(reader, mappingStart, instance.GetType(), missing);
            }
        }

        if (instance is IYamlOnDeserialized onDeserialized)
        {
            try
            {
                onDeserialized.OnDeserialized();
            }
            catch (YamlException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new YamlException(reader.SourceName, reader.Start, reader.End, $"An error occurred while invoking '{nameof(IYamlOnDeserialized)}.{nameof(IYamlOnDeserialized.OnDeserialized)}' on '{instance.GetType()}'.", exception);
            }
        }

        reader.Read();
    }

    private T? ReadObjectCoreWithConstructor(YamlReader reader, Contract contract)
    {
        var constructor = contract.Constructor ?? throw new InvalidOperationException("Constructor model was not available.");

        var mappingStart = reader.Start;
        var mappingAnchor = reader.Anchor;
        var options = reader.Options;
        var mergeEnabled = YamlMergeKey.IsEnabled(options);
        HashSet<string>? explicitKeys = mergeEnabled ? new HashSet<string>(reader.PropertyNameComparer) : null;

        HashSet<string>? seenKeys = options.DuplicateKeyHandling == YamlDuplicateKeyHandling.LastWins
            ? null
            : new HashSet<string>(reader.PropertyNameComparer);

        var requiredSeen = contract.RequiredMembers.Length == 0 ? null : new bool[contract.RequiredMembers.Length];

        var args = new object?[constructor.ParameterCount];
        var paramSeen = new bool[constructor.ParameterCount];

        var memberValues = new Dictionary<Member, BufferedMemberAssignment>();
        List<BufferedExtensionEntry>? extensionEntries = contract.ExtensionData is null ? null : new List<BufferedExtensionEntry>();

        reader.Read();
        while (reader.TokenType != YamlTokenType.EndMapping)
        {
            if (reader.TokenType != YamlTokenType.Scalar)
            {
                throw YamlThrowHelper.ThrowExpectedScalarKey(reader);
            }

            var keyStart = reader.Start;
            var keyEnd = reader.End;
            var isMergeKey = YamlMergeKey.IsMergeKey(reader);
            var key = reader.ScalarValue ?? string.Empty;
            reader.Read();

            if (isMergeKey)
            {
                ReadAndApplyMergeToConstructorBuffers(reader, contract, constructor, args, paramSeen, memberValues, extensionEntries, explicitKeys!, requiredSeen);
                continue;
            }

            explicitKeys?.Add(key);

            var wasSeen = seenKeys is not null && !seenKeys.Add(key);
            if (wasSeen && options.DuplicateKeyHandling == YamlDuplicateKeyHandling.Error)
            {
                throw new YamlException(reader.SourceName, keyStart, keyEnd, $"Duplicate mapping key '{key}'.");
            }

            if (wasSeen && options.DuplicateKeyHandling == YamlDuplicateKeyHandling.FirstWins)
            {
                reader.Skip();
                continue;
            }

            if (contract.TryGetMember(key, out var requiredCandidate))
            {
                if (requiredSeen is not null && requiredCandidate.RequiredIndex >= 0)
                {
                    requiredSeen[requiredCandidate.RequiredIndex] = true;
                }
            }

            if (constructor.TryGetParameterIndex(key, out var parameterIndex))
            {
                var parameterType = constructor.GetParameterType(parameterIndex);
                var converter = constructor.GetParameterConverter(parameterIndex) ?? reader.GetConverter(parameterType);
                object? value;
                try
                {
                    value = converter.Read(reader, parameterType);
                }
                catch (YamlException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new YamlException(reader.SourceName, keyStart, keyEnd, exception.Message, exception);
                }

                ThrowIfNullForNonNullableConstructorParameter(reader, contract, constructor, parameterIndex, value);
                args[parameterIndex] = value;
                paramSeen[parameterIndex] = true;
                continue;
            }

            if (contract.TryGetMember(key, out var member))
            {
                if (!member.CanWrite)
                {
                    if (contract.ExtensionData is not null)
                    {
                        var extValue = ReadExtensionDataValue(reader, contract.ExtensionData);
                        extensionEntries!.Add(new BufferedExtensionEntry(key, extValue, keyStart, keyEnd));
                    }
                    else
                    {
                        reader.Skip();
                    }

                    continue;
                }

                var value = ReadBufferedMemberValue(reader, member, keyStart, keyEnd);
                ThrowIfNullForNonNullableMember(reader, contract, member, value);
                memberValues[member] = new BufferedMemberAssignment(member, value, keyStart, keyEnd);
                continue;
            }

            if (contract.ExtensionData is not null)
            {
                var extValue = ReadExtensionDataValue(reader, contract.ExtensionData);
                extensionEntries!.Add(new BufferedExtensionEntry(key, extValue, keyStart, keyEnd));
                continue;
            }

            SkipOrThrowUnmappedMember(reader, contract, key);
        }

        // Ensure all constructor parameters are satisfied before constructing the instance.
        for (var i = 0; i < constructor.ParameterCount; i++)
        {
            if (paramSeen[i])
            {
                continue;
            }

            if (constructor.TryGetDefaultValue(i, out var defaultValue))
            {
                args[i] = defaultValue;
                continue;
            }

            if (reader.Options.RespectRequiredConstructorParameters)
            {
                throw YamlThrowHelper.ThrowMissingRequiredConstructorParameter(reader, mappingStart, typeof(T), constructor.GetParameterName(i));
            }

            args[i] = null;
        }

        // The instance is kept boxed while its members are assigned: a value type would otherwise be copied by each
        // assignment and lifecycle callback, and the deserialized value would lose them.
        object instance;
        try
        {
            instance = constructor.CreateInstance(args);
        }
        catch (YamlException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new YamlException(reader.SourceName, mappingStart, reader.End, exception.Message, exception);
        }

        if (reader.ReferenceReader is not null && mappingAnchor is not null)
        {
            reader.ReferenceReader.Register(mappingAnchor, instance);
        }

        if (instance is IYamlOnDeserializing onDeserializing)
        {
            try
            {
                onDeserializing.OnDeserializing();
            }
            catch (YamlException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new YamlException(reader.SourceName, reader.Start, reader.End, $"An error occurred while invoking '{nameof(IYamlOnDeserializing)}.{nameof(IYamlOnDeserializing.OnDeserializing)}' on '{typeof(T)}'.", exception);
            }
        }

        foreach (var assignment in memberValues.Values)
        {
            try
            {
                assignment.Member.SetValue(instance, assignment.Value);
            }
            catch (YamlException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new YamlException(reader.SourceName, assignment.KeyStart, assignment.KeyEnd, exception.Message, exception);
            }
        }

        if (extensionEntries is not null && extensionEntries.Count != 0)
        {
            for (var i = 0; i < extensionEntries.Count; i++)
            {
                var entry = extensionEntries[i];
                try
                {
                    AddExtensionDataValue(instance, contract.ExtensionData!, entry.Key, entry.Value);
                }
                catch (YamlException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new YamlException(reader.SourceName, entry.KeyStart, entry.KeyEnd, exception.Message, exception);
                }
            }
        }

        if (requiredSeen is not null)
        {
            List<string>? missing = null;
            for (var i = 0; i < requiredSeen.Length; i++)
            {
                if (!requiredSeen[i])
                {
                    missing ??= new List<string>();
                    missing.Add(contract.RequiredMembers[i].Name);
                }
            }

            if (missing is not null)
            {
                throw YamlThrowHelper.ThrowMissingRequiredMembers(reader, mappingStart, typeof(T), missing);
            }
        }

        if (instance is IYamlOnDeserialized onDeserialized)
        {
            try
            {
                onDeserialized.OnDeserialized();
            }
            catch (YamlException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new YamlException(reader.SourceName, reader.Start, reader.End, $"An error occurred while invoking '{nameof(IYamlOnDeserialized)}.{nameof(IYamlOnDeserialized.OnDeserialized)}' on '{typeof(T)}'.", exception);
            }
        }

        reader.Read();

        return (T)instance;
    }

    /// <summary>
    /// Indicates a null scalar read into a member of type <paramref name="type"/> assigns <see langword="null"/> without
    /// going through a converter: the type accepts <see langword="null"/> and no custom converter handles it.
    /// </summary>
    private static bool ReadsNullScalarAsNull(Type type, YamlReaderWriterBase readerWriter)
        => (!type.IsValueType || Nullable.GetUnderlyingType(type) is not null) &&
           !type.IsDefined(typeof(YamlConverterAttribute), inherit: false) &&
           !readerWriter.TryGetCustomConverter(type, out _);

    private static object? ReadBufferedMemberValue(YamlReader reader, Member member, Mark keyStart, Mark keyEnd)
    {
        if (member.ReadsNullScalarAsNull && reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            reader.Read();
            return null;
        }

        var converter = member.Converter ??= reader.GetConverter(member.MemberType);
        try
        {
            return converter.Read(reader, member.MemberType);
        }
        catch (YamlException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new YamlException(reader.SourceName, keyStart, keyEnd, exception.Message, exception);
        }
    }

    private static void SkipOrThrowUnmappedMember(YamlReader reader, Contract contract, string key)
    {
        if (contract.UnmappedMemberHandling == YamlUnmappedMemberHandling.Disallow)
        {
            throw YamlThrowHelper.ThrowUnmappedMember(reader, contract.DeclaringType, key);
        }

        reader.Skip();
    }

    private void ReadAndApplyMergeToInstance(YamlReader reader, object instance, Contract contract, HashSet<string> explicitKeys, bool[]? requiredSeen)
    {
        if (!YamlMergeKey.IsEnabled(reader.Options))
        {
            reader.Skip();
            return;
        }

        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            reader.Read();
            return;
        }

        YamlMergeKey.ReplayAlias(reader, contract.DeclaringType);

        if (reader.TokenType == YamlTokenType.StartMapping)
        {
            ApplyMergeMappingToInstance(reader, instance, contract, explicitKeys, requiredSeen);
            return;
        }

        if (reader.TokenType == YamlTokenType.StartSequence)
        {
            reader.Read();
            while (reader.TokenType != YamlTokenType.EndSequence)
            {
                YamlMergeKey.ReplayAlias(reader, contract.DeclaringType);

                if (reader.TokenType != YamlTokenType.StartMapping)
                {
                    throw new YamlException(reader.SourceName, reader.Start, reader.End, "Merge sequence entries must be mappings.");
                }

                ApplyMergeMappingToInstance(reader, instance, contract, explicitKeys, requiredSeen);
            }

            reader.Read();
            return;
        }


        throw new YamlException(reader.SourceName, reader.Start, reader.End, "Merge key value must be a mapping or a sequence of mappings.");
    }

    private void ApplyMergeMappingToInstance(YamlReader reader, object instance, Contract contract, HashSet<string> explicitKeys, bool[]? requiredSeen)
    {
        if (reader.TokenType != YamlTokenType.StartMapping)
        {
            throw YamlThrowHelper.ThrowExpectedMapping(reader);
        }

        reader.Read();
        while (reader.TokenType != YamlTokenType.EndMapping)
        {
            if (reader.TokenType != YamlTokenType.Scalar)
            {
                throw YamlThrowHelper.ThrowExpectedScalarKey(reader);
            }

            var keyStart = reader.Start;
            var keyEnd = reader.End;
            var isMergeKey = YamlMergeKey.IsMergeKey(reader);
            var key = reader.ScalarValue ?? string.Empty;
            reader.Read();

            if (isMergeKey)
            {
                ReadAndApplyMergeToInstance(reader, instance, contract, explicitKeys, requiredSeen);
                continue;
            }

            // The key is provided by the merge. An explicitly declared key, and a key an earlier mapping of the
            // merge already provided, both take precedence over it.
            if (!explicitKeys.Add(key))
            {
                reader.Skip();
                continue;
            }

            if (contract.TryGetMember(key, out var member))
            {
                if (requiredSeen is not null && member.RequiredIndex >= 0)
                {
                    requiredSeen[member.RequiredIndex] = true;
                }

                if (member.ShouldIgnoreOnRead)
                {
                    reader.Skip();
                    continue;
                }

                ReadAndApplyMemberValue(reader, instance, contract, member, key, keyStart, keyEnd);
                continue;
            }

            if (contract.ExtensionData is not null)
            {
                ReadExtensionData(reader, instance, contract.ExtensionData, key, keyStart, keyEnd);
                continue;
            }

            SkipOrThrowUnmappedMember(reader, contract, key);
        }

        reader.Read();
    }

    private static void ReadAndApplyMergeToPopulatedInstance(YamlReader reader, object instance, Contract contract, HashSet<string> explicitKeys, bool[]? requiredSeen)
    {
        if (!YamlMergeKey.IsEnabled(reader.Options))
        {
            reader.Skip();
            return;
        }

        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            reader.Read();
            return;
        }

        YamlMergeKey.ReplayAlias(reader, contract.DeclaringType);

        if (reader.TokenType == YamlTokenType.StartMapping)
        {
            ApplyMergeMappingToPopulatedInstance(reader, instance, contract, explicitKeys, requiredSeen);
            return;
        }

        if (reader.TokenType == YamlTokenType.StartSequence)
        {
            reader.Read();
            while (reader.TokenType != YamlTokenType.EndSequence)
            {
                YamlMergeKey.ReplayAlias(reader, contract.DeclaringType);

                if (reader.TokenType != YamlTokenType.StartMapping)
                {
                    throw new YamlException(reader.SourceName, reader.Start, reader.End, "Merge sequence entries must be mappings.");
                }

                ApplyMergeMappingToPopulatedInstance(reader, instance, contract, explicitKeys, requiredSeen);
            }

            reader.Read();
            return;
        }


        throw new YamlException(reader.SourceName, reader.Start, reader.End, "Merge key value must be a mapping or a sequence of mappings.");
    }

    private static void ApplyMergeMappingToPopulatedInstance(YamlReader reader, object instance, Contract contract, HashSet<string> explicitKeys, bool[]? requiredSeen)
    {
        if (reader.TokenType != YamlTokenType.StartMapping)
        {
            throw YamlThrowHelper.ThrowExpectedMapping(reader);
        }

        reader.Read();
        while (reader.TokenType != YamlTokenType.EndMapping)
        {
            if (reader.TokenType != YamlTokenType.Scalar)
            {
                throw YamlThrowHelper.ThrowExpectedScalarKey(reader);
            }

            var keyStart = reader.Start;
            var keyEnd = reader.End;
            var isMergeKey = YamlMergeKey.IsMergeKey(reader);
            var key = reader.ScalarValue ?? string.Empty;
            reader.Read();

            if (isMergeKey)
            {
                ReadAndApplyMergeToPopulatedInstance(reader, instance, contract, explicitKeys, requiredSeen);
                continue;
            }

            // The key is provided by the merge. An explicitly declared key, and a key an earlier mapping of the
            // merge already provided, both take precedence over it.
            if (!explicitKeys.Add(key))
            {
                reader.Skip();
                continue;
            }

            if (contract.TryGetMember(key, out var member))
            {
                if (requiredSeen is not null && member.RequiredIndex >= 0)
                {
                    requiredSeen[member.RequiredIndex] = true;
                }

                if (member.ShouldIgnoreOnRead)
                {
                    reader.Skip();
                    continue;
                }

                ReadAndApplyMemberValue(reader, instance, contract, member, key, keyStart, keyEnd);
                continue;
            }

            if (contract.ExtensionData is not null)
            {
                ReadExtensionData(reader, instance, contract.ExtensionData, key, keyStart, keyEnd);
                continue;
            }

            SkipOrThrowUnmappedMember(reader, contract, key);
        }

        reader.Read();
    }

    private static void ReadAndApplyMemberValue(YamlReader reader, object instance, Contract contract, Member member, string key, Mark keyStart, Mark keyEnd)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(member);

        var effectiveHandling = member.GetEffectiveObjectCreationHandling(contract.PreferredObjectCreationHandling);
        var preferPopulate = effectiveHandling == YamlObjectCreationHandling.Populate;

        // A null scalar replaces the value: there is nothing to populate.
        var isNullScalar = reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader);
        if (isNullScalar)
        {
            if (preferPopulate && !member.CanWrite)
            {
                throw new InvalidOperationException($"Unable to assign 'null' to the property or field of type '{member.MemberType}'.");
            }

            if (!member.CanWrite)
            {
                if (contract.ExtensionData is not null)
                {
                    ReadExtensionData(reader, instance, contract.ExtensionData, key, keyStart, keyEnd);
                }
                else
                {
                    reader.Skip();
                }

                return;
            }

            if (member.ReadsNullScalarAsNull)
            {
                ThrowIfNullForNonNullableMember(reader, contract, member, null);
                reader.Read();
                try
                {
                    member.SetValue(instance, null);
                }
                catch (YamlException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new YamlException(reader.SourceName, keyStart, keyEnd, exception.Message, exception);
                }

                return;
            }
        }

        var converter = member.Converter ??= reader.GetConverter(member.MemberType);
        var canPopulate = !isNullScalar && converter.CanPopulate(member.MemberType);
        var canPopulateMember = canPopulate && (!member.MemberType.IsValueType || member.CanWrite);
        var explicitPopulate = member.ObjectCreationHandling == YamlObjectCreationHandling.Populate;

        if (preferPopulate && explicitPopulate && !canPopulateMember && !isNullScalar)
        {
            if (member.MemberType.IsValueType && !member.CanWrite)
            {
                throw new InvalidOperationException($"Property '{member.ClrName}' on type '{contract.DeclaringType}' is marked with YamlObjectCreationHandling.Populate but is a value type that doesn't have a setter.");
            }

            throw new InvalidOperationException($"Property '{member.ClrName}' on type '{contract.DeclaringType}' is marked with YamlObjectCreationHandling.Populate but it doesn't support populating. This can be either because the property type is immutable or it could use a custom converter.");
        }

        if (preferPopulate && canPopulateMember)
        {
            object? currentValue;
            try
            {
                currentValue = member.GetValue(instance);
            }
            catch (Exception exception)
            {
                throw new YamlException(reader.SourceName, keyStart, keyEnd, exception.Message, exception);
            }

            if (currentValue is not null)
            {
                object? populatedValue;
                try
                {
                    populatedValue = converter.Populate(reader, member.MemberType, currentValue);
                }
                catch (YamlException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new YamlException(reader.SourceName, keyStart, keyEnd, exception.Message, exception);
                }

                if (member.CanWrite)
                {
                    ThrowIfNullForNonNullableMember(reader, contract, member, populatedValue);
                    try
                    {
                        member.SetValue(instance, populatedValue);
                    }
                    catch (YamlException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        throw new YamlException(reader.SourceName, keyStart, keyEnd, exception.Message, exception);
                    }
                }

                return;
            }
        }

        if (!member.CanWrite)
        {
            if (contract.ExtensionData is not null)
            {
                ReadExtensionData(reader, instance, contract.ExtensionData, key, keyStart, keyEnd);
            }
            else
            {
                reader.Skip();
            }

            return;
        }

        object? value;
        try
        {
            value = converter.Read(reader, member.MemberType);
        }
        catch (YamlException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new YamlException(reader.SourceName, keyStart, keyEnd, exception.Message, exception);
        }

        ThrowIfNullForNonNullableMember(reader, contract, member, value);
        try
        {
            member.SetValue(instance, value);
        }
        catch (YamlException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new YamlException(reader.SourceName, keyStart, keyEnd, exception.Message, exception);
        }
    }

    private void ReadAndApplyMergeToConstructorBuffers(
        YamlReader reader,
        Contract contract,
        ConstructorModel constructor,
        object?[] args,
        bool[] paramSeen,
        Dictionary<Member, BufferedMemberAssignment> memberValues,
        List<BufferedExtensionEntry>? extensionEntries,
        HashSet<string> explicitKeys,
        bool[]? requiredSeen)
    {
        if (!YamlMergeKey.IsEnabled(reader.Options))
        {
            reader.Skip();
            return;
        }

        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            reader.Read();
            return;
        }

        YamlMergeKey.ReplayAlias(reader, contract.DeclaringType);

        if (reader.TokenType == YamlTokenType.StartMapping)
        {
            ApplyMergeMappingToConstructorBuffers(reader, contract, constructor, args, paramSeen, memberValues, extensionEntries, explicitKeys, requiredSeen);
            return;
        }

        if (reader.TokenType == YamlTokenType.StartSequence)
        {
            reader.Read();
            while (reader.TokenType != YamlTokenType.EndSequence)
            {
                YamlMergeKey.ReplayAlias(reader, contract.DeclaringType);

                if (reader.TokenType != YamlTokenType.StartMapping)
                {
                    throw new YamlException(reader.SourceName, reader.Start, reader.End, "Merge sequence entries must be mappings.");
                }

                ApplyMergeMappingToConstructorBuffers(reader, contract, constructor, args, paramSeen, memberValues, extensionEntries, explicitKeys, requiredSeen);
            }

            reader.Read();
            return;
        }


        throw new YamlException(reader.SourceName, reader.Start, reader.End, "Merge key value must be a mapping or a sequence of mappings.");
    }

    private void ApplyMergeMappingToConstructorBuffers(
        YamlReader reader,
        Contract contract,
        ConstructorModel constructor,
        object?[] args,
        bool[] paramSeen,
        Dictionary<Member, BufferedMemberAssignment> memberValues,
        List<BufferedExtensionEntry>? extensionEntries,
        HashSet<string> explicitKeys,
        bool[]? requiredSeen)
    {
        if (reader.TokenType != YamlTokenType.StartMapping)
        {
            throw YamlThrowHelper.ThrowExpectedMapping(reader);
        }

        reader.Read();
        while (reader.TokenType != YamlTokenType.EndMapping)
        {
            if (reader.TokenType != YamlTokenType.Scalar)
            {
                throw YamlThrowHelper.ThrowExpectedScalarKey(reader);
            }

            var keyStart = reader.Start;
            var keyEnd = reader.End;
            var isMergeKey = YamlMergeKey.IsMergeKey(reader);
            var key = reader.ScalarValue ?? string.Empty;
            reader.Read();

            if (isMergeKey)
            {
                ReadAndApplyMergeToConstructorBuffers(reader, contract, constructor, args, paramSeen, memberValues, extensionEntries, explicitKeys, requiredSeen);
                continue;
            }

            // The key is provided by the merge. An explicitly declared key, and a key an earlier mapping of the
            // merge already provided, both take precedence over it.
            if (!explicitKeys.Add(key))
            {
                reader.Skip();
                continue;
            }

            if (contract.TryGetMember(key, out var requiredCandidate))
            {
                if (requiredSeen is not null && requiredCandidate.RequiredIndex >= 0)
                {
                    requiredSeen[requiredCandidate.RequiredIndex] = true;
                }
            }

            if (constructor.TryGetParameterIndex(key, out var parameterIndex))
            {
                var parameterType = constructor.GetParameterType(parameterIndex);
                var converter = constructor.GetParameterConverter(parameterIndex) ?? reader.GetConverter(parameterType);
                object? value;
                try
                {
                    value = converter.Read(reader, parameterType);
                }
                catch (YamlException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new YamlException(reader.SourceName, keyStart, keyEnd, exception.Message, exception);
                }

                ThrowIfNullForNonNullableConstructorParameter(reader, contract, constructor, parameterIndex, value);
                args[parameterIndex] = value;
                paramSeen[parameterIndex] = true;
                continue;
            }

            if (contract.TryGetMember(key, out var member))
            {
                if (!member.CanWrite)
                {
                    if (contract.ExtensionData is not null)
                    {
                        var extValue = ReadExtensionDataValue(reader, contract.ExtensionData);
                        extensionEntries!.Add(new BufferedExtensionEntry(key, extValue, keyStart, keyEnd));
                    }
                    else
                    {
                        reader.Skip();
                    }

                    continue;
                }

                var value = ReadBufferedMemberValue(reader, member, keyStart, keyEnd);
                ThrowIfNullForNonNullableMember(reader, contract, member, value);
                memberValues[member] = new BufferedMemberAssignment(member, value, keyStart, keyEnd);
                continue;
            }

            if (contract.ExtensionData is not null)
            {
                var extValue = ReadExtensionDataValue(reader, contract.ExtensionData);
                extensionEntries!.Add(new BufferedExtensionEntry(key, extValue, keyStart, keyEnd));
                continue;
            }

            SkipOrThrowUnmappedMember(reader, contract, key);
        }

        reader.Read();
    }

    private static void WriteObjectCore(YamlWriter writer, object value, Contract contract)
    {
        writer.WriteStartMapping();

        var options = writer.Options;
        var members = options.MappingOrder == YamlMappingOrderPolicy.Sorted
            ? contract.MembersSorted
            : contract.MembersDeclaration;

        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if (!member.CanRead || member.IsIgnoredAsReadOnly(options))
            {
                continue;
            }

            var memberValue = member.GetValue(value);
            ThrowIfNullForNonNullableMember(writer.Options, contract.DeclaringType, member, memberValue);
            if (member.ShouldIgnoreOnWrite(memberValue, options))
            {
                continue;
            }

            writer.WritePropertyName(member.Name);
            var converter = member.Converter ??= writer.GetConverter(member.MemberType);
            using var styleScope = writer.PushBlockSequenceItemStyle(member.BlockSequenceMappingStyle, member.BlockSequenceSequenceStyle);
            using var stringStyleScope = writer.PushStringStyle(member.StringStyle);
            converter.Write(writer, memberValue);
        }

        WriteExtensionData(writer, value, contract, skippedKey: null);
        writer.WriteEndMapping();
    }

    private static void ThrowIfNullForNonNullableMember(YamlSerializerOptions options, Type declaringType, Member member, object? value)
    {
        if (value is null && options.RespectNullableAnnotations && member.DisallowNullOnSerialize)
        {
            throw YamlThrowHelper.ThrowNullForNonNullableMember(declaringType, member.Name);
        }
    }

    private static void ThrowIfNullForNonNullableMember(YamlReader reader, Contract contract, Member member, object? value)
    {
        if (value is null && reader.Options.RespectNullableAnnotations && member.DisallowNullOnDeserialize)
        {
            throw YamlThrowHelper.ThrowNullForNonNullableMember(reader, contract.DeclaringType, member.Name);
        }
    }

    private static void ThrowIfNullForNonNullableConstructorParameter(YamlReader reader, Contract contract, ConstructorModel constructor, int parameterIndex, object? value)
    {
        if (value is null && reader.Options.RespectNullableAnnotations && constructor.DisallowNull(parameterIndex))
        {
            throw YamlThrowHelper.ThrowNullForNonNullableConstructorParameter(reader, contract.DeclaringType, constructor.GetParameterName(parameterIndex));
        }
    }

    private YamlTypeClassifierContext? _classifierContext;

    private YamlTypeClassifierContext GetClassifierContext(PolymorphismModel polymorphism)
        => _classifierContext ??= YamlTypeClassifierContext.CreateForPolymorphicType(
            typeof(T),
            polymorphism.DerivedTypes,
            polymorphism.AcceptsPropertyDiscriminator ? polymorphism.DiscriminatorPropertyName : null);

    private T? ReadPolymorphic(YamlReader reader, Contract contract)
    {
        var polymorphism = contract.Polymorphism!;
        var rootTag = reader.Tag;
        var nodeStart = reader.Start;
        var nodeEnd = reader.End;

        // The node is the payload of this type, selected by a discriminator an enclosing polymorphic type consumed.
        var derivedTypeResolved = reader.IsCurrentNodeDerivedTypeResolved;

        // The discriminator is consumed here, so the selected type does not see it as one of its own entries.
        var buffered = YamlReader.BufferCurrentNodeToStringAndRemoveDiscriminator(
            reader,
            polymorphism.AcceptsPropertyDiscriminator ? polymorphism.DiscriminatorPropertyName : null,
            removeTag: polymorphism.AcceptsTagDiscriminator,
            out var discriminatorValue);

        Type? targetType = null;
        var isExplicitlySelected = false;
        if (polymorphism.AcceptsPropertyDiscriminator && discriminatorValue is not null)
        {
            if (polymorphism.TryGetDerivedTypeFromDiscriminator(discriminatorValue, out var derived))
            {
                targetType = derived;
                isExplicitlySelected = true;
            }
            else if (polymorphism.DefaultDerivedType is not null)
            {
                targetType = polymorphism.DefaultDerivedType;
            }
            else if (polymorphism.UnknownDerivedTypeHandling == YamlUnknownDerivedTypeHandling.Fail)
            {
                throw YamlThrowHelper.ThrowUnknownTypeDiscriminator(reader, nodeStart, nodeEnd, discriminatorValue, typeof(T));
            }
        }

        if (targetType is null && polymorphism.AcceptsTagDiscriminator && rootTag is not null)
        {
            if (polymorphism.TryGetDerivedTypeFromTag(rootTag, out var derivedFromTag))
            {
                targetType = derivedFromTag;
                isExplicitlySelected = true;
            }
            else if (polymorphism.DefaultDerivedType is not null)
            {
                targetType = polymorphism.DefaultDerivedType;
            }
            else if (polymorphism.UnknownDerivedTypeHandling == YamlUnknownDerivedTypeHandling.Fail)
            {
                throw YamlThrowHelper.ThrowUnknownTypeTag(reader, nodeStart, nodeEnd, rootTag, typeof(T));
            }
        }

        // An enclosing polymorphic type already selected this type, so neither the default derived type nor a
        // classifier can replace it. An abstract type cannot be read as itself, so it keeps selecting a derived type.
        if (targetType is null && derivedTypeResolved && !typeof(T).IsAbstract)
        {
            targetType = typeof(T);
        }

        // A registered classifier selects a derived type from the payload itself. It never overrides an explicit
        // discriminator or tag, so it only runs once those have failed to resolve a type.
        targetType ??= YamlTypeClassification.ClassifyBufferedNode(reader, buffered, GetClassifierContext(polymorphism));

        targetType ??= polymorphism.DefaultDerivedType ?? typeof(T);

        var bufferedReader = reader.CreateReader(buffered);
        if (!bufferedReader.Read())
        {
            return default;
        }

        if (targetType == typeof(T))
        {
            if (typeof(T).IsAbstract)
            {
                throw YamlThrowHelper.ThrowAbstractTypeWithoutDiscriminator(reader, nodeStart, nodeEnd, typeof(T));
            }

            return ReadObjectCore(bufferedReader, contract);
        }

        if (isExplicitlySelected)
        {
            bufferedReader.MarkCurrentNodeDerivedTypeResolved();
        }

        var converter = bufferedReader.GetConverter(targetType);
        var value = converter.Read(bufferedReader, targetType);
        return (T?)value;
    }

    private static void WritePolymorphic(YamlWriter writer, object value, Type runtimeType, Contract contract)
    {
        var polymorphism = contract.Polymorphism!;
        if (!polymorphism.TryGetDerivedTypeInfo(runtimeType, out var derivedInfo))
        {
            throw new NotSupportedException($"Type '{runtimeType}' is not a registered derived type of '{typeof(T)}'.");
        }

        if (polymorphism.EmitsTagDiscriminator && derivedInfo.Tag is not null)
        {
            writer.WriteTag(derivedInfo.Tag);
        }

        writer.WriteStartMapping();

        if (polymorphism.EmitsPropertyDiscriminator && derivedInfo.Discriminator is not null)
        {
            writer.WritePropertyName(polymorphism.DiscriminatorPropertyName);
            writer.WriteScalar(derivedInfo.Discriminator);
        }

        // An entry named like the discriminator property could not be read back, as it is consumed as the discriminator.
        var skippedPropertyName = polymorphism.EmitsPropertyDiscriminator ? polymorphism.DiscriminatorPropertyName : null;

        var options = writer.Options;
        var derivedContract = Contract.Create(runtimeType, writer);
        var members = options.MappingOrder == YamlMappingOrderPolicy.Sorted
            ? derivedContract.MembersSorted
            : derivedContract.MembersDeclaration;

        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if (!member.CanRead || member.IsIgnoredAsReadOnly(options))
            {
                continue;
            }

            if (string.Equals(member.Name, skippedPropertyName, StringComparison.Ordinal))
            {
                continue;
            }

            var memberValue = member.GetValue(value);
            ThrowIfNullForNonNullableMember(writer.Options, derivedContract.DeclaringType, member, memberValue);
            if (member.ShouldIgnoreOnWrite(memberValue, options))
            {
                continue;
            }

            writer.WritePropertyName(member.Name);
            var converter = member.Converter ??= writer.GetConverter(member.MemberType);
            using var styleScope = writer.PushBlockSequenceItemStyle(member.BlockSequenceMappingStyle, member.BlockSequenceSequenceStyle);
            using var stringStyleScope = writer.PushStringStyle(member.StringStyle);
            converter.Write(writer, memberValue);
        }

        WriteExtensionData(writer, value, derivedContract, skippedPropertyName);
        writer.WriteEndMapping();
    }

    private sealed class Contract
    {
        private readonly Dictionary<string, Member> _membersByName;
        private readonly Func<YamlReaderWriterBase, ConstructorResolution> _resolveConstructor;
        private ConstructorResolution? _constructorResolution;

        public Contract(
            Type declaringType,
            Func<YamlReaderWriterBase, ConstructorResolution> resolveConstructor,
            Member[] membersDeclaration,
            Member[] membersSorted,
            Dictionary<string, Member> membersByName,
            Member[] requiredMembers,
            ExtensionDataInfo? extensionData,
            PolymorphismModel? polymorphism,
            YamlUnmappedMemberHandling unmappedMemberHandling,
            YamlObjectCreationHandling preferredObjectCreationHandling)
        {
            DeclaringType = declaringType;
            _resolveConstructor = resolveConstructor;
            MembersDeclaration = membersDeclaration;
            MembersSorted = membersSorted;
            _membersByName = membersByName;
            RequiredMembers = requiredMembers;
            ExtensionData = extensionData;
            Polymorphism = polymorphism;
            UnmappedMemberHandling = unmappedMemberHandling;
            PreferredObjectCreationHandling = preferredObjectCreationHandling;
        }

        public Type DeclaringType { get; }

        public Func<object> CreateInstance => GetConstructorResolution().CreateInstance;

        public ConstructorModel? Constructor => GetConstructorResolution().Model;

        public Member[] MembersDeclaration { get; }

        public Member[] MembersSorted { get; }

        public Member[] RequiredMembers { get; }

        public ExtensionDataInfo? ExtensionData { get; }

        public PolymorphismModel? Polymorphism { get; }

        public YamlUnmappedMemberHandling UnmappedMemberHandling { get; }

        public YamlObjectCreationHandling PreferredObjectCreationHandling { get; }

        internal const string EnumerableWithoutMembersReason = "it is enumerable, but it is not a supported collection and has no serializable member, so its elements would be lost. Implement ICollection<T> with a public parameterless constructor, use a supported collection, or register a converter.";

        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2070",
            Justification = "Contract discovery uses reflection and is only exercised by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2067",
            Justification = "Contract discovery and instance creation use reflection and are only exercised by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2075",
            Justification = "Contract discovery walks the base types using reflection and is only exercised by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
        public static Contract Create(Type type, YamlReaderWriterBase readerWriter)
        {
            ArgumentNullException.ThrowIfNull(readerWriter);
            var options = readerWriter.Options;
            var unmappedMemberHandling = GetUnmappedMemberHandling(type, options);
            var preferredObjectCreationHandling = GetPreferredObjectCreationHandling(type, options);
            var nullabilityContext = options.RespectNullableAnnotations ? new NullabilityInfoContext() : null;

            var members = new List<Member>();
            var requiredMembers = new List<Member>();
            ExtensionDataInfo? extensionData = null;

            // Members are discovered from the base type down to the declaring type, in declaration order. A member that
            // overrides or hides a member with the same name takes the position of the member it replaces, so the
            // contract keeps a single member per name and matches the order used by the source generator.
            var discoveredMembers = new List<MemberInfo?>();
            var discoveredMemberIndexByName = new Dictionary<string, int>(StringComparer.Ordinal);
            MemberInfo? extensionDataMember = null;
            foreach (var currentType in GetTypeHierarchy(type))
            {
                const BindingFlags DeclaredInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                foreach (var property in currentType.GetProperties(DeclaredInstance))
                {
                    if (property.GetIndexParameters().Length != 0)
                    {
                        continue;
                    }

                    if (IsExtensionData(property))
                    {
                        extensionDataMember = SelectExtensionDataMember(type, extensionDataMember, property);
                        continue;
                    }

                    var canRead = property.GetMethod is not null && (property.GetMethod.IsPublic || IsIncluded(property));
                    if (canRead)
                    {
                        AddDiscoveredMember(discoveredMembers, discoveredMemberIndexByName, property, isIgnored: GetIgnoreCondition(property, type) == YamlIgnoreCondition.Always);
                    }
                }

                foreach (var field in currentType.GetFields(DeclaredInstance))
                {
                    if (IsExtensionData(field))
                    {
                        extensionDataMember = SelectExtensionDataMember(type, extensionDataMember, field);
                        continue;
                    }

                    var canRead = IsIncluded(field) || (options.IncludeFields && field.IsPublic);
                    if (canRead)
                    {
                        AddDiscoveredMember(discoveredMembers, discoveredMemberIndexByName, field, isIgnored: GetIgnoreCondition(field, type) == YamlIgnoreCondition.Always);
                    }
                }
            }

            switch (extensionDataMember)
            {
                case PropertyInfo property:
                {
                    if (GetIgnoreCondition(property) is not null and not YamlIgnoreCondition.Never)
                    {
                        throw new NotSupportedException($"Extension data member '{property.Name}' on '{type}' cannot be ignored.");
                    }

                    if (IsRequired(property))
                    {
                        throw new NotSupportedException($"Extension data member '{property.Name}' on '{type}' cannot be required.");
                    }

                    var hasIncludeAttr = IsIncluded(property);
                    if (property.GetMethod is null || !(property.GetMethod.IsPublic || hasIncludeAttr))
                    {
                        throw new NotSupportedException($"Extension data member '{property.Name}' on '{type}' must be readable.");
                    }

                    var canWrite = property.SetMethod is not null && (property.SetMethod.IsPublic || hasIncludeAttr);
                    var extensionMember = new Member(property.Name, order: 0, declarationOrder: 0, property.PropertyType, property, ignoreCondition: null, isRequired: false, canWrite, objectCreationHandling: null);
                    extensionData = ExtensionDataInfo.Create(type, extensionMember, property.PropertyType);
                    break;
                }

                case FieldInfo field:
                {
                    if (GetIgnoreCondition(field) is not null and not YamlIgnoreCondition.Never)
                    {
                        throw new NotSupportedException($"Extension data member '{field.Name}' on '{type}' cannot be ignored.");
                    }

                    if (IsRequired(field))
                    {
                        throw new NotSupportedException($"Extension data member '{field.Name}' on '{type}' cannot be required.");
                    }

                    var extensionMember = new Member(field.Name, order: 0, declarationOrder: 0, field.FieldType, field, ignoreCondition: null, isRequired: false, canWrite: !field.IsInitOnly, objectCreationHandling: null);
                    extensionData = ExtensionDataInfo.Create(type, extensionMember, field.FieldType);
                    break;
                }
            }

            for (var declarationOrder = 0; declarationOrder < discoveredMembers.Count; declarationOrder++)
            {
                Member member;
                switch (discoveredMembers[declarationOrder])
                {
                    case null:
                        continue;

                    case PropertyInfo property:
                    {
                        var canWrite = property.SetMethod is not null && (property.SetMethod.IsPublic || IsIncluded(property));
                        var (mappingStyle, sequenceStyle) = GetBlockSequenceItemStyles(property);
                        member = new Member(
                            GetMemberName(property, type, readerWriter),
                            GetMemberOrder(property),
                            declarationOrder,
                            property.PropertyType,
                            property,
                            GetIgnoreCondition(property, type),
                            IsRequired(property),
                            canWrite,
                            GetObjectCreationHandling(property),
                            mappingStyle,
                            sequenceStyle,
                            GetStringStyle(property),
                            DisallowNullOnSerialize(nullabilityContext, property),
                            DisallowNullOnDeserialize(nullabilityContext, property),
                            isReadOnlyProperty: !canWrite);
                        member.Converter = CreateConverterFromAttribute(property, property.PropertyType, options)
                            ?? CreateNumberHandlingConverter(property, property.PropertyType, type, readerWriter);
                        break;
                    }

                    case FieldInfo field:
                    {
                        var (mappingStyle, sequenceStyle) = GetBlockSequenceItemStyles(field);
                        member = new Member(
                            GetMemberName(field, type, readerWriter),
                            GetMemberOrder(field),
                            declarationOrder,
                            field.FieldType,
                            field,
                            GetIgnoreCondition(field, type),
                            IsRequired(field),
                            canWrite: !field.IsInitOnly,
                            GetObjectCreationHandling(field),
                            mappingStyle,
                            sequenceStyle,
                            GetStringStyle(field),
                            DisallowNullOnSerialize(nullabilityContext, field),
                            DisallowNullOnDeserialize(nullabilityContext, field),
                            isReadOnlyField: field.IsInitOnly);
                        member.Converter = CreateConverterFromAttribute(field, field.FieldType, options)
                            ?? CreateNumberHandlingConverter(field, field.FieldType, type, readerWriter);
                        break;
                    }

                    default:
                        throw new InvalidOperationException($"Unexpected member '{discoveredMembers[declarationOrder]}'.");
                }

                member.ReadsNullScalarAsNull = member.Converter is null && ReadsNullScalarAsNull(member.MemberType, readerWriter);
                members.Add(member);
                if (member.IsRequired && !member.ShouldIgnoreOnRead)
                {
                    requiredMembers.Add(member);
                }
            }

            // An enumerable type that no collection converter handles is serialized as an object. Without any member, it
            // would be written as an empty mapping and its elements silently lost. The source generator reports it too.
            if (members.Count == 0 && extensionData is null && typeof(IEnumerable).IsAssignableFrom(type))
            {
                throw new NotSupportedException($"Type '{type}' is not supported: {EnumerableWithoutMembersReason}");
            }

            // Two members cannot share a YAML name: the mapping would contain the key twice. Members whose names only differ
            // by case are allowed; when names are matched case-insensitively, the first declared member wins.
            var map = new Dictionary<string, Member>(members.Count, readerWriter.PropertyNameComparer);
            var membersByOrdinalName = new Dictionary<string, Member>(members.Count, StringComparer.Ordinal);
            foreach (var member in members)
            {
                if (!membersByOrdinalName.TryAdd(member.Name, member))
                {
                    throw new InvalidOperationException($"Members '{membersByOrdinalName[member.Name].ClrName}' and '{member.ClrName}' of '{type}' both map to the YAML member name '{member.Name}'.");
                }

                map.TryAdd(member.Name, member);
            }

            // The members are sorted into a copy: the deserialization constructor binds its parameters to the members in
            // discovery order.
            var membersDeclaration = members.ToArray();
            Array.Sort(membersDeclaration, static (x, y) =>
            {
                var orderCompare = x.Order.CompareTo(y.Order);
                return orderCompare != 0 ? orderCompare : x.DeclarationOrder.CompareTo(y.DeclarationOrder);
            });

            var membersSorted = (Member[])membersDeclaration.Clone();
            Array.Sort(membersSorted, static (x, y) =>
            {
                var orderCompare = x.Order.CompareTo(y.Order);
                if (orderCompare != 0)
                {
                    return orderCompare;
                }

                var nameCompare = string.CompareOrdinal(x.Name, y.Name);
                return nameCompare != 0 ? nameCompare : x.DeclarationOrder.CompareTo(y.DeclarationOrder);
            });

            var polymorphism = PolymorphismModel.TryCreate(type, options);
            for (var i = 0; i < requiredMembers.Count; i++)
            {
                requiredMembers[i].RequiredIndex = i;
            }

            return new Contract(type, ResolveConstructor, membersDeclaration, membersSorted, map, requiredMembers.ToArray(), extensionData, polymorphism, unmappedMemberHandling, preferredObjectCreationHandling);

            ConstructorResolution ResolveConstructor(YamlReaderWriterBase constructorReaderWriter)
            {
                var selectedConstructor = SelectDeserializationConstructor(type);
                ConstructorModel? constructorModel = null;

                Func<object> createInstance = () =>
                {
                    if (type.IsAbstract || type.IsInterface)
                    {
                        throw new NotSupportedException($"Type '{type}' cannot be instantiated.");
                    }

                    object? instance;
                    try
                    {
                        instance = Activator.CreateInstance(type);
                    }
                    catch (MissingMethodException exception)
                    {
                        throw new NotSupportedException($"Type '{type}' does not have a public parameterless constructor.", exception);
                    }

                    if (instance is null)
                    {
                        throw new NotSupportedException($"Type '{type}' does not have a public parameterless constructor.");
                    }

                    return instance;
                };

                if (selectedConstructor is not null)
                {
                    var parameters = selectedConstructor.GetParameters();
                    if (parameters.Length == 0)
                    {
                        createInstance = () =>
                        {
                            if (type.IsAbstract || type.IsInterface)
                            {
                                throw new NotSupportedException($"Type '{type}' cannot be instantiated.");
                            }

                            return selectedConstructor.Invoke(null)
                                   ?? throw new NotSupportedException($"Type '{type}' could not be instantiated.");
                        };
                    }
                    else
                    {
                        // A NullabilityInfoContext is not thread-safe, and the constructor can be resolved concurrently.
                        var constructorNullabilityContext = options.RespectNullableAnnotations ? new NullabilityInfoContext() : null;
                        constructorModel = new ConstructorModel(selectedConstructor, type, members, constructorReaderWriter, constructorNullabilityContext);
                        createInstance = () => throw new NotSupportedException($"Type '{type}' must be deserialized using a parameterized constructor.");
                    }
                }

                return new ConstructorResolution(createInstance, constructorModel);
            }
        }

        public bool TryGetMember(string name, out Member member) => _membersByName.TryGetValue(name, out member!);

        /// <summary>Selects the deserialization constructor, which <see cref="CreateInstance"/> and <see cref="Constructor"/> use.</summary>
        /// <remarks>
        /// The constructor is only needed to read the type, so it is selected the first time the type is read: a type
        /// without a usable constructor can still be serialized, like the source-generated serializer does.
        /// </remarks>
        public void EnsureConstructor(YamlReaderWriterBase readerWriter)
            => _constructorResolution ??= _resolveConstructor(readerWriter);

        private ConstructorResolution GetConstructorResolution()
            => _constructorResolution ?? throw new InvalidOperationException($"The deserialization constructor of '{DeclaringType}' was not selected.");
    }

    private sealed record ConstructorResolution(Func<object> CreateInstance, ConstructorModel? Model);

    private enum ExtensionDataKind
    {
        Dictionary,
        ReadOnlyDictionary,
        Mapping,
    }

    private sealed class ExtensionDataInfo
    {
        private ExtensionDataInfo(Member member, ExtensionDataKind kind, Type? dictionaryValueType, Func<object> createContainer, Type? containerType = null, Func<object, IEnumerable<KeyValuePair<string, object?>>>? enumerateEntries = null)
        {
            Member = member;
            Kind = kind;
            DictionaryValueType = dictionaryValueType;
            CreateContainer = createContainer;
            ContainerType = containerType;
            EnumerateEntries = enumerateEntries;
        }

        public Member Member { get; }

        public ExtensionDataKind Kind { get; }

        public Type? DictionaryValueType { get; }

        public Func<object> CreateContainer { get; }

        /// <summary>
        /// Type of the mutable container created by <see cref="CreateContainer"/>. Only set for <see cref="ExtensionDataKind.ReadOnlyDictionary"/>.
        /// </summary>
        public Type? ContainerType { get; }

        /// <summary>
        /// Enumerates the entries of a read-only dictionary container. Only set for <see cref="ExtensionDataKind.ReadOnlyDictionary"/>.
        /// </summary>
        public Func<object, IEnumerable<KeyValuePair<string, object?>>>? EnumerateEntries { get; }

        [UnconditionalSuppressMessage(
            "AOT",
            "IL3050",
            Justification = "Extension-data container instantiation uses reflection and is only exercised by reflection-based serialization. NativeAOT scenarios should use source-generated metadata.")]
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2067",
            Justification = "Extension-data container instantiation uses reflection and is only exercised by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
        public static ExtensionDataInfo Create(Type declaringType, Member member, Type memberType)
        {
            ArgumentNullException.ThrowIfNull(declaringType);
            ArgumentNullException.ThrowIfNull(member);
            ArgumentNullException.ThrowIfNull(memberType);

            if (typeof(YamlMapping).IsAssignableFrom(memberType))
            {
                object CreateMapping()
                {
                    if (memberType == typeof(YamlMapping))
                    {
                        return new YamlMapping();
                    }

                    if (memberType.IsAbstract || memberType.IsInterface)
                    {
                        throw new NotSupportedException($"Extension data member '{member.Name}' on '{declaringType}' must be a concrete '{typeof(YamlMapping)}' type.");
                    }

                    return Activator.CreateInstance(memberType)
                           ?? throw new NotSupportedException($"Extension data member '{member.Name}' on '{declaringType}' could not be instantiated.");
                }

                return new ExtensionDataInfo(member, ExtensionDataKind.Mapping, dictionaryValueType: null, CreateMapping);
            }

            var kind = ExtensionDataKind.Dictionary;
            if (!TryGetExtensionDataDictionaryValueType(memberType, typeof(IDictionary<,>), out var valueType))
            {
                if (!TryGetExtensionDataDictionaryValueType(memberType, typeof(IReadOnlyDictionary<,>), out valueType))
                {
                    throw new NotSupportedException($"Extension data member '{member.Name}' on '{declaringType}' must be a '{typeof(YamlMapping)}' or implement 'IDictionary<string, object>', 'IDictionary<string, YamlNode>', 'IReadOnlyDictionary<string, object>', or 'IReadOnlyDictionary<string, YamlNode>'.");
                }

                kind = ExtensionDataKind.ReadOnlyDictionary;
            }

            Type createType;
            if (valueType == typeof(object))
            {
                createType = typeof(Dictionary<string, object?>);
            }
            else if (typeof(YamlNode).IsAssignableFrom(valueType))
            {
                createType = typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType);
            }
            else
            {
                throw new NotSupportedException($"Extension data dictionary member '{member.Name}' on '{declaringType}' must use 'object' or '{typeof(YamlNode)}' values.");
            }

            if (!memberType.IsAssignableFrom(createType))
            {
                throw new NotSupportedException($"Extension data dictionary member '{member.Name}' on '{declaringType}' must be assignable from '{createType}'.");
            }

            object CreateDictionary()
            {
                return Activator.CreateInstance(createType)
                       ?? throw new NotSupportedException($"Extension data member '{member.Name}' on '{declaringType}' could not be instantiated.");
            }

            if (kind == ExtensionDataKind.ReadOnlyDictionary)
            {
                return new ExtensionDataInfo(member, kind, valueType, CreateDictionary, createType, CreateReadOnlyDictionaryEnumerator(valueType));
            }

            return new ExtensionDataInfo(member, kind, valueType, CreateDictionary);
        }

        [UnconditionalSuppressMessage(
            "AOT",
            "IL3050",
            Justification = "Extension-data enumeration uses reflection and is only exercised by reflection-based serialization. NativeAOT scenarios should use source-generated metadata.")]
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2060",
            Justification = "Extension-data enumeration uses reflection and is only exercised by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
        private static Func<object, IEnumerable<KeyValuePair<string, object?>>> CreateReadOnlyDictionaryEnumerator(Type valueType)
        {
            var method = typeof(YamlObjectConverter<T>)
                .GetMethod(nameof(EnumerateReadOnlyDictionary), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(valueType);

            return method.CreateDelegate<Func<object, IEnumerable<KeyValuePair<string, object?>>>>();
        }

        private static bool TryGetExtensionDataDictionaryValueType(Type type, Type dictionaryDefinition, out Type valueType)
        {
            valueType = null!;

            if (TryGetDictionaryInterface(type, dictionaryDefinition, out var dictionaryInterface))
            {
                valueType = dictionaryInterface.GetGenericArguments()[1];
                if (valueType == typeof(object))
                {
                    return true;
                }

                if (typeof(YamlNode).IsAssignableFrom(valueType))
                {
                    return true;
                }
            }

            return false;
        }

        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2070",
            Justification = "Extension-data dictionary detection uses reflection and is only exercised by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
        private static bool TryGetDictionaryInterface(Type type, Type dictionaryDefinition, out Type dictionaryInterface)
        {
            dictionaryInterface = null!;

            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                if (definition == dictionaryDefinition && type.GetGenericArguments()[0] == typeof(string))
                {
                    dictionaryInterface = type;
                    return true;
                }
            }

            var interfaces = type.GetInterfaces();
            for (var i = 0; i < interfaces.Length; i++)
            {
                var candidate = interfaces[i];
                if (!candidate.IsGenericType)
                {
                    continue;
                }

                var definition = candidate.GetGenericTypeDefinition();
                if (definition == dictionaryDefinition && candidate.GetGenericArguments()[0] == typeof(string))
                {
                    dictionaryInterface = candidate;
                    return true;
                }
            }

            return false;
        }
    }

    private sealed class ConstructorModel
    {
        private readonly ConstructorInfo _constructor;
        private readonly ParameterInfo[] _parameters;
        private readonly Type[] _parameterTypes;
        private readonly bool[] _parametersDisallowNull;
        private readonly YamlConverter?[] _parameterConverters;
        private readonly Dictionary<string, int> _parameterIndexByYamlName;

        public ConstructorModel(ConstructorInfo constructor, Type declaringType, IReadOnlyList<Member> members, YamlReaderWriterBase readerWriter, NullabilityInfoContext? nullabilityContext)
        {
            ArgumentNullException.ThrowIfNull(constructor);
            ArgumentNullException.ThrowIfNull(declaringType);
            ArgumentNullException.ThrowIfNull(members);
            ArgumentNullException.ThrowIfNull(readerWriter);

            _constructor = constructor;
            _parameters = constructor.GetParameters();
            _parameterTypes = new Type[_parameters.Length];
            _parametersDisallowNull = new bool[_parameters.Length];
            _parameterConverters = new YamlConverter?[_parameters.Length];

            var clrNameToMember = new Dictionary<string, Member>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];
                clrNameToMember.TryAdd(member.ClrName, member);
            }

            var declaredPolicy = GetDeclaredNamingPolicy(declaringType);
            _parameterIndexByYamlName = new Dictionary<string, int>(readerWriter.PropertyNameComparer);
            for (var i = 0; i < _parameters.Length; i++)
            {
                var parameter = _parameters[i];
                var parameterName = parameter.Name ?? throw new NotSupportedException($"Constructor '{constructor}' defines a parameter without a name.");

                var boundMember = clrNameToMember.GetValueOrDefault(parameterName);
                var yamlName = boundMember is not null
                    ? boundMember.Name
                    : declaredPolicy is not null
                        ? ApplyNamingPolicy(parameterName, declaredPolicy.GetValueOrDefault())
                        : readerWriter.ConvertName(parameterName);

                if (_parameterIndexByYamlName.ContainsKey(yamlName))
                {
                    throw new NotSupportedException($"Constructor '{constructor}' defines multiple parameters that bind to mapping key '{yamlName}'.");
                }

                _parameterIndexByYamlName.Add(yamlName, i);
                _parameterTypes[i] = parameter.ParameterType;
                _parametersDisallowNull[i] = DisallowNullOnDeserialize(nullabilityContext, parameter);

                // The member a parameter binds to is written with its [YamlConverter] or [YamlNumberHandling], so the
                // parameter is read with it too. Other members only get a converter once they are read.
                if (boundMember?.MemberType == parameter.ParameterType)
                {
                    _parameterConverters[i] = boundMember.Converter;
                }
            }
        }

        public int ParameterCount => _parameters.Length;

        public bool TryGetParameterIndex(string yamlKey, out int index)
            => _parameterIndexByYamlName.TryGetValue(yamlKey, out index);

        public Type GetParameterType(int index) => _parameterTypes[index];

        public string GetParameterName(int index) => _parameters[index].Name ?? string.Empty;

        public bool DisallowNull(int index) => _parametersDisallowNull[index];

        public YamlConverter? GetParameterConverter(int index) => _parameterConverters[index];

        public bool TryGetDefaultValue(int index, out object? value)
        {
            var parameter = _parameters[index];
            if (parameter.HasDefaultValue)
            {
                value = parameter.DefaultValue;

                // The default value of a nullable enum parameter is reported as its underlying integral value, which
                // cannot be passed to the constructor.
                if (value is not null && Nullable.GetUnderlyingType(parameter.ParameterType) is { IsEnum: true } enumType && value.GetType() != enumType)
                {
                    value = Enum.ToObject(enumType, value);
                }

                return true;
            }

            value = null;
            return false;
        }

        public object CreateInstance(object?[] args)
        {
            return _constructor.Invoke(args)
                   ?? throw new NotSupportedException($"Constructor '{_constructor}' returned null.");
        }
    }

    private sealed class PolymorphismModel
    {
        private readonly Dictionary<string, Type> _discriminatorToType;
        private readonly Dictionary<string, Type> _tagToType;
        private readonly Dictionary<Type, DerivedTypeInfo> _typeToDerived;

        private PolymorphismModel(
            string discriminatorPropertyName,
            YamlTypeDiscriminatorStyle style,
            YamlUnknownDerivedTypeHandling unknownDerivedTypeHandling,
            Dictionary<string, Type> discriminatorToType,
            Dictionary<string, Type> tagToType,
            Dictionary<Type, DerivedTypeInfo> typeToDerived,
            Type? defaultDerivedType)
        {
            DiscriminatorPropertyName = discriminatorPropertyName;
            Style = style;
            UnknownDerivedTypeHandling = unknownDerivedTypeHandling;
            _discriminatorToType = discriminatorToType;
            _tagToType = tagToType;
            _typeToDerived = typeToDerived;
            DefaultDerivedType = defaultDerivedType;
        }

        public string DiscriminatorPropertyName { get; }

        public YamlTypeDiscriminatorStyle Style { get; }

        public YamlUnknownDerivedTypeHandling UnknownDerivedTypeHandling { get; }

        public Type? DefaultDerivedType { get; }

        public bool AcceptsPropertyDiscriminator => Style is YamlTypeDiscriminatorStyle.Property or YamlTypeDiscriminatorStyle.Both;

        public bool AcceptsTagDiscriminator => Style is YamlTypeDiscriminatorStyle.Tag or YamlTypeDiscriminatorStyle.Both;

        public bool EmitsPropertyDiscriminator => Style is YamlTypeDiscriminatorStyle.Property or YamlTypeDiscriminatorStyle.Both;

        public bool EmitsTagDiscriminator => Style is YamlTypeDiscriminatorStyle.Tag or YamlTypeDiscriminatorStyle.Both;

        public bool TryGetDerivedTypeFromDiscriminator(string discriminator, out Type derivedType)
            => _discriminatorToType.TryGetValue(discriminator, out derivedType!);

        public bool TryGetDerivedTypeFromTag(string tag, out Type derivedType)
            => _tagToType.TryGetValue(tag, out derivedType!);

        public bool TryGetDerivedTypeInfo(Type derivedType, out DerivedTypeInfo info)
            => _typeToDerived.TryGetValue(derivedType, out info);

        private YamlDerivedType[]? _derivedTypes;

        /// <summary>Gets the registered derived types, in the shape a <see cref="YamlTypeClassifierFactory"/> consumes.</summary>
        public IReadOnlyList<YamlDerivedType> DerivedTypes
        {
            get
            {
                if (_derivedTypes is not null)
                {
                    return _derivedTypes;
                }

                var derivedTypes = new YamlDerivedType[_typeToDerived.Count];
                var index = 0;
                foreach (var entry in _typeToDerived)
                {
                    var info = entry.Value;
                    derivedTypes[index++] = info.Discriminator is null
                        ? new YamlDerivedType(entry.Key) { Tag = info.Tag }
                        : new YamlDerivedType(entry.Key, info.Discriminator) { Tag = info.Tag };
                }

                return _derivedTypes = derivedTypes;
            }
        }

        public static PolymorphismModel? TryCreate(Type type, YamlSerializerOptions options)
        {
            var yamlDerived = type.GetCustomAttributes(typeof(YamlDerivedTypeAttribute), inherit: false);

            var hasRuntimeMappings = options.PolymorphismOptions.DerivedTypeMappings.TryGetValue(type, out var runtimeDerived)
                                     && runtimeDerived is { Count: > 0 };

            var yamlPolymorphic = type.GetCustomAttribute<YamlPolymorphicAttribute>(inherit: false);

            // A value set on the declaration overrides the serializer-level setting; the serializer-level value applies when unset.
            var inferOverride = yamlPolymorphic?.InferClosedTypePolymorphismOrNull;
            var hasExplicitRegistrations = yamlDerived.Length != 0 || hasRuntimeMappings;
            var infer = (inferOverride ?? options.PolymorphismOptions.InferClosedTypePolymorphism) && !hasExplicitRegistrations;

            // The closed type metadata is only read when inference could apply, so ordinary type resolution does not
            // pay for it: an explicit opt-in always needs the answer to be validated, while the serializer-level
            // opt-in only matters when the declaration registers no derived type.
            Type[]? inferredDerivedTypes = null;
            if (inferOverride is true || infer)
            {
                var isClosedType = YamlClosedTypeHelper.IsClosedType(type, out var closedDerivedTypes);
                if (inferOverride is true && !isClosedType)
                {
                    throw new InvalidOperationException($"Type '{type}' enables '{nameof(YamlPolymorphicAttribute)}.{nameof(YamlPolymorphicAttribute.InferClosedTypePolymorphism)}' but is not a closed type, so no derived type can be inferred. Declare the type 'closed' or register its derived types using '{nameof(YamlDerivedTypeAttribute)}'.");
                }

                if (infer)
                {
                    inferredDerivedTypes = closedDerivedTypes;
                }
            }

            if (!hasExplicitRegistrations && inferredDerivedTypes is null)
            {
                return null;
            }

            var style = options.PolymorphismOptions.DiscriminatorStyle;
            if (yamlPolymorphic is not null && yamlPolymorphic.DiscriminatorStyle != YamlTypeDiscriminatorStyle.Unspecified)
            {
                style = yamlPolymorphic.DiscriminatorStyle;
            }

            var discriminatorPropertyName = yamlPolymorphic?.TypeDiscriminatorPropertyName;
            discriminatorPropertyName = string.IsNullOrWhiteSpace(discriminatorPropertyName)
                ? options.PolymorphismOptions.TypeDiscriminatorPropertyName
                : discriminatorPropertyName;

            var unknownHandling = options.PolymorphismOptions.UnknownDerivedTypeHandling;
            if (yamlPolymorphic is not null && yamlPolymorphic.UnknownDerivedTypeHandling != YamlUnknownDerivedTypeHandling.Unspecified)
            {
                unknownHandling = yamlPolymorphic.UnknownDerivedTypeHandling;
            }

            var discriminatorToType = new Dictionary<string, Type>(StringComparer.Ordinal);
            var tagToType = new Dictionary<string, Type>(StringComparer.Ordinal);
            var typeToDerived = new Dictionary<Type, DerivedTypeInfo>();
            Type? defaultDerivedType = null;

            // Registrations from the same source must not overlap: which registration would win is not obvious, and
            // the discriminator written for a type registered twice could not be told from the one read back.
            foreach (YamlDerivedTypeAttribute attribute in yamlDerived)
            {
                var derivedType = YamlDerivedTypeHelper.ResolveDerivedType(type, attribute.DerivedType);
                if (!type.IsAssignableFrom(derivedType))
                {
                    throw new InvalidOperationException($"Derived type '{derivedType}' is not assignable to '{type}'.");
                }

                ThrowIfDuplicateRegistration(type, derivedType, attribute.Discriminator, attribute.Tag, defaultDerivedType, discriminatorToType, tagToType, typeToDerived);
                AddRegistration(derivedType, attribute.Discriminator, attribute.Tag, ref defaultDerivedType, discriminatorToType, tagToType, typeToDerived);
            }

            if (hasRuntimeMappings)
            {
                // A runtime mapping overlapping an attribute registration is skipped, as attributes take precedence.
                // Runtime mappings overlapping each other are rejected, like attribute registrations are.
                var runtimeDiscriminatorToType = new Dictionary<string, Type>(StringComparer.Ordinal);
                var runtimeTagToType = new Dictionary<string, Type>(StringComparer.Ordinal);
                var runtimeTypeToDerived = new Dictionary<Type, DerivedTypeInfo>();
                Type? runtimeDefaultDerivedType = null;
                foreach (var entry in runtimeDerived!)
                {
                    var derivedType = YamlDerivedTypeHelper.ResolveDerivedType(type, entry.DerivedType);
                    if (!type.IsAssignableFrom(derivedType))
                    {
                        throw new InvalidOperationException($"Derived type '{derivedType}' is not assignable to '{type}'.");
                    }

                    ThrowIfDuplicateRegistration(type, derivedType, entry.Discriminator, entry.Tag, runtimeDefaultDerivedType, runtimeDiscriminatorToType, runtimeTagToType, runtimeTypeToDerived);
                    AddRegistration(derivedType, entry.Discriminator, entry.Tag, ref runtimeDefaultDerivedType, runtimeDiscriminatorToType, runtimeTagToType, runtimeTypeToDerived);

                    var isDefaultMapping = entry.Discriminator is null && entry.Tag is null;
                    if (ShouldAddLowerPrecedenceMapping(
                        derivedType,
                        entry.Discriminator,
                        entry.Tag,
                        isDefaultMapping,
                        defaultDerivedType,
                        discriminatorToType,
                        tagToType,
                        typeToDerived))
                    {
                        AddRegistration(derivedType, entry.Discriminator, entry.Tag, ref defaultDerivedType, discriminatorToType, tagToType, typeToDerived);
                    }
                }
            }

            if (inferredDerivedTypes is not null)
            {
                // A derived type that is itself closed brings its own hierarchy along: the whole hierarchy is known at
                // compile time, so every descendant is registered instead of only the direct derived types. Each level
                // is resolved against the type declaring it so an open generic derived type unifies with its own base.
                var pendingHierarchies = new Queue<(Type BaseType, Type[] DerivedTypes)>();
                pendingHierarchies.Enqueue((type, inferredDerivedTypes));

                while (pendingHierarchies.Count > 0)
                {
                    var (declaringType, declaredDerivedTypes) = pendingHierarchies.Dequeue();
                    foreach (var inferredDerivedType in declaredDerivedTypes)
                    {
                        var derivedType = YamlDerivedTypeHelper.ResolveDerivedType(declaringType, inferredDerivedType);
                        if (!type.IsAssignableFrom(derivedType))
                        {
                            throw new InvalidOperationException($"Derived type '{derivedType}' is not assignable to '{type}'.");
                        }

                        if (!YamlClosedTypeHelper.IsAtLeastAsVisibleAs(derivedType, type))
                        {
                            throw new InvalidOperationException($"Derived type '{derivedType}' inferred for the closed type '{type}' is less visible than '{type}' and cannot be registered.");
                        }

                        var discriminator = YamlClosedTypeHelper.GetInferredDiscriminator(derivedType);
                        if (discriminatorToType.ContainsKey(discriminator))
                        {
                            throw new InvalidOperationException($"Derived type '{derivedType}' inferred for the closed type '{type}' uses the discriminator '{discriminator}', which is already registered by another derived type.");
                        }

                        discriminatorToType.Add(discriminator, derivedType);
                        typeToDerived[derivedType] = new DerivedTypeInfo(discriminator, tag: null);

                        // Only a newly registered type is expanded, and a type can only be registered once, so the
                        // traversal always terminates.
                        if (YamlClosedTypeHelper.IsClosedType(derivedType, out var nestedDerivedTypes) && nestedDerivedTypes is not null)
                        {
                            pendingHierarchies.Enqueue((derivedType, nestedDerivedTypes));
                        }
                    }
                }
            }

            return new PolymorphismModel(discriminatorPropertyName, style, unknownHandling, discriminatorToType, tagToType, typeToDerived, defaultDerivedType);
        }

        private static void ThrowIfDuplicateRegistration(
            Type type,
            Type derivedType,
            string? discriminator,
            string? tag,
            Type? defaultDerivedType,
            Dictionary<string, Type> discriminatorToType,
            Dictionary<string, Type> tagToType,
            Dictionary<Type, DerivedTypeInfo> typeToDerived)
        {
            if (typeToDerived.ContainsKey(derivedType))
            {
                throw new InvalidOperationException($"The polymorphic type '{type}' registers the derived type '{derivedType}' more than once.");
            }

            if (discriminator is not null && discriminatorToType.TryGetValue(discriminator, out var discriminatorType))
            {
                throw new InvalidOperationException($"The polymorphic type '{type}' registers the discriminator '{discriminator}' for both '{discriminatorType}' and '{derivedType}'.");
            }

            if (tag is not null && tagToType.TryGetValue(tag, out var tagType))
            {
                throw new InvalidOperationException($"The polymorphic type '{type}' registers the tag '{tag}' for both '{tagType}' and '{derivedType}'.");
            }

            if (discriminator is null && tag is null && defaultDerivedType is not null)
            {
                throw new InvalidOperationException($"The polymorphic type '{type}' registers both '{defaultDerivedType}' and '{derivedType}' as its default derived type, without a discriminator or a tag.");
            }
        }

        private static void AddRegistration(
            Type derivedType,
            string? discriminator,
            string? tag,
            ref Type? defaultDerivedType,
            Dictionary<string, Type> discriminatorToType,
            Dictionary<string, Type> tagToType,
            Dictionary<Type, DerivedTypeInfo> typeToDerived)
        {
            if (discriminator is null && tag is null)
            {
                defaultDerivedType = derivedType;
            }

            if (discriminator is not null)
            {
                discriminatorToType.Add(discriminator, derivedType);
            }

            if (tag is not null)
            {
                tagToType.Add(tag, derivedType);
            }

            typeToDerived.Add(derivedType, new DerivedTypeInfo(discriminator, tag));
        }

        private static bool ShouldAddLowerPrecedenceMapping(
            Type derivedType,
            string? discriminator,
            string? tag,
            bool isDefaultMapping,
            Type? defaultDerivedType,
            Dictionary<string, Type> discriminatorToType,
            Dictionary<string, Type> tagToType,
            Dictionary<Type, DerivedTypeInfo> typeToDerived)
        {
            if (typeToDerived.ContainsKey(derivedType))
            {
                return false;
            }

            if (isDefaultMapping)
            {
                if (defaultDerivedType is not null)
                {
                    return false;
                }
            }
            else if (discriminator is not null && discriminatorToType.ContainsKey(discriminator))
            {
                return false;
            }

            if (tag is not null && tagToType.ContainsKey(tag))
            {
                return false;
            }

            return true;
        }

        public readonly struct DerivedTypeInfo
        {
            public DerivedTypeInfo(string? discriminator, string? tag)
            {
                Discriminator = discriminator;
                Tag = tag;
            }

            public string? Discriminator { get; }

            public string? Tag { get; }
        }
    }

    private sealed class Member
    {
        private readonly PropertyInfo? _property;
        private readonly FieldInfo? _field;

        public Member(
            string name,
            int order,
            int declarationOrder,
            Type memberType,
            PropertyInfo property,
            YamlIgnoreCondition? ignoreCondition,
            bool isRequired,
            bool canWrite,
            YamlObjectCreationHandling? objectCreationHandling,
            YamlSequenceItemStyle blockSequenceMappingStyle = YamlSequenceItemStyle.Default,
            YamlSequenceItemStyle blockSequenceSequenceStyle = YamlSequenceItemStyle.Default,
            ScalarStyle stringStyle = ScalarStyle.Any,
            bool disallowNullOnSerialize = false,
            bool disallowNullOnDeserialize = false,
            bool isReadOnlyProperty = false)
        {
            ClrName = property.Name;
            Name = name;
            Order = order;
            DeclarationOrder = declarationOrder;
            MemberType = memberType;
            _property = property;
            _field = null;
            IgnoreCondition = ignoreCondition;
            IsRequired = isRequired;
            CanWrite = canWrite;
            ObjectCreationHandling = objectCreationHandling;
            BlockSequenceMappingStyle = blockSequenceMappingStyle;
            BlockSequenceSequenceStyle = blockSequenceSequenceStyle;
            StringStyle = stringStyle;
            DisallowNullOnSerialize = disallowNullOnSerialize;
            DisallowNullOnDeserialize = disallowNullOnDeserialize;
            IsReadOnlyProperty = isReadOnlyProperty;
        }

        public Member(
            string name,
            int order,
            int declarationOrder,
            Type memberType,
            FieldInfo field,
            YamlIgnoreCondition? ignoreCondition,
            bool isRequired,
            bool canWrite,
            YamlObjectCreationHandling? objectCreationHandling,
            YamlSequenceItemStyle blockSequenceMappingStyle = YamlSequenceItemStyle.Default,
            YamlSequenceItemStyle blockSequenceSequenceStyle = YamlSequenceItemStyle.Default,
            ScalarStyle stringStyle = ScalarStyle.Any,
            bool disallowNullOnSerialize = false,
            bool disallowNullOnDeserialize = false,
            bool isReadOnlyField = false)
        {
            ClrName = field.Name;
            Name = name;
            Order = order;
            DeclarationOrder = declarationOrder;
            MemberType = memberType;
            _property = null;
            _field = field;
            IgnoreCondition = ignoreCondition;
            IsRequired = isRequired;
            CanWrite = canWrite;
            ObjectCreationHandling = objectCreationHandling;
            BlockSequenceMappingStyle = blockSequenceMappingStyle;
            BlockSequenceSequenceStyle = blockSequenceSequenceStyle;
            StringStyle = stringStyle;
            DisallowNullOnSerialize = disallowNullOnSerialize;
            DisallowNullOnDeserialize = disallowNullOnDeserialize;
            IsReadOnlyField = isReadOnlyField;
        }

        public string ClrName { get; }

        public string Name { get; }

        public int Order { get; }

        public int DeclarationOrder { get; }

        public Type MemberType { get; }

        public bool CanRead => _property?.GetMethod is not null || _field is not null;

        public bool CanWrite { get; }

        public YamlObjectCreationHandling? ObjectCreationHandling { get; }

        public YamlSequenceItemStyle BlockSequenceMappingStyle { get; }

        public YamlSequenceItemStyle BlockSequenceSequenceStyle { get; }

        public ScalarStyle StringStyle { get; }

        public YamlIgnoreCondition? IgnoreCondition { get; }

        public bool IsRequired { get; }

        public int RequiredIndex { get; set; } = -1;

        public bool ShouldIgnoreOnRead => IgnoreCondition == YamlIgnoreCondition.WhenReading;

        public bool DisallowNullOnSerialize { get; }

        public bool DisallowNullOnDeserialize { get; }

        public bool IsReadOnlyProperty { get; }

        public bool IsReadOnlyField { get; }

        public YamlConverter? Converter { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a null scalar assigns <see langword="null"/> without being read by a converter.
        /// </summary>
        /// <remarks>
        /// This is the case of a member that accepts <see langword="null"/> and has no custom converter: the built-in
        /// converter of its type is not used, because the node converter, for instance, reads a null scalar as a
        /// <see cref="YamlValue"/>. A custom converter decides what a null scalar means, and the converter of a
        /// non-nullable value type rejects it, as it does for a root value or a collection element.
        /// </remarks>
        public bool ReadsNullScalarAsNull { get; set; }

        public YamlObjectCreationHandling GetEffectiveObjectCreationHandling(YamlObjectCreationHandling preferredObjectCreationHandling)
            => ObjectCreationHandling ?? preferredObjectCreationHandling;

        public object? GetValue(object instance)
        {
            if (_property is not null)
            {
                return _property.GetValue(instance);
            }

            return _field!.GetValue(instance);
        }

        public void SetValue(object instance, object? value)
        {
            if (_property is not null)
            {
                _property.SetValue(instance, value);
                return;
            }

            _field!.SetValue(instance, value);
        }

        /// <summary>
        /// Gets whether the member is skipped on write because it is read-only. The value of such a member is not read,
        /// so it is not rejected when it is null despite being declared as non-nullable.
        /// </summary>
        public bool IsIgnoredAsReadOnly(YamlSerializerOptions options)
            => (IsReadOnlyProperty && options.IgnoreReadOnlyProperties) || (IsReadOnlyField && options.IgnoreReadOnlyFields);

        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2072",
            Justification = "Default-value comparison uses reflection and is only exercised by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
        public bool ShouldIgnoreOnWrite(object? value, YamlSerializerOptions options)
        {
            var ignoreCondition = IgnoreCondition ?? options.DefaultIgnoreCondition;

            switch (ignoreCondition)
            {
                case YamlIgnoreCondition.Never:
                case YamlIgnoreCondition.WhenReading:
                    return false;

                case YamlIgnoreCondition.Always:
                case YamlIgnoreCondition.WhenWriting:
                    return true;

                case YamlIgnoreCondition.WhenWritingNull:
                    return value is null;

                case YamlIgnoreCondition.WhenWritingDefault:
                    if (value is null)
                    {
                        return true;
                    }

                    if (MemberType.IsValueType)
                    {
                        var defaultValue = Activator.CreateInstance(MemberType);
                        return value.Equals(defaultValue);
                    }

                    return false;

                default:
                    return false;
            }
        }
    }

    private readonly struct BufferedMemberAssignment
    {
        public BufferedMemberAssignment(Member member, object? value, Mark keyStart, Mark keyEnd)
        {
            Member = member;
            Value = value;
            KeyStart = keyStart;
            KeyEnd = keyEnd;
        }

        public Member Member { get; }

        public object? Value { get; }

        public Mark KeyStart { get; }

        public Mark KeyEnd { get; }
    }

    private readonly struct BufferedExtensionEntry
    {
        public BufferedExtensionEntry(string key, object? value, Mark keyStart, Mark keyEnd)
        {
            Key = key;
            Value = value;
            KeyStart = keyStart;
            KeyEnd = keyEnd;
        }

        public string Key { get; }

        public object? Value { get; }

        public Mark KeyStart { get; }

        public Mark KeyEnd { get; }
    }

    private static void ReadExtensionData(YamlReader reader, object instance, ExtensionDataInfo extensionData, string key, Mark keyStart, Mark keyEnd)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(extensionData);
        ArgumentNullException.ThrowIfNull(key);

        try
        {
            var value = ReadExtensionDataValue(reader, extensionData);
            AddExtensionDataValue(instance, extensionData, key, value);
        }
        catch (YamlException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The failure is reported at the key, whether it is declared by the mapping or provided by a merge.
            throw new YamlException(reader.SourceName, keyStart, keyEnd, exception.Message, exception);
        }
    }

    private static object? ReadExtensionDataValue(YamlReader reader, ExtensionDataInfo extensionData)
    {
        switch (extensionData.Kind)
        {
            case ExtensionDataKind.Dictionary:
            case ExtensionDataKind.ReadOnlyDictionary:
            {
                var valueType = extensionData.DictionaryValueType ?? typeof(object);
                var converter = reader.GetConverter(valueType);
                return converter.Read(reader, valueType);
            }

            case ExtensionDataKind.Mapping:
            {
                var elementConverter = reader.GetConverter(typeof(YamlElement));
                return elementConverter.Read(reader, typeof(YamlElement));
            }

            default:
                throw new InvalidOperationException($"Unknown extension data kind '{extensionData.Kind}'.");
        }
    }

    private static void AddExtensionDataValue(object instance, ExtensionDataInfo extensionData, string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(extensionData);
        ArgumentNullException.ThrowIfNull(key);

        var member = extensionData.Member;
        var container = member.GetValue(instance);

        // A read-only dictionary member cannot be mutated through its declared type: create a mutable dictionary,
        // copy the existing entries into it, and assign it back to the member.
        if (extensionData.Kind == ExtensionDataKind.ReadOnlyDictionary && !extensionData.ContainerType!.IsInstanceOfType(container))
        {
            var mutableContainer = extensionData.CreateContainer();
            if (container is not null)
            {
                var mutableDictionary = (IDictionary)mutableContainer;
                foreach (var entry in extensionData.EnumerateEntries!(container))
                {
                    mutableDictionary[entry.Key] = entry.Value;
                }
            }

            container = mutableContainer;
            AssignExtensionDataContainer(instance, member, container);
        }
        else if (container is null)
        {
            container = extensionData.CreateContainer();
            AssignExtensionDataContainer(instance, member, container);
        }

        switch (extensionData.Kind)
        {
            case ExtensionDataKind.Dictionary:
            case ExtensionDataKind.ReadOnlyDictionary:
            {
                if (container is not IDictionary dictionary)
                {
                    throw new NotSupportedException($"Extension data member '{member.Name}' on '{instance.GetType()}' must implement '{typeof(IDictionary)}'.");
                }

                var valueType = extensionData.DictionaryValueType ?? typeof(object);
                if (value is not null && valueType != typeof(object) && !valueType.IsInstanceOfType(value))
                {
                    throw new NotSupportedException($"Extension data value '{value.GetType()}' cannot be stored in '{valueType}'.");
                }

                dictionary[key] = value;
                return;
            }

            case ExtensionDataKind.Mapping:
            {
                if (container is not YamlMapping mapping)
                {
                    throw new NotSupportedException($"Extension data member '{member.Name}' on '{instance.GetType()}' must be a '{typeof(YamlMapping)}'.");
                }

                if (value is not null and not YamlElement)
                {
                    throw new NotSupportedException($"Extension data mapping value must be a '{typeof(YamlElement)}'.");
                }

                var element = (YamlElement?)value;
                for (var i = 0; i < mapping.Count; i++)
                {
                    if (mapping[i].Key is YamlValue keyValue && string.Equals(keyValue.Value, key, StringComparison.Ordinal))
                    {
                        mapping[i] = new KeyValuePair<YamlElement, YamlElement?>(mapping[i].Key, element);
                        return;
                    }
                }

                mapping.Add(new YamlValue(key), element);
                return;
            }

            default:
                throw new InvalidOperationException($"Unknown extension data kind '{extensionData.Kind}'.");
        }
    }

    private static void AssignExtensionDataContainer(object instance, Member member, object container)
    {
        try
        {
            member.SetValue(instance, container);
        }
        catch (Exception exception)
        {
            throw new NotSupportedException($"Extension data member '{member.Name}' could not be assigned on '{instance.GetType()}'.", exception);
        }
    }

    private static IEnumerable<KeyValuePair<string, object?>> EnumerateReadOnlyDictionary<TValue>(object container)
    {
        foreach (var pair in (IReadOnlyDictionary<string, TValue>)container)
        {
            yield return new KeyValuePair<string, object?>(pair.Key, pair.Value);
        }
    }

    private static void WriteExtensionData(YamlWriter writer, object instance, Contract contract, string? skippedKey)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(contract);

        var extensionData = contract.ExtensionData;
        if (extensionData is null)
        {
            return;
        }

        var member = extensionData.Member;
        var container = member.GetValue(instance);
        if (container is null)
        {
            return;
        }

        switch (extensionData.Kind)
        {
            case ExtensionDataKind.Dictionary:
                WriteExtensionDictionary(writer, container, extensionData.DictionaryValueType ?? typeof(object), skippedKey);
                return;

            case ExtensionDataKind.ReadOnlyDictionary:
                WriteExtensionEntries(writer, extensionData.EnumerateEntries!(container), extensionData.DictionaryValueType ?? typeof(object), skippedKey);
                return;

            case ExtensionDataKind.Mapping:
                if (container is not YamlMapping mapping)
                {
                    throw new YamlException(Mark.Empty, Mark.Empty, $"Extension data member '{member.Name}' on '{instance.GetType()}' must be a '{typeof(YamlMapping)}'.");
                }

                WriteExtensionMapping(writer, mapping, skippedKey);
                return;

            default:
                throw new InvalidOperationException($"Unknown extension data kind '{extensionData.Kind}'.");
        }
    }

    private static void WriteExtensionDictionary(YamlWriter writer, object container, Type valueType, string? skippedKey)
    {
        if (container is not IDictionary dictionary)
        {
            throw new YamlException(Mark.Empty, Mark.Empty, $"Extension data dictionary must implement '{typeof(IDictionary)}'.");
        }

        if (writer.Options.MappingOrder == YamlMappingOrderPolicy.Sorted)
        {
            var items = new List<KeyValuePair<string, object?>>(dictionary.Count);
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not string key)
                {
                    throw new YamlException(Mark.Empty, Mark.Empty, "Extension data dictionary keys must be strings.");
                }

                items.Add(new KeyValuePair<string, object?>(key, entry.Value));
            }

            items.Sort(static (x, y) => string.CompareOrdinal(x.Key, y.Key));
            for (var i = 0; i < items.Count; i++)
            {
                WriteExtensionEntry(writer, items[i].Key, items[i].Value, valueType, skippedKey);
            }

            return;
        }

        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not string key)
            {
                throw new YamlException(Mark.Empty, Mark.Empty, "Extension data dictionary keys must be strings.");
            }

            WriteExtensionEntry(writer, key, entry.Value, valueType, skippedKey);
        }
    }

    private static void WriteExtensionEntries(YamlWriter writer, IEnumerable<KeyValuePair<string, object?>> entries, Type valueType, string? skippedKey)
    {
        if (writer.Options.MappingOrder == YamlMappingOrderPolicy.Sorted)
        {
            var items = new List<KeyValuePair<string, object?>>(entries);
            items.Sort(static (x, y) => string.CompareOrdinal(x.Key, y.Key));
            for (var i = 0; i < items.Count; i++)
            {
                WriteExtensionEntry(writer, items[i].Key, items[i].Value, valueType, skippedKey);
            }

            return;
        }

        foreach (var entry in entries)
        {
            WriteExtensionEntry(writer, entry.Key, entry.Value, valueType, skippedKey);
        }
    }

    private static void WriteExtensionMapping(YamlWriter writer, YamlMapping mapping, string? skippedKey)
    {
        if (writer.Options.MappingOrder == YamlMappingOrderPolicy.Sorted)
        {
            var items = new List<KeyValuePair<string, YamlElement?>>(mapping.Count);
            for (var i = 0; i < mapping.Count; i++)
            {
                var pair = mapping[i];
                if (pair.Key is not YamlValue keyValue)
                {
                    throw new YamlException(Mark.Empty, Mark.Empty, "Only scalar mapping keys are supported for extension data.");
                }

                items.Add(new KeyValuePair<string, YamlElement?>(keyValue.Value, pair.Value));
            }

            items.Sort(static (x, y) => string.CompareOrdinal(x.Key, y.Key));
            for (var i = 0; i < items.Count; i++)
            {
                WriteExtensionEntry(writer, items[i].Key, items[i].Value, typeof(YamlNode), skippedKey);
            }

            return;
        }

        for (var i = 0; i < mapping.Count; i++)
        {
            var pair = mapping[i];
            if (pair.Key is not YamlValue keyValue)
            {
                throw new YamlException(Mark.Empty, Mark.Empty, "Only scalar mapping keys are supported for extension data.");
            }

            WriteExtensionEntry(writer, keyValue.Value, pair.Value, typeof(YamlNode), skippedKey);
        }
    }

    private static void WriteExtensionEntry(YamlWriter writer, string key, object? value, Type valueType, string? skippedKey)
    {
        if (string.Equals(key, skippedKey, StringComparison.Ordinal))
        {
            return;
        }

        writer.WritePropertyName(key);
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        var converter = writer.GetConverter(valueType);
        converter.Write(writer, value);
    }

    private static YamlIgnoreCondition? GetIgnoreCondition(MemberInfo member)
    {
        var yamlIgnore = member.GetCustomAttribute<YamlIgnoreAttribute>(inherit: true);
        if (yamlIgnore is not null)
        {
            return yamlIgnore.Condition;
        }

        return null;
    }

    private static YamlIgnoreCondition? GetIgnoreCondition(MemberInfo member, Type declaringType)
        => GetIgnoreCondition(member) ?? GetDeclaredIgnoreCondition(declaringType);

    private static YamlIgnoreCondition? GetDeclaredIgnoreCondition(Type type)
        => type.GetCustomAttribute<YamlIgnoreAttribute>(inherit: true)?.Condition;

    private static bool IsRequired(MemberInfo member)
    {
        if (Attribute.IsDefined(member, typeof(YamlRequiredAttribute), inherit: true))
        {
            return true;
        }

        if (member.IsDefined(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute), inherit: false))
        {
            return true;
        }

        return false;
    }

    private static bool DisallowNullOnSerialize(NullabilityInfoContext? context, PropertyInfo property)
        => IsNullableReferenceTypeEnforced(property.PropertyType) && context?.Create(property).ReadState == NullabilityState.NotNull;

    private static bool DisallowNullOnDeserialize(NullabilityInfoContext? context, PropertyInfo property)
        => IsNullableReferenceTypeEnforced(property.PropertyType) && context?.Create(property).WriteState == NullabilityState.NotNull;

    private static bool DisallowNullOnSerialize(NullabilityInfoContext? context, FieldInfo field)
        => IsNullableReferenceTypeEnforced(field.FieldType) && context?.Create(field).ReadState == NullabilityState.NotNull;

    private static bool DisallowNullOnDeserialize(NullabilityInfoContext? context, FieldInfo field)
        => IsNullableReferenceTypeEnforced(field.FieldType) && context?.Create(field).WriteState == NullabilityState.NotNull;

    private static bool DisallowNullOnDeserialize(NullabilityInfoContext? context, ParameterInfo parameter)
        => IsNullableReferenceTypeEnforced(parameter.ParameterType) && context?.Create(parameter).WriteState == NullabilityState.NotNull;

    private static bool IsNullableReferenceTypeEnforced(Type type)
        => !type.IsValueType;

    private static bool IsExtensionData(MemberInfo member)
    {
        return Attribute.IsDefined(member, typeof(YamlExtensionDataAttribute), inherit: true);
    }

    private static bool IsIncluded(MemberInfo member)
    {
        return Attribute.IsDefined(member, typeof(YamlIncludeAttribute), inherit: true);
    }

    /// <summary>Gets the type hierarchy of <paramref name="type"/>, from its base-most type down to <paramref name="type"/> itself.</summary>
    private static List<Type> GetTypeHierarchy(Type type)
    {
        var hierarchy = new List<Type>();
        for (var current = type; current is not null && current != typeof(object) && current != typeof(ValueType); current = current.BaseType)
        {
            hierarchy.Add(current);
        }

        hierarchy.Reverse();
        return hierarchy;
    }

    private static void AddDiscoveredMember(List<MemberInfo?> members, Dictionary<string, int> indexByName, MemberInfo member, bool isIgnored)
    {
        // An ignored member still hides the member it overrides or hides, so neither is part of the contract.
        var entry = isIgnored ? null : member;
        if (indexByName.TryGetValue(member.Name, out var existingIndex))
        {
            members[existingIndex] = entry;
            return;
        }

        indexByName.Add(member.Name, members.Count);
        members.Add(entry);
    }

    private static MemberInfo SelectExtensionDataMember(Type type, MemberInfo? existing, MemberInfo candidate)
    {
        // A member overriding or hiding the extension data member of a base type replaces it.
        if (existing is not null && !string.Equals(existing.Name, candidate.Name, StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Type '{type}' defines multiple extension data members.");
        }

        return candidate;
    }

    private static YamlUnmappedMemberHandling GetUnmappedMemberHandling(Type type, YamlSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(options);

        var attribute = type.GetCustomAttribute<YamlUnmappedMemberHandlingAttribute>(inherit: false);
        if (attribute is not null)
        {
            return attribute.Handling;
        }

        return options.UnmappedMemberHandling;
    }

    private static YamlObjectCreationHandling GetPreferredObjectCreationHandling(Type type, YamlSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(options);

        var attribute = type.GetCustomAttribute<YamlObjectCreationHandlingAttribute>(inherit: false);
        return attribute?.Handling ?? options.PreferredObjectCreationHandling;
    }

    private static YamlObjectCreationHandling? GetObjectCreationHandling(MemberInfo member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return member.GetCustomAttribute<YamlObjectCreationHandlingAttribute>(inherit: true)?.Handling;
    }

    private static YamlNumberHandlingConverter? CreateNumberHandlingConverter(MemberInfo member, Type memberType, Type declaringType, YamlReaderWriterBase readerWriter)
    {
        var handling = GetEffectiveNumberHandling(member, declaringType);
        if (handling == YamlNumberHandling.None || !YamlNumberHandlingConverter.IsSupportedType(memberType))
        {
            return null;
        }

        var inner = readerWriter.GetConverter(memberType);
        return new YamlNumberHandlingConverter(inner, memberType, handling);
    }

    private static YamlNumberHandling GetEffectiveNumberHandling(MemberInfo member, Type declaringType)
    {
        var yamlMember = member.GetCustomAttribute<YamlNumberHandlingAttribute>(inherit: true);
        if (yamlMember is not null)
        {
            return yamlMember.Handling;
        }

        var yamlType = declaringType.GetCustomAttribute<YamlNumberHandlingAttribute>(inherit: true);
        if (yamlType is not null)
        {
            return yamlType.Handling;
        }

        return YamlNumberHandling.None;
    }

    private static (YamlSequenceItemStyle MappingStyle, YamlSequenceItemStyle SequenceStyle) GetBlockSequenceItemStyles(MemberInfo member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var attribute = member.GetCustomAttribute<YamlBlockSequenceItemStyleAttribute>(inherit: true);
        if (attribute is null)
        {
            return (YamlSequenceItemStyle.Default, YamlSequenceItemStyle.Default);
        }

        YamlSerializerOptions.ValidateSequenceItemStyle(attribute.MappingStyle, nameof(YamlBlockSequenceItemStyleAttribute.MappingStyle));
        YamlSerializerOptions.ValidateSequenceItemStyle(attribute.SequenceStyle, nameof(YamlBlockSequenceItemStyleAttribute.SequenceStyle));
        return (attribute.MappingStyle, attribute.SequenceStyle);
    }

    private static ScalarStyle GetStringStyle(MemberInfo member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var attribute = member.GetCustomAttribute<YamlStringStyleAttribute>(inherit: true);
        if (attribute is null)
        {
            return ScalarStyle.Any;
        }

        YamlSerializerOptions.ValidateScalarStyle(attribute.Style, nameof(YamlStringStyleAttribute.Style));
        return attribute.Style;
    }

    private static YamlConverter? CreateConverterFromAttribute(MemberInfo member, Type memberType, YamlSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(memberType);
        ArgumentNullException.ThrowIfNull(options);

        var attribute = member.GetCustomAttribute<YamlConverterAttribute>(inherit: true);
        if (attribute is null)
        {
            return null;
        }

        var converterType = YamlConverterAttributeHelper.ResolveConverterType(attribute.ConverterType, memberType);
        if (!typeof(YamlConverter).IsAssignableFrom(converterType))
        {
            throw new NotSupportedException($"Converter type '{converterType}' must derive from '{typeof(YamlConverter)}'.");
        }

        var converter = (YamlConverter)Activator.CreateInstance(converterType)!;
        if (converter is YamlConverterFactory factory)
        {
            var created = factory.CreateConverter(memberType, options);
            if (created is null || !created.CanConvert(memberType))
            {
                throw new InvalidOperationException($"Converter factory '{factory.GetType()}' returned an invalid converter for '{memberType}'.");
            }

            return created;
        }

        if (!converter.CanConvert(memberType))
        {
            throw new NotSupportedException($"Converter '{converterType}' cannot handle '{memberType}'.");
        }

        return converter;
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Constructor selection uses reflection and is only exercised by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
    private static ConstructorInfo? SelectDeserializationConstructor(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (type.IsAbstract || type.IsInterface)
        {
            return null;
        }

        var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        ConstructorInfo? attributed = null;
        for (var i = 0; i < constructors.Length; i++)
        {
            var ctor = constructors[i];
            if (ctor.IsDefined(typeof(YamlConstructorAttribute), inherit: false))
            {
                if (attributed is not null)
                {
                    throw new NotSupportedException($"Type '{type}' defines multiple constructors annotated with '{typeof(YamlConstructorAttribute)}'.");
                }

                attributed = ctor;
            }
        }

        // A value type can always be created without calling a constructor, so it only uses the one it opts into.
        if (attributed is not null || type.IsValueType)
        {
            return attributed;
        }

        // Prefer the default public parameterless constructor when available.
        if (type.GetConstructor(Type.EmptyTypes) is not null)
        {
            return null;
        }

        var publicConstructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
        if (publicConstructors.Length == 1)
        {
            return publicConstructors[0];
        }

        if (publicConstructors.Length == 0)
        {
            throw new NotSupportedException($"Type '{type}' does not have a public constructor. Use '{typeof(YamlConstructorAttribute)}' to opt into a non-public constructor.");
        }

        throw new NotSupportedException($"Type '{type}' defines multiple public constructors. Use '{typeof(YamlConstructorAttribute)}' to select the constructor to use for deserialization.");
    }

    private static string GetMemberName(MemberInfo member, Type declaringType, YamlReaderWriterBase readerWriter)
    {
        var yamlName = member.GetCustomAttribute<YamlPropertyNameAttribute>(inherit: true);
        if (yamlName is not null)
        {
            return yamlName.Name;
        }

        var name = member.Name;
        var declaredPolicy = member.GetCustomAttribute<YamlNamingPolicyAttribute>(inherit: true)?.NamingPolicy
            ?? GetDeclaredNamingPolicy(declaringType);
        if (declaredPolicy is not null)
        {
            return ApplyNamingPolicy(name, declaredPolicy.GetValueOrDefault());
        }

        return readerWriter.ConvertName(name);
    }

    private static YamlKnownNamingPolicy? GetDeclaredNamingPolicy(Type type)
    {
        return type.GetCustomAttribute<YamlNamingPolicyAttribute>(inherit: true)?.NamingPolicy;
    }

    private static string ApplyNamingPolicy(string name, YamlKnownNamingPolicy namingPolicy)
    {
        var policy = YamlNamingPolicy.GetPolicy(namingPolicy);
        return policy is null ? name : policy.ConvertName(name);
    }

    private static int GetMemberOrder(MemberInfo member)
    {
        var yamlOrder = member.GetCustomAttribute<YamlPropertyOrderAttribute>(inherit: true);
        if (yamlOrder is not null)
        {
            return yamlOrder.Order;
        }

        return 0;
    }
}
