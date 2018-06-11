using System;
using System.Collections.Generic;

namespace Meziantou.Framework.Utilities
{
    public class ProcessResult
    {
        public ProcessResult(int exitCode, IReadOnlyList<ProcessOutput> output)
        {
            ExitCode = exitCode;
            Output = output ?? throw new ArgumentNullException(nameof(output));
        }

        public int ExitCode { get; }
        public IReadOnlyList<ProcessOutput> Output { get; }
    }
}
