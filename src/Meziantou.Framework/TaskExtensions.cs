namespace Meziantou.Framework;

public static partial class TaskExtensions
{
    // https://www.meziantou.net/fire-and-forget-a-task-in-dotnet.htm
    public static void Forget(this Task task)
    {
        // Only care about tasks that may fault or are faulted,
        // so fast-path for SuccessfullyCompleted and Canceled tasks
        if (!task.IsCompleted || task.IsFaulted)
        {
            _ = ForgetAwaited(task);
        }

        async static Task ForgetAwaited(Task task)
        {
            try
            {
                // No need to resume on the original SynchronizationContext
                await task.ConfigureAwait(false);
            }
            catch
            {
                // Nothing to do here
            }
        }
    }
}
