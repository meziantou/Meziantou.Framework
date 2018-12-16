using System;
using System.Collections.Generic;

namespace Meziantou.Framework.CodeDom
{
    internal sealed class ParsedType
    {
        private static readonly Dictionary<string, ParsedType> _parsedTypes = new Dictionary<string, ParsedType>(StringComparer.Ordinal);

        private readonly List<ParsedType> _arguments = new List<ParsedType>();
        private string _typeName;

        public string TypeName
        {
            get => _typeName;
            private set => _typeName = value ?? throw new ArgumentNullException(nameof(value));
        }

        public string Namespace
        {
            get
            {
                var typeName = TypeName;
                if (typeName == null)
                    return null;

                var index = typeName.LastIndexOf('.');
                if (index < 0)
                    return null;

                return typeName.Substring(0, index);
            }
        }

        public string Name
        {
            get
            {
                var typeName = TypeName;
                if (typeName == null)
                    return null;

                var index = typeName.LastIndexOf('.');
                if (index < 0)
                    return typeName;

                return typeName.Substring(index + 1);
            }
        }

        public string AssemblyName { get; private set; }

        public bool IsGeneric { get; private set; }

        public ParsedType[] Arguments => _arguments.ToArray();

        static ParsedType()
        {
            _parsedTypes["string"] = new ParsedType(typeof(string).FullName);
            _parsedTypes["bool"] = new ParsedType(typeof(bool).FullName);
            _parsedTypes["int"] = new ParsedType(typeof(int).FullName);
            _parsedTypes["uint"] = new ParsedType(typeof(uint).FullName);
            _parsedTypes["long"] = new ParsedType(typeof(long).FullName);
            _parsedTypes["ulong"] = new ParsedType(typeof(ulong).FullName);
            _parsedTypes["short"] = new ParsedType(typeof(short).FullName);
            _parsedTypes["ushort"] = new ParsedType(typeof(ushort).FullName);
            _parsedTypes["byte"] = new ParsedType(typeof(byte).FullName);
            _parsedTypes["sbyte"] = new ParsedType(typeof(sbyte).FullName);
            _parsedTypes["float"] = new ParsedType(typeof(float).FullName);
            _parsedTypes["double"] = new ParsedType(typeof(double).FullName);
            _parsedTypes["decimal"] = new ParsedType(typeof(decimal).FullName);
            _parsedTypes["object"] = new ParsedType(typeof(object).FullName);
            _parsedTypes["char"] = new ParsedType(typeof(char).FullName);
            _parsedTypes["void"] = new ParsedType(typeof(void).FullName);
        }

        private ParsedType(string typeName)
        {
            if (typeName == null)
                throw new ArgumentNullException(nameof(typeName));

            TypeName = typeName.Trim();
            if (TypeName.StartsWith("[", StringComparison.Ordinal) && TypeName.EndsWith("]", StringComparison.Ordinal))
            {
                TypeName = TypeName.Substring(1, TypeName.Length - 2).Trim();
            }

            var asm = TypeName.IndexOf(',');
            if (asm >= 0)
            {
                AssemblyName = TypeName.Substring(asm + 1).Trim();
                TypeName = TypeName.Substring(0, asm).Trim();
            }
        }

        public static ParsedType Parse(string typeName)
        {
            if (typeName == null)
                throw new ArgumentNullException(nameof(typeName));

            typeName = typeName.Trim();
            if (typeName.StartsWith("Of ", StringComparison.Ordinal))
            {
                typeName = typeName.Substring(3).Trim();
            }

            if (!_parsedTypes.TryGetValue(typeName, out var pt))
            {
                pt = Parse(typeName, "<", '>');
                if (pt != null)
                {
                    _parsedTypes.Add(typeName, pt);
                    return pt;
                }

                pt = Parse(typeName, "(Of", ')');
                if (pt != null)
                {
                    _parsedTypes.Add(typeName, pt);
                    return pt;
                }

                pt = new ParsedType(typeName);
            }

            return pt;
        }

        private static ParsedType Parse(string typeName, string start, char end)
        {
            if (typeName == null)
                throw new ArgumentNullException(nameof(typeName));

            if ((typeName.StartsWith("[", StringComparison.Ordinal)) && typeName.EndsWith("]", StringComparison.Ordinal))
            {
                typeName = typeName.Substring(1, typeName.Length - 2).Trim();
            }

            ParsedType pt;
            int lt;
            int gt;
            var quot = typeName.IndexOf('`');
            if (quot >= 0)
            {
                // reflection style
                lt = typeName.IndexOf('[', quot + 1);
                gt = typeName.LastIndexOf(']');
                if ((lt < 0) || (gt < 0))
                {
                    int args;
                    var asm = typeName.IndexOf(',', quot);
                    if (asm < 0)
                    {
                        pt = new ParsedType(typeName.Substring(0, quot));
                        args = int.Parse(typeName.Substring(quot + 1));
                    }
                    else
                    {
                        pt = new ParsedType(typeName.Substring(0, quot));
                        pt.AssemblyName = typeName.Substring(asm + 1).Trim();
                        args = int.Parse(typeName.Substring(quot + 1, asm - quot - 1));
                    }
                    for (var i = 0; i < args; i++)
                    {
                        pt._arguments.Add(new ParsedType(string.Empty));
                    }
                    pt.IsGeneric = true;
                    return pt;
                }
            }
            else
            {
                lt = typeName.IndexOf(start, StringComparison.Ordinal);
                gt = typeName.LastIndexOf(end);
                if ((lt < 0) || (gt < 0))
                    return null;
            }

            if (quot >= 0)
            {
                pt = new ParsedType(typeName.Substring(0, quot));
            }
            else
            {
                pt = new ParsedType(typeName.Substring(0, lt));
            }

            pt.IsGeneric = true;

            var startPos = lt + 1;
            var parenCount = 0;
            for (var i = startPos; i < gt; i++)
            {
                var c = typeName[i];
                if (parenCount == 0)
                {
                    if (c == ',')
                    {
                        var spt = Parse(typeName.Substring(startPos, i - startPos));
                        pt._arguments.Add(spt);
                        startPos = i + 1;
                    }
                    else if (c == '[')
                    {
                        parenCount++;
                    }
                    else if (ChunkStarts(typeName, i, start))
                    {
                        parenCount++;
                        i += start.Length - 1;
                    }
                    else if ((c == end) || (c == ']'))
                    {
                        parenCount--;
                    }
                }
                else
                {
                    if (c == '[')
                    {
                        parenCount++;
                    }
                    else if (ChunkStarts(typeName, i, start))
                    {
                        parenCount++;
                        i += start.Length - 1;
                    }
                    else if ((c == end) || (c == ']'))
                    {
                        parenCount--;
                    }
                }
            }

            if (parenCount == 0)
            {
                if (gt < startPos)
                    return null;

                var spt = Parse(typeName.Substring(startPos, gt - startPos));
                pt._arguments.Add(spt);
            }

            return pt;
        }

        private static bool ChunkStarts(string text, int pos, string chunk)
        {
            for (var i = 0; i < chunk.Length; i++)
            {
                if ((i + pos) > (text.Length - 1))
                    return false;

                if (text[i + pos] != chunk[i])
                    return false;
            }

            return true;
        }
    }
}
