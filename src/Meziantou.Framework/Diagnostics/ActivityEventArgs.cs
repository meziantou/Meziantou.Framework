using System.Diagnostics;

namespace Meziantou.Framework.Diagnostics;

/// <summary>Provides data for the <see cref="ScopedActivityListener.ActivityStarted"/> and <see cref="ScopedActivityListener.ActivityStopped"/> events.</summary>
public sealed class ActivityEventArgs : EventArgs
{
    internal ActivityEventArgs(Activity activity) => Activity = activity;

    /// <summary>Gets the activity that started or stopped.</summary>
    public Activity Activity { get; }
}
