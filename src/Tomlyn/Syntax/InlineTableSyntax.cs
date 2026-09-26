using System;

namespace Tomlyn.Syntax
{
    /// <summary>
    /// An inline table TOML syntax node.
    /// </summary>
    public sealed class InlineTableSyntax : ValueSyntax
    {
        private SyntaxToken? _openBrace;
        private SyntaxToken? _closeBrace;

        /// <summary>
        /// Creates a new instance of an <see cref="InlineTableSyntax"/>
        /// </summary>
        public InlineTableSyntax() : base(SyntaxKind.InlineTable)
        {
            Items = new SyntaxList<InlineTableItemSyntax>() { Parent = this };
        }

        /// <summary>
        /// Creates a new instance of an <see cref="InlineTableSyntax"/>
        /// </summary>
        /// <param name="keyValues">The key values of this inline table</param>
        public InlineTableSyntax(params KeyValueSyntax[] keyValues) : this()
        {
            ArgumentNullException.ThrowIfNull(keyValues);
            OpenBrace = SyntaxFactory.Token(TokenKind.OpenBrace).AddTrailingWhitespace();
            CloseBrace = SyntaxFactory.Token(TokenKind.CloseBrace).AddLeadingWhitespace();

            for (var i = 0; i < keyValues.Length; i++)
            {
                var keyValue = keyValues[i];
                Items.Add(new InlineTableItemSyntax(keyValue)
                {
                    Comma = (i + 1 < keyValues.Length) ? SyntaxFactory.Token(TokenKind.Comma).AddTrailingWhitespace() : null
                });
            }
        }

        /// <summary>
        /// The token open brace `{`
        /// </summary>
        public SyntaxToken? OpenBrace
        {
            get => _openBrace;
            set => ParentToThis(ref _openBrace, value, TokenKind.OpenBrace);
        }

        /// <summary>
        /// The items of this table.
        /// </summary>
        public SyntaxList<InlineTableItemSyntax> Items { get; }

        /// <summary>
        /// The token close brace `}`
        /// </summary>
        public SyntaxToken? CloseBrace
        {
            get => _closeBrace;
            set => ParentToThis(ref _closeBrace, value, TokenKind.CloseBrace);
        }

        /// <inheritdoc />
        public override void Accept(SyntaxVisitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override int ChildrenCount => 3;

        /// <inheritdoc />
        protected override SyntaxNode? GetChildImpl(int index)
        {
            switch (index)
            {
                case 0:
                    return OpenBrace;
                case 1:
                    return Items;
                default:
                    return CloseBrace;
            }
        }

        /// <inheritdoc />
        protected override string ToDebuggerDisplay()
        {
            return $"{base.ToDebuggerDisplay()} Count = {Items.ChildrenCount}";
        }
    }
}