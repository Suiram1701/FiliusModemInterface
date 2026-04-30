using System.Text;
using FiliusModemInterface.JavaObjectStream;
using FiliusModemInterface.JavaObjectStream.Attributes;
using static FiliusModemInterface.JavaObjectStream.JavaSerializerHelper;

namespace FiliusModemInterface.Filius.Utils;

[JavaClass("java.util.HashSet", ClassFlags = JavaClassFlags.Serializable | JavaClassFlags.WriteMethod, SerialVersionUid = -5024744406713321676L)]
public class HashSet
{
    public HashSet<string> Set { get; } = [];
    
    public void WriteObject(BinaryWriter writer, JavaObjectWriter objWriter)
    {
        // TC_BLOCKDATA mit capacity + loadFactor + size
        writer.Write((byte)0x77); // TC_BLOCKDATA
        writer.Write((byte)12);   // 4 + 4 + 4 Bytes

        int   capacity = Set.Capacity;     // Math.Max(Set.Count * 2, 16);
        var   loadFactor = 0.75f;
        int   size       = Set.Count;

        WriteS32(writer, capacity);
        WriteFloat(writer, loadFactor);
        WriteS32(writer, size);

        foreach (string element in Set)
        {
            if (objWriter.TryGetHandle(element, out int handle))
            {
                writer.Write(TcReference);
                WriteS32(writer, handle + BaseWireHandle);
            }
            else
            {
                byte[] encoded = Encoding.UTF8.GetBytes(element);
                if (encoded.Length <= 0xFFFF)
                {
                    writer.Write(TcString);
                    WriteU16(writer, (ushort)encoded.Length);
                }
                else
                {
                    writer.Write(TcLongString);
                    WriteS64(writer, encoded.Length);
                }
            
                writer.Write(encoded);
                objWriter.AssignHandle(element);
            }
        }
    }

    public void ReadObject(BinaryReader reader, JavaObjectReader objReader)
    {
        // TC_BLOCKDATA with capacity + loadFactor + size
        byte blockTag = reader.ReadByte();
        if (blockTag != TcBlockData)
            throw new InvalidDataException($"Expected TC_BLOCKDATA for HashSet metadata, got 0x{blockTag:X2}");

        byte blockLen = reader.ReadByte();
        if (blockLen < 12)
            throw new InvalidDataException($"HashSet metadata block too short: {blockLen} bytes");

        int   capacity   = ReadS32(reader);
        float loadFactor = ReadFloat(reader);
        int   size       = ReadS32(reader);

        // Skip remaining metadata byte (when blockLen > 12)
        int remaining = blockLen - 12;
        if (remaining > 0)
            reader.ReadBytes(remaining);

        Set.Clear();
        for (var i = 0; i < size; i++)
        {
            byte tag = reader.ReadByte();

            string element;
            switch (tag)
            {
                case TcString:
                    element = ReadUtf(reader);
                    objReader.AssignHandle(element);
                    break;
                case TcLongString:
                    element = ReadUtfLong(reader);
                    objReader.AssignHandle(element);
                    break;
                case TcReference:
                    element = objReader.ResolveReference(ReadS32(reader));
                    break;
                default:
                    throw new NotSupportedException($"Unknown HashSet element tag: 0x{tag:X2}");
            }

            Set.Add(element);
        }
    }
}