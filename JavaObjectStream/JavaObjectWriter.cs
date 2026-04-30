using System.Reflection;
using System.Text;
using FiliusModemInterface.JavaObjectStream.Attributes;
using static FiliusModemInterface.JavaObjectStream.JavaSerializerHelper;

namespace FiliusModemInterface.JavaObjectStream;

/// <summary>
/// The basic part of this class and used dtos were written by Claude.ai and modified by me to work.
/// </summary>
public sealed class JavaObjectWriter : IDisposable
{
    private readonly BinaryWriter _writer;
    private readonly Func<string, JavaClassDesc> _getClassDesc;

    private int _indexCounter = 0;
    private readonly Dictionary<object, int> _handles = new(ReferenceEqualityComparer.Instance);

    public JavaObjectWriter(Stream stream, Func<string, JavaClassDesc> getClassDesc, Encoding? encoding = null)
    {
        if (stream is not { CanWrite: true })
            throw new ArgumentException("Stream must be writable", nameof(stream));
        _writer = new BinaryWriter(stream, encoding ?? Encoding.UTF8);
        _getClassDesc = getClassDesc;
    }

    public void WriteObject(object obj)
    {
        if (obj is string str)
        {
            WriteStringObject(str);
            return;
        }
        
        Type type = obj.GetType();
        if (type.GetCustomAttribute<JavaClassAttribute>() is not { } classAttribute)
            throw new NotSupportedException($"Cannot deserialize object of type {type}");
        
        JavaClassDesc classDesc = _getClassDesc(classAttribute.ClassName);
        
        _writer.Write(TcObject);
        WriteClassDescEntry(classDesc);
        WriteClassData(obj, type, classDesc);
        _writer.Flush();
    }

    private void WriteClassDescEntry(JavaClassDesc? classDesc)
    {
        if (classDesc is null)
        {
            _writer.Write(TcNull);
            return;
        }

        if (TryGetHandle(classDesc, out int handle))
        {
            _writer.Write(TcReference);
            WriteS32(_writer, handle + BaseWireHandle);
            return;
        }
        AssignHandle(classDesc);

        _writer.Write(TcClassDesc);
        WriteUtf(_writer, classDesc.ClassName);
        WriteS64(_writer, classDesc.SerialVersionUid);
        _writer.Write((byte)classDesc.ClassDescFlags);
        WriteU16(_writer, (ushort)classDesc.Fields.Count);
        
        foreach (JavaFieldDesc field in JavaObjectSerializer.OrderFields(classDesc.Fields))
        {
            _writer.Write((byte)field.TypeCode);
            WriteUtf(_writer, field.Name);

            if (field.TypeCode is not ('L' or '['))
                continue;
            
            // Für Objekt/Array-Felder muss der Klassenname als TC_STRING folgen
            if (TryGetHandle(field.ClassName!, out int classNameHandle))
            {
                _writer.Write(TcReference);
                WriteS32(_writer, classNameHandle + BaseWireHandle);
            }
            else
            {
                WriteStringObject($"L{field.ClassName};");     // Expected format for object fields
            }
        }

        // Annotations: TC_ENDBLOCKDATA (keine custom writeObject-Daten)
        _writer.Write(TcEndBlockData);

        // Handle NACH den Feldern aber VOR der Superklasse zuweisen (spiegelt ReadClassDesc)

        WriteClassDescEntry(classDesc.SuperClass);
    }
    
    private void WriteClassData(object obj, Type type, JavaClassDesc classDesc)
    {
        AssignHandle(obj);
        WriteClassDataRecursive(obj, type, classDesc);
    }

