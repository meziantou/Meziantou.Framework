using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Meziantou.Framework.CodeDom
{
    public partial class CSharpCodeGenerator
    {
        private static IDictionary<string, string> _predefinedTypes;
        private static string[] _keywords;

        static CSharpCodeGenerator()
        {
            _predefinedTypes = new Dictionary<string, string>();
            _predefinedTypes[typeof(bool).FullName] = "bool";
            _predefinedTypes[typeof(byte).FullName] = "byte";
            _predefinedTypes[typeof(char).FullName] = "char";
            _predefinedTypes[typeof(decimal).FullName] = "decimal";
            _predefinedTypes[typeof(double).FullName] = "double";
            _predefinedTypes[typeof(float).FullName] = "float";
            _predefinedTypes[typeof(int).FullName] = "int";
            _predefinedTypes[typeof(long).FullName] = "long";
            _predefinedTypes[typeof(object).FullName] = "object";
            _predefinedTypes[typeof(sbyte).FullName] = "sbyte";
            _predefinedTypes[typeof(short).FullName] = "short";
            _predefinedTypes[typeof(string).FullName] = "string";
            _predefinedTypes[typeof(uint).FullName] = "uint";
            _predefinedTypes[typeof(ulong).FullName] = "ulong";
            _predefinedTypes[typeof(ushort).FullName] = "ushort";
            _predefinedTypes[typeof(void).FullName] = "void";

            _keywords = new string[]
            {
                "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong", "double", "float", "decimal",
                "string", "char", "void", "object", "typeof", "sizeof", "null", "true", "false", "if", "else", "while", "for", "foreach", "do", "switch",
                "case", "default", "lock", "try", "throw", "catch", "finally", "goto", "break", "continue", "return", "public", "private", "internal",
                "protected", "static", "readonly", "sealed", "const", "fixed", "stackalloc", "volatile", "new", "override", "abstract", "virtual",
                "event", "extern", "ref", "out", "in", "is", "as", "params", "__arglist", "__makeref", "__reftype", "__refvalue", "this", "base",
                "namespace", "using", "class", "struct", "interface", "enum", "delegate", "checked", "unchecked", "unsafe", "operator", "implicit", "explicit"
            };
        }

        public string Write(CodeObject codeObject)
        {
            if (codeObject == null) throw new ArgumentNullException(nameof(codeObject));

            using (var sw = new StringWriter())
            {
                Write(sw, codeObject);
                return sw.ToString();
            }
        }

        public void Write(TextWriter writer, CodeObject codeObject)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (codeObject == null) throw new ArgumentNullException(nameof(codeObject));

            using (var indentedTextWriter = new IndentedTextWriter(writer, IndentedTextWriter.DefaultTabString, false))
            {
                Write(indentedTextWriter, codeObject);
            }
        }

        public void Write(IndentedTextWriter writer, CodeObject codeObject)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (codeObject == null) throw new ArgumentNullException(nameof(codeObject));

            switch (codeObject)
            {
                case CodeCompilationUnit o:
                    Write(writer, o);
                    break;

                case CodeNamespaceDeclaration o:
                    Write(writer, o);
                    break;

                case CodeTypeDeclaration o:
                    Write(writer, o);
                    break;

                case CodeExpression o:
                    Write(writer, o);
                    break;

                case CodeStatement o:
                    Write(writer, o, _defaultWriteStatementOptions);
                    break;

                case CodeDirective o:
                    Write(writer, o);
                    break;

                case CodeMemberDeclaration o:
                    Write(writer, o);
                    break;

                case CodeMethodArgumentDeclaration o:
                    Write(writer, o);
                    break;

                case CodeCustomAttribute o:
                    Write(writer, o);
                    break;

                case CodeCustomAttributeArgument o:
                    Write(writer, o);
                    break;

                case CodeCatchClauseCollection o:
                    Write(writer, o);
                    break;

                case CodeCatchClause o:
                    Write(writer, o);
                    break;

                case CodeConstructorInitializer o:
                    Write(writer, o);
                    break;

                case CodeTypeParameterConstraint o:
                    Write(writer, o);
                    break;

                default:
                    throw new NotSupportedException();
            }
        }

        protected virtual void Write(IndentedTextWriter writer, CodeCompilationUnit unit)
        {
            Write(writer, unit.Usings, writer.NewLine);
            Write(writer, unit.Namespaces, writer.NewLine);
            Write(writer, unit.Types, writer.NewLine);
        }

        protected virtual void Write(IndentedTextWriter writer, CodeNamespaceDeclaration ns)
        {
            writer.Write("namespace ");
            WriteIdentifier(writer, ns.Name);
            writer.WriteLine();
            writer.WriteLine("{");
            writer.Indent++;
            Write(writer, ns.Usings, writer.NewLine);
            Write(writer, ns.Types, writer.NewLine);
            Write(writer, ns.Namespaces, writer.NewLine);
            writer.Indent--;
            writer.WriteLine("}");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeTypeDeclaration type)
        {
            Write(writer, type.CustomAttributes);
            Write(writer, type.Modifiers);
            switch (type)
            {
                case CodeClassDeclaration o:
                    Write(writer, o);
                    break;

                case CodeEnumerationDeclaration o:
                    Write(writer, o);
                    break;

                case CodeInterfaceDeclaration o:
                    Write(writer, o);
                    break;

                default:
                    throw new NotSupportedException();
            }
        }

        protected virtual void Write(IndentedTextWriter writer, CodeEnumerationDeclaration enumeration)
        {
            writer.Write("enum ");
            WriteIdentifier(writer, enumeration.Name);

            if (enumeration.BaseType != null)
            {
                writer.Write(" : ");
                Write(writer, enumeration.BaseType);
            }

            writer.WriteLine();
            writer.WriteLine("{");
            writer.Indent++;

            Write(writer, enumeration.Members, (e) =>
            {
                if (!e.Last)
                {
                    writer.WriteLine(",");
                }
            });

            writer.WriteLine();
            writer.Indent--;
            writer.WriteLine("}");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeEnumerationMember member)
        {
            Write(writer, member.CustomAttributes);
            WriteIdentifier(writer, member.Name);
            if (member.Value != null)
            {
                writer.Write(" = ");
                Write(writer, member.Value);
            }
        }

        protected virtual void Write(IndentedTextWriter writer, CodeMethodDeclaration member)
        {
            Write(writer, member.CustomAttributes);
            Write(writer, member.Modifiers);
            if (member.ReturnType == null)
            {
                writer.Write("void ");
            }
            else
            {
                Write(writer, member.ReturnType);
                writer.Write(" ");
            }
            WriteIdentifier(writer, member.Name);
            WriteGenericParameters(writer, member);
            writer.Write("(");
            Write(writer, member.Arguments);
            writer.Write(")");
            if (member.Statements != null || member.HasConstraints())
            {
                writer.WriteLine();
            }

            WriteGenericParameterConstraints(writer, member);
            if (member.Statements == null)
            {
                if (member.HasConstraints())
                {
                    writer.Indent++;
                    writer.WriteLine(";");
                    writer.Indent--;
                }
                else
                {
                    writer.WriteLine(";");
                }
            }
            else
            {
                Write(writer, member.Statements);
            }
        }

        protected virtual void Write(IndentedTextWriter writer, IEnumerable<CodeMethodArgumentDeclaration> args)
        {
            Write(writer, args, ", ");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeMethodArgumentDeclaration arg)
        {
            Write(writer, arg.CustomAttributes);
            Write(writer, arg.Direction);
            Write(writer, arg.Type);
            writer.Write(" ");
            WriteIdentifier(writer, arg.Name);
            if (arg.DefaultValue != null)
            {
                writer.Write(" = ");
                Write(writer, arg.DefaultValue);
            }
        }

        protected virtual void Write(IndentedTextWriter writer, Direction direction)
        {
            switch (direction)
            {
                case Direction.Out:
                    writer.Write("out ");
                    break;

                case Direction.InOut:
                    writer.Write("ref ");
                    break;

                case Direction.In:
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(direction));
            }
        }

        protected virtual void Write(IndentedTextWriter writer, CodeFieldDeclaration member)
        {
            Write(writer, member.CustomAttributes);
            Write(writer, member.Modifiers);
            if (member.Type == null)
            {
                writer.Write("var ");
            }
            else
            {
                Write(writer, member.Type);
                writer.Write(" ");
            }

            WriteIdentifier(writer, member.Name);

            if (member.InitExpression != null)
            {
                writer.Write(" = ");
                Write(writer, member.InitExpression);
            }

            writer.WriteLine(";");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeEventFieldDeclaration member)
        {
            Write(writer, member.CustomAttributes);
            Write(writer, member.Modifiers);
            writer.Write("event ");
            if (member.Type == null)
            {
                writer.Write("var ");
            }
            else
            {
                Write(writer, member.Type);
                writer.Write(" ");
            }

            WriteIdentifier(writer, member.Name);
            writer.WriteLine(";");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeConstructorDeclaration member)
        {
            Write(writer, member.CustomAttributes);
            Write(writer, member.Modifiers);

            var name = member.ParentType?.Name ?? member.Name;
            if (name != null)
            {
                WriteIdentifier(writer, name);
            }

            writer.Write("(");
            Write(writer, member.Arguments);
            writer.Write(")");
            if (member.Initializer != null)
            {
                writer.WriteLine();
                writer.Indent++;
                Write(writer, member.Initializer);
                writer.Indent--;
            }
            writer.WriteLine();
            WriteStatementsOrEmptyBlock(writer, member.Statements);
        }

        protected virtual void Write(IndentedTextWriter writer, CodePropertyDeclaration member)
        {
            Write(writer, member.CustomAttributes);
            Write(writer, member.Modifiers);
            Write(writer, member.Type);
            writer.Write(" ");
            WriteIdentifier(writer, member.Name);

            writer.WriteLine();
            writer.WriteLine("{");
            writer.Indent++;

            if (member.Getter != null)
            {
                writer.WriteLine("get");
                WriteStatementsOrEmptyBlock(writer, member.Getter);
            }

            if (member.Setter != null)
            {
                writer.WriteLine("set");
                WriteStatementsOrEmptyBlock(writer, member.Setter);
            }

            writer.Indent--;
            writer.WriteLine("}");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeClassDeclaration type)
        {
            writer.Write("class ");
            WriteIdentifier(writer, type.Name);
            WriteGenericParameters(writer, type);

            var baseTypes = GetBaseTypes(type);
            if (baseTypes.Any())
            {
                writer.Write(" : ");
                Write(writer, baseTypes, ", ");
            }

            writer.WriteLine();
            WriteGenericParameterConstraints(writer, type);

            writer.WriteLine("{");
            writer.Indent++;
            WriteLines(writer, type.Members.Cast<CodeObject>().Concat(type.Types), null);
            writer.Indent--;
            writer.WriteLine("}");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeInterfaceDeclaration type)
        {
            writer.Write("interface ");
            WriteIdentifier(writer, type.Name);
            WriteGenericParameters(writer, type);

            var baseTypes = GetBaseTypes(type);
            if (baseTypes.Any())
            {
                writer.Write(" : ");
                Write(writer, baseTypes, ", ");
            }

            writer.WriteLine();
            WriteGenericParameterConstraints(writer, type);

            writer.WriteLine("{");
            writer.Indent++;
            Write(writer, type.Members, writer.NewLine);
            Write(writer, type.Types, writer.NewLine);
            writer.Indent--;
            writer.WriteLine("}");
        }

        protected virtual void Write(IndentedTextWriter writer, Modifiers modifiers)
        {
            if ((modifiers & Modifiers.Private) == Modifiers.Private)
            {
                writer.Write("private ");
            }
            if ((modifiers & Modifiers.Protected) == Modifiers.Protected)
            {
                writer.Write("protected ");
            }
            if ((modifiers & Modifiers.Internal) == Modifiers.Internal)
            {
                writer.Write("internal ");
            }
            if ((modifiers & Modifiers.Public) == Modifiers.Public)
            {
                writer.Write("public ");
            }

            if ((modifiers & Modifiers.Partial) == Modifiers.Partial)
            {
                writer.Write("partial ");
            }

            if ((modifiers & Modifiers.Abstract) == Modifiers.Abstract)
            {
                writer.Write("abstract ");
            }
            if ((modifiers & Modifiers.Override) == Modifiers.Override)
            {
                writer.Write("override ");
            }
            if ((modifiers & Modifiers.Sealed) == Modifiers.Sealed)
            {
                writer.Write("sealed ");
            }
            if ((modifiers & Modifiers.Static) == Modifiers.Static)
            {
                writer.Write("static ");
            }

            if ((modifiers & Modifiers.Async) == Modifiers.Async)
            {
                writer.Write("async ");
            }
            if ((modifiers & Modifiers.Const) == Modifiers.Const)
            {
                writer.Write("const ");
            }
            if ((modifiers & Modifiers.New) == Modifiers.New)
            {
                writer.Write("new ");
            }
            if ((modifiers & Modifiers.ReadOnly) == Modifiers.ReadOnly)
            {
                writer.Write("readonly ");
            }
            if ((modifiers & Modifiers.Unsafe) == Modifiers.Unsafe)
            {
                writer.Write("unsafe ");
            }
            if ((modifiers & Modifiers.Virtual) == Modifiers.Virtual)
            {
                writer.Write("virtual ");
            }
            if ((modifiers & Modifiers.Volatile) == Modifiers.Volatile)
            {
                writer.Write("volatile ");
            }
        }

        protected virtual void Write(IndentedTextWriter writer, ICollection<CodeCustomAttribute> attributes)
        {
            if (attributes.Count > 0)
            {
                Write(writer, attributes, writer.NewLine);
                writer.WriteLine();
            }
        }

        protected virtual void Write(IndentedTextWriter writer, CodeCustomAttribute attribute)
        {
            writer.Write("[");
            Write(writer, attribute.Type);

            if (attribute.Arguments.Count > 0)
            {
                writer.Write("(");
                Write(writer, attribute.Arguments.OrderBy(GetSortOrder), ", ");
                writer.Write(")");
            }

            writer.Write("]");

            int GetSortOrder(CodeCustomAttributeArgument arg)
            {
                if (arg.PropertyName == null)
                    return 0;

                return 1;
            }
        }

        protected virtual void Write(IndentedTextWriter writer, CodeCustomAttributeArgument arg)
        {
            if (!string.IsNullOrEmpty(arg.PropertyName))
            {
                WriteIdentifier(writer, arg.PropertyName);
                writer.Write(" = ");
            }

            Write(writer, arg.Value);
        }

        protected virtual void Write(IndentedTextWriter writer, CodeMemberDeclaration member)
        {
            switch (member)
            {
                case CodeEnumerationMember o:
                    Write(writer, o);
                    break;

                case CodeMethodDeclaration o:
                    Write(writer, o);
                    break;

                case CodeFieldDeclaration o:
                    Write(writer, o);
                    break;

                case CodeConstructorDeclaration o:
                    Write(writer, o);
                    break;

                case CodePropertyDeclaration o:
                    Write(writer, o);
                    break;

                case CodeEventFieldDeclaration o:
                    Write(writer, o);
                    break;

                default:
                    throw new NotSupportedException();
            }
        }

        protected virtual void Write(IndentedTextWriter writer, CodeDirective directive)
        {
            switch (directive)
            {
                case CodeUsingDirective o:
                    Write(writer, o);
                    break;

                default:
                    throw new NotSupportedException();
            }
        }

        protected virtual void Write(IndentedTextWriter writer, CodeUsingDirective usingDirective)
        {
            writer.Write("using ");
            writer.Write(usingDirective.Namespace);
            writer.Write(";");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeCatchClause catchClause)
        {
            writer.Write("catch");
            if (catchClause.ExceptionType != null)
            {
                writer.Write(" (");
                Write(writer, catchClause.ExceptionType);
                if (!string.IsNullOrEmpty(catchClause.ExceptionVariableName))
                {
                    writer.Write(" ");
                    writer.Write(catchClause.ExceptionVariableName);
                }
                writer.Write(")");
            }
            writer.WriteLine();

            WriteStatementsOrEmptyBlock(writer, catchClause.Body);
        }

        protected virtual void Write(IndentedTextWriter writer, CodeCatchClauseCollection clauses)
        {
            Write(writer, clauses, "");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeConstructorInitializer initializer)
        {
            writer.Write(": ");
            switch (initializer)
            {
                case CodeConstructorThisInitializer o:
                    writer.Write("this");
                    break;

                case CodeConstructorBaseInitializer o:
                    writer.Write("base");
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(initializer));
            }

            writer.Write('(');
            Write(writer, initializer.Arguments, ", ");
            writer.Write(')');
        }

        protected virtual void Write(IndentedTextWriter writer, CodeTypeParameterConstraint constraint)
        {
            switch (constraint)
            {
                case CodeBaseTypeParameterConstraint o:
                    Write(writer, o);
                    break;

                case CodeClassTypeParameterConstraint o:
                    Write(writer, o);
                    break;

                case CodeValueTypeTypeParameterConstraint o:
                    Write(writer, o);
                    break;

                case CodeConstructorParameterConstraint o:
                    Write(writer, o);
                    break;

                default:
                    throw new NotSupportedException();
            }
        }

        protected virtual void Write(IndentedTextWriter writer, CodeBaseTypeParameterConstraint constraint)
        {
            Write(writer, constraint.Type);
        }

        protected virtual void Write(IndentedTextWriter writer, CodeClassTypeParameterConstraint constraint)
        {
            writer.Write("class");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeValueTypeTypeParameterConstraint constraint)
        {
            writer.Write("struct");
        }

        protected virtual void Write(IndentedTextWriter writer, CodeConstructorParameterConstraint constraint)
        {
            writer.Write("new()");
        }

        protected virtual string Write(BinaryOperator op)
        {
            switch (op)
            {
                case BinaryOperator.None:
                    return "";
                case BinaryOperator.Equals:
                    return "==";
                case BinaryOperator.NotEquals:
                    return "!=";
                case BinaryOperator.LessThan:
                    return "<";
                case BinaryOperator.LessThanOrEqual:
                    return "<=";
                case BinaryOperator.GreaterThan:
                    return ">";
                case BinaryOperator.GreaterThanOrEqual:
                    return ">=";
                case BinaryOperator.Or:
                    return "||";
                case BinaryOperator.BitwiseOr:
                    return "|";
                case BinaryOperator.And:
                    return "&&";
                case BinaryOperator.BitwiseAnd:
                    return "&";
                case BinaryOperator.Add:
                    return "+";
                case BinaryOperator.Substract:
                    return "-";
                case BinaryOperator.Multiply:
                    return "*";
                case BinaryOperator.Divide:
                    return "/";
                case BinaryOperator.Modulo:
                    return "%";
                case BinaryOperator.ShiftLeft:
                    return "<<";
                case BinaryOperator.ShiftRight:
                    return ">>";

                default:
                    throw new ArgumentOutOfRangeException(nameof(op));
            }
        }

        protected virtual bool IsPrefixOperator(UnaryOperator op)
        {
            switch (op)
            {
                case UnaryOperator.None:
                case UnaryOperator.Not:
                case UnaryOperator.Complement:
                case UnaryOperator.Plus:
                case UnaryOperator.Minus:
                case UnaryOperator.PreIncrement:
                case UnaryOperator.PreDecrement:
                    return true;

                case UnaryOperator.PostIncrement:
                case UnaryOperator.PostDecrement:
                    return false;

                default:
                    throw new ArgumentOutOfRangeException(nameof(op));
            }
        }

        protected virtual string Write(UnaryOperator op)
        {
            switch (op)
            {
                case UnaryOperator.None:
                    return "";
                case UnaryOperator.Not:
                    return "!";
                case UnaryOperator.Complement:
                    return "~";
                case UnaryOperator.Plus:
                    return "+";
                case UnaryOperator.Minus:
                    return "-";
                case UnaryOperator.PreIncrement:
                case UnaryOperator.PostIncrement:
                    return "++";
                case UnaryOperator.PreDecrement:
                case UnaryOperator.PostDecrement:
                    return "--";

                default:
                    throw new ArgumentOutOfRangeException(nameof(op));
            }
        }

        protected virtual void WriteIdentifier(IndentedTextWriter writer, string name)
        {
            if (_keywords.Contains(name))
            {
                writer.Write("@");
            }

            writer.Write(name);
        }

        private void WriteEmptyBlock(IndentedTextWriter writer)
        {
            writer.WriteLine("{");
            writer.WriteLine("}");
        }

        private void WriteStatementsOrEmptyBlock(IndentedTextWriter writer, CodeStatementCollection statements)
        {
            if (statements != null)
            {
                Write(writer, statements);
            }
            else
            {
                WriteEmptyBlock(writer);
            }
        }

        protected virtual void Write(IndentedTextWriter writer, CodeStatementCollection statements)
        {
            writer.WriteLine("{");
            writer.Indent++;
            Write(writer, statements, writer.NewLine);
            writer.Indent--;
            writer.WriteLine("}");
        }

        protected virtual void WriteConstraints(IndentedTextWriter writer, CodeTypeParameter parameter)
        {
            // 1. class, struct
            // 2. base
            // 3. new()
            var orderedConstraints = new List<CodeTypeParameterConstraint>(parameter.Constraints.Count);
            orderedConstraints.AddRange(parameter.Constraints.Where(p => p is CodeValueTypeTypeParameterConstraint || p is CodeClassTypeParameterConstraint));
            orderedConstraints.AddRange(parameter.Constraints.Where(p => p is CodeBaseTypeParameterConstraint));
            orderedConstraints.AddRange(parameter.Constraints.Where(p => p is CodeConstructorParameterConstraint));

            writer.Write("where ");
            writer.Write(parameter.Name);
            writer.Write(" : ");
            Write(writer, orderedConstraints, ", ");
        }

        private void WriteGenericParameters(IndentedTextWriter writer, IParametrableType type)
        {
            if (type.Parameters.Any())
            {
                writer.Write("<");
                WriteValues(writer, type.Parameters.Select(p => p.Name), ", ");
                writer.Write(">");
            }
        }

        private void WriteGenericParameterConstraints(IndentedTextWriter writer, IParametrableType type)
        {
            writer.Indent++;
            foreach (var parameter in type.Parameters)
            {
                if (parameter.HasConstraints())
                {
                    WriteConstraints(writer, parameter);
                    writer.WriteLine();
                }
            }
            writer.Indent--;
        }

        private List<CodeTypeReference> GetBaseTypes(IInheritanceParameters c)
        {
            var list = new List<CodeTypeReference>();
            if (c.BaseType != null)
            {
                list.Add(c.BaseType);
            }

            foreach (var type in c.Implements)
            {
                list.Add(type);
            }

            return list;
        }

        private void WriteValues<T>(IndentedTextWriter writer, IEnumerable<T> objects, string separator)
        {
            bool first = true;
            foreach (var o in objects)
            {
                if (!first)
                {
                    writer.Write(separator);
                }

                writer.Write(o);
                first = false;
            }
        }

        private void Write<T>(IndentedTextWriter writer, IEnumerable<T> objects, string separator) where T : CodeObject
        {
            bool first = true;
            foreach (var o in objects)
            {
                if (!first)
                {
                    writer.Write(separator);
                }

                Write(writer, o);
                first = false;
            }
        }

        private void WriteLines<T>(IndentedTextWriter writer, IEnumerable<T> objects, string endOfLine) where T : CodeObject
        {
            bool first = true;
            foreach (var o in objects)
            {
                if (!first)
                {
                    writer.WriteLine(endOfLine);
                }

                Write(writer, o);
                first = false;
            }
        }

        private void Write<T>(IndentedTextWriter writer, IReadOnlyList<T> objects, Action<(T Item, bool First, bool Last)> afterItemAction) where T : CodeObject
        {
            for (int i = 0; i < objects.Count; i++)
            {
                var o = objects[i];
                Write(writer, o);
                afterItemAction((o, i == 0, i == objects.Count - 1));
            }
        }
    }
}
