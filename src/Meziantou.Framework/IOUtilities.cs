using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Meziantou.Framework
{
    public static class IOUtilities
    {
        private static readonly string[] ReservedFileNames = new[]
         {
            "con", "prn", "aux", "nul",
            "com0", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
            "lpt0", "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
        };

        /// <summary>
        /// Determines whether the specified exception is a sharing violation exception.
        /// </summary>
        /// <param name="exception">The exception. May not be null.</param>
        /// <returns>
        /// 	<c>true</c> if the specified exception is a sharing violation exception; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsSharingViolation(IOException exception)
        {
            if (exception == null)
                throw new ArgumentNullException(nameof(exception));

            var hr = exception.HResult;
            return hr == -2147024864; // 0x80070020 ERROR_SHARING_VIOLATION
        }

        /// <summary>
        /// Makes sure a directory exists for a given file path.
        /// </summary>
        /// <param name="filePath">The file path. Note this is not to be confused with the directory path. May not be null.</param>
        public static void PathCreateDirectory(string filePath)
        {
            if (filePath == null)
                throw new ArgumentNullException(nameof(filePath));

            if (!Path.IsPathRooted(filePath))
            {
                filePath = Path.GetFullPath(filePath);
            }

            var dir = Path.GetDirectoryName(filePath);
            if (dir == null)
                return;

            Directory.CreateDirectory(dir);
        }

        /// <summary>
        /// Unprotects the given file path.
        /// </summary>
        /// <param name="path">The file path. May not be null.</param>
        public static void PathUnprotect(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            var fi = new FileInfo(path);
            if (fi.Exists)
            {
                if (fi.IsReadOnly)
                {
                    fi.IsReadOnly = false;
                }
            }
        }

        public static bool ArePathEqual(string path1, string path2)
        {
            if (path1 == null)
                throw new ArgumentNullException(nameof(path1));
            if (path2 == null)
                throw new ArgumentNullException(nameof(path2));

            var uri1 = new Uri(path1);
            var uri2 = new Uri(path2);

            return Uri.Compare(uri1, uri2, UriComponents.AbsoluteUri, UriFormat.UriEscaped, StringComparison.OrdinalIgnoreCase) == 0;
        }

        public static bool IsChildPathOf(string parent, string child)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            if (child == null)
                throw new ArgumentNullException(nameof(child));

            var parentUri = new Uri(parent);
            var childUri = new Uri(child);

            return parentUri.IsBaseOf(childUri);
        }

        public static string MakeRelativePath(string root, string path)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            var parentUri = new Uri(root);
            var childUri = new Uri(path);

            var relativeUri = parentUri.MakeRelativeUri(childUri).ToString();
            return relativeUri.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Converts a text into a valid file name.
        /// </summary>
        /// <param name="fileName">The file name.</param>
        /// <param name="reservedNameFormat">The reserved format to use for reserved names. If null '_{0}_' will be used.</param>
        /// <param name="reservedCharFormat">The reserved format to use for reserved characters. If null '_x{0}_' will be used.</param>
        /// <returns>
        /// A valid file name.
        /// </returns>
        public static string ToValidFileName(string fileName, string reservedNameFormat = "_{0}_", string reservedCharFormat = "_x{0}_")
        {
            if (fileName == null)
                throw new ArgumentNullException(nameof(fileName));
            if (reservedNameFormat == null)
                throw new ArgumentNullException(nameof(reservedNameFormat));
            if (reservedCharFormat == null)
                throw new ArgumentNullException(nameof(reservedCharFormat));

            if (Array.IndexOf(ReservedFileNames, fileName.ToLowerInvariant()) >= 0 ||
                IsAllDots(fileName))
            {
                return string.Format(reservedNameFormat, fileName);
            }

            var invalid = Path.GetInvalidFileNameChars();

            var sb = new StringBuilder(fileName.Length);
            foreach (var c in fileName)
            {
                if (Array.IndexOf(invalid, c) >= 0)
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, reservedCharFormat, (short)c);
                }
                else
                {
                    sb.Append(c);
                }
            }

            var s = sb.ToString();
            if (string.Equals(s, fileName, StringComparison.Ordinal))
            {
                s = fileName;
            }

            return s;
        }

        private static bool IsAllDots(string fileName)
        {
            foreach (var c in fileName)
            {
                if (c != '.')
                    return false;
            }
            return true;
        }
    }
}
