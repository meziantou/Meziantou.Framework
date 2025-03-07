


//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------
#nullable enable
using System;
using System.Threading.Tasks;

namespace Meziantou.Framework
{
    public static class ThrottleExtensions
    {

        public static Action Throttle(this Action action, TimeSpan interval)
        {
            return Throttle(action, interval, TimeProvider.System);
        }

        public static Action Throttle(this Action action, TimeSpan interval, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(timeProvider);

            Task? task = null;
            var l = new object();

            return () =>
            {

                if (task is not null)
                    return;

                lock (l)
                {
                    if (task is not null)
                        return;

                    task = Task.Delay(interval, timeProvider).ContinueWith(t =>
                    {
                        action();
                        t.Dispose();
                        task = null;
                    }, TaskScheduler.Default);
                }
            };
        }

        public static Action<T0> Throttle<T0>(this Action<T0> action, TimeSpan interval)
        {
            return Throttle(action, interval, TimeProvider.System);
        }

        public static Action<T0> Throttle<T0>(this Action<T0> action, TimeSpan interval, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(timeProvider);

            Task? task = null;
            var l = new object();

            ValueTuple<T0> args = default;

            return (T0 arg0) =>
            {

                args = ValueTuple.Create(arg0);

                if (task is not null)
                    return;

                lock (l)
                {
                    if (task is not null)
                        return;

                    task = Task.Delay(interval, timeProvider).ContinueWith(t =>
                    {
                        action(args.Item0);
                        t.Dispose();
                        task = null;
                    }, TaskScheduler.Default);
                }
            };
        }

        public static Action<T0, T1> Throttle<T0, T1>(this Action<T0, T1> action, TimeSpan interval)
        {
            return Throttle(action, interval, TimeProvider.System);
        }

        public static Action<T0, T1> Throttle<T0, T1>(this Action<T0, T1> action, TimeSpan interval, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(timeProvider);

            Task? task = null;
            var l = new object();

            ValueTuple<T0, T1> args = default;

            return (T0 arg0, T1 arg1) =>
            {

                args = ValueTuple.Create(arg0, arg1);

                if (task is not null)
                    return;

                lock (l)
                {
                    if (task is not null)
                        return;

                    task = Task.Delay(interval, timeProvider).ContinueWith(t =>
                    {
                        action(args.Item0, args.Item1);
                        t.Dispose();
                        task = null;
                    }, TaskScheduler.Default);
                }
            };
        }

        public static Action<T0, T1, T2> Throttle<T0, T1, T2>(this Action<T0, T1, T2> action, TimeSpan interval)
        {
            return Throttle(action, interval, TimeProvider.System);
        }

        public static Action<T0, T1, T2> Throttle<T0, T1, T2>(this Action<T0, T1, T2> action, TimeSpan interval, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(timeProvider);

            Task? task = null;
            var l = new object();

            ValueTuple<T0, T1, T2> args = default;

            return (T0 arg0, T1 arg1, T2 arg2) =>
            {

                args = ValueTuple.Create(arg0, arg1, arg2);

                if (task is not null)
                    return;

                lock (l)
                {
                    if (task is not null)
                        return;

                    task = Task.Delay(interval, timeProvider).ContinueWith(t =>
                    {
                        action(args.Item0, args.Item1, args.Item2);
                        t.Dispose();
                        task = null;
                    }, TaskScheduler.Default);
                }
            };
        }

        public static Action<T0, T1, T2, T3> Throttle<T0, T1, T2, T3>(this Action<T0, T1, T2, T3> action, TimeSpan interval)
        {
            return Throttle(action, interval, TimeProvider.System);
        }

        public static Action<T0, T1, T2, T3> Throttle<T0, T1, T2, T3>(this Action<T0, T1, T2, T3> action, TimeSpan interval, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(timeProvider);

            Task? task = null;
            var l = new object();

            ValueTuple<T0, T1, T2, T3> args = default;

            return (T0 arg0, T1 arg1, T2 arg2, T3 arg3) =>
            {

                args = ValueTuple.Create(arg0, arg1, arg2, arg3);

                if (task is not null)
                    return;

                lock (l)
                {
                    if (task is not null)
                        return;

                    task = Task.Delay(interval, timeProvider).ContinueWith(t =>
                    {
                        action(args.Item0, args.Item1, args.Item2, args.Item3);
                        t.Dispose();
                        task = null;
                    }, TaskScheduler.Default);
                }
            };
        }

        public static Action<T0, T1, T2, T3, T4> Throttle<T0, T1, T2, T3, T4>(this Action<T0, T1, T2, T3, T4> action, TimeSpan interval)
        {
            return Throttle(action, interval, TimeProvider.System);
        }

