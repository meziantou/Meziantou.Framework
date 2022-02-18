#pragma warning disable MA0042 // Do not use blocking call

using System.Runtime.CompilerServices;

namespace Meziantou.Framework
{
    // https://www.meziantou.net/get-the-result-of-multiple-tasks-in-a-valuetuple-and-whenall.htm
    public static partial class TaskEx
    {
        public static async Task<(T0, T1)> WhenAll<T0, T1>(Task<T0> task0, Task<T1> task1)
        {
            if (task0 is null)
                throw new ArgumentNullException(nameof(task0));
            if (task1 is null)
                throw new ArgumentNullException(nameof(task1));

            await Task.WhenAll(task0, task1).ConfigureAwait(false);
            return (task0.Result, task1.Result);
        }

        public static async Task<(T0, T1, T2)> WhenAll<T0, T1, T2>(Task<T0> task0, Task<T1> task1, Task<T2> task2)
        {
            if (task0 is null)
                throw new ArgumentNullException(nameof(task0));
            if (task1 is null)
                throw new ArgumentNullException(nameof(task1));
            if (task2 is null)
                throw new ArgumentNullException(nameof(task2));

            await Task.WhenAll(task0, task1, task2).ConfigureAwait(false);
            return (task0.Result, task1.Result, task2.Result);
        }

        public static async Task<(T0, T1, T2, T3)> WhenAll<T0, T1, T2, T3>(Task<T0> task0, Task<T1> task1, Task<T2> task2, Task<T3> task3)
        {
            if (task0 is null)
                throw new ArgumentNullException(nameof(task0));
            if (task1 is null)
                throw new ArgumentNullException(nameof(task1));
            if (task2 is null)
                throw new ArgumentNullException(nameof(task2));
            if (task3 is null)
                throw new ArgumentNullException(nameof(task3));

            await Task.WhenAll(task0, task1, task2, task3).ConfigureAwait(false);
            return (task0.Result, task1.Result, task2.Result, task3.Result);
        }

        public static async Task<(T0, T1, T2, T3, T4)> WhenAll<T0, T1, T2, T3, T4>(Task<T0> task0, Task<T1> task1, Task<T2> task2, Task<T3> task3, Task<T4> task4)
        {
            if (task0 is null)
                throw new ArgumentNullException(nameof(task0));
            if (task1 is null)
                throw new ArgumentNullException(nameof(task1));
            if (task2 is null)
                throw new ArgumentNullException(nameof(task2));
            if (task3 is null)
                throw new ArgumentNullException(nameof(task3));
            if (task4 is null)
                throw new ArgumentNullException(nameof(task4));

            await Task.WhenAll(task0, task1, task2, task3, task4).ConfigureAwait(false);
            return (task0.Result, task1.Result, task2.Result, task3.Result, task4.Result);
        }

        public static async Task<(T0, T1, T2, T3, T4, T5)> WhenAll<T0, T1, T2, T3, T4, T5>(Task<T0> task0, Task<T1> task1, Task<T2> task2, Task<T3> task3, Task<T4> task4, Task<T5> task5)
        {
            if (task0 is null)
                throw new ArgumentNullException(nameof(task0));
            if (task1 is null)
                throw new ArgumentNullException(nameof(task1));
            if (task2 is null)
                throw new ArgumentNullException(nameof(task2));
            if (task3 is null)
                throw new ArgumentNullException(nameof(task3));
            if (task4 is null)
                throw new ArgumentNullException(nameof(task4));
            if (task5 is null)
                throw new ArgumentNullException(nameof(task5));

            await Task.WhenAll(task0, task1, task2, task3, task4, task5).ConfigureAwait(false);
            return (task0.Result, task1.Result, task2.Result, task3.Result, task4.Result, task5.Result);
        }

        public static async Task<(T0, T1, T2, T3, T4, T5, T6)> WhenAll<T0, T1, T2, T3, T4, T5, T6>(Task<T0> task0, Task<T1> task1, Task<T2> task2, Task<T3> task3, Task<T4> task4, Task<T5> task5, Task<T6> task6)
        {
            if (task0 is null)
                throw new ArgumentNullException(nameof(task0));
            if (task1 is null)
                throw new ArgumentNullException(nameof(task1));
            if (task2 is null)
                throw new ArgumentNullException(nameof(task2));
            if (task3 is null)
                throw new ArgumentNullException(nameof(task3));
            if (task4 is null)
                throw new ArgumentNullException(nameof(task4));
            if (task5 is null)
                throw new ArgumentNullException(nameof(task5));
            if (task6 is null)
                throw new ArgumentNullException(nameof(task6));

            await Task.WhenAll(task0, task1, task2, task3, task4, task5, task6).ConfigureAwait(false);
            return (task0.Result, task1.Result, task2.Result, task3.Result, task4.Result, task5.Result, task6.Result);
        }

        public static TaskAwaiter<(T1, T2)> GetAwaiter<T1, T2>(this ValueTuple<Task<T1>, Task<T2>> tasks)
        {
            return WhenAll(tasks.Item1, tasks.Item2).GetAwaiter();
        }

        public static TaskAwaiter<(T1, T2, T3)> GetAwaiter<T1, T2, T3>(this ValueTuple<Task<T1>, Task<T2>, Task<T3>> tasks)
        {
            return WhenAll(tasks.Item1, tasks.Item2, tasks.Item3).GetAwaiter();
        }

        public static TaskAwaiter<(T1, T2, T3, T4)> GetAwaiter<T1, T2, T3, T4>(this ValueTuple<Task<T1>, Task<T2>, Task<T3>, Task<T4>> tasks)
        {
            return WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4).GetAwaiter();
        }

        public static TaskAwaiter<(T1, T2, T3, T4, T5)> GetAwaiter<T1, T2, T3, T4, T5>(this ValueTuple<Task<T1>, Task<T2>, Task<T3>, Task<T4>, Task<T5>> tasks)
        {
            return WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5).GetAwaiter();
        }

        public static TaskAwaiter<(T1, T2, T3, T4, T5, T6)> GetAwaiter<T1, T2, T3, T4, T5, T6>(this ValueTuple<Task<T1>, Task<T2>, Task<T3>, Task<T4>, Task<T5>, Task<T6>> tasks)
        {
            return WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6).GetAwaiter();
        }

        public static TaskAwaiter<(T1, T2, T3, T4, T5, T6, T7)> GetAwaiter<T1, T2, T3, T4, T5, T6, T7>(this ValueTuple<Task<T1>, Task<T2>, Task<T3>, Task<T4>, Task<T5>, Task<T6>, Task<T7>> tasks)
        {
            return WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6, tasks.Item7).GetAwaiter();
        }
    }
}
