using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Meziantou.Framework.Win32
{
    /// <summary>
    /// Defines a file's perceived type based on its extension.
    /// </summary>
    public sealed class Perceived
    {
        private static readonly Dictionary<string, Perceived> s_perceivedTypes = new Dictionary<string, Perceived>(StringComparer.OrdinalIgnoreCase);

        private static object SyncObject { get; } = new object();

        private Perceived(string extension, PerceivedType perceivedType, PerceivedTypeSource perceivedTypeSource)
        {
            Extension = extension;
            PerceivedType = perceivedType;
            PerceivedTypeSource = perceivedTypeSource;
        }

        public static void AddDefaultPerceivedTypes()
        {
            AddPerceived(".appxmanifest", PerceivedType.Text);
            AddPerceived(".asax", PerceivedType.Text);
            AddPerceived(".ascx", PerceivedType.Text);
            AddPerceived(".ashx", PerceivedType.Text);
            AddPerceived(".asmx", PerceivedType.Text);
            AddPerceived(".bat", PerceivedType.Text);
            AddPerceived(".class", PerceivedType.Text);
            AddPerceived(".cmd", PerceivedType.Text);
            AddPerceived(".cs", PerceivedType.Text);
            AddPerceived(".cshtml", PerceivedType.Text);
            AddPerceived(".css", PerceivedType.Text);
            AddPerceived(".cfxproj", PerceivedType.Text);
            AddPerceived(".config", PerceivedType.Text);
            AddPerceived(".csproj", PerceivedType.Text);
            AddPerceived(".dll", PerceivedType.Application);
            AddPerceived(".exe", PerceivedType.Application);
            AddPerceived(".htm", PerceivedType.Text);
            AddPerceived(".html", PerceivedType.Text);
            AddPerceived(".iqy", PerceivedType.Text);
            AddPerceived(".js", PerceivedType.Text);
            AddPerceived(".master", PerceivedType.Text);
            AddPerceived(".manifest", PerceivedType.Text);
            AddPerceived(".rdl", PerceivedType.Text);
            AddPerceived(".reg", PerceivedType.Text);
            AddPerceived(".resx", PerceivedType.Text);
            AddPerceived(".rtf", PerceivedType.Text);
            AddPerceived(".rzt", PerceivedType.Text);
            AddPerceived(".sln", PerceivedType.Text);
            AddPerceived(".sql", PerceivedType.Text);
            AddPerceived(".sqlproj", PerceivedType.Text);
            AddPerceived(".snippet", PerceivedType.Text);
            AddPerceived(".svc", PerceivedType.Text);
            AddPerceived(".tpl", PerceivedType.Text);
            AddPerceived(".tplxaml", PerceivedType.Text);
            AddPerceived(".vb", PerceivedType.Text);
            AddPerceived(".vbhtml", PerceivedType.Text);
            AddPerceived(".vbproj", PerceivedType.Text);
            AddPerceived(".vbs", PerceivedType.Text);
            AddPerceived(".vdproj", PerceivedType.Text);
            AddPerceived(".wsdl", PerceivedType.Text);
            AddPerceived(".wxi", PerceivedType.Text);
            AddPerceived(".wxl", PerceivedType.Text);
            AddPerceived(".wxs", PerceivedType.Text);
            AddPerceived(".wixlib", PerceivedType.Text);
            AddPerceived(".xaml", PerceivedType.Text);
            AddPerceived(".xsd", PerceivedType.Text);
            AddPerceived(".xsl", PerceivedType.Text);
            AddPerceived(".xslt", PerceivedType.Text);
        }

        /// <summary>
        /// Adds a perceived instance to the list.
        /// </summary>
        /// <param name="extension">The file extension. May not be null.</param>
        /// <param name="type">The perceived type.</param>
        public static Perceived AddPerceived(string extension, PerceivedType type)
        {
            if (extension == null)
                throw new ArgumentNullException(nameof(extension));

            var perceived = new Perceived(extension, type, PerceivedTypeSource.HardCoded);
            lock (SyncObject)
            {
                s_perceivedTypes[perceived.Extension] = perceived;
            }

            return perceived;
        }

        /// <summary>
        /// Gets the file's xtension.
        /// </summary>
        /// <value>The file's extension.</value>
        public string Extension { get; }

        /// <summary>
        /// Indicates the normalized perceived type.
        /// </summary>
        /// <value>The normalized perceived type.</value>
        public PerceivedType PerceivedType { get; }

        /// <summary>
        /// Indicates the source of the perceived type information.
        /// </summary>
        /// <value>the source of the perceived type information.</value>
        public PerceivedTypeSource PerceivedTypeSource { get; }

        /// <summary>
        /// Gets a file's perceived type based on its extension.
        /// </summary>
        /// <param name="fileName">The file name. May not be null..</param>
        /// <returns>An instance of the PerceivedType type.</returns>
        [SupportedOSPlatform("windows")]
        public static Perceived GetPerceivedType(string fileName)
        {
            if (fileName == null)
                throw new ArgumentNullException(nameof(fileName));

            var extension = Path.GetExtension(fileName);
            if (extension == null)
                throw new ArgumentException("The extension cannot be determined from the file name", nameof(fileName));

            extension = extension.ToLowerInvariant();

            if (s_perceivedTypes.TryGetValue(extension, out var ptype))
                return ptype;

            if (!IsSupportedPlatform())
                throw new PlatformNotSupportedException("PerceivedType is only supported on Windows");

            lock (SyncObject)
            {
                var type = PerceivedType.Unknown;
                var source = PerceivedTypeSource.Undefined;
                if (!s_perceivedTypes.TryGetValue(extension, out ptype))
                {
                    using (var key = Registry.ClassesRoot.OpenSubKey(extension, writable: false))
                    {
                        if (key != null)
                        {
                            var ct = key.GetStringValue("PerceivedType");
                            if (ct != null)
                            {
                                type = Extensions.GetEnumValue(ct, PerceivedType.Custom);
                                source = PerceivedTypeSource.SoftCoded;
                            }
                            else
                            {
                                ct = key.GetStringValue("Content Type");
                                if (ct != null)
                                {
                                    var pos = ct.IndexOf('/', StringComparison.Ordinal);
                                    if (pos > 0)
                                    {
                                        type = Extensions.GetEnumValue(ct.Substring(0, pos), PerceivedType.Custom);
                                        source = PerceivedTypeSource.Mime;
                                    }
                                }
                            }
                        }
                    }

                    if (type == PerceivedType.Unknown)
                    {
                        var text = IntPtr.Zero;
                        type = PerceivedType.Unknown;
                        source = PerceivedTypeSource.Undefined;
                        var hr = AssocGetPerceivedType(extension, ref type, ref source, ref text);
                        if (hr != 0)
                        {
                            type = PerceivedType.Unspecified;
                            source = PerceivedTypeSource.Undefined;
                        }
                    }

                    ptype = new Perceived(extension, type, source);
                    s_perceivedTypes.Add(extension, ptype);
                }

                return ptype;
            }
        }

        /// <summary>
        /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </returns>
        public override string ToString()
        {
            return Extension + ":" + PerceivedType + " (" + PerceivedTypeSource + ")";
        }

        private static bool IsSupportedPlatform()
        {
            return Environment.OSVersion.Platform == PlatformID.Win32NT;
        }

        [DllImport("shlwapi.dll")]
        private static extern int AssocGetPerceivedType(
            [MarshalAs(UnmanagedType.LPWStr)] string pszExt,
            ref PerceivedType ptype,
            ref PerceivedTypeSource pflag,
            ref IntPtr ppszType);
    }
}
