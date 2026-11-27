using Meziantou.Framework.WasmEngine.Text;

namespace Meziantou.Framework.WasmEngine.Tests;

public sealed class ComponentWatParserTests
{
    private const string Preamble = "0061736D 0D000100";
    private const string EmptyModule = "0061736D 01000000";

    private static void AssertEncoding(string text, string expected)
    {
        var actual = WasmRuntime.WatToWasm(text);
        Assert.Equal(expected.Replace(" ", "", StringComparison.Ordinal), Convert.ToHexString(actual));
    }

    [Fact]
    public void EmptyComponent()
    {
        AssertEncoding("(component)", Preamble);
        AssertEncoding("(component $C)", Preamble);
    }

    [Fact]
    public void BinaryAndQuote()
    {
        Assert.Equal([0x00, 0x61, 0x73, 0x6D, 0x0D, 0x00, 0x01, 0x00], WasmRuntime.WatToWasm("""(component binary "\00asm" "\0d\00\01\00")"""));
        AssertEncoding("""(component $C quote "(type u8)")""", Preamble + "07 02 01 7D");
    }

    [Fact]
    public void ScriptDefinition()
    {
        var node = SExpressionReader.Parse("(component definition $C (type u8))")[0];
        Assert.Equal(Convert.FromHexString("0061736D0D000100" + "0702017D"), ComponentWatParser.ParseComponent(node));
    }

    [Fact]
    public void CoreModuleAndCoreInstance()
    {
        AssertEncoding("""
            (component
              (core module $M)
              (core instance $m (instantiate $M)))
            """, Preamble + "01 08 " + EmptyModule + "02 04 01 00 00 00");
    }

    [Fact]
    public void RecordWithInlineType()
    {
        // The inline list type is defined before the record
        AssertEncoding("""
            (component
              (type $r (record (field "a" u8) (field "b" (list string)))))
            """, Preamble + "07 0B 02 70 73 72 02 01 61 7D 01 62 00");
    }

    [Fact]
    public void DefinedValueTypes()
    {
        AssertEncoding("""
            (component
              (type (variant (case "a") (case "b" u32)))
              (type (tuple u8 s8))
              (type (flags "x" "y"))
              (type (enum "x"))
              (type (option bool))
              (type (result u32 (error string)))
              (type (result))
              (type (list u8 4))
              (type (stream u8))
              (type (future))
              (type (map string u32))
              (type error-context)
              (type $res (resource (rep i32)))
              (type (own $res))
              (type (borrow $res)))
            """, Preamble + "07 37 0F"
            + "71 02 01 61 00 00 01 62 01 79 00"
            + "6F 02 7D 7E"
            + "6E 02 01 78 01 79"
            + "6D 01 01 78"
            + "6B 7F"
            + "6A 01 79 01 73"
            + "6A 00 00"
            + "67 7D 04"
            + "66 01 7D"
            + "65 00"
            + "63 73 79"
            + "64"
            + "3F 7F 00"
            + "69 0C"
            + "68 0C");
    }

    [Fact]
    public void FunctionTypesAndImports()
    {
        // Each inline type is a new type, even when it is equal to a preceding type
        AssertEncoding("""
            (component
              (import "f" (func $f (param "x" string) (result (list u8))))
              (import "g" (func (param "x" string) (result (list u8))))
              (type $t (func async))
              (import "h" (func (type $t))))
            """, Preamble
            + "07 0A 02 70 7D 40 01 01 78 73 00 00"
            + "0A 06 01 00 01 66 01 01"
            + "07 0A 02 70 7D 40 01 01 78 73 00 02"
            + "0A 06 01 00 01 67 01 03"
            + "07 05 01 43 00 01 00"
            + "0A 06 01 00 01 68 01 04");
    }

    [Fact]
    public void InstanceTypeWithImplicitOuterAlias()
    {
        // $T is a type of the component: the instance type aliases it
        AssertEncoding("""
            (component $C
              (type $T u32)
              (type (instance
                (export "r" (type $r (sub resource)))
                (export "f" (func (param "a" $T) (result (own $r)))))))
            """, Preamble + "07 20 02 79"
            + "42 05"
            + "04 00 01 72 03 01"
            + "01 69 00"
            + "02 03 02 01 00"
            + "01 40 01 01 61 02 00 01"
            + "04 00 01 66 01 03");
    }