        public static Action<T0, T1, T2, T3, T4> Throttle<T0, T1, T2, T3, T4>(this Action<T0, T1, T2, T3, T4> action, TimeSpan interval, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(timeProvider);

            Task? task = null;
            var l = new object();

            ValueTuple<T0, T1, T2, T3, T4> args = default;

            return (T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4) =>
            {

                args = ValueTuple.Create(arg0, arg1, arg2, arg3, arg4);

                if (task is not null)
                    return;

                lock (l)
                {
                    if (task is not null)
                        return;

                    task = Task.Delay(interval, timeProvider).ContinueWith(t =>
                    {
                        action(args.Item0, args.Item1, args.Item2, args.Item3, args.Item4);
                        t.Dispose();
                        task = null;
                    }, TaskScheduler.Default);
                }
            };
        }

        public static Action<T0, T1, T2, T3, T4, T5> Throttle<T0, T1, T2, T3, T4, T5>(this Action<T0, T1, T2, T3, T4, T5> action, TimeSpan interval)
        {
            return Throttle(action, interval, TimeProvider.System);
        }

        public static Action<T0, T1, T2, T3, T4, T5> Throttle<T0, T1, T2, T3, T4, T5>(this Action<T0, T1, T2, T3, T4, T5> action, TimeSpan interval, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(timeProvider);

            Task? task = null;
            var l = new object();

            ValueTuple<T0, T1, T2, T3, T4, T5> args = default;

            return (T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5) =>
            {

                args = ValueTuple.Create(arg0, arg1, arg2, arg3, arg4, arg5);

                if (task is not null)
                    return;

                lock (l)
                {
                    if (task is not null)
                        return;

                    task = Task.Delay(interval, timeProvider).ContinueWith(t =>
                    {
                        action(args.Item0, args.Item1, args.Item2, args.Item3, args.Item4, args.Item5);
                        t.Dispose();
                        task = null;
                    }, TaskScheduler.Default);
                }
            };
        }

        public static Action<T0, T1, T2, T3, T4, T5, T6> Throttle<T0, T1, T2, T3, T4, T5, T6>(this Action<T0, T1, T2, T3, T4, T5, T6> action, TimeSpan interval)
        {
            return Throttle(action, interval, TimeProvider.System);
        }

        public static Action<T0, T1, T2, T3, T4, T5, T6> Throttle<T0, T1, T2, T3, T4, T5, T6>(this Action<T0, T1, T2, T3, T4, T5, T6> action, TimeSpan interval, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(timeProvider);

            Task? task = null;
            var l = new object();

            ValueTuple<T0, T1, T2, T3, T4, T5, T6> args = default;

            return (T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6) =>
            {

                args = ValueTuple.Create(arg0, arg1, arg2, arg3, arg4, arg5, arg6);

                if (task is not null)
                    return;

                lock (l)
                {
                    if (task is not null)
                        return;

                    task = Task.Delay(interval, timeProvider).ContinueWith(t =>
                    {
                        action(args.Item0, args.Item1, args.Item2, args.Item3, args.Item4, args.Item5, args.Item6);
                        t.Dispose();
                        task = null;
                    }, TaskScheduler.Default);
                }
            };
        }

        public static Action<T0, T1, T2, T3, T4, T5, T6, T7> Throttle<T0, T1, T2, T3, T4, T5, T6, T7>(this Action<T0, T1, T2, T3, T4, T5, T6, T7> action, TimeSpan interval)
        {
            return Throttle(action, interval, TimeProvider.System);
        }

        public static Action<T0, T1, T2, T3, T4, T5, T6, T7> Throttle<T0, T1, T2, T3, T4, T5, T6, T7>(this Action<T0, T1, T2, T3, T4, T5, T6, T7> action, TimeSpan interval, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(timeProvider);

            Task? task = null;
            var l = new object();

            ValueTuple<T0, T1, T2, T3, T4, T5, T6, T7> args = default;

            return (T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7) =>
            {

                args = ValueTuple.Create(arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7);

                if (task is not null)
                    return;

                lock (l)
                {
                    if (task is not null)
                        return;

                    task = Task.Delay(interval, timeProvider).ContinueWith(t =>
                    {
                        action(args.Item0, args.Item1, args.Item2, args.Item3, args.Item4, args.Item5, args.Item6, args.Item7);
                        t.Dispose();
                        task = null;
                    }, TaskScheduler.Default);
                }
            };
        }

