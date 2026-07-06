using System.Collections;
using System.Reflection;
using Meziantou.Framework.Yaml.Model;

namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlObjectConverter<T> : YamlConverter<T?>
{
    private Contract? _contract;

    public override bool CanPopulate(Type typeToConvert) => typeToConvert == typeof(T);

    public override T? Read(YamlReader reader)
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return (T)rootAliasValue!;
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

        if (writer.ReferenceWriter is not null && value is not string && !typeof(T).IsValueType)
        {
            if (writer.ReferenceWriter.TryGetAnchor(value, out var existing))
            {
                writer.WriteAlias(existing);
                return;
            }

            var anchor = writer.ReferenceWriter.GetOrAddAnchor(value);
            writer.WriteAnchor(anchor);
        }

        if (value is IYamlOnSerializing onSerializing)
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

        var runtimeType = value.GetType();
        if (contract.Polymorphism is not null && runtimeType != typeof(T))
        {
            YamlObjectConverter<T>.WritePolymorphic(writer, value, runtimeType, contract);
        }
        else
        {
            YamlObjectConverter<T>.WriteObjectCore(writer, value, contract);
        }

        if (value is IYamlOnSerialized onSerialized)
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

        T instance;
        try
        {
            instance = (T)contract.CreateInstance();
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
        var mergeEnabled = options.Schema is YamlSchemaKind.Core or YamlSchemaKind.Extended;
        HashSet<string>? explicitKeys = mergeEnabled ? new HashSet<string>(reader.PropertyNameComparer) : null;
        HashSet<Member>? seenMembers = options.DuplicateKeyHandling == YamlDuplicateKeyHandling.LastWins ? null : new HashSet<Member>();
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
            var key = reader.ScalarValue ?? string.Empty;
            reader.Read();

            if (mergeEnabled && string.Equals(key, "<<", StringComparison.Ordinal))
            {
                ReadAndApplyMergeToInstance(reader, instance!, contract, explicitKeys!, requiredSeen);
                continue;
            }

            explicitKeys?.Add(key);

            if (!contract.TryGetMember(key, out var member))
            {
                if (contract.ExtensionData is null)
                {
                    SkipOrThrowUnmappedMember(reader, contract, key);
                    continue;
                }

                try
                {
                    ReadExtensionData(reader, instance!, contract.ExtensionData, key);
                }
                catch (YamlException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new YamlException(reader.SourceName, keyStart, keyEnd, exception.Message, exception);
                }
                continue;
            }

            if (requiredSeen is not null && member.RequiredIndex >= 0)
            {
                requiredSeen[member.RequiredIndex] = true;
            }

            var wasSeen = seenMembers is not null && !seenMembers.Add(member);
            if (wasSeen && options.DuplicateKeyHandling == YamlDuplicateKeyHandling.Error)
            {
                throw new YamlException(reader.SourceName, keyStart, keyEnd, $"Duplicate mapping key '{key}'.");
            }

            if (wasSeen && options.DuplicateKeyHandling == YamlDuplicateKeyHandling.FirstWins)
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
                throw new YamlException(reader.SourceName, mappingStart, reader.End, $"Missing required mapping key(s) for '{typeof(T)}': {string.Join(", ", missing)}.");
            }
        }

        reader.Read();

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

        return instance;
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
        var mergeEnabled = options.Schema is YamlSchemaKind.Core or YamlSchemaKind.Extended;
        HashSet<string>? explicitKeys = mergeEnabled ? new HashSet<string>(reader.PropertyNameComparer) : null;
        HashSet<Member>? seenMembers = options.DuplicateKeyHandling == YamlDuplicateKeyHandling.LastWins ? null : new HashSet<Member>();
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
            var key = reader.ScalarValue ?? string.Empty;
            reader.Read();

            if (mergeEnabled && string.Equals(key, "<<", StringComparison.Ordinal))
            {
                ReadAndApplyMergeToPopulatedInstance(reader, instance, contract, explicitKeys!, requiredSeen);
                continue;
            }

            explicitKeys?.Add(key);

            if (!contract.TryGetMember(key, out var member))
            {
                if (contract.ExtensionData is null)
                {
                    SkipOrThrowUnmappedMember(reader, contract, key);
                    continue;
                }

                try
                {
                    ReadExtensionData(reader, instance, contract.ExtensionData, key);
                }
                catch (YamlException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new YamlException(reader.SourceName, keyStart, keyEnd, exception.Message, exception);
                }

                continue;
            }

            if (requiredSeen is not null && member.RequiredIndex >= 0)
            {
                requiredSeen[member.RequiredIndex] = true;
            }

            var wasSeen = seenMembers is not null && !seenMembers.Add(member);
            if (wasSeen && options.DuplicateKeyHandling == YamlDuplicateKeyHandling.Error)
            {
                throw new YamlException(reader.SourceName, keyStart, keyEnd, $"Duplicate mapping key '{key}'.");
            }

            if (wasSeen && options.DuplicateKeyHandling == YamlDuplicateKeyHandling.FirstWins)
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
                throw new YamlException(reader.SourceName, mappingStart, reader.End, $"Missing required mapping key(s) for '{instance.GetType()}': {string.Join(", ", missing)}.");
            }
        }

        reader.Read();

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
    }

    private T? ReadObjectCoreWithConstructor(YamlReader reader, Contract contract)
    {
        var constructor = contract.Constructor ?? throw new InvalidOperationException("Constructor model was not available.");

        var mappingStart = reader.Start;
        var mappingAnchor = reader.Anchor;
        var options = reader.Options;
        var mergeEnabled = options.Schema is YamlSchemaKind.Core or YamlSchemaKind.Extended;
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
            var key = reader.ScalarValue ?? string.Empty;
            reader.Read();

            if (mergeEnabled && string.Equals(key, "<<", StringComparison.Ordinal))
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
                var converter = reader.GetConverter(parameterType);
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

                var converter = member.Converter ??= reader.GetConverter(member.MemberType);
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

        T instance;
        try
        {
            instance = (T)constructor.CreateInstance(args);
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
            reader.ReferenceReader.Register(mappingAnchor, instance!);
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
                throw new YamlException(reader.SourceName, mappingStart, reader.End, $"An error occurred while invoking '{nameof(IYamlOnDeserializing)}.{nameof(IYamlOnDeserializing.OnDeserializing)}' on '{typeof(T)}'.", exception);
            }
        }

        foreach (var assignment in memberValues.Values)
        {
            try
            {
                assignment.Member.SetValue(instance!, assignment.Value);
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
                    AddExtensionDataValue(instance!, contract.ExtensionData!, entry.Key, entry.Value);
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
                throw new YamlException(reader.SourceName, mappingStart, reader.End, $"Missing required mapping key(s) for '{typeof(T)}': {string.Join(", ", missing)}.");
            }
        }

        reader.Read();

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

        return instance;
    }

    private static bool IsMergeKeyEnabled(YamlSerializerOptions options)
        => options.Schema is YamlSchemaKind.Core or YamlSchemaKind.Extended;

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
        if (!IsMergeKeyEnabled(reader.Options))
        {
            reader.Skip();
            return;
        }

        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            reader.Read();
            return;
        }

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
                if (reader.TokenType != YamlTokenType.StartMapping)
                {
                    throw new YamlException(reader.SourceName, reader.Start, reader.End, "Merge sequence entries must be mappings.");
                }

                ApplyMergeMappingToInstance(reader, instance, contract, explicitKeys, requiredSeen);
            }

            reader.Read();
            return;
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            // Merge requires reading mapping entries; alias expansion is only supported when ReferenceHandling is Preserve
            // and the alias resolves to a mapping-like value. For other cases, report a clear error.
            if (reader.TryReadAlias(out var aliasValue) && aliasValue is Dictionary<string, object?> mergedDict)
            {
                foreach (var pair in mergedDict)
                {
                    if (explicitKeys.Contains(pair.Key))
                    {
                        continue;
                    }

                    if (contract.TryGetMember(pair.Key, out var member) && member.CanWrite)
                    {
                        member.SetValue(instance, pair.Value);
                    }
                }

                return;
            }

            throw new YamlException(reader.SourceName, reader.Start, reader.End, "Merge alias values are not supported unless they resolve to a mapping.");
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
            var key = reader.ScalarValue ?? string.Empty;
            reader.Read();

            if (IsMergeKeyEnabled(reader.Options) && string.Equals(key, "<<", StringComparison.Ordinal))
            {
                ReadAndApplyMergeToInstance(reader, instance, contract, explicitKeys, requiredSeen);
                continue;
            }

            if (explicitKeys.Contains(key))
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

                ReadAndApplyMemberValue(reader, instance, contract, member, key, keyStart, keyEnd);
                continue;
            }

            if (contract.ExtensionData is not null)
            {
                ReadExtensionData(reader, instance, contract.ExtensionData, key);
                continue;
            }

            SkipOrThrowUnmappedMember(reader, contract, key);
        }

        reader.Read();
    }

    private static void ReadAndApplyMergeToPopulatedInstance(YamlReader reader, object instance, Contract contract, HashSet<string> explicitKeys, bool[]? requiredSeen)
    {
        if (!IsMergeKeyEnabled(reader.Options))
        {
            reader.Skip();
            return;
        }

        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            reader.Read();
            return;
        }

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
                if (reader.TokenType != YamlTokenType.StartMapping)
                {
                    throw new YamlException(reader.SourceName, reader.Start, reader.End, "Merge sequence entries must be mappings.");
                }

                ApplyMergeMappingToPopulatedInstance(reader, instance, contract, explicitKeys, requiredSeen);
            }

            reader.Read();
            return;
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, "Merge alias values are not supported unless they resolve to a mapping.");
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
            var key = reader.ScalarValue ?? string.Empty;
            reader.Read();

            if (IsMergeKeyEnabled(reader.Options) && string.Equals(key, "<<", StringComparison.Ordinal))
            {
                ReadAndApplyMergeToPopulatedInstance(reader, instance, contract, explicitKeys, requiredSeen);
                continue;
            }

            if (explicitKeys.Contains(key))
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

                ReadAndApplyMemberValue(reader, instance, contract, member, key, keyStart, keyEnd);
                continue;
            }

            if (contract.ExtensionData is not null)
            {
                ReadExtensionData(reader, instance, contract.ExtensionData, key);
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

        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            if (preferPopulate && !member.CanWrite)
            {
                throw new InvalidOperationException($"Unable to assign 'null' to the property or field of type '{member.MemberType}'.");
            }

            if (!member.CanWrite)
            {
                if (contract.ExtensionData is not null)
                {
                    ReadExtensionData(reader, instance, contract.ExtensionData, key);
                }
                else
                {
                    reader.Skip();
                }

                return;
            }

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

        var converter = member.Converter ??= reader.GetConverter(member.MemberType);
        var canPopulate = converter.CanPopulate(member.MemberType);
        var canPopulateMember = canPopulate && (!member.MemberType.IsValueType || member.CanWrite);
        var explicitPopulate = member.ObjectCreationHandling == YamlObjectCreationHandling.Populate;

        if (preferPopulate && explicitPopulate && !canPopulateMember)
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
                ReadExtensionData(reader, instance, contract.ExtensionData, key);
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
        if (!IsMergeKeyEnabled(reader.Options))
        {
            reader.Skip();
            return;
        }

        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            reader.Read();
            return;
        }

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
            var key = reader.ScalarValue ?? string.Empty;
            reader.Read();

            if (IsMergeKeyEnabled(reader.Options) && string.Equals(key, "<<", StringComparison.Ordinal))
            {
                ReadAndApplyMergeToConstructorBuffers(reader, contract, constructor, args, paramSeen, memberValues, extensionEntries, explicitKeys, requiredSeen);
                continue;
            }

            if (explicitKeys.Contains(key))
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
                var converter = reader.GetConverter(parameterType);
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

                var converter = member.Converter ??= reader.GetConverter(member.MemberType);
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
            if (!member.CanRead)
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
            converter.Write(writer, memberValue);
        }

        WriteExtensionData(writer, value, contract);
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

    private T? ReadPolymorphic(YamlReader reader, Contract contract)
    {
        var polymorphism = contract.Polymorphism!;
        var rootTag = reader.Tag;

        var buffered = YamlReader.BufferCurrentNodeToStringAndFindDiscriminator(reader, polymorphism.DiscriminatorPropertyName, out var discriminatorValue);

        Type? targetType = null;
        if (polymorphism.AcceptsPropertyDiscriminator && discriminatorValue is not null)
        {
            if (polymorphism.TryGetDerivedTypeFromDiscriminator(discriminatorValue, out var derived))
            {
                targetType = derived;
            }
            else if (polymorphism.DefaultDerivedType is not null)
            {
                targetType = polymorphism.DefaultDerivedType;
            }
            else if (polymorphism.UnknownDerivedTypeHandling == YamlUnknownDerivedTypeHandling.Fail)
            {
                throw YamlThrowHelper.ThrowUnknownTypeDiscriminator(reader, discriminatorValue, typeof(T));
            }
        }

        if (targetType is null && polymorphism.AcceptsTagDiscriminator && rootTag is not null)
        {
            if (polymorphism.TryGetDerivedTypeFromTag(rootTag, out var derivedFromTag))
            {
                targetType = derivedFromTag;
            }
            else if (polymorphism.DefaultDerivedType is not null)
            {
                targetType = polymorphism.DefaultDerivedType;
            }
            else if (polymorphism.UnknownDerivedTypeHandling == YamlUnknownDerivedTypeHandling.Fail)
            {
                throw YamlThrowHelper.ThrowUnknownTypeTag(reader, rootTag, typeof(T));
            }
        }

        targetType ??= polymorphism.DefaultDerivedType ?? typeof(T);

        var bufferedReader = reader.CreateReader(buffered);
        if (!bufferedReader.Read())
        {
            return default;
        }

        if (targetType == typeof(T))
        {
            return ReadObjectCore(bufferedReader, contract);
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

        var options = writer.Options;
        var derivedContract = Contract.Create(runtimeType, writer);
        var members = options.MappingOrder == YamlMappingOrderPolicy.Sorted
            ? derivedContract.MembersSorted
            : derivedContract.MembersDeclaration;

        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if (!member.CanRead)
            {
                continue;
            }

            if (string.Equals(member.Name, polymorphism.DiscriminatorPropertyName, StringComparison.Ordinal))
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
            converter.Write(writer, memberValue);
        }

        WriteExtensionData(writer, value, derivedContract);
        writer.WriteEndMapping();
    }

    private sealed class Contract
    {
        private readonly Dictionary<string, Member> _membersByName;

        public Contract(
            Type declaringType,
            Func<object> createInstance,
            ConstructorModel? constructorModel,
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
            CreateInstance = createInstance;
            Constructor = constructorModel;
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

        public Func<object> CreateInstance { get; }

        public ConstructorModel? Constructor { get; }

        public Member[] MembersDeclaration { get; }

        public Member[] MembersSorted { get; }

        public Member[] RequiredMembers { get; }

        public ExtensionDataInfo? ExtensionData { get; }

        public PolymorphismModel? Polymorphism { get; }

        public YamlUnmappedMemberHandling UnmappedMemberHandling { get; }

        public YamlObjectCreationHandling PreferredObjectCreationHandling { get; }

        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2070",
            Justification = "Contract discovery uses reflection and is only exercised by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2067",
            Justification = "Contract discovery and instance creation use reflection and are only exercised by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
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

            const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var property in type.GetProperties(AllInstance))
            {
                if (property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                var hasIncludeAttr = property.IsDefined(typeof(YamlIncludeAttribute), inherit: true);
                var canRead = property.GetMethod is not null && (property.GetMethod.IsPublic || hasIncludeAttr);
                var canWrite = property.SetMethod is not null && (property.SetMethod.IsPublic || hasIncludeAttr);

                if (IsExtensionData(property))
                {
                    if (extensionData is not null)
                    {
                        throw new NotSupportedException($"Type '{type}' defines multiple extension data members.");
                    }

                    if (IsIgnored(property, out _))
                    {
                        throw new NotSupportedException($"Extension data member '{property.Name}' on '{type}' cannot be ignored.");
                    }

                    if (IsRequired(property))
                    {
                        throw new NotSupportedException($"Extension data member '{property.Name}' on '{type}' cannot be required.");
                    }

                    if (!canRead)
                    {
                        throw new NotSupportedException($"Extension data member '{property.Name}' on '{type}' must be readable.");
                    }

                    var extensionMember = new Member(property.Name, order: 0, declarationOrder: property.MetadataToken, property.PropertyType, property, ignoreCondition: null, isRequired: false, canWrite, objectCreationHandling: null);
                    extensionData = ExtensionDataInfo.Create(type, extensionMember, property.PropertyType);
                    continue;
                }

                if (!canRead)
                {
                    continue;
                }

                if (IsIgnored(property, out var ignoreCondition))
                {
                    continue;
                }

                var name = GetMemberName(property, readerWriter);
                var order = GetMemberOrder(property);
                var token = property.MetadataToken;

                var (mappingStyle, sequenceStyle) = GetBlockSequenceItemStyles(property);
                var member = new Member(
                    name,
                    order,
                    token,
                    property.PropertyType,
                    property,
                    ignoreCondition,
                    IsRequired(property),
                    canWrite,
                    GetObjectCreationHandling(property),
                    mappingStyle,
                    sequenceStyle,
                    DisallowNullOnSerialize(nullabilityContext, property),
                    DisallowNullOnDeserialize(nullabilityContext, property),
                    isReadOnlyProperty: !canWrite);
                member.Converter = CreateConverterFromAttribute(property, property.PropertyType, options)
                    ?? CreateNumberHandlingConverter(property, property.PropertyType, type, readerWriter);
                members.Add(member);
                if (member.IsRequired)
                {
                    requiredMembers.Add(member);
                }
            }

            foreach (var field in type.GetFields(AllInstance))
            {
                if (IsExtensionData(field))
                {
                    if (extensionData is not null)
                    {
                        throw new NotSupportedException($"Type '{type}' defines multiple extension data members.");
                    }

                    if (IsIgnored(field, out _))
                    {
                        throw new NotSupportedException($"Extension data member '{field.Name}' on '{type}' cannot be ignored.");
                    }

                    if (IsRequired(field))
                    {
                        throw new NotSupportedException($"Extension data member '{field.Name}' on '{type}' cannot be required.");
                    }

                    var extensionMember = new Member(field.Name, order: 0, declarationOrder: field.MetadataToken, field.FieldType, field, ignoreCondition: null, isRequired: false, canWrite: !field.IsInitOnly, objectCreationHandling: null);
                    extensionData = ExtensionDataInfo.Create(type, extensionMember, field.FieldType);
                    continue;
                }

                var hasIncludeAttr = field.IsDefined(typeof(YamlIncludeAttribute), inherit: true);
                var canRead = hasIncludeAttr || (options.IncludeFields && field.IsPublic);
                if (!canRead)
                {
                    continue;
                }

                if (IsIgnored(field, out var ignoreCondition))
                {
                    continue;
                }

                var name = GetMemberName(field, readerWriter);
                var order = GetMemberOrder(field);
                var token = field.MetadataToken;

                var (mappingStyle, sequenceStyle) = GetBlockSequenceItemStyles(field);
                var member = new Member(
                    name,
                    order,
                    token,
                    field.FieldType,
                    field,
                    ignoreCondition,
                    IsRequired(field),
                    canWrite: !field.IsInitOnly,
                    GetObjectCreationHandling(field),
                    mappingStyle,
                    sequenceStyle,
                    DisallowNullOnSerialize(nullabilityContext, field),
                    DisallowNullOnDeserialize(nullabilityContext, field),
                    isReadOnlyField: field.IsInitOnly);
                member.Converter = CreateConverterFromAttribute(field, field.FieldType, options)
                    ?? CreateNumberHandlingConverter(field, field.FieldType, type, readerWriter);
                members.Add(member);
                if (member.IsRequired)
                {
                    requiredMembers.Add(member);
                }
            }

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
                    constructorModel = new ConstructorModel(selectedConstructor, members, readerWriter, nullabilityContext);
                    createInstance = () => throw new NotSupportedException($"Type '{type}' must be deserialized using a parameterized constructor.");
                }
            }

            members.Sort(static (x, y) =>
            {
                var orderCompare = x.Order.CompareTo(y.Order);
                return orderCompare != 0 ? orderCompare : x.DeclarationOrder.CompareTo(y.DeclarationOrder);
            });
            var membersDeclaration = members.ToArray();

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

            var map = new Dictionary<string, Member>(membersDeclaration.Length, readerWriter.PropertyNameComparer);
            for (var i = 0; i < membersDeclaration.Length; i++)
            {
                var member = membersDeclaration[i];
                map[member.Name] = member;
            }

            var polymorphism = PolymorphismModel.TryCreate(type, options);
            for (var i = 0; i < requiredMembers.Count; i++)
            {
                requiredMembers[i].RequiredIndex = i;
            }

            return new Contract(type, createInstance, constructorModel, membersDeclaration, membersSorted, map, requiredMembers.ToArray(), extensionData, polymorphism, unmappedMemberHandling, preferredObjectCreationHandling);
        }

        public bool TryGetMember(string name, out Member member) => _membersByName.TryGetValue(name, out member!);
    }

    private enum ExtensionDataKind
    {
        Dictionary,
        Mapping,
    }

    private sealed class ExtensionDataInfo
    {
        private ExtensionDataInfo(Member member, ExtensionDataKind kind, Type? dictionaryValueType, Func<object> createContainer)
        {
            Member = member;
            Kind = kind;
            DictionaryValueType = dictionaryValueType;
            CreateContainer = createContainer;
        }

        public Member Member { get; }

        public ExtensionDataKind Kind { get; }

        public Type? DictionaryValueType { get; }

        public Func<object> CreateContainer { get; }

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

            if (!TryGetExtensionDataDictionaryValueType(memberType, out var valueType))
            {
                throw new NotSupportedException($"Extension data member '{member.Name}' on '{declaringType}' must be a '{typeof(YamlMapping)}' or implement 'IDictionary<string, object>' or 'IDictionary<string, YamlNode>'.");
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

            return new ExtensionDataInfo(member, ExtensionDataKind.Dictionary, valueType, CreateDictionary);
        }

        private static bool TryGetExtensionDataDictionaryValueType(Type type, out Type valueType)
        {
            valueType = null!;

            if (TryGetDictionaryInterface(type, out var dictionaryInterface))
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
        private static bool TryGetDictionaryInterface(Type type, out Type dictionaryInterface)
        {
            dictionaryInterface = null!;

            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                if (definition == typeof(IDictionary<,>) && type.GetGenericArguments()[0] == typeof(string))
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
                if (definition == typeof(IDictionary<,>) && candidate.GetGenericArguments()[0] == typeof(string))
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
        private readonly Dictionary<string, int> _parameterIndexByYamlName;

        public ConstructorModel(ConstructorInfo constructor, IReadOnlyList<Member> members, YamlReaderWriterBase readerWriter, NullabilityInfoContext? nullabilityContext)
        {
            ArgumentNullException.ThrowIfNull(constructor);
            ArgumentNullException.ThrowIfNull(members);
            ArgumentNullException.ThrowIfNull(readerWriter);

            _constructor = constructor;
            _parameters = constructor.GetParameters();
            _parameterTypes = new Type[_parameters.Length];
            _parametersDisallowNull = new bool[_parameters.Length];

            var clrNameToSerialized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];
                if (!clrNameToSerialized.ContainsKey(member.ClrName))
                {
                    clrNameToSerialized.Add(member.ClrName, member.Name);
                }
            }

            _parameterIndexByYamlName = new Dictionary<string, int>(readerWriter.PropertyNameComparer);
            for (var i = 0; i < _parameters.Length; i++)
            {
                var parameter = _parameters[i];
                var parameterName = parameter.Name ?? throw new NotSupportedException($"Constructor '{constructor}' defines a parameter without a name.");

                var yamlName = clrNameToSerialized.TryGetValue(parameterName, out var memberName)
                    ? memberName
                    : readerWriter.ConvertName(parameterName);

                if (_parameterIndexByYamlName.ContainsKey(yamlName))
                {
                    throw new NotSupportedException($"Constructor '{constructor}' defines multiple parameters that bind to mapping key '{yamlName}'.");
                }

                _parameterIndexByYamlName.Add(yamlName, i);
                _parameterTypes[i] = parameter.ParameterType;
                _parametersDisallowNull[i] = DisallowNullOnDeserialize(nullabilityContext, parameter);
            }
        }

        public int ParameterCount => _parameters.Length;

        public bool TryGetParameterIndex(string yamlKey, out int index)
            => _parameterIndexByYamlName.TryGetValue(yamlKey, out index);

        public Type GetParameterType(int index) => _parameterTypes[index];

        public string GetParameterName(int index) => _parameters[index].Name ?? string.Empty;

        public bool DisallowNull(int index) => _parametersDisallowNull[index];

        public bool TryGetDefaultValue(int index, out object? value)
        {
            var parameter = _parameters[index];
            if (parameter.HasDefaultValue)
            {
                value = parameter.DefaultValue;
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

        public static PolymorphismModel? TryCreate(Type type, YamlSerializerOptions options)
        {
            var yamlDerived = type.GetCustomAttributes(typeof(YamlDerivedTypeAttribute), inherit: false);

            var hasRuntimeMappings = options.PolymorphismOptions.DerivedTypeMappings.TryGetValue(type, out var runtimeDerived)
                                     && runtimeDerived is { Count: > 0 };

            if (yamlDerived.Length == 0 && !hasRuntimeMappings)
            {
                return null;
            }

            var yamlPolymorphic = type.GetCustomAttribute<YamlPolymorphicAttribute>(inherit: false);

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

            foreach (YamlDerivedTypeAttribute attribute in yamlDerived)
            {
                if (!type.IsAssignableFrom(attribute.DerivedType))
                {
                    throw new InvalidOperationException($"Derived type '{attribute.DerivedType}' is not assignable to '{type}'.");
                }

                if (attribute.Discriminator is null)
                {
                    if (attribute.Tag is null)
                    {
                        defaultDerivedType = attribute.DerivedType;
                    }

                    typeToDerived[attribute.DerivedType] = new DerivedTypeInfo(null, attribute.Tag);
                }
                else
                {
                    discriminatorToType.Add(attribute.Discriminator, attribute.DerivedType);
                    typeToDerived[attribute.DerivedType] = new DerivedTypeInfo(attribute.Discriminator, attribute.Tag);
                }

                if (attribute.Tag is not null)
                {
                    tagToType.Add(attribute.Tag, attribute.DerivedType);
                }
            }

            if (hasRuntimeMappings)
            {
                foreach (var entry in runtimeDerived!)
                {
                    if (!type.IsAssignableFrom(entry.DerivedType))
                    {
                        throw new InvalidOperationException($"Derived type '{entry.DerivedType}' is not assignable to '{type}'.");
                    }

                    if (entry.Discriminator is null)
                    {
                        var isDefaultMapping = entry.Tag is null;
                        if (ShouldAddLowerPrecedenceMapping(
                            entry.DerivedType,
                            discriminator: null,
                            entry.Tag,
                            isDefaultMapping,
                            defaultDerivedType,
                            discriminatorToType,
                            tagToType,
                            typeToDerived))
                        {
                            if (isDefaultMapping)
                            {
                                defaultDerivedType ??= entry.DerivedType;
                            }

                            typeToDerived.Add(entry.DerivedType, new DerivedTypeInfo(null, entry.Tag));
                            if (entry.Tag is not null)
                            {
                                tagToType.Add(entry.Tag, entry.DerivedType);
                            }
                        }
                    }
                    else
                    {
                        if (ShouldAddLowerPrecedenceMapping(
                            entry.DerivedType,
                            entry.Discriminator,
                            entry.Tag,
                            isDefaultMapping: false,
                            defaultDerivedType,
                            discriminatorToType,
                            tagToType,
                            typeToDerived))
                        {
                            discriminatorToType.Add(entry.Discriminator, entry.DerivedType);
                            typeToDerived.Add(entry.DerivedType, new DerivedTypeInfo(entry.Discriminator, entry.Tag));
                            if (entry.Tag is not null)
                            {
                                tagToType.Add(entry.Tag, entry.DerivedType);
                            }
                        }
                    }
                }
            }

            return new PolymorphismModel(discriminatorPropertyName, style, unknownHandling, discriminatorToType, tagToType, typeToDerived, defaultDerivedType);
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

        public YamlIgnoreCondition? IgnoreCondition { get; }

        public bool IsRequired { get; }

        public int RequiredIndex { get; set; } = -1;

        public bool DisallowNullOnSerialize { get; }

        public bool DisallowNullOnDeserialize { get; }

        public bool IsReadOnlyProperty { get; }

        public bool IsReadOnlyField { get; }

        public YamlConverter? Converter { get; set; }

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

        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2072",
            Justification = "Default-value comparison uses reflection and is only exercised by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
        public bool ShouldIgnoreOnWrite(object? value, YamlSerializerOptions options)
        {
            if (IsReadOnlyProperty && options.IgnoreReadOnlyProperties)
            {
                return true;
            }

            if (IsReadOnlyField && options.IgnoreReadOnlyFields)
            {
                return true;
            }

            var ignoreCondition = IgnoreCondition ?? options.DefaultIgnoreCondition;

            switch (ignoreCondition)
            {
                case YamlIgnoreCondition.Never:
                    return false;

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

    private static void ReadExtensionData(YamlReader reader, object instance, ExtensionDataInfo extensionData, string key)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(extensionData);
        ArgumentNullException.ThrowIfNull(key);

        var value = ReadExtensionDataValue(reader, extensionData);
        AddExtensionDataValue(instance, extensionData, key, value);
    }

    private static object? ReadExtensionDataValue(YamlReader reader, ExtensionDataInfo extensionData)
    {
        switch (extensionData.Kind)
        {
            case ExtensionDataKind.Dictionary:
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
        if (container is null)
        {
            container = extensionData.CreateContainer();
            try
            {
                member.SetValue(instance, container);
            }
            catch (Exception exception)
            {
                throw new NotSupportedException($"Extension data member '{member.Name}' could not be assigned on '{instance.GetType()}'.", exception);
            }
        }

        switch (extensionData.Kind)
        {
            case ExtensionDataKind.Dictionary:
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

    private static void WriteExtensionData(YamlWriter writer, object instance, Contract contract)
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
                WriteExtensionDictionary(writer, container, extensionData.DictionaryValueType ?? typeof(object));
                return;

            case ExtensionDataKind.Mapping:
                if (container is not YamlMapping mapping)
                {
                    throw new YamlException(Mark.Empty, Mark.Empty, $"Extension data member '{member.Name}' on '{instance.GetType()}' must be a '{typeof(YamlMapping)}'.");
                }

                WriteExtensionMapping(writer, mapping);
                return;

            default:
                throw new InvalidOperationException($"Unknown extension data kind '{extensionData.Kind}'.");
        }
    }

    private static void WriteExtensionDictionary(YamlWriter writer, object container, Type valueType)
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
                WriteExtensionEntry(writer, items[i].Key, items[i].Value, valueType);
            }

            return;
        }

        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not string key)
            {
                throw new YamlException(Mark.Empty, Mark.Empty, "Extension data dictionary keys must be strings.");
            }

            WriteExtensionEntry(writer, key, entry.Value, valueType);
        }
    }

    private static void WriteExtensionMapping(YamlWriter writer, YamlMapping mapping)
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
                WriteExtensionEntry(writer, items[i].Key, items[i].Value, typeof(YamlNode));
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

            WriteExtensionEntry(writer, keyValue.Value, pair.Value, typeof(YamlNode));
        }
    }

    private static void WriteExtensionEntry(YamlWriter writer, string key, object? value, Type valueType)
    {
        writer.WritePropertyName(key);
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        var converter = writer.GetConverter(valueType);
        converter.Write(writer, value);
    }

    private static bool IsIgnored(MemberInfo member, out YamlIgnoreCondition? ignoreCondition)
    {
        ignoreCondition = null;

        if (member.IsDefined(typeof(YamlIgnoreAttribute), inherit: true))
        {
            ignoreCondition = YamlIgnoreCondition.WhenWritingDefault;
            return true;
        }

        return false;
    }

    private static bool IsRequired(MemberInfo member)
    {
        if (member.IsDefined(typeof(YamlRequiredAttribute), inherit: true))
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
        return member.IsDefined(typeof(YamlExtensionDataAttribute), inherit: true);
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

        return options.RejectUnmatchedProperties ? YamlUnmappedMemberHandling.Disallow : options.UnmappedMemberHandling;
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
        if (handling == YamlNumberHandling.None || !IsSupportedNumberHandlingType(memberType))
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

    private static bool IsSupportedNumberHandlingType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying == typeof(byte) || underlying == typeof(sbyte)
            || underlying == typeof(short) || underlying == typeof(ushort)
            || underlying == typeof(int) || underlying == typeof(uint)
            || underlying == typeof(long) || underlying == typeof(ulong)
            || underlying == typeof(float) || underlying == typeof(double)
            || underlying == typeof(decimal)
            || underlying == typeof(nint) || underlying == typeof(nuint);
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

        var converterType = attribute.ConverterType;
        if (converterType.IsGenericTypeDefinition)
        {
            throw new NotSupportedException($"Converter type '{converterType}' cannot be an open generic type.");
        }

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

        if (type.IsAbstract || type.IsInterface || type.IsValueType)
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

        if (attributed is not null)
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

    private static string GetMemberName(MemberInfo member, YamlReaderWriterBase readerWriter)
    {
        var yamlName = member.GetCustomAttribute<YamlPropertyNameAttribute>(inherit: true);
        if (yamlName is not null)
        {
            return yamlName.Name;
        }

        var name = member.Name;
        return readerWriter.ConvertName(name);
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
