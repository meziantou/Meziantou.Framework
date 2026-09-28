namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class VimHighlighterTests
{
    [Fact]
    public void Vimrc()
    {
        AssertHighlighter("vim",
"""
" General settings
set nocompatible
set number relativenumber
set tabstop=4 shiftwidth=4 expandtab
syntax on
filetype plugin indent on

let mapleader = ","
let g:netrw_banner = 0

nnoremap <leader>w :w<CR>
inoremap jk <Esc>
nnoremap <C-h> <C-w>h
""",
"""
<span class="hljs-comment">&quot; General settings</span>
<span class="hljs-keyword">set</span> nocompatible
<span class="hljs-keyword">set</span> <span class="hljs-keyword">number</span> relativenumber
<span class="hljs-keyword">set</span> tabstop=<span class="hljs-number">4</span> <span class="hljs-built_in">shiftwidth</span>=<span class="hljs-number">4</span> expandtab
<span class="hljs-keyword">syntax</span> <span class="hljs-keyword">on</span>
<span class="hljs-keyword">filetype</span> plugin <span class="hljs-built_in">indent</span> <span class="hljs-keyword">on</span>

<span class="hljs-keyword">let</span> mapleader = <span class="hljs-string">&quot;,&quot;</span>
<span class="hljs-keyword">let</span> <span class="hljs-variable">g:netrw_banner</span> = <span class="hljs-number">0</span>

<span class="hljs-keyword">nnoremap</span> <span class="hljs-symbol">&lt;leader&gt;</span><span class="hljs-keyword">w</span> :<span class="hljs-keyword">w</span><span class="hljs-symbol">&lt;CR&gt;</span>
<span class="hljs-keyword">inoremap</span> jk <span class="hljs-symbol">&lt;Esc&gt;</span>
<span class="hljs-keyword">nnoremap</span> <span class="hljs-symbol">&lt;C-h&gt;</span> <span class="hljs-symbol">&lt;C-w&gt;</span>h
""");
    }

    [Fact]
    public void Functions()
    {
        AssertHighlighter("vim",
"""
function! s:Trim(str) abort
  return substitute(a:str, '^\s*\(.\{-}\)\s*$', '\1', '')
endfunction

function MyFunc(a, b)
  let l:sum = a:a + a:b
  if l:sum > 10
    echo "big"
  elseif l:sum == 0
    echom 'zero'
  else
    echo "small: " . l:sum
  endif
  return l:sum
endfunction
""",
"""
<span class="hljs-keyword">function!</span> <span class="hljs-title">s</span>:Trim<span class="hljs-params">(str)</span> abort
  <span class="hljs-keyword">return</span> <span class="hljs-keyword">substitute</span>(<span class="hljs-variable">a:str</span>, <span class="hljs-string">&#x27;^\s*\(.\{-}\)\s*$&#x27;</span>, <span class="hljs-string">&#x27;\1&#x27;</span>, <span class="hljs-string">&#x27;&#x27;</span>)
<span class="hljs-keyword">endfunction</span>

<span class="hljs-keyword">function</span> <span class="hljs-title">MyFunc</span><span class="hljs-params">(a, b)</span>
  <span class="hljs-keyword">let</span> <span class="hljs-variable">l:sum</span> = <span class="hljs-variable">a:a</span> + <span class="hljs-variable">a:b</span>
  <span class="hljs-keyword">if</span> <span class="hljs-variable">l:sum</span> &gt; <span class="hljs-number">10</span>
    <span class="hljs-keyword">echo</span> <span class="hljs-string">&quot;big&quot;</span>
  <span class="hljs-keyword">elseif</span> <span class="hljs-variable">l:sum</span> == <span class="hljs-number">0</span>
    <span class="hljs-keyword">echom</span> <span class="hljs-string">&#x27;zero&#x27;</span>
  <span class="hljs-keyword">else</span>
    <span class="hljs-keyword">echo</span> <span class="hljs-string">&quot;small: &quot;</span> . <span class="hljs-variable">l:sum</span>
  <span class="hljs-keyword">endif</span>
  <span class="hljs-keyword">return</span> <span class="hljs-variable">l:sum</span>
<span class="hljs-keyword">endfunction</span>
""");
    }

    [Fact]
    public void Autocommands()
    {
        AssertHighlighter("vim",
"""
augroup MyGroup
  autocmd!
  autocmd BufWritePre *.py :%s/\s\+$//e
  autocmd FileType go setlocal noexpandtab
augroup END
""",
"""
<span class="hljs-keyword">augroup</span> MyGroup
  autocmd!
  <span class="hljs-keyword">autocmd</span> BufWritePre *.<span class="hljs-keyword">py</span> :%s/\s\+$//<span class="hljs-keyword">e</span>
  <span class="hljs-keyword">autocmd</span> FileType <span class="hljs-keyword">go</span> <span class="hljs-keyword">setlocal</span> noexpandtab
<span class="hljs-keyword">augroup</span> END
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("vim",
"""
" Map "jk" to escape
  " indented "comment" here
echo "hello" " trailing comment
let x = 1 " set x
""",
"""
<span class="hljs-comment">&quot; Map &quot;jk&quot; to escape</span>
  <span class="hljs-comment">&quot; indented &quot;comment&quot; here</span>
<span class="hljs-keyword">echo</span> <span class="hljs-string">&quot;hello&quot;</span> <span class="hljs-comment">&quot; trailing comment</span>
<span class="hljs-keyword">let</span> <span class="hljs-keyword">x</span> = <span class="hljs-number">1</span> <span class="hljs-comment">&quot; set x</span>
""");
    }

    [Fact]
    public void ControlFlow()
    {
        AssertHighlighter("vim",
"""
for item in [1, 2, 3]
  call add(list, item * 2)
endfor
let i = 0
while i < len(list)
  let i += 1
endwhile
try
  call Foo()
catch /E123/
  echoerr v:exception
finally
  echo "done"
endtry
""",
"""
<span class="hljs-keyword">for</span> item in [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>]
  <span class="hljs-keyword">call</span> <span class="hljs-built_in">add</span>(<span class="hljs-keyword">list</span>, item * <span class="hljs-number">2</span>)
<span class="hljs-keyword">endfor</span>
<span class="hljs-keyword">let</span> i = <span class="hljs-number">0</span>
<span class="hljs-keyword">while</span> i &lt; <span class="hljs-built_in">len</span>(<span class="hljs-keyword">list</span>)
  <span class="hljs-keyword">let</span> i += <span class="hljs-number">1</span>
<span class="hljs-keyword">endwhile</span>
<span class="hljs-keyword">try</span>
  <span class="hljs-keyword">call</span> Foo()
<span class="hljs-keyword">catch</span> /E123/
  <span class="hljs-keyword">echoerr</span> <span class="hljs-variable">v:exception</span>
<span class="hljs-keyword">finally</span>
  <span class="hljs-keyword">echo</span> <span class="hljs-string">&quot;done&quot;</span>
<span class="hljs-keyword">endtry</span>
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("vim",
"""
let s = "escaped \" quote"
let t = 'it''s'
let u = "line
\ continued"
echo 'unterminated
echo "next"
normal! 'a
echo "after"
""",
"""
<span class="hljs-keyword">let</span> s = <span class="hljs-string">&quot;escaped \&quot; quote&quot;</span>
<span class="hljs-keyword">let</span> t = <span class="hljs-string">&#x27;it&#x27;</span><span class="hljs-string">&#x27;s&#x27;</span>
<span class="hljs-keyword">let</span> <span class="hljs-keyword">u</span> = <span class="hljs-string">&quot;line
\ continued&quot;</span>
<span class="hljs-keyword">echo</span> <span class="hljs-string">&#x27;unterminated</span>
<span class="hljs-keyword">echo</span> <span class="hljs-string">&quot;next&quot;</span>
normal! <span class="hljs-string">&#x27;a</span>
<span class="hljs-keyword">echo</span> <span class="hljs-string">&quot;after&quot;</span>
""");
    }

    [Fact]
    public void DictionariesAndBuiltIns()
    {
        AssertHighlighter("vim",
"""
let d = {'key': 'value', 'n': 42}
let l = [1, 2.5, 0x1F]
echo has('nvim') ? 'nvim' : 'vim'
echo exists('g:loaded_plugin')
""",
"""
<span class="hljs-keyword">let</span> d = {<span class="hljs-string">&#x27;key&#x27;</span>: <span class="hljs-string">&#x27;value&#x27;</span>, <span class="hljs-string">&#x27;n&#x27;</span>: <span class="hljs-number">42</span>}
<span class="hljs-keyword">let</span> <span class="hljs-keyword">l</span> = [<span class="hljs-number">1</span>, <span class="hljs-number">2.5</span>, <span class="hljs-number">0</span>x1F]
<span class="hljs-keyword">echo</span> <span class="hljs-built_in">has</span>(<span class="hljs-string">&#x27;nvim&#x27;</span>) ? <span class="hljs-string">&#x27;nvim&#x27;</span> : <span class="hljs-string">&#x27;vim&#x27;</span>
<span class="hljs-keyword">echo</span> <span class="hljs-built_in">exists</span>(<span class="hljs-string">&#x27;g:loaded_plugin&#x27;</span>)
""");
    }

    [Fact]
    public void MarksAndCommands()
    {
        AssertHighlighter("vim",
"""
:'<,'>s/foo/bar/g
command! -nargs=1 Greet echo "Hello " . <q-args>
""",
"""
:<span class="hljs-string">&#x27;&lt;,&#x27;</span>&gt;s/foo/bar/g
command! -nargs=<span class="hljs-number">1</span> Greet <span class="hljs-keyword">echo</span> <span class="hljs-string">&quot;Hello &quot;</span> . <span class="hljs-symbol">&lt;q-args&gt;</span>
""");
    }

    [Fact]
    public void IllegalSemicolonIsIgnored()
    {
        AssertHighlighter("vim",
"""
echo 1; echo 2
""",
"""
<span class="hljs-keyword">echo</span> <span class="hljs-number">1</span>; <span class="hljs-keyword">echo</span> <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void Empty()
    {
        AssertHighlighter("vim", "", "");
    }
}
