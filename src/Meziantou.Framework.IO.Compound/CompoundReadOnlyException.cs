using System;
using System.Runtime.Serialization;

namespace Meziantou.Framework.IO.Compound
{
    /// <summary>
    /// The exception that is thrown when a compound read only error occurs.
    /// </summary>
    [Serializable]
    public sealed class CompoundReadOnlyException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CompoundReadOnlyException"/> class.
        /// </summary>
        public CompoundReadOnlyException()
            : this("Collection is marked as read only")
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompoundReadOnlyException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        public CompoundReadOnlyException(string? message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompoundReadOnlyException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The inner exception.</param>
        public CompoundReadOnlyException(string? message, Exception? innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompoundReadOnlyException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="T:System.Runtime.Serialization.SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="T:System.Runtime.Serialization.StreamingContext"/> that contains contextual information about the source or destination.</param>
        private CompoundReadOnlyException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
