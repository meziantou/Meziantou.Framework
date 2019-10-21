#nullable disable
using System;
using System.Runtime.Serialization;

namespace Meziantou.Framework.IO.Compound
{
    /// <summary>
    /// The exception that is thrown when a compound file error occurs.
    /// </summary>
    [Serializable]
    public class CompoundFileException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CompoundFileException"/> class.
        /// </summary>
        public CompoundFileException()
            : this("Compound File Exception")
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompoundFileException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        public CompoundFileException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompoundFileException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The inner exception.</param>
        public CompoundFileException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompoundFileException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="T:System.Runtime.Serialization.SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="T:System.Runtime.Serialization.StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected CompoundFileException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
