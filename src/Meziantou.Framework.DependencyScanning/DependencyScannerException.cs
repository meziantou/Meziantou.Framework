using System.Runtime.Serialization;

namespace Meziantou.Framework.DependencyScanning
{
    public class DependencyScannerException : Exception
    {
        public DependencyScannerException()
        {
        }

        public DependencyScannerException(string? message) : base(message)
        {
        }

        public DependencyScannerException(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        protected DependencyScannerException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
