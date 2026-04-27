namespace FiliusModemInterface.JavaObjectStream;

public sealed class JavaClassDesc(string className, long serialVersionUid, JavaClassFlags classFlags, List<JavaFieldDesc> fields, JavaClassDesc? superClass)
{
    public string ClassName { get; } = className;
    
    public JavaClassFlags ClassDescFlags { get; } = classFlags;
    
    public long SerialVersionUid { get; } = serialVersionUid;
    
    public List<JavaFieldDesc> Fields { get; } = fields;
    
    public JavaClassDesc? SuperClass { get; } = superClass;
}

public sealed record JavaFieldDesc(string Name, char TypeCode, string? ClassName = null);

[Flags]
public enum JavaClassFlags
{
    WriteMethod = 0x01,
    Serializable = 0x02,
    Externalizable = 0x04,
    BlockData = 0x08,
    Enum = 0x10
}