        public static Action<T0, T1, T2, T3, T4, T5, T6, T7, T8> Throttle<T0, T1, T2, T3, T4, T5, T6, T7, T8>(this Action<T0, T1, T2, T3, T4, T5, T6, T7, T8> action, TimeSpan interval)
        {
            return Throttle(action, interval, TimeProvider.System);
        }

        public static Action<T0, T1, T2, T3, T4, T5, T6, T7, T8> Throttle<T0, T1, T2, T3, T4, T5, T6, T7, T8>(this Action<T0, T1, T2, T3, T4, T5, T6, T7, T8> action, TimeSpan interval, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(timeProvider);

            Task? task = null;
            var l = new object();

            ValueTuple<T0, T1, T2, T3, T4, T5, T6, T7, T8> args = default;

            return (T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8) =>
            {

                args = ValueTuple.Create(arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);

                if (task is not null)
                    return;

                lock (l)
                {
                    if (task is not null)
                        return;

                    task = Task.Delay(interval, timeProvider).ContinueWith(t =>
                    {
                        action(args.Item0, args.Item1, args.Item2, args.Item3, args.Item4, args.Item5, args.Item6, args.Item7, args.Item8);
                        t.Dispose();
                        task = null;
                    }, TaskScheduler.Default);
                }
            };
        }

        public static Action<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9> Throttle<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9>(this Action<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9> action, TimeSpan interval)
        {
            return Throttle(action, interval, TimeProvider.System);
        }

        public static Action<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9> Throttle<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9>(this Action<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9> action, TimeSpan interval, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(timeProvider);

            Task? task = null;
            var l = new object();

            ValueTuple<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9> args = default;

            return (T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9) =>
            {

                args = ValueTuple.Create(arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9);

                if (task is not null)
                    return;

                lock (l)
                {
                    if (task is not null)
                        return;

                    task = Task.Delay(interval, timeProvider).ContinueWith(t =>
                    {
                        action(args.Item0, args.Item1, args.Item2, args.Item3, args.Item4, args.Item5, args.Item6, args.Item7, args.Item8, args.Item9);
                        t.Dispose();
                        task = null;
                    }, TaskScheduler.Default);
                }
            };
        }

        private static class ValueTuple
        {

            public static ValueTuple<T0> Create<T0>(T0 arg0)
            {
                return new ValueTuple<T0>(arg0);
            }

            public static ValueTuple<T0, T1> Create<T0, T1>(T0 arg0, T1 arg1)
            {
                return new ValueTuple<T0, T1>(arg0, arg1);
            }

            public static ValueTuple<T0, T1, T2> Create<T0, T1, T2>(T0 arg0, T1 arg1, T2 arg2)
            {
                return new ValueTuple<T0, T1, T2>(arg0, arg1, arg2);
            }

            public static ValueTuple<T0, T1, T2, T3> Create<T0, T1, T2, T3>(T0 arg0, T1 arg1, T2 arg2, T3 arg3)
            {
                return new ValueTuple<T0, T1, T2, T3>(arg0, arg1, arg2, arg3);
            }

