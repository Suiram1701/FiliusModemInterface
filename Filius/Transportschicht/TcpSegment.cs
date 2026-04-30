using System.Diagnostics;
using FiliusModemInterface.JavaObjectStream.Attributes;

namespace FiliusModemInterface.Filius.Transportschicht;

[DebuggerDisplay("{ToString(),nq}")]
[JavaClass("filius.software.transportschicht.TcpSegment", SerialVersionUid = 1L)]
public class TcpSegment : Segment
{
    [JavaField("seqNummer")] public long SeqNumber { get; set; }
    
    [JavaField("ackNummer")] public long AckNumber { get; set; }
    
    [JavaField("dataOffset")] public int DataOffset { get; set; }

    [JavaField("reservedField")] public int ReservedField { get; set; } = 0;
    
    [JavaField("urg")] public bool Urg { get; set; }
    
    [JavaField("ack")] public bool Ack { get; set; }
    
    [JavaField("psh")] public bool Psh { get; set; }
    
    [JavaField("rst")] public bool Rst { get; set; }
    
    [JavaField("syn")] public bool Syn { get; set; }
    
    [JavaField("fin")] public bool Fin { get; set; }
    
    [JavaField("window")] public int Window { get; set; }
    
    [JavaField("urgentPointer")] public int UrgentPointer { get; set; }
    
    public override string ToString() => $"[src={SourcePort}, dest={DestinationPort}, seq={SeqNumber}, ack={AckNumber}, offset={DataOffset}, reserved={ReservedField}, " +
                                         $"urg={Urg}, ack={Ack}, psh={Psh}, rst={Rst}, syn={Syn}, fin={Fin}, window={Window}, checksum={Checksum}, urgent={UrgentPointer}, data={Data}]";
}