    [Fact]
    public void CanonicalDefinitionsWithInlineAliases()
    {
        AssertEncoding("""
            (component
              (import "i" (instance $i (export "f" (func))))
              (core module $M)
              (core instance $m (instantiate $M))
              (core func $lowered (canon lower (func $i "f") string-encoding=utf16 (memory (core memory $m "mem")) (realloc (core func $m "realloc"))))
              (func $lifted (canon lift (core func $m "run") async (callback (core func $m "cb")) (post-return 0))))
            """, Preamble
            + "07 0E 01 42 02 01 40 00 01 00 04 00 01 66 01 00"
            + "0A 06 01 00 01 69 05 00"
            + "01 08 " + EmptyModule
            + "02 04 01 00 00 00"
            + "06 1A 03 01 00 00 01 66 00 02 01 00 03 6D 65 6D 00 00 01 00 07 72 65 61 6C 6C 6F 63"
            + "08 0A 01 01 00 00 03 01 03 00 04 00"
            + "07 05 01 40 00 01 00"
            + "06 10 02 00 00 01 00 03 72 75 6E 00 00 01 00 02 63 62"
            + "08 0B 01 00 00 02 03 06 07 03 05 00 01");
    }

    [Fact]
    public void NestedComponentAndInstantiation()
    {
        AssertEncoding("""
            (component
              (type $T string)
              (component $C
                (import "x" (func))
                (type (list $T)))
              (import "f" (func $f))
              (instance $c (instantiate $C (with "x" (func $f)) (with "y" (instance (export "g" (func $f))))))
              (export "c" (instance $c))
              (export $e "e" (func $f) (func)))
            """, Preamble
            + "07 02 01 73"
            + "04 23 " + Preamble + "07 05 01 40 00 01 00 0A 06 01 00 01 78 01 00 06 05 01 03 02 01 00 07 03 01 70 01"
            + "07 05 01 40 00 01 00"
            + "0A 06 01 00 01 66 01 01"
            + "05 13 02 01 01 00 01 67 01 00 00 00 02 01 78 01 00 01 79 05 00"
            + "0B 07 01 00 01 63 05 01 00"
            + "07 05 01 40 00 01 00"
            + "0B 09 01 00 01 65 01 00 01 01 02");
    }

    [Fact]
    public void CoreTypesAndModuleType()
    {
        AssertEncoding("""
            (component
              (core type $ft (func (param i32) (result i32)))
              (core type (sub (func)))
              (core rec (type $a (struct)) (type (array i8)))
              (core type (module
                (type $f (func (param i32) (result i32)))
                (import "a" "b" (func (param i32) (result i32)))
                (import "a" "m" (memory 1))
                (alias outer 1 0 (type $g))
                (export "c" (func (type $g)))
                (export "t" (global (mut i64)))))
              (import "m" (core module (type 4))))
            """, Preamble
            + "03 3A 04"
            + "60 01 7F 01 7F"
            + "00 50 00 60 00 00"
            + "4E 02 5F 00 5E 78 00"
            + "50 06 01 60 01 7F 01 7F 00 01 61 01 62 00 00 00 01 61 01 6D 02 00 01 02 10 01 01 00 03 01 63 00 01 03 01 74 03 7E 01"
            + "0A 07 01 00 01 6D 00 11 04");
    }

    [Fact]
    public void ModuleTypeWithImplicitOuterAlias()
    {
        AssertEncoding("""
            (component $C
              (core type $F (func))
              (core type (module (import "a" "b" (func (type $F))))))
            """, Preamble + "03 12 02 60 00 00 50 02 02 10 01 01 00 00 01 61 01 62 00 00");
    }

    [Fact]
    public void ModuleTypeInlineFunctionTypes()
    {
        // An inline function type reuses a type declaration with the same signature, not a preceding inline type
        AssertEncoding("""
            (component
              (core type (module
                (import "a" "b" (func (param i32)))
                (import "a" "c" (func (param $p i32)))
                (type (func (param i32)))
                (export "d" (func (param i32))))))
            """, Preamble + "03 25 01 50 06"
            + "01 60 01 7F 00"
            + "00 01 61 01 62 00 00"
            + "01 60 01 7F 00"
            + "00 01 61 01 63 00 01"
            + "01 60 01 7F 00"
            + "03 01 64 00 02");
    }

