using System;
using System.Collections.Generic;

namespace Meziantou.Framework
{
    public class ProcessResult
    {
        internal ProcessResult(int exitCode, IReadOnlyList<ProcessOutput> output)
        {
            ExitCode = exitCode;
            Output = output ?? throw new ArgumentNullException(nameof(output));
        }

        public int ExitCode { get; }
        public IReadOnlyList<ProcessOutput> Output { get; }
    }
}
