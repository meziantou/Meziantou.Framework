namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class LatexHighlighterTests
{
    [Fact]
    public void Preamble()
    {
        AssertHighlighter("latex",
"""
\documentclass[11pt,a4paper]{article}
\usepackage[utf8]{inputenc}
\usepackage{amsmath, amssymb}
\title{A \emph{Sample} Document}
\author{John Doe \and Jane Roe}
\date{\today}
""",
"""
<span class="hljs-keyword">\documentclass</span>[11pt,a4paper]{article}
<span class="hljs-keyword">\usepackage</span>[utf8]{inputenc}
<span class="hljs-keyword">\usepackage</span>{amsmath, amssymb}
<span class="hljs-keyword">\title</span>{A <span class="hljs-keyword">\emph</span>{Sample} Document}
<span class="hljs-keyword">\author</span>{John Doe <span class="hljs-keyword">\and</span> Jane Roe}
<span class="hljs-keyword">\date</span>{<span class="hljs-keyword">\today</span>}
""");
    }

    [Fact]
    public void Document()
    {
        AssertHighlighter("latex",
"""
\begin{document}
\maketitle
\section{Introduction}\label{sec:intro}
Some text with \textbf{bold} and \textit{italic}, see Section~\ref{sec:intro} and \cite{knuth84}.
\subsection*{Details}
\begin{itemize}
  \item First
  \item[-] Second
\end{itemize}
\end{document}
""",
"""
<span class="hljs-keyword">\begin</span>{document}
<span class="hljs-keyword">\maketitle</span>
<span class="hljs-keyword">\section</span>{Introduction}<span class="hljs-keyword">\label</span>{sec:intro}
Some text with <span class="hljs-keyword">\textbf</span>{bold} and <span class="hljs-keyword">\textit</span>{italic}, see Section~<span class="hljs-keyword">\ref</span>{sec:intro} and <span class="hljs-keyword">\cite</span>{knuth84}.
<span class="hljs-keyword">\subsection</span>*{Details}
<span class="hljs-keyword">\begin</span>{itemize}
  <span class="hljs-keyword">\item</span> First
  <span class="hljs-keyword">\item</span>[-] Second
<span class="hljs-keyword">\end</span>{itemize}
<span class="hljs-keyword">\end</span>{document}
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("latex",
"""
% A comment
text % trailing comment with \command and $math$
%%% several percent signs
% TODO: fix this
% FIXME: and this
\% is not a comment
""",
"""
<span class="hljs-comment">% A comment</span>
text <span class="hljs-comment">% trailing comment with \command and $math$</span>
<span class="hljs-comment">%%% several percent signs</span>
<span class="hljs-comment">% <span class="hljs-doctag">TODO:</span> fix this</span>
<span class="hljs-comment">% <span class="hljs-doctag">FIXME:</span> and this</span>
<span class="hljs-keyword">\%</span> is not a comment
""");
    }

    [Fact]
    public void MagicComments()
    {
        AssertHighlighter("latex",
"""
%!TEX program = xelatex
% !TeX root = main.tex
%!tex encoding = UTF-8
% !BIB TS-program = biber
%!bib program = bibtex
% ! TeX needs the bang right after the optional space
%  !TeX needs the bang right after the optional space
%! TEX is not a magic comment either
""",
"""
<span class="hljs-meta">%!TEX program = xelatex</span>
<span class="hljs-meta">% !TeX root = main.tex</span>
<span class="hljs-meta">%!tex encoding = UTF-8</span>
<span class="hljs-meta">% !BIB TS-program = biber</span>
<span class="hljs-meta">%!bib program = bibtex</span>
<span class="hljs-comment">% ! TeX needs the bang right after the optional space</span>
<span class="hljs-comment">%  !TeX needs the bang right after the optional space</span>
<span class="hljs-comment">%! TEX is not a magic comment either</span>
""");
    }

    // Every control word is a keyword; highlight.js's list of well-known words only raises the relevance.
    [Fact]
    public void ControlWords()
    {
        AssertHighlighter("latex",
"""
\relax \foo \FooBar \@foo \makeat@letter@x
\alpha\beta\Gamma\varrho\rho
\frac{a}{b} \left( x \right) \bigg( \Big)
""",
"""
<span class="hljs-keyword">\relax</span> <span class="hljs-keyword">\foo</span> <span class="hljs-keyword">\FooBar</span> <span class="hljs-keyword">\@foo</span> <span class="hljs-keyword">\makeat@letter@x</span>
<span class="hljs-keyword">\alpha</span><span class="hljs-keyword">\beta</span><span class="hljs-keyword">\Gamma</span><span class="hljs-keyword">\varrho</span><span class="hljs-keyword">\rho</span>
<span class="hljs-keyword">\frac</span>{a}{b} <span class="hljs-keyword">\left</span>( x <span class="hljs-keyword">\right</span>) <span class="hljs-keyword">\bigg</span>( <span class="hljs-keyword">\Big</span>)
""");
    }

    [Fact]
    public void ControlSymbols()
    {
        AssertHighlighter("latex",
"""
Costs 5\$ \& 10\% off.\\
New line \\[2pt] after.
Thin\,space and \ control space, \{braces\} \# \_ \^ \~ \" \' \`
\[ x \]
""",
"""
Costs 5<span class="hljs-keyword">\$</span> <span class="hljs-keyword">\&amp;</span> 10<span class="hljs-keyword">\%</span> off.<span class="hljs-keyword">\\</span>
New line <span class="hljs-keyword">\\</span>[2pt] after.
Thin<span class="hljs-keyword">\,</span>space and <span class="hljs-keyword">\ </span>control space, <span class="hljs-keyword">\{</span>braces<span class="hljs-keyword">\}</span> <span class="hljs-keyword">\#</span> <span class="hljs-keyword">\_</span> <span class="hljs-keyword">\^</span> <span class="hljs-keyword">\~</span> <span class="hljs-keyword">\&quot;</span> <span class="hljs-keyword">\&#x27;</span> <span class="hljs-keyword">\`</span>
<span class="hljs-keyword">\[</span> x <span class="hljs-keyword">\]</span>
""");
    }

    [Fact]
    public void SpecialCharacters()
    {
        AssertHighlighter("latex",
"""
Inline $a^2 + b_i^2 = c^2$ and $$x$$.
a & b & c \\
x^{n} y_{i,j}
""",
"""
Inline <span class="hljs-built_in">$</span>a<span class="hljs-built_in">^</span>2 + b<span class="hljs-built_in">_</span>i<span class="hljs-built_in">^</span>2 = c<span class="hljs-built_in">^</span>2<span class="hljs-built_in">$</span> and <span class="hljs-built_in">$</span><span class="hljs-built_in">$</span>x<span class="hljs-built_in">$</span><span class="hljs-built_in">$</span>.
a <span class="hljs-built_in">&amp;</span> b <span class="hljs-built_in">&amp;</span> c <span class="hljs-keyword">\\</span>
x<span class="hljs-built_in">^</span>{n} y<span class="hljs-built_in">_</span>{i,j}
""");
    }

    [Fact]
    public void MathDisplay()
    {
        AssertHighlighter("latex",
"""
\begin{equation}
  \sum_{i=1}^{n} i = \frac{n(n+1)}{2} \quad \text{and} \quad \int_0^\infty e^{-x}\,dx = 1
\end{equation}
""",
"""
<span class="hljs-keyword">\begin</span>{equation}
  <span class="hljs-keyword">\sum</span><span class="hljs-built_in">_</span>{i=1}<span class="hljs-built_in">^</span>{n} i = <span class="hljs-keyword">\frac</span>{n(n+1)}{2} <span class="hljs-keyword">\quad</span> <span class="hljs-keyword">\text</span>{and} <span class="hljs-keyword">\quad</span> <span class="hljs-keyword">\int</span><span class="hljs-built_in">_</span>0<span class="hljs-built_in">^</span><span class="hljs-keyword">\infty</span> e<span class="hljs-built_in">^</span>{-x}<span class="hljs-keyword">\,</span>dx = 1
<span class="hljs-keyword">\end</span>{equation}
""");
    }

    [Fact]
    public void MacroParameters()
    {
        AssertHighlighter("latex",
"""
\newcommand{\pair}[2]{(#1, #2)}
\def\swap#1#2{#2#1}
\def\outer#1{\def\inner##1{#1##1}}
### #
""",
"""
<span class="hljs-keyword">\newcommand</span>{<span class="hljs-keyword">\pair</span>}[2]{(<span class="hljs-params">#1</span>, <span class="hljs-params">#2</span>)}
<span class="hljs-keyword">\def</span><span class="hljs-keyword">\swap</span><span class="hljs-params">#1</span><span class="hljs-params">#2</span>{<span class="hljs-params">#2</span><span class="hljs-params">#1</span>}
<span class="hljs-keyword">\def</span><span class="hljs-keyword">\outer</span><span class="hljs-params">#1</span>{<span class="hljs-keyword">\def</span><span class="hljs-keyword">\inner</span><span class="hljs-params">##1</span>{<span class="hljs-params">#1</span><span class="hljs-params">##1</span>}}
<span class="hljs-params">###</span> <span class="hljs-params">#</span>
""");
    }

    [Fact]
    public void MacroDefinitions()
    {
        AssertHighlighter("latex",
"""
\newcommand{\R}{\mathbb{R}}
\renewcommand*{\vec}[1]{\mathbf{#1}}
\newcommand\foo[2][default]{#1 and #2}
\let\oldsection\section
\makeatletter
\def\@foo{\@bar}
\makeatother
\NewDocumentCommand{\baz}{m o}{#1}
\ProvidesPackage{mypkg}[2020/01/01 v1.0]
\ProcessOptions\relax
""",
"""
<span class="hljs-keyword">\newcommand</span>{<span class="hljs-keyword">\R</span>}{<span class="hljs-keyword">\mathbb</span>{R}}
<span class="hljs-keyword">\renewcommand</span>*{<span class="hljs-keyword">\vec</span>}[1]{<span class="hljs-keyword">\mathbf</span>{<span class="hljs-params">#1</span>}}
<span class="hljs-keyword">\newcommand</span><span class="hljs-keyword">\foo</span>[2][default]{<span class="hljs-params">#1</span> and <span class="hljs-params">#2</span>}
<span class="hljs-keyword">\let</span><span class="hljs-keyword">\oldsection</span><span class="hljs-keyword">\section</span>
<span class="hljs-keyword">\makeatletter</span>
<span class="hljs-keyword">\def</span><span class="hljs-keyword">\@foo</span>{<span class="hljs-keyword">\@bar</span>}
<span class="hljs-keyword">\makeatother</span>
<span class="hljs-keyword">\NewDocumentCommand</span>{<span class="hljs-keyword">\baz</span>}{m o}{<span class="hljs-params">#1</span>}
<span class="hljs-keyword">\ProvidesPackage</span>{mypkg}[2020/01/01 v1.0]
<span class="hljs-keyword">\ProcessOptions</span><span class="hljs-keyword">\relax</span>
""");
    }

    // Outside a control sequence, ^^ notation is consumed without a scope, so its carets are not special characters.
    [Fact]
    public void DoubleCaretNotation()
    {
        AssertHighlighter("latex",
"""
^^41 ^^^^0041 ^^^^^^01f600 ^^M ^^?
^^zz ^^^0ab ^^^^^0abcd ^^4G
\^^41 \^^M \^^^^0041
""",
"""
^^41 ^^^^0041 ^^^^^^01f600 ^^M ^^?
^^zz ^^^0ab ^^^^^0abcd ^^4G
<span class="hljs-keyword">\^^41</span> <span class="hljs-keyword">\^^M</span> <span class="hljs-keyword">\^^^^0041</span>
""");
    }

    [Fact]
    public void Expl3()
    {
        AssertHighlighter("latex",
"""
\ExplSyntaxOn
\cs_new:Npn \my_function:nn #1#2 { #1 #2 }
\tl_new:N \l_my_tl
\tl_set:Nn \l_my_tl { hello }
\int_gset:Nn \g__my_private_int { 42 }
\bool_if:NTF \c_true_bool { yes } { no }
\quark_new:N \q_my_mark
\s__my_stop \__my_internal:n {x}
\ExplSyntaxOff
""",
"""
<span class="hljs-keyword">\ExplSyntaxOn</span>
<span class="hljs-keyword">\cs_new:Npn</span> <span class="hljs-keyword">\my_function:nn</span> <span class="hljs-params">#1</span><span class="hljs-params">#2</span> { <span class="hljs-params">#1</span> <span class="hljs-params">#2</span> }
<span class="hljs-keyword">\tl_new:N</span> <span class="hljs-keyword">\l_my_tl</span>
<span class="hljs-keyword">\tl_set:Nn</span> <span class="hljs-keyword">\l_my_tl</span> { hello }
<span class="hljs-keyword">\int_gset:Nn</span> <span class="hljs-keyword">\g__my_private_int</span> { 42 }
<span class="hljs-keyword">\bool_if:NTF</span> <span class="hljs-keyword">\c_true_bool</span> { yes } { no }
<span class="hljs-keyword">\quark_new:N</span> <span class="hljs-keyword">\q_my_mark</span>
<span class="hljs-keyword">\s__my_stop</span> <span class="hljs-keyword">\__my_internal:n</span> {x}
<span class="hljs-keyword">\ExplSyntaxOff</span>
""");
    }

    [Fact]
    public void Expl3SpecialNames()
    {
        AssertHighlighter("latex",
"""
\use:c \use_i:nn \use:nn
\else: \fi: \or: \if:w \cs:w \exp:w \hbox:n \vbox:n
\exp_last_unbraced:Nf \::N \::n \::: \::o_unbraced
\cs_new:Npn_ \l_a
""",
"""
<span class="hljs-keyword">\use:c</span> <span class="hljs-keyword">\use_i:nn</span> <span class="hljs-keyword">\use:nn</span>
<span class="hljs-keyword">\else:</span> <span class="hljs-keyword">\fi:</span> <span class="hljs-keyword">\or:</span> <span class="hljs-keyword">\if:w</span> <span class="hljs-keyword">\cs:w</span> <span class="hljs-keyword">\exp:w</span> <span class="hljs-keyword">\hbox:n</span> <span class="hljs-keyword">\vbox:n</span>
<span class="hljs-keyword">\exp_last_unbraced:Nf</span> <span class="hljs-keyword">\::N</span> <span class="hljs-keyword">\::n</span> <span class="hljs-keyword">\:::</span> <span class="hljs-keyword">\::o_unbraced</span>
<span class="hljs-keyword">\cs</span><span class="hljs-built_in">_</span>new:Npn<span class="hljs-built_in">_</span> <span class="hljs-keyword">\l</span><span class="hljs-built_in">_</span>a
""");
    }

    [Fact]
    public void BraceGroups()
    {
        AssertHighlighter("latex",
"""
\foo{a {b {c} d} e}[opt]{\bar{x}}
{\bf group % comment
}
{$x^2$ #1}
""",
"""
<span class="hljs-keyword">\foo</span>{a {b {c} d} e}[opt]{<span class="hljs-keyword">\bar</span>{x}}
{<span class="hljs-keyword">\bf</span> group <span class="hljs-comment">% comment</span>
}
{<span class="hljs-built_in">$</span>x<span class="hljs-built_in">^</span>2<span class="hljs-built_in">$</span> <span class="hljs-params">#1</span>}
""");
    }

    [Fact]
    public void Verb()
    {
        AssertHighlighter("latex",
"""
Use \verb|\textbf{x}| or \verb+a%b+ or \verb!$x$!.
\verb |spaced| and \verb
|on the next line|
\verba is a control word
""",
"""
Use <span class="hljs-keyword">\verb</span>|<span class="hljs-string">\textbf{x}</span>| or <span class="hljs-keyword">\verb</span>+<span class="hljs-string">a%b</span>+ or <span class="hljs-keyword">\verb</span>!<span class="hljs-string">$x$</span>!.
<span class="hljs-keyword">\verb</span> |<span class="hljs-string">spaced</span>| and <span class="hljs-keyword">\verb</span>
|<span class="hljs-string">on the next line</span>|
<span class="hljs-keyword">\verba</span> is a control word
""");
    }

    // Deviation from highlight.js, which takes the star for the delimiter and highlights the rest as a string.
    [Fact]
    public void VerbStar()
    {
        AssertHighlighter("latex",
"""
\verb*|a b| and \verb*a*b* \verb*!x!
""",
"""
<span class="hljs-keyword">\verb</span>*|<span class="hljs-string">a b</span>| and <span class="hljs-keyword">\verb</span>*a<span class="hljs-string">*b* \verb*!x!</span>
""");
    }

    [Fact]
    public void VerbInsideBraces()
    {
        AssertHighlighter("latex",
"""
\textbf{\verb|x|} \footnote{\verb+y+}
""",
"""
<span class="hljs-keyword">\textbf</span>{<span class="hljs-keyword">\verb</span>|<span class="hljs-string">x</span>|} <span class="hljs-keyword">\footnote</span>{<span class="hljs-keyword">\verb</span>+<span class="hljs-string">y</span>+}
""");
    }

    // Deviation from highlight.js, which does not accept the optional argument nor braces, and takes `[` or `{` for the
    // delimiter.
    [Fact]
    public void Lstinline()
    {
        AssertHighlighter("latex",
"""
\lstinline|int x = 1;| \lstinline!y! \lstinline {z}
\lstinline{int x;} \lstinline[language=C]{a{b}c} \lstinline[style=x]|y|
""",
"""
<span class="hljs-keyword">\lstinline</span>|<span class="hljs-string">int x = 1;</span>| <span class="hljs-keyword">\lstinline</span>!<span class="hljs-string">y</span>! <span class="hljs-keyword">\lstinline</span> {<span class="hljs-string">z</span>}
<span class="hljs-keyword">\lstinline</span>{<span class="hljs-string">int x;</span>} <span class="hljs-keyword">\lstinline</span>[language=C]{<span class="hljs-string">a{b}c</span>} <span class="hljs-keyword">\lstinline</span>[style=x]|<span class="hljs-string">y</span>|
""");
    }

    // As in highlight.js, spaces are not skipped before the delimiter: the space after the language is the delimiter.
    [Fact]
    public void Mint()
    {
        AssertHighlighter("latex",
"""
\mint{python}|print("hi")|
\mint {python} |x = 1|
\mint{python}/y = 2/
""",
"""
<span class="hljs-keyword">\mint</span>{python}|<span class="hljs-string">print(&quot;hi&quot;)</span>|
<span class="hljs-keyword">\mint</span> {python} <span class="hljs-string">|x</span> = 1|
<span class="hljs-keyword">\mint</span>{python}/<span class="hljs-string">y = 2</span>/
""");
    }

    [Fact]
    public void Mintinline()
    {
        AssertHighlighter("latex",
"""
\mintinline{c}{int main() { return 0; }}
\mintinline{js}|let a = {}|
\mintinline{py}{a{b}c} rest
""",
"""
<span class="hljs-keyword">\mintinline</span>{c}{<span class="hljs-string">int main() { return 0; }</span>}
<span class="hljs-keyword">\mintinline</span>{js}|<span class="hljs-string">let a = {}</span>|
<span class="hljs-keyword">\mintinline</span>{py}{<span class="hljs-string">a{b}c</span>} rest
""");
    }

    [Fact]
    public void VerbatimEnvironment()
    {
        AssertHighlighter("latex",
"""
\begin{verbatim}
  \section{not a command} % not a comment
  $x$ #1 ^^41
\end{verbatim}
\begin{verbatim*}
a b  c
\end{verbatim*}
""",
"""
<span class="hljs-keyword">\begin</span>{verbatim}<span class="hljs-string">
  \section{not a command} % not a comment
  $x$ #1 ^^41
</span><span class="hljs-keyword">\end</span>{verbatim}
<span class="hljs-keyword">\begin</span>{verbatim*}<span class="hljs-string">
a b  c
</span><span class="hljs-keyword">\end</span>{verbatim*}
""");
    }

    [Fact]
    public void VerbatimBeginWithSpaces()
    {
        AssertHighlighter("latex",
"""
\begin {verbatim}
spaced
\end{verbatim}
\begin
  {verbatim}
next line
\end{verbatim}
""",
"""
<span class="hljs-keyword">\begin</span> {verbatim}<span class="hljs-string">
spaced
</span><span class="hljs-keyword">\end</span>{verbatim}
<span class="hljs-keyword">\begin</span>
  {verbatim}<span class="hljs-string">
next line
</span><span class="hljs-keyword">\end</span>{verbatim}
""");
    }

    [Fact]
    public void FancyVerbatim()
    {
        AssertHighlighter("latex",
"""
\begin{Verbatim}[fontsize=\small]
x = 1
\end{Verbatim}
\begin{BVerbatim}
boxed
\end{BVerbatim}
\begin{LVerbatim*}[numbers=left]
listing
\end{LVerbatim*}
""",
"""
<span class="hljs-keyword">\begin</span>{Verbatim}[fontsize=<span class="hljs-keyword">\small</span>]<span class="hljs-string">
x = 1
</span><span class="hljs-keyword">\end</span>{Verbatim}
<span class="hljs-keyword">\begin</span>{BVerbatim}
<span class="hljs-string">boxed
</span><span class="hljs-keyword">\end</span>{BVerbatim}
<span class="hljs-keyword">\begin</span>{LVerbatim*}[numbers=left]<span class="hljs-string">
listing
</span><span class="hljs-keyword">\end</span>{LVerbatim*}
""");
    }

    [Fact]
    public void FileContents()
    {
        AssertHighlighter("latex",
"""
\begin{filecontents}{data.txt}
1,2,3
\end{filecontents}
\begin{filecontents*}{x.tex}
\foo
\end{filecontents*}
""",
"""
<span class="hljs-keyword">\begin</span>{filecontents}{data.txt}<span class="hljs-string">
1,2,3
</span><span class="hljs-keyword">\end</span>{filecontents}
<span class="hljs-keyword">\begin</span>{filecontents*}{x.tex}<span class="hljs-string">
\foo
</span><span class="hljs-keyword">\end</span>{filecontents*}
""");
    }

    [Fact]
    public void MintedEnvironment()
    {
        AssertHighlighter("latex",
"""
\begin{minted}{python}
def f(x):
    return x  # \end{nope}
\end{minted}
\begin{minted}[linenos, frame=lines]{cpp}
int main() { return 0; }
\end{minted}
""",
"""
<span class="hljs-keyword">\begin</span>{minted}{python}<span class="hljs-string">
def f(x):
    return x  # \end{nope}
</span><span class="hljs-keyword">\end</span>{minted}
<span class="hljs-keyword">\begin</span>{minted}[linenos, frame=lines]{cpp}<span class="hljs-string">
int main() { return 0; }
</span><span class="hljs-keyword">\end</span>{minted}
""");
    }

    [Fact]
    public void OtherEnvironments()
    {
        AssertHighlighter("latex",
"""
\begin{figure}[ht]
\centering
\includegraphics[width=0.5\textwidth]{img.png}
\caption{A figure}
\end{figure}
""",
"""
<span class="hljs-keyword">\begin</span>{figure}[ht]
<span class="hljs-keyword">\centering</span>
<span class="hljs-keyword">\includegraphics</span>[width=0.5<span class="hljs-keyword">\textwidth</span>]{img.png}
<span class="hljs-keyword">\caption</span>{A figure}
<span class="hljs-keyword">\end</span>{figure}
""");
    }

    [Fact]
    public void Urls()
    {
        AssertHighlighter("latex",
"""
\url{https://example.com/a_b%20c#frag}
\url {http://x.org/{nested}/y}
\url{a}{b}
""",
"""
<span class="hljs-keyword">\url</span>{<span class="hljs-link">https://example.com/a_b%20c#frag</span>}
<span class="hljs-keyword">\url</span> {<span class="hljs-link">http://x.org/{nested}/y</span>}
<span class="hljs-keyword">\url</span>{<span class="hljs-link">a</span>}{b}
""");
    }

    // As in highlight.js, spaces are not skipped before the link after an optional argument.
    [Fact]
    public void Href()
    {
        AssertHighlighter("latex",
"""
\href{https://example.com}{Example \textbf{site}}
\href[pdfnewwindow]{mailto:a@b.c}{mail}
\href [x] {y}
""",
"""
<span class="hljs-keyword">\href</span>{<span class="hljs-link">https://example.com</span>}{Example <span class="hljs-keyword">\textbf</span>{site}}
<span class="hljs-keyword">\href</span>[pdfnewwindow]{<span class="hljs-link">mailto:a@b.c</span>}{mail}
<span class="hljs-keyword">\href</span> [x] {y}
""");
    }

    [Fact]
    public void Hyperref()
    {
        AssertHighlighter("latex",
"""
\hyperref[sec:intro]{the intro}
\hyperref{file.pdf}{category}{name}{text}
""",
"""
<span class="hljs-keyword">\hyperref</span>[sec:intro]{the intro}
<span class="hljs-keyword">\hyperref</span>{<span class="hljs-link">file.pdf</span>}{category}{name}{text}
""");
    }

    // Deviation from highlight.js, which highlights everything up to the next closing brace after empty braces.
    [Fact]
    public void EmptyBraces()
    {
        AssertHighlighter("latex",
"""
\url{} next} \href{}{text} \mintinline{c}{} x} \url{{}} y}
""",
"""
<span class="hljs-keyword">\url</span>{} next} <span class="hljs-keyword">\href</span>{}{text} <span class="hljs-keyword">\mintinline</span>{c}{} x} <span class="hljs-keyword">\url</span>{<span class="hljs-link">{}</span>} y}
""");
    }

    [Fact]
    public void UnterminatedVerb()
    {
        AssertHighlighter("latex",
"""
\verb|unterminated
more text
""",
"""
<span class="hljs-keyword">\verb</span>|<span class="hljs-string">unterminated
more text</span>
""");
    }

    [Fact]
    public void UnterminatedVerbatim()
    {
        AssertHighlighter("latex",
"""
\begin{verbatim}
unterminated verbatim
\section{x}
""",
"""
<span class="hljs-keyword">\begin</span>{verbatim}<span class="hljs-string">
unterminated verbatim
\section{x}</span>
""");
    }

    [Fact]
    public void UnterminatedUrl()
    {
        AssertHighlighter("latex",
"""
\url{unterminated
\foo
""",
"""
<span class="hljs-keyword">\url</span>{<span class="hljs-link">unterminated
\foo</span>
""");
    }

    [Fact]
    public void VerbAtEndOfInput()
    {
        AssertHighlighter("latex",
"""
text \verb
""",
"""
text <span class="hljs-keyword">\verb</span>
""");
    }

    [Fact]
    public void BackslashAtEndOfInput()
    {
        AssertHighlighter("latex",
"""
a \
""",
"""
a <span class="hljs-keyword">\</span>
""");
    }

    [Fact]
    public void BackslashBeforeNewLine()
    {
        AssertHighlighter("latex",
"""
a \
b
""",
"""
a <span class="hljs-keyword">\
</span>b
""");
    }

    [Fact]
    public void EmptyVerbatim()
    {
        AssertHighlighter("latex",
"""
\begin{verbatim}\end{verbatim}
""",
"""
<span class="hljs-keyword">\begin</span>{verbatim}<span class="hljs-string"></span><span class="hljs-keyword">\end</span>{verbatim}
""");
    }

    // A control word is made of ASCII letters.
    [Fact]
    public void NonAsciiControlWords()
    {
        AssertHighlighter("latex",
"""
\café \été{x}
""",
"""
<span class="hljs-keyword">\caf</span>é <span class="hljs-keyword">\é</span>té{x}
""");
    }

    [Fact]
    public void TexAlias()
    {
        AssertHighlighter("tex",
"""
\input{macros} % load
\bye
""",
"""
<span class="hljs-keyword">\input</span>{macros} <span class="hljs-comment">% load</span>
<span class="hljs-keyword">\bye</span>
""");
    }
}
