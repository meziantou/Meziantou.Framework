// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

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