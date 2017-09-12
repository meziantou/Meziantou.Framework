using System;
using System.Collections.Generic;

namespace Meziantou.Framework.CodeDom
{
    public abstract class CodeObject
    {
        public IDictionary<string, object> Data { get; } = new Dictionary<string, object>();

        public void SetData(string key, object value)
        {
            Data[key] = value;
        }
        
        public CodeObject Parent { get; internal set; }

        protected T SetParent<T>(T value) where T : CodeObject
        {
            return SetParent(this, value);
        }

        protected T SetParent<T>(CodeObject parent, T value) where T : CodeObject
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));

            if (value == null)
                return null;

            if (value.Parent != null && value.Parent != parent)
            {
                throw new ArgumentException("Object already has a parent.", nameof(value));
            }

            value.Parent = parent;
            return value;
        }
    }
}