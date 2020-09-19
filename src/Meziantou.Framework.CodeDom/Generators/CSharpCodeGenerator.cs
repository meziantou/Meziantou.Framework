using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
                [typeof(bool).FullName!] = "bool",
                [typeof(byte).FullName!] = "byte",
                [typeof(char).FullName!] = "char",
                [typeof(decimal).FullName!] = "decimal",
                [typeof(double).FullName!] = "double",
                [typeof(float).FullName!] = "float",
                [typeof(int).FullName!] = "int",
                [typeof(long).FullName!] = "long",
                [typeof(object).FullName!] = "object",
                [typeof(sbyte).FullName!] = "sbyte",
                [typeof(short).FullName!] = "short",
                [typeof(string).FullName!] = "string",
                [typeof(uint).FullName!] = "uint",
                [typeof(ulong).FullName!] = "ulong",
                [typeof(ushort).FullName!] = "ushort",
                [typeof(void).FullName!] = "void",
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
                    WriteCompilationUnit(writer, o);
                    break;

                case NamespaceDeclaration o:
                    WriteNamespaceDeclaration(writer, o);
                    break;

                case TypeDeclaration o:
                    WriteTypeDeclaration(writer, o);
                    break;

                case Expression o:
                    WriteExpression(writer, o);
                    break;

                case Statement o:
                    WriteStatement(writer, o);
                    break;

                case Directive o:
                    WriteDirective(writer, o);
                    break;

                case MemberDeclaration o:
                    WriteMemberDeclaration(writer, o);
                    break;

                case MethodArgumentDeclaration o:
                    WriteMethodArgument(writer, o);
                    break;

                case CustomAttribute o:
                    WriteCustomAttribute(writer, o);
                    break;

                case CustomAttributeArgument o:
                    WriteCustomAttributeArgument(writer, o);
                    break;

                case CatchClauseCollection o:
                    WriteCatchClauseCollection(writer, o);
                    break;

                case CatchClause o:
                    WriteCatchClause(writer, o);
                    break;

                case ConstructorInitializer o:
                    WriteConstructorInitializer(writer, o);
                    break;

                case TypeParameterConstraint o:
                    WriteTypeParameterConstraint(writer, o);
                    break;

                case Comment o:
                    WriteComment(writer, o);
                    break;

                case XmlComment o:
                    WriteXmlComment(writer, o);
                    break;

                default:
                    throw new NotSupportedException();
            }
        }

        protected virtual void WriteCompilationUnit(IndentedTextWriter writer, CompilationUnit unit)
        {
            WriteBeforeComments(writer, unit);
            WriteNullableContextBefore(writer, unit);
            Write(writer, unit.Usings, writer.NewLine);
            Write(writer, unit.Namespaces, writer.NewLine);
            Write(writer, unit.Types, writer.NewLine);
            WriteAfterComments(writer, unit);
        }

        protected virtual void WriteNamespaceDeclaration(IndentedTextWriter writer, NamespaceDeclaration ns)
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

        protected virtual void WriteTypeDeclaration(IndentedTextWriter writer, TypeDeclaration type)
        {
            WriteNullableContextBefore(writer, type);
            WriteXmlComments(writer, type);
            WriteBeforeComments(writer, type);
            WriteCustomAttributes(writer, type.CustomAttributes);
            WriteModifiers(writer, type.Modifiers);
            switch (type)
            {
                case ClassDeclaration o:
                    WriteClassDeclaration(writer, o);
                    break;

                case StructDeclaration o:
                    WriteStructDeclaration(writer, o);
                    break;

                case EnumerationDeclaration o:
                    WriteEnumerationDeclaration(writer, o);
                    break;

                case InterfaceDeclaration o:
                    WriteInterfaceDeclaration(writer, o);
                    break;

                case DelegateDeclaration o:
                    WriteDelegateDeclaration(writer, o);
                    break;

                default:
                    throw new NotSupportedException();
            }

            WriteAfterComments(writer, type);
            WriteNullableContextAfter(writer, type);
        }

        protected virtual void WriteEnumerationDeclaration(IndentedTextWriter writer, EnumerationDeclaration enumeration)
        {
            writer.Write("enum ");
            WriteIdentifier(writer, enumeration.Name);

            if (enumeration.BaseType != null)
            {
                writer.Write(" : ");
                WriteTypeReference(writer, enumeration.BaseType);
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

        protected virtual void WriteEnumerationMember(IndentedTextWriter writer, EnumerationMember member)
        {
            WriteCustomAttributes(writer, member.CustomAttributes);
            WriteIdentifier(writer, member.Name);
            if (member.Value != null)
            {
                writer.Write(" = ");
                WriteExpression(writer, member.Value);
            }
        }

        protected virtual void WriteDelegateDeclaration(IndentedTextWriter writer, DelegateDeclaration d)
        {
            writer.Write("delegate ");
            if (d.ReturnType == null)
            {
                writer.Write("void ");
            }
            else
            {
                WriteTypeReference(writer, d.ReturnType);
                writer.Write(" ");
            }
            WriteIdentifier(writer, d.Name);
            WriteGenericParameters(writer, d);
            writer.Write("(");
            WriteMethodArguments(writer, d.Arguments);
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

        protected virtual void WriteMethodDeclaration(IndentedTextWriter writer, MethodDeclaration member)
        {
            WriteCustomAttributes(writer, member.CustomAttributes);
            WriteModifiers(writer, member.Modifiers);
            if (member.ReturnType == null)
            {
                writer.Write("void ");
            }
            else
            {
                WriteTypeReference(writer, member.ReturnType);
                writer.Write(' ');
            }

            if (member.PrivateImplementationType != null)
            {
                WriteTypeReference(writer, member.PrivateImplementationType);
                writer.Write('.');
            }

            WriteIdentifier(writer, member.Name);
            WriteGenericParameters(writer, member);
            writer.Write("(");
            WriteMethodArguments(writer, member.Arguments);
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
                WriteStatements(writer, member.Statements);
            }
        }

        protected virtual void WriteOperatorDeclaration(IndentedTextWriter writer, OperatorDeclaration member)
        {
            var isConversion = member.Modifiers.HasFlag(Modifiers.Implicit) || member.Modifiers.HasFlag(Modifiers.Explicit);

            WriteCustomAttributes(writer, member.CustomAttributes);
            WriteModifiers(writer, member.Modifiers);
            if (isConversion)
            {
                writer.Write("operator ");

                if (member.ReturnType != null)
                {
                    WriteTypeReference(writer, member.ReturnType);
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
                    WriteTypeReference(writer, member.ReturnType);
                    writer.Write(' ');
                }

                writer.Write("operator ");
            }

            WriteIdentifier(writer, member.Name);
            writer.Write("(");
            WriteMethodArguments(writer, member.Arguments);
            writer.Write(")");
            writer.WriteLine();
            WriteStatements(writer, member.Statements);
        }

        protected virtual void WriteMethodArguments(IndentedTextWriter writer, CodeObjectCollection<MethodArgumentDeclaration> args)
        {
            Write(writer, args, ", ");
        }

        protected virtual void WriteMethodArgument(IndentedTextWriter writer, MethodArgumentDeclaration arg)
        {
            WriteBeforeComments(writer, arg);
            WriteCustomAttributes(writer, arg.CustomAttributes);
            if (arg.IsExtension)
            {
                writer.Write("this ");
            }

            WriteDirection(writer, arg.Direction);
            WriteTypeReference(writer, arg.Type);
            writer.Write(" ");
            WriteIdentifier(writer, arg.Name);
            if (arg.DefaultValue != null)
            {
                writer.Write(" = ");
                WriteExpression(writer, arg.DefaultValue);
            }

            WriteAfterComments(writer, arg);
        }

        protected virtual void WriteDirection(IndentedTextWriter writer, Direction direction)
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

        protected virtual void WriteFieldDeclaration(IndentedTextWriter writer, FieldDeclaration member)
        {
            WriteCustomAttributes(writer, member.CustomAttributes);
            WriteModifiers(writer, member.Modifiers);
            if (member.Type == null)
            {
                writer.Write("var ");
            }
            else
            {
                WriteTypeReference(writer, member.Type);
                writer.Write(" ");
            }

            WriteIdentifier(writer, member.Name);

            if (member.InitExpression != null)
            {
                writer.Write(" = ");
                WriteExpression(writer, member.InitExpression);
            }

            writer.WriteLine(";");
        }

        protected virtual void WriteEventFieldDeclaration(IndentedTextWriter writer, EventFieldDeclaration member)
        {
            WriteCustomAttributes(writer, member.CustomAttributes);
            WriteModifiers(writer, member.Modifiers);
            writer.Write("event ");
            if (member.Type != null)
            {
                WriteTypeReference(writer, member.Type);
                writer.Write(" ");
            }

            if (member.PrivateImplementationType != null)
            {
                WriteTypeReference(writer, member.PrivateImplementationType);
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
                    WriteStatements(writer, member.AddAccessor);
                }

                if (member.RemoveAccessor != null)
                {
                    writer.WriteLine("remove");
                    WriteStatements(writer, member.RemoveAccessor);
                }

                writer.Indent--;
                writer.WriteLine("}");
            }
        }

        protected virtual void WriteConstructorDeclaration(IndentedTextWriter writer, ConstructorDeclaration member)
        {
            WriteCustomAttributes(writer, member.CustomAttributes);
            WriteModifiers(writer, member.Modifiers);

            var name = member.ParentType?.Name ?? member.Name;
            if (name != null)
            {
                WriteIdentifier(writer, name);
            }

            writer.Write("(");
            WriteMethodArguments(writer, member.Arguments);
            writer.Write(")");
            if (member.Initializer != null)
            {
                writer.WriteLine();
                writer.Indent++;
                WriteConstructorInitializer(writer, member.Initializer);
                writer.Indent--;
            }
            writer.WriteLine();
            WriteStatements(writer, member.Statements);
        }

        protected virtual void WritePropertyDeclaration(IndentedTextWriter writer, PropertyDeclaration member)
        {
            WriteCustomAttributes(writer, member.CustomAttributes);
            WriteModifiers(writer, member.Modifiers);
            WriteTypeReference(writer, member.Type);
            writer.Write(" ");

            if (member.PrivateImplementationType != null)
            {
                WriteTypeReference(writer, member.PrivateImplementationType);
                writer.Write('.');
            }

            WriteIdentifier(writer, member.Name);

            writer.WriteLine();
            writer.WriteLine("{");
            writer.Indent++;

            if (member.Getter != null)
            {
                WriteCustomAttributes(writer, member.Getter.CustomAttributes);
                WriteModifiers(writer, member.Getter.Modifiers);
                if (member.Getter.Statements != null)
                {
                    writer.WriteLine("get");
                    WriteStatements(writer, member.Getter.Statements);
                }
                else
                {
                    writer.WriteLine("get;");
                }
            }

            if (member.Setter != null)
            {
                WriteCustomAttributes(writer, member.Setter.CustomAttributes);
                WriteModifiers(writer, member.Setter.Modifiers);
                if (member.Setter.Statements != null)
                {
                    writer.WriteLine("set");
                    WriteStatements(writer, member.Setter.Statements);
                }
                else
                {
                    writer.WriteLine("set;");
                }
            }

            writer.Indent--;
            writer.WriteLine("}");
        }

        protected virtual void WriteClassDeclaration(IndentedTextWriter writer, ClassDeclaration type)
        {
            writer.Write("class ");
            WriteIdentifier(writer, type.Name);
            WriteGenericParameters(writer, type);

            var baseTypes = GetBaseTypes(type);
            if (baseTypes.Any())
            {
                writer.Write(" : ");
                WriteTypeReferences(writer, baseTypes, ", ");
            }

            writer.WriteLine();
            WriteGenericParameterConstraints(writer, type);

            writer.WriteLine("{");
            writer.Indent++;
            WriteLines(writer, Concat(type.Members, type.Types), endOfLine: null);
            writer.Indent--;
            writer.WriteLine("}");
        }

        protected virtual void WriteStructDeclaration(IndentedTextWriter writer, StructDeclaration type)
        {
            writer.Write("struct ");
            WriteIdentifier(writer, type.Name);
            WriteGenericParameters(writer, type);

            var baseTypes = type.Implements;
            if (baseTypes.Any())
            {
                writer.Write(" : ");
                WriteTypeReferences(writer, baseTypes, ", ");
            }

            writer.WriteLine();
            WriteGenericParameterConstraints(writer, type);

            writer.WriteLine("{");
            writer.Indent++;
            WriteLines(writer, Concat(type.Members, type.Types), endOfLine: null);
            writer.Indent--;
            writer.WriteLine("}");
        }

        protected virtual void WriteInterfaceDeclaration(IndentedTextWriter writer, InterfaceDeclaration type)
        {
            writer.Write("interface ");
            WriteIdentifier(writer, type.Name);
            WriteGenericParameters(writer, type);

            var baseTypes = GetBaseTypes(type);
            if (baseTypes.Any())
            {
                writer.Write(" : ");
                WriteTypeReferences(writer, baseTypes, ", ");
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

        protected virtual void WriteModifiers(IndentedTextWriter writer, Modifiers modifiers)
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

        protected virtual void WriteCustomAttributes(IndentedTextWriter writer, CodeObjectCollection<CustomAttribute> attributes)
        {
            if (attributes.Count > 0)
            {
                Write(writer, attributes, writer.NewLine);
                writer.WriteLine();
            }
        }

        protected virtual void WriteCustomAttribute(IndentedTextWriter writer, CustomAttribute attribute)
        {
            WriteBeforeComments(writer, attribute);
            writer.Write("[");
            if (attribute.Target.HasValue)
            {
                writer.Write(WriteCustomAttributeTarget(attribute.Target.Value));
                writer.Write(": ");
            }

            WriteTypeReference(writer, attribute.Type);

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

        protected virtual void WriteCustomAttributeArgument(IndentedTextWriter writer, CustomAttributeArgument arg)
        {
            WriteBeforeComments(writer, arg);
            if (!string.IsNullOrEmpty(arg.PropertyName))
            {
                WriteIdentifier(writer, arg.PropertyName);
                writer.Write(" = ");
            }

            WriteExpression(writer, arg.Value);
            WriteAfterComments(writer, arg);
        }

        protected virtual void WriteMemberDeclaration(IndentedTextWriter writer, MemberDeclaration member)
        {
            WriteNullableContextBefore(writer, member);
            WriteXmlComments(writer, member);
            WriteBeforeComments(writer, member);
            switch (member)
            {
                case EnumerationMember o:
                    WriteEnumerationMember(writer, o);
                    break;

                case MethodDeclaration o:
                    WriteMethodDeclaration(writer, o);
                    break;

                case OperatorDeclaration o:
                    WriteOperatorDeclaration(writer, o);
                    break;

                case FieldDeclaration o:
                    WriteFieldDeclaration(writer, o);
                    break;

                case ConstructorDeclaration o:
                    WriteConstructorDeclaration(writer, o);
                    break;

                case PropertyDeclaration o:
                    WritePropertyDeclaration(writer, o);
                    break;

                case EventFieldDeclaration o:
                    WriteEventFieldDeclaration(writer, o);
                    break;

                default:
                    throw new NotSupportedException();
            }

            WriteAfterComments(writer, member);
            WriteNullableContextAfter(writer, member);
        }

        protected virtual void WriteDirective(IndentedTextWriter writer, Directive directive)
        {
            WriteBeforeComments(writer, directive);
            switch (directive)
            {
                case UsingDirective o:
                    WriteUsingDirective(writer, o);
                    break;

                default:
                    throw new NotSupportedException();
            }

            WriteAfterComments(writer, directive);
        }

        protected virtual void WriteUsingDirective(IndentedTextWriter writer, UsingDirective usingDirective)
        {
            writer.Write("using ");
            writer.Write(usingDirective.Namespace);
            writer.Write(";");
        }

        protected virtual void WriteCatchClause(IndentedTextWriter writer, CatchClause catchClause)
        {
            WriteBeforeComments(writer, catchClause);
            writer.Write("catch");
            if (catchClause.ExceptionType != null)
            {
                writer.Write(" (");
                WriteTypeReference(writer, catchClause.ExceptionType);
                if (!string.IsNullOrEmpty(catchClause.ExceptionVariableName))
                {
                    writer.Write(" ");
                    writer.Write(catchClause.ExceptionVariableName);
                }
                writer.Write(")");
            }
            writer.WriteLine();

            WriteStatements(writer, catchClause.Body);
            WriteAfterComments(writer, catchClause);
        }

        protected virtual void WriteCatchClauseCollection(IndentedTextWriter writer, CatchClauseCollection clauses)
        {
            Write(writer, clauses, "");
        }

        protected virtual void WriteConstructorInitializer(IndentedTextWriter writer, ConstructorInitializer initializer)
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

        protected virtual void WriteTypeParameterConstraint(IndentedTextWriter writer, TypeParameterConstraint constraint)
        {
            switch (constraint)
            {
                case BaseTypeParameterConstraint o:
                    WriteBaseTypeParameterContraint(writer, o);
                    break;

                case ClassTypeParameterConstraint o:
                    WriteClassTypeParameterConstraint(writer, o);
                    break;

                case ValueTypeTypeParameterConstraint o:
                    WriteValueTypeParameterContraint(writer, o);
                    break;

                case ConstructorParameterConstraint o:
                    WriteConstructParameterConstraint(writer, o);
                    break;

                case UnmanagedTypeParameterConstraint o:
                    WriteUnmanagedTypeParameterConstraint(writer, o);
                    break;

                default:
                    throw new NotSupportedException();
            }
        }

        protected virtual void WriteBaseTypeParameterContraint(IndentedTextWriter writer, BaseTypeParameterConstraint constraint)
        {
            WriteTypeReference(writer, constraint.Type);
        }

        protected virtual void WriteClassTypeParameterConstraint(IndentedTextWriter writer, ClassTypeParameterConstraint constraint)
        {
            writer.Write("class");
        }

        protected virtual void WriteValueTypeParameterContraint(IndentedTextWriter writer, ValueTypeTypeParameterConstraint constraint)
        {
            writer.Write("struct");
        }

        protected virtual void WriteUnmanagedTypeParameterConstraint(IndentedTextWriter writer, UnmanagedTypeParameterConstraint constraint)
        {
            writer.Write("unmanaged");
        }

        protected virtual void WriteConstructParameterConstraint(IndentedTextWriter writer, ConstructorParameterConstraint constraint)
        {
            writer.Write("new()");
        }

        protected virtual CommentType WriteComment(IndentedTextWriter writer, Comment comment)
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

        protected virtual void WriteXmlComment(IndentedTextWriter writer, XmlComment comment)
        {
            WriteDocumentationComment(writer, comment.Element?.ToString());
        }

        protected virtual string WriteBinaryOperator(BinaryOperator op)
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

        [SuppressMessage("Style", "IDE0066:Convert switch statement to expression", Justification = "Better readability")]
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

        protected virtual string WriteUnaryOperator(UnaryOperator op)
        {
            return op switch
            {
                UnaryOperator.None => "",
                UnaryOperator.Not => "!",
                UnaryOperator.Complement => "~",
                UnaryOperator.Plus => "+",
                UnaryOperator.Minus => "-",
                UnaryOperator.PreIncrement or UnaryOperator.PostIncrement => "++",
                UnaryOperator.PreDecrement or UnaryOperator.PostDecrement => "--",
                _ => throw new ArgumentOutOfRangeException(nameof(op)),
            };
        }

        protected virtual string WriteCustomAttributeTarget(CustomAttributeTarget target)
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

        protected virtual void WriteIdentifier(IndentedTextWriter writer, string? name)
        {
            if (name == null)
                return;

            if (s_keywords.Contains(name, StringComparer.Ordinal))
            {
                writer.Write("@");
            }

            writer.Write(name);
        }

        protected virtual void WriteStatements(IndentedTextWriter writer, StatementCollection? statements)
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

                    WriteStatement(writer, statement);
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

        protected virtual void WriteDocumentationComment(IndentedTextWriter writer, string? comment)
        {
            if (comment == null)
                return;

            using var sr = new StringReader(comment);
            string? line;
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

        protected virtual void WriteLineComment(IndentedTextWriter writer, string? comment)
        {
            if (comment == null)
            {
                writer.WriteLine("//");
                return;
            }

            using var sr = new StringReader(comment);
            string? line;
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

        protected virtual bool TryWriteInlineComment(IndentedTextWriter writer, string? comment)
        {
            if (comment == null)
            {
                writer.WriteLine("/* */");
                return true;
            }

            if (comment.Contains("*/", StringComparison.Ordinal))
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
                WriteXmlComment(writer, comment);
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

                type = WriteComment(writer, c);
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

                type = WriteComment(writer, c);
                first = false;
            }
        }

        private void WriteGenericParameters(IndentedTextWriter writer, IList<TypeReference> types)
        {
            if (types.Any())
            {
                writer.Write("<");
                WriteTypeReferences(writer, types, ", ");
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

        private void WriteLines<T>(IndentedTextWriter writer, IEnumerable<T> objects, string? endOfLine)
            where T : CodeObject
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

        protected virtual void WriteTypeReference(IndentedTextWriter writer, TypeReference? type)
        {
            if (type == null)
                return;

            if (s_predefinedTypes.TryGetValue(type.ClrFullTypeNameWithoutArray, out var keyword))
            {
                writer.Write(keyword);
            }
            else
            {
                if (type.TypeName != null)
                {
                    writer.Write(type.TypeName.Replace('+', '.'));
                }

                WriteGenericParameters(writer, type.Parameters);
            }

            if (type.Nullable == NullableAnnotation.Nullable)
            {
                writer.Write('?');
            }

            if (type.IsArray)
            {
                writer.Write('[');
                for (var i = 1; i < type.ArrayRank; i++)
                {
                    writer.Write(',');
                }

                writer.Write(']');
            }
        }

        protected virtual void WriteTypeReferences(IndentedTextWriter writer, IEnumerable<TypeReference?> types, string separator)
        {
            var first = true;
            foreach (var type in types)
            {
                if (type != null)
                {
                    if (!first)
                    {
                        writer.Write(separator);
                    }

                    WriteTypeReference(writer, type);
                    first = false;
                }
            }
        }

        protected virtual void WriteNullableContextBefore(IndentedTextWriter writer, INullableContext nullableContext)
        {
            switch (nullableContext.NullableContext)
            {
                case NullableContext.Enable:
                    writer.EnsureNewLine();
                    writer.WriteLineNoTabs("#nullable enable");
                    break;

                case NullableContext.Disable:
                    writer.EnsureNewLine();
                    writer.WriteLineNoTabs("#nullable disable");
                    break;
            }
        }

        protected virtual void WriteNullableContextAfter(IndentedTextWriter writer, INullableContext nullableContext)
        {
            switch (nullableContext.NullableContext)
            {
                case NullableContext.Enable:
                    writer.EnsureNewLine();
                    writer.WriteLineNoTabs("#nullable disable");
                    break;

                case NullableContext.Disable:
                    writer.EnsureNewLine();
                    writer.WriteLineNoTabs("#nullable enable");
                    break;
            }
        }

        private static IEnumerable<CodeObject> Concat(IEnumerable<CodeObject> items1, IEnumerable<CodeObject> items2)
        {
            foreach (var item in items1)
                yield return item;

            foreach (var item in items2)
                yield return item;
        }
    }
}
