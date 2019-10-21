#nullable disable
using System.IO;

namespace Meziantou.Framework.IO.Compound
{
    /// <summary>
    /// Defines a stream in a compound file.
    /// </summary>
    public sealed class CompoundFileStream
    {
        internal CompoundFile.DirectoryEntry _entry;
        private MemoryStream _stream;

        internal CompoundFileStream(CompoundFileStorage parent, CompoundFile.DirectoryEntry entry)
        {
            Parent = parent;
            _entry = entry;
            _stream = new MemoryStream();
        }

        /// <summary>
        /// Gets the parent file.
        /// </summary>
        public CompoundFileStorage Parent { get; private set; }

        /// <summary>
        /// Gets the stream path.
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

        internal int WriteOffset(Stream output, long offset, int size)
        {
            _stream.Position = offset;
            var bytes = new byte[size];
            int read = _stream.Read(bytes, 0, bytes.Length);
            if (read > 0)
            {
                output.Write(bytes, 0, read);
            }

            return read;
        }

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
        /// Gets the stream name.
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
        /// Gets the stream length.
        /// </summary>
        public long Length
        {
            get
            {
                if (_stream == null)
                    return 0;

                return _stream.Length;
            }
        }

        /// <summary>
        /// Gets the physical underlying stream.
        /// </summary>
        public Stream Stream => _stream;

        internal void Dispose()
        {
            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }
        }
    }
}
