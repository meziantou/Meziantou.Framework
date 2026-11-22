using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Meziantou.Framework.Csv.Serialization.Converters;

namespace Meziantou.Framework.Csv.Serialization.Metadata;

/// <summary>Creates the metadata of types with reflection.</summary>
/// <remarks>This resolver is not compatible with trimming and Native AOT: use a generated <see cref="CsvSerializerContext"/> instead.</remarks>
public sealed class ReflectionCsvTypeInfoResolver : ICsvTypeInfoResolver
{
    private const BindingFlags DeclaredMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>Initializes a new instance of the <see cref="ReflectionCsvTypeInfoResolver"/> class.</summary>
    [RequiresUnreferencedCode(CsvSerializer.ReflectionMessage)]
    [RequiresDynamicCode(CsvSerializer.ReflectionMessage)]
    public ReflectionCsvTypeInfoResolver()
    {
    }

    /// <summary>Gets the default instance.</summary>
    [field: MaybeNull]
    public static ReflectionCsvTypeInfoResolver Default
    {
        [RequiresUnreferencedCode(CsvSerializer.ReflectionMessage)]
        [RequiresDynamicCode(CsvSerializer.ReflectionMessage)]
        get => field ??= new ReflectionCsvTypeInfoResolver();
    }

    /// <inheritdoc/>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The constructor of the resolver requires unreferenced code.")]
    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "The constructor of the resolver requires unreferenced code.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The constructor of the resolver requires dynamic code.")]
    public CsvTypeInfo? GetTypeInfo(Type type, CsvSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(options);

        if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters || type.IsPointer || type.IsByRef || type.IsByRefLike)
            throw new NotSupportedException($"The type '{type}' cannot be serialized: it must be a concrete class or struct.");

        var method = typeof(ReflectionCsvTypeInfoResolver).GetMethod(nameof(CreateTypeInfo), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type);
        try
        {
            return (CsvTypeInfo)method.Invoke(obj: null, [options])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    [RequiresUnreferencedCode(CsvSerializer.ReflectionMessage)]
    [RequiresDynamicCode(CsvSerializer.ReflectionMessage)]
    private static CsvTypeInfo<T> CreateTypeInfo<T>(CsvSerializerOptions options)
    {
        var type = typeof(T);
        var members = GetMembers(type, options);

        // Constructor
        var constructor = SelectConstructor(type);
        var parameters = constructor?.GetParameters() ?? [];
        var parameterValues = new CsvParameterInfoValues[parameters.Length];
        var parameterMembers = new int[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            parameterValues[i] = new CsvParameterInfoValues
            {
                Name = parameter.Name ?? "",
                ParameterType = parameter.ParameterType,
                Position = i,
                HasDefaultValue = parameter.HasDefaultValue,
                DefaultValue = parameter.HasDefaultValue ? parameter.DefaultValue : null,
            };

            parameterMembers[i] = members.FindIndex(member => string.Equals(member.Name, parameter.Name, StringComparison.OrdinalIgnoreCase) && member.Type == parameter.ParameterType);
        }

        var properties = new List<CsvPropertyInfo>(members.Count + parameters.Length);
        for (var i = 0; i < members.Count; i++)
        {
            var parameterIndex = Array.IndexOf(parameterMembers, i);
            properties.Add(CreatePropertyInfo<T>(options, members[i], parameterIndex));
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameterMembers[i] < 0)
            {
                // A parameter without a member is a column that is read but not written
                properties.Add(CreatePropertyInfo<T>(options, new MemberData(parameters[i].Name ?? "", parameters[i].ParameterType, Member: null, parameters[i], Include: false), i));
            }
        }

        Func<T>? objectCreator = null;
        Func<CsvConstructorArguments, T>? parameterizedCreator = null;
        if (constructor is not null)
        {
            if (parameters.Length > 0)
            {
                parameterizedCreator = CreateParameterizedCreator<T>(constructor, parameters);
            }
            else if (RuntimeFeature.IsDynamicCodeCompiled)
            {
                objectCreator = Expression.Lambda<Func<T>>(Expression.New(constructor)).Compile();
            }
            else
            {
                var invoker = ConstructorInvoker.Create(constructor);
                objectCreator = () => (T)invoker.Invoke();
            }
        }
        else if (type.IsValueType)
        {
            objectCreator = () => default!;
        }

        return CsvMetadataServices.CreateObjectInfo(options, new CsvObjectInfoValues<T>
        {
            ObjectCreator = objectCreator,
            ObjectWithParameterizedConstructorCreator = parameterizedCreator,
            ConstructorParameters = parameterValues,
            Properties = properties,
        });
    }

    [RequiresUnreferencedCode(CsvSerializer.ReflectionMessage)]
    private static ConstructorInfo? SelectConstructor(Type type)
    {
        var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var annotated = constructors.Where(c => c.IsDefined(typeof(CsvConstructorAttribute), inherit: false)).ToArray();
        if (annotated.Length > 1)
            throw new InvalidOperationException($"The type '{type}' has more than one constructor with CsvConstructorAttribute.");

        if (annotated.Length == 1)
            return annotated[0];

        var publicConstructors = constructors.Where(c => c.IsPublic).ToArray();
        var parameterless = Array.Find(publicConstructors, c => c.GetParameters().Length == 0);
        if (parameterless is not null)
            return parameterless;

        if (type.IsValueType)
            return null;

        if (publicConstructors.Length == 1)
            return publicConstructors[0];

        throw new InvalidOperationException($"The type '{type}' has no public parameterless constructor and more than one public constructor: use CsvConstructorAttribute to select one.");
    }

    [RequiresUnreferencedCode(CsvSerializer.ReflectionMessage)]
    private static List<MemberData> GetMembers(Type type, CsvSerializerOptions options)
    {
        var hierarchy = new List<Type>();
        for (var current = type; current is not null && current != typeof(object) && current != typeof(ValueType); current = current.BaseType)
        {
            hierarchy.Add(current);
        }

        hierarchy.Reverse();
        var result = new List<MemberData>();
        foreach (var current in hierarchy)
        {
            // Properties first, then fields, each in declaration order: the source generator uses the same order
            foreach (var member in current.GetMembers(DeclaredMembers).OrderBy(member => member is FieldInfo ? 1 : 0).ThenBy(member => member.MetadataToken))
            {
                if (member.IsDefined(typeof(CsvIgnoreAttribute), inherit: true))
                    continue;

                var include = member.IsDefined(typeof(CsvIncludeAttribute), inherit: true);
                Type memberType;
                switch (member)
                {
                    case PropertyInfo property:
                        if (property.GetIndexParameters().Length > 0)
                            continue;

                        var isPublic = property.GetMethod?.IsPublic == true || property.SetMethod?.IsPublic == true;
                        if (!isPublic && !include)
                            continue;

                        if (property.GetMethod?.IsStatic == true || property.SetMethod?.IsStatic == true)
                            continue;

                        memberType = property.PropertyType;
                        break;

                    case FieldInfo field:
                        if (field.IsStatic || field.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
                            continue;

                        if (!(include || (field.IsPublic && options.IncludeFields)))
                            continue;

                        memberType = field.FieldType;
                        break;

                    default:
                        continue;
                }

                if (memberType.IsByRef || memberType.IsPointer || memberType.IsByRefLike)
                    continue;

                var data = new MemberData(member.Name, memberType, member, Parameter: null, include);
                var existing = result.FindIndex(m => m.Name == member.Name);
                if (existing >= 0)
                {
                    // An overridden or hidden member keeps the position of the member of the base type
                    result[existing] = data;
                }
                else
                {
                    result.Add(data);
                }
            }
        }

        return result;
    }

    [RequiresUnreferencedCode(CsvSerializer.ReflectionMessage)]
    [RequiresDynamicCode(CsvSerializer.ReflectionMessage)]
    private static CsvPropertyInfo CreatePropertyInfo<T>(CsvSerializerOptions options, MemberData member, int parameterIndex)
    {
        var method = typeof(ReflectionCsvTypeInfoResolver).GetMethod(nameof(CreateTypedPropertyInfo), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(typeof(T), member.Type);
        try
        {
            return (CsvPropertyInfo)method.Invoke(obj: null, [options, member, parameterIndex])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    [RequiresUnreferencedCode(CsvSerializer.ReflectionMessage)]
    [RequiresDynamicCode(CsvSerializer.ReflectionMessage)]
    private static CsvPropertyInfo CreateTypedPropertyInfo<TDeclaring, TValue>(CsvSerializerOptions options, MemberData member, int parameterIndex)
    {
        var attributes = member.Member;
        var getter = default(Func<TDeclaring, TValue>);
        var setter = default(CsvPropertySetter<TDeclaring, TValue>);
        var isRequired = false;
        switch (member.Member)
        {
            case PropertyInfo property:
                getter = CreateGetter<TDeclaring, TValue>(property, member.Include);
                if (parameterIndex < 0)
                {
                    setter = CreateSetter<TDeclaring, TValue>(property, member.Include);
                }

                isRequired = property.IsDefined(typeof(RequiredMemberAttribute), inherit: false);
                break;

            case FieldInfo field:
                getter = CreateGetter<TDeclaring, TValue>(field);
                if (parameterIndex < 0 && !field.IsInitOnly)
                {
                    setter = CreateSetter<TDeclaring, TValue>(field);
                }

                isRequired = field.IsDefined(typeof(RequiredMemberAttribute), inherit: false);
                break;
        }

        isRequired |= attributes?.IsDefined(typeof(CsvRequiredAttribute), inherit: true) == true;

        CsvConverter? customConverter = null;
        if (attributes?.GetCustomAttribute<CsvConverterAttribute>(inherit: true) is { } converterAttribute)
        {
            customConverter = Activator.CreateInstance(converterAttribute.ConverterType) as CsvConverter
                ?? throw new InvalidOperationException($"The type '{converterAttribute.ConverterType}' of the CsvConverterAttribute of '{typeof(TDeclaring)}.{member.Name}' is not a CsvConverter.");
        }

        return CsvMetadataServices.CreatePropertyInfo(options, new CsvPropertyInfoValues<TDeclaring, TValue>
        {
            MemberName = member.Name,
            ColumnName = attributes?.GetCustomAttribute<CsvPropertyNameAttribute>(inherit: true)?.Name,
            Order = attributes?.GetCustomAttribute<CsvPropertyOrderAttribute>(inherit: true)?.Order ?? 0,
            ColumnIndex = attributes?.GetCustomAttribute<CsvColumnIndexAttribute>(inherit: true)?.Index,
            Format = attributes?.GetCustomAttribute<CsvFormatAttribute>(inherit: true)?.Format,
            IsRequired = isRequired,
            Getter = getter,
            Setter = setter,
            Converter = customConverter,
            DefaultConverter = CreateDefaultConverter<TValue>(),
            ParameterIndex = parameterIndex,
        });
    }

    // The accessors are compiled when the runtime compiles dynamic code: a compiled accessor is one delegate call, without boxing. Otherwise, they are
    // delegates bound to the accessor methods, or reflection calls for the fields.
    [RequiresDynamicCode(CsvSerializer.ReflectionMessage)]
    private static Func<TDeclaring, TValue>? CreateGetter<TDeclaring, TValue>(PropertyInfo property, bool includeNonPublic)
    {
        var getMethod = property.GetMethod;
        if (getMethod is null || (!getMethod.IsPublic && !includeNonPublic))
            return null;

        if (RuntimeFeature.IsDynamicCodeCompiled)
        {
            var obj = Expression.Parameter(typeof(TDeclaring), "obj");
            return Expression.Lambda<Func<TDeclaring, TValue>>(Expression.Property(obj, property), obj).Compile();
        }

        if (!typeof(TDeclaring).IsValueType)
            return getMethod.CreateDelegate<Func<TDeclaring, TValue>>();

        // The receiver of an instance method of a value type is passed by reference
        var refGetter = getMethod.CreateDelegate<RefGetter<TDeclaring, TValue>>();
        return obj => refGetter(ref obj);
    }

    [RequiresDynamicCode(CsvSerializer.ReflectionMessage)]
    private static CsvPropertySetter<TDeclaring, TValue>? CreateSetter<TDeclaring, TValue>(PropertyInfo property, bool includeNonPublic)
    {
        var setMethod = property.SetMethod;
        if (setMethod is null || (!setMethod.IsPublic && !includeNonPublic))
            return null;

        if (RuntimeFeature.IsDynamicCodeCompiled)
            return CompileSetter<TDeclaring, TValue>(obj => Expression.Property(obj, property));

        if (!typeof(TDeclaring).IsValueType)
        {
            var action = setMethod.CreateDelegate<Action<TDeclaring, TValue>>();
            return (ref TDeclaring obj, TValue value) => action(obj, value);
        }

        // The receiver of an instance method of a value type is passed by reference, as in CsvPropertySetter
        return setMethod.CreateDelegate<CsvPropertySetter<TDeclaring, TValue>>();
    }

    [RequiresDynamicCode(CsvSerializer.ReflectionMessage)]
    private static Func<TDeclaring, TValue> CreateGetter<TDeclaring, TValue>(FieldInfo field)
    {
        if (RuntimeFeature.IsDynamicCodeCompiled)
        {
            var obj = Expression.Parameter(typeof(TDeclaring), "obj");
            return Expression.Lambda<Func<TDeclaring, TValue>>(Expression.Field(obj, field), obj).Compile();
        }

        return obj => (TValue)field.GetValue(obj)!;
    }

    [RequiresDynamicCode(CsvSerializer.ReflectionMessage)]
    private static CsvPropertySetter<TDeclaring, TValue> CreateSetter<TDeclaring, TValue>(FieldInfo field)
    {
        if (RuntimeFeature.IsDynamicCodeCompiled)
            return CompileSetter<TDeclaring, TValue>(obj => Expression.Field(obj, field));

        if (typeof(TDeclaring).IsValueType)
        {
            return (ref TDeclaring obj, TValue value) =>
            {
                object boxed = obj!;
                field.SetValue(boxed, value);
                obj = (TDeclaring)boxed;
            };
        }

        return (ref TDeclaring obj, TValue value) => field.SetValue(obj, value);
    }

    [RequiresDynamicCode(CsvSerializer.ReflectionMessage)]
    private static CsvPropertySetter<TDeclaring, TValue> CompileSetter<TDeclaring, TValue>(Func<ParameterExpression, MemberExpression> member)
    {
        // (ref TDeclaring obj, TValue value) => obj.Member = value
        var obj = Expression.Parameter(typeof(TDeclaring).MakeByRefType(), "obj");
        var value = Expression.Parameter(typeof(TValue), "value");
        return Expression.Lambda<CsvPropertySetter<TDeclaring, TValue>>(Expression.Assign(member(obj), value), obj, value).Compile();
    }

    [RequiresDynamicCode(CsvSerializer.ReflectionMessage)]
    private static Func<CsvConstructorArguments, T> CreateParameterizedCreator<T>(ConstructorInfo constructor, ParameterInfo[] parameters)
    {
        if (RuntimeFeature.IsDynamicCodeCompiled)
        {
            // args => new T(args.GetValue<P0>(0), args.GetValue<P1>(1), ...): the values are not boxed
            var args = Expression.Parameter(typeof(CsvConstructorArguments), "args");
            var getValue = typeof(CsvConstructorArguments).GetMethod(nameof(CsvConstructorArguments.GetValue))!;
            var values = parameters.Select((parameter, index) => (Expression)Expression.Call(args, getValue.MakeGenericMethod(parameter.ParameterType), Expression.Constant(index)));
            return Expression.Lambda<Func<CsvConstructorArguments, T>>(Expression.New(constructor, values), args).Compile();
        }

        var invoker = ConstructorInvoker.Create(constructor);
        return args =>
        {
            var values = new object?[args.Count];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = args.GetBoxedValue(i);
            }

            return (T)invoker.Invoke(values.AsSpan());
        };
    }

    private delegate TValue RefGetter<TDeclaring, out TValue>(ref TDeclaring obj);

    [RequiresUnreferencedCode(CsvSerializer.ReflectionMessage)]
    [RequiresDynamicCode(CsvSerializer.ReflectionMessage)]
    private static CsvConverter<TValue>? CreateDefaultConverter<TValue>()
    {
        var type = typeof(TValue);
        if (CsvDefaultConverters.Get<TValue>() is { } builtIn)
            return builtIn;

        if (type.IsEnum)
            return (CsvConverter<TValue>)Activator.CreateInstance(typeof(EnumCsvConverter<>).MakeGenericType(type))!;

        if (Nullable.GetUnderlyingType(type) is { } underlyingType)
        {
            var innerConverter = typeof(ReflectionCsvTypeInfoResolver).GetMethod(nameof(CreateDefaultConverter), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(underlyingType).Invoke(obj: null, parameters: null);
            if (innerConverter is null)
                return null;

            return (CsvConverter<TValue>)Activator.CreateInstance(typeof(NullableCsvConverter<>).MakeGenericType(underlyingType), innerConverter)!;
        }

        if (ImplementsGeneric(type, typeof(ISpanParsable<>)) && typeof(ISpanFormattable).IsAssignableFrom(type))
        {
            if (ImplementsGeneric(type, typeof(IUtf8SpanParsable<>)) && typeof(IUtf8SpanFormattable).IsAssignableFrom(type))
                return (CsvConverter<TValue>)Activator.CreateInstance(typeof(Utf8SpanParsableCsvConverter<>).MakeGenericType(type))!;

            return (CsvConverter<TValue>)Activator.CreateInstance(typeof(SpanParsableCsvConverter<>).MakeGenericType(type))!;
        }

        return null;

        static bool ImplementsGeneric(Type type, Type genericInterface)
            => Array.Exists(type.GetInterfaces(), i => i.IsGenericType && i.GetGenericTypeDefinition() == genericInterface && i.GetGenericArguments()[0] == type);
    }

    private sealed record MemberData(string Name, Type Type, MemberInfo? Member, ParameterInfo? Parameter, bool Include);
}
