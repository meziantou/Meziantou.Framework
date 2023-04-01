namespace Meziantou.Framework.HumanReadableSerializer.FSharp.Tests

type public Shape =
    | Rectangle of width : float * length : float
    | Circle of radius : float
    | Prism of width : float * float * height : float

module public Factory =
    let create_tuple = (1,2,3)
    let create_array = [|1;2;3|]
    let create_list = [1;2;3]
    let create_seq = seq { for i in 1 .. 3 -> i }
    let create_map = Map [ (1, "a"); (2, "b") ]
    let create_map_string = Map [ ("a", 1); ("b", 2) ]
    let create_set = Set(seq { 1 .. 3 })
    let create_option_none = None
    let create_option_some = Some(1)
    let create_valueoption_none = ValueNone
    let create_valueoption_some = ValueSome(1)