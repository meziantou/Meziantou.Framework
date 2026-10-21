using Meziantou.Framework.JavaScriptEngine.Ast;

namespace Meziantou.Framework.JavaScriptEngine.Compiler;

internal sealed partial class CodeGenerator
{
    /// <summary>The short-circuit of an optional chain: the optional parts jump to a trampoline that pops the stack and pushes undefined.</summary>
    private sealed class ChainContext(int baseDepth, Label end)
    {
        public int BaseDepth { get; } = baseDepth;

        public Label End { get; } = end;

        public List<(Label Label, int Pops)> Trampolines { get; } = [];
    }

    private ChainContext? _chain;

    private void EmitExpression(Node node)
    {
        switch (node)
        {
            case NullLiteral:
                _builder.Emit(OpCode.PushNull);
                return;
            case BooleanLiteral boolean:
                _builder.Emit(boolean.Value ? OpCode.PushTrue : OpCode.PushFalse);
                return;
            case NumericLiteral number:
                _builder.EmitPushNumber(number.Value);
                return;
            case StringLiteral str:
                _builder.EmitPushString(str.Value);
                return;
            case BigIntLiteral bigint:
                _builder.Emit(OpCode.PushConst, _builder.AddConstant(JsValue.FromBigInt(bigint.Value)));
                return;
            case RegExpLiteral regexp:
                _builder.SetPosition(regexp.Start);
                _builder.Emit(OpCode.CreateRegExp, _builder.AddConstant(regexp.Pattern), _builder.AddConstant(regexp.Flags));
                return;
            case TemplateLiteral template:
                EmitTemplateLiteral(template);
                return;
            case TaggedTemplateExpression tagged:
                EmitTaggedTemplate(tagged);
                return;
            case Identifier identifier:
                _builder.SetPosition(identifier.Start);
                EmitLoadIdentifier(identifier);
                return;
            case ThisExpression:
                _builder.SetPosition(node.Start);
                EmitLoadSpecial(node, VariableKind.This, checkInitialized: true);
                return;
            case ArrayExpression array:
                EmitArray(array);
                return;
            case ObjectExpression obj:
                EmitObject(obj);
                return;
            case FunctionExpression function:
                EmitFunctionExpression(function.Function, function.Function.Id?.Name ?? "");
                return;
            case ArrowFunctionExpression arrow:
                EmitFunctionExpression(arrow.Function, "");
                return;
            case ClassExpression classExpression:
                EmitClass(classExpression.Class, classExpression.Class.Id?.Name ?? "");
                return;
            case UnaryExpression unary:
                EmitUnary(unary);
                return;
            case UpdateExpression update:
                EmitUpdate(update);
                return;
            case BinaryExpression binary:
                EmitBinary(binary);
                return;
            case LogicalExpression logical:
                EmitLogical(logical);
                return;
            case AssignmentExpression assignment:
                EmitAssignment(assignment);
                return;
            case ConditionalExpression conditional:
                {
                    EmitExpression(conditional.Test);
                    var elseLabel = _builder.DefineLabel();
                    var endLabel = _builder.DefineLabel();
                    _builder.EmitJump(OpCode.JumpIfFalse, elseLabel);
                    EmitExpression(conditional.Consequent);
                    _builder.EmitJump(OpCode.Jump, endLabel);
                    _builder.MarkLabel(elseLabel);
                    EmitExpression(conditional.Alternate);
                    _builder.MarkLabel(endLabel);
                    return;
                }

            case CallExpression call:
                EmitCall(call);
                return;
            case NewExpression newExpression:
                EmitNew(newExpression);
                return;
            case MemberExpression member:
                EmitMember(member);
                return;
            case ChainExpression chain:
                EmitChain(chain);
                return;
            case SequenceExpression sequence:
                for (var i = 0; i < sequence.Expressions.Count; i++)
                {
                    EmitExpression(sequence.Expressions[i]);
                    if (i < sequence.Expressions.Count - 1)
                    {
                        _builder.Emit(OpCode.Pop);
                    }
                }

                return;
            case YieldExpression yieldExpression:
                if (yieldExpression.Delegate)
                {
                    EmitYieldDelegate(yieldExpression);
                }
                else
                {
                    EmitYield(yieldExpression);
                }

                return;
            case AwaitExpression awaitExpression:
                EmitExpression(awaitExpression.Argument);
                _builder.SetPosition(awaitExpression.Start);
                _builder.Emit(OpCode.Await);
                return;
            case MetaProperty meta:
                if (meta.Kind == MetaPropertyKind.NewTarget)
                {
                    EmitLoadSpecial(meta, VariableKind.NewTarget, checkInitialized: false);
                }
                else
                {
                    _builder.Emit(OpCode.LoadImportMeta);
                }

                return;
            case ImportExpression import:
                EmitExpression(import.Source);
                if (import.Options is not null)
                {
                    EmitExpression(import.Options);
                }
                else
                {
                    _builder.Emit(OpCode.PushUndefined);
                }

                _builder.SetPosition(import.Start);
                _builder.Emit(OpCode.ImportCall);
                return;
            default:
                throw new InvalidOperationException("Unexpected expression: " + node.Type);
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Named evaluation
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>IsAnonymousFunctionDefinition(expr)</summary>
    private static bool IsAnonymousFunctionDefinition(Node node)
    {
        return node switch
        {
            FunctionExpression f => f.Function.Id is null,
            ArrowFunctionExpression => true,
            ClassExpression c => c.Class.Id is null,
            _ => false,
        };
    }

    /// <summary>Evaluates an expression with NamedEvaluation when it is an anonymous function definition and the target is an identifier.</summary>
    private void EmitNamedEvaluation(Node expression, Node? target)
    {
        if (target is Identifier identifier && IsAnonymousFunctionDefinition(expression))
        {
            EmitNamedEvaluation(expression, identifier.Name);
            return;
        }

        EmitExpression(expression);
    }

    private void EmitNamedEvaluation(Node expression, string name)
    {
        switch (expression)
        {
            case FunctionExpression { Function.Id: null } function:
                EmitFunctionExpression(function.Function, name);
                return;
            case ArrowFunctionExpression arrow:
                EmitFunctionExpression(arrow.Function, name);
                return;
            case ClassExpression { Class.Id: null } classExpression:
                EmitClass(classExpression.Class, name);
                return;
            default:
                EmitExpression(expression);
                return;
        }
    }

    /// <summary>Evaluates an anonymous function definition whose name is the property key on the stack (computed keys): key -> key function.</summary>
    private void EmitNamedEvaluationWithKeyOnStack(Node expression, int prefix)
    {
        EmitNamedEvaluation(expression, "");
        if (IsAnonymousFunctionDefinition(expression))
        {
            _builder.Emit(OpCode.Swap);
            _builder.Emit(OpCode.Dup);
            _builder.Emit(OpCode.Rot3);
            _builder.Emit(OpCode.SetFunctionNameComputed, prefix);
        }
    }

    private void EmitFunctionExpression(FunctionNode function, string name)
    {
        _builder.Emit(OpCode.CreateClosure, CompileNestedFunction(function, name));
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Literals
    // ------------------------------------------------------------------------------------------------------------------

    private void EmitTemplateLiteral(TemplateLiteral template)
    {
        _builder.EmitPushString(template.Quasis[0].Cooked ?? "");
        for (var i = 0; i < template.Expressions.Count; i++)
        {
            EmitExpression(template.Expressions[i]);
            _builder.Emit(OpCode.ToStringValue);
            _builder.Emit(OpCode.Add);
            var quasi = template.Quasis[i + 1].Cooked ?? "";
            if (quasi.Length > 0)
            {
                _builder.EmitPushString(quasi);
                _builder.Emit(OpCode.Add);
            }
        }
    }

    private void EmitTaggedTemplate(TaggedTemplateExpression tagged)
    {
        EmitCallee(tagged.Tag);
        var site = new TemplateSite([.. tagged.Quasi.Quasis.Select(q => q.Cooked)], [.. tagged.Quasi.Quasis.Select(q => q.Raw)]);
        _builder.Emit(OpCode.GetTemplateObject, _builder.AddObject(site));
        foreach (var expression in tagged.Quasi.Expressions)
        {
            EmitExpression(expression);
        }

        _builder.SetPosition(tagged.Start);
        _builder.Emit(OpCode.Call, tagged.Quasi.Expressions.Count + 1);
    }

    private void EmitArray(ArrayExpression array)
    {
        _builder.Emit(OpCode.NewArray);
        foreach (var element in array.Elements)
        {
            switch (element)
            {
                case null:
                    _builder.Emit(OpCode.PushHole);
                    _builder.Emit(OpCode.ArrayAppend);
                    break;
                case SpreadElement spread:
                    EmitExpression(spread.Argument);
                    _builder.SetPosition(spread.Start);
                    _builder.Emit(OpCode.ArrayAppendSpread);
                    break;
                default:
                    EmitExpression(element);
                    _builder.Emit(OpCode.ArrayAppend);
                    break;
            }
        }
    }

    private void EmitObject(ObjectExpression obj)
    {
        _builder.Emit(OpCode.NewObject);
        foreach (var node in obj.Properties)
        {
            if (node is SpreadElement spread)
            {
                EmitExpression(spread.Argument);
                _builder.Emit(OpCode.CopyDataProperties);
                continue;
            }

            var property = (Property)node;
            if (property.Kind != PropertyKind.Init || property.Method)
            {
                EmitMethodDefinition(property);
                continue;
            }

            // __proto__: value sets the prototype (not with a computed key nor a shorthand)
            if (!property.Computed && !property.Shorthand && GetStaticKeyName(property.Key) == "__proto__")
            {
                EmitExpression(property.Value);
                _builder.Emit(OpCode.SetPrototypeLiteral);
                continue;
            }

            if (property.Computed)
            {
                EmitExpression(property.Key);
                _builder.Emit(OpCode.ToPropertyKey);
                EmitNamedEvaluationWithKeyOnStack(property.Value, prefix: 0);
                _builder.Emit(OpCode.DefineFieldComputed);
            }
            else
            {
                var key = GetPropertyKey(property.Key);
                if (IsAnonymousFunctionDefinition(property.Value))
                {
                    EmitNamedEvaluation(property.Value, GetFunctionNameForKey(key));
                }
                else
                {
                    EmitExpression(property.Value);
                }

                _builder.Emit(OpCode.DefineField, _builder.AddKey(key));
            }
        }
    }

    /// <summary>A method, getter or setter of an object literal: object -> object.</summary>
    private void EmitMethodDefinition(Property property)
    {
        var function = ((FunctionExpression)property.Value).Function;
        var kind = property.Kind switch
        {
            PropertyKind.Get => 1,
            PropertyKind.Set => 2,
            _ => 0,
        };

        if (property.Computed)
        {
            EmitExpression(property.Key);
            _builder.Emit(OpCode.ToPropertyKey);
            _builder.Emit(OpCode.CreateClosure, CompileNestedFunction(function, ""));
            _builder.Emit(OpCode.Swap);
            _builder.Emit(OpCode.Dup);
            _builder.Emit(OpCode.Rot3);
            _builder.Emit(OpCode.SetFunctionNameComputed, kind);
        }
        else
        {
            var key = GetPropertyKey(property.Key);
            _builder.EmitPushString(""); // replaced below by the key
            _builder.Emit(OpCode.Pop);
            EmitPushKey(key);
            var prefix = kind switch
            {
                1 => "get ",
                2 => "set ",
                _ => "",
            };

            _builder.Emit(OpCode.CreateClosure, CompileNestedFunction(function, prefix + GetFunctionNameForKey(key)));
        }

        _builder.Emit(OpCode.DefineMethod, kind, 1);
    }

    /// <summary>Pushes a constant property key as a value (a string or a number).</summary>
    private void EmitPushKey(PropertyKey key) => _builder.EmitPushString(key.AsString());

    /// <summary>The name of an anonymous function defined by a property with a constant key.</summary>
    private static string GetFunctionNameForKey(PropertyKey key) => key.IsSymbol ? "[" + key.AsSymbol().Description + "]" : key.AsString();

    /// <summary>The property key of a non-computed key: an identifier, a string, a number or a BigInt.</summary>
    private static PropertyKey GetPropertyKey(Node key)
    {
        return key switch
        {
            Identifier identifier => new PropertyKey(identifier.Name),
            StringLiteral str => new PropertyKey(str.Value),
            NumericLiteral number => PropertyKey.FromDouble(number.Value),
            BigIntLiteral bigint => new PropertyKey(bigint.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            PrivateIdentifier privateIdentifier => new PropertyKey("#" + privateIdentifier.Name),
            _ => throw new InvalidOperationException("Not a static property key: " + key.Type),
        };
    }

    private static string? GetStaticKeyName(Node key)
    {
        return key switch
        {
            Identifier identifier => identifier.Name,
            StringLiteral str => str.Value,
            _ => null,
        };
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Operators
    // ------------------------------------------------------------------------------------------------------------------

    private void EmitUnary(UnaryExpression unary)
    {
        switch (unary.Operator)
        {
            case UnaryOperator.Delete:
                EmitDelete(unary.Argument);
                return;
            case UnaryOperator.TypeOf when unary.Argument is Identifier identifier:
                EmitLoadIdentifier(identifier, forTypeOf: true);
                _builder.Emit(OpCode.TypeOf);
                return;
            case UnaryOperator.Void:
                EmitExpression(unary.Argument);
                _builder.Emit(OpCode.Pop);
                _builder.Emit(OpCode.PushUndefined);
                return;
        }

        EmitExpression(unary.Argument);
        _builder.SetPosition(unary.Start);
        _builder.Emit(unary.Operator switch
        {
            UnaryOperator.Minus => OpCode.Negate,
            UnaryOperator.Plus => OpCode.ToNumber,
            UnaryOperator.LogicalNot => OpCode.LogicalNot,
            UnaryOperator.BitwiseNot => OpCode.BitwiseNot,
            UnaryOperator.TypeOf => OpCode.TypeOf,
            _ => throw new InvalidOperationException("Unknown unary operator"),
        });
    }

    private void EmitDelete(Node argument)
    {
        switch (argument)
        {
            case Identifier identifier:
                {
                    var reference = _analysis.References[identifier];
                    if (reference.IsDynamic || reference.Variable is null || reference.Variable.Location is VariableLocation.GlobalObject or VariableLocation.Dynamic)
                    {
                        _builder.Emit(OpCode.DeleteName, GetNameKey(identifier.Name));
                    }
                    else
                    {
                        _builder.Emit(OpCode.PushFalse);
                    }

                    return;
                }

            case MemberExpression { Object: Super } superMember:
                // delete super.x throws a ReferenceError (after evaluating the key)
                EmitLoadSpecial(superMember, VariableKind.This, checkInitialized: true);
                _builder.Emit(OpCode.Pop);
                if (superMember.Computed)
                {
                    EmitExpression(superMember.Property);
                    _builder.Emit(OpCode.Pop);
                }

                _builder.Emit(OpCode.ThrowError, (int)ErrorType.ReferenceError, _builder.AddConstant("Unsupported reference to 'super'"));
                _builder.Emit(OpCode.PushTrue);
                return;
            case MemberExpression member:
                EmitExpression(member.Object);
                _builder.SetPosition(member.Start);
                if (member.Computed)
                {
                    EmitExpression(member.Property);
                    _builder.Emit(OpCode.DeleteElement, IsStrict ? 1 : 0);
                }
                else
                {
                    _builder.Emit(OpCode.DeleteProperty, _builder.AddKey(GetPropertyKey(member.Property)), IsStrict ? 1 : 0);
                }

                return;
            case ChainExpression chain:
                EmitChainDelete(chain);
                return;
            default:
                EmitExpression(argument);
                _builder.Emit(OpCode.Pop);
                _builder.Emit(OpCode.PushTrue);
                return;
        }
    }

    private void EmitBinary(BinaryExpression binary)
    {
        if (binary.Left is PrivateIdentifier privateIdentifier)
        {
            EmitExpression(binary.Right);
            var variable = _analysis.References[privateIdentifier].Variable;
            if (variable is null)
            {
                _builder.Emit(OpCode.LoadName, GetNameKey("#" + privateIdentifier.Name));
                _builder.Emit(OpCode.PrivateIn, -1, 0);
            }
            else
            {
                _builder.Emit(OpCode.PrivateIn, GetContextDepth(variable.Scope), variable.Index);
            }

            return;
        }

        EmitExpression(binary.Left);
        EmitExpression(binary.Right);
        _builder.SetPosition(binary.Start);
        _builder.Emit(GetBinaryOpCode(binary.Operator));
    }

    private static OpCode GetBinaryOpCode(BinaryOperator op)
    {
        return op switch
        {
            BinaryOperator.Add => OpCode.Add,
            BinaryOperator.Subtract => OpCode.Subtract,
            BinaryOperator.Multiply => OpCode.Multiply,
            BinaryOperator.Divide => OpCode.Divide,
            BinaryOperator.Modulo => OpCode.Modulo,
            BinaryOperator.Exponentiate => OpCode.Exponentiate,
            BinaryOperator.LeftShift => OpCode.LeftShift,
            BinaryOperator.RightShift => OpCode.RightShift,
            BinaryOperator.UnsignedRightShift => OpCode.UnsignedRightShift,
            BinaryOperator.BitwiseAnd => OpCode.BitwiseAnd,
            BinaryOperator.BitwiseOr => OpCode.BitwiseOr,
            BinaryOperator.BitwiseXor => OpCode.BitwiseXor,
            BinaryOperator.Equal => OpCode.Equal,
            BinaryOperator.NotEqual => OpCode.NotEqual,
            BinaryOperator.StrictEqual => OpCode.StrictEqual,
            BinaryOperator.StrictNotEqual => OpCode.StrictNotEqual,
            BinaryOperator.LessThan => OpCode.LessThan,
            BinaryOperator.LessThanOrEqual => OpCode.LessThanOrEqual,
            BinaryOperator.GreaterThan => OpCode.GreaterThan,
            BinaryOperator.GreaterThanOrEqual => OpCode.GreaterThanOrEqual,
            BinaryOperator.In => OpCode.In,
            BinaryOperator.InstanceOf => OpCode.InstanceOf,
            _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown binary operator"),
        };
    }

    private static OpCode GetCompoundOpCode(AssignmentOperator op)
    {
        return op switch
        {
            AssignmentOperator.AddAssign => OpCode.Add,
            AssignmentOperator.SubtractAssign => OpCode.Subtract,
            AssignmentOperator.MultiplyAssign => OpCode.Multiply,
            AssignmentOperator.DivideAssign => OpCode.Divide,
            AssignmentOperator.ModuloAssign => OpCode.Modulo,
            AssignmentOperator.ExponentiateAssign => OpCode.Exponentiate,
            AssignmentOperator.LeftShiftAssign => OpCode.LeftShift,
            AssignmentOperator.RightShiftAssign => OpCode.RightShift,
            AssignmentOperator.UnsignedRightShiftAssign => OpCode.UnsignedRightShift,
            AssignmentOperator.BitwiseAndAssign => OpCode.BitwiseAnd,
            AssignmentOperator.BitwiseOrAssign => OpCode.BitwiseOr,
            AssignmentOperator.BitwiseXorAssign => OpCode.BitwiseXor,
            _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Not a compound assignment"),
        };
    }

    private void EmitLogical(LogicalExpression logical)
    {
        EmitExpression(logical.Left);
        var endLabel = _builder.DefineLabel();
        _builder.EmitJump(logical.Operator switch
        {
            LogicalOperator.And => OpCode.JumpIfFalseKeep,
            LogicalOperator.Or => OpCode.JumpIfTrueKeep,
            _ => OpCode.JumpIfNotNullishKeep,
        }, endLabel);
        _builder.Emit(OpCode.Pop);
        EmitExpression(logical.Right);
        _builder.MarkLabel(endLabel);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // References: member expressions, super, private names
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>The kinds of the targets of assignments and updates.</summary>
    private enum TargetKind
    {
        Identifier,

        /// <summary>An identifier resolved at runtime: the base is the resolved environment (with, eval).</summary>
        DynamicIdentifier,
        Property,
        Element,
        SuperProperty,
        SuperElement,
        Private,

        /// <summary>A call in sloppy code (web compatibility): the call is evaluated, then a ReferenceError is thrown.</summary>
        InvalidCall,
    }

    /// <summary>Evaluates the base (and the key) of a reference: the values a get and a put need (e.g. object, or object key).</summary>
    private TargetKind EmitReferenceBase(Node target)
    {
        switch (target)
        {
            case Identifier identifier when _analysis.References[identifier].IsDynamic:
                _builder.Emit(OpCode.ResolveName, GetNameKey(identifier.Name));
                return TargetKind.DynamicIdentifier;
            case Identifier:
                return TargetKind.Identifier;
            case MemberExpression { Object: Super } superMember:
                // this [key] base: the key is evaluated before GetSuperBase, and converted (ToPropertyKey) by the get or the put
                EmitLoadSpecial(superMember, VariableKind.This, checkInitialized: true);
                if (superMember.Computed)
                {
                    EmitExpression(superMember.Property);
                    EmitLoadSuperBase(superMember);
                    return TargetKind.SuperElement;
                }

                EmitLoadSuperBase(superMember);
                return TargetKind.SuperProperty;
            case MemberExpression { Property: PrivateIdentifier } privateMember:
                EmitExpression(privateMember.Object);
                return TargetKind.Private;
            case MemberExpression member:
                EmitExpression(member.Object);
                if (member.Computed)
                {
                    EmitExpression(member.Property);
                    return TargetKind.Element;
                }

                return TargetKind.Property;
            case CallExpression call:
                EmitExpression(call);
                _builder.Emit(OpCode.Pop);
                _builder.SetPosition(call.Start);
                _builder.Emit(OpCode.ThrowError, (int)ErrorType.ReferenceError, _builder.AddConstant("Invalid left-hand side in assignment"));
                return TargetKind.InvalidCall;
            default:
                throw new InvalidOperationException("Invalid assignment target: " + target.Type);
        }
    }

    /// <summary>Gets the value of a reference whose base is on the stack (the base is consumed).</summary>
    private void EmitGetReference(Node target, TargetKind kind)
    {
        _builder.SetPosition(target.Start);
        switch (kind)
        {
            case TargetKind.Identifier:
                EmitLoadIdentifier((Identifier)target);
                break;
            case TargetKind.DynamicIdentifier:
                _builder.Emit(OpCode.LoadResolved, GetNameKey(((Identifier)target).Name));
                break;
            case TargetKind.Property:
                _builder.Emit(OpCode.GetProperty, _builder.AddKey(GetPropertyKey(((MemberExpression)target).Property)));
                break;
            case TargetKind.Element:
                _builder.Emit(OpCode.GetElement);
                break;
            case TargetKind.SuperProperty:
                _builder.Emit(OpCode.GetSuperProperty, _builder.AddKey(GetPropertyKey(((MemberExpression)target).Property)));
                break;
            case TargetKind.SuperElement:
                _builder.Emit(OpCode.GetSuperElement);
                break;
            case TargetKind.Private:
                EmitPrivateOperation(OpCode.GetPrivate, (PrivateIdentifier)((MemberExpression)target).Property);
                break;
            case TargetKind.InvalidCall:
                // Unreachable: the base has thrown
                _builder.Emit(OpCode.PushUndefined);
                break;
        }
    }

    /// <summary>Puts the value on the stack in a reference whose base is below it (base value -> value).</summary>
    private void EmitPutReference(Node target, TargetKind kind)
    {
        _builder.SetPosition(target.Start);
        switch (kind)
        {
            case TargetKind.Identifier:
                _builder.Emit(OpCode.Dup);
                EmitStoreIdentifier((Identifier)target);
                break;
            case TargetKind.DynamicIdentifier:
                _builder.Emit(OpCode.StoreResolved, GetNameKey(((Identifier)target).Name), IsStrict ? 1 : 0);
                break;
            case TargetKind.Property:
                _builder.Emit(OpCode.SetProperty, _builder.AddKey(GetPropertyKey(((MemberExpression)target).Property)));
                break;
            case TargetKind.Element:
                _builder.Emit(OpCode.SetElement);
                break;
            case TargetKind.SuperProperty:
                _builder.Emit(OpCode.SetSuperProperty, _builder.AddKey(GetPropertyKey(((MemberExpression)target).Property)));
                break;
            case TargetKind.SuperElement:
                _builder.Emit(OpCode.SetSuperElement);
                break;
            case TargetKind.Private:
                EmitPrivateOperation(OpCode.SetPrivate, (PrivateIdentifier)((MemberExpression)target).Property);
                break;
        }
    }

    /// <summary>
    /// Converts the key of a computed reference whose value is read then written (compound assignments, updates): GetValue
    /// converts it (after ToObject of the base), so that PutValue does not convert it again.
    /// </summary>
    private void EmitConvertReferenceKey(TargetKind kind)
    {
        switch (kind)
        {
            case TargetKind.Element:
                // object key
                _builder.Emit(OpCode.RequireObjectCoercibleBase);
                _builder.Emit(OpCode.ToPropertyKey);
                break;
            case TargetKind.SuperElement:
                // this key base
                _builder.Emit(OpCode.Swap);
                _builder.Emit(OpCode.RequireObjectCoercibleBase);
                _builder.Emit(OpCode.ToPropertyKey);
                _builder.Emit(OpCode.Swap);
                break;
        }
    }

    /// <summary>The number of values on the stack for the base of a reference.</summary>
    private static int GetBaseSize(TargetKind kind)
    {
        return kind switch
        {
            TargetKind.Identifier or TargetKind.InvalidCall => 0,
            TargetKind.Property or TargetKind.Private or TargetKind.DynamicIdentifier => 1,
            TargetKind.SuperElement => 3,
            _ => 2,
        };
    }

    /// <summary>Duplicates the base of a reference (so that a get and a put can use it).</summary>
    private void EmitDuplicateBase(TargetKind kind)
    {
        switch (GetBaseSize(kind))
        {
            case 1:
                _builder.Emit(OpCode.Dup);
                break;
            case 2:
                _builder.Emit(OpCode.Dup2);
                break;
            case 3:
                _builder.Emit(OpCode.Dup3);
                break;
        }
    }

    /// <summary>Moves the value on the top of the stack below the base of a reference.</summary>
    private void EmitMoveBelowBase(TargetKind kind)
    {
        switch (GetBaseSize(kind))
        {
            case 1:
                _builder.Emit(OpCode.Rot3);
                break;
            case 2:
                _builder.Emit(OpCode.Rot4);
                break;
            case 3:
                _builder.Emit(OpCode.Rot5);
                break;
        }
    }

    /// <summary>Pushes the base of a super reference (GetSuperBase: the prototype of the home object).</summary>
    private void EmitLoadSuperBase(MemberExpression member)
    {
        EmitLoadSpecial(member.Object, VariableKind.HomeObject, checkInitialized: false);
        _builder.Emit(OpCode.LoadSuperBase);
    }

    private void EmitPrivateOperation(OpCode op, PrivateIdentifier name)
    {
        var variable = _analysis.References[name].Variable;
        if (variable is null)
        {
            // eval code: the private name is in the environment of the enclosing class
            _builder.Emit(OpCode.LoadName, GetNameKey("#" + name.Name));
            _builder.Emit(op, -1, 0);
            return;
        }

        _builder.Emit(op, GetContextDepth(variable.Scope), variable.Index);
    }

    private void EmitMember(MemberExpression member)
    {
        // a?.[key]: the object is checked before the key is evaluated
        if (member is { Optional: true, Computed: true })
        {
            EmitExpression(member.Object);
            EmitOptionalCheck(0);
            EmitExpression(member.Property);
            EmitGetReference(member, TargetKind.Element);
            return;
        }

        var kind = EmitReferenceBase(member);
        if (member.Optional)
        {
            EmitOptionalCheck(GetBaseSize(kind));
        }

        EmitGetReference(member, kind);
    }

    /// <summary>The optional part of a chain: when the value (below 'extra' values) is null or undefined, the chain is undefined.</summary>
    private void EmitOptionalCheck(int depthAboveBase)
    {
        var chain = _chain ?? throw new InvalidOperationException("An optional expression outside of a chain");
        var trampoline = _builder.DefineLabel();
        var pops = _builder.StackDepth - chain.BaseDepth;
        _ = depthAboveBase;
        chain.Trampolines.Add((trampoline, pops));
        _builder.EmitJump(OpCode.JumpIfNullishKeep, trampoline);
    }

    private void EmitChain(ChainExpression chain)
    {
        var outer = _chain;
        var context = new ChainContext(_builder.StackDepth, _builder.DefineLabel());
        _chain = context;
        EmitExpression(chain.Expression);
        _chain = outer;
        EmitChainTrampolines(context);
    }

    private void EmitChainTrampolines(ChainContext context)
    {
        if (context.Trampolines.Count == 0)
        {
            _builder.MarkLabel(context.End);
            return;
        }

        _builder.EmitJump(OpCode.Jump, context.End);
        foreach (var (label, pops) in context.Trampolines)
        {
            _builder.MarkLabel(label);
            for (var i = 0; i < pops; i++)
            {
                _builder.Emit(OpCode.Pop);
            }

            _builder.Emit(OpCode.PushUndefined);
            _builder.EmitJump(OpCode.Jump, context.End);
        }

        _builder.MarkLabel(context.End);
    }

    private void EmitChainDelete(ChainExpression chain)
    {
        // delete a?.b: true when the chain short-circuits
        var outer = _chain;
        var context = new ChainContext(_builder.StackDepth, _builder.DefineLabel());
        _chain = context;
        if (chain.Expression is MemberExpression member)
        {
            EmitExpression(member.Object);
            if (member.Optional)
            {
                EmitOptionalCheck(0);
            }

            if (member.Computed)
            {
                EmitExpression(member.Property);
                _builder.Emit(OpCode.DeleteElement, IsStrict ? 1 : 0);
            }
            else
            {
                _builder.Emit(OpCode.DeleteProperty, _builder.AddKey(GetPropertyKey(member.Property)), IsStrict ? 1 : 0);
            }
        }
        else
        {
            EmitExpression(chain.Expression);
            _builder.Emit(OpCode.Pop);
            _builder.Emit(OpCode.PushTrue);
        }

        _chain = outer;
        if (context.Trampolines.Count == 0)
        {
            _builder.MarkLabel(context.End);
            return;
        }

        _builder.EmitJump(OpCode.Jump, context.End);
        foreach (var (label, pops) in context.Trampolines)
        {
            _builder.MarkLabel(label);
            for (var i = 0; i < pops; i++)
            {
                _builder.Emit(OpCode.Pop);
            }

            _builder.Emit(OpCode.PushTrue);
            _builder.EmitJump(OpCode.Jump, context.End);
        }

        _builder.MarkLabel(context.End);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Assignments and updates
    // ------------------------------------------------------------------------------------------------------------------

    private void EmitAssignment(AssignmentExpression assignment)
    {
        var left = assignment.Left;
        if (assignment.Operator == AssignmentOperator.Assign)
        {
            if (left is ObjectPattern or ArrayPattern)
            {
                EmitExpression(assignment.Right);
                _builder.Emit(OpCode.Dup);
                EmitDestructuringAssignment(left);
                return;
            }

            var kind = EmitReferenceBase(left);
            EmitNamedEvaluation(assignment.Right, GetNamedEvaluationTarget(assignment));
            EmitPutReference(left, kind);
            return;
        }

        if (assignment.Operator is AssignmentOperator.AndAssign or AssignmentOperator.OrAssign or AssignmentOperator.NullishAssign)
        {
            EmitLogicalAssignment(assignment);
            return;
        }

        // Compound assignment: base, get, operator, put
        var targetKind = EmitReferenceBase(left);
        EmitConvertReferenceKey(targetKind);

        EmitDuplicateBase(targetKind);
        EmitGetReference(left, targetKind);
        EmitExpression(assignment.Right);
        _builder.SetPosition(assignment.Start);
        _builder.Emit(GetCompoundOpCode(assignment.Operator));
        EmitPutReference(left, targetKind);
    }

    /// <summary>The target that names an anonymous function of the right side: none when the left side is parenthesized (IsIdentifierRef of a CoverParenthesizedExpression is false).</summary>
    private static Node? GetNamedEvaluationTarget(AssignmentExpression assignment) => assignment.Left.Start == assignment.Start ? assignment.Left : null;

    private void EmitLogicalAssignment(AssignmentExpression assignment)
    {
        var left = assignment.Left;
        var kind = EmitReferenceBase(left);
        EmitConvertReferenceKey(kind);

        EmitDuplicateBase(kind);
        EmitGetReference(left, kind);
        var shortCircuit = _builder.DefineLabel();
        var endLabel = _builder.DefineLabel();
        _builder.EmitJump(assignment.Operator switch
        {
            AssignmentOperator.AndAssign => OpCode.JumpIfFalseKeep,
            AssignmentOperator.OrAssign => OpCode.JumpIfTrueKeep,
            _ => OpCode.JumpIfNotNullishKeep,
        }, shortCircuit);
        _builder.Emit(OpCode.Pop);
        EmitNamedEvaluation(assignment.Right, GetNamedEvaluationTarget(assignment));
        EmitPutReference(left, kind);
        _builder.EmitJump(OpCode.Jump, endLabel);

        // The value is kept; the base is dropped
        _builder.MarkLabel(shortCircuit);
        for (var i = 0; i < GetBaseSize(kind); i++)
        {
            _builder.Emit(OpCode.Swap);
            _builder.Emit(OpCode.Pop);
        }

        _builder.MarkLabel(endLabel);
    }

    /// <summary>Evaluates an expression whose value is not used (an expression statement, the parts of a for statement).</summary>
    private void EmitDiscardedExpression(Node expression)
    {
        if (expression is UpdateExpression update)
        {
            // i++ and ++i are the same when the value is not used: no copy of the old value
            if (update.Argument is Identifier identifier && _analysis.References[identifier] is { IsDynamic: false, NeedsTdzCheck: false, Variable: { Location: VariableLocation.Register, IsImmutable: false } variable }
                && variable.Kind != VariableKind.FunctionName)
            {
                _builder.SetPosition(update.Start);
                _builder.Emit(update.Operator == UpdateOperator.Increment ? OpCode.IncrementLocal : OpCode.DecrementLocal, variable.Index);
                return;
            }

            EmitUpdate(update, valueUsed: false);
            _builder.Emit(OpCode.Pop);
            return;
        }

        EmitExpression(expression);
        _builder.Emit(OpCode.Pop);
    }

    private void EmitUpdate(UpdateExpression update, bool valueUsed = true)
    {
        // When the value is not used, a postfix update is a prefix one (no copy of the old value)
        var prefix = update.Prefix || !valueUsed;
        var target = update.Argument;
        var kind = EmitReferenceBase(target);
        EmitConvertReferenceKey(kind);

        EmitDuplicateBase(kind);
        EmitGetReference(target, kind);
        _builder.SetPosition(update.Start);
        _builder.Emit(OpCode.ToNumeric);
        if (!prefix)
        {
            // The old value is the result: keep a copy below the base
            _builder.Emit(OpCode.Dup);
            EmitMoveBelowBase(kind);
        }

        _builder.Emit(update.Operator == UpdateOperator.Increment ? OpCode.Increment : OpCode.Decrement);
        EmitPutReference(target, kind);
        if (!prefix)
        {
            _builder.Emit(OpCode.Pop);
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Calls
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Pushes the function and the this value of a call.</summary>
    private void EmitCallee(Node callee)
    {
        switch (callee)
        {
            case MemberExpression { Object: Super } superMember:
                EmitLoadSpecial(superMember, VariableKind.This, checkInitialized: true);
                _builder.Emit(OpCode.Dup);
                if (superMember.Computed)
                {
                    EmitExpression(superMember.Property);
                    EmitLoadSuperBase(superMember);
                    _builder.Emit(OpCode.GetSuperElement);
                }
                else
                {
                    EmitLoadSuperBase(superMember);
                    _builder.Emit(OpCode.GetSuperProperty, _builder.AddKey(GetPropertyKey(superMember.Property)));
                }

                _builder.Emit(OpCode.Swap);
                return;
            case MemberExpression member:
                EmitExpression(member.Object);
                if (member.Optional)
                {
                    EmitOptionalCheck(0);
                }

                _builder.Emit(OpCode.Dup);
                if (member.Property is PrivateIdentifier privateName)
                {
                    EmitPrivateOperation(OpCode.GetPrivate, privateName);
                }
                else if (member.Computed)
                {
                    EmitExpression(member.Property);
                    _builder.Emit(OpCode.GetElement);
                }
                else
                {
                    // Dup, GetProperty, Swap
                    _builder.RemoveLastDup();
                    _builder.SetPosition(member.Property.Start);
                    _builder.Emit(OpCode.GetMethod, _builder.AddKey(GetPropertyKey(member.Property)));
                    return;
                }

                _builder.Emit(OpCode.Swap);
                return;
            case Identifier identifier:
                {
                    var reference = _analysis.References[identifier];
                    if (reference.IsDynamic)
                    {
                        _builder.Emit(OpCode.LoadNameForCall, GetNameKey(identifier.Name));
                        return;
                    }

                    EmitLoadIdentifier(identifier);
                    _builder.Emit(OpCode.PushUndefined);
                    return;
                }

            case ChainExpression chain when chain.Expression is MemberExpression:
                // (a?.b)() is not an optional call, but the this value is a
                EmitCalleeChain(chain);
                return;
            default:
                EmitExpression(callee);
                _builder.Emit(OpCode.PushUndefined);
                return;
        }
    }

    private void EmitCalleeChain(ChainExpression chain)
    {
        var outer = _chain;
        var context = new ChainContext(_builder.StackDepth, _builder.DefineLabel());
        _chain = context;
        EmitCallee(chain.Expression);
        _chain = outer;
        if (context.Trampolines.Count == 0)
        {
            _builder.MarkLabel(context.End);
            return;
        }

        _builder.EmitJump(OpCode.Jump, context.End);
        foreach (var (label, pops) in context.Trampolines)
        {
            _builder.MarkLabel(label);
            for (var i = 0; i < pops; i++)
            {
                _builder.Emit(OpCode.Pop);
            }

            _builder.Emit(OpCode.PushUndefined);
            _builder.Emit(OpCode.PushUndefined);
            _builder.EmitJump(OpCode.Jump, context.End);
        }

        _builder.MarkLabel(context.End);
    }

    private void EmitCall(CallExpression call)
    {
        if (call.Callee is Super)
        {
            EmitSuperCall(call);
            return;
        }

        var isDirectEval = call.Callee is Identifier { Name: "eval" } && !call.Optional;
        EmitCallee(call.Callee);
        if (call.Optional)
        {
            // callee?.(...): the function is below the this value
            _builder.Emit(OpCode.Swap);
            EmitOptionalCheck(0);
            _builder.Emit(OpCode.Swap);
        }

        var hasSpread = call.Arguments.Exists(a => a is SpreadElement);
        if (hasSpread)
        {
            EmitArgumentsArray(call.Arguments);
        }
        else
        {
            foreach (var argument in call.Arguments)
            {
                EmitExpression(argument);
            }
        }

        _builder.SetPosition(GetCallPosition(call.Callee, call.Start));
        _builder.AddCallSite(call.Callee.Start, call.Callee.End);
        if (isDirectEval)
        {
            var context = _builder.AddObject(CreateEvalContext());
            if (hasSpread)
            {
                _builder.Emit(OpCode.CallEvalSpread, context);
            }
            else
            {
                _builder.Emit(OpCode.CallEval, call.Arguments.Count, context);
            }
        }
        else if (hasSpread)
        {
            _builder.Emit(OpCode.CallSpread);
        }
        else
        {
            _builder.Emit(OpCode.Call, call.Arguments.Count);
        }
    }

    /// <summary>The context of the code of a direct eval: strictness, and the syntax allowed by the enclosing functions.</summary>
    private EvalContext CreateEvalContext()
    {
        var thisFunction = _function;
        while (!thisFunction.HasOwnThis && thisFunction.Parent is not null)
        {
            thisFunction = thisFunction.Parent;
        }

        var isFunction = thisFunction.Kind is not FunctionInfoKind.Script and not FunctionInfoKind.Module and not FunctionInfoKind.Eval;
        var privateNames = new List<string>();
        for (var scope = _scope; scope is not null; scope = scope.Parent)
        {
            if (scope.Kind == ScopeKind.Class)
            {
                foreach (var variable in scope.OrderedVariables)
                {
                    if (variable.Kind == VariableKind.PrivateName)
                    {
                        privateNames.Add(variable.Name[1..]);
                    }
                }
            }
        }

        return new EvalContext(
            IsStrict: IsStrict,
            AllowNewTarget: isFunction,
            AllowSuperProperty: isFunction && (thisFunction.Kind is FunctionInfoKind.Method or FunctionInfoKind.ClassConstructor or FunctionInfoKind.ClassFieldInitializer or FunctionInfoKind.ClassStaticBlock),
            AllowSuperCall: isFunction && thisFunction.Kind == FunctionInfoKind.ClassConstructor && thisFunction.FunctionNode!.IsDerivedConstructor,
            InClassFieldInitializer: thisFunction.Kind == FunctionInfoKind.ClassFieldInitializer,
            PrivateNames: [.. privateNames]);
    }

    private void EmitArgumentsArray(List<Node> arguments)
    {
        _builder.Emit(OpCode.NewArray);
        foreach (var argument in arguments)
        {
            if (argument is SpreadElement spread)
            {
                EmitExpression(spread.Argument);
                _builder.Emit(OpCode.ArrayAppendSpread);
            }
            else
            {
                EmitExpression(argument);
                _builder.Emit(OpCode.ArrayAppend);
            }
        }
    }

    /// <summary>
    /// The source position of a call or a construction (its errors, stack frames and the location of the code it calls):
    /// the name of the method of a method call, else the callee, as in the browsers (e.g. "b" in "a.b()", "F" in "new F()").
    /// </summary>
    private static int GetCallPosition(Node callee, int expressionStart) => callee switch
    {
        MemberExpression { Computed: false } member => member.Property.Start,
        Identifier identifier => identifier.Start,
        _ => expressionStart,
    };

    private void EmitNew(NewExpression newExpression)
    {
        EmitExpression(newExpression.Callee);
        if (newExpression.Arguments.Exists(a => a is SpreadElement))
        {
            EmitArgumentsArray(newExpression.Arguments);
            _builder.SetPosition(GetCallPosition(newExpression.Callee, newExpression.Start));
            _builder.AddCallSite(newExpression.Callee.Start, newExpression.Callee.End);
            _builder.Emit(OpCode.NewSpread);
            return;
        }

        foreach (var argument in newExpression.Arguments)
        {
            EmitExpression(argument);
        }

        _builder.SetPosition(GetCallPosition(newExpression.Callee, newExpression.Start));
        _builder.AddCallSite(newExpression.Callee.Start, newExpression.Callee.End);
        _builder.Emit(OpCode.New, newExpression.Arguments.Count);
    }

    private void EmitSuperCall(CallExpression call)
    {
        // The active function (its prototype is the super constructor) and new.target
        EmitLoadSpecial(call, VariableKind.ActiveFunction, checkInitialized: false);
        EmitLoadSpecial(call, VariableKind.NewTarget, checkInitialized: false);
        if (call.Arguments.Exists(a => a is SpreadElement))
        {
            EmitArgumentsArray(call.Arguments);
            _builder.SetPosition(call.Start);
            _builder.Emit(OpCode.SuperCallSpread);
        }
        else
        {
            foreach (var argument in call.Arguments)
            {
                EmitExpression(argument);
            }

            _builder.SetPosition(call.Start);
            _builder.Emit(OpCode.SuperCall, call.Arguments.Count);
        }

        // Bind the this value (ReferenceError when it is already bound), then initialize the fields
        var thisReference = _analysis.GetSpecialReference(call, VariableKind.This);
        if (thisReference.Kind == SpecialReferenceKind.Variable)
        {
            var thisVariable = thisReference.Variable!;
            EmitLoadVariable(thisVariable, checkTdz: false);
            _builder.Emit(OpCode.CheckThisUninitialized);
            _builder.Emit(OpCode.Pop);
            _builder.Emit(OpCode.Dup);
            EmitInitializeVariable(thisVariable);
        }
        else if (thisReference.Kind == SpecialReferenceKind.Dynamic)
        {
            _builder.Emit(OpCode.LoadNameOrUndefined, GetNameKey("this"));
            _builder.Emit(OpCode.CheckThisUninitialized);
            _builder.Emit(OpCode.Pop);
            _builder.Emit(OpCode.Dup);
            _builder.Emit(OpCode.InitializeName, GetNameKey("this"));
        }
        else
        {
            _builder.Emit(OpCode.BindThis);
        }

        EmitLoadSpecial(call, VariableKind.ActiveFunction, checkInitialized: false);
        _builder.Emit(OpCode.InitializeInstanceElements);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Generators
    // ------------------------------------------------------------------------------------------------------------------

    private bool IsAsyncGenerator => _function.FunctionNode is { IsAsync: true, IsGenerator: true };

    private void EmitYield(YieldExpression yieldExpression)
    {
        if (yieldExpression.Argument is not null)
        {
            EmitExpression(yieldExpression.Argument);
        }
        else
        {
            _builder.Emit(OpCode.PushUndefined);
        }

        _builder.SetPosition(yieldExpression.Start);
        if (IsAsyncGenerator)
        {
            _builder.Emit(OpCode.Await);
        }
        else
        {
            _builder.Emit(OpCode.CreateIterResult);
        }

        _builder.Emit(OpCode.Yield);
        EmitResumeDispatch(awaitReturnValue: IsAsyncGenerator);
    }

    /// <summary>
    /// After a yield: the stack is (received, resumeKind). A throw resumption throws the received value, a return resumption
    /// returns it (through the finally blocks), a next resumption leaves it on the stack.
    /// </summary>
    private void EmitResumeDispatch(bool awaitReturnValue)
    {
        var nextLabel = _builder.DefineLabel();
        var returnLabel = _builder.DefineLabel();
        _builder.Emit(OpCode.Dup);
        _builder.EmitPushNumber(0);
        _builder.Emit(OpCode.StrictEqual);
        _builder.EmitJump(OpCode.JumpIfTrue, nextLabel);
        _builder.EmitPushNumber(1);
        _builder.Emit(OpCode.StrictEqual);
        _builder.EmitJump(OpCode.JumpIfFalse, returnLabel);
        _builder.Emit(OpCode.Throw);
        _builder.MarkLabel(returnLabel);
        if (awaitReturnValue)
        {
            _builder.Emit(OpCode.Await);
        }

        EmitReturn();
        _builder.StackDepth++;
        _builder.MarkLabel(nextLabel);
        _builder.Emit(OpCode.Pop);
    }

    private void EmitYieldDelegate(YieldExpression yieldExpression)
    {
        var isAsync = IsAsyncGenerator;
        EmitExpression(yieldExpression.Argument!);
        _builder.SetPosition(yieldExpression.Start);
        _builder.Emit(isAsync ? OpCode.GetAsyncIterator : OpCode.GetIterator);
        var iterator = _function.AllocateRegister();
        var received = _function.AllocateRegister();
        var kind = _function.AllocateRegister();
        _builder.Emit(OpCode.StoreLocal, iterator);
        _builder.Emit(OpCode.PushUndefined);
        _builder.Emit(OpCode.StoreLocal, received);
        _builder.EmitPushNumber(0);
        _builder.Emit(OpCode.StoreLocal, kind);

        var loopLabel = _builder.DefineLabel();
        var doneLabel = _builder.DefineLabel();
        var returnWithoutMethod = _builder.DefineLabel();
        _builder.MarkLabel(loopLabel);
        _builder.Emit(OpCode.LoadLocal, iterator);
        _builder.Emit(OpCode.LoadLocal, received);
        _builder.Emit(OpCode.LoadLocal, kind);

        // (iterator, received, kind) -> result, or undefined when a return resumption finds no return method
        _builder.Emit(OpCode.IteratorDelegateStep, isAsync ? 1 : 0);
        _builder.Emit(OpCode.Dup);
        _builder.Emit(OpCode.PushHole);
        _builder.Emit(OpCode.StrictEqual);
        _builder.EmitJump(OpCode.JumpIfTrue, returnWithoutMethod);
        if (isAsync)
        {
            _builder.Emit(OpCode.Await);
            _builder.Emit(OpCode.ThrowIfNotObject);
        }

        _builder.Emit(OpCode.Dup);
        _builder.Emit(OpCode.IteratorComplete);
        _builder.EmitJump(OpCode.JumpIfTrue, doneLabel);
        if (isAsync)
        {
            _builder.Emit(OpCode.IteratorValue);
        }

        _builder.Emit(OpCode.Yield);
        _builder.Emit(OpCode.StoreLocal, kind);
        _builder.Emit(OpCode.StoreLocal, received);
        if (isAsync)
        {
            // AsyncGeneratorUnwrapYieldResumption: the value of a return resumption is awaited; when the await throws, the
            // resumption is a throw completion
            var depth = _builder.StackDepth;
            _builder.Emit(OpCode.LoadLocal, kind);
            _builder.EmitPushNumber(2);
            _builder.Emit(OpCode.StrictEqual);
            _builder.EmitJump(OpCode.JumpIfFalse, loopLabel);
            var regionStart = _builder.Position;
            _builder.Emit(OpCode.LoadLocal, received);
            _builder.Emit(OpCode.Await);
            _builder.Emit(OpCode.StoreLocal, received);
            var regionEnd = _builder.Position;
            _builder.EmitJump(OpCode.Jump, loopLabel);

            var awaitThrew = _builder.DefineLabel();
            _builder.SetLabelDepth(awaitThrew, depth + 1);
            _builder.MarkLabel(awaitThrew);
            _builder.AddHandler(regionStart, regionEnd, awaitThrew, depth, environmentRegister: -1);
            _builder.Emit(OpCode.StoreLocal, received);
            _builder.EmitPushNumber(1);
            _builder.Emit(OpCode.StoreLocal, kind);
        }

        _builder.EmitJump(OpCode.Jump, loopLabel);

        // A return resumption without a return method returns the received value
        _builder.MarkLabel(returnWithoutMethod);
        _builder.Emit(OpCode.Pop);
        _builder.Emit(OpCode.LoadLocal, received);
        if (isAsync)
        {
            _builder.Emit(OpCode.Await);
        }

        EmitReturn();

        // Done: the value is the result of yield*, or the return value for a return resumption
        _builder.StackDepth++;
        _builder.MarkLabel(doneLabel);
        _builder.Emit(OpCode.IteratorValue);
        var notReturn = _builder.DefineLabel();
        _builder.Emit(OpCode.LoadLocal, kind);
        _builder.EmitPushNumber(2);
        _builder.Emit(OpCode.StrictEqual);
        _builder.EmitJump(OpCode.JumpIfFalse, notReturn);
        EmitReturn();
        _builder.StackDepth++;
        _builder.MarkLabel(notReturn);
    }
}
