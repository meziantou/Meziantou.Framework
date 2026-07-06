using System.Text.RegularExpressions;

namespace Meziantou.Framework.SyntaxHighlighting.Engine;

internal static class Tokenizer
{
    private enum HitKind { Begin, End, Illegal }

    private readonly record struct Hit(int Index, int Length, HitKind Kind, CompiledMode? Child, int EndOwnerDepth, Match? Match);

    public static Func<string, CompiledMode>? SubLanguageResolver { get; set; }

    public static string Highlight(string input, CompiledMode root, HighlightOptions options)
    {
        var result = Run(input, root, options);
        if (result is not null)
            return result;

        var fallback = new HtmlEmitter(options, root.ClassNameAliases);
        fallback.AddText(input);
        return fallback.ToHtml();
    }

    private static string? Run(string input, CompiledMode root, HighlightOptions options)
    {
        var emitter = new HtmlEmitter(options, root.ClassNameAliases);
        var stack = new List<CompiledMode> { root };
        // Parallel to `stack`: for each frame, the captured begin group-1 value
        // when the mode has EndSameAsBegin; otherwise null.
        var beginCaptures = new List<string?> { null };
        var modeBuffer = new StringBuilder();
        // Absolute position in `input` where the current contents of `modeBuffer`
        // started accumulating. Used by KeywordValidator to look at full-input
        // context (e.g. preceding `from <id> in` for LINQ clauses) beyond the
        // current buffer slice. Set whenever the buffer transitions from empty
        // to non-empty.
        var bufferStart = 0;
        var index = 0;
        var lastBeginIndex = -1;

        while (true)
        {
            var hit = FindNextHit(input, index, stack, beginCaptures);
            if (hit is null)
                break;

            var h = hit.Value;
            AppendBuffer(modeBuffer, input, index, h.Index - index, ref bufferStart);

            // 0-width loop safety: begin then end at same position with empty lexeme.
            if (lastBeginIndex == h.Index && h is { Kind: HitKind.End, Length: 0 })
            {
                if (h.Index < input.Length)
                {
                    AppendBuffer(modeBuffer, input, h.Index, 1, ref bufferStart);
                    index = h.Index + 1;
                    lastBeginIndex = -1;
                    continue;
                }
                break;
            }

            switch (h.Kind)
            {
                case HitKind.Begin:
                    DoBegin(input, h, stack, beginCaptures, modeBuffer, ref bufferStart, emitter, options);
                    lastBeginIndex = h.Index;
                    var entered = stack[^1];
                    index = entered.ReturnBegin ? h.Index : (h.Index + h.Length);
                    break;

                case HitKind.End:
                    index = DoEnd(input, h, stack, beginCaptures, modeBuffer, ref bufferStart, emitter, options);
                    lastBeginIndex = -1;
                    break;

                case HitKind.Illegal:
                    return null;
            }
        }

        AppendBuffer(modeBuffer, input, index, input.Length - index, ref bufferStart);
        ProcessBuffer(stack[^1], modeBuffer, bufferStart, input, emitter, options);

        // Close any unclosed scopes (skip the root frame).
        for (var i = stack.Count - 1; i >= 1; i--)
        {
            if (stack[i].Scope is not null)
                emitter.CloseScope();
        }

        return emitter.ToHtml();
    }

    private static Hit? FindNextHit(string input, int from, List<CompiledMode> stack, List<string?> beginCaptures)
    {
        Hit? best = null;
        var top = stack[^1];

        foreach (var child in top.Contains)
        {
            if (child.BeginRe is null)
                continue;
            var m = child.BeginRe.Match(input, from);
            // For guarded modes (hljs's `on:begin` veto), walk forward through
            // candidate matches until one is accepted by the guard.
            while (m.Success && child.BeginGuard is not null && !BeginGuards.Accept(child.BeginGuard, m, input))
            {
                m = child.BeginRe.Match(input, m.Index + Math.Max(1, m.Length));
            }
            if (!m.Success)
                continue;
            if (best is null || m.Index < best.Value.Index)
                best = new Hit(m.Index, m.Length, HitKind.Begin, child, -1, m);
        }

        // End — walk up the stack for endsWithParent.
        for (var depth = stack.Count - 1; depth >= 0; depth--)
        {
            var frame = stack[depth];
            if (frame.EndRe is not null)
            {
                var m = MatchEnd(frame, beginCaptures[depth], input, from);
                if (m is not null && (best is null || m.Index < best.Value.Index))
                {
                    best = new Hit(m.Index, m.Length, HitKind.End, frame, depth, m);
                }
            }
            if (!frame.EndsWithParent)
                break;
        }

        if (top.IllegalRe is not null)
        {
            var m = top.IllegalRe.Match(input, from);
            if (m.Success && (best is null || m.Index < best.Value.Index))
                best = new Hit(m.Index, m.Length, HitKind.Illegal, Child: null, -1, m);
        }

        return best;
    }

