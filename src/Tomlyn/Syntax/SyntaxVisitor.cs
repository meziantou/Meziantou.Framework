// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Collections.Generic;

namespace Tomlyn.Syntax
{
    /// <summary>
    /// Visitor base class for traversing TOML syntax trees.
    /// </summary>
    public abstract class SyntaxVisitor
    {
        /// <summary>
        /// Visits a syntax list node.
        /// </summary>
        /// <param name="list">The list node.</param>
        public virtual void Visit(SyntaxList list)
        {
            DefaultVisit(list);
        }
        /// <summary>
        /// Visits a document node.
        /// </summary>
        /// <param name="document">The document node.</param>
        public virtual void Visit(DocumentSyntax document)
        {
            DefaultVisit(document);
        }
        /// <summary>
        /// Visits a key/value node.
        /// </summary>
        /// <param name="keyValue">The key/value node.</param>
        public virtual void Visit(KeyValueSyntax keyValue)
        {
            DefaultVisit(keyValue);
        }
        /// <summary>
        /// Visits a string value node.
        /// </summary>
        /// <param name="stringValue">The string value node.</param>
        public virtual void Visit(StringValueSyntax stringValue)
        {
            DefaultVisit(stringValue);
        }
        /// <summary>
        /// Visits an integer value node.
        /// </summary>
        /// <param name="integerValue">The integer value node.</param>
        public virtual void Visit(IntegerValueSyntax integerValue)
        {
            DefaultVisit(integerValue);
        }
        /// <summary>
        /// Visits a boolean value node.
        /// </summary>
        /// <param name="boolValue">The boolean value node.</param>
        public virtual void Visit(BooleanValueSyntax boolValue)
        {
            DefaultVisit(boolValue);
        }
        /// <summary>
        /// Visits a floating-point value node.
        /// </summary>
        /// <param name="floatValue">The float value node.</param>
        public virtual void Visit(FloatValueSyntax floatValue)
        {
            DefaultVisit(floatValue);
        }
        /// <summary>
        /// Visits a table node.
        /// </summary>
        /// <param name="table">The table node.</param>
        public virtual void Visit(TableSyntax table)
        {
            DefaultVisit(table);
        }

        /// <summary>
        /// Visits a table array node.
        /// </summary>
        /// <param name="table">The table array node.</param>
        public virtual void Visit(TableArraySyntax table)
        {
            DefaultVisit(table);
        }

        /// <summary>
        /// Visits a token node.
        /// </summary>
        /// <param name="token">The token node.</param>
        public virtual void Visit(SyntaxToken token)
        {
            DefaultVisit(token);
        }
        /// <summary>
        /// Visits a trivia node.
        /// </summary>
        /// <param name="trivia">The trivia node.</param>
        public virtual void Visit(SyntaxTrivia trivia)
        {
        }
        /// <summary>
        /// Visits a bare key node.
        /// </summary>
        /// <param name="bareKey">The bare key node.</param>
        public virtual void Visit(BareKeySyntax bareKey)
        {
            DefaultVisit(bareKey);
        }
        /// <summary>
        /// Visits a key node.
        /// </summary>
        /// <param name="key">The key node.</param>
        public virtual void Visit(KeySyntax key)
        {
            DefaultVisit(key);
        }
        /// <summary>
        /// Visits a date/time value node.
        /// </summary>
        /// <param name="dateTime">The date/time value node.</param>
        public virtual void Visit(DateTimeValueSyntax dateTime)
        {
            DefaultVisit(dateTime);
        }

        /// <summary>
        /// Visits an array node.
        /// </summary>
        /// <param name="array">The array node.</param>
        public virtual void Visit(ArraySyntax array)
        {
            DefaultVisit(array);
        }
        /// <summary>
        /// Visits an inline table item node.
        /// </summary>
        /// <param name="inlineTableItem">The inline table item node.</param>
        public virtual void Visit(InlineTableItemSyntax inlineTableItem)
        {
            DefaultVisit(inlineTableItem);
        }
        /// <summary>
        /// Visits an array item node.
        /// </summary>
        /// <param name="arrayItem">The array item node.</param>
        public virtual void Visit(ArrayItemSyntax arrayItem)
        {
            DefaultVisit(arrayItem);
        }
        /// <summary>
        /// Visits a dotted key item node.
        /// </summary>
        /// <param name="dottedKeyItem">The dotted key item node.</param>
        public virtual void Visit(DottedKeyItemSyntax dottedKeyItem)
        {
            DefaultVisit(dottedKeyItem);
        }
        /// <summary>
        /// Visits an inline table node.
        /// </summary>
        /// <param name="inlineTable">The inline table node.</param>
        public virtual void Visit(InlineTableSyntax inlineTable)
        {
            DefaultVisit(inlineTable);
        }

        /// <summary>
        /// Default visit implementation that walks children and trivia.
        /// </summary>
        /// <param name="node">The node to visit.</param>
        public virtual void DefaultVisit(SyntaxNode? node)
        {
            if (node == null) return;

            VisitTrivias(node.LeadingTrivia);

            for (int i = 0; i < node.ChildrenCount; i++)
            {
                var child = node.GetChild(i);
                if (child != null)
                {
                    child.Accept(this);
                }
            }

            VisitTrivias(node.TrailingTrivia);
        }

        private void VisitTrivias(List<SyntaxTrivia>? trivias)
        {
            if (trivias != null)
            {
                foreach (var trivia in trivias)
                {
                    if (trivia != null)
                    {
                        trivia.Accept(this);
                    }
                }
            }
        }
    }
}