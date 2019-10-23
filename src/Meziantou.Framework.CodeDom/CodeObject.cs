﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.CodeDom
{
    public abstract class CodeObject
    {
        public IDictionary<string, object?> Data { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

        public void SetData(string key, object? value)
        {
            Data[key] = value;
        }

        public CodeObject? Parent { get; internal set; }

        protected void SetParent<T>(ref T? field, T? value)
            where T : CodeObject
        {
            SetParent(this, ref field, value);
        }

        protected static void SetParent<T>(CodeObject parent, ref T? field, T? value) where T : CodeObject
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            if (field != null)
            {
                field.Parent = null; // Detach previous value
            }

            if (value != null)
            {
                if (value.Parent != null && value.Parent != parent)
                    throw new ArgumentException("Object already has a parent.", nameof(value));

                value.Parent = parent;
            }

            field = value;
        }

        protected T SetParent<T>(T? value) where T : CodeObject
        {
            return SetParent(this, value);
        }

        protected static T SetParent<T>(CodeObject parent, T? value) where T : CodeObject
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            if (value?.Parent != parent)
                throw new ArgumentException("Object already has a parent.", nameof(value));

            value.Parent = parent;
            return value;
        }
    }
}
