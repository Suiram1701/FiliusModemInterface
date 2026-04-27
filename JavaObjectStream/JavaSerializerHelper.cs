using System.Net;
using System.Text;

namespace FiliusModemInterface.JavaObjectStream;

public static class JavaSerializerHelper
{
    public const byte TcNull          = 0x70;
    public const byte TcReference     = 0x71;
    public const byte TcClassDesc     = 0x72;
    public const byte TcObject        = 0x73;
    public const byte TcString        = 0x74;
    public const byte TcArray         = 0x75;
    public const byte TcBlockData     = 0x77;
    public const byte TcEndBlockData  = 0x78;
    public const byte TcBlockDataLong = 0x7A;
    public const byte TcLongString    = 0x7C;
    
    public const int BaseWireHandle = 0x7E0000;
    
    public static string ReadUtf(BinaryReader reader)
    {
        ushort length = ReadU16(reader);
        byte[] bytes  = reader.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }

    public static string ReadUtfLong(BinaryReader reader)
    {
        long   length = ReadS64(reader);
        byte[] bytes  = reader.ReadBytes((int)length);
        return Encoding.UTF8.GetString(bytes);
    }

    public static ushort ReadU16(BinaryReader reader) => (ushort)IPAddress.NetworkToHostOrder((short)reader.ReadUInt16());
    public static short  ReadS16(BinaryReader reader) => IPAddress.NetworkToHostOrder(reader.ReadInt16());
    public static int    ReadS32(BinaryReader reader) => IPAddress.NetworkToHostOrder(reader.ReadInt32());
    public static long   ReadS64(BinaryReader reader) => IPAddress.NetworkToHostOrder(reader.ReadInt64());

    public static float ReadFloat(BinaryReader reader)
    {
        byte[] b = reader.ReadBytes(4);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return BitConverter.ToSingle(b);
    }

    public static double ReadDouble(BinaryReader reader)
    {
        byte[] b = reader.ReadBytes(8);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return BitConverter.ToDouble(b);
    }
    
    public static void WriteUtf(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteU16(writer, (ushort)bytes.Length);
        writer.Write(bytes);
    }

    public static void WriteUtfLong(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteS64(writer, bytes.Length);
        writer.Write(bytes);
    }

    public static void WriteU16(BinaryWriter writer, ushort value) => writer.Write((ushort)IPAddress.HostToNetworkOrder((short)value));

    public static void WriteS16(BinaryWriter writer, short value) => writer.Write(IPAddress.HostToNetworkOrder(value));

    public static void WriteS32(BinaryWriter writer, int value) => writer.Write(IPAddress.HostToNetworkOrder(value));

    public static void WriteS64(BinaryWriter writer, long value) => writer.Write(IPAddress.HostToNetworkOrder(value));

    public static void WriteFloat(BinaryWriter writer, float value)
    {
        byte[] b = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        writer.Write(b);
    }

    public static void WriteDouble(BinaryWriter writer, double value)
    {
        byte[] b = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        writer.Write(b);
    }
}