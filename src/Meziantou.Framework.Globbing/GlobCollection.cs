using System.Collections;
using System.ComponentModel;

namespace Meziantou.Framework.Globbing;

[System.Runtime.CompilerServices.CollectionBuilder(typeof(GlobCollection), nameof(Create))]
public sealed class GlobCollection : IReadOnlyList<IGlobEvaluatable>, IGlobEvaluatable
{
    private readonly IGlobEvaluatable[] _globs;

    public GlobCollection(params IGlobEvaluatable[] globs) => _globs = globs;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static GlobCollection Create(ReadOnlySpan<IGlobEvaluatable> globs) => new(globs.ToArray());

    GlobMode IGlobEvaluatable.Mode => (_globs.Length == 0 || _globs.Any(g => g.Mode is GlobMode.Include)) ? GlobMode.Include : GlobMode.Exclude;
    bool IGlobEvaluatable.CanMatchFiles => _globs.Any(g => g.Mode is GlobMode.Include && ((IGlobEvaluatable)g).CanMatchFiles);
    bool IGlobEvaluatable.CanMatchDirectories => _globs.Any(g => g.Mode is GlobMode.Include && ((IGlobEvaluatable)g).CanMatchDirectories);
    bool IGlobEvaluatable.IsMultiLevel => _globs.Any(g => g.Mode is GlobMode.Include && ((IGlobEvaluatable)g).IsMultiLevel);

    public int Count => _globs.Length;
    public IGlobEvaluatable this[int index] => _globs[index];

    public bool IsMatch(ReadOnlySpan<char> directory, ReadOnlySpan<char> filename, PathItemType? itemType)
    {
        var match = false;
        foreach (var glob in _globs)
        {
            if (match && glob.Mode is GlobMode.Include)
                continue;

            if (glob.IsMatch(directory, filename, itemType))
            {
                if (glob.Mode is GlobMode.Exclude)
                    return false;

                match = true;
            }
        }

        return match;
    }

    public bool IsPartialMatch(ReadOnlySpan<char> folderPath, ReadOnlySpan<char> filename)
    {
        foreach (var glob in _globs)
        {
            if (glob.Mode is GlobMode.Exclude)
                continue;

            if (glob.IsPartialMatch(folderPath, filename))
                return true;
        }

        return false;
    }

    public IEnumerator<IGlobEvaluatable> GetEnumerator() => ((IEnumerable<IGlobEvaluatable>)_globs).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _globs.GetEnumerator();
}