            public static ValueTuple<T0, T1, T2, T3, T4> Create<T0, T1, T2, T3, T4>(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
            {
                return new ValueTuple<T0, T1, T2, T3, T4>(arg0, arg1, arg2, arg3, arg4);
            }

            public static ValueTuple<T0, T1, T2, T3, T4, T5> Create<T0, T1, T2, T3, T4, T5>(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
            {
                return new ValueTuple<T0, T1, T2, T3, T4, T5>(arg0, arg1, arg2, arg3, arg4, arg5);
            }

            public static ValueTuple<T0, T1, T2, T3, T4, T5, T6> Create<T0, T1, T2, T3, T4, T5, T6>(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
            {
                return new ValueTuple<T0, T1, T2, T3, T4, T5, T6>(arg0, arg1, arg2, arg3, arg4, arg5, arg6);
            }

            public static ValueTuple<T0, T1, T2, T3, T4, T5, T6, T7> Create<T0, T1, T2, T3, T4, T5, T6, T7>(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
            {
                return new ValueTuple<T0, T1, T2, T3, T4, T5, T6, T7>(arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7);
            }

            public static ValueTuple<T0, T1, T2, T3, T4, T5, T6, T7, T8> Create<T0, T1, T2, T3, T4, T5, T6, T7, T8>(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
            {
                return new ValueTuple<T0, T1, T2, T3, T4, T5, T6, T7, T8>(arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
            }

            public static ValueTuple<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9> Create<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9>(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
            {
                return new ValueTuple<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9>(arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9);
            }

        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
        private readonly struct ValueTuple<T0>
        {

            public readonly T0 Item0;

            public ValueTuple(T0 arg0)
            {

                Item0 = arg0;

            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
        private readonly struct ValueTuple<T0, T1>
        {

            public readonly T0 Item0;

            public readonly T1 Item1;

            public ValueTuple(T0 arg0, T1 arg1)
            {

                Item0 = arg0;

                Item1 = arg1;

            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
        private readonly struct ValueTuple<T0, T1, T2>
        {

            public readonly T0 Item0;

            public readonly T1 Item1;

            public readonly T2 Item2;

            public ValueTuple(T0 arg0, T1 arg1, T2 arg2)
            {

                Item0 = arg0;

                Item1 = arg1;

                Item2 = arg2;

            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
        private readonly struct ValueTuple<T0, T1, T2, T3>
        {

            public readonly T0 Item0;

            public readonly T1 Item1;

            public readonly T2 Item2;

            public readonly T3 Item3;

            public ValueTuple(T0 arg0, T1 arg1, T2 arg2, T3 arg3)
            {

                Item0 = arg0;

                Item1 = arg1;

                Item2 = arg2;

                Item3 = arg3;

            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
        private readonly struct ValueTuple<T0, T1, T2, T3, T4>
        {

            public readonly T0 Item0;

            public readonly T1 Item1;

            public readonly T2 Item2;

            public readonly T3 Item3;

            public readonly T4 Item4;

            public ValueTuple(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
            {

                Item0 = arg0;

                Item1 = arg1;

                Item2 = arg2;

                Item3 = arg3;

                Item4 = arg4;

            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
        private readonly struct ValueTuple<T0, T1, T2, T3, T4, T5>
        {

            public readonly T0 Item0;

            public readonly T1 Item1;

            public readonly T2 Item2;

            public readonly T3 Item3;

            public readonly T4 Item4;

            public readonly T5 Item5;

            public ValueTuple(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
            {

                Item0 = arg0;

                Item1 = arg1;

                Item2 = arg2;

                Item3 = arg3;

                Item4 = arg4;

                Item5 = arg5;

            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
        private readonly struct ValueTuple<T0, T1, T2, T3, T4, T5, T6>
        {

            public readonly T0 Item0;

            public readonly T1 Item1;

            public readonly T2 Item2;

            public readonly T3 Item3;

            public readonly T4 Item4;

            public readonly T5 Item5;

            public readonly T6 Item6;

            public ValueTuple(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
            {

                Item0 = arg0;

                Item1 = arg1;

                Item2 = arg2;

                Item3 = arg3;

                Item4 = arg4;

                Item5 = arg5;

                Item6 = arg6;

            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
        private readonly struct ValueTuple<T0, T1, T2, T3, T4, T5, T6, T7>
        {

            public readonly T0 Item0;

            public readonly T1 Item1;

            public readonly T2 Item2;

            public readonly T3 Item3;

            public readonly T4 Item4;

            public readonly T5 Item5;

            public readonly T6 Item6;

            public readonly T7 Item7;

            public ValueTuple(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
            {

                Item0 = arg0;

                Item1 = arg1;

                Item2 = arg2;

                Item3 = arg3;

                Item4 = arg4;

                Item5 = arg5;

                Item6 = arg6;

                Item7 = arg7;

            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
        private readonly struct ValueTuple<T0, T1, T2, T3, T4, T5, T6, T7, T8>
        {

            public readonly T0 Item0;

            public readonly T1 Item1;

            public readonly T2 Item2;

            public readonly T3 Item3;

            public readonly T4 Item4;

            public readonly T5 Item5;

            public readonly T6 Item6;

            public readonly T7 Item7;

            public readonly T8 Item8;

            public ValueTuple(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
            {

                Item0 = arg0;

                Item1 = arg1;

                Item2 = arg2;

                Item3 = arg3;

                Item4 = arg4;

                Item5 = arg5;

                Item6 = arg6;

                Item7 = arg7;

                Item8 = arg8;

            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
        private readonly struct ValueTuple<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9>
        {

            public readonly T0 Item0;

            public readonly T1 Item1;

            public readonly T2 Item2;

            public readonly T3 Item3;

            public readonly T4 Item4;

            public readonly T5 Item5;

            public readonly T6 Item6;

            public readonly T7 Item7;

            public readonly T8 Item8;

            public readonly T9 Item9;

            public ValueTuple(T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
            {

                Item0 = arg0;

                Item1 = arg1;

                Item2 = arg2;

                Item3 = arg3;

                Item4 = arg4;

                Item5 = arg5;

                Item6 = arg6;

                Item7 = arg7;

                Item8 = arg8;

                Item9 = arg9;

            }
        }

    }
}
