namespace Meziantou.Framework.DependencyScanning
{
    public interface ILocationLineInfo
    {
        /// <summary>
        /// The current line number or 0 if no line information is available.
        /// </summary>
        /// <remarks>This property is used primarily for error reporting, but can be called at any time. The starting value is 1. Combined with LinePosition, a value of 1,1 indicates the start of a document.</remarks>
        public int LineNumber { get; }

        /// <summary>
        /// The current line position or 0 if no line information is available.
        /// </summary>
        /// <remarks>This property is used primarily for error reporting, but can be called at any time. The starting value is 1. Combined with LineNumber, a value of 1,1 indicates the start of a document.</remarks>
        public int LinePosition { get; }
    }
}
