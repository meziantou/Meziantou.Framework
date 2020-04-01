using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Meziantou.Framework.IO.Compound
{
    /// <summary>
    /// A utility class to read and write Compound File. See [MS-CFB]: Compound File Binary File Format for more information at msdn.microsoft.com/en-us/library/dd942138.aspx.
    /// </summary>
    public sealed class CompoundFile : IDisposable
    {
        /// <summary>
        /// Defines the SummaryInformation FMTID.
        /// </summary>
        public static readonly Guid SummaryInformationFormatId = new Guid("{F29F85E0-4FF9-1068-AB91-08002B27B3D9}");

        /// <summary>
        /// Defines the SummaryInformation stream name.
        /// </summary>
        public static readonly string SummaryInformationStreamName = "\x0005SummaryInformation";

        /// <summary>
        /// Defines the DocSummaryInformation FMTID.
        /// </summary>
        public static readonly Guid DocSummaryInformationFormatId = new Guid("{D5CDD502-2E9C-101B-9397-08002B2CF9AE}");

        /// <summary>
        /// Defines the DocSummaryInformation stream name.
        /// </summary>
        public static readonly string DocSummaryInformationStreamName = "\x0005DocumentSummaryInformation";

        private static readonly byte[] s_signature = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
        private const int PIDSI_TITLE = 0x00000002;  // VT_LPSTR
        private const int PIDSI_SUBJECT = 0x00000003;  // VT_LPSTR
        private const int PIDSI_AUTHOR = 0x00000004;  // VT_LPSTR
        private const int PIDSI_KEYWORDS = 0x00000005;  // VT_LPSTR
        private const int PIDSI_COMMENTS = 0x00000006;  // VT_LPSTR
        private const int PIDDSI_COMPANY = 0x0000000F; // VT_LPSTR

        private const uint DIFSECT = 0xFFFFFFFC;
        private const uint FATSECT = 0xFFFFFFFD;
        private const uint ENDOFCHAIN = 0xFFFFFFFE;
        private const uint FREESECT = 0xFFFFFFFF;
        private const uint NOSTREAM = 0xFFFFFFFF;

        // specific to us
        private const uint AUTOINDEX = 0XFFFFFFFF;

        // we only define what we handle
        private enum VARTYPE : ushort
        {
            VT_NULL = 1,
            VT_I2 = 2,
            VT_I4 = 3,
            VT_R4 = 4,
            VT_R8 = 5,
            VT_CY = 6,
            VT_DATE = 7,
            //VT_BSTR = 8,
            //VT_DISPATCH = 9,
            VT_ERROR = 10,
            VT_BOOL = 11,
            //VT_UNKNOWN = 13,
            VT_DECIMAL = 14,
            VT_I1 = 16,
            VT_UI1 = 17,
            VT_UI2 = 18,
            VT_UI4 = 19,
            VT_I8 = 20,
            VT_UI8 = 21,
            VT_INT = 22,
            VT_UINT = 23,
            VT_HRESULT = 25,
            VT_LPSTR = 30,
            //VT_LPWSTR = 31,
            VT_FILETIME = 64,
        }

        private const string RootEntryName = "Root Entry";

        /// <summary>
        /// Defines the number of integrated DiFat sectors.
        /// </summary>
        public const int DiFatHeaderCount = 109;

        /// <summary>
        /// Defines the number of directory entries per sector.
        /// </summary>
        public const int DirectoryEntriesPerSector = 4;

        /// <summary>
        /// Defines the file header size.
        /// </summary>
        public const int HeaderSize = 512;

        /// <summary>
        /// Gets the mini stream cutoff value.
        /// </summary>
        public int MiniStreamCutoff { get; private set; }

        /// <summary>
        /// Gets the sector size.
        /// </summary>
        /// <value>
        /// The sector size.
        /// </value>
        public int SectorSize { get; private set; }

        /// <summary>
        /// Gets the mini sector size.
        /// </summary>
        /// <value>
        /// The the mini sector size.
        /// </value>
        public int MiniSectorSize { get; private set; }

        /// <summary>
        /// Gets the number or fat entries per sector.
        /// </summary>
        public int FatEntriesPerSector => SectorSize / 4;

        // represents physical layout of sectors in file
        private readonly Dictionary<uint, SectorRef> _sectors = new Dictionary<uint, SectorRef>();

        // list per type
        private readonly List<StorageSector> _storageSectors = new List<StorageSector>();
        private readonly List<MiniFatSector> _miniFatSectors = new List<MiniFatSector>();
        private readonly List<DiFatSector> _diFatSectors = new List<DiFatSector>();
        private readonly List<MiniStreamSector> _miniStreamSectors = new List<MiniStreamSector>();
        private readonly List<DirectorySector> _directorySectors = new List<DirectorySector>();
        private readonly List<FatSector> _fatSectors = new List<FatSector>(); // represent the difat array

        // directory entries
        private readonly List<DirectoryEntry> _entries = new List<DirectoryEntry>();

        // OLE-PS properties
        private readonly Dictionary<Guid, Dictionary<int, object?>> _properties = new Dictionary<Guid, Dictionary<int, object?>>();

        // write variables
        private CompoundFileStorage? _rootStorage;
        private uint _miniStreamNextId;
        private DirectoryStorage? _rootEntry;

        // read variables
        private int _fatSectorsCount;
        private uint _firstDirectorySectorId;
        private uint _firstMiniFatSectorId;
        private int _miniFatSectorsCount;
        private uint _firstDiFatSectorId;
        private int _diFatSectorsCount;

        /// <summary>
        /// Loads the file from the specified input stream.
        /// </summary>
        /// <param name="inputStream">The input stream. May not be null.</param>
        public void Load(Stream inputStream)
        {
            if (inputStream == null)
                throw new ObjectDisposedException(nameof(inputStream));

            var reader = new BinaryReader(inputStream);
            Load(reader);
        }

        /// <summary>
        /// Loads the file from the specified file path.
        /// </summary>
        /// <param name="filePath">The file path. May not be null.</param>
        public void Load(string filePath)
        {
            if (filePath == null)
                throw new ObjectDisposedException(nameof(filePath));

            using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
            Load(file);
        }

        private void AddDirectoryEntry(DirectoryEntry entry)
        {
            var lastDir = _directorySectors.LastOrDefault();
            if (lastDir == null || lastDir.Entries.Count >= DirectoryEntriesPerSector)
            {
                lastDir = new DirectorySector(this);
                AddPhysicalSector(lastDir, AUTOINDEX);
            }
            lastDir.Entries.Add(entry);

            entry.Index = (uint)_entries.Count;
            _entries.Add(entry);
        }

        private void AddPhysicalSector(Sector sector, uint physicalIndex)
        {
            int index;
            if (sector is FatSector fatSector)
            {
                index = _fatSectors.Count;
                _fatSectors.Add(fatSector);
            }
            else if (sector is StorageSector storageSector)
            {
                index = _storageSectors.Count;
                _storageSectors.Add(storageSector);
            }
            else if (sector is MiniFatSector miniFatSector)
            {
                index = _miniFatSectors.Count;
                _miniFatSectors.Add(miniFatSector);
            }
            else if (sector is MiniStreamSector miniStreamSector)
            {
                index = _miniStreamSectors.Count;
                _miniStreamSectors.Add(miniStreamSector);
            }
            else if (sector is DirectorySector directorySector)
            {
                index = _directorySectors.Count;
                _directorySectors.Add(directorySector);
            }
            else if (sector is DiFatSector diFatSector)
            {
                index = _diFatSectors.Count;
                _diFatSectors.Add(diFatSector);
            }
            else
            {
                throw new NotSupportedException();
            }

            sector.Index = index;

            if (physicalIndex == AUTOINDEX) // save mode
                physicalIndex = (uint)_sectors.Count;

            sector.PhysicalIndex = physicalIndex;
            var sr = new SectorRef(sector.SectorType, index);
            _sectors.Add(physicalIndex, sr);
        }

        /// <summary>
        /// Saves the file to specified file path.
        /// </summary>
        /// <param name="filePath">The file path. May not be null.</param>
        public void Save(string filePath)
        {
            if (filePath == null)
                throw new ObjectDisposedException("filePath");

            using var file = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            Save(file);
        }

        /// <summary>
        /// Saves the file to the specified output stream.
        /// </summary>
        /// <param name="outputStream">The output stream. May not be null.</param>
        public void Save(Stream outputStream)
        {
            if (outputStream == null)
                throw new ObjectDisposedException("outputStream");

            _sectors.Clear();
            _fatSectors.Clear();
            _miniFatSectors.Clear();
            _miniStreamSectors.Clear();
            _diFatSectors.Clear();
            _directorySectors.Clear();

            if (SectorSize == 0)
                SectorSize = 512;

            if (MiniSectorSize == 0)
                MiniSectorSize = 64;

            if (MiniStreamCutoff == 0)
                MiniStreamCutoff = 4096;

            if (_rootEntry == null)
            {
                _rootEntry = new DirectoryStorage(RootEntryName) { ObjectType = DirectoryObjectType.RootStorage };
                AddDirectoryEntry(_rootEntry);
                _rootStorage = new CompoundFileStorage(this, parent: null, _rootEntry, isRoot: true);
            }
            else
            {
                foreach (var entry in _entries)
                {
                    var sector = _directorySectors.LastOrDefault();
                    if (sector == null || sector.Entries.Count >= DirectoryEntriesPerSector)
                    {
                        sector = new DirectorySector(this);
                        AddPhysicalSector(sector, AUTOINDEX);
                    }
                    sector.Entries.Add(entry);
                }
            }

            // add the PS streams
            WritePropertySets();

            // build sectors
            _miniStreamNextId = 0;
            AddStreamSectors(RootStorage);

            // add ministream info
            var miniStream = _miniStreamSectors.FirstOrDefault();
            if (miniStream != null)
            {
                RootStorage._entry.StartingSectorLocation = miniStream.PhysicalIndex;
                RootStorage._entry.StreamSize = MiniStreamSize;
            }

            // build fat sectors
            AddFatSectors();
            AddDiFatSectors();

            // write out header
            var writer = new BinaryWriter(outputStream); // we don't dispose the writer
            WriteHeader(writer);

            // write out sectors
            var ids = new List<uint>(_sectors.Keys);
            ids.Sort();
            uint i = 0;
            foreach (var id in ids)
            {
                if (id != i)
                    throw new InvalidOperationException("File is invalid. Wrong sector layout.");

                var sector = GetSector(id);
                if (sector.PhysicalIndex != id)
                    throw new InvalidOperationException("File is invalid. Wrong sector numbering.");

                long pos = -1;
                if (writer.BaseStream.CanSeek)
                {
                    pos = writer.BaseStream.Position;
                    if (pos % SectorSize != 0)
                        throw new InvalidOperationException();
                }

                sector.Write(writer);

                if (pos != -1)
                {
                    if (writer.BaseStream.Position != pos + SectorSize)
                        throw new InvalidOperationException();
                }

                i++;
            }
        }

        private void AddFatSectors()
        {
            var newSectors = new List<FatSector>();

            // add root
            var currentFatSector = new FatSector(this);
            AddPhysicalSector(currentFatSector, AUTOINDEX);

            foreach (var sr in _sectors)
            {
                var sector = GetSector(sr.Value);

                if (currentFatSector.Sectors.Count >= FatEntriesPerSector)
                {
                    currentFatSector = new FatSector(this);
                    newSectors.Add(currentFatSector);
                }

                if (sector is FatSector)
                {
                    currentFatSector.Sectors.Add(currentFatSector.Sectors.Count, FATSECT);
                }
                else
                {
                    var next = GetNextSector(sector);
                    var nextIndex = next == null ? ENDOFCHAIN : next.PhysicalIndex;
                    currentFatSector.Sectors.Add(currentFatSector.Sectors.Count, nextIndex);
                }
            }

            foreach (var sector in newSectors)
            {
                AddPhysicalSector(sector, AUTOINDEX);
            }
        }

        private Sector? GetNextSector(Sector sector)
        {
            switch (sector.SectorType)
            {
                case SectorType.MiniFat:
                    return sector.Index + 1 >= _miniFatSectors.Count ? null : _miniFatSectors[sector.Index + 1];

                case SectorType.DiFat:
                    return sector.Index + 1 >= _diFatSectors.Count ? null : _diFatSectors[sector.Index + 1];

                case SectorType.Directory:
                    return sector.Index + 1 >= _directorySectors.Count ? null : _directorySectors[sector.Index + 1];

                case SectorType.MiniStream:
                    var miniStream = (MiniStreamSector)sector;
                    return miniStream.Next;

                case SectorType.Storage:
                    var storage = (StorageSector)sector;
                    return storage.Next;

                default:
                    return null;
            }
        }

        private Sector GetSector(uint physicalIndex)
        {
            return GetSector(_sectors[physicalIndex]);
        }

        private Sector GetSector(SectorRef sr)
        {
            return sr.SectorType switch
            {
                SectorType.Fat => _fatSectors[sr.Index],
                SectorType.MiniFat => _miniFatSectors[sr.Index],
                SectorType.Directory => _directorySectors[sr.Index],
                SectorType.MiniStream => _miniStreamSectors[sr.Index],
                SectorType.Storage => _storageSectors[sr.Index],
                SectorType.DiFat => _diFatSectors[sr.Index],
                _ => throw new NotSupportedException(),
            };
        }

        private void AddDiFatSectors()
        {
            var neededFatSectors = _fatSectors.Count - DiFatHeaderCount;
            if (neededFatSectors <= 0)
                return;

            var neededDiFatSectors = neededFatSectors / FatEntriesPerSector;
            if (neededFatSectors % FatEntriesPerSector != 0)
                neededDiFatSectors++;

            var firstFatIndex = DiFatHeaderCount;
            DiFatSector? last = null;
            for (var i = 0; i < neededDiFatSectors; i++)
            {
                // add to fat
                var fatSector = _fatSectors.LastOrDefault();
                if (fatSector.Sectors.Count >= FatEntriesPerSector)
                {
                    fatSector = new FatSector(this);
                    AddPhysicalSector(fatSector, AUTOINDEX);
                }
                // add the DI fat reference to the last sector of the last fat sector
                fatSector.Sectors.Add(fatSector.Sectors.Count, DIFSECT);

                var diFatSector = new DiFatSector(this);
                AddPhysicalSector(diFatSector, AUTOINDEX);

                if (last != null)
                {
                    // chain DI fat sectors
                    last.Sectors.Add(diFatSector.PhysicalIndex);
                }

                // only 127 entries to be able to chain
                var fatSectorCount = Math.Min(FatEntriesPerSector - 1, _fatSectors.Count - firstFatIndex);
                for (var j = 0; j < fatSectorCount; j++)
                {
                    var sector = _fatSectors[firstFatIndex++];
                    diFatSector.Sectors.Add(sector.PhysicalIndex);
                }

                last = diFatSector;
            }
        }

        private ulong MiniStreamSize
        {
            get
            {
                ulong size = 0;
                foreach (var sector in _miniStreamSectors)
                {
                    size += (ulong)sector.Size;
                }
                return size;
            }
        }

        internal CompoundFileStorage AddStorage(CompoundFileStorage storage, string name)
        {
            var entry = new DirectoryStorage(name) { ObjectType = DirectoryObjectType.Storage };
            AddDirectoryEntry(entry);
            var child = new CompoundFileStorage(this, storage, entry, isRoot: false);
            storage._entry.AddEntry(entry);
            return child;
        }

        internal CompoundFileStream AddStream(CompoundFileStorage storage, string name)
        {
            var entry = new DirectoryEntry(name) { ObjectType = DirectoryObjectType.Stream };
            AddDirectoryEntry(entry);
            var child = new CompoundFileStream(storage, entry);
            storage._entry.AddEntry(entry);
            return child;
        }

        private void AddStreamSectors(CompoundFileStorage storage)
        {
            foreach (var childStorage in storage.ChildStorages)
            {
                AddStreamSectors(childStorage);
            }

            foreach (var childStream in storage.ChildStreams)
            {
                if (childStream.Length == 0) // REVIEW: skip empty streams?
                    continue;

                if (childStream.Length < MiniStreamCutoff)
                {
                    // sector # in mini fat
                    childStream._entry.StartingSectorLocation = _miniStreamNextId;
                    childStream._entry.StreamSize = (ulong)childStream.Length;

                    AddMiniFatSectors(childStream);
                    AddMiniStreamSectors(childStream);
                }
                else
                {
                    AddStorageSectors(childStream);
                }
            }
        }

        private void AddMiniFatSectors(CompoundFileStream stream)
        {
            var neededMiniSectors = (int)(stream.Length / MiniSectorSize);
            if (stream.Length % MiniSectorSize != 0)
                neededMiniSectors++;

            var lastSector = _miniFatSectors.LastOrDefault();
            if (lastSector == null || lastSector.Entries.Count == FatEntriesPerSector)
            {
                lastSector = new MiniFatSector(this);
                AddPhysicalSector(lastSector, AUTOINDEX);
            }

            do
            {
                var leftEntriesOnSector = Math.Min(neededMiniSectors, FatEntriesPerSector - lastSector.Entries.Count);
                if (leftEntriesOnSector == 0)
                {
                    lastSector = new MiniFatSector(this);
                    AddPhysicalSector(lastSector, AUTOINDEX);
                    leftEntriesOnSector = Math.Min(neededMiniSectors, FatEntriesPerSector);
                }

                for (var i = 0; i < leftEntriesOnSector; i++)
                {
                    _miniStreamNextId++;
                    if (neededMiniSectors == 1)
                    {
                        lastSector.Entries.Add(ENDOFCHAIN);
                    }
                    else
                    {
                        lastSector.Entries.Add(_miniStreamNextId);
                    }
                    neededMiniSectors--;
                }
            }
            while (neededMiniSectors > 0);
        }

        private void AddMiniStreamSectors(CompoundFileStream stream)
        {
            var lastSector = _miniStreamSectors.LastOrDefault();
            if (lastSector == null || lastSector.Size == SectorSize)
            {
                var newSector = new MiniStreamSector(this);
                if (lastSector != null)
                    lastSector.Next = newSector;
                AddPhysicalSector(newSector, AUTOINDEX);
                lastSector = newSector;
            }

            var offset = 0;
            do
            {
                var leftOnSector = SectorSize - lastSector.Size;
                if (leftOnSector == 0)
                {
                    var newSector = new MiniStreamSector(this);
                    if (lastSector != null)
                        lastSector.Next = newSector;
                    AddPhysicalSector(newSector, AUTOINDEX);
                    leftOnSector = SectorSize;

                    lastSector = newSector;
                }

                if (lastSector.Streams.Count == 0)
                    lastSector.FirstStreamOffset = offset;
                lastSector.Streams.Add(stream);

                var size = Math.Min(leftOnSector, (int)stream.Length - offset);
                offset += size;
            }
            while (offset < stream.Length);
        }

        private void AddStorageSectors(CompoundFileStream stream)
        {
            var sectorsCount = (int)(stream.Length / SectorSize);
            if (stream.Length % SectorSize != 0)
                sectorsCount++;

            long offset = 0;
            StorageSector? prev = null;
            for (var i = 0; i < sectorsCount; i++)
            {
                var sector = new StorageSector(this, stream, offset);

                offset += SectorSize;
                AddPhysicalSector(sector, AUTOINDEX);

                if (i == 0)
                {
                    stream._entry.StartingSectorLocation = sector.PhysicalIndex;
                    stream._entry.StreamSize = (ulong)stream.Length;
                }

                if (prev != null)
                    prev.Next = sector;
                prev = sector;
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            if (_rootStorage != null)
                _rootStorage.Dispose();
        }

        /// <summary>
        /// Gets the root storage container.
        /// </summary>
        public CompoundFileStorage RootStorage
        {
            get
            {
                if (_rootStorage == null)
                {
                    _rootEntry = new DirectoryStorage(RootEntryName) { ObjectType = DirectoryObjectType.RootStorage };
                    AddDirectoryEntry(_rootEntry);
                    _rootStorage = new CompoundFileStorage(this, parent: null, _rootEntry, isRoot: true);
                }
                return _rootStorage;
            }
        }

        /// <summary>
        /// Gets or sets the CLSID.
        /// </summary>
        /// <value>
        /// The CLSID.
        /// </value>
        public Guid ClassId { get; set; }

        /// <summary>
        /// Gets or sets the author.
        /// </summary>
        /// <value>
        /// The author.
        /// </value>
        public string? Author
        {
            get => GetProperty<string?>(SummaryInformationFormatId, PIDSI_AUTHOR, defaultValue: null);
            set => SetProperty(SummaryInformationFormatId, PIDSI_AUTHOR, value);
        }

        /// <summary>
        /// Gets or sets the company.
        /// </summary>
        /// <value>
        /// The company.
        /// </value>
        public string? Company
        {
            get => GetProperty<string?>(DocSummaryInformationFormatId, PIDDSI_COMPANY, defaultValue: null);
            set => SetProperty(DocSummaryInformationFormatId, PIDDSI_COMPANY, value);
        }

        /// <summary>
        /// Gets or sets the title.
        /// </summary>
        /// <value>
        /// The title.
        /// </value>
        public string? Title
        {
            get => GetProperty<string?>(SummaryInformationFormatId, PIDSI_TITLE, defaultValue: null);
            set => SetProperty(SummaryInformationFormatId, PIDSI_TITLE, value);
        }

        /// <summary>
        /// Gets or sets the subject.
        /// </summary>
        /// <value>
        /// The subject.
        /// </value>
        public string? Subject
        {
            get => GetProperty<string?>(SummaryInformationFormatId, PIDSI_SUBJECT, defaultValue: null);
            set => SetProperty(SummaryInformationFormatId, PIDSI_SUBJECT, value);
        }

        /// <summary>
        /// Gets or sets the keywords.
        /// </summary>
        /// <value>
        /// The keywords.
        /// </value>
        public string? Keywords
        {
            get => GetProperty<string?>(SummaryInformationFormatId, PIDSI_KEYWORDS, defaultValue: null);
            set => SetProperty(SummaryInformationFormatId, PIDSI_KEYWORDS, value);
        }

        /// <summary>
        /// Gets or sets the comments.
        /// </summary>
        /// <value>
        /// The comments.
        /// </value>
        public string? Comments
        {
            get => GetProperty<string?>(SummaryInformationFormatId, PIDSI_COMMENTS, defaultValue: null);
            set => SetProperty(SummaryInformationFormatId, PIDSI_COMMENTS, value);
        }

        /// <summary>
        /// Gets the list of property sets.
        /// </summary>
        /// <value>
        /// The list of property sets.
        /// </value>
        public IEnumerable<Guid> PropertySets => _properties.Keys;

        /// <summary>
        /// Gets the specified property set.
        /// </summary>
        /// <param name="fmtid">The property set id.</param>
        /// <returns>A list of properties in the set.</returns>
        public IDictionary<int, object?>? GetPropertySet(Guid fmtid)
        {
            if (_properties.TryGetValue(fmtid, out var values))
                return values;

            return null;
        }

        /// <summary>
        /// Sets a property value.
        /// </summary>
        /// <param name="fmtid">The property fmtid.</param>
        /// <param name="id">The property id.</param>
        /// <param name="value">The value.</param>
        public void SetProperty(Guid fmtid, int id, object? value)
        {
            if (!_properties.TryGetValue(fmtid, out var values))
            {
                values = new Dictionary<int, object?>();
                _properties.Add(fmtid, values);
            }
            values[id] = value;
        }

        /// <summary>
        /// Gets a property value.
        /// </summary>
        /// <typeparam name="T">The expected type.</typeparam>
        /// <param name="fmtid">The property fmtid.</param>
        /// <param name="id">The property id.</param>
        /// <param name="defaultValue">The default value if the property was not found or if the value could not be converted into expected type.</param>
        /// <returns>The property value.</returns>
        public T GetProperty<T>(Guid fmtid, int id, T defaultValue)
        {
            if (!_properties.TryGetValue(fmtid, out var values))
                return defaultValue;

            if (!values.TryGetValue(id, out var value))
                return defaultValue;

            return ConvertUtilities.ChangeType(value, defaultValue);
        }

        private void Load(BinaryReader reader)
        {
            _fatSectors.Clear();
            _diFatSectors.Clear();
            _directorySectors.Clear();
            _sectors.Clear();
            _storageSectors.Clear();
            _miniFatSectors.Clear();
            _miniStreamSectors.Clear();
            _entries.Clear();
            _properties.Clear();

            var bytes = reader.ReadBytes(s_signature.Length);
            if (!bytes.SequenceEqual(s_signature))
                throw new CompoundFileException("File is invalid. Wrong signature.");

            var clsid = reader.ReadBytes(16);
            if (clsid.Length != 16)
                throw new CompoundFileException("File is invalid. Wrong class id.");

            ClassId = new Guid(clsid); // Header CLSID
            reader.ReadUInt16(); // Minor Version
            reader.ReadUInt16(); // Major Version
            if (reader.ReadUInt16() != 0xFFFE) // Byte Order
                throw new CompoundFileException("File is invalid. Wrong byte order.");

            SectorSize = 1 << reader.ReadUInt16(); // Sector Shift - 512 bytes
            MiniSectorSize = 1 << reader.ReadUInt16(); // Mini Sector Shift - 64 bytes
            if (reader.ReadBytes(6 + 4).Length != 6 + 4) // Reserved + Number of Directory Sectors
                throw new CompoundFileException("File is invalid.");

            _fatSectorsCount = reader.ReadInt32(); // Number of FAT Sectors 
            _firstDirectorySectorId = reader.ReadUInt32(); // First Directory Sector Location
            reader.ReadInt32(); // Transaction Signature Number

            MiniStreamCutoff = reader.ReadInt32(); // Mini Stream Cutoff Size

            _firstMiniFatSectorId = reader.ReadUInt32(); // First Mini FAT Sector Location
            _miniFatSectorsCount = reader.ReadInt32(); // Number of Mini FAT Sectors

            _firstDiFatSectorId = reader.ReadUInt32(); // First DIFAT Sector Location
            _diFatSectorsCount = reader.ReadInt32(); // Number of DIFAT Sectors

            for (var i = 0; i < DiFatHeaderCount; i++)
            {
                var physicalIndex = reader.ReadUInt32();
                if (physicalIndex != FREESECT)
                {
                    var fatSector = new FatSector(this);
                    AddPhysicalSector(fatSector, physicalIndex);
                }
            }

            // directory first sector
            var firstDirectorySector = new DirectorySector(this);
            AddPhysicalSector(firstDirectorySector, _firstDirectorySectorId);
            SeekToSector(reader, firstDirectorySector.PhysicalIndex);
            firstDirectorySector.Read(reader);

            // root entry & storage
            foreach (var entry in firstDirectorySector.Entries)
            {
                if (entry is DirectoryStorage storage && storage.ObjectType == DirectoryObjectType.RootStorage)
                {
                    _rootEntry = storage;
                    break;
                }
            }

            // DI FAT sectors
            if (_firstDiFatSectorId != ENDOFCHAIN)
            {
                var firstDiFatSector = new DiFatSector(this);
                AddPhysicalSector(firstDiFatSector, _firstDiFatSectorId);
                SeekToSector(reader, firstDiFatSector.PhysicalIndex);
                firstDiFatSector.Read(reader);

                var currentDiFatSector = firstDiFatSector;
                while (currentDiFatSector.Sectors.LastOrDefault() != ENDOFCHAIN && currentDiFatSector.Sectors.LastOrDefault() != FREESECT)
                {
                    var index = currentDiFatSector.Sectors.LastOrDefault();
                    currentDiFatSector = new DiFatSector(this);
                    AddPhysicalSector(currentDiFatSector, index);
                    SeekToSector(reader, currentDiFatSector.PhysicalIndex);
                    currentDiFatSector.Read(reader);
                }
            }

            // mini FAT sectors
            if (_firstMiniFatSectorId != ENDOFCHAIN)
            {
                var firstMiniFatSector = new MiniFatSector(this);
                AddPhysicalSector(firstMiniFatSector, _firstMiniFatSectorId);
                SeekToSector(reader, firstMiniFatSector.PhysicalIndex);
                firstMiniFatSector.Read(reader);
            }

            // read all fat sectors now to build chains
            foreach (var fatSector in _fatSectors.ToArray())
            {
                SeekToSector(reader, fatSector.PhysicalIndex);
                fatSector.Read(reader);
            }

            // read directories
            ReadDirectoryChain(_firstDirectorySectorId);
            foreach (var sector in _directorySectors)
            {
                if (sector != firstDirectorySector)
                {
                    SeekToSector(reader, sector.PhysicalIndex);
                    sector.Read(reader);
                }
            }

            // read mini FAT
            if (_firstMiniFatSectorId != ENDOFCHAIN)
                ReadMiniFatChain(_firstMiniFatSectorId);
            foreach (var sector in _miniFatSectors)
            {
                if (sector.PhysicalIndex != _firstMiniFatSectorId)
                {
                    SeekToSector(reader, sector.PhysicalIndex);
                    sector.Read(reader);
                }
            }

            // read mini stream
            if (firstDirectorySector.Entries[0].StartingSectorLocation != 0 &&
                firstDirectorySector.Entries[0].StartingSectorLocation != ENDOFCHAIN)
            {
                var firstMiniStreamSector = new MiniStreamSector(this);
                AddPhysicalSector(firstMiniStreamSector, firstDirectorySector.Entries[0].StartingSectorLocation);
                ReadMiniStreamChain(firstMiniStreamSector.PhysicalIndex);
            }

            // create storage hierarchy
            Debug.Assert(_rootEntry != null);
            CreateStorage(parent: null, _rootEntry, isRoot: true);

            // load data
            LoadStreams(RootStorage, reader);
            LoadPropertySets();
        }

        private void ReadDirectoryChain(uint physicalIndex)
        {
            var index = GetNextPhysicalIndex(physicalIndex);
            if (index == ENDOFCHAIN)
                return;

            var sector = new DirectorySector(this);
            AddPhysicalSector(sector, index);
            ReadDirectoryChain(index);
        }

        private void ReadMiniFatChain(uint physicalIndex)
        {
            var index = GetNextPhysicalIndex(physicalIndex);
            if (index == ENDOFCHAIN)
                return;

            var sector = new MiniFatSector(this);
            AddPhysicalSector(sector, index);
            ReadMiniFatChain(index);
        }

        private void ReadMiniStreamChain(uint physicalIndex)
        {
            var index = GetNextPhysicalIndex(physicalIndex);
            if (index == ENDOFCHAIN)
                return;

            var sector = new MiniStreamSector(this);
            AddPhysicalSector(sector, index);
            ReadMiniStreamChain(index);
        }

        private uint GetNextPhysicalIndex(uint physicalIndex)
        {
            var fatSectorIndex = (int)(physicalIndex / FatEntriesPerSector);
            var sector = _fatSectors[fatSectorIndex];
            return sector.Sectors[(int)(physicalIndex % FatEntriesPerSector)];
        }

        private void CreateStorage(CompoundFileStorage? parent, DirectoryStorage entry, bool isRoot)
        {
            var storage = new CompoundFileStorage(this, parent, entry, isRoot);
            if (entry.ObjectType == DirectoryObjectType.RootStorage)
            {
                _rootStorage = storage;
            }
            else
            {
                Debug.Assert(parent != null);
                parent._storages.Add(storage);
            }

            if (entry.ChildId != NOSTREAM)
                AddEntry(storage, entry.ChildId);
        }

        private void LoadStreams(CompoundFileStorage storage, BinaryReader reader)
        {
            foreach (var childStorage in storage.ChildStorages)
            {
                LoadStreams(childStorage, reader);
            }

            foreach (var stream in storage.ChildStreams)
            {
                var size = stream._entry.StreamSize;
                if (size < (uint)MiniStreamCutoff)
                {
                    foreach (var miniSectorIndex in GetMiniStreamSectorChain(stream._entry.StartingSectorLocation))
                    {
                        SeekToMiniStreamSector(reader, miniSectorIndex);
                        var needed = (int)Math.Min(size, (ulong)MiniSectorSize);
                        var read = reader.ReadBytes(needed);
                        if (read.Length != needed)
                            throw new CompoundFileException("File is invalid. Wrong mini stream length.");

                        stream.Stream.Write(read, 0, read.Length);
                        size -= (ulong)read.Length;
                    }
                    if (size != 0)
                        throw new CompoundFileException("File is invalid. Wrong mini stream size.");
                }
                else
                {
                    foreach (var physicalIndex in GetSectorChain(stream._entry.StartingSectorLocation))
                    {
                        SeekToSector(reader, physicalIndex);
                        var needed = (int)Math.Min(size, (ulong)SectorSize);
                        var read = reader.ReadBytes(needed);
                        if (read.Length != needed)
                            throw new CompoundFileException("File is invalid. Wrong stream length.");

                        stream.Stream.Write(read, 0, read.Length);
                        size -= (ulong)read.Length;
                    }
                    if (size != 0)
                        throw new CompoundFileException("File is invalid. Wrong stream size.");
                }
            }
        }

        private uint GetNextMiniStreamSectorIndex(uint miniStreamSectorIndex)
        {
            var miniFatSectorIndex = (int)(miniStreamSectorIndex / FatEntriesPerSector);
            var sector = _miniFatSectors[miniFatSectorIndex];
            return sector.Entries[(int)(miniStreamSectorIndex % FatEntriesPerSector)];
        }

        private IEnumerable<uint> GetMiniStreamSectorChain(uint miniStreamSectorIndex)
        {
            var next = miniStreamSectorIndex;
            do
            {
                if (next == ENDOFCHAIN)
                    yield break;

                yield return next;
                next = GetNextMiniStreamSectorIndex(next);
            }
            while (true);
        }

        private IEnumerable<uint> GetSectorChain(uint startPhysicalIndex)
        {
            var next = startPhysicalIndex;
            do
            {
                if (next == ENDOFCHAIN)
                    yield break;

                yield return next;
                next = GetNextPhysicalIndex(next);
            }
            while (true);
        }

        private void AddEntry(CompoundFileStorage parent, uint id)
        {
            var entry = _entries[(int)id];
            if (entry is DirectoryStorage childStorage)
            {
                CreateStorage(parent, childStorage, isRoot: false);
            }
            else
            {
                var stream = new CompoundFileStream(parent, entry);
                parent._streams.Add(stream);

                if (entry.LeftSiblingId != NOSTREAM)
                    AddEntry(parent, entry.LeftSiblingId);

                if (entry.RightSiblingId != NOSTREAM)
                    AddEntry(parent, entry.RightSiblingId);
            }
        }

        private void SeekToMiniStreamSector(BinaryReader reader, uint miniSectorIndex)
        {
            var miniStreamSector = _miniStreamSectors[(int)(miniSectorIndex / (SectorSize / MiniSectorSize))];
            SeekToSector(reader, miniStreamSector.PhysicalIndex);

            var index = miniSectorIndex % (SectorSize / MiniSectorSize);
            reader.BaseStream.Seek(index * MiniSectorSize, SeekOrigin.Current);
        }

        private void SeekToSector(BinaryReader reader, uint physicalIndex)
        {
            reader.BaseStream.Seek(HeaderSize + physicalIndex * SectorSize, SeekOrigin.Begin);
        }

        private void WriteHeader(BinaryWriter writer)
        {
            writer.Write(s_signature); // Header Signature
            writer.Write(ClassId.ToByteArray()); // Header CLSID
            writer.Write((ushort)0x3E); // Minor Version
            writer.Write((ushort)0x3); // Major Version
            writer.Write((ushort)0xFFFE); // Byte Order
            writer.Write((ushort)0x9); // Sector Shift - 512 bytes
            writer.Write((ushort)0x6); // Mini Sector Shift - 64 bytes
            writer.Write(new byte[6]); // Reserved
            writer.Write(new byte[4]); // Number of Directory Sectors
            writer.Write(_fatSectors.Count); // Number of FAT Sectors - will be updated at the end
            var firstDirectory = _directorySectors.FirstOrDefault().PhysicalIndex;
            writer.Write(firstDirectory); // First Directory Sector Location
            writer.Write(0); // Transaction Signature Number

            writer.Write(MiniStreamCutoff); // Mini Stream Cutoff Size
            var firstMiniFat = _miniFatSectors.Count == 0 ? ENDOFCHAIN : _miniFatSectors.FirstOrDefault().PhysicalIndex;
            writer.Write(firstMiniFat); // First Mini FAT Sector Location
            writer.Write(_miniFatSectors.Count); // Number of Mini FAT Sectors

            var firstDiFat = _diFatSectors.Count == 0 ? ENDOFCHAIN : _diFatSectors.FirstOrDefault().PhysicalIndex;
            writer.Write(firstDiFat); // First DIFAT Sector Location
            writer.Write(_diFatSectors.Count); // Number of DIFAT Sectors

            // write difat sectors in header (limited to 109 by design, see spec)
            var i = 0;
            foreach (var sector in _fatSectors)
            {
                writer.Write(sector.PhysicalIndex);
                i++;
                if (i == DiFatHeaderCount)
                    break;
            }

            if (i < DiFatHeaderCount)
            {
                for (var j = 0; j < DiFatHeaderCount - i; j++)
                {
                    writer.Write(FREESECT);
                }
            }
        }

        private void LoadPropertySets()
        {
            if (RootStorage == null)
                return;

            LoadPropertySet(SummaryInformationFormatId, RootStorage.GetChildStream(SummaryInformationStreamName));
            LoadPropertySet(DocSummaryInformationFormatId, RootStorage.GetChildStream(DocSummaryInformationStreamName));
        }

        // NOTE: we don't support user-defined properties
        private void LoadPropertySet(Guid fmtid, CompoundFileStream? stream)
        {
            if (stream == null)
                return;

            if (!_properties.TryGetValue(fmtid, out var values))
            {
                values = new Dictionary<int, object?>();
                _properties.Add(fmtid, values);
            }

            stream.Stream.Position = 0;
            var reader = new BinaryReader(stream.Stream);
            var byteOrder = reader.ReadUInt16(); // ByteOrder
            if (byteOrder != 0xFFFE)
                return;

            reader.ReadUInt16(); // Version
            reader.ReadUInt32(); // SystemIdentifier
            reader.ReadBytes(16); // CLSID
            var sets = reader.ReadInt32(); // NumPropertySets
            if (sets <= 0 || sets > 2)
                return;

            if (new Guid(reader.ReadBytes(16)) != fmtid) // FMTID0
                return;

            var baseOffset = reader.ReadInt32(); // Offset0;
            if (sets == 2)
            {
                reader.ReadBytes(16); // FMTID1
                reader.ReadInt32(); // Offset1
            }

            _ = reader.ReadInt32();
            var count = reader.ReadInt32();

            var props = new List<(int propKey, int propOffset)>();
            for (var i = 0; i < count; i++)
            {
                var propKey = reader.ReadInt32();
                var propOffset = reader.ReadInt32();

                props.Add((propKey, propOffset));
            }

            Encoding? cpEncoding = null;
            for (var i = 0; i < count; i++)
            {
                stream.Stream.Seek(baseOffset + props[i].propOffset, SeekOrigin.Begin);
                var value = LoadPropertyValue(reader, cpEncoding);
                if (Type.Missing.Equals(value))
                    continue;

                if (props[i].propKey == 1) // codepage
                    cpEncoding = Encoding.GetEncoding(ConvertUtilities.ChangeType(value, 1252));
                values.Add(props[i].propKey, value);
            }
        }

        private static object LoadPropertyValue(BinaryReader reader, Encoding? cpEncoding)
        {
            if (cpEncoding == null)
            {
                cpEncoding = Encoding.Default;
            }

            var vt = (VARTYPE)reader.ReadUInt16();
            reader.ReadUInt16(); // Padding
            switch (vt)
            {
                case VARTYPE.VT_I2:
                    return reader.ReadInt16();

                case VARTYPE.VT_INT:
                case VARTYPE.VT_I4:
                    return reader.ReadInt32();

                case VARTYPE.VT_R4:
                    return reader.ReadSingle();

                case VARTYPE.VT_R8:
                    return reader.ReadDouble();

                case VARTYPE.VT_CY:
                    return reader.ReadInt64();

                case VARTYPE.VT_DATE:
                    return DateTime.FromOADate(reader.ReadDouble());

                case VARTYPE.VT_BOOL:
                    return reader.ReadUInt32() != 0;

                case VARTYPE.VT_ERROR:
                case VARTYPE.VT_HRESULT:
                case VARTYPE.VT_UINT:
                case VARTYPE.VT_UI4:
                    return reader.ReadUInt32();

                case VARTYPE.VT_DECIMAL:
                    return reader.ReadDecimal();

                case VARTYPE.VT_I1:
                    return reader.ReadSByte();

                case VARTYPE.VT_UI1:
                    return reader.ReadByte();

                case VARTYPE.VT_UI2:
                    return reader.ReadUInt16();

                case VARTYPE.VT_I8:
                    return reader.ReadInt64();

                case VARTYPE.VT_UI8:
                    return reader.ReadUInt64();

                case VARTYPE.VT_LPSTR:
                    return ReadStringProperty(reader, cpEncoding);

                case VARTYPE.VT_FILETIME:
                    return DateTime.FromFileTime(reader.ReadInt64());
            }
            return Type.Missing;
        }

        private static string ReadStringProperty(BinaryReader reader, Encoding encoding)
        {
            var len = reader.ReadInt32();
            if (len == 0)
                return string.Empty;

            var bytes = reader.ReadBytes(len);
            var end = bytes.Length;
            while (end > 0 && bytes[end - 1] == 0)
            {
                end--;
            }
            if (end == 0)
                return string.Empty;

            return encoding.GetString(bytes, 0, end);
        }

        private void WritePropertySets()
        {
            WritePropertySet(SummaryInformationFormatId);
            WritePropertySet(DocSummaryInformationFormatId);
        }

        // NOTE: we don't support user-defined properties
        private void WritePropertySet(Guid fmtid)
        {
            // [MS-OLEPS]: Object Linking and Embedding (OLE) Property Set Data Structures
            // http://msdn.microsoft.com/en-us/library/dd942421.aspx
            if (!_properties.TryGetValue(fmtid, out var values))
                return;

            string streamName;
            if (fmtid == SummaryInformationFormatId)
            {
                streamName = SummaryInformationStreamName;
            }
            else if (fmtid == DocSummaryInformationFormatId)
            {
                streamName = DocSummaryInformationStreamName;
            }
            else
            {
                throw new NotSupportedException();
            }

            var stream = RootStorage.GetOrAddChildStream(streamName);
            stream.Stream.Position = 0;
            var writer = new BinaryWriter(stream.Stream);
            writer.Write((ushort)0xFFFE); // ByteOrder
            writer.Write((ushort)0); // Version
            writer.Write(0x20105); // SystemIdentifier (XP)
            writer.Write(Guid.Empty.ToByteArray()); // CLSID
            writer.Write(1); // NumPropertySets
            writer.Write(fmtid.ToByteArray()); // FMTID0
            writer.Write(48); // Offset0
            // FMTID1 & Offset1 absent
            WritePropertySet(writer, values);

            // set default codepage, since we'll be using it
            values[1] = Encoding.Default.CodePage;

            stream.Stream.SetLength(stream.Stream.Position);
        }

        private static void WritePropertySet(BinaryWriter writer, Dictionary<int, object?> values)
        {
            var offsets = new List<int>();
            var size = sizeof(int) // Size
                + sizeof(int) // NumProperties
                + values.Count * 2 * sizeof(int); // PropertyIdentifierAndOffset x

            var firstOffset = size;
            foreach (var property in values)
            {
                // add size of PropertyIdentifierAndOffset = size of PropertyIdentifier + size of Offset
                offsets.Add(firstOffset);
                var valueSize = GetPropertyValueSize(property.Value);
                size += valueSize;
                firstOffset += valueSize;
            }

            writer.Write(size);
            writer.Write(values.Count);

            var offsetIndex = 0;
            foreach (var property in values)
            {
                // PropertyIdentifierAndOffset x
                writer.Write(property.Key);
                writer.Write(offsets[offsetIndex++]);
            }

            foreach (var property in values)
            {
                // Property x
                WritePropertyValue(writer, property.Value);
            }
        }

        private static int GetPropertyValueSize(object? value)
        {
            const int TypeAndPaddingSize = 4;
            var size = TypeAndPaddingSize;
            if (value != null)
            {
                switch (Type.GetTypeCode(value.GetType()))
                {
                    case TypeCode.Boolean:
                    case TypeCode.Byte:
                    case TypeCode.SByte:
                    case TypeCode.Int32:
                    case TypeCode.Int16:
                    case TypeCode.UInt32:
                    case TypeCode.UInt16:
                        size += sizeof(int);
                        break;

                    case TypeCode.Decimal:
                        size += sizeof(decimal);
                        break;

                    case TypeCode.Double:
                        size += sizeof(double);
                        break;

                    case TypeCode.Single:
                        size += sizeof(float);
                        break;

                    case TypeCode.Int64:
                    case TypeCode.UInt64:
                    case TypeCode.DateTime:
                        size += sizeof(long);
                        break;

                    //case TypeCode.Char:
                    //case TypeCode.String:
                    default:
                        size += sizeof(int); // size
                        var s = string.Format("{0}", value);
                        var bytesLen = s.Length + 1;
                        size += bytesLen;
                        if (bytesLen % 4 != 0)
                            size += 4 - bytesLen % 4; // padding
                        break;

                }
            }
            return size;
        }

        private static void WritePropertyValue(BinaryWriter writer, object? value)
        {
            if (value != null)
            {
                switch (Type.GetTypeCode(value.GetType()))
                {
                    case TypeCode.Decimal:
                        writer.Write((ushort)VARTYPE.VT_DECIMAL);
                        writer.Write((ushort)0); // Padding
                        writer.Write((decimal)value);
                        return;

                    case TypeCode.Boolean:
                        writer.Write((ushort)VARTYPE.VT_BOOL);
                        writer.Write((ushort)0); // Padding
                        writer.Write((bool)value ? 1 : 0);
                        return;

                    case TypeCode.Int32:
                        writer.Write((ushort)VARTYPE.VT_I4);
                        writer.Write((ushort)0); // Padding
                        writer.Write((int)value);
                        return;

                    case TypeCode.Int16:
                        writer.Write((ushort)VARTYPE.VT_I2);
                        writer.Write((ushort)0); // Padding
                        writer.Write((short)value);
                        writer.Write((short)0); // Padding
                        return;

                    case TypeCode.Int64:
                        writer.Write((ushort)VARTYPE.VT_I8);
                        writer.Write((ushort)0); // Padding
                        writer.Write((long)value);
                        return;

                    case TypeCode.UInt32:
                        writer.Write((ushort)VARTYPE.VT_UI4);
                        writer.Write((ushort)0); // Padding
                        writer.Write((uint)value);
                        return;

                    case TypeCode.UInt16:
                        writer.Write((ushort)VARTYPE.VT_UI2);
                        writer.Write((ushort)0); // Padding
                        writer.Write((short)value);
                        writer.Write((ushort)0); // Padding
                        return;

                    case TypeCode.UInt64:
                        writer.Write((ushort)VARTYPE.VT_UI8);
                        writer.Write((ushort)0); // Padding
                        writer.Write((ulong)value);
                        return;

                    case TypeCode.Byte:
                        writer.Write((ushort)VARTYPE.VT_UI1);
                        writer.Write((ushort)0); // Padding
                        writer.Write((byte)value);
                        writer.Write((byte)0); // Padding
                        writer.Write((ushort)0); // Padding
                        return;

                    case TypeCode.SByte:
                        writer.Write((ushort)VARTYPE.VT_I1);
                        writer.Write((ushort)0); // Padding
                        writer.Write((sbyte)value);
                        writer.Write((byte)0); // Padding
                        writer.Write((ushort)0); // Padding
                        return;

                    case TypeCode.Single:
                        writer.Write((ushort)VARTYPE.VT_R4);
                        writer.Write((ushort)0); // Padding
                        writer.Write((float)value);
                        return;

                    case TypeCode.Double:
                        writer.Write((ushort)VARTYPE.VT_R8);
                        writer.Write((ushort)0); // Padding
                        writer.Write((double)value);
                        return;

                    case TypeCode.DateTime:
                        writer.Write((ushort)VARTYPE.VT_FILETIME);
                        writer.Write((ushort)0); // Padding
                        writer.Write(((DateTime)value).ToFileTime());
                        return;

                    //case TypeCode.Char:
                    //case TypeCode.String:
                    default:
                        writer.Write((ushort)VARTYPE.VT_LPSTR); // Type
                        writer.Write((ushort)0); // Padding
                        var s = string.Format("{0}", value);
                        var bytesLen = s.Length + 1;
                        if (bytesLen % 4 != 0)
                            bytesLen += 4 - bytesLen % 4;
                        writer.Write(bytesLen); // Size
                        var bytes = Encoding.Default.GetBytes(s + '\0');
                        writer.Write(bytes);
                        // padding
                        if (bytes.Length % 4 != 0)
                        {
                            for (var i = 0; i < 4 - bytes.Length % 4; i++)
                            {
                                writer.Write((byte)0);
                            }
                        }
                        return;

                }
            }
            writer.Write((ushort)VARTYPE.VT_NULL); // Type
            writer.Write((ushort)0); // Padding
        }

        private abstract class Sector
        {
            protected Sector(CompoundFile file)
            {
                File = file;
            }

            public CompoundFile File;
            public abstract void Write(BinaryWriter writer);
            public abstract void Read(BinaryReader reader);
            public abstract SectorType SectorType { get; }

            public uint PhysicalIndex; // represent the index in the whole file, same as index in _sectors
            public int Index; // Index in the specific collection (_fatSectors, etc.)

            public override string ToString()
            {
                return GetType().Name + ":" + PhysicalIndex;
            }
        }

        private sealed class MiniStreamSector : Sector
        {
            public readonly List<CompoundFileStream> Streams = new List<CompoundFileStream>();
            public int FirstStreamOffset;
            public MiniStreamSector? Next;

            public MiniStreamSector(CompoundFile file)
                : base(file)
            {
            }

            public override SectorType SectorType => SectorType.MiniStream;

            public override void Read(BinaryReader reader)
            {
                throw new NotImplementedException();
            }

            public override void Write(BinaryWriter writer)
            {
                var leftOnSector = File.SectorSize;
                for (var i = 0; i < Streams.Count; i++)
                {
                    var offset = i == 0 ? FirstStreamOffset : 0;
                    var size = Math.Min((int)Streams[i].Length - offset, leftOnSector);
                    Streams[i].WriteOffset(writer.BaseStream, offset, size);
                    leftOnSector -= size;

                    // pad mini sector with zeros
                    if (leftOnSector % File.MiniSectorSize != 0)
                    {
                        var pad = leftOnSector % File.MiniSectorSize;
                        writer.Write(new byte[pad]);
                        leftOnSector -= pad;
                    }
                }
                if (leftOnSector > 0)
                    writer.Write(new byte[leftOnSector]); // pad sector with zeros
            }

            public int Size
            {
                get
                {
                    var size = 0;
                    for (var i = 0; i < Streams.Count; i++)
                    {
                        size += (int)Streams[i].Length;
                        if (i == 0)
                            size -= FirstStreamOffset;
                        if (size % File.MiniSectorSize != 0)
                            // pad to sector size
                            size += File.MiniSectorSize - size % File.MiniSectorSize;

                        if (size >= File.SectorSize)
                        {
                            size = File.SectorSize;
                            break;
                        }
                    }
                    return size;
                }
            }
        }

        private sealed class StorageSector : Sector
        {
            public CompoundFileStream Stream;
            public long Offset;
            public StorageSector? Next;

            public StorageSector(CompoundFile file, CompoundFileStream stream, long offset)
                : base(file)
            {
                Stream = stream;
                Offset = offset;
            }

            public override SectorType SectorType => SectorType.Storage;

            public override void Read(BinaryReader reader)
            {
                throw new NotImplementedException();
            }

            public override void Write(BinaryWriter writer)
            {
                var written = Stream.WriteOffset(writer.BaseStream, Offset, File.SectorSize);
                if (written < File.SectorSize)
                    writer.Write(new byte[File.SectorSize - written]);
            }
        }

        private sealed class FatSector : Sector
        {
            public readonly Dictionary<int, uint> Sectors = new Dictionary<int, uint>();

            public FatSector(CompoundFile file)
                : base(file)
            {
            }

            public override SectorType SectorType => SectorType.Fat;

            public override void Read(BinaryReader reader)
            {
                for (var i = 0; i < File.FatEntriesPerSector; i++)
                {
                    Sectors.Add(i, reader.ReadUInt32());
                }
            }

            public override void Write(BinaryWriter writer)
            {
                if (Sectors.Count > File.FatEntriesPerSector)
                    throw new InvalidOperationException();

                for (var i = 0; i < Sectors.Count; i++)
                {
                    writer.Write(Sectors[i]);
                }

                for (var i = 0; i < File.FatEntriesPerSector - Sectors.Count; i++)
                {
                    writer.Write(FREESECT);
                }
            }
        }

        private sealed class MiniFatSector : Sector
        {
            public readonly List<uint> Entries = new List<uint>();

            public MiniFatSector(CompoundFile file)
                : base(file)
            {
            }

            public override SectorType SectorType => SectorType.MiniFat;

            public override void Read(BinaryReader reader)
            {
                for (var i = 0; i < File.FatEntriesPerSector; i++)
                {
                    var index = reader.ReadUInt32();
                    Entries.Add(index);
                }
            }

            public override void Write(BinaryWriter writer)
            {
                if (Entries.Count > File.FatEntriesPerSector)
                    throw new InvalidOperationException();

                foreach (var t in Entries)
                {
                    writer.Write(t);
                }

                for (var i = 0; i < File.FatEntriesPerSector - Entries.Count; i++)
                {
                    writer.Write(FREESECT);
                }
            }
        }

        private sealed class DiFatSector : Sector
        {
            public List<uint> Sectors = new List<uint>();

            public DiFatSector(CompoundFile file)
                : base(file)
            {
            }

            public override SectorType SectorType => SectorType.DiFat;

            public override void Read(BinaryReader reader)
            {
                for (var i = 0; i < File.FatEntriesPerSector; i++)
                {
                    var physicalIndex = reader.ReadUInt32();
                    if (physicalIndex != FREESECT)
                    {
                        var fatSector = new FatSector(File);
                        File.AddPhysicalSector(fatSector, physicalIndex);
                    }
                    Sectors.Add(physicalIndex);
                }
            }

            public override void Write(BinaryWriter writer)
            {
                if (Sectors.Count > File.FatEntriesPerSector)
                    throw new InvalidOperationException();

                for (var i = 0; i < Sectors.Count; i++)
                {
                    writer.Write(Sectors[i]);
                }

                for (var i = 0; i < File.FatEntriesPerSector - Sectors.Count; i++)
                {
                    writer.Write(FREESECT);
                }
            }
        }

        internal enum DirectoryObjectType : byte
        {
            Unallocated = 0,
            Storage = 1,
            Stream = 2,
            RootStorage = 5,
        }

        internal enum DirectoryColor : byte
        {
            Red = 0,
            Black = 1,
        }

        private sealed class DirectoryEntryComparer : IComparer<DirectoryEntry>
        {
            public int Compare(DirectoryEntry x, DirectoryEntry y)
            {
                if (x.Name.Length < y.Name.Length)
                    return -1;

                if (x.Name.Length > y.Name.Length)
                    return 1;

                return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
            }
        }

        internal sealed class DirectoryStorage : DirectoryEntry
        {
            private readonly List<DirectoryEntry> Entries = new List<DirectoryEntry>();

            public DirectoryStorage(string name)
                : base(name)
            {
            }

            public void AddEntry(DirectoryEntry entry)
            {
                if (Entries.Count == 0)
                    ChildId = entry.Index;

                Entries.Add(entry);
                Entries.Sort(new DirectoryEntryComparer());

                // we only setup right sibling since our tree is not really balanced (black only)
                if (Entries.Count > 1)
                {
                    for (var i = 0; i < Entries.Count - 1; i++)
                    {
                        Entries[i].RightSiblingId = Entries[i + 1].Index;
                    }
                }
            }

            public override int TotalEntriesCount
            {
                get
                {
                    var sum = 1;
                    foreach (var entry in Entries)
                    {
                        sum += entry.TotalEntriesCount;
                    }
                    return sum;
                }
            }
        }

        internal class DirectoryEntry
        {
            public string Name;
            public DirectoryObjectType ObjectType { get; set; }
            public DirectoryColor Color { get; set; }
            public uint LeftSiblingId { get; set; }
            public uint RightSiblingId { get; set; }
            public uint ChildId { get; set; }
            public Guid ClassId { get; set; }
            public uint StateBits { get; set; }
            public DateTime CreationTime { get; set; }
            public DateTime ModifiedTime { get; set; }
            public uint StartingSectorLocation { get; set; }
            public ulong StreamSize { get; set; }
            public uint Index { get; set; }

            public override string ToString() => Name;

            public virtual int TotalEntriesCount => 1;

            public DirectoryEntry(string name)
            {
                // about red-black-trees, from official spec:
                // "The simplest implementation of the above invariants would be to mark every node as black, in which case the tree is simply a binary tree."
                // NOTE: it's slower but we'll have only a few entries anyway
                Color = DirectoryColor.Black;
                CreationTime = DateTime.Now;
                ModifiedTime = CreationTime;
                LeftSiblingId = NOSTREAM;
                RightSiblingId = NOSTREAM;
                ChildId = NOSTREAM;
                Name = name;
            }

            public static DirectoryEntry? Read(BinaryReader reader)
            {
                var nameBytes = reader.ReadBytes(64);
                var len = reader.ReadInt16();
                if (len <= 0)
                    return null;

                var name = Encoding.Unicode.GetString(nameBytes, 0, len - 2);

                var objectType = (DirectoryObjectType)reader.ReadByte();
                DirectoryEntry entry;
                switch (objectType)
                {
                    case DirectoryObjectType.RootStorage:
                    case DirectoryObjectType.Storage:
                        entry = new DirectoryStorage(name);
                        break;

                    default:
                        entry = new DirectoryEntry(name);
                        break;
                }

                entry.ObjectType = objectType;
                entry.Color = (DirectoryColor)reader.ReadByte();
                entry.LeftSiblingId = reader.ReadUInt32();
                entry.RightSiblingId = reader.ReadUInt32();
                entry.ChildId = reader.ReadUInt32();
                entry.ClassId = new Guid(reader.ReadBytes(16));
                entry.StateBits = reader.ReadUInt32();
                var time = reader.ReadInt64();
                entry.CreationTime = time != 0 ? DateTime.FromFileTime(time) : DateTime.MinValue;
                time = reader.ReadInt64();
                entry.ModifiedTime = time != 0 ? DateTime.FromFileTime(time) : DateTime.MinValue;
                entry.StartingSectorLocation = reader.ReadUInt32();
                entry.StreamSize = reader.ReadUInt64();
                return entry;
            }

            public void Write(BinaryWriter writer)
            {
                var name = Name.Length > 32 ? Name.Substring(0, 32) : Name.PadRight(32, '\0');
                writer.Write(Encoding.Unicode.GetBytes(name)); // Directory Entry Name
                writer.Write((ushort)(2 * (Math.Min(32, Name.Length) + 1))); // Directory Entry Name Length
                writer.Write((byte)ObjectType); // Object Type
                writer.Write((byte)Color); // Color Flag
                writer.Write(LeftSiblingId); // Left Sibling ID
                writer.Write(RightSiblingId); // Right Sibling ID
                writer.Write(ChildId); // Child ID
                writer.Write(ObjectType == DirectoryObjectType.Stream ? Guid.Empty.ToByteArray() : ClassId.ToByteArray());
                writer.Write(StateBits); // State Bits
                if (ObjectType == DirectoryObjectType.RootStorage)
                {
                    writer.Write(0L);
                    writer.Write(0L);
                }
                else
                {
                    writer.Write(CreationTime == DateTime.MinValue ? 0L : CreationTime.ToFileTime()); // Creation Time
                    writer.Write(CreationTime == DateTime.MinValue ? 0L : ModifiedTime.ToFileTime()); // Modified Time
                }
                writer.Write(StartingSectorLocation);
                writer.Write(StreamSize);
            }
        }

        private sealed class DirectorySector : Sector
        {
            public readonly List<DirectoryEntry> Entries = new List<DirectoryEntry>();

            public DirectorySector(CompoundFile file)
                : base(file)
            {
            }

            public override SectorType SectorType => SectorType.Directory;

            public override void Read(BinaryReader reader)
            {
                for (var i = 0; i < DirectoryEntriesPerSector; i++)
                {
                    var entry = DirectoryEntry.Read(reader);
                    if (entry != null)
                    {
                        Entries.Add(entry);
                        File._entries.Add(entry);
                    }
                }
            }

            public override void Write(BinaryWriter writer)
            {
                if (Entries.Count > DirectoryEntriesPerSector)
                    throw new InvalidOperationException();

                foreach (var entry in Entries)
                {
                    entry.Write(writer);
                }

                for (var i = 0; i < DirectoryEntriesPerSector - Entries.Count; i++)
                {
                    writer.Write(new byte[File.SectorSize / DirectoryEntriesPerSector]);
                }
            }
        }

        private enum SectorType
        {
            Fat,
            MiniFat,
            DiFat,
            Directory,
            MiniStream,
            Storage,
        }

        private sealed class SectorRef
        {
            public SectorRef(SectorType type, int index)
            {
                SectorType = type;
                Index = index;
            }

            public SectorType SectorType;
            public int Index;

            public override string ToString()
            {
                return SectorType + ":" + Index.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
