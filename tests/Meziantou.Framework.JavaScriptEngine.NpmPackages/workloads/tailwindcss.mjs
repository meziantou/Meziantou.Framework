// Tailwind CSS v4: the compiler of the theme and of the utilities that the candidates (class names) use
import { compile } from "tailwindcss";

const css = `
@theme {
    --spacing: 0.25rem;
    --color-red-500: oklch(63.7% 0.237 25.331);
    --color-blue-600: oklch(54.6% 0.245 262.881);
    --color-white: #fff;
    --breakpoint-md: 48rem;
    --breakpoint-lg: 64rem;
    --text-sm: 0.875rem;
    --text-sm--line-height: calc(1.25 / 0.875);
    --text-xl: 1.25rem;
    --radius-lg: 0.5rem;
    --font-weight-bold: 700;
    --shadow-md: 0 4px 6px -1px rgb(0 0 0 / 0.1);
}
@custom-variant dark (&:where(.dark, .dark *));
@utility content-auto { content-visibility: auto; }
.btn { @apply px-4 py-2 rounded-lg font-bold; }
@tailwind utilities;
`;

export default async () => {
  const compiler = await compile(css);
  return compiler.build([
    "flex", "grid", "grid-cols-3", "gap-4", "p-4", "px-[13px]", "mt-auto", "-mx-2", "w-1/2", "h-screen",
    "text-red-500", "bg-blue-600/50", "text-sm", "md:text-xl", "lg:grid-cols-4", "hover:bg-red-500", "dark:text-white",
    "focus-visible:outline-2", "group-hover:underline", "rounded-lg", "shadow-md", "translate-x-2", "rotate-45",
    "content-auto", "[mask-type:luminance]", "not-a-utility", "first:pt-0", "aria-checked:font-bold", "@container",
  ]);
};
