namespace FiliusModemInterface.JavaObjectStream.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class JavaClassAttribute(string className) : Attribute
{
    public string ClassName { get; } = className;

    public JavaClassFlags ClassFlags { get; init; } = JavaClassFlags.Serializable;
    
    public long SerialVersionUid { get; init; }
}