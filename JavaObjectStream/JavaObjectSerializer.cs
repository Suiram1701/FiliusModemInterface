using System.Reflection;
using FiliusModemInterface.JavaObjectStream.Attributes;

namespace FiliusModemInterface.JavaObjectStream;

public static class JavaObjectSerializer
{
    private static readonly Lock _lock = new();
    private static readonly Dictionary<string, Type> _classesCache = new();
    private static readonly Dictionary<string, PropertyInfo> _propertiesCache = new();
    private static readonly Dictionary<Type, JavaClassDesc> _classesDescCache = new();

    private static readonly IReadOnlyDictionary<Type, string> _staticTypeClasses = new Dictionary<Type, string>()
    {
        { typeof(string), "java/lang/String" },
        { typeof(Filius.Utils.HashSet), "java/util/Set" }
    }.AsReadOnly();

    public static Type? GetTypeByJavaClass(string className)
    {
        lock (_lock)
        {
            if (_classesCache.Count == 0)
            {
                foreach ((Type @class, JavaClassAttribute? attribute) in Assembly.GetExecutingAssembly()
                             .GetTypes()
                             .Select(t => (t, t.GetCustomAttribute<JavaClassAttribute>()))
                             .Where(pair => pair.Item2 is not null))
                {
                    _classesCache.Add(attribute!.ClassName, @class);
                }
            }
            
            return _classesCache.GetValueOrDefault(className);
        }
    }
    
    public static JavaClassDesc GetClassDesc(string className)
    {
        Type type = GetTypeByJavaClass(className) ?? throw new NotSupportedException($"Class {className} is not supported.");
        return GetClassDesc(type);
    }
    
    public static PropertyInfo? GetPropertyByFieldName(string className, string fieldName)
    {
        var key = $"{className}.{fieldName}";
        lock (_lock)
        {
            if (!_propertiesCache.ContainsKey(key))
            {
                Type type = GetTypeByJavaClass(className)!;
                foreach ((PropertyInfo propertyInfo, JavaFieldAttribute? attribute) in type
                             .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                             .Select(info => (info, info.GetCustomAttribute<JavaFieldAttribute>(true)))
                             .Where(pair => pair.Item2 is not null))
                {
                    _propertiesCache.TryAdd($"{className}.{attribute!.FieldName}", propertyInfo);
                }
            }

            return _propertiesCache.GetValueOrDefault(key);
        }
    }
    
    public static JavaClassDesc GetClassDesc(Type type)
    {
        lock (_lock)
        {
            if (!_classesDescCache.ContainsKey(type))
            {
                var attribute = type.GetCustomAttribute<JavaClassAttribute>();
                if (attribute is null)
                    throw new NotSupportedException($"Class {type} does not support Java serialization.");

                JavaClassDesc? superClass = type.BaseType is not null && type.BaseType != typeof(object)
                    ? GetClassDesc(type.BaseType)
                    : null;
                List<JavaFieldDesc> fields = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(info => (info, info.GetCustomAttribute<JavaFieldAttribute>(true)))
                    .Where(pair => pair.Item2 is not null)
                    .Select(pair => GetFieldDesc(pair.info, pair.Item2!))
                    .ToList();
                _classesDescCache.Add(type, new JavaClassDesc(attribute.ClassName, attribute.SerialVersionUid, attribute.ClassFlags, fields, superClass));
            }
            
            return _classesDescCache[type];
        }
    }
    
    private static JavaFieldDesc GetFieldDesc(PropertyInfo info, JavaFieldAttribute attribute)
    {
        char? typeCode = info.PropertyType.Name switch
        {
            nameof(SByte) => 'B',
            nameof(UInt16) => 'C',
            nameof(Double) => 'D',
            nameof(Single) => 'F',
            nameof(Int32) => 'I',
            nameof(Int64) => 'J',
            nameof(Int16) => 'S',
            nameof(Boolean) => 'Z',
            _ => null
        };
        if (info.PropertyType.IsArray)
            typeCode = '[';

        string? fieldClassName = null;
        if (typeCode is null)
        {
            typeCode = 'L';
            fieldClassName = _staticTypeClasses.GetValueOrDefault(info.PropertyType)
                             ?? _classesCache.SingleOrDefault(kvp => kvp.Value == info.PropertyType).Key;
        }

        return new JavaFieldDesc(attribute.FieldName, typeCode.Value, fieldClassName);
    }
}