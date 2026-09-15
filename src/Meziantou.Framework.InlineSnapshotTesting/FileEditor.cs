using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using Meziantou.Framework.InlineSnapshotTesting.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.InlineSnapshotTesting;

internal static class FileEditor
{
    // A merge tool can keep the lock of another process while the user resolves the merge.
    private static readonly TimeSpan InterProcessLockTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StaleTemporaryFileAge = TimeSpan.FromDays(1);
    private static readonly FullPath TemporaryDirectoryRoot = FullPath.GetTempPath() / "Meziantou.Framework.InlineSnapshotTesting";
    private static int s_staleTemporaryFilesDeleted;

    private static readonly ConcurrentDictionary<FullPath, Lock> FileLocks = new();
    private static readonly ConcurrentDictionary<FullPath, FullPath> TempFiles = new();

    // Concurrent because the lock above is per file: two threads updating snapshots in two different files
    // hold two different locks and would otherwise mutate this collection at the same time.
    // The List<FileEdit> of a given file is only ever touched under that file's lock, so it stays a plain list.
    private static readonly ConcurrentDictionary<FullPath, List<FileEdit>> Changes = new();

    private static int GetActualLine(FullPath fullPath, int startLine)
    {
        if (Changes.TryGetValue(fullPath, out var edits))
        {
            var diff = 0;
            foreach (var edit in edits)
            {
                if (edit.StartLine < startLine)
                {
                    diff += edit.AfterLineSpan - edit.BeforeLineSpan;
                }
            }

            return startLine + diff;
        }

        return startLine;
    }

    private static SourceText GetSourceText(InlineSnapshotSettings settings, FullPath tempPath)
    {
        using var fs = File.OpenRead(tempPath);
        using var stream = fs.CanSeek ? fs : CopyToMemoryStream(fs);
        return SourceText.From(stream, settings.FileEncoding);

        static Stream CopyToMemoryStream(Stream stream)
        {
            var ms = new MemoryStream();
            try
            {
                stream.CopyTo(ms);

                // The copy, not the source: this branch only runs for a source that cannot seek, and SourceText.From
                // reads the returned stream from its current position, which CopyTo leaves at the end.
                ms.Seek(0, SeekOrigin.Begin);
                return ms;
            }
            catch
            {
                ms.Dispose();
                throw;
            }
        }
    }

    public static void UpdateFile(CallerContext context, InlineSnapshotSettings settings, string? existingValue, string? newValue, CancellationToken cancellationToken = default)
    {
        var lockObject = FileLocks.GetOrAdd(context.FilePath, _ => new());
        lock (lockObject)
        {
            // The lock above only covers this process, while the test processes of the other target frameworks run
            // the same tests at the same time and edit the same files.
            using var interProcessLock = AcquireInterProcessLock(context.FilePath);

            var tempPath = TempFiles.GetOrAdd(context.FilePath, CreateTemporaryFilePath);
            var preprocessorSymbols = context.GetCompilationDefines();

            // Find node to update
            var options = new CSharpParseOptions(preprocessorSymbols: preprocessorSymbols);
            var reuseTemporaryFile = settings.SnapshotUpdateStrategy.ReuseTemporaryFile;
            var filePath = reuseTemporaryFile && File.Exists(tempPath) ? tempPath : context.FilePath;
            var sourceText = GetSourceText(settings, filePath);
            var tree = CSharpSyntaxTree.ParseText(sourceText, options, filePath, cancellationToken);
            var root = tree.GetRoot(cancellationToken);

            var (invocationExpression, argumentExpression, isUpToDate) = FindInvocationToUpdate(context, sourceText, root, existingValue, newValue);

            // The call was compiled with the previous snapshot, but the file already holds the new one. Another test
            // process, such as the one of another target framework, or a previous execution of the same call in a loop
            // or a theory already updated it.
            if (isUpToDate)
                return;

            // Update node
            var indentation = settings.Indentation ?? DetectIndentation(sourceText);
            var eol = settings.EndOfLine ?? DetectEndOfLine(sourceText);
            var startPosition = GetStartPosition(invocationExpression);

            LiteralExpressionSyntax newArgumentExpression;
            if (newValue is null)
            {
                newArgumentExpression = SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
            }
            else
            {
                var formattedValue = CSharpStringLiteral.Create(newValue, context.FilterFormats(settings.AllowedStringFormats), indentation, startPosition, eol);
                newArgumentExpression = SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(formattedValue, newValue));
            }

            SyntaxNode oldNode;
            SyntaxNode newNode;
            if (argumentExpression is not null)
            {
                oldNode = argumentExpression;
                newNode = newArgumentExpression
                        .WithLeadingTrivia(argumentExpression.GetLeadingTrivia())
                        .WithTrailingTrivia(argumentExpression.GetTrailingTrivia());
            }
            else
            {
                oldNode = invocationExpression;
                newNode = invocationExpression.AddArgumentListArguments(CreateArgument(context, invocationExpression, newArgumentExpression));
            }

            // Reformatting a snapshot that is already written the preferred way changes nothing. Rewriting the file would
            // still trigger a rebuild and the file watchers, and a merge tool strategy would open a diff with no difference.
            if (oldNode.ToFullString() == newNode.ToFullString())
                return;

            var newRoot = root.ReplaceNode(oldNode, newNode);

            if (reuseTemporaryFile)
            {
                AddFileEdit(context, oldNode, newNode);
            }

            // Save the file
            // Create a temp file, show diff if needed, Move or let the tool update the file
            var encoding = settings.FileEncoding ?? sourceText.Encoding ?? Encoding.UTF8;

            tempPath.CreateParentDirectory();
            var tempFileInfo = new FileInfo(tempPath);
            if (tempFileInfo.Exists)
            {
                tempFileInfo.TrySetReadOnly(false);
            }

            using (var outputStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var textWriter = new StreamWriter(outputStream, encoding))
            {
                newRoot.WriteTo(textWriter);
            }

            tempFileInfo.TrySetReadOnly(true);

            settings.SnapshotUpdateStrategy.UpdateFile(settings, context.FilePath, tempPath);

            if (reuseTemporaryFile)
            {
                // The strategy discarded the temporary file (for instance, because no merge tool could be started), so
                // the next update reads the source file again, which contains none of the edits tracked so far.
                if (!File.Exists(tempPath))
                {
                    Changes.TryRemove(context.FilePath, out _);
                }

                return;
            }

            // Track the changes
            // note: Diff tools allow partial merge or custom edits => we need to reload the document to find the new expression
            var mergedSourceText = GetSourceText(settings, filePath);
            var mergedTree = CSharpSyntaxTree.ParseText(mergedSourceText, options, filePath, cancellationToken);
            var mergedRoot = mergedTree.GetRoot(cancellationToken);

            // Only the argument list is edited, so the updated call still has its argument list where it was. Looking for
            // the largest call around that position instead would find the enclosing call when the snapshot is validated
            // inside a lambda, and record a line shift that is much too large.
            var argumentListStart = invocationExpression.ArgumentList.SpanStart;
            var mergedInvocation = mergedRoot.DescendantNodes(new TextSpan(argumentListStart, 1))
                .OfType<InvocationExpressionSyntax>()
                .FirstOrDefault(invocation => invocation.ArgumentList.SpanStart == argumentListStart && IsInvocationOf(invocation, context.MethodName));

            if (mergedInvocation is null)
            {
                // The merge changed the file in a way that cannot be tracked. The line of the next snapshots cannot be
                // predicted anymore, so they are searched in the member containing the call.
                Changes.TryRemove(context.FilePath, out _);
                return;
            }

            AddFileEdit(context, invocationExpression, mergedInvocation);
        }
    }

    /// <summary>
    /// Returns a path in a directory of its own, so the temporary file keeps the name of the source file, which is what a
    /// merge tool shows. The files a merge tool strategy leaves behind are deleted once they are old enough.
    /// </summary>
    private static FullPath CreateTemporaryFilePath(FullPath sourceFilePath)
    {
        var root = TemporaryDirectoryRoot / "files";
        if (Interlocked.Exchange(ref s_staleTemporaryFilesDeleted, 1) == 0)
        {
            DeleteStaleTemporaryFiles(root);
        }

        return root / Guid.NewGuid().ToString("N") / sourceFilePath.Name;
    }

    /// <summary>
    /// A merge tool that does not block the test can still show a temporary file after the test process exits, so these
    /// files cannot be deleted when the process exits. The ones that have not been written for a day are deleted instead.
    /// </summary>
    internal static void DeleteStaleTemporaryFiles(FullPath root)
    {
        try
        {
            var directory = new DirectoryInfo(root);
            if (!directory.Exists)
                return;

            var threshold = DateTime.UtcNow - StaleTemporaryFileAge;
            foreach (var subdirectory in directory.EnumerateDirectories())
            {
                try
                {
                    var files = subdirectory.GetFiles();
                    var lastWriteTime = files.Select(file => file.LastWriteTimeUtc).Append(subdirectory.LastWriteTimeUtc).Max();
                    if (lastWriteTime >= threshold)
                        continue;

                    foreach (var file in files)
                    {
                        file.TrySetReadOnly(false);
                    }

                    subdirectory.Delete(recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static FileStream AcquireInterProcessLock(FullPath filePath)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(filePath.Value)));
        var lockPath = TemporaryDirectoryRoot / (hash + ".lock");
        lockPath.CreateParentDirectory();

        // The lock file is never deleted: deleting it while another process waits for it would let that process lock a
        // new file while a third one still holds the deleted one.
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex)
            {
                if (stopwatch.Elapsed >= InterProcessLockTimeout)
                {
                    throw new InlineSnapshotException($"""
                        Cannot update '{filePath}': another process has been updating it for more than {InterProcessLockTimeout.TotalMinutes} minutes.
                        It may be another test process waiting for a merge tool started with '{nameof(SnapshotUpdateStrategy)}.{nameof(SnapshotUpdateStrategy.MergeToolSync)}' to close.
                        Lock file: {lockPath}
                        """, ex);
                }

                Thread.Sleep(50);
            }
        }
    }

    private static void AddFileEdit(CallerContext context, SyntaxNode oldNode, SyntaxNode newNode)
    {
        var oldSpan = oldNode.GetLocation().GetLineSpan();
        var newSpan = newNode.GetLocation().GetLineSpan();

        var fileEdit = new FileEdit(
            context.LineNumber,
            oldSpan.EndLinePosition.Line - oldSpan.StartLinePosition.Line,
            newSpan.EndLinePosition.Line - newSpan.StartLinePosition.Line);
        var fileEdits = Changes.GetOrAdd(context.FilePath, _ => []);

        fileEdits.Add(fileEdit);
    }

    /// <summary>Finds the call to update, and reports whether its snapshot already holds <paramref name="newValue"/>.</summary>
    private static (InvocationExpressionSyntax Invocation, ExpressionSyntax? Argument, bool IsUpToDate) FindInvocationToUpdate(CallerContext context, SourceText sourceText, SyntaxNode root, string? existingValue, string? newValue)
    {
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(invocation => IsInvocationOf(invocation, context.MethodName)).ToArray();

        // [CallerLineNumber] reports the line of the method name, which is not the first line of a multi-line call
        var lineIndex = GetActualLine(context.FilePath, context.LineNumber) - 1;
        List<InvocationExpressionSyntax> candidates = [];
        if (lineIndex >= 0 && lineIndex < sourceText.Lines.Count)
        {
            candidates.AddRange(invocations.Where(invocation => sourceText.Lines.GetLineFromPosition(GetMethodNameToken(invocation).SpanStart).LineNumber == lineIndex));
        }

        // Several calls on the same line: keep the ones in the statement the PDB attributes the call to
        if (candidates.Count > 1 && TryGetSequencePointNode(context, sourceText, root) is { } sequencePointNode)
        {
            var inStatement = candidates.Where(candidate => sequencePointNode.Span.Contains(candidate.Span)).ToList();
            if (inStatement.Count > 0)
            {
                candidates = inStatement;
            }
        }

        if (TryFindUniqueMatch(context, candidates, existingValue, out var match, out var firstActualValue))
            return (match.Invocation, match.Argument, IsUpToDate: false);

        // The snapshot on the expected line already holds the new value. It is checked before searching the member for
        // the previous value, which could belong to another call that still has to be updated.
        if (TryFindUniqueMatch(context, candidates, newValue, out match, out _))
            return (match.Invocation, match.Argument, IsUpToDate: true);

        // The call is not where it is expected. The file may have been edited since the build, by the user, by another
        // test process, or by a merge tool. Search the whole member for a unique call whose snapshot is the expected one.
        var memberInvocations = invocations.Where(invocation => !candidates.Contains(invocation) && IsInCallerMember(context, invocation)).ToList();
        if (TryFindUniqueMatch(context, memberInvocations, existingValue, out match, out _))
            return (match.Invocation, match.Argument, IsUpToDate: false);

        if (TryFindUniqueMatch(context, memberInvocations, newValue, out match, out _))
            return (match.Invocation, match.Argument, IsUpToDate: true);

        if (candidates.Count > 0)
            throw new InlineSnapshotException($"Cannot find the argument to update. The current value doesn't match the expected value.\nExpected: <{existingValue}>\nActual: <{firstActualValue}>");

        throw new InlineSnapshotException("Cannot find the SyntaxNode to update");
    }

    /// <param name="firstActualValue">The value of the snapshot of the first invocation, when none of them matches.</param>
    private static bool TryFindUniqueMatch(CallerContext context, IEnumerable<InvocationExpressionSyntax> invocations, string? value, out (InvocationExpressionSyntax Invocation, ExpressionSyntax? Argument) match, out string? firstActualValue)
    {
        match = default;
        firstActualValue = null;
        var found = false;
        foreach (var invocation in invocations)
        {
            if (TryMatchArgument(context, invocation, value, out var argument, out var actualValue))
            {
                if (found)
                    throw new InlineSnapshotException("The SyntaxNode to update is ambiguous");

                match = (invocation, argument);
                found = true;
            }
            else if (!found)
            {
                firstActualValue ??= actualValue;
            }
        }

        return found;
    }

    /// <summary>Returns the outermost node starting where the PDB sequence point of the call starts, typically the statement.</summary>
    private static SyntaxNode? TryGetSequencePointNode(CallerContext context, SourceText sourceText, SyntaxNode root)
    {
        if (context.SequencePointLineNumber <= 0 || context.SequencePointColumnNumber <= 0)
            return null;

        var lineIndex = GetActualLine(context.FilePath, context.SequencePointLineNumber) - 1;
        if (lineIndex < 0 || lineIndex >= sourceText.Lines.Count)
            return null;

        var line = sourceText.Lines[lineIndex];
        var position = line.Start + context.SequencePointColumnNumber - 1;
        if (position >= line.End)
            return null;

        var token = root.FindToken(position);
        if (token.SpanStart != position)
            return null;

        SyntaxNode? result = null;
        for (var node = token.Parent; node is not null && node.SpanStart == position && node is not CompilationUnitSyntax; node = node.Parent)
        {
            result = node;
        }

        return result;
    }

    private static bool IsInCallerMember(CallerContext context, SyntaxNode node)
    {
        if (context.CallerMemberName is null)
            return true;

        foreach (var ancestor in node.Ancestors())
        {
            var isCallerMember = ancestor switch
            {
                MethodDeclarationSyntax method => method.Identifier.ValueText == context.CallerMemberName,
                LocalFunctionStatementSyntax localFunction => localFunction.Identifier.ValueText == context.CallerMemberName,
                PropertyDeclarationSyntax property => IsAccessorOf(property.Identifier.ValueText, context.CallerMemberName),
                EventDeclarationSyntax @event => IsAccessorOf(@event.Identifier.ValueText, context.CallerMemberName),
                _ => false,
            };

            if (isCallerMember)
                return true;
        }

        return false;

        static bool IsAccessorOf(string memberName, string methodName)
        {
            var separator = methodName.IndexOf('_', StringComparison.Ordinal);
            return separator > 0 && methodName.AsSpan(separator + 1).SequenceEqual(memberName);
        }
    }

    private static bool IsInvocationOf(InvocationExpressionSyntax invocation, string methodName)
    {
        var token = GetMethodNameToken(invocation);
        return token.ValueText == methodName;
    }

    private static SyntaxToken GetMethodNameToken(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            // Dummy.MethodName(), Dummy.MethodName<T>()
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier,

            // Dummy?.MethodName()
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier,

            // MethodName(), MethodName<T>()
            SimpleNameSyntax name => name.Identifier,
            _ => default,
        };
    }

    /// <summary>
    /// Returns the argument indexes that may hold the snapshot, the most likely first. An extension method called with
    /// the extension syntax does not receive its first parameter as an argument. Whether the receiver is a value or the
    /// type declaring the method cannot be told without a semantic model, so both indexes are tried.
    /// </summary>
    private static int[] GetArgumentIndexes(CallerContext context, InvocationExpressionSyntax invocation)
    {
        if (!context.IsExtensionMethod || context.ParameterIndex == 0)
            return [context.ParameterIndex];

        var isExtensionSyntax = invocation.Expression switch
        {
            MemberBindingExpressionSyntax => true,
            MemberAccessExpressionSyntax memberAccess => !IsReferenceToDeclaringType(memberAccess.Expression, context.DeclaringTypeName),
            _ => false,
        };

        return isExtensionSyntax ? [context.ParameterIndex - 1, context.ParameterIndex] : [context.ParameterIndex, context.ParameterIndex - 1];

        static bool IsReferenceToDeclaringType(ExpressionSyntax expression, string? typeName)
        {
            return expression switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText == typeName,
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText == typeName,
                AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText == typeName,
                _ => false,
            };
        }
    }

    /// <summary>Finds the argument holding the snapshot and reports whether it matches the value the method received.</summary>
    /// <param name="argumentExpression">The argument holding the snapshot, or <see langword="null"/> when the argument is omitted.</param>
    private static bool TryMatchArgument(CallerContext context, InvocationExpressionSyntax invocation, string? existingValue, out ExpressionSyntax? argumentExpression, out string? actualValue)
    {
        var arguments = invocation.ArgumentList.Arguments;
        foreach (var argument in arguments)
        {
            if (argument.NameColon is { Name.Identifier.ValueText: var name } && name == context.ParameterName)
            {
                argumentExpression = argument.Expression;
                return ExpressionSyntaxMatchesValue(argument.Expression, existingValue, out actualValue);
            }
        }

        argumentExpression = null;
        actualValue = null;
        var isMostLikelyIndex = true;
        foreach (var index in GetArgumentIndexes(context, invocation))
        {
            if (index < arguments.Count && arguments[index].NameColon is null)
            {
                if (ExpressionSyntaxMatchesValue(arguments[index].Expression, existingValue, out var value))
                {
                    argumentExpression = arguments[index].Expression;
                    actualValue = value;
                    return true;
                }

                if (isMostLikelyIndex)
                {
                    actualValue = value;
                }
            }
            else if (existingValue is null || existingValue == context.ParameterDefaultValue)
            {
                // The argument is omitted, so the method received the default value of the parameter
                actualValue = existingValue;
                return true;
            }

            isMostLikelyIndex = false;
        }

        return false;
    }

    private static ArgumentSyntax CreateArgument(CallerContext context, InvocationExpressionSyntax invocation, ExpressionSyntax expression)
    {
        var arguments = invocation.ArgumentList.Arguments;
        var argument = SyntaxFactory.Argument(expression);

        // A positional argument only binds to the snapshot parameter when every parameter before it has an argument.
        // Otherwise, it would silently bind to another optional parameter.
        if (GetArgumentIndexes(context, invocation)[0] != arguments.Count || arguments.Any(existingArgument => existingArgument.NameColon is not null))
        {
            argument = argument.WithNameColon(SyntaxFactory.NameColon(SyntaxFactory.IdentifierName(context.ParameterName)).WithTrailingTrivia(SyntaxFactory.Space));
        }

        return arguments.Count > 0 ? argument.WithLeadingTrivia(SyntaxFactory.Space) : argument;
    }

    private static int GetStartPosition(InvocationExpressionSyntax invocationExpression)
    {
        var line = invocationExpression.Expression.GetLocation().GetLineSpan().EndLinePosition.Line;
        var lineText = invocationExpression.SyntaxTree.GetText().Lines[line].ToString();
        return lineText.Length - lineText.AsSpan().TrimStart().Length;
    }

    private static bool ExpressionSyntaxMatchesValue(ExpressionSyntax? expression, string? value, out string? actualValue)
    {
        if (expression is not null)
        {
            if (expression.IsKind(SyntaxKind.NullLiteralExpression))
            {
                actualValue = null;
                return actualValue == value;
            }
            else if (expression.IsKind(SyntaxKind.StringLiteralExpression))
            {
                var literalExpression = (LiteralExpressionSyntax)expression;
                actualValue = (string?)literalExpression.Token.Value;
                return actualValue == value;
            }
        }

        actualValue = null;
        return false;
    }

    internal static string DetectIndentation(SourceText sourceText)
    {
        foreach (var line in sourceText.Lines)
        {
            // A text ending with a line break has a trailing empty line whose Start is past the last character.
            if (line.Start == line.End)
                continue;

            if (sourceText[line.Start] is (' ' or '\t') and var space)
            {
                for (var i = line.Start + 1; i < line.End; i++)
                {
                    var c = sourceText[i];
                    if (c != space)
                        return sourceText.GetSubText(new TextSpan(line.Start, i - line.Start)).ToString();
                }
            }
        }

        // Fallback
        return "    ";
    }

    internal static string DetectEndOfLine(SourceText sourceText)
    {
        foreach (var line in sourceText.Lines)
        {
            var span = line.SpanIncludingLineBreak;
            if (span.Length == 0)
                continue;

            if (span.Length >= 2 && sourceText[span.End - 2] == '\r' && sourceText[span.End - 1] == '\n')
                return "\r\n";

            if (span.Length >= 1 && sourceText[span.End - 1] == '\n')
                return "\n";
        }

        return Environment.NewLine;
    }

    private sealed record FileEdit(int StartLine, int BeforeLineSpan, int AfterLineSpan);
}
