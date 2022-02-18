using System.Collections.Concurrent;

namespace Meziantou.Framework.Threading
{
    public sealed class MonoThreadedTaskScheduler : TaskScheduler, IDisposable
    {
        private readonly ConcurrentQueue<Task> _tasks = new();
        private readonly AutoResetEvent _stop = new(initialState: false);
        private readonly AutoResetEvent _dequeue = new(initialState: false);

        public MonoThreadedTaskScheduler()
            : this(threadName: null)
        {
        }

        public MonoThreadedTaskScheduler(string? threadName)
        {
            Thread = new Thread(SafeThreadExecute)
            {
                IsBackground = true,
                Name = threadName,
            };

            Thread.Start();

            DisposeThreadJoinTimeout = TimeSpan.FromMilliseconds(1000);
            WaitTimeout = TimeSpan.FromMilliseconds(100);
        }

        private Thread? Thread { get; set; }
        public bool DequeueOnDispose { get; set; }
        public TimeSpan DisposeThreadJoinTimeout { get; set; }
        public TimeSpan WaitTimeout { get; set; }
        public TimeSpan DequeueTimeout { get; set; }

        public int QueueCount => _tasks.Count;

        public void Dispose()
        {
            if (_stop != null)
            {
                _stop.Set();
                _stop.Dispose();
            }

            if (_dequeue != null)
            {
                _dequeue.Dispose();
            }

            if (DequeueOnDispose)
            {
                Dequeue();
            }

            if (Thread != null && Thread.IsAlive)
            {
                Thread.Join(DisposeThreadJoinTimeout);
            }

            Thread = null;
        }

        private bool ExecuteTask(Task task)
        {
            return TryExecuteTask(task);
        }

        private void Dequeue()
        {
            do
            {
                if (!_tasks.TryDequeue(out var task))
                    break;

                ExecuteTask(task);
            }
            while (true);
        }

        private void SafeThreadExecute()
        {
            try
            {
                ThreadExecute();
            }
            catch
            {
            }
        }

        private void ThreadExecute()
        {
            do
            {
                // note: Stop must be first in array (in case both events happen at the same exact time)
                var i = WaitHandle.WaitAny(new[] { _stop, _dequeue }, WaitTimeout);
                if (i == 0)
                    break;

                // note: we can dequeue on _dequeue event, or on timeout
                Dequeue();
            }
            while (true);
        }

        protected override IEnumerable<Task> GetScheduledTasks() => _tasks;

        protected override void QueueTask(Task task!!)
        {
            _tasks.Enqueue(task);
            _dequeue.Set();
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            return false;
        }
    }
}