    [Theory]
    [InlineData("(canon lift (core func 0) (func (type 3)))", "00 00 00 00 03")]
    [InlineData("(canon lower (func $f) (core func))", "01 00 00 00")]
    [InlineData("(canon resource.new $r (core func))", "02 00")]
    [InlineData("(canon resource.drop $r (core func))", "03 00")]
    [InlineData("(canon resource.rep $r (core func))", "04 00")]
    [InlineData("(canon context.get i32 0 (core func))", "0A 7F 00")]
    [InlineData("(canon context.set i32 1 (core func))", "0B 7F 01")]
    [InlineData("(canon backpressure.inc (core func))", "24")]
    [InlineData("(canon backpressure.dec (core func))", "25")]
    [InlineData("(canon task.return (result u32) (memory $mem) (core func))", "09 00 79 01 03 00")]
    [InlineData("(canon task.return (core func))", "09 01 00 00")]
    [InlineData("(canon task.cancel (core func))", "05")]
    [InlineData("(canon subtask.cancel async (core func))", "06 01")]
    [InlineData("(canon subtask.drop (core func))", "0D")]
    [InlineData("(canon stream.new $s (core func))", "0E 01")]
    [InlineData("(canon stream.read $s (memory $mem) async (core func))", "0F 01 02 03 00 06")]
    [InlineData("(canon stream.write $s (memory $mem) (core func))", "10 01 01 03 00")]
    [InlineData("(canon stream.cancel-read $s async (core func))", "11 01 01")]
    [InlineData("(canon stream.cancel-write $s (core func))", "12 01 00")]
    [InlineData("(canon stream.drop-readable $s (core func))", "13 01")]
    [InlineData("(canon stream.drop-writable $s (core func))", "14 01")]
    [InlineData("(canon stream.forward $s (core func))", "2E 01")]
    [InlineData("(canon future.new $fu (core func))", "15 02")]
    [InlineData("(canon future.read $fu (memory $mem) (core func))", "16 02 01 03 00")]
    [InlineData("(canon future.write $fu (core func))", "17 02 00")]
    [InlineData("(canon future.cancel-read $fu (core func))", "18 02 00")]
    [InlineData("(canon future.cancel-write $fu async (core func))", "19 02 01")]
    [InlineData("(canon future.drop-readable $fu (core func))", "1A 02")]
    [InlineData("(canon future.drop-writable $fu (core func))", "1B 02")]
    [InlineData("(canon future.forward (type $fu) (core func))", "2F 02")]
    [InlineData("(canon error-context.new (memory $mem) string-encoding=utf8 (core func))", "1C 02 03 00 00")]
    [InlineData("(canon error-context.debug-message (memory $mem) (core func))", "1D 01 03 00")]
    [InlineData("(canon error-context.drop (core func))", "1E")]
    [InlineData("(canon waitable-set.new (core func))", "1F")]
    [InlineData("(canon waitable-set.wait (memory $mem) (core func))", "20 00 00")]
    [InlineData("(canon waitable-set.poll (memory 0) (core func))", "21 00 00")]
    [InlineData("(canon waitable-set.drop (core func))", "22")]
    [InlineData("(canon waitable.join (core func))", "23")]
    [InlineData("(canon thread.index (core func))", "26")]
    [InlineData("(canon thread.new-indirect $ft (core table $tbl) (core func))", "27 00 00")]
    [InlineData("(canon thread.resume-later (core func))", "28")]
    [InlineData("(canon thread.suspend (core func))", "29 00")]
    [InlineData("(canon thread.yield (core func))", "0C 00")]
    [InlineData("(canon thread.suspend-then-resume (core func))", "2A 00")]
    [InlineData("(canon thread.yield-then-resume (core func))", "2B 00")]
    [InlineData("(canon thread.suspend-then-promote (core func))", "2C 00")]
    [InlineData("(canon thread.yield-then-promote (core func))", "2D 00")]
    [InlineData("(canon thread.spawn-ref shared $ft (core func))", "40 01 00")]
    [InlineData("(canon thread.spawn-indirect (core type $ft) $tbl (core func))", "41 00 00 00")]
    [InlineData("(canon thread.available-parallelism (core func))", "42 00")]
    [InlineData("(core func (canon resource.new $r))", "02 00")]
    [InlineData("(core func $lowered (canon lower (func 0) (memory $mem)))", "01 00 00 01 03 00")]
    public void CanonicalBuiltins(string canon, string expected)
    {
        var bytes = WasmRuntime.WatToWasm($$"""
            (component
              (type $r (resource (rep i32)))
              (type $s (stream u8))
              (type $fu (future))
              (core module $M)
              (core instance $m (instantiate $M))
              (alias core export $m "mem" (core memory $mem))
              (alias core export $m "tbl" (core table $tbl))
              (core type $ft (func (param i32)))
              (import "f" (func $f))
              {{canon}})
            """);
        var content = Convert.FromHexString(expected.Replace(" ", "", StringComparison.Ordinal));
        var section = Convert.FromHexString("08" + (content.Length + 1).ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + "01" + Convert.ToHexString(content));
        Assert.Equal(Convert.ToHexString(section), Convert.ToHexString(bytes.AsSpan(bytes.Length - section.Length)));
    }

