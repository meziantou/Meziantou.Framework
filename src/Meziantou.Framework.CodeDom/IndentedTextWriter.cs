using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;

namespace Meziantou.Framework.CodeDom
{
    public class IndentedTextWriter : TextWriter
    {
        private int _indentLevel;
        private bool _tabsPending;
        public const string DefaultTabString = "    ";

        public override Encoding Encoding => InnerWriter.Encoding;

        public override string NewLine
        {
            get => InnerWriter.NewLine;
            [param: AllowNull]
            set => InnerWriter.NewLine = value;
        }

        public int Indent
        {
            get => _indentLevel;
            set
            {
                if (value < 0)
                {
                    value = 0;
                }

                _indentLevel = value;
            }
        }

        public bool CloseWriter { get; }
        public TextWriter InnerWriter { get; }
        public string TabString { get; }

        public IndentedTextWriter(TextWriter writer)
          : this(writer, DefaultTabString)
        {
        }

        public IndentedTextWriter(TextWriter writer, string tabString)
            : this(writer, tabString, closeWriter: true)
        {
        }

        public IndentedTextWriter(TextWriter writer, string tabString, bool closeWriter)
          : base(CultureInfo.InvariantCulture)
        {
            TabString = tabString;
            InnerWriter = writer;
            CloseWriter = closeWriter;
        }

        public override void Close()
        {
            if (!CloseWriter)
                return;

            InnerWriter.Close();
        }

        public override void Flush()
        {
            InnerWriter.Flush();
        }

        protected virtual void OutputTabs()
        {
            if (!_tabsPending)
                return;

            for (var index = 0; index < _indentLevel; index++)
            {
                InnerWriter.Write(TabString);
            }

            _tabsPending = false;
        }

        public override void Write(string? value)
        {
            if (!string.Equals(value, NewLine, StringComparison.Ordinal))
            {
                OutputTabs();
            }

            InnerWriter.Write(value);
            if (value != null && value.EndsWith(NewLine, StringComparison.Ordinal))
            {
                _tabsPending = true;
            }
        }

        public override void Write(bool value)
        {
            OutputTabs();
            InnerWriter.Write(value);
        }

        public override void Write(char value)
        {
            OutputTabs();
            InnerWriter.Write(value);
        }

        public override void Write(char[]? buffer)
        {
            OutputTabs();
            InnerWriter.Write(buffer);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            OutputTabs();
            InnerWriter.Write(buffer, index, count);
        }

        public override void Write(double value)
        {
            OutputTabs();
            InnerWriter.Write(value);
        }

        public override void Write(float value)
        {
            OutputTabs();
            InnerWriter.Write(value);
        }

        public override void Write(int value)
        {
            OutputTabs();
            InnerWriter.Write(value);
        }

        public override void Write(long value)
        {
            OutputTabs();
            InnerWriter.Write(value);
        }

        public override void Write(object? value)
        {
            OutputTabs();
            InnerWriter.Write(value);
        }

        public override void Write(string format, object? arg0)
        {
            OutputTabs();
            InnerWriter.Write(format, arg0);
        }

        public override void Write(string format, object? arg0, object? arg1)
        {
            OutputTabs();
            InnerWriter.Write(format, arg0, arg1);
        }

        public override void Write(string format, params object?[] arg)
        {
            OutputTabs();
            InnerWriter.Write(format, arg);
        }

        public void WriteLineNoTabs(string s)
        {
            InnerWriter.WriteLine(s);
        }

        public override void WriteLine(string? value)
        {
            if (value == null)
            {
                WriteLine();
                return;
            }

            OutputTabs();
            InnerWriter.WriteLine(value);
            _tabsPending = true;
        }

        public override void WriteLine()
        {
            //OutputTabs(); // do not create a line with only empty spaces
            InnerWriter.WriteLine();
            _tabsPending = true;
        }

        public override void WriteLine(bool value)
        {
            OutputTabs();
            InnerWriter.WriteLine(value);
            _tabsPending = true;
        }

        public override void WriteLine(char value)
        {
            OutputTabs();
            InnerWriter.WriteLine(value);
            _tabsPending = true;
        }

        public override void WriteLine(char[]? buffer)
        {
            OutputTabs();
            InnerWriter.WriteLine(buffer);
            _tabsPending = true;
        }

        public override void WriteLine(char[] buffer, int index, int count)
        {
            OutputTabs();
            InnerWriter.WriteLine(buffer, index, count);
            _tabsPending = true;
        }

        public override void WriteLine(double value)
        {
            OutputTabs();
            InnerWriter.WriteLine(value);
            _tabsPending = true;
        }

        public override void WriteLine(float value)
        {
            OutputTabs();
            InnerWriter.WriteLine(value);
            _tabsPending = true;
        }

        public override void WriteLine(int value)
        {
            OutputTabs();
            InnerWriter.WriteLine(value);
            _tabsPending = true;
        }

        public override void WriteLine(long value)
        {
            OutputTabs();
            InnerWriter.WriteLine(value);
            _tabsPending = true;
        }

        public override void WriteLine(object? value)
        {
            OutputTabs();
            InnerWriter.WriteLine(value);
            _tabsPending = true;
        }

        public override void WriteLine(string format, object? arg0)
        {
            OutputTabs();
            InnerWriter.WriteLine(format, arg0);
            _tabsPending = true;
        }

        public override void WriteLine(string format, object? arg0, object? arg1)
        {
            OutputTabs();
            InnerWriter.WriteLine(format, arg0, arg1);
            _tabsPending = true;
        }

        public override void WriteLine(string format, params object?[] arg)
        {
            OutputTabs();
            InnerWriter.WriteLine(format, arg);
            _tabsPending = true;
        }

        public override void WriteLine(uint value)
        {
            OutputTabs();
            InnerWriter.WriteLine(value);
            _tabsPending = true;
        }
    }
}
