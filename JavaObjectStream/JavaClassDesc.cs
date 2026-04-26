namespace FiliusModemInterface.JavaObjectStream;

public sealed class JavaClassDesc(string className, long serialVersionUid, byte classFlags, List<JavaFieldDesc> fields, JavaClassDesc superClass)
{
    public string ClassName { get; } = className;
    
    public byte ClassDescFlags { get; } = classFlags;
    
    public bool HasWriteMethod   => (ClassDescFlags & 0x01) != 0;
    
    public bool IsExternalizable => (ClassDescFlags & 0x04) != 0;
    
    public long SerialVersionUid { get; } = serialVersionUid;
    
    public List<JavaFieldDesc> Fields { get; } = fields;
    
    public JavaClassDesc? SuperClass { get; } = superClass;
}

public sealed record JavaFieldDesc(string Name, char TypeCode);