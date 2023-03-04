namespace Meziantou.Framework.FSharpTests

type public Say() =
    member this.int32 =
        async {
            let! _ = System.Threading.Tasks.Task.Delay(15) |> Async.AwaitTask
            return 42
        }

    member this.dummyUnit =
        async {
            let! _ = System.Threading.Tasks.Task.Delay(15) |> Async.AwaitTask
            ()
        }
