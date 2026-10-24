// The TypeScript 6 compiler (9 MB of JavaScript): transpilation, and a program with an in-memory compiler host that
// type checks, reports diagnostics and emits declarations
import ts from "typescript";

const files = {
  "/main.ts": `import { Shape, area } from "./shapes";
export class Registry<T extends Shape> {
    #items = new Map<string, T>();
    add(name: string, item: T): this { this.#items.set(name, item); return this; }
    get total(): number { return [...this.#items.values()].reduce((sum, item) => sum + area(item), 0); }
}
const registry = new Registry().add("c", { kind: "circle", radius: 1 }).add("s", { kind: "square", size: 2 });
export const total: number = registry.total;
const wrong: string = 42;
export enum Direction { Up = 1, Down, Left = "L" }
export type Readonly2<T> = { readonly [K in keyof T]: T[K] };
declare module "./shapes" { interface Circle { color?: string } }
`,
  "/shapes.ts": `export interface Circle { kind: "circle"; radius: number }
export interface Square { kind: "square"; size: number }
export type Shape = Circle | Square;
export function area(shape: Shape): number {
    switch (shape.kind) {
        case "circle": return Math.PI * shape.radius ** 2;
        case "square": return shape.size ** 2;
    }
}
export const unused = (value?: string) => value?.trim() ?? "";
`,
  "/lib.d.ts": `interface Array<T> { length: number; reduce<U>(f: (p: U, c: T) => U, i: U): U; [n: number]: T; [Symbol.iterator](): Iterator<T>; }
interface ReadonlyArray<T> { length: number; [n: number]: T; }
interface Boolean {} interface Function {} interface CallableFunction {} interface NewableFunction {} interface IArguments {}
interface Number {} interface Object {} interface RegExp {} interface String { trim(): string; }
interface Symbol {} interface SymbolConstructor { readonly iterator: unique symbol; } declare var Symbol: SymbolConstructor;
interface Iterator<T> { next(): { done?: boolean; value: T }; } interface IterableIterator<T> extends Iterator<T> { [Symbol.iterator](): IterableIterator<T>; }
interface Map<K, V> { set(k: K, v: V): this; values(): IterableIterator<V>; } interface MapConstructor { new <K, V>(): Map<K, V>; } declare var Map: MapConstructor;
interface Math { PI: number; } declare var Math: Math;
interface PromiseLike<T> {} interface Promise<T> {} interface TemplateStringsArray {}
type Partial<T> = { [P in keyof T]?: T[P] };
`,
};

export default () => {
  const results = [];
  const transpiled = ts.transpileModule(files["/main.ts"], { compilerOptions: { target: ts.ScriptTarget.ES2017, module: ts.ModuleKind.CommonJS, sourceMap: true }, fileName: "main.ts", reportDiagnostics: true });
  results.push(transpiled.outputText, transpiled.sourceMapText);

  const output = {};
  const options = { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext, moduleResolution: ts.ModuleResolutionKind.Bundler, declaration: true, strict: true, noLib: true, types: [], noEmitOnError: false, newLine: ts.NewLineKind.LineFeed };
  const host = {
    getSourceFile: (fileName, languageVersion) => files[fileName] === undefined ? undefined : ts.createSourceFile(fileName, files[fileName], languageVersion, true),
    writeFile: (fileName, text) => { output[fileName] = text; },
    getDefaultLibFileName: () => "/lib.d.ts",
    useCaseSensitiveFileNames: () => true,
    getCanonicalFileName: fileName => fileName,
    getCurrentDirectory: () => "/",
    getNewLine: () => "\n",
    fileExists: fileName => files[fileName] !== undefined,
    readFile: fileName => files[fileName],
    directoryExists: () => true,
    getDirectories: () => [],
  };
  const program = ts.createProgram(["/main.ts", "/lib.d.ts"], options, host);
  const diagnostics = ts.getPreEmitDiagnostics(program);
  results.push(diagnostics.map(diagnostic => (diagnostic.file?.fileName ?? "") + ":" + (diagnostic.start === undefined ? "" : diagnostic.file.getLineAndCharacterOfPosition(diagnostic.start).line + 1) + " TS" + diagnostic.code + " " + ts.flattenDiagnosticMessageText(diagnostic.messageText, "\n")).join("\n"));
  program.emit();
  for (const name of Object.keys(output).sort())
    results.push(name + "\n" + output[name]);

  const checker = program.getTypeChecker();
  const source = program.getSourceFile("/shapes.ts");
  const symbols = checker.getExportsOfModule(checker.getSymbolAtLocation(source));
  results.push(symbols.map(symbol => symbol.name + ": " + checker.typeToString(checker.getTypeOfSymbolAtLocation(symbol, source))).join("\n"));
  const printer = ts.createPrinter();
  const node = ts.factory.createVariableStatement([ts.factory.createModifier(ts.SyntaxKind.ExportKeyword)], ts.factory.createVariableDeclarationList([ts.factory.createVariableDeclaration("answer", undefined, ts.factory.createKeywordTypeNode(ts.SyntaxKind.NumberKeyword), ts.factory.createNumericLiteral(42))], ts.NodeFlags.Const));
  results.push(printer.printNode(ts.EmitHint.Unspecified, node, ts.createSourceFile("x.ts", "", ts.ScriptTarget.Latest)));
  return results.join("\n----\n");
};
