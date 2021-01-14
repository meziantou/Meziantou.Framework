using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Meziantou.Framework
{
    public sealed class ProcessOutputCollection : IReadOnlyList<ProcessOutput>
    {
        private readonly IReadOnlyList<ProcessOutput> _output;

        internal ProcessOutputCollection(IReadOnlyList<ProcessOutput> output)
        {
            _output = output;
        }

        public int Count => _output.Count;
        public ProcessOutput this[int index] => _output[index];

        public IEnumerator<ProcessOutput> GetEnumerator() => _output.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString()
        {
            var sb = new StringBuilder();
            foreach (var item in _output)
            {
                sb.Append(item).AppendLine();
            }

            return sb.ToString();
        }
    }
}
