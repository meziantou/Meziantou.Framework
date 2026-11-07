#if !MEZIANTOU_FRAMEWORK_ROSLYN_ENABLE_WARNINGS
#pragma warning disable
#endif
#nullable enable
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.Roslyn;

/// <summary>
/// Decides whether a diagnostic can be reported by a <see cref="DiagnosticReporter"/>.
/// </summary>
/// <param name="diagnostic">The diagnostic about to be reported. <c>diagnostic.Descriptor</c> and <c>diagnostic.Location.SourceTree</c> give access to the descriptor and the syntax tree.</param>
/// <param name="options">The analyzer options of the context the diagnostic is reported from.</param>
/// <param name="cancellationToken">The cancellation token of the context the diagnostic is reported from.</param>
/// <returns><see langword="true"/> to report the diagnostic; <see langword="false"/> to drop it.</returns>
#if !MEZIANTOU_FRAMEWORK_ROSLYN_DISABLE_EMBEDDEDATTRIBUTE
[Microsoft.CodeAnalysis.Embedded]
#endif
internal delegate bool DiagnosticFilter(Diagnostic diagnostic, AnalyzerOptions options, CancellationToken cancellationToken);
