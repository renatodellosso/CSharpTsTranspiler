namespace TsTranspiler;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = true)]
public class Transpile : Attribute
{ }