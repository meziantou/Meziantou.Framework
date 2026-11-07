using Meziantou.Framework.Annotations;

#region FastEnumGenerator_Declaration
[assembly: FastEnumAttribute(typeof(Sample.Color), IsPublic = true, ExtensionMethodNamespace = "Sample.Extensions")]

namespace Sample;

public enum Color
{
    Blue,
    Red,
    Green,
}
#endregion
