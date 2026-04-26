using System.Net;
using System.Text;

namespace FiliusModemInterface.JavaObjectStream;

/// <summary>
/// A stream reader which is beable to parse a binary java object stream. The reader doesn't care about the stream header. The first 4 bytes have to be read
/// </summary>
/// <remarks>The main part of this class and used dtos were written by Claude.ai</remarks>
/// <param name="stream">The stream to read from.</param>
public sealed class JavaObjectReader(Stream stream) : IDisposable
{
    private const byte TcNull          = 0x70;
    private const byte TcReference     = 0x71;
    private const byte TcClassDesc     = 0x72;
    private const byte TcObject        = 0x73;
    private const byte TcString        = 0x74;
    private const byte TcArray         = 0x75;
    private const byte TcEndBlockData  = 0x78;
    private const byte TcBlockData     = 0x77;
    private const byte TcBlockDataLong = 0x7A;
    private const byte TcLongString    = 0x7C;

    private readonly BinaryReader _reader = new(stream, Encoding.UTF8, leaveOpen: true);

    private List<object> _handles = new();
    private const int BaseWireHandle = 0x7E0000;
    
    public JavaObject ReadObject()
    {
        int oldHandlesCount = _handles.Count;
        try
        {
            return (JavaObject)ReadContent()!;
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

    private JavaObject ReadTcObject()
    {
        JavaClassDesc classDesc = ReadClassDesc();
        JavaObject obj = new(classDesc.ClassName);
        AssignHandle(obj);

        ReadClassData(obj, classDesc);
        return obj;
    }

    private void ReadClassData(JavaObject obj, JavaClassDesc classDesc)
    {
        if (classDesc.SuperClass != null)
            ReadClassData(obj, classDesc.SuperClass);

        if (classDesc.IsExternalizable)
        {
            SkipAnnotations();
            return;
        }

        foreach (JavaFieldDesc field in classDesc.Fields)
        {
            object value = ReadFieldValue(field.TypeCode);
            obj.Fields[field.Name] = value;
        }

        if (classDesc.HasWriteMethod)
            SkipAnnotations();
    }

    private object ReadFieldValue(char typeCode)
    {
        return typeCode switch
        {
            'B' => _reader.ReadSByte(),
            'C' => (char)ReadU16(),
            'D' => ReadDouble(),
            'F' => ReadFloat(),
            'I' => ReadS32(),
            'J' => ReadS64(),
            'S' => ReadS16(),
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
        
        string className       = ReadUtf();
        long   serialVersionUid = ReadS64();
        byte   classDescFlags  = _reader.ReadByte();
        ushort fieldCount      = ReadU16();

        List<JavaFieldDesc> fields = new(fieldCount);
        for (int i = 0; i < fieldCount; i++)
        {
            var   typeCode  = (char)_reader.ReadByte();
            string fieldName = ReadUtf();

            // Für Objekt/Array-Typen folgt der Klassenname als String-Content
            if (typeCode == 'L' || typeCode == '[')
                ReadContent(); // Klassenname – für unsere Zwecke ignorierbar

            fields.Add(new JavaFieldDesc(fieldName, typeCode));
        }

        // TC_ENDBLOCKDATA überspringen (optionale Annotations ignorieren)
        SkipAnnotations();

        JavaClassDesc superClass = ReadClassDesc();

        JavaClassDesc desc = new(className, serialVersionUid, classDescFlags, fields, superClass);
        AssignHandle(desc, index);
        return desc;
    }

    private object ReadTcArray()
    {
        JavaClassDesc classDesc = ReadClassDesc();
        AssignHandle(null!);

        int size = ReadS32();

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
        string s = longForm ? ReadUtfLong() : ReadUtf();
        AssignHandle(s);
        return s;
    }

    private object ReadTcReference() => ReadTcReferenceRaw();

    private object ReadTcReferenceRaw()
    {
        int handle = ReadS32() - BaseWireHandle;
        return _handles[handle];
    }

    private byte[] ReadBlockData(bool longForm)
    {
        int size = longForm ? ReadS32() : _reader.ReadByte();
        return _reader.ReadBytes(size);
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

    private string ReadUtf()
    {
        ushort length = ReadU16();
        byte[] bytes  = _reader.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }

    private string ReadUtfLong()
    {
        long   length = ReadS64();
        byte[] bytes  = _reader.ReadBytes((int)length);
        return Encoding.UTF8.GetString(bytes);
    }

    private ushort ReadU16() => (ushort)IPAddress.NetworkToHostOrder((short)_reader.ReadUInt16());
    private short  ReadS16() => IPAddress.NetworkToHostOrder(_reader.ReadInt16());
    private int    ReadS32() => IPAddress.NetworkToHostOrder(_reader.ReadInt32());
    private long   ReadS64() => IPAddress.NetworkToHostOrder(_reader.ReadInt64());

    private float ReadFloat()
    {
        byte[] b = _reader.ReadBytes(4);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return BitConverter.ToSingle(b);
    }

    private double ReadDouble()
    {
        byte[] b = _reader.ReadBytes(8);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return BitConverter.ToDouble(b);
    }

    public void Dispose() => _reader.Dispose();
}