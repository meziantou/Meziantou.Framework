// Sucrase: fast transformation of TypeScript, JSX, Flow, imports and class fields
import sucrase from "sucrase";

const typescript = `import type { A } from "./a";
import React, { useState } from "react";
export enum Color { Red = "r", Green = "g" }
interface Props { title: string; count?: number }
export abstract class Base<T> implements Iface { protected abstract value: T; private readonly x = 1; constructor(public name: string) { } }
export const App = ({ title, count = 0 }: Props): JSX.Element => {
    const [state, setState] = useState<number>(count!);
    return <div className="app" onClick={() => setState(state + 1)}>{title as string}: {state}<>fragment</></div>;
};
export default function f(this: Window, a?: number): asserts a is number { }
let value = ("x" as unknown) satisfies unknown;
`;

const flow = `// @flow
import { type T } from "./t";
function f(a: number, b?: string): Array<?T> { return [a]; }
export type Opaque = {| x: number |};
`;

export default () => [
  sucrase.transform(typescript, { transforms: ["typescript", "jsx", "imports"], filePath: "App.tsx", production: true }).code,
  sucrase.transform(typescript, { transforms: ["typescript", "jsx"], jsxRuntime: "automatic", filePath: "App.tsx", disableESTransforms: true }).code,
  sucrase.transform(flow, { transforms: ["flow", "imports"] }).code,
  sucrase.transform("class A { x = 1; static y = 2; #z = 3; }\nconst a = b?.c ?? d;", { transforms: [] }).code,
  sucrase.getVersion(),
].join("\n----\n");
