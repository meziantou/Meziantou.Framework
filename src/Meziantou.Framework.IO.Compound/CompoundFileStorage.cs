using System;
using System.Collections.Generic;

namespace Meziantou.Framework.IO.Compound
{
    /// <summary>
    /// Defines a storage container in a compound file.
    /// </summary>
    public sealed class CompoundFileStorage
    {
        internal List<CompoundFileStream> _streams = new List<CompoundFileStream>();
        internal CompoundFile.DirectoryStorage _entry;
        private readonly CompoundFile _file;
        internal readonly List<CompoundFileStorage> _storages = new List<CompoundFileStorage>();

        internal CompoundFileStorage(CompoundFile file, CompoundFileStorage parent, CompoundFile.DirectoryStorage entry, bool isRoot)
        {
            Parent = parent;
            _file = file;
            _entry = entry;
            IsRoot = isRoot;
        }

        /// <summary>
        /// Gets the parent file.
        /// </summary>
        public CompoundFileStorage Parent { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this instance is the root storage.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is the root storage; otherwise, <c>false</c>.
        /// </value>
        public bool IsRoot { get; private set; }

        /// <summary>
        /// Returns a <see cref="string"/> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string"/> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return Name;
        }

        /// <summary>
        /// Gets the storage path.
        /// </summary>
        public string Path
        {
            get
            {
                if (Parent == null)
                    return Name;

                return Parent.Path + @"\" + Name;
            }
        }

        /// <summary>
        /// Gets the storage name.
        /// </summary>
        public string Name
        {
            get
            {
                if (_entry == null)
                    return null;

                return _entry.Name;
            }
        }

        /// <summary>
        /// Gets a child storage by its name.
        /// </summary>
        /// <param name="name">The name. May not be null.</param>
        /// <returns>An instance of the CompoundFileStorage class or null if not found.</returns>
        public CompoundFileStorage GetChildStorage(string name)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            return _storages.Find(s => string.Compare(s.Name, name, StringComparison.OrdinalIgnoreCase) == 0);
        }

        /// <summary>
        /// Gets a child storage by its name or create a new one if not found.
        /// </summary>
        /// <param name="name">The name. May not be null.</param>
        /// <returns>An instance of the CompoundFileStorage class.</returns>
        public CompoundFileStorage GetOrAddChildStorage(string name)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            var storage = GetChildStorage(name);
            if (storage != null)
                return storage;

            storage = _file.AddStorage(this, name);
            _storages.Add(storage);
            return storage;
        }

        /// <summary>
        /// Gets a child stream by its name.
        /// </summary>
        /// <param name="name">The name. May not be null.</param>
        /// <returns>
        /// An instance of the CompoundFileStream class or null if not found.
        /// </returns>
        public CompoundFileStream GetChildStream(string name)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            return _streams.Find(s => string.Compare(s.Name, name, StringComparison.OrdinalIgnoreCase) == 0);
        }

        /// <summary>
        /// Gets a child stream by its name or create a new one if not found.
        /// </summary>
        /// <param name="name">The name. May not be null.</param>
        /// <returns>
        /// An instance of the CompoundFileStream class.
        /// </returns>
        public CompoundFileStream GetOrAddChildStream(string name)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            var stream = GetChildStream(name);
            if (stream != null)
                return stream;

            stream = _file.AddStream(this, name);
            _streams.Add(stream);
            return stream;
        }

        /// <summary>
        /// Gets a list of child storages.
        /// </summary>
        public IEnumerable<CompoundFileStorage> ChildStorages => _storages;

        /// <summary>
        /// Gets a list of child streams.
        /// </summary>
        public IEnumerable<CompoundFileStream> ChildStreams => _streams;

        internal void Dispose()
        {
            foreach (var stream in _streams.ToArray())
            {
                stream.Dispose();
                _streams.Remove(stream);
            }

            foreach (var storage in _storages)
            {
                storage.Dispose();
            }
        }
    }
}