    /// <summary>
    /// For modes with EndSameAsBegin, scan forward through end candidates and skip any
    /// whose group-1 capture doesn't match the value captured at begin time.
    /// </summary>
    private static Match? MatchEnd(CompiledMode frame, string? beginCapture, string input, int from)
    {
        if (frame.EndRe is null)
            return null;
        if (!frame.EndSameAsBegin)
        {
            var m = frame.EndRe.Match(input, from);
            return m.Success ? m : null;
        }

        var expected = beginCapture.AsSpan();
        var cursor = from;
        while (cursor <= input.Length)
        {
            var m = frame.EndRe.Match(input, cursor);
            if (!m.Success)
                return null;

            if (m.Groups.Count > 1 && m.Groups[1].ValueSpan.SequenceEqual(expected))
                return m;

            // Advance past this candidate and look for the next one.
            cursor = m.Index + Math.Max(1, m.Length);
        }
        return null;
    }

    private static void AppendBuffer(StringBuilder buf, string input, int start, int length, ref int bufferStart)
    {
        if (length <= 0)
            return;

        if (buf.Length is 0)
        {
            bufferStart = start;
        }

        buf.Append(input, start, length);
    }

    private static void DoBegin(string input, Hit hit, List<CompiledMode> stack, List<string?> beginCaptures, StringBuilder modeBuffer, ref int bufferStart, HtmlEmitter emitter, HighlightOptions options)
    {
        var newMode = hit.Child!;
        var lexemeStart = hit.Index;
        var lexemeLength = hit.Length;
        var top = stack[^1];

        string? capture = null;
        if (newMode.EndSameAsBegin && hit.Match is { Groups.Count: > 1 } m && m.Groups[1].Success)
            capture = m.Groups[1].Value;

        // skip: true → the begin lexeme is appended to the parent's buffer and the
        // mode is pushed without opening a scope or flushing. End match will do
        // the mirror operation. The parent's buffer stays intact, so its
        // sub-language (if any) re-tokenizes the whole region as one unit.
        if (newMode.Skip)
        {
            AppendBuffer(modeBuffer, input, lexemeStart, lexemeLength, ref bufferStart);
            stack.Add(newMode);
            beginCaptures.Add(capture);
            return;
        }

        if (newMode.BeginGroupScopes is not null && hit.Match is not null)
        {
            ProcessBuffer(top, modeBuffer, bufferStart, input, emitter, options);
            EnterMode(newMode, stack, beginCaptures, capture, emitter);
            EmitMultiClass(newMode, hit.Match, emitter);
            return;
        }

        if (newMode.ExcludeBegin)
        {
            AppendBuffer(modeBuffer, input, lexemeStart, lexemeLength, ref bufferStart);
            ProcessBuffer(top, modeBuffer, bufferStart, input, emitter, options);
            EnterMode(newMode, stack, beginCaptures, capture, emitter);
        }
        else
        {
            ProcessBuffer(top, modeBuffer, bufferStart, input, emitter, options);
            EnterMode(newMode, stack, beginCaptures, capture, emitter);
            if (!newMode.ReturnBegin)
                AppendBuffer(modeBuffer, input, lexemeStart, lexemeLength, ref bufferStart);
        }
    }

    private static void EmitMultiClass(CompiledMode mode, Match m, HtmlEmitter emitter)
    {
        var order = mode.BeginGroupOrder!;
        var scopeMap = mode.BeginGroupScopes;
        foreach (var groupIndex in order)
        {
            if (groupIndex >= m.Groups.Count)
                continue;
            var group = m.Groups[groupIndex];
            if (!group.Success)
                continue;
            var text = group.ValueSpan;
            if (text.Length is 0)
                continue;

            if (scopeMap is not null && scopeMap.TryGetValue(groupIndex, out var scope))
            {
                emitter.OpenScope(scope);
                emitter.AddText(text);
                emitter.CloseScope();
            }
            else
            {
                // Mirror hljs: unscoped groups still go through the mode's keyword scanner.
                // Pass the group's value as both buffer and input; validators that need
                // wider context don't fire here, but the common case (keyword lookup) works.
                ProcessKeywords(mode, group.Value, 0, group.Value, emitter);
            }
        }
    }

    private static int DoEnd(string input, Hit hit, List<CompiledMode> stack, List<string?> beginCaptures, StringBuilder modeBuffer, ref int bufferStart, HtmlEmitter emitter, HighlightOptions options)
    {
        var endOwner = hit.Child!;
        var endOwnerDepth = hit.EndOwnerDepth;
        var lexemeStart = hit.Index;
        var lexemeLength = hit.Length;

        var origin = stack[^1];
        var originExcludeEnd = origin.ExcludeEnd;
        var originReturnEnd = origin.ReturnEnd;

        var terminalDepth = endOwnerDepth;
        while (terminalDepth > 0 && stack[terminalDepth].EndsParent)
            terminalDepth--;

        // skip: true → mirror of DoBegin. Append the end lexeme to the buffer
        // and pop without flushing or closing a scope.
        if (origin.Skip)
        {
            AppendBuffer(modeBuffer, input, lexemeStart, lexemeLength, ref bufferStart);
            stack.RemoveAt(stack.Count - 1);
            beginCaptures.RemoveAt(beginCaptures.Count - 1);
            return lexemeStart + lexemeLength;
        }

        // endScope (hljs `_wrap`): flush the buffer, then emit the end lexeme
        // wrapped in its own scope. Mutually exclusive with returnEnd/excludeEnd.
        if (endOwner.EndScope is { } endScope)
        {
            ProcessBuffer(origin, modeBuffer, bufferStart, input, emitter, options);
            emitter.OpenScope(endScope);
            emitter.AddText(input.AsSpan(lexemeStart, lexemeLength));
            emitter.CloseScope();
        }
        else
        {
            if (!originReturnEnd && !originExcludeEnd)
                AppendBuffer(modeBuffer, input, lexemeStart, lexemeLength, ref bufferStart);

            ProcessBuffer(origin, modeBuffer, bufferStart, input, emitter, options);

            if (originExcludeEnd)
                AppendBuffer(modeBuffer, input, lexemeStart, lexemeLength, ref bufferStart);
        }

        while (stack.Count - 1 >= terminalDepth && stack.Count > 1)
        {
            var current = stack[^1];
            if (current.Scope is not null)
                emitter.CloseScope();
            stack.RemoveAt(stack.Count - 1);
            beginCaptures.RemoveAt(beginCaptures.Count - 1);
        }

        if (endOwner.Starts is not null)
            EnterMode(endOwner.Starts, stack, beginCaptures, beginCapture: null, emitter);

        return originReturnEnd ? lexemeStart : (lexemeStart + lexemeLength);
    }

    private static void EnterMode(CompiledMode newMode, List<CompiledMode> stack, List<string?> beginCaptures, string? beginCapture, HtmlEmitter emitter)
    {
        if (newMode.Scope is { } scope)
            emitter.OpenScope(scope);
        stack.Add(newMode);
        beginCaptures.Add(beginCapture);
    }

    private static void ProcessBuffer(CompiledMode top, StringBuilder modeBuffer, int bufferStart, string input, HtmlEmitter emitter, HighlightOptions options)
    {
        if (modeBuffer.Length is 0)
            return;

        var text = modeBuffer.ToString();
        modeBuffer.Clear();

        if (top.SubLanguage is not null && SubLanguageResolver is not null)
        {
            var subRoot = SubLanguageResolver(top.SubLanguage);
            var subHtml = Highlight(text, subRoot, options);
            emitter.OpenSubLanguage(top.SubLanguage);
            emitter.AppendRaw(subHtml);
            emitter.CloseScope();
            return;
        }

        ProcessKeywords(top, text, bufferStart, input, emitter);
    }

    private static void ProcessKeywords(CompiledMode top, string text, int bufferStart, string input, HtmlEmitter emitter)
    {
        if (top.KeywordPatternRe is null || top.KeywordMap is null)
        {
            emitter.AddText(text);
            return;
        }

        var span = text.AsSpan();
        var lookup = top.KeywordMap.GetAlternateLookup<ReadOnlySpan<char>>();
        var lastIndex = 0;
        foreach (var m in top.KeywordPatternRe.EnumerateMatches(span))
        {
            if (m.Index > lastIndex)
            {
                emitter.AddText(span[lastIndex..m.Index]);
            }

            var word = span.Slice(m.Index, m.Length);
            if (lookup.TryGetValue(word, out var data) && data.Scope is not null && (top.KeywordValidator is null || top.KeywordValidator(input, bufferStart + m.Index, word)))
            {
                emitter.OpenScope(data.Scope);
                emitter.AddText(word);
                emitter.CloseScope();
            }
            else
            {
                emitter.AddText(word);
            }

            lastIndex = m.Index + m.Length;
        }
        if (lastIndex < span.Length)
        {
            emitter.AddText(span[lastIndex..]);
        }
    }
}
