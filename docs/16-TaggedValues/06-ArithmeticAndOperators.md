---
title: Arithmetic and operators
description: How tags flow through arithmetic, and how to tag the result and the operands of your own operators.
---

## Built-in arithmetic

Built-in arithmetic on numbers keeps the tag, including on the numbers of the framework such as `Int128`, `Half`, and
`BigInteger`. So `orderNumber + 1 == projectNumber` is still reported:

<!-- ref:../../samples/Meziantou.Framework.TaggedValues.Samples/Arithmetic.cs;region=TaggedValues_Arithmetic;format=md-fenced-code-block;dedent=true -->
```csharp
_ = orderNumber + 1 == projectNumber; // MFTV0001, orderNumber + 1 is still an OrderNumber
_ = orderNumber + projectNumber;      // MFTV0003

var doubled = meters * 2;             // still in Meters
_ = doubled == seconds;               // MFTV0001

var speed = meters / seconds;         // not tagged
_ = speed == seconds;                 // ok
```
<!-- endref -->

| Operation | Tag of the result |
| -- | -- |
| `+` and `-` | The tag of their operands. Adding values with different tags is reported ([MFTV0003](09-Rules/04-MFTV0003.md)). |
| `*` and `/` | The tag of the tagged operand when the other operand is not tagged: `price * 2` is a price, but `meters / seconds` is not tagged. |
| Unary `+` and `-`, `++`, `--`, `+=`, `-=` | The tag of the operand. |
| `%`, bitwise operators, shifts, string concatenations | Not tagged: they create another kind of value. |

## User-defined operators and conversions

A user-defined operator or conversion creates a new value, so it does not keep the tag of its operands: `dueDate -
startDate` is a `TimeSpan`, not a due date. Tag the return value of the operator to tag its result, and its parameters
to check its operands:

<!-- ref:../../samples/Meziantou.Framework.TaggedValues.Samples/Distance.cs;region=TaggedValues_Operator;format=md-fenced-code-block;dedent=true -->
```csharp
[return: ValueTag("Meters")]
public static Distance operator +([ValueTag("Meters")] Distance left, [ValueTag("Meters")] Distance right) => new(left.Value + right.Value);
```
<!-- endref -->

The parameters of an operator check the operands of a comparison operator, and the target of a compound assignment such
as `+=`, like any other argument.
