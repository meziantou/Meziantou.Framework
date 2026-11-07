namespace Meziantou.Framework.Unix.ControlGroups;

/// <summary>Describes what a cgroup interface file holds, so an absent value can be told apart from an unlimited one.</summary>
public enum CGroupValueState
{
    /// <summary>The interface file does not exist: the controller is not enabled on the parent cgroup, or the running kernel does not support the feature.</summary>
    Unavailable = 0,

    /// <summary>The interface file exists and holds no limit (<c>max</c>).</summary>
    NotConfigured = 1,

    /// <summary>The interface file exists and holds a value.</summary>
    Configured = 2,

    /// <summary>The interface file exists, but its content was not understood. This is either a bug in this library or a kernel format change.</summary>
    Invalid = 3,
}