    private void WriteClassDataRecursive(object obj, Type type, JavaClassDesc classDesc)
    {
        if (classDesc.SuperClass is not null)
            WriteClassDataRecursive(obj, type.BaseType!, classDesc.SuperClass);

        if (!classDesc.ClassDescFlags.HasFlag(JavaClassFlags.Externalizable))
        {
            foreach (JavaFieldDesc field in JavaObjectSerializer.OrderFields(classDesc.Fields))
            {
                PropertyInfo property = JavaObjectSerializer.GetPropertyByFieldName(classDesc.ClassName, field.Name)!;
                WriteFieldValue(field.TypeCode, property.GetValue(obj));
            }
        }

        if (classDesc.ClassDescFlags.HasFlag(JavaClassFlags.WriteMethod)
            || classDesc.ClassDescFlags.HasFlag(JavaClassFlags.Externalizable))
        {
            MethodInfo writeMethod = type.GetMethod("WriteObject", BindingFlags.Public | BindingFlags.Instance, [typeof(BinaryWriter), typeof(JavaObjectWriter)])
                                     ?? throw new NotImplementedException($"Type {type} does not implement the writer correctly!");
            writeMethod.Invoke(obj, [_writer, this]);
            _writer.Write(TcEndBlockData);
        }
    }

    // --- Feldwerte ---

    private void WriteFieldValue(char typeCode, object? value)
    {
        switch (typeCode)
        {
            case 'B': _writer.Write(Convert.ToSByte(value));    break;
            case 'C': WriteU16(_writer, Convert.ToChar(value));          break;
            case 'D': WriteDouble(_writer, Convert.ToDouble(value));     break;
            case 'F': WriteFloat(_writer, Convert.ToSingle(value));      break;
            case 'I': WriteS32(_writer, Convert.ToInt32(value));         break;
            case 'J': WriteS64(_writer, Convert.ToInt64(value));         break;
            case 'S': WriteS16(_writer, Convert.ToInt16(value));         break;
            case 'Z': _writer.Write(Convert.ToBoolean(value));  break;

            case 'L':
                WriteObjectField(value);
                break;

            case '[':
                WriteArrayField(value);
                break;

            default:
                throw new NotSupportedException($"Unknown field type: {typeCode}");
        }
    }

    private void WriteObjectField(object? value)
    {
        if (value is null)
        {
            _writer.Write(TcNull);
        }
        else if (TryGetHandle(value, out int handle))
        {
            _writer.Write(TcReference);
            WriteS32(_writer, handle + BaseWireHandle);
        }
        else if (value is string str)
        {
            WriteStringObject(str);
        }
        else
        {
            WriteObject(value);
        }
    }

    private void WriteArrayField(object? value)
    {
        if (value is null)
        {
            _writer.Write(TcNull);
            return;
        }

        if (TryGetHandle(value, out int handle))
        {
            _writer.Write(TcReference);
            WriteS32(_writer, handle + BaseWireHandle);
            return;
        }

        if (value is byte[] byteArray)
        {
            _writer.Write(TcArray);

            // ClassDesc für [B
            _writer.Write(TcClassDesc);
            WriteUtf(_writer, "[B");
            WriteS64(_writer, -5984697328218786468L); // Standard serialVersionUID für byte[]
            _writer.Write((byte)0x02);       // SC_SERIALIZABLE
            WriteU16(_writer, 0);                      // Keine Felder
            _writer.Write(TcEndBlockData);
            AssignHandle(new object());       // Dummy-Handle für die ClassDesc
            _writer.Write(TcNull);            // Keine Superklasse

            AssignHandle(byteArray);
            WriteS32(_writer, byteArray.Length);
            _writer.Write(byteArray);
            return;
        }

        throw new NotSupportedException($"Nur byte[] Arrays werden unterstützt.");
    }

    private void WriteStringObject(string value)
    {
        if (value.Length <= 0xFFFF)
        {
            _writer.Write(TcString);
            WriteUtf(_writer, value);
        }
        else
        {
            _writer.Write(TcLongString);
            WriteUtfLong(_writer, value);
        }

        AssignHandle(value);
    }

    // --- Handle-Verwaltung ---

    public bool TryGetHandle(object value, out int handle) => _handles.TryGetValue(value, out handle);
    
    public void AssignHandle(object obj, int index = -1)
    {
        if (index == -1)
            index = _indexCounter++;
        _handles[obj] = index;
    }

    private int ReserveHandle() => _indexCounter++;
    
    public void Dispose() => _writer.Dispose();
}