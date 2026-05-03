using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using static FiliusModemInterface.JavaObjectStream.JavaSerializerHelper;

namespace FiliusModemInterface.Filius.Dns;

public class DnsMessage : IParsable<DnsMessage>
{
    public const int QueryCode = 0;
    public const int Response = 1;

    public const int NoError = 0;
    public const int FormatError = 1;
    public const int ServerFailure = 2;
    public const int NameError = 3;
    public const int NotImplemented = 4;
    public const int Refused = 5;

    public int Id { get; set; } = Random.Shared.Next() * 65536;
    
    public bool IsLocal { get; set; }

    public int QueryResponse { get; set; } = QueryCode;

    public int OpCode { get; set; } = 0;
    
    public bool AuthoritativeAnswer { get; set; }
    
    public bool Truncated { get; set; }

    public bool RecursionDesired { get; set; } = true;
    
    public bool RecursionAvailable { get; set; } = true;

    public int ResponseCode { get; set; } = NoError;

    public List<Query> Queries { get; set; } = [];

    public List<ResourceRecord> AnswerRecords { get; set; } = [];

    public List<ResourceRecord> AuthoratativeRecords { get; set; } = [];

    public List<ResourceRecord> AdditionalRecords { get; set; } = [];

    public void FormatForFilius()
    {
        if (QueryResponse == Response)     // Filius doesn't like the response to contain the original query
            Queries.Clear();

        for (var i = 0; i < AnswerRecords.Count; i++)
        {
            ResourceRecord record = AnswerRecords[i];
            if (record.Type != nameof(ResourceRecord.RecordType.CNAME))     // Filius can't handle CNAME records so it has to be transformed
                continue;
            
            ResourceRecord? ipAnswer = AnswerRecords.FirstOrDefault(r =>
                r.DomainName == record.RData && r.Type is nameof(ResourceRecord.RecordType.A) or nameof(ResourceRecord.RecordType.AAAA));
            if (ipAnswer is null)
                continue;
                
            AnswerRecords.Add(ipAnswer with { DomainName = record.DomainName });
        }
    }
    
    public void Serialize(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        WriteU16(writer, (ushort)Id);

        ushort flags = 0;
        if (QueryResponse == 1) flags |= 0x8000;
        flags |= (ushort)(((byte)OpCode & 0x0F) << 11);
        if (AuthoritativeAnswer) flags |= 0x0400;
        if (Truncated) flags |= 0x0200;
        if (RecursionDesired) flags |= 0x0100;
        if (RecursionAvailable) flags |= 0x0080;
        flags |= 0x0010;     // DNSSEC checking disabled
        flags |= (ushort)((byte)ResponseCode & 0x0F);
        WriteU16(writer, flags);
        
        WriteU16(writer, (ushort)Queries.Count);
        WriteU16(writer, (ushort)AnswerRecords.Count);
        WriteU16(writer, (ushort)AuthoratativeRecords.Count);
        WriteU16(writer, (ushort)AdditionalRecords.Count);

        foreach (Query query in Queries)
            query.Serialize(writer);
        foreach (ResourceRecord record in AnswerRecords)
            record.Serialize(writer);
        foreach (ResourceRecord record in AuthoratativeRecords)
            record.Serialize(writer);
        foreach (ResourceRecord record in AdditionalRecords)
            record.Serialize(writer);
    }

