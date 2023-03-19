namespace Meziantou.Framework.HumanReadableSerializer.FSharp.Tests

type public Shape =
    | Rectangle of width : float * length : float
    | Circle of radius : float
    | Prism of width : float * float * height : float
