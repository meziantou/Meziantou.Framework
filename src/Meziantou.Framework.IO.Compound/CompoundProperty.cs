using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Meziantou.Framework.IO.Compound
{
    /// <summary>
    /// Represents a Compound file property.
    /// </summary>
    [DebuggerDisplay("Name={Name}, Id={Id}, Value={Value}")]
    public sealed class CompoundProperty
    {
        /// <summary>
        /// Defines the SummaryInformation FMTID.
        /// </summary>
        public static readonly Guid SummaryInformationFormatId = new Guid("{F29F85E0-4FF9-1068-AB91-08002B27B3D9}");

        /// <summary>
        /// Defines the DocSummaryInformation FMTID.
        /// </summary>
        public static readonly Guid DocSummaryInformationFormatId = new Guid("{D5CDD502-2E9C-101B-9397-08002B2CF9AE}");

        /// <summary>
        /// Defines the UserDefinedProperties FMTID.
        /// </summary>
        public static readonly Guid UserDefinedPropertiesId = new Guid("{D5CDD505-2E9C-101B-9397-08002B2CF9AE}");

        private object _value;

        // SummaryInformation Property Set
        private const uint PIDSI_TITLE = 0x00000002;  // VT_LPSTR
        private const uint PIDSI_SUBJECT = 0x00000003;  // VT_LPSTR
        private const uint PIDSI_AUTHOR = 0x00000004;  // VT_LPSTR
        private const uint PIDSI_KEYWORDS = 0x00000005;  // VT_LPSTR
        private const uint PIDSI_COMMENTS = 0x00000006;  // VT_LPSTR
        private const uint PIDSI_TEMPLATE = 0x00000007;  // VT_LPSTR
        private const uint PIDSI_LASTAUTHOR = 0x00000008;  // VT_LPSTR
        private const uint PIDSI_REVNUMBER = 0x00000009;  // VT_LPSTR
        private const uint PIDSI_EDITTIME = 0x0000000a;  // VT_FILETIME (UTC)
        private const uint PIDSI_LASTPRINTED = 0x0000000b;  // VT_FILETIME (UTC)
        private const uint PIDSI_CREATE_DTM = 0x0000000c;  // VT_FILETIME (UTC)
        private const uint PIDSI_LASTSAVE_DTM = 0x0000000d;  // VT_FILETIME (UTC)
        private const uint PIDSI_PAGECOUNT = 0x0000000e;  // VT_I4
        private const uint PIDSI_WORDCOUNT = 0x0000000f;  // VT_I4
        private const uint PIDSI_CHARCOUNT = 0x00000010;  // VT_I4
        private const uint PIDSI_THUMBNAIL = 0x00000011;  // VT_CF
        private const uint PIDSI_APPNAME = 0x00000012;  // VT_LPSTR
        private const uint PIDSI_DOC_SECURITY = 0x00000013;  // VT_I4

        // DocSummaryInformation Property Set
        private const uint PIDDSI_CATEGORY = 0x00000002; // VT_LPSTR
        private const uint PIDDSI_PRESFORMAT = 0x00000003; // VT_LPSTR
        private const uint PIDDSI_BYTECOUNT = 0x00000004; // VT_I4
        private const uint PIDDSI_LINECOUNT = 0x00000005; // VT_I4
        private const uint PIDDSI_PARCOUNT = 0x00000006; // VT_I4
        private const uint PIDDSI_SLIDECOUNT = 0x00000007; // VT_I4
        private const uint PIDDSI_NOTECOUNT = 0x00000008; // VT_I4
        private const uint PIDDSI_HIDDENCOUNT = 0x00000009; // VT_I4
        private const uint PIDDSI_MMCLIPCOUNT = 0x0000000A; // VT_I4
        private const uint PIDDSI_SCALE = 0x0000000B; // VT_BOOL
        private const uint PIDDSI_HEADINGPAIR = 0x0000000C; // VT_VARIANT | VT_VECTOR
        private const uint PIDDSI_DOCPARTS = 0x0000000D; // VT_LPSTR | VT_VECTOR
        private const uint PIDDSI_MANAGER = 0x0000000E; // VT_LPSTR
        private const uint PIDDSI_COMPANY = 0x0000000F; // VT_LPSTR
        private const uint PIDDSI_LINKSDIRTY = 0x00000010; // VT_BOOL
        private const uint PIDDSI_CCHWITHSPACES = 0x00000011;
        private const uint PIDDSI_GUID = 0x00000012;
        private const uint PIDDSI_SHAREDDOC = 0x00000013;
        private const uint PIDDSI_LINKBASE = 0x00000014;
        private const uint PIDDSI_HLINKS = 0x00000015;
        private const uint PIDDSI_HYPERLINKSCHANGED = 0x00000016;
        private const uint PIDDSI_VERSION = 0x00000017;
        private const uint PIDDSI_DIGSIG = 0x00000018;
        private const uint PIDDSI_CONTENTTYPE = 0x0000001A;
        private const uint PIDDSI_CONTENTSTATUS = 0x0000001B;
        private const uint PIDDSI_LANGUAGE = 0x0000001C;
        private const uint PIDDSI_DOCVERSION = 0x0000001D;

        /// <summary>
        /// Defines the list of well-known properties.
        /// </summary>
        public static CompoundProperty[] KnownProperties;

        static CompoundProperty()
        {
            var list = new List<CompoundProperty>
            {
                new CompoundProperty(SummaryInformationFormatId, "ApplicationName", PIDSI_APPNAME),
                new CompoundProperty(SummaryInformationFormatId, "Author", PIDSI_AUTHOR),
                new CompoundProperty(SummaryInformationFormatId, "Characters", PIDSI_CHARCOUNT),
                new CompoundProperty(SummaryInformationFormatId, "Comments", PIDSI_COMMENTS),
                new CompoundProperty(SummaryInformationFormatId, "CreateDate", PIDSI_CREATE_DTM),
                new CompoundProperty(SummaryInformationFormatId, "Security", PIDSI_DOC_SECURITY),
                new CompoundProperty(SummaryInformationFormatId, "TotalEditingTime", PIDSI_EDITTIME),
                new CompoundProperty(SummaryInformationFormatId, "Keywords", PIDSI_KEYWORDS),
                new CompoundProperty(SummaryInformationFormatId, "LastSavedBy", PIDSI_LASTAUTHOR),
                new CompoundProperty(SummaryInformationFormatId, "LastPrintedDate", PIDSI_LASTPRINTED),
                new CompoundProperty(SummaryInformationFormatId, "LastSavedDate", PIDSI_LASTSAVE_DTM),
                new CompoundProperty(SummaryInformationFormatId, "Pages", PIDSI_PAGECOUNT),
                new CompoundProperty(SummaryInformationFormatId, "RevisionNumber", PIDSI_REVNUMBER),
                new CompoundProperty(SummaryInformationFormatId, "Subject", PIDSI_SUBJECT),
                new CompoundProperty(SummaryInformationFormatId, "Template", PIDSI_TEMPLATE),
                new CompoundProperty(SummaryInformationFormatId, "ThumbNail", PIDSI_THUMBNAIL),
                new CompoundProperty(SummaryInformationFormatId, "Title", PIDSI_TITLE),
                new CompoundProperty(SummaryInformationFormatId, "Words", PIDSI_WORDCOUNT),
                new CompoundProperty(DocSummaryInformationFormatId, "Bytes", PIDDSI_BYTECOUNT),
                new CompoundProperty(DocSummaryInformationFormatId, "Category", PIDDSI_CATEGORY),
                new CompoundProperty(DocSummaryInformationFormatId, "CharactersWithSpace", PIDDSI_CCHWITHSPACES),
                new CompoundProperty(DocSummaryInformationFormatId, "Company", PIDDSI_COMPANY),
                new CompoundProperty(DocSummaryInformationFormatId, "DigitalSignature", PIDDSI_DIGSIG),
                new CompoundProperty(DocSummaryInformationFormatId, "ContentType", PIDDSI_CONTENTTYPE),
                new CompoundProperty(DocSummaryInformationFormatId, "ContentStatus", PIDDSI_CONTENTSTATUS),
                new CompoundProperty(DocSummaryInformationFormatId, "DocParts", PIDDSI_DOCPARTS),
                new CompoundProperty(DocSummaryInformationFormatId, "DocVersion", PIDDSI_DOCVERSION),
                new CompoundProperty(DocSummaryInformationFormatId, "Guid", PIDDSI_GUID),
                new CompoundProperty(DocSummaryInformationFormatId, "HeadingPair", PIDDSI_HEADINGPAIR),
                new CompoundProperty(DocSummaryInformationFormatId, "HiddenSlides", PIDDSI_HIDDENCOUNT),
                new CompoundProperty(DocSummaryInformationFormatId, "HyperLinks", PIDDSI_HLINKS),
                new CompoundProperty(DocSummaryInformationFormatId, "HyperLinksChanged", PIDDSI_HYPERLINKSCHANGED),
                new CompoundProperty(DocSummaryInformationFormatId, "Language", PIDDSI_LANGUAGE),
                new CompoundProperty(DocSummaryInformationFormatId, "Lines", PIDDSI_LINECOUNT),
                new CompoundProperty(DocSummaryInformationFormatId, "LinkBase", PIDDSI_LINKBASE),
                new CompoundProperty(DocSummaryInformationFormatId, "LinksUpToDate", PIDDSI_LINKSDIRTY),
                new CompoundProperty(DocSummaryInformationFormatId, "Manager", PIDDSI_MANAGER),
                new CompoundProperty(DocSummaryInformationFormatId, "MMClips", PIDDSI_MMCLIPCOUNT),
                new CompoundProperty(DocSummaryInformationFormatId, "Notes", PIDDSI_NOTECOUNT),
                new CompoundProperty(DocSummaryInformationFormatId, "Paragraphs", PIDDSI_PARCOUNT),
                new CompoundProperty(DocSummaryInformationFormatId, "PresentationTarget", PIDDSI_PRESFORMAT),
                new CompoundProperty(DocSummaryInformationFormatId, "ScaleCrop", PIDDSI_SCALE),
                new CompoundProperty(DocSummaryInformationFormatId, "SharedDoc", PIDDSI_SHAREDDOC),
                new CompoundProperty(DocSummaryInformationFormatId, "Slides", PIDDSI_SLIDECOUNT),
                new CompoundProperty(DocSummaryInformationFormatId, "Version", PIDDSI_VERSION),
            };
            KnownProperties = list.ToArray();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompoundProperty"/> class.
        /// </summary>
        /// <param name="formatId">The format id.</param>
        /// <param name="name">The name.</param>
        public CompoundProperty(Guid formatId, string name)
        {
            // determine id
            CompoundProperty kp = null;
            foreach (var property in KnownProperties)
            {
                if (property.FormatId == formatId &&
                    string.Compare(name, property.Name, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    kp = property;
                    break;
                }
            }

            if (kp != null)
            {
                FormatId = kp.FormatId;
                Name = kp.Name;
                Id = kp.Id;
            }
            else
            {
                FormatId = formatId;
                Name = name;
            }
            Changed = true;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompoundProperty"/> class.
        /// </summary>
        /// <param name="formatId">The format id.</param>
        /// <param name="name">The name.</param>
        /// <param name="id">The id.</param>
        public CompoundProperty(Guid formatId, string name, uint id)
        {
            FormatId = formatId;
            Name = name;
            Id = id;
            Changed = true;
        }

        /// <summary>
        /// Gets the FMTID.
        /// </summary>
        /// <value>The format id.</value>
        public Guid FormatId { get; private set; }

        /// <summary>
        /// Gets the name.
        /// </summary>
        /// <value>The name.</value>
        public string Name { get; private set; }

        /// <summary>
        /// Gets the id.
        /// </summary>
        /// <value>The id.</value>
        public uint Id { get; private set; }

        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="CompoundProperty"/> has changed.
        /// </summary>
        /// <value><c>true</c> if changed; otherwise, <c>false</c>.</value>
        public bool Changed { get; internal set; }

        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="CompoundProperty"/> is deleted.
        /// </summary>
        /// <value><c>true</c> if deleted; otherwise, <c>false</c>.</value>
        public bool Deleted { get; internal set; }

        /// <summary>
        /// Gets a value indicating whether this instance is a known property.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this instance is a known property; otherwise, <c>false</c>.
        /// </value>
        public bool IsKnownProperty => FormatId == SummaryInformationFormatId || FormatId == DocSummaryInformationFormatId;

        /// <summary>
        /// Gets or sets the value.
        /// </summary>
        /// <value>The value.</value>
        public object Value
        {
            get => _value;
            set
            {
                _value = value;
                Changed = true;
            }
        }

        /// <summary>
        /// Returns a <see cref="string"/> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string"/> that represents this instance.
        /// </returns>
        public override string ToString() => Name;
    }
}