    public static DnsMessage Deserialize(Stream stream)
    {
        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        DnsMessage message = new() { Id = ReadU16(reader) };
        
        ushort flags = ReadU16(reader);
        message.QueryResponse = ((flags & 0x8000) != 0) ? 1 : 0;
        message.OpCode = (flags >> 11) & 0x0F;
        message.AuthoritativeAnswer = (flags & 0x0400) != 0;
        message.Truncated = (flags & 0x0200) != 0;
        message.RecursionDesired = (flags & 0x0100) != 0;
        message.RecursionAvailable = (flags & 0x0080) != 0;
        message.ResponseCode = flags & 0x0F;
        
        int queryCount = ReadU16(reader);
        int answerCount = ReadU16(reader);
        int authoritativeCount = ReadU16(reader);
        int additionalCount = ReadU16(reader);
        
        for (var i = 0; i < queryCount; i++)
            message.Queries.Add(Query.Deserialize(reader));
        for (var i = 0; i < answerCount; i++)
            message.AnswerRecords.Add(ResourceRecord.Deserialize(reader));
        for (var i = 0; i < authoritativeCount; i++)
            message.AuthoratativeRecords.Add(ResourceRecord.Deserialize(reader));
        for (var i = 0; i < additionalCount; i++)
            message.AdditionalRecords.Add(ResourceRecord.Deserialize(reader));
        return message;
    }
    
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out DnsMessage result)
    {
        try
        {
            result = Parse(s ?? "", provider);
            return true;
        }
        catch (Exception)
        {
            result = null;
            return false;
        }
    }
    
    public static DnsMessage Parse(string s, IFormatProvider? provider = null)
    {
        DnsMessage message = new();
        
        string[] lines = s.Split("\n");
        string[] metadata = lines[0].Split(' ');

        int queryCount = 0, answerCount = 0, authoritativeCount = 0, additionalCount = 0;
        foreach (string token in metadata)
        {
            if (token.StartsWith("ID"))
                message.Id = int.Parse(token[3..]);
            else if (token.StartsWith("QR"))
                message.QueryResponse = int.Parse(token[3..]);
            else if (token.StartsWith("RCODE"))
                message.ResponseCode = int.Parse(token[6..]);
            else if (token.StartsWith("QDCOUNT"))
                queryCount = int.Parse(token[8..]);
            else if (token.StartsWith("ANCOUNT"))
                answerCount = int.Parse(token[8..]);
            else if (token.StartsWith("NSCOUNT"))
                authoritativeCount = int.Parse(token[8..]);
            else if (token.StartsWith("ARCOUNT"))
                additionalCount = int.Parse(token[8..]);
        }

        var readLine = 1;
        for (var i = 0; i < queryCount; i++)
            message.Queries.Add(Query.Parse(lines[readLine++]));
        for (var i = 0; i < answerCount; i++)
            message.AnswerRecords.Add(ResourceRecord.Parse(lines[readLine++]));
        for (var i = 0; i < authoritativeCount; i++)
            message.AuthoratativeRecords.Add(ResourceRecord.Parse(lines[readLine++]));
        for (var i = 0; i < additionalCount; i++)
            message.AdditionalRecords.Add(ResourceRecord.Parse(lines[readLine++]));
        return message;
    }

    public override string ToString()
    {
        StringBuilder sb = new();
        sb.Append($"ID={Id} ");
        sb.Append($"QR={QueryResponse} ");
        sb.Append($"RCODE={ResponseCode} ");
        sb.Append($"QDCOUNT={Queries.Count} ");
        sb.Append($"ANCOUNT={AnswerRecords.Count} ");
        sb.Append($"NSCOUNT={AuthoratativeRecords.Count} ");
        sb.Append($"ARCOUNT={AdditionalRecords.Count} ");
        sb.Append('\n');

        foreach (Query query in Queries)
            sb.AppendLine(query.ToString());
        foreach (ResourceRecord record in AnswerRecords)
            sb.AppendLine(record.ToString());
        foreach (ResourceRecord record in AuthoratativeRecords)
            sb.AppendLine(record.ToString());
        foreach (ResourceRecord record in AdditionalRecords)
            sb.AppendLine(record.ToString());
        return sb.ToString();
    }

    private static void WriteDomainName(BinaryWriter writer, string domain)
    {
        if (string.IsNullOrEmpty(domain) || domain == ".")
        {
            writer.Write((byte)0x00);
        }
        else
        {
            foreach (string label in domain.TrimEnd('.').Split('.'))
            {
                byte[] encoded = Encoding.ASCII.GetBytes(label);
                writer.Write((byte)encoded.Length);
                writer.Write(encoded);
            }
        }

        writer.Write((byte)0x00);
    }

    private static string ReadDomainName(BinaryReader reader)
    {
        // Written by Claude
        var    labels     = new StringBuilder();
        int    safetyHops = 128;   // Schutz gegen zirkuläre Pointer
        long?  returnPos  = null;  // gesicherte Position nach erstem Pointer-Sprung
 
        while (true)
        {
            if (safetyHops-- == 0)
                throw new InvalidDataException("Too many DNS pointer");
 
            byte len = reader.ReadByte();
 
            // ── Terminierungs-Null ────────────────────────
            if (len == 0)
                break;
 
            // ── Pointer-Kompression (Bits 7+6 = 11) ──────
            if ((len & 0xC0) == 0xC0)
            {
                byte  low = reader.ReadByte();
                int   ptr = ((len & 0x3F) << 8) | low;
 
                // Nur beim ersten Pointer die Rückkehrposition sichern
                if (returnPos is null)
                    returnPos = reader.BaseStream.Position;
 
                reader.BaseStream.Seek(ptr, SeekOrigin.Begin);
                continue;
            }
 
            // ── Normales Label (len = Länge in Byte) ──────
            if (len > 63)
                throw new InvalidDataException($"Label-Länge {len} überschreitet RFC-Maximum (63).");
 
            if (labels.Length > 0)
                labels.Append('.');
 
            labels.Append(Encoding.ASCII.GetString(reader.ReadBytes(len)));
        }
 
        // Nach einem Pointer-Sprung: Stream zurücksetzen
        if (returnPos.HasValue)
            reader.BaseStream.Seek(returnPos.Value, SeekOrigin.Begin);
 
        return labels.Length > 0 ? labels.ToString() : ".";
    }
    
    public record Query(string QName, string QType, string QClass = "IN")
    {
        public void Serialize(BinaryWriter writer)
        {
            WriteDomainName(writer, QName);
            WriteU16(writer, (ushort)Enum.Parse<ResourceRecord.RecordType>(QType));
            WriteU16(writer, (ushort)Enum.Parse<ResourceRecord.Class>(QClass));
        }

        public static Query Deserialize(BinaryReader reader)
        {
            return new Query(
                QName: ReadDomainName(reader),
                QType: ((ResourceRecord.RecordType)ReadU16(reader)).ToString(),
                QClass: ((ResourceRecord.Class)ReadU16(reader)).ToString());
        }
        
        public static Query Parse(string s)
        {
            string[] parts = s.Split(' ');
            return new Query(parts[0], parts[1], parts[2]);
        }
        
        public override string ToString() => $"{QName} {QType} {QClass}";
    }

    public record ResourceRecord(string DomainName, string Type, string RData, int Ttl = 3600)
    {
        public enum RecordType : ushort
        {
            A = 1,
            NS = 2,
            CNAME = 5,
            SOA = 6,
            PTR = 12,
            MX = 15,
            TXT = 16,
            AAAA = 28,
            ANY = 255
        }
        
        public enum Class : ushort
        {
            IN = 1,
            ANY = 255
        }

        public void Serialize(BinaryWriter writer)
        {
            WriteDomainName(writer, DomainName);
            WriteU16(writer, (ushort)Enum.Parse<RecordType>(Type));
            WriteU16(writer, (ushort)Class.IN);     // Filius expects class IN always for records
            WriteU32(writer, Ttl);
            WriteU16(writer, (ushort)RData.Length);

            byte[] rdata;
            switch (Type)
            {
                case nameof(RecordType.A):
                    rdata = RData.Split('.').Select(byte.Parse).ToArray();
                    break;
                case nameof(RecordType.AAAA):
                    rdata = RData.Split(':').Select(byte.Parse).ToArray();
                    break;
                case nameof(RecordType.CNAME):
                case nameof(RecordType.MX):
                case nameof(RecordType.NS):
                {
                    using MemoryStream ms = new();
                    using BinaryWriter domainWriter = new(ms, Encoding.ASCII, leaveOpen: true);
                    WriteDomainName(domainWriter, RData);
                    rdata = ms.ToArray();
                }
                    break;
                default:
                    rdata = Encoding.ASCII.GetBytes(RData);
                    break;
            }
            
            WriteU16(writer, (ushort)rdata.Length);
            writer.Write(rdata);
        }

        public static ResourceRecord Deserialize(BinaryReader reader)
        {
            string domainName = ReadDomainName(reader);
            var type = (RecordType)ReadU16(reader);
            _ = ReadU16(reader);     // Class isn't stored here
            int ttl = ReadU32(reader);
            
            int length = ReadU16(reader);
            byte[] rdata = reader.ReadBytes(length);
            string data = type switch
            {
                RecordType.A => string.Join('.', rdata.Select(@byte => @byte.ToString())),
                RecordType.AAAA => string.Join(':', rdata.Select(@byte => @byte.ToString())),
                RecordType.CNAME or RecordType.MX or RecordType.NS => ReadDomainName(
                    new BinaryReader(new MemoryStream(rdata), Encoding.ASCII, leaveOpen: false)),
                _ => Encoding.ASCII.GetString(rdata)
            };
            return new ResourceRecord(domainName, type.ToString(), data, ttl);
        }
        
        public static ResourceRecord Parse(string s)
        {
            string[] parts = s.Split(' ');
            string domainName = Regex.IsMatch(parts[0], ".*\\.$") ? parts[0] : $"{parts[0]}.";
            return new ResourceRecord(domainName, parts[1], parts[3], int.Parse(parts[2]));
        }
        
        public override string ToString() => $"{DomainName} {Type} {Ttl} {RData}";
    }
}