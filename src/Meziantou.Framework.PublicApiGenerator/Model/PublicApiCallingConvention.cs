namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>The calling convention of a function pointer type.</summary>
public enum PublicApiCallingConvention
{
    Managed,
    Unmanaged,
    CDecl,
    StdCall,
    ThisCall,
    FastCall,
    VarArgs,
}