    [Fact]
    public void InlineExportImportAndAliasAbbreviations()
    {
        AssertEncoding("""
            (component
              (import "i" (instance $i (export "c" (component))))
              (component $c (alias export $i "c"))
              (instance (export "j") (export "k") (import "x"))
              (type (export "u") u8)
              (core module (export "m")))
            """, Preamble
            + "07 0C 01 42 02 01 41 00 04 00 01 63 04 00"
            + "0A 06 01 00 01 69 05 00"
            + "06 06 01 04 00 00 01 63"
            + "07 03 01 42 00"
            + "0A 06 01 00 01 78 05 01"
            + "07 02 01 7D"
            + "01 08 " + EmptyModule
            + "0B 1A 04 00 01 6A 05 01 00 00 01 6B 05 01 00 00 01 75 03 02 00 00 01 6D 00 11 00 00");
    }

    [Fact]
    public void InlineExportsDontChangeTheIndices()
    {
        // The second module is the core module 1: the inline export is defined after the last definition
        AssertEncoding("""
            (component
              (core module (export "a"))
              (core module)
              (export "b" (core module 1)))
            """, Preamble
            + "01 08 " + EmptyModule
            + "01 08 " + EmptyModule
            + "0B 0F 02 00 01 62 00 11 01 00 00 01 61 00 11 00 00");
    }

    [Fact]
    public void ChainedInlineExportAliases()
    {
        AssertEncoding("""
            (component
              (import "j" (instance $j (export "a" (instance (export "f" (func))))))
              (export "f" (func $j "a" "f")))
            """, Preamble
            + "07 17 01 42 02 01 42 02 01 40 00 01 00 04 00 01 66 01 00 04 00 01 61 05 00"
            + "0A 06 01 00 01 6A 05 00"
            + "06 0B 02 05 00 00 01 61 01 00 01 01 66"
            + "0B 07 01 00 01 66 01 00 00");
    }

    [Fact]
    public void NameAttributes()
    {
        AssertEncoding("""
            (component
              (import "a" (implements "a:b/c") (version-suffix ".1") (external-id "x") (instance)))
            """, Preamble
            + "07 03 01 42 00"
            + "0A 15 01 02 01 61 03 00 05 61 3A 62 2F 63 01 02 2E 31 02 01 78 05 00");
    }

    [Fact]
    public void StartAndValues()
    {
        AssertEncoding("""
            (component
              (import "v" (value $v u32))
              (value $w string "hi")
              (import "f" (func $f (param "a" u32) (result string)))
              (start $f (value $v) (result (value $r)))
              (export "r" (value $r)))
            """, Preamble
            + "0A 07 01 00 01 76 02 01 79"
            + "0C 06 01 73 03 02 68 69"
            + "07 08 01 40 01 01 61 79 00 73"
            + "0A 06 01 00 01 66 01 00"
            + "09 04 00 01 00 01"
            + "0B 07 01 00 01 72 02 02 00");
    }

    [Theory]
    [InlineData("""(component (import "x" (func $x)) (component (export "x" (func $x))))""", "outer item `x` is not a module, type, or component")]
    [InlineData("""(component (export "x" (func $unknown)))""", "unknown func $unknown")]
    [InlineData("""(component (import "a" (external-id "x") (external-id "y") (func)))""", "duplicate `external-id` attribute")]
    [InlineData("""(component (type $t u8) (type $t u8))""", "duplicate type identifier $t")]
    public void InvalidText(string text, string message)
    {
        var exception = Assert.Throws<FormatException>(() => WasmRuntime.WatToWasm(text));
        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ModuleIsStillAModule()
    {
        Assert.Equal(Convert.FromHexString(EmptyModule.Replace(" ", "", StringComparison.Ordinal)), WasmRuntime.WatToWasm("(module)"));
    }
}
