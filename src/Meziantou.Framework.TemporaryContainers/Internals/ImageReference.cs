namespace Meziantou.Framework.TemporaryContainers.Internals;

internal static class ImageReference
{
    /// <summary>Adds the <c>latest</c> tag to a reference that has neither a tag nor a digest.</summary>
    /// <remarks>The Engine API pulls every tag of a repository when the reference has none, where the docker CLI pulls <c>latest</c>. A registry host with a port (<c>localhost:5000/app</c>) is not a tag: only the last path segment can carry one.</remarks>
    public static string WithDefaultTag(string reference)
    {
        if (reference.Contains('@', StringComparison.Ordinal))
            return reference;

        var lastSegment = reference[(reference.LastIndexOf('/', StringComparison.Ordinal) + 1)..];
        return lastSegment.Contains(':', StringComparison.Ordinal) ? reference : reference + ":latest";
    }
}
