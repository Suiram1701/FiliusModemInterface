using System.Reflection;
using FiliusModemInterface.JavaObjectStream.Attributes;

namespace FiliusModemInterface.JavaObjectStream;

public static class JavaObjectSerializer
{
    private static readonly Lock _lock = new();
    private static readonly Dictionary<string, Type> _classesCache = new();
    private static readonly Dictionary<string, PropertyInfo> _propertiesCache = new();
    
    public static T DeserializeObject<T>(JavaObject obj)
    {
        object @object = DeserializeObject(obj);
        if (@object.GetType() != typeof(T))
            throw new InvalidOperationException($"Unable to deserialize object {@object.GetType()} ({obj.ClassName}) into {typeof(T)}");
        return (T)@object;
    }
    
    public static object DeserializeObject(JavaObject obj)
    {
        if (GetTypeByJavaClass(obj.ClassName) is not { } type)
            throw new NotSupportedException($"Cannot deserialize object of type {obj.ClassName}");
        
        object instance = Activator.CreateInstance(type)!;
        foreach ((string fieldName, object value) in obj.Fields)
        {
            PropertyInfo? property = GetPropertyByFieldName(obj.ClassName, fieldName);
            if (property is null)
                throw new NotSupportedException($"Field {fieldName} isn't supported on type {type}");

            if (value is JavaObject valueObject)
                property.SetValue(instance, DeserializeObject(valueObject));
            else
                property.SetValue(instance, value);
        }
        
        return instance;
    }

    private static Type? GetTypeByJavaClass(string className)
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

    private static PropertyInfo? GetPropertyByFieldName(string className, string fieldName)
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
}