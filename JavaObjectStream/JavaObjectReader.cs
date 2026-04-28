using System.Net;
using System.Reflection;
using System.Text;
using FiliusModemInterface.JavaObjectStream.Attributes;
using static FiliusModemInterface.JavaObjectStream.JavaSerializerHelper;

namespace FiliusModemInterface.JavaObjectStream;

/// <summary>
/// A stream reader which is beable to parse a binary java object stream. The reader doesn't care about the stream header. The first 4 bytes have to be read
/// </summary>
/// <remarks>The basic part of this class and used dtos were written by Claude.ai and modified by me to work.</remarks>
public sealed class JavaObjectReader : IDisposable
{
    private readonly Encoding _encoding;
    private readonly BinaryReader _reader;
    private readonly List<object> _handles = [];

    public JavaObjectReader(Stream stream, Encoding? encoding = null)
    {
        if (stream is not { CanRead: true, CanSeek: true })
            throw new ArgumentException("Stream must be readable and seekable", nameof(stream));
        _encoding ??= Encoding.UTF8;
        _reader = new BinaryReader(stream, _encoding);
    }
    
    public T ReadObject<T>()
    {
        object @object = ReadObject();
        if (@object.GetType() != typeof(T))
            throw new InvalidOperationException($"Unable to deserialize object {@object.GetType()} into {typeof(T)}");
        return (T)@object;
    }
    
    public object ReadObject()
    {
        int oldHandlesCount = _handles.Count;
        try
        {
            return ReadContent()!;
        }
        catch (EndOfStreamException)
        {
            _handles.RemoveRange(oldHandlesCount, _handles.Count - (oldHandlesCount + 1));
            throw;
        }
    }

    private object? ReadContent()
    {
        byte tag = _reader.ReadByte();
        return tag switch
        {
            TcObject        => ReadTcObject(),
            TcArray         => ReadTcArray(),
            TcString        => ReadTcString(false),
            TcLongString    => ReadTcString(true),
            TcReference     => ReadTcReference(),
            TcNull          => null,
            TcBlockData     => ReadBlockData(false),
            TcBlockDataLong => ReadBlockData(true),
            TcEndBlockData  => throw new InvalidDataException("Unexpected TC_ENDBLOCKDATA outside of annotation."),
            _ => throw new NotSupportedException($"Unknown Tag: 0x{tag:X2}")
        };
    }

    private object ReadTcObject()
    {
        JavaClassDesc classDesc = ReadClassDesc();
        if (JavaObjectSerializer.GetTypeByJavaClass(classDesc.ClassName) is not { } type)
            throw new NotSupportedException($"Cannot deserialize object of type {classDesc.ClassName}");
        
        var classAttribute = type.GetCustomAttribute<JavaClassAttribute>()!;
        if (classAttribute.SerialVersionUid != 0L && classAttribute.SerialVersionUid != classDesc.SerialVersionUid)
            throw new InvalidOperationException($"Read version Uid {classDesc.SerialVersionUid} does not match the expected version {classAttribute.SerialVersionUid}");
        if (classAttribute.ClassFlags != classDesc.ClassDescFlags)
            throw new InvalidOperationException($"Read flags {classDesc.ClassDescFlags:X2} does not match the flags {classAttribute.ClassFlags:X2}");
        
        object instance = Activator.CreateInstance(type)!;
        AssignHandle(instance);

        ReadClassData(instance, type, classDesc);
        return instance;
    }

    private void ReadClassData(object obj, Type type, JavaClassDesc classDesc)
    {
        if (classDesc.SuperClass != null)
            ReadClassData(obj, type.BaseType!, classDesc.SuperClass);

        if (!classDesc.ClassDescFlags.HasFlag(JavaClassFlags.Externalizable))
        {
            foreach (JavaFieldDesc field in classDesc.Fields)
            {
                PropertyInfo property = JavaObjectSerializer.GetPropertyByFieldName(classDesc.ClassName, field.Name)!;
                property.SetValue(obj, ReadFieldValue(field.TypeCode));
            }
        }

        if (classDesc.ClassDescFlags.HasFlag(JavaClassFlags.WriteMethod)
            || classDesc.ClassDescFlags.HasFlag(JavaClassFlags.Externalizable))
        {
            MethodInfo readMethod = type.GetMethod("ReadObject", BindingFlags.Public | BindingFlags.Instance, [typeof(BinaryReader), typeof(JavaObjectReader)])
                                    ?? throw new NotImplementedException($"Type {type} does not implement the reader correctly!");
            byte[] annotation = CaptureAnnotation();
            BinaryReader annotationReader = new(new MemoryStream(annotation), _encoding, leaveOpen: false);
            readMethod.Invoke(obj, [annotationReader, this]);
        }
    }

    private object ReadFieldValue(char typeCode)
    {
        return typeCode switch
        {
            'B' => _reader.ReadSByte(),
            'C' => (char)ReadU16(_reader),
            'D' => ReadDouble(_reader),
            'F' => ReadFloat(_reader),
            'I' => ReadS32(_reader),
            'J' => ReadS64(_reader),
            'S' => ReadS16(_reader),
            'Z' => _reader.ReadBoolean(),
            '[' => ReadContent()!, // Array
            'L' => ReadContent()!, // Objekt-Referenz
            _   => throw new NotSupportedException($"Unknown field type: {typeCode}")
        };
    }

    private JavaClassDesc ReadClassDesc()
    {
        byte tag = _reader.ReadByte();

        if (tag == TcNull)
            return null!;
        if (tag == TcReference)
            return (JavaClassDesc)ReadTcReferenceRaw();
        if (tag != TcClassDesc)
            throw new InvalidDataException($"Expected TC_CLASSDESC, got 0x{tag:X2}");

        int index = _handles.Count;
        AssignHandle(null!);     // Dummy
        
        string className       = ReadUtf(_reader);
        long   serialVersionUid = ReadS64(_reader);
        byte   classDescFlags  = _reader.ReadByte();
        ushort fieldCount      = ReadU16(_reader);

        List<JavaFieldDesc> fields = new(fieldCount);
        for (var i = 0; i < fieldCount; i++)
        {
            var   typeCode  = (char)_reader.ReadByte();
            string fieldName = ReadUtf(_reader);

            string? fieldClassName = null;
            if (typeCode is 'L' or '[')     // Für Objekt/Array-Typen folgt der Klassenname als String-Content
                fieldClassName = (string)ReadContent()!;

            fields.Add(new JavaFieldDesc(fieldName, typeCode, fieldClassName?.Substring(1, fieldCount - 2)));     // An object field will start with L and ends with ;
        }

        // TC_ENDBLOCKDATA überspringen (optionale Annotations ignorieren)
        SkipAnnotations();

        JavaClassDesc superClass = ReadClassDesc();

        JavaClassDesc desc = new(className, serialVersionUid, (JavaClassFlags)classDescFlags, fields, superClass);
        AssignHandle(desc, index);
        return desc;
    }

    private object ReadTcArray()
    {
        JavaClassDesc classDesc = ReadClassDesc();
        AssignHandle(null!);

        int size = ReadS32(_reader);

        // Elementtyp aus dem Klassenname ableiten: "[B" = byte[], "[I" = int[], ...
        char elementType = classDesc.ClassName.Length > 1 ? classDesc.ClassName[1] : 'B';

        if (elementType == 'B')
        {
            byte[] bytes = _reader.ReadBytes(size);
            _handles[^1] = bytes;
            return bytes;
        }

        var array = new object[size];
        for (int i = 0; i < size; i++)
            array[i] = ReadFieldValue(elementType);

        _handles[^1] = array;
        return array;
    }

    private string ReadTcString(bool longForm)
    {
        string s = longForm ? ReadUtfLong(_reader) : ReadUtf(_reader);
        AssignHandle(s);
        return s;
    }

    private object ReadTcReference() => ReadTcReferenceRaw();

    private object ReadTcReferenceRaw()
    {
        int handle = ReadS32(_reader) - BaseWireHandle;
        return _handles[handle];
    }

    private byte[] ReadBlockData(bool longForm)
    {
        int size = longForm ? ReadS32(_reader) : _reader.ReadByte();
        return _reader.ReadBytes(size);
    }

    private byte[] CaptureAnnotation()
    {
        using MemoryStream buffer = new();
        while (true)
        {
            byte tag = _reader.ReadByte();

            if (tag == TcEndBlockData)
                return buffer.ToArray();
            buffer.WriteByte(tag);

            // Restlichen Inhalt je nach Tag raw lesen
            CaptureTagContent(tag, buffer);
        }
    }
    
    private void CaptureTagContent(byte tag, Stream buffer)
    {
        switch (tag)
        {
            case TcBlockData:
                byte length0 = ReadAndStoreBytes(1, buffer)[0];
                _ = ReadAndStoreBytes(length0, buffer);
                break;
            case TcBlockDataLong:
                byte[] lenBytes1 = ReadAndStoreBytes(4, buffer);
                int length1 = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lenBytes1));
                _ = ReadAndStoreBytes(length1, buffer);
                break;
            case TcString:
                byte[] lenBytes2 = ReadAndStoreBytes(2, buffer);
                int length2 = IPAddress.NetworkToHostOrder((short)BitConverter.ToUInt16(lenBytes2));
                _ = ReadAndStoreBytes(length2, buffer);
                break;
            case TcNull:
                break; // Nur der Tag, kein weiterer Inhalt
            case TcReference:
                _ = ReadAndStoreBytes(4, buffer);
                break;
            case TcObject:
            case TcArray:
                // Verschachtelte Objekte vollständig in Buffer lesen –
                // Position merken, Content normal parsen, dann Bytes kopieren
                long startPos = _reader.BaseStream.Position;
                _reader.BaseStream.Position--; // Tag zurücklegen
                ReadContent(); // Normal parsen (Handle wird registriert)
                long endPos = _reader.BaseStream.Position;

                // Gelesene Bytes in Buffer kopieren
                long length = endPos - startPos + 1; // +1 für den Tag
                _reader.BaseStream.Position = startPos - 1;
                _ = ReadAndStoreBytes((int)length, buffer);
                break;
            default:
                throw new NotSupportedException($"Unbekannter Annotation-Tag: 0x{tag:X2}");
        }
    }
    
    private byte[] ReadAndStoreBytes(int count, Stream buffer)
    {
        byte[] bytes = _reader.ReadBytes(count);
        buffer.Write(bytes);
        return bytes;
    }
    
    private void SkipAnnotations()
    {
        while (true)
        {
            byte tag = _reader.ReadByte();
            if (tag == TcEndBlockData)
                return;

            // Alles andere als Content lesen und wegwerfen
            _reader.BaseStream.Position--; // Tag zurücklegen
            ReadContent();
        }
    }

    private void AssignHandle(object obj, int index = -1)
    {
        if (index > -1)
            _handles[index] = obj;
        else
            _handles.Add(obj);
    }

    public string ResolveReference(int handle)
    {
        object resolved = _handles[handle - 0x7E0000];
        return resolved as string ?? throw new InvalidDataException($"Handle does not point to string: {resolved.GetType().Name}");
    }
    
    public void Dispose() => _reader.Dispose();
}