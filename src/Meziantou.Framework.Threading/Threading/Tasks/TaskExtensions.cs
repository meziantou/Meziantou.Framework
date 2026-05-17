namespace Meziantou.Framework.Threading.Tasks;

/// <summary>Provides extension methods for <see cref="Task"/> and <see cref="ValueTask"/>.</summary>
public static partial class TaskExtensions
{
    /// <summary>Allows a task to continue executing without waiting for it to complete, while still observing any exceptions.</summary>
    /// <param name="task">The task to forget.</param>
    /// <remarks>
    /// This method ensures that exceptions in the task are observed to prevent unobserved task exceptions.
    /// See <see href="https://www.meziantou.net/fire-and-forget-a-task-in-dotnet.htm" /> for more information.
    /// </remarks>
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
            await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
