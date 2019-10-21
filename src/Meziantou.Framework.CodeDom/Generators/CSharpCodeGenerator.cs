#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Meziantou.Framework.CodeDom
{
    public partial class CSharpCodeGenerator
    {
        private static readonly IDictionary<string, string> s_predefinedTypes;
        private static readonly string[] s_keywords;

        static CSharpCodeGenerator()
        {
            s_predefinedTypes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [typeof(bool).FullName] = "bool",
                [typeof(byte).FullName] = "byte",
                [typeof(char).FullName] = "char",
                [typeof(decimal).FullName] = "decimal",
                [typeof(double).FullName] = "double",
                [typeof(float).FullName] = "float",
                [typeof(int).FullName] = "int",
                [typeof(long).FullName] = "long",
                [typeof(object).FullName] = "object",
                [typeof(sbyte).FullName] = "sbyte",
                [typeof(short).FullName] = "short",
                [typeof(string).FullName] = "string",
                [typeof(uint).FullName] = "uint",
                [typeof(ulong).FullName] = "ulong",
                [typeof(ushort).FullName] = "ushort",
                [typeof(void).FullName] = "void",
            };

            s_keywords = new string[]
            {
                "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong", "double", "float", "decimal",
                "string", "char", "void", "object", "typeof", "sizeof", "null", "true", "false", "if", "else", "while", "for", "foreach", "do", "switch",
                "case", "default", "lock", "try", "throw", "catch", "finally", "goto", "break", "continue", "return", "public", "private", "internal",
                "protected", "static", "readonly", "sealed", "const", "fixed", "stackalloc", "volatile", "new", "override", "abstract", "virtual",
                "event", "extern", "ref", "out", "in", "is", "as", "params", "__arglist", "__makeref", "__reftype", "__refvalue", "this", "base",
                "namespace", "using", "class", "struct", "interface", "enum", "delegate", "checked", "unchecked", "unsafe", "operator", "implicit", "explicit",
            };
        }

        public string Write(CodeObject codeObject)
        {
            if (codeObject == null)
                throw new ArgumentNullException(nameof(codeObject));

            using var sw = new StringWriter();
            Write(sw, codeObject);
            return sw.ToString();
        }

        public void Write(TextWriter writer, CodeObject codeObject)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (codeObject == null)
                throw new ArgumentNullException(nameof(codeObject));

            using var indentedTextWriter = new IndentedTextWriter(writer, IndentedTextWriter.DefaultTabString, closeWriter: false)
            {
                NewLine = "\n",
            };

            Write(indentedTextWriter, codeObject);
        }

        public void Write(IndentedTextWriter writer, CodeObject codeObject)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (codeObject == null)
                throw new ArgumentNullException(nameof(codeObject));

            switch (codeObject)
            {
                case CompilationUnit o:
                    Write(writer, o);
                    break;

                case NamespaceDeclaration o:
                    Write(writer, o);
                    break;

                case TypeDeclaration o:
                    Write(writer, o);
                    break;

                case Expression o:
                    Write(writer, o);
                    break;

                case Statement o:
                    Write(writer, o);
                    break;

                case Directive o:
                    Write(writer, o);
                    break;

                case MemberDeclaration o:
                    Write(writer, o);
                    break;

                case MethodArgumentDeclaration o:
                    Write(writer, o);
                    break;

                case CustomAttribute o:
                    Write(writer, o);
                    break;

                case CustomAttributeArgument o:
                    Write(writer, o);
                    break;

                case CatchClauseCollection o:
                    Write(writer, o);
                    break;

                case CatchClause o:
                    Write(writer, o);
                    break;

                case ConstructorInitializer o:
                    Write(writer, o);
                    break;

                case TypeParameterConstraint o:
                    Write(writer, o);
                    break;

                case Comment o:
                    Write(writer, o);
                    break;

                case XmlComment o:
                    Write(writer, o);
                    break;

                default:
                    throw new NotSupportedException();
            }
        }

        protected virtual void Write(IndentedTextWriter writer, CompilationUnit unit)
        {
            Write(writer, unit.Usings, writer.NewLine);
            Write(writer, unit.Namespaces, writer.NewLine);
            Write(writer, unit.Types, writer.NewLine);
        }

        protected virtual void Write(IndentedTextWriter writer, NamespaceDeclaration ns)
        {
            WriteBeforeComments(writer, ns);
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
            WriteAfterComments(writer, ns);
        }

        protected virtual void Write(IndentedTextWriter writer, TypeDeclaration type)
        {
            WriteXmlComments(writer, type);
            WriteBeforeComments(writer, type);
            Write(writer, type.CustomAttributes);
            Write(writer, type.Modifiers);
            switch (type)
            {
                case ClassDeclaration o:
                    Write(writer, o);
                    break;

                case StructDeclaration o:
                    Write(writer, o);
                    break;

                case EnumerationDeclaration o:
                    Write(writer, o);
                    break;

                case InterfaceDeclaration o:
                    Write(writer, o);
                    break;

                case DelegateDeclaration o:
                    Write(writer, o);
                    break;

                default:
                    throw new NotSupportedException();
            }

            WriteAfterComments(writer, type);
        }

        protected virtual void Write(IndentedTextWriter writer, EnumerationDeclaration enumeration)
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

        protected virtual void Write(IndentedTextWriter writer, EnumerationMember member)
        {
            Write(writer, member.CustomAttributes);
            WriteIdentifier(writer, member.Name);
            if (member.Value != null)
            {
                writer.Write(" = ");
                Write(writer, member.Value);
            }
        }

        protected virtual void Write(IndentedTextWriter writer, DelegateDeclaration d)
        {
            writer.Write("delegate ");
            if (d.ReturnType == null)
            {
                writer.Write("void ");
            }
            else
            {
                Write(writer, d.ReturnType);
                writer.Write(" ");
            }
            WriteIdentifier(writer, d.Name);
            WriteGenericParameters(writer, d);
            writer.Write("(");
            Write(writer, d.Arguments);
            writer.Write(")");
            WriteGenericParameterConstraints(writer, d);
            if (d.HasConstraints())
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

        protected virtual void Write(IndentedTextWriter writer, MethodDeclaration member)
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
                writer.Write(' ');
            }

            if (member.PrivateImplementationType != null)
            {
                Write(writer, member.PrivateImplementationType);
                writer.Write('.');
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

        protected virtual void Write(IndentedTextWriter writer, OperatorDeclaration member)
        {
            var isConversion = member.Modifiers.HasFlag(Modifiers.Implicit) || member.Modifiers.HasFlag(Modifiers.Explicit);

            Write(writer, member.CustomAttributes);
            Write(writer, member.Modifiers);
            if (isConversion)
            {
                writer.Write("operator ");

                if (member.ReturnType != null)
                {
                    Write(writer, member.ReturnType);
                }
                else
                {
                    var declaringType = member.GetSelfOrParentOfType<TypeDeclaration>();
                    if (declaringType != null)
                    {
                        WriteIdentifier(writer, declaringType.Name);
                    }
                }
            }
            else
            {
                if (member.ReturnType != null)
                {
                    Write(writer, member.ReturnType);
                    writer.Write(' ');
                }

                writer.Write("operator ");
            }

            WriteIdentifier(writer, member.Name);
            writer.Write("(");
            Write(writer, member.Arguments);
            writer.Write(")");
            writer.WriteLine();
            Write(writer, member.Statements);
        }

        protected virtual void Write(IndentedTextWriter writer, CodeObjectCollection<MethodArgumentDeclaration> args)
        {
            Write(writer, args, ", ");
        }

        protected virtual void Write(IndentedTextWriter writer, MethodArgumentDeclaration arg)
        {
            WriteBeforeComments(writer, arg);
            Write(writer, arg.CustomAttributes);
            if (arg.IsExtension)
            {
                writer.Write("this ");
            }

            Write(writer, arg.Direction);
            Write(writer, arg.Type);
            writer.Write(" ");
            WriteIdentifier(writer, arg.Name);
            if (arg.DefaultValue != null)
            {
                writer.Write(" = ");
                Write(writer, arg.DefaultValue);
            }

            WriteAfterComments(writer, arg);
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

                case Direction.ReadOnlyRef:
                    writer.Write("in ");
                    break;

                case Direction.In:
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(direction));
            }
        }

        protected virtual void Write(IndentedTextWriter writer, FieldDeclaration member)
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

        protected virtual void Write(IndentedTextWriter writer, EventFieldDeclaration member)
        {
            Write(writer, member.CustomAttributes);
            Write(writer, member.Modifiers);
            writer.Write("event ");
            if (member.Type != null)
            {
                Write(writer, member.Type);
                writer.Write(" ");
            }

            if (member.PrivateImplementationType != null)
            {
                Write(writer, member.PrivateImplementationType);
                writer.Write('.');
            }

            WriteIdentifier(writer, member.Name);

            if (member.AddAccessor == null && member.RemoveAccessor == null)
            {
                writer.WriteLine(";");
            }
            else
            {
                writer.WriteLine();
                writer.WriteLine("{");
                writer.Indent++;

                if (member.AddAccessor != null)
                {
                    writer.WriteLine("add");
                    Write(writer, member.AddAccessor);
                }

                if (member.RemoveAccessor != null)
                {
                    writer.WriteLine("remove");
                    Write(writer, member.RemoveAccessor);
                }

                writer.Indent--;
                writer.WriteLine("}");
            }
        }

        protected virtual void Write(IndentedTextWriter writer, ConstructorDeclaration member)
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
            Write(writer, member.Statements);
        }

        protected virtual void Write(IndentedTextWriter writer, PropertyDeclaration member)
        {
            Write(writer, member.CustomAttributes);
            Write(writer, member.Modifiers);
            Write(writer, member.Type);
            writer.Write(" ");

            if (member.PrivateImplementationType != null)
            {
                Write(writer, member.PrivateImplementationType);
                writer.Write('.');
            }

            WriteIdentifier(writer, member.Name);

            writer.WriteLine();
            writer.WriteLine("{");
            writer.Indent++;

            if (member.Getter != null)
            {
                Write(writer, member.Getter.CustomAttributes);
                Write(writer, member.Getter.Modifiers);
                writer.WriteLine("get");
                Write(writer, member.Getter.Statements);
            }

            if (member.Setter != null)
            {
                Write(writer, member.Setter.CustomAttributes);
                Write(writer, member.Setter.Modifiers);
                writer.WriteLine("set");
                Write(writer, member.Setter.Statements);
            }

            writer.Indent--;
            writer.WriteLine("}");
        }

        protected virtual void Write(IndentedTextWriter writer, ClassDeclaration type)
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
            WriteLines(writer, type.Members.Cast<CodeObject>().Concat(type.Types), endOfLine: null);
            writer.Indent--;
            writer.WriteLine("}");
        }

        protected virtual void Write(IndentedTextWriter writer, StructDeclaration type)
        {
            writer.Write("struct ");
            WriteIdentifier(writer, type.Name);
            WriteGenericParameters(writer, type);

            var baseTypes = type.Implements;
            if (baseTypes.Any())
            {
                writer.Write(" : ");
                Write(writer, baseTypes, ", ");
            }

            writer.WriteLine();
            WriteGenericParameterConstraints(writer, type);

            writer.WriteLine("{");
            writer.Indent++;
            WriteLines(writer, type.Members.Cast<CodeObject>().Concat(type.Types), endOfLine: null);
            writer.Indent--;
            writer.WriteLine("}");
        }

        protected virtual void Write(IndentedTextWriter writer, InterfaceDeclaration type)
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
            if ((modifiers & Modifiers.Ref) == Modifiers.Ref)
            {
                writer.Write("ref ");
            }
            if ((modifiers & Modifiers.ReadOnly) == Modifiers.ReadOnly)
            {
                writer.Write("readonly ");
            }
            if ((modifiers & Modifiers.Partial) == Modifiers.Partial)
            {
                writer.Write("partial ");
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
            if ((modifiers & Modifiers.Implicit) == Modifiers.Implicit)
            {
                writer.Write("implicit ");
            }
            if ((modifiers & Modifiers.Explicit) == Modifiers.Explicit)
            {
                writer.Write("explicit ");
            }
        }

        protected virtual void Write(IndentedTextWriter writer, CodeObjectCollection<CustomAttribute> attributes)
        {
            if (attributes.Count > 0)
            {
                Write(writer, attributes, writer.NewLine);
                writer.WriteLine();
            }
        }

        protected virtual void Write(IndentedTextWriter writer, CustomAttribute attribute)
        {
            WriteBeforeComments(writer, attribute);
            writer.Write("[");
            if (attribute.Target.HasValue)
            {
                writer.Write(Write(attribute.Target.Value));
                writer.Write(": ");
            }

            Write(writer, attribute.Type);

            if (attribute.Arguments.Count > 0)
            {
                writer.Write("(");
                Write(writer, attribute.Arguments.OrderBy(GetSortOrder), ", ");
                writer.Write(")");
            }

            writer.Write("]");
            WriteAfterComments(writer, attribute);

            static int GetSortOrder(CustomAttributeArgument arg)
            {
                if (arg.PropertyName == null)
                    return 0;

                return 1;
            }
        }

        protected virtual void Write(IndentedTextWriter writer, CustomAttributeArgument arg)
        {
            WriteBeforeComments(writer, arg);
            if (!string.IsNullOrEmpty(arg.PropertyName))
            {
                WriteIdentifier(writer, arg.PropertyName);
                writer.Write(" = ");
            }

            Write(writer, arg.Value);
            WriteAfterComments(writer, arg);
        }

        protected virtual void Write(IndentedTextWriter writer, MemberDeclaration member)
        {
            WriteXmlComments(writer, member);
            WriteBeforeComments(writer, member);
            switch (member)
            {
                case EnumerationMember o:
                    Write(writer, o);
                    break;

                case MethodDeclaration o:
                    Write(writer, o);
                    break;

                case OperatorDeclaration o:
                    Write(writer, o);
                    break;

                case FieldDeclaration o:
                    Write(writer, o);
                    break;

                case ConstructorDeclaration o:
                    Write(writer, o);
                    break;

                case PropertyDeclaration o:
                    Write(writer, o);
                    break;

                case EventFieldDeclaration o:
                    Write(writer, o);
                    break;

                default:
                    throw new NotSupportedException();
            }

            WriteAfterComments(writer, member);
        }

        protected virtual void Write(IndentedTextWriter writer, Directive directive)
        {
            WriteBeforeComments(writer, directive);
            switch (directive)
            {
                case UsingDirective o:
                    Write(writer, o);
                    break;

                default:
                    throw new NotSupportedException();
            }

            WriteAfterComments(writer, directive);
        }

        protected virtual void Write(IndentedTextWriter writer, UsingDirective usingDirective)
        {
            writer.Write("using ");
            writer.Write(usingDirective.Namespace);
            writer.Write(";");
        }

        protected virtual void Write(IndentedTextWriter writer, CatchClause catchClause)
        {
            WriteBeforeComments(writer, catchClause);
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

            Write(writer, catchClause.Body);
            WriteAfterComments(writer, catchClause);
        }

        protected virtual void Write(IndentedTextWriter writer, CatchClauseCollection clauses)
        {
            Write(writer, clauses, "");
        }

        protected virtual void Write(IndentedTextWriter writer, ConstructorInitializer initializer)
        {
            WriteBeforeComments(writer, initializer);
            writer.Write(": ");
            switch (initializer)
            {
                case ConstructorThisInitializer _:
                    writer.Write("this");
                    break;

                case ConstructorBaseInitializer _:
                    writer.Write("base");
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(initializer));
            }

            writer.Write('(');
            Write(writer, initializer.Arguments, ", ");
            writer.Write(')');
            WriteAfterComments(writer, initializer);
        }

        protected virtual void Write(IndentedTextWriter writer, TypeParameterConstraint constraint)
        {
            switch (constraint)
            {
                case BaseTypeParameterConstraint o:
                    Write(writer, o);
                    break;

                case ClassTypeParameterConstraint o:
                    Write(writer, o);
                    break;

                case ValueTypeTypeParameterConstraint o:
                    Write(writer, o);
                    break;

                case ConstructorParameterConstraint o:
                    Write(writer, o);
                    break;

                case UnmanagedTypeParameterConstraint o:
                    Write(writer, o);
                    break;

                default:
                    throw new NotSupportedException();
            }
        }

        protected virtual void Write(IndentedTextWriter writer, BaseTypeParameterConstraint constraint)
        {
            Write(writer, constraint.Type);
        }

        protected virtual void Write(IndentedTextWriter writer, ClassTypeParameterConstraint constraint)
        {
            writer.Write("class");
        }

        protected virtual void Write(IndentedTextWriter writer, ValueTypeTypeParameterConstraint constraint)
        {
            writer.Write("struct");
        }

        protected virtual void Write(IndentedTextWriter writer, UnmanagedTypeParameterConstraint constraint)
        {
            writer.Write("unmanaged");
        }

        protected virtual void Write(IndentedTextWriter writer, ConstructorParameterConstraint constraint)
        {
            writer.Write("new()");
        }

        protected virtual CommentType Write(IndentedTextWriter writer, Comment comment)
        {
            switch (comment.Type)
            {
                case CommentType.LineComment:
                    WriteLineComment(writer, comment.Text);
                    return CommentType.LineComment;

                case CommentType.InlineComment:
                    if (TryWriteInlineComment(writer, comment.Text))
                        return CommentType.InlineComment;
                    return CommentType.LineComment;

                default:
                    throw new ArgumentOutOfRangeException(nameof(comment));
            }
        }

        protected virtual void Write(IndentedTextWriter writer, XmlComment comment)
        {
            WriteDocumentationComment(writer, comment.Element.ToString());
        }

        protected virtual string Write(BinaryOperator op)
        {
            return op switch
            {
                BinaryOperator.None => "",
                BinaryOperator.Equals => "==",
                BinaryOperator.NotEquals => "!=",
                BinaryOperator.LessThan => "<",
                BinaryOperator.LessThanOrEqual => "<=",
                BinaryOperator.GreaterThan => ">",
                BinaryOperator.GreaterThanOrEqual => ">=",
                BinaryOperator.Or => "||",
                BinaryOperator.BitwiseOr => "|",
                BinaryOperator.And => "&&",
                BinaryOperator.BitwiseAnd => "&",
                BinaryOperator.Add => "+",
                BinaryOperator.Substract => "-",
                BinaryOperator.Multiply => "*",
                BinaryOperator.Divide => "/",
                BinaryOperator.Modulo => "%",
                BinaryOperator.ShiftLeft => "<<",
                BinaryOperator.ShiftRight => ">>",
                BinaryOperator.Xor => "^",
                _ => throw new ArgumentOutOfRangeException(nameof(op)),
            };
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

        protected virtual string Write(CustomAttributeTarget target)
        {
            return target switch
            {
                CustomAttributeTarget.Assembly => "assembly",
                CustomAttributeTarget.Module => "module",
                CustomAttributeTarget.Field => "field",
                CustomAttributeTarget.Event => "event",
                CustomAttributeTarget.Method => "method",
                CustomAttributeTarget.Param => "param",
                CustomAttributeTarget.Property => "property",
                CustomAttributeTarget.Return => "return",
                CustomAttributeTarget.Type => "type",
                _ => throw new ArgumentOutOfRangeException(nameof(target)),
            };
        }

        protected virtual void WriteIdentifier(IndentedTextWriter writer, string name)
        {
            if (s_keywords.Contains(name, StringComparer.Ordinal))
            {
                writer.Write("@");
            }

            writer.Write(name);
        }

        protected virtual void Write(IndentedTextWriter writer, StatementCollection statements)
        {
            writer.WriteLine("{");
            writer.Indent++;

            if (statements != null)
            {
                var mustAddNewLine = false;
                foreach (var statement in statements)
                {
                    if (mustAddNewLine)
                    {
                        writer.WriteLine();
                        mustAddNewLine = false;
                    }

                    Write(writer, statement);
                    mustAddNewLine = IsBlockStatement(statement);
                }
            }

            writer.Indent--;
            writer.WriteLine("}");

            static bool IsBlockStatement(Statement statement)
            {
                return statement is ConditionStatement
                    || statement is IterationStatement
                    || statement is TryCatchFinallyStatement
                    || statement is UsingStatement
                    || statement is WhileStatement;
            }
        }

        protected virtual void WriteConstraints(IndentedTextWriter writer, TypeParameter parameter)
        {
            // 1. class, struct, unmanaged
            // 2. base
            // 3. new()
            var orderedConstraints = new List<TypeParameterConstraint>(parameter.Constraints.Count);
            orderedConstraints.AddRange(parameter.Constraints.Where(p => p is ValueTypeTypeParameterConstraint || p is ClassTypeParameterConstraint || p is UnmanagedTypeParameterConstraint));
            orderedConstraints.AddRange(parameter.Constraints.Where(p => p is BaseTypeParameterConstraint));
            orderedConstraints.AddRange(parameter.Constraints.Where(p => p is ConstructorParameterConstraint));

            writer.Write("where ");
            writer.Write(parameter.Name);
            writer.Write(" : ");
            Write(writer, orderedConstraints, ", ");
        }

        protected virtual void WriteDocumentationComment(IndentedTextWriter writer, string comment)
        {
            if (comment == null)
                return;

            using var sr = new StringReader(comment);
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                if (string.IsNullOrEmpty(line))
                {
                    writer.WriteLine("///");
                }
                else
                {
                    writer.WriteLine("/// " + line);
                }
            }
        }

        protected virtual void WriteLineComment(IndentedTextWriter writer, string comment)
        {
            if (comment == null)
            {
                writer.WriteLine("//");
                return;
            }

            using var sr = new StringReader(comment);
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                if (string.IsNullOrEmpty(line))
                {
                    writer.WriteLine("//");
                }
                else
                {
                    writer.WriteLine("// " + line);
                }
            }
        }

        protected virtual bool TryWriteInlineComment(IndentedTextWriter writer, string comment)
        {
            if (comment == null)
            {
                writer.WriteLine("/* */");
                return true;
            }

            if (comment.Contains("*/"))
            {
                WriteLineComment(writer, comment);
                return false;
            }
            else
            {
                writer.Write("/* ");
                writer.Write(comment);
                writer.Write(" */");
                return true;
            }
        }

        protected virtual void WriteXmlComments(IndentedTextWriter writer, IXmlCommentable commentable)
        {
            foreach (var comment in commentable.XmlComments)
            {
                Write(writer, comment);
            }
        }

        protected virtual void WriteBeforeComments(IndentedTextWriter writer, ICommentable commentable)
        {
            CommentType type = default;
            var first = true;
            foreach (var c in commentable.CommentsBefore)
            {
                if (!first)
                {
                    if (type == CommentType.InlineComment)
                    {
                        writer.Write(' ');
                    }
                }

                type = Write(writer, c);
                first = false;
            }

            if (!first && type == CommentType.InlineComment)
            {
                writer.Write(' ');
            }
        }

        protected virtual void WriteAfterComments(IndentedTextWriter writer, ICommentable commentable)
        {
            var isInlined = commentable is Expression;

            CommentType type = default;
            var first = true;
            foreach (var c in commentable.CommentsAfter)
            {
                if ((isInlined && first) || (type == CommentType.InlineComment && c.Type == CommentType.InlineComment))
                {
                    writer.Write(' ');
                }

                type = Write(writer, c);
                first = false;
            }
        }

        private void WriteGenericParameters(IndentedTextWriter writer, CodeObjectCollection<TypeReference> types)
        {
            if (types.Any())
            {
                writer.Write("<");
                Write(writer, types, ", ");
                writer.Write(">");
            }
        }

        private static void WriteGenericParameters(IndentedTextWriter writer, IParametrableType type)
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

        private static List<TypeReference> GetBaseTypes(IInheritanceParameters c)
        {
            var list = new List<TypeReference>();
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

        private static void WriteValues<T>(IndentedTextWriter writer, IEnumerable<T> objects, string separator)
        {
            var first = true;
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
            var first = true;
            foreach (var o in objects)
            {
                if (!first && separator != null)
                {
                    writer.Write(separator);
                }

                Write(writer, o);
                first = false;
            }
        }

        private void WriteLines<T>(IndentedTextWriter writer, IEnumerable<T> objects, string endOfLine) where T : CodeObject
        {
            var first = true;
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
            for (var i = 0; i < objects.Count; i++)
            {
                var o = objects[i];
                Write(writer, o);
                afterItemAction((o, i == 0, i == objects.Count - 1));
            }
        }
    }
}
