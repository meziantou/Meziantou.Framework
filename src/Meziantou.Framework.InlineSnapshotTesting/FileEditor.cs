using System.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis;
using System.Collections.Concurrent;
using Meziantou.Framework.InlineSnapshotTesting.Utils;

namespace Meziantou.Framework.InlineSnapshotTesting;

internal static class FileEditor
{
    private static readonly ConcurrentDictionary<string, object> FileLocks = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> TempFiles = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, List<FileEdit>> Changes = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Errors = new(StringComparer.Ordinal);

    private static int GetActualLine(string fullPath, int startLine)
    {
        if (Changes.TryGetValue(fullPath, out var edits))
        {
            var diff = 0;
            // Edits are ordered by BeforeLine
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

    private static SourceText GetSourceText(InlineSnapshotSettings settings, string tempPath)
    {
        using var fs = File.OpenRead(tempPath);
        using var stream = fs.CanRead ? fs : CopyToMemoryStream(fs);
        return SourceText.From(stream, settings.FileEncoding);

        static Stream CopyToMemoryStream(Stream stream)
        {
            var ms = new MemoryStream();
            try
            {
                stream.CopyTo(ms);
                stream.Seek(0, SeekOrigin.Begin);
                return ms;
            }
            catch
            {
                ms.Dispose();
                throw;
            }
        }
    }

    public static void UpdateFile(CallerContext context, InlineSnapshotSettings settings, string? existingValue, string? newValue)
    {
        var lockObject = FileLocks.GetOrAdd(context.FilePath, _ => new object());
        lock (lockObject)
        {
            if (Errors.Contains(context.FilePath))
                throw new InlineSnapshotException("The previous merged cannot be resolved. Restart the tests to update this snapshot.");

            var tempPath = TempFiles.GetOrAdd(context.FilePath, _ => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cs"));

            var options = new CSharpParseOptions();
            var sourceText = GetSourceText(settings, File.Exists(tempPath) ? tempPath : context.FilePath);
            var tree = CSharpSyntaxTree.ParseText(sourceText);
            var root = tree.GetRoot();

            var actualLine = GetActualLine(context.FilePath, context.LineNumber);
            var span = sourceText.Lines[actualLine - 1].Span;
            if (context.ColumnNumber > 0)
                span = new TextSpan(span.Start + context.ColumnNumber, 1);

            var nodes = root.DescendantNodesAndSelf(span)
                .OfType<InvocationExpressionSyntax>()
                .Where(invocation =>
                {
                    // Dummy.MethodName()
                    if (invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: string memberName } && memberName == context.MethodName)
                        return true;

                    // MethodName()
                    if (invocation.Expression is IdentifierNameSyntax { Identifier.Text: string identifierName } && identifierName == context.MethodName)
                        return true;

                    return false;
                })
                .ToArray();

            if (nodes.Length == 0)
                throw new InlineSnapshotException("Cannot find the SyntaxNode to update");

            if (nodes.Length > 1)
                throw new InlineSnapshotException("The SyntaxNode to update is ambiguous");

            var invocationExpression = nodes[0];
            var argumentExpression = FindArgumentExpression(context, invocationExpression.ArgumentList.Arguments, existingValue);

            var indentation = settings.Indentation ?? DetectIndentation(sourceText);
            var eol = settings.EndOfLine ?? DetectEndOfLine(sourceText);
            var startPosition = invocationExpression.GetLocation().GetMappedLineSpan().StartLinePosition.Character;

            var formattedValue = CSharpStringLiteral.Create(newValue, context.FilterFormats(settings.AllowedStringFormats), indentation, startPosition, eol);
            var newArgumentExpression = SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(formattedValue, newValue))
                .WithLeadingTrivia(argumentExpression.GetLeadingTrivia())
                .WithTrailingTrivia(argumentExpression.GetTrailingTrivia());
            var newRoot = root.ReplaceNode(argumentExpression, newArgumentExpression);

            // Save the file
            // Create a temp file, show diff if needed, Move or let the tool update the file
            var encoding = settings.FileEncoding ?? sourceText.Encoding ?? DetectEncoding(context) ?? Encoding.UTF8;

            Directory.CreateDirectory(Path.GetDirectoryName(tempPath));
            using (var outputStream = File.OpenWrite(tempPath))
            using (var textWriter = new StreamWriter(outputStream, encoding))
            {
                newRoot.WriteTo(textWriter);
            }

            settings.SnapshotUpdateStrategy.UpdateFile(settings, context.FilePath, tempPath);

            // Track the changes
            // note: Diff tools allow partial merge or custom edits => we need to reload the document to find the new expression
            // note: Diff tools allow to remove the temp path
            var mergedSourceText = GetSourceText(settings, File.Exists(tempPath) ? tempPath : context.FilePath);
            var mergedTree = CSharpSyntaxTree.ParseText(mergedSourceText);
            var mergedRoot = mergedTree.GetRoot();
            var potentialMergedExpressions = mergedRoot.DescendantNodesAndSelf(new TextSpan(argumentExpression.SpanStart, 1)).OfType<LiteralExpressionSyntax>().ToArray();
            if (potentialMergedExpressions.Length != 1)
            {
                Errors.Add(context.FilePath);
                return;
            }

            var argumentSpan = argumentExpression.GetLocation().GetMappedLineSpan();
            var newArgumentSpan = potentialMergedExpressions[0].GetLocation().GetMappedLineSpan();

            var fileEdit = new FileEdit(
                context.LineNumber,
                argumentSpan.EndLinePosition.Line - argumentSpan.StartLinePosition.Line,
                newArgumentSpan.EndLinePosition.Line - newArgumentSpan.StartLinePosition.Line);
            if (!Changes.TryGetValue(context.FilePath, out var fileEdits))
            {
                fileEdits = new List<FileEdit>();
                Changes.Add(context.FilePath, fileEdits);
            }

            fileEdits.Add(fileEdit);
            fileEdits.Sort((a, b) => a.StartLine - b.StartLine);
        }
    }

    private static ExpressionSyntax FindArgumentExpression(CallerContext context, SeparatedSyntaxList<ArgumentSyntax> arguments, string? existingValue)
    {
        // Try find by name
        ExpressionSyntax? argumentExpression = null;
        if (context.ParameterName != null)
        {
            foreach (var argument in arguments)
            {
                if (argument.NameColon is { Name.Identifier.Text: var identifier } && identifier == context.ParameterName)
                {
                    argumentExpression = argument.Expression;
                    break;
                }
            }
        }

        if (argumentExpression == null && context.ParameterIndex >= 0 && context.ParameterIndex < arguments.Count)
        {
            argumentExpression = arguments[context.ParameterIndex].Expression;
        }

        argumentExpression ??= FindSingleArgumentMatchingValue(arguments, existingValue);
        if (argumentExpression == null)
            throw new InlineSnapshotException("Cannot find the argument to update");

        if (!ExpressionSyntaxMatchesValue(argumentExpression, existingValue, out var actualValue))
            throw new InlineSnapshotException($"Cannot find the argument to update. The current value doesn't match the expected value.\nExpected: <{existingValue}>\nActual: <{actualValue}>");

        return argumentExpression;
    }

    private static ExpressionSyntax? FindSingleArgumentMatchingValue(SeparatedSyntaxList<ArgumentSyntax> arguments, string value)
    {
        foreach (var argument in arguments)
        {
            if (ExpressionSyntaxMatchesValue(argument.Expression, value, out _))
                return argument.Expression;
        }

        return null;
    }

    private static bool ExpressionSyntaxMatchesValue(ExpressionSyntax? expression, string? value, out string? actualValue)
    {
        if (expression != null)
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

    internal static Encoding? DetectEncoding(CallerContext context)
    {
        using var fs = File.OpenRead(context.FilePath);
        var data = new byte[4];
        var count = fs.ReadAtLeast(data, minimumBytes: 4, throwOnEndOfStream: false);
        var readData = data.AsSpan()[..count];

        if (readData.Length < 2)
            return null;

        if (readData[0] == 0xff && readData[1] == 0xfe && (readData.Length < 4 || readData[2] != 0 || readData[3] != 0))
            return Encoding.Unicode;

        if (readData[0] == 0xfe && readData[1] == 0xff)
            return Encoding.BigEndianUnicode;

        if (readData.Length < 3)
            return null;

        if (readData[0] == 0xef && readData[1] == 0xbb && readData[2] == 0xbf)
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

#pragma warning disable SYSLIB0001 // Type or member is obsolete
        if (readData[0] == 0x2b && readData[1] == 0x2f && readData[2] == 0x76)
            return Encoding.UTF7;
#pragma warning restore SYSLIB0001

        if (readData.Length < 4)
            return null;

        if (readData[0] == 0xff && readData[1] == 0xfe && readData[2] == 0 && readData[3] == 0)
            return Encoding.UTF32;

        if (readData[0] == 0 && readData[1] == 0 && readData[2] == 0xfe && readData[3] == 0xff)
            return Encoding.GetEncoding(12001);

        return null;
    }

    internal static string DetectIndentation(SourceText sourceText)
    {
        foreach (var line in sourceText.Lines)
        {
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
