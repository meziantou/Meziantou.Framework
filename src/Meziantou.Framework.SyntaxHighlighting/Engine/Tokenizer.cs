using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Meziantou.Framework.SyntaxHighlighting.Languages;

namespace Meziantou.Framework.SyntaxHighlighting.Engine;

internal static class Tokenizer
{
    private enum HitKind { Begin, End, Illegal }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Hit(int Index, int Length, HitKind Kind, CompiledMode Mode, int EndOwnerDepth);

    /// <summary>
    /// The leftmost match of one regex at an index &gt;= <see cref="From"/>, memoized for one run.
    /// <see cref="Index"/> is -1 when there is no match; <see cref="From"/> is -1 when nothing is cached.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ScanEntry(int From, int Index, int Length)
    {
        public static ScanEntry Empty { get; } = new(-1, -1, 0);

        /// <remarks>
        /// A match found at index <c>i</c> while scanning from <c>f</c> is still the leftmost match for any
        /// start in <c>[f, i]</c> — there is nothing in between, or it would have been found first — and a
        /// pattern that did not match from <c>f</c> cannot match from any later start either. The cursor
        /// can move backwards (ReturnBegin/ReturnEnd), so a start before <c>f</c> needs a real scan.
        /// </remarks>
        public bool TryGet(int from, out int index, out int length)
        {
            if (From >= 0 && from >= From && (Index < 0 || Index >= from))
            {
                index = Index;
                length = Length;
                return true;
            }

            index = -1;
            length = 0;
            return false;
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private struct Frame
    {
        public CompiledMode Mode;

        // The value captured at begin when the mode has EndSameAsBegin; otherwise null.
        public string? Capture;

        // Memoized end match for EndSameAsBegin modes, whose result depends on Capture and therefore
        // cannot live in the per-regex scan cache.
        public ScanEntry EndCache;

        public readonly bool HasOpenScope => Mode.Scope is not null && !Mode.Skip;
    }

    public static string Highlight(string text, CompiledMode root, HighlightOptions options) => Highlight(text, root, options, out _);

    /// <param name="isFallback"><see langword="true"/> when the result is the input as plain text because highlighting was abandoned.</param>
    public static string Highlight(string text, CompiledMode root, HighlightOptions options, out bool isFallback)
    {
        isFallback = false;
        if (text.Length is 0)
            return "";

        var hasByteOrderMark = text[0] is '\uFEFF';
        var input = LineEndings.Normalize(text, hasByteOrderMark ? 1 : 0, out var lineEndings);
        var session = new Session(options, text.Length);

        try
        {
            if (hasByteOrderMark)
            {
                // A byte order mark is not whitespace for .NET's `\s`, so grammars whose root treats
                // any non-whitespace as illegal would reject the whole document because of it.
                session.Emitter.AddText("\uFEFF");
            }

            var run = new Run(session, input, root, options.IgnoreIllegals);
            if (!run.Execute(continuation: null) || session.Aborted)
            {
                isFallback = true;
                return Fallback(text, options);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            isFallback = true;
            return Fallback(text, options);
        }

        return lineEndings.Restore(session.Emitter.ToHtml());
    }

    private static string Fallback(string text, HighlightOptions options)
    {
        var emitter = new HtmlEmitter(options, text.Length + 16);
        emitter.AddText(text);
        return emitter.ToHtml();
    }

    /// <summary>State shared by the top-level run and the runs of every embedded sub-language.</summary>
    private sealed class Session(HighlightOptions options, int inputLength)
    {
        private readonly Dictionary<CompiledMode, Stack<ScanEntry[]>> _scanCachePool = new(ReferenceEqualityComparer.Instance);

        public HtmlEmitter Emitter { get; } = new(options, inputLength * 2);

        public int Iterations { get; set; }

        // Set when a grammar stopped making progress; every run then stops and the result is plain text.
        public bool Aborted { get; set; }

        public ScanEntry[] RentScanCache(CompiledMode root)
        {
            if (!_scanCachePool.TryGetValue(root, out var pool) || !pool.TryPop(out var cache))
            {
                cache = new ScanEntry[root.RegexSlotCount];
            }

            Array.Fill(cache, ScanEntry.Empty);
            return cache;
        }

        public void ReturnScanCache(CompiledMode root, ScanEntry[] cache)
        {
            if (!_scanCachePool.TryGetValue(root, out var pool))
            {
                pool = new Stack<ScanEntry[]>();
                _scanCachePool.Add(root, pool);
            }

            pool.Push(cache);
        }
    }

    private sealed class Run(Session session, string input, CompiledMode root, bool ignoreIllegals)
    {
        private readonly HtmlEmitter _emitter = session.Emitter;
        private readonly List<Frame> _stack = [];
        private ScanEntry[] _scanCache = [];

        // The text accumulated for the current mode is always the contiguous range
        // input[_bufferStart.._bufferStart + _bufferLength] (see Compiler.Validate).
        private int _bufferStart;
        private int _bufferLength;

        // highlight.js "continuations": the mode stack each sub-language ended in, so that the next
        // fragment of the same sub-language (e.g. the markup after a `${}` in a JS html`` template)
        // resumes in that state rather than at the sub-language's root.
        private Dictionary<string, Frame[]>? _continuations;
        private BeginGuards.ClosingTagIndex? _closingTags;

        /// <summary>The mode stack the run ended in.</summary>
        public Frame[] FinalStack { get; private set; } = [];

        /// <returns><see langword="false"/> when an illegal lexeme was found and illegal lexemes are not ignored, or when the session was aborted.</returns>
        public bool Execute(Frame[]? continuation)
        {
            _scanCache = session.RentScanCache(root);
            try
            {
                return ExecuteCore(continuation);
            }
            finally
            {
                session.ReturnScanCache(root, _scanCache);
            }
        }

        private bool ExecuteCore(Frame[]? continuation)
        {
            if (continuation is null)
            {
                _stack.Add(new Frame { Mode = root, EndCache = ScanEntry.Empty });
            }
            else
            {
                _stack.AddRange(continuation);
                for (var i = 1; i < _stack.Count; i++)
                {
                    if (_stack[i].HasOpenScope)
                        _emitter.OpenScope(_stack[i].Mode.Scope!, _stack[i].Mode.ClassNameAliases);
                }
            }

            var index = 0;
            var lastBeginIndex = -1;
            var stalledIndex = -1;
            var stalledIterations = 0;

            while (FindNextHit(index, out var hit))
            {
                if (!CheckForInfiniteLoop(hit.Index, ref stalledIndex, ref stalledIterations))
                    return false;

                AppendBuffer(index, hit.Index - index);

                // 0-width loop safety: begin then end at same position with empty lexeme.
                if (lastBeginIndex == hit.Index && hit is { Kind: HitKind.End, Length: 0 })
                {
                    if (hit.Index >= input.Length)
                        break;

                    AppendBuffer(hit.Index, 1);
                    index = hit.Index + 1;
                    lastBeginIndex = -1;
                    continue;
                }

                switch (hit.Kind)
                {
                    case HitKind.Begin:
                        DoBegin(hit);
                        lastBeginIndex = hit.Index;
                        index = hit.Mode.ReturnBegin ? hit.Index : (hit.Index + hit.Length);
                        break;

                    case HitKind.End:
                        index = DoEnd(hit);
                        lastBeginIndex = -1;
                        break;

                    case HitKind.Illegal:
                        if (!ignoreIllegals)
                            return false;

                        // Like highlight.js with ignoreIllegals: the lexeme is text of the current mode.
                        // A zero-width illegal match (e.g. `$`) must still advance.
                        if (hit.Index >= input.Length)
                        {
                            index = input.Length;
                            goto EndOfInput;
                        }

                        var length = Math.Max(1, hit.Length);
                        AppendBuffer(hit.Index, length);
                        index = hit.Index + length;
                        lastBeginIndex = -1;
                        break;
                }

                // A sub-language run flushed by DoBegin/DoEnd may have given up.
                if (session.Aborted)
                    return false;
            }

        EndOfInput:
            AppendBuffer(index, input.Length - index);
            ProcessBuffer(_stack[^1].Mode);

            // Close any unclosed scopes (skip the root frame).
            for (var i = _stack.Count - 1; i >= 1; i--)
            {
                if (_stack[i].HasOpenScope)
                    _emitter.CloseScope();
            }

            var finalStack = _stack.ToArray();
            for (var i = 0; i < finalStack.Length; i++)
            {
                finalStack[i].EndCache = ScanEntry.Empty;
            }

            FinalStack = finalStack;
            return true;
        }

        /// <summary>
        /// Last-resort guard against grammars that stop making progress (e.g. zero-width begins that
        /// keep entering modes at the same position). highlight.js has the same safety net.
        /// </summary>
        private bool CheckForInfiniteLoop(int hitIndex, ref int stalledIndex, ref int stalledIterations)
        {
            session.Iterations++;
            if (hitIndex == stalledIndex)
            {
                stalledIterations++;
            }
            else
            {
                stalledIndex = hitIndex;
                stalledIterations = 0;
            }

            if (stalledIterations > 10_000 || (session.Iterations > 100_000 && session.Iterations > hitIndex * 3))
            {
                session.Aborted = true;
            }

            return !session.Aborted;
        }

        private bool FindNextHit(int from, out Hit best)
        {
            best = default;
            var found = false;
            var top = _stack[^1].Mode;

            foreach (var child in top.Contains)
            {
                if (child.BeginRe is null)
                    continue;

                if (NextBegin(child, from, out var index, out var length) && (!found || index < best.Index))
                {
                    best = new Hit(index, length, HitKind.Begin, child, EndOwnerDepth: -1);
                    found = true;
                }
            }

            // End — walk up the stack for endsWithParent.
            for (var depth = _stack.Count - 1; depth >= 0; depth--)
            {
                var mode = _stack[depth].Mode;
                if (mode.EndRe is not null && NextEnd(depth, from, out var index, out var length) && (!found || index < best.Index))
                {
                    best = new Hit(index, length, HitKind.End, mode, depth);
                    found = true;
                }

                if (!mode.EndsWithParent)
                    break;
            }

            if (top.IllegalRe is not null && NextMatch(top.IllegalRe, top.IllegalSlot, from, out var illegalIndex, out var illegalLength) && (!found || illegalIndex < best.Index))
            {
                best = new Hit(illegalIndex, illegalLength, HitKind.Illegal, top, EndOwnerDepth: -1);
                found = true;
            }

            return found;
        }

        private bool NextMatch(Regex regex, int slot, int from, out int index, out int length)
        {
            ref var entry = ref _scanCache[slot];
            if (!entry.TryGet(from, out index, out length))
            {
                Scan(regex, from, out index, out length);
                entry = new ScanEntry(from, index, length);
            }

            return index >= 0;
        }

        private bool Scan(Regex regex, int from, out int index, out int length)
        {
            // EnumerateMatches does not allocate a Match; the few hits that need capture groups
            // re-run the regex at the known position (see GetMatchAt).
            foreach (var match in regex.EnumerateMatches(input, from))
            {
                index = match.Index;
                length = match.Length;
                return true;
            }

            index = -1;
            length = 0;
            return false;
        }

        private Match GetMatchAt(Regex regex, int index, int length)
        {
            var match = regex.Match(input, index);
            if (!match.Success || match.Index != index || match.Length != length)
                throw new InvalidOperationException($"The pattern '{regex}' did not reproduce the match at index {index}.");

            return match;
        }

        private bool NextBegin(CompiledMode child, int from, out int index, out int length)
        {
            if (child.BeginGuard is null)
                return NextMatch(child.BeginRe!, child.BeginSlot, from, out index, out length);

            // For guarded modes (hljs's `on:begin` veto), walk forward through candidate matches until
            // one is accepted. The accepted candidate is what gets memoized, so each candidate is
            // guarded once rather than on every lookup.
            ref var entry = ref _scanCache[child.BeginSlot];
            if (entry.TryGet(from, out index, out length))
                return index >= 0;

            var cursor = from;
            while (Scan(child.BeginRe!, cursor, out index, out length))
            {
                if (BeginGuards.Accept(child.BeginGuard, index, length, input, ref _closingTags))
                {
                    entry = new ScanEntry(from, index, length);
                    return true;
                }

                // A zero-width candidate at the very end of the input would otherwise ask the regex to
                // start past the end of the string, which throws.
                cursor = index + Math.Max(1, length);
                if (cursor > input.Length)
                    break;
            }

            entry = new ScanEntry(from, -1, 0);
            index = -1;
            length = 0;
            return false;
        }

        /// <summary>
        /// For modes with EndSameAsBegin, scan forward through end candidates and skip any whose
        /// capture doesn't match the value captured at begin time.
        /// </summary>
        private bool NextEnd(int depth, int from, out int index, out int length)
        {
            ref var frame = ref CollectionsMarshal.AsSpan(_stack)[depth];
            var mode = frame.Mode;
            if (!mode.EndSameAsBegin)
                return NextMatch(mode.EndRe!, mode.EndSlot, from, out index, out length);

            if (frame.EndCache.TryGet(from, out index, out length))
                return index >= 0;

            var expected = frame.Capture.AsSpan();
            var cursor = from;
            while (cursor <= input.Length)
            {
                var match = mode.EndRe!.Match(input, cursor);
                if (!match.Success)
                    break;

                if (GetSameAsBeginCapture(match).SequenceEqual(expected))
                {
                    index = match.Index;
                    length = match.Length;
                    frame.EndCache = new ScanEntry(from, index, length);
                    return true;
                }

                // Advance past this candidate and look for the next one.
                cursor = match.Index + Math.Max(1, match.Length);
            }

            frame.EndCache = new ScanEntry(from, -1, 0);
            index = -1;
            length = 0;
            return false;
        }

        /// <summary>
        /// The delimiter captured for EndSameAsBegin: the first non-empty capturing group. Patterns
        /// with alternative spellings put the delimiter in different groups, e.g. PHP's
        /// <c>&lt;&lt;&lt;(?:(\w+)|"(\w+)")</c> (highlight.js uses <c>m[1] || m[2]</c>).
        /// </summary>
        private static ReadOnlySpan<char> GetSameAsBeginCapture(Match match)
        {
            for (var i = 1; i < match.Groups.Count; i++)
            {
                var group = match.Groups[i];
                if (group is { Success: true, Length: > 0 })
                    return group.ValueSpan;
            }

            return [];
        }

        private void AppendBuffer(int start, int length)
        {
            if (length <= 0)
                return;

            if (_bufferLength is 0)
            {
                _bufferStart = start;
            }
            else if (_bufferStart + _bufferLength != start)
            {
                throw new InvalidOperationException("The mode buffer must be a contiguous range of the input.");
            }

            _bufferLength += length;
        }

        private void DoBegin(Hit hit)
        {
            var newMode = hit.Mode;

            Match? match = null;
            if (newMode.EndSameAsBegin || newMode.BeginGroupScopes is not null)
            {
                match = GetMatchAt(newMode.BeginRe!, hit.Index, hit.Length);
            }

            var capture = newMode.EndSameAsBegin ? GetSameAsBeginCapture(match!).ToString() : null;

            // skip: true → the begin lexeme is appended to the parent's buffer and the
            // mode is pushed without opening a scope or flushing. End match will do
            // the mirror operation. The parent's buffer stays intact, so its
            // sub-language (if any) re-tokenizes the whole region as one unit.
            if (newMode.Skip)
            {
                AppendBuffer(hit.Index, hit.Length);
                PushMode(newMode, capture);
                return;
            }

            var top = _stack[^1].Mode;
            if (newMode.BeginGroupScopes is not null)
            {
                ProcessBuffer(top);
                PushMode(newMode, capture);
                EmitMultiClass(newMode, match!);
                return;
            }

            if (newMode.ExcludeBegin)
            {
                AppendBuffer(hit.Index, hit.Length);
                ProcessBuffer(top);
                PushMode(newMode, capture);
            }
            else
            {
                ProcessBuffer(top);
                PushMode(newMode, capture);
                if (!newMode.ReturnBegin)
                    AppendBuffer(hit.Index, hit.Length);
            }
        }

        private void EmitMultiClass(CompiledMode mode, Match match)
        {
            var scopeMap = mode.BeginGroupScopes!;
            foreach (var groupIndex in mode.BeginGroupOrder!)
            {
                if (groupIndex >= match.Groups.Count)
                    continue;

                var group = match.Groups[groupIndex];
                if (group is not { Success: true, Length: > 0 })
                    continue;

                if (scopeMap.TryGetValue(groupIndex, out var scope))
                {
                    _emitter.OpenScope(scope, mode.ClassNameAliases);
                    _emitter.AddText(group.ValueSpan);
                    _emitter.CloseScope();
                }
                else
                {
                    // Mirror hljs: unscoped groups still go through the mode's keyword scanner.
                    ProcessKeywords(mode, group.Index, group.Length);
                }
            }
        }

        private int DoEnd(Hit hit)
        {
            var lexemeStart = hit.Index;
            var lexemeLength = hit.Length;
            var origin = _stack[^1].Mode;

            // The mode that actually ends: the owner of the end match, or the ancestor it ends through
            // endsParent (highlight.js's endOfMode).
            var terminalDepth = hit.EndOwnerDepth;
            while (terminalDepth > 0 && _stack[terminalDepth].Mode.EndsParent)
                terminalDepth--;

            var endMode = _stack[terminalDepth].Mode;

            if (origin.EndScope is { } endScope)
            {
                // endScope (hljs `_wrap`): flush the buffer, then emit the end lexeme wrapped in its
                // own scope. Mutually exclusive with returnEnd/excludeEnd.
                ProcessBuffer(origin);
                _emitter.OpenScope(endScope, origin.ClassNameAliases);
                _emitter.AddText(input.AsSpan(lexemeStart, lexemeLength));
                _emitter.CloseScope();
            }
            else if (origin.Skip)
            {
                // skip: true → mirror of DoBegin. The end lexeme stays in the parent's buffer.
                AppendBuffer(lexemeStart, lexemeLength);
            }
            else
            {
                if (!origin.ReturnEnd && !origin.ExcludeEnd)
                    AppendBuffer(lexemeStart, lexemeLength);

                ProcessBuffer(origin);

                if (origin.ExcludeEnd)
                    AppendBuffer(lexemeStart, lexemeLength);
            }

            while (_stack.Count - 1 >= terminalDepth && _stack.Count > 1)
            {
                if (_stack[^1].HasOpenScope)
                    _emitter.CloseScope();
                _stack.RemoveAt(_stack.Count - 1);
            }

            if (endMode.Starts is not null)
                PushMode(endMode.Starts, capture: null);

            return origin.ReturnEnd ? lexemeStart : (lexemeStart + lexemeLength);
        }

        private void PushMode(CompiledMode mode, string? capture)
        {
            var frame = new Frame { Mode = mode, Capture = capture, EndCache = ScanEntry.Empty };
            if (frame.HasOpenScope)
                _emitter.OpenScope(mode.Scope!, mode.ClassNameAliases);

            _stack.Add(frame);
        }

        private void ProcessBuffer(CompiledMode top)
        {
            if (_bufferLength is 0)
                return;

            var start = _bufferStart;
            var length = _bufferLength;
            _bufferLength = 0;

            if (top.SubLanguage is { } subLanguage)
            {
                // Embedded languages always ignore illegal lexemes, like highlight.js does.
                var subRoot = LanguageRegistry.Get(subLanguage, root.MatchTimeout);
                _continuations ??= new Dictionary<string, Frame[]>(StringComparer.Ordinal);
                _continuations.TryGetValue(subLanguage, out var continuation);

                _emitter.OpenSubLanguage(subLanguage);
                var run = new Run(session, input.Substring(start, length), subRoot, ignoreIllegals: true);
                run.Execute(continuation);
                _emitter.CloseScope();

                _continuations[subLanguage] = run.FinalStack;
                return;
            }

            ProcessKeywords(top, start, length);
        }

        private void ProcessKeywords(CompiledMode mode, int start, int length)
        {
            var span = input.AsSpan(start, length);
            if (mode.KeywordPatternRe is null || mode.KeywordMap is null)
            {
                _emitter.AddText(span);
                return;
            }

            var lookup = mode.KeywordMap.GetAlternateLookup<ReadOnlySpan<char>>();
            var lastIndex = 0;
            foreach (var match in mode.KeywordPatternRe.EnumerateMatches(span))
            {
                if (match.Index > lastIndex)
                {
                    _emitter.AddText(span[lastIndex..match.Index]);
                }

                var word = span.Slice(match.Index, match.Length);
                if (lookup.TryGetValue(word, out var scope) && scope is not null && (mode.KeywordValidator is null || mode.KeywordValidator(input, start + match.Index, word)))
                {
                    _emitter.OpenScope(scope, mode.ClassNameAliases);
                    _emitter.AddText(word);
                    _emitter.CloseScope();
                }
                else
                {
                    _emitter.AddText(word);
                }

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < span.Length)
            {
                _emitter.AddText(span[lastIndex..]);
            }
        }
    }

    /// <summary>
    /// Grammars come from highlight.js, where <c>$</c> matches before <c>\r</c> and <c>.</c> does not match
    /// it; in .NET neither is true. Tokenizing CRLF text as LF and restoring the <c>\r</c> afterwards gives
    /// every grammar the line-ending behavior it was written for, including the patterns that spell
    /// <c>\n</c> explicitly.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly struct LineEndings
    {
        // Null when nothing was normalized. Otherwise one entry per '\n' of the normalized text, true
        // when it was a CRLF — or empty when every '\n' was, which is the common case.
        private readonly bool[]? _isCrLf;

        private LineEndings(bool[] isCrLf) => _isCrLf = isCrLf;

        public static string Normalize(string text, int start, out LineEndings lineEndings)
        {
            var content = text.AsSpan(start);
            var crLfCount = content.Count("\r\n");
            if (crLfCount is 0)
            {
                lineEndings = default;
                return start is 0 ? text : content.ToString();
            }

            var lfCount = content.Count('\n');
            bool[] isCrLf = [];
            if (lfCount != crLfCount)
            {
                isCrLf = new bool[lfCount];
                var lineFeed = 0;
                for (var i = 0; i < content.Length; i++)
                {
                    if (content[i] is '\n')
                    {
                        isCrLf[lineFeed++] = i > 0 && content[i - 1] is '\r';
                    }
                }
            }

            lineEndings = new LineEndings(isCrLf);
            return content.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        /// <remarks>
        /// Markup never contains a line feed and the text of the output is the input in order, so the
        /// n-th line feed of the HTML is the n-th line feed of the normalized input.
        /// </remarks>
        public string Restore(string html)
        {
            if (_isCrLf is null)
                return html;

            if (_isCrLf.Length is 0)
                return html.Replace("\n", "\r\n", StringComparison.Ordinal);

            var result = new StringBuilder(html.Length + _isCrLf.Length);
            var lineFeed = 0;
            var segmentStart = 0;
            for (var i = 0; i < html.Length; i++)
            {
                if (html[i] is '\n')
                {
                    result.Append(html, segmentStart, i - segmentStart);
                    result.Append(_isCrLf[lineFeed++] ? "\r\n" : "\n");
                    segmentStart = i + 1;
                }
            }

            result.Append(html, segmentStart, html.Length - segmentStart);
            return result.ToString();
        }
    }
}
