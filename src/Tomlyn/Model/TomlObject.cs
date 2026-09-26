using System;
using System.Globalization;

namespace Tomlyn.Model
{
    /// <summary>
    /// Base class for the runtime representation of a TOML object
    /// </summary>
    public abstract class TomlObject
    {
        protected TomlObject(ObjectKind kind)
        {
            Kind = kind;
        }

        /// <summary>
        /// The kind of the object
        /// </summary>
        public ObjectKind Kind { get; }
    }
}