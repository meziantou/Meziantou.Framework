namespace Meziantou.Framework
{
    public sealed class ProcessOutput
    {
        public ProcessOutput(ProcessOutputType type, string text)
        {
            Type = type;
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }

        public ProcessOutputType Type { get; }
        public string Text { get; }

        public void Desconstruct(out ProcessOutputType type, out string text)
        {
            type = Type;
            text = Text;
        }

        public override string ToString()
        {
            return Type switch
            {
                ProcessOutputType.StandardError => "error: " + Text,
                _ => Text
            };
        }
    }
}
