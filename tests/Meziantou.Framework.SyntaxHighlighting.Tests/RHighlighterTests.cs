namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class RHighlighterTests
{
    [Fact]
    public void Hello()
    {
        AssertHighlighter("r",
"""
print("Hello, world!")
""",
"""
print<span class="hljs-punctuation">(</span><span class="hljs-string">&quot;Hello, world!&quot;</span><span class="hljs-punctuation">)</span>
""");
    }

    [Fact]
    public void Assignment()
    {
        AssertHighlighter("r",
"""
x <- 5
y = 10
15 -> z
w <<- 20
30 ->> v
pi <- 3.14
my.var <- c(1, 2, 3)
.hidden <- TRUE
""",
"""
x <span class="hljs-operator">&lt;-</span> <span class="hljs-number">5</span>
y <span class="hljs-operator">=</span> <span class="hljs-number">10</span>
<span class="hljs-number">15</span> <span class="hljs-operator">-&gt;</span> z
w <span class="hljs-operator">&lt;&lt;-</span> <span class="hljs-number">20</span>
<span class="hljs-number">30</span> <span class="hljs-operator">-&gt;&gt;</span> v
<span class="hljs-built_in">pi</span> <span class="hljs-operator">&lt;-</span> <span class="hljs-number">3.14</span>
my.var <span class="hljs-operator">&lt;-</span> <span class="hljs-built_in">c</span><span class="hljs-punctuation">(</span><span class="hljs-number">1</span><span class="hljs-punctuation">,</span> <span class="hljs-number">2</span><span class="hljs-punctuation">,</span> <span class="hljs-number">3</span><span class="hljs-punctuation">)</span>
.hidden <span class="hljs-operator">&lt;-</span> <span class="hljs-literal">TRUE</span>
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("r",
"""
a <- 42
b <- 3.14
c <- 1e10
d <- 2.5e-3
e <- 0x1F
f <- 5L
g <- 2i
h <- 0x1.8p3
i <- .5
j <- 100L + 1L
k <- -7
l <- x1 + 2
""",
"""
a <span class="hljs-operator">&lt;-</span> <span class="hljs-number">42</span>
b <span class="hljs-operator">&lt;-</span> <span class="hljs-number">3.14</span>
<span class="hljs-built_in">c</span> <span class="hljs-operator">&lt;-</span> <span class="hljs-number">1e10</span>
d <span class="hljs-operator">&lt;-</span> <span class="hljs-number">2.5e-3</span>
e <span class="hljs-operator">&lt;-</span> <span class="hljs-number">0x1F</span>
f <span class="hljs-operator">&lt;-</span> <span class="hljs-number">5L</span>
g <span class="hljs-operator">&lt;-</span> <span class="hljs-number">2i</span>
h <span class="hljs-operator">&lt;-</span> <span class="hljs-number">0x1.8p3</span>
i <span class="hljs-operator">&lt;-</span> <span class="hljs-number">.5</span>
j <span class="hljs-operator">&lt;-</span> <span class="hljs-number">100L</span> <span class="hljs-operator">+</span> <span class="hljs-number">1L</span>
k <span class="hljs-operator">&lt;-</span> <span class="hljs-operator">-</span><span class="hljs-number">7</span>
l <span class="hljs-operator">&lt;-</span> x1 <span class="hljs-operator">+</span> <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("r",
"""
s1 <- "double \"quoted\" \n"
s2 <- 'single \'quoted\''
s3 <- "multi
line"
""",
"""
s1 <span class="hljs-operator">&lt;-</span> <span class="hljs-string">&quot;double \&quot;quoted\&quot; \n&quot;</span>
s2 <span class="hljs-operator">&lt;-</span> <span class="hljs-string">&#x27;single \&#x27;quoted\&#x27;&#x27;</span>
s3 <span class="hljs-operator">&lt;-</span> <span class="hljs-string">&quot;multi
line&quot;</span>
""");
    }

    [Fact]
    public void RawStrings()
    {
        AssertHighlighter("r",
"""
r1 <- r"(C:\path\to\file)"
r2 <- R"[brackets]"
r3 <- r"{braces}"
r4 <- r"-(has )" inside)-"
r5 <- r'(single)'
r6 <- r"--(two )-" dashes)--"
""",
"""
r1 <span class="hljs-operator">&lt;-</span> <span class="hljs-string">r&quot;(C:\path\to\file)&quot;</span>
r2 <span class="hljs-operator">&lt;-</span> <span class="hljs-string">R&quot;[brackets]&quot;</span>
r3 <span class="hljs-operator">&lt;-</span> <span class="hljs-string">r&quot;{braces}&quot;</span>
r4 <span class="hljs-operator">&lt;-</span> <span class="hljs-string">r&quot;-(has )&quot; inside)-&quot;</span>
r5 <span class="hljs-operator">&lt;-</span> <span class="hljs-string">r&#x27;(single)&#x27;</span>
r6 <span class="hljs-operator">&lt;-</span> <span class="hljs-string">r&quot;--(two )-&quot; dashes)--&quot;</span>
""");
    }

    [Fact]
    public void Functions()
    {
        AssertHighlighter("r",
"""
add <- function(x, y = 2) {
  return(x + y)
}
square <- \(x) x^2
result <- add(1, y = 3)
""",
"""
add <span class="hljs-operator">&lt;-</span> <span class="hljs-keyword">function</span><span class="hljs-punctuation">(</span>x<span class="hljs-punctuation">,</span> y <span class="hljs-operator">=</span> <span class="hljs-number">2</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
  <span class="hljs-built_in">return</span><span class="hljs-punctuation">(</span>x <span class="hljs-operator">+</span> y<span class="hljs-punctuation">)</span>
<span class="hljs-punctuation">}</span>
square <span class="hljs-operator">&lt;-</span> <span class="hljs-punctuation">\</span><span class="hljs-punctuation">(</span>x<span class="hljs-punctuation">)</span> x<span class="hljs-operator">^</span><span class="hljs-number">2</span>
result <span class="hljs-operator">&lt;-</span> add<span class="hljs-punctuation">(</span><span class="hljs-number">1</span><span class="hljs-punctuation">,</span> y <span class="hljs-operator">=</span> <span class="hljs-number">3</span><span class="hljs-punctuation">)</span>
""");
    }

    [Fact]
    public void ControlFlow()
    {
        AssertHighlighter("r",
"""
if (x > 0) {
  print("positive")
} else if (x == 0) {
  print("zero")
} else {
  print("negative")
}
for (i in 1:10) {
  if (i %% 2 == 0) next
  if (i > 8) break
}
while (TRUE) {
  break
}
repeat {
  break
}
""",
"""
<span class="hljs-keyword">if</span> <span class="hljs-punctuation">(</span>x <span class="hljs-operator">&gt;</span> <span class="hljs-number">0</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
  print<span class="hljs-punctuation">(</span><span class="hljs-string">&quot;positive&quot;</span><span class="hljs-punctuation">)</span>
<span class="hljs-punctuation">}</span> <span class="hljs-keyword">else</span> <span class="hljs-keyword">if</span> <span class="hljs-punctuation">(</span>x <span class="hljs-operator">==</span> <span class="hljs-number">0</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
  print<span class="hljs-punctuation">(</span><span class="hljs-string">&quot;zero&quot;</span><span class="hljs-punctuation">)</span>
<span class="hljs-punctuation">}</span> <span class="hljs-keyword">else</span> <span class="hljs-punctuation">{</span>
  print<span class="hljs-punctuation">(</span><span class="hljs-string">&quot;negative&quot;</span><span class="hljs-punctuation">)</span>
<span class="hljs-punctuation">}</span>
<span class="hljs-keyword">for</span> <span class="hljs-punctuation">(</span>i <span class="hljs-keyword">in</span> <span class="hljs-number">1</span><span class="hljs-operator">:</span><span class="hljs-number">10</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
  <span class="hljs-keyword">if</span> <span class="hljs-punctuation">(</span>i <span class="hljs-operator">%%</span> <span class="hljs-number">2</span> <span class="hljs-operator">==</span> <span class="hljs-number">0</span><span class="hljs-punctuation">)</span> <span class="hljs-keyword">next</span>
  <span class="hljs-keyword">if</span> <span class="hljs-punctuation">(</span>i <span class="hljs-operator">&gt;</span> <span class="hljs-number">8</span><span class="hljs-punctuation">)</span> <span class="hljs-keyword">break</span>
<span class="hljs-punctuation">}</span>
<span class="hljs-keyword">while</span> <span class="hljs-punctuation">(</span><span class="hljs-literal">TRUE</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
  <span class="hljs-keyword">break</span>
<span class="hljs-punctuation">}</span>
<span class="hljs-keyword">repeat</span> <span class="hljs-punctuation">{</span>
  <span class="hljs-keyword">break</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Operators()
    {
        AssertHighlighter("r",
"""
a <- x + y - z * w / v ^ 2
b <- x %% 3
c <- x %/% 3
d <- x %in% y
e <- x %>% f()
g <- x |> f()
h <- !a & b | c && d || e
i <- a == b; j <- a != b; k <- a <= b
m <- base::paste("a")
n <- pkg:::internal()
o <- obj$field
p <- obj@slot
q <- ~ x + y
""",
"""
a <span class="hljs-operator">&lt;-</span> x <span class="hljs-operator">+</span> y <span class="hljs-operator">-</span> z <span class="hljs-operator">*</span> w <span class="hljs-operator">/</span> v <span class="hljs-operator">^</span> <span class="hljs-number">2</span>
b <span class="hljs-operator">&lt;-</span> x <span class="hljs-operator">%%</span> <span class="hljs-number">3</span>
<span class="hljs-built_in">c</span> <span class="hljs-operator">&lt;-</span> x <span class="hljs-operator">%/%</span> <span class="hljs-number">3</span>
d <span class="hljs-operator">&lt;-</span> x <span class="hljs-operator">%in%</span> y
e <span class="hljs-operator">&lt;-</span> x <span class="hljs-operator">%&gt;%</span> f<span class="hljs-punctuation">(</span><span class="hljs-punctuation">)</span>
g <span class="hljs-operator">&lt;-</span> x <span class="hljs-operator">|&gt;</span> f<span class="hljs-punctuation">(</span><span class="hljs-punctuation">)</span>
h <span class="hljs-operator">&lt;-</span> <span class="hljs-operator">!</span>a <span class="hljs-operator">&amp;</span> b <span class="hljs-operator">|</span> <span class="hljs-built_in">c</span> <span class="hljs-operator">&amp;&amp;</span> d <span class="hljs-operator">||</span> e
i <span class="hljs-operator">&lt;-</span> a <span class="hljs-operator">==</span> b; j <span class="hljs-operator">&lt;-</span> a <span class="hljs-operator">!=</span> b; k <span class="hljs-operator">&lt;-</span> a <span class="hljs-operator">&lt;=</span> b
m <span class="hljs-operator">&lt;-</span> base<span class="hljs-operator">::</span>paste<span class="hljs-punctuation">(</span><span class="hljs-string">&quot;a&quot;</span><span class="hljs-punctuation">)</span>
n <span class="hljs-operator">&lt;-</span> pkg<span class="hljs-operator">:::</span>internal<span class="hljs-punctuation">(</span><span class="hljs-punctuation">)</span>
o <span class="hljs-operator">&lt;-</span> obj<span class="hljs-operator">$</span>field
p <span class="hljs-operator">&lt;-</span> obj<span class="hljs-operator">@</span>slot
q <span class="hljs-operator">&lt;-</span> <span class="hljs-operator">~</span> x <span class="hljs-operator">+</span> y
""");
    }

    [Fact]
    public void Literals()
    {
        AssertHighlighter("r",
"""
a <- NULL
b <- NA
c <- c(TRUE, FALSE, T, F)
d <- Inf; e <- -Inf; f <- NaN
g <- NA_integer_; h <- NA_real_; i <- NA_character_; j <- NA_complex_
""",
"""
a <span class="hljs-operator">&lt;-</span> <span class="hljs-literal">NULL</span>
b <span class="hljs-operator">&lt;-</span> <span class="hljs-literal">NA</span>
<span class="hljs-built_in">c</span> <span class="hljs-operator">&lt;-</span> <span class="hljs-built_in">c</span><span class="hljs-punctuation">(</span><span class="hljs-literal">TRUE</span><span class="hljs-punctuation">,</span> <span class="hljs-literal">FALSE</span><span class="hljs-punctuation">,</span> <span class="hljs-built_in">T</span><span class="hljs-punctuation">,</span> <span class="hljs-built_in">F</span><span class="hljs-punctuation">)</span>
d <span class="hljs-operator">&lt;-</span> <span class="hljs-literal">Inf</span>; e <span class="hljs-operator">&lt;-</span> <span class="hljs-operator">-</span><span class="hljs-literal">Inf</span>; f <span class="hljs-operator">&lt;-</span> <span class="hljs-literal">NaN</span>
g <span class="hljs-operator">&lt;-</span> <span class="hljs-literal">NA_integer_</span>; h <span class="hljs-operator">&lt;-</span> <span class="hljs-literal">NA_real_</span>; i <span class="hljs-operator">&lt;-</span> <span class="hljs-literal">NA_character_</span>; j <span class="hljs-operator">&lt;-</span> <span class="hljs-literal">NA_complex_</span>
""");
    }

    [Fact]
    public void Builtins()
    {
        AssertHighlighter("r",
"""
x <- c(1, 2, 3)
n <- length(x)
s <- sum(x)
m <- max(x)
is.na(x)
as.numeric("1")
l <- list(a = 1, b = 2)
""",
"""
x <span class="hljs-operator">&lt;-</span> <span class="hljs-built_in">c</span><span class="hljs-punctuation">(</span><span class="hljs-number">1</span><span class="hljs-punctuation">,</span> <span class="hljs-number">2</span><span class="hljs-punctuation">,</span> <span class="hljs-number">3</span><span class="hljs-punctuation">)</span>
n <span class="hljs-operator">&lt;-</span> <span class="hljs-built_in">length</span><span class="hljs-punctuation">(</span>x<span class="hljs-punctuation">)</span>
s <span class="hljs-operator">&lt;-</span> <span class="hljs-built_in">sum</span><span class="hljs-punctuation">(</span>x<span class="hljs-punctuation">)</span>
m <span class="hljs-operator">&lt;-</span> <span class="hljs-built_in">max</span><span class="hljs-punctuation">(</span>x<span class="hljs-punctuation">)</span>
<span class="hljs-built_in">is.na</span><span class="hljs-punctuation">(</span>x<span class="hljs-punctuation">)</span>
<span class="hljs-built_in">as.numeric</span><span class="hljs-punctuation">(</span><span class="hljs-string">&quot;1&quot;</span><span class="hljs-punctuation">)</span>
l <span class="hljs-operator">&lt;-</span> <span class="hljs-built_in">list</span><span class="hljs-punctuation">(</span>a <span class="hljs-operator">=</span> <span class="hljs-number">1</span><span class="hljs-punctuation">,</span> b <span class="hljs-operator">=</span> <span class="hljs-number">2</span><span class="hljs-punctuation">)</span>
""");
    }

    [Fact]
    public void VectorsIndexing()
    {
        AssertHighlighter("r",
"""
v <- c(10, 20, 30)
v[1]
v[-1]
v[v > 15]
lst[["key"]]
df[1, "col"]
m[2, , drop = FALSE]
""",
"""
v <span class="hljs-operator">&lt;-</span> <span class="hljs-built_in">c</span><span class="hljs-punctuation">(</span><span class="hljs-number">10</span><span class="hljs-punctuation">,</span> <span class="hljs-number">20</span><span class="hljs-punctuation">,</span> <span class="hljs-number">30</span><span class="hljs-punctuation">)</span>
v<span class="hljs-punctuation">[</span><span class="hljs-number">1</span><span class="hljs-punctuation">]</span>
v<span class="hljs-punctuation">[</span><span class="hljs-operator">-</span><span class="hljs-number">1</span><span class="hljs-punctuation">]</span>
v<span class="hljs-punctuation">[</span>v <span class="hljs-operator">&gt;</span> <span class="hljs-number">15</span><span class="hljs-punctuation">]</span>
lst<span class="hljs-punctuation">[[</span><span class="hljs-string">&quot;key&quot;</span><span class="hljs-punctuation">]</span><span class="hljs-punctuation">]</span>
df<span class="hljs-punctuation">[</span><span class="hljs-number">1</span><span class="hljs-punctuation">,</span> <span class="hljs-string">&quot;col&quot;</span><span class="hljs-punctuation">]</span>
m<span class="hljs-punctuation">[</span><span class="hljs-number">2</span><span class="hljs-punctuation">,</span> <span class="hljs-punctuation">,</span> drop <span class="hljs-operator">=</span> <span class="hljs-literal">FALSE</span><span class="hljs-punctuation">]</span>
""");
    }

    [Fact]
    public void DataFrame()
    {
        AssertHighlighter("r",
"""
df <- data.frame(
  name = c("Alice", "Bob"),
  age = c(25, 30),
  stringsAsFactors = FALSE
)
summary(df)
df$age[df$name == "Bob"]
""",
"""
df <span class="hljs-operator">&lt;-</span> data.frame<span class="hljs-punctuation">(</span>
  name <span class="hljs-operator">=</span> <span class="hljs-built_in">c</span><span class="hljs-punctuation">(</span><span class="hljs-string">&quot;Alice&quot;</span><span class="hljs-punctuation">,</span> <span class="hljs-string">&quot;Bob&quot;</span><span class="hljs-punctuation">)</span><span class="hljs-punctuation">,</span>
  age <span class="hljs-operator">=</span> <span class="hljs-built_in">c</span><span class="hljs-punctuation">(</span><span class="hljs-number">25</span><span class="hljs-punctuation">,</span> <span class="hljs-number">30</span><span class="hljs-punctuation">)</span><span class="hljs-punctuation">,</span>
  stringsAsFactors <span class="hljs-operator">=</span> <span class="hljs-literal">FALSE</span>
<span class="hljs-punctuation">)</span>
summary<span class="hljs-punctuation">(</span>df<span class="hljs-punctuation">)</span>
df<span class="hljs-operator">$</span>age<span class="hljs-punctuation">[</span>df<span class="hljs-operator">$</span>name <span class="hljs-operator">==</span> <span class="hljs-string">&quot;Bob&quot;</span><span class="hljs-punctuation">]</span>
""");
    }

    [Fact]
    public void Tidyverse()
    {
        AssertHighlighter("r",
"""
library(dplyr)
result <- df %>%
  filter(age > 20) %>%
  mutate(age2 = age * 2) %>%
  group_by(name) %>%
  summarise(total = sum(age2))
""",
"""
library<span class="hljs-punctuation">(</span>dplyr<span class="hljs-punctuation">)</span>
result <span class="hljs-operator">&lt;-</span> df <span class="hljs-operator">%&gt;%</span>
  filter<span class="hljs-punctuation">(</span>age <span class="hljs-operator">&gt;</span> <span class="hljs-number">20</span><span class="hljs-punctuation">)</span> <span class="hljs-operator">%&gt;%</span>
  mutate<span class="hljs-punctuation">(</span>age2 <span class="hljs-operator">=</span> age <span class="hljs-operator">*</span> <span class="hljs-number">2</span><span class="hljs-punctuation">)</span> <span class="hljs-operator">%&gt;%</span>
  group_by<span class="hljs-punctuation">(</span>name<span class="hljs-punctuation">)</span> <span class="hljs-operator">%&gt;%</span>
  summarise<span class="hljs-punctuation">(</span>total <span class="hljs-operator">=</span> <span class="hljs-built_in">sum</span><span class="hljs-punctuation">(</span>age2<span class="hljs-punctuation">)</span><span class="hljs-punctuation">)</span>
""");
    }

    [Fact]
    public void BacktickNames()
    {
        AssertHighlighter("r",
"""
`my var` <- 1
`if` <- function(x) x
df$`weird name`
`a\`b` <- 2
""",
"""
`my var` <span class="hljs-operator">&lt;-</span> <span class="hljs-number">1</span>
`if` <span class="hljs-operator">&lt;-</span> <span class="hljs-keyword">function</span><span class="hljs-punctuation">(</span>x<span class="hljs-punctuation">)</span> x
df<span class="hljs-operator">$</span>`weird name`
`a\`b` <span class="hljs-operator">&lt;-</span> <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("r",
"""
# a comment
x <- 1 # trailing
# TODO: fix
""",
"""
<span class="hljs-comment"># a comment</span>
x <span class="hljs-operator">&lt;-</span> <span class="hljs-number">1</span> <span class="hljs-comment"># trailing</span>
<span class="hljs-comment"># <span class="hljs-doctag">TODO:</span> fix</span>
""");
    }

    [Fact]
    public void Roxygen()
    {
        AssertHighlighter("r",
"""
#' Add two numbers
#'
#' @param x A number.
#' @param `y` Another number.
#' @return The sum of \code{x} and \code{y}.
#' @export
#' @examples
#' add(1, 2)
#' add(3, @x)
#' @seealso \link{sum}
add <- function(x, y) x + y
""",
"""
<span class="hljs-comment">#&#x27; Add two numbers</span>
<span class="hljs-comment">#&#x27;</span>
<span class="hljs-comment">#&#x27; <span class="hljs-doctag">@param <span class="hljs-variable">x</span></span> A number.</span>
<span class="hljs-comment">#&#x27; <span class="hljs-doctag">@param <span class="hljs-variable">`y`</span></span> Another number.</span>
<span class="hljs-comment">#&#x27; <span class="hljs-doctag">@return</span> The sum of <span class="hljs-keyword">\code</span>{x} and <span class="hljs-keyword">\code</span>{y}.</span>
<span class="hljs-comment">#&#x27; <span class="hljs-doctag">@export</span></span>
<span class="hljs-comment">#&#x27; <span class="hljs-doctag">@examples</span>
#&#x27; add(1, 2)
#&#x27; add(3, @x)</span>
<span class="hljs-comment">#&#x27; <span class="hljs-doctag">@seealso</span> <span class="hljs-keyword">\link</span>{sum}</span>
add <span class="hljs-operator">&lt;-</span> <span class="hljs-keyword">function</span><span class="hljs-punctuation">(</span>x<span class="hljs-punctuation">,</span> y<span class="hljs-punctuation">)</span> x <span class="hljs-operator">+</span> y
""");
    }

    [Fact]
    public void RoxygenExamplesEnd()
    {
        AssertHighlighter("r",
"""
#' @examples
#' foo(1)
bar <- 2
""",
"""
<span class="hljs-comment">#&#x27; <span class="hljs-doctag">@examples</span>
#&#x27; foo(1)</span>
bar <span class="hljs-operator">&lt;-</span> <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void S4Classes()
    {
        AssertHighlighter("r",
"""
setClass("Person", representation(name = "character", age = "numeric"))
setGeneric("greet", function(obj, ...) standardGeneric("greet"))
setMethod("greet", "Person", function(obj, ...) cat("Hi", obj@name, "\n"))
""",
"""
setClass<span class="hljs-punctuation">(</span><span class="hljs-string">&quot;Person&quot;</span><span class="hljs-punctuation">,</span> representation<span class="hljs-punctuation">(</span>name <span class="hljs-operator">=</span> <span class="hljs-string">&quot;character&quot;</span><span class="hljs-punctuation">,</span> age <span class="hljs-operator">=</span> <span class="hljs-string">&quot;numeric&quot;</span><span class="hljs-punctuation">)</span><span class="hljs-punctuation">)</span>
setGeneric<span class="hljs-punctuation">(</span><span class="hljs-string">&quot;greet&quot;</span><span class="hljs-punctuation">,</span> <span class="hljs-keyword">function</span><span class="hljs-punctuation">(</span>obj<span class="hljs-punctuation">,</span> ...<span class="hljs-punctuation">)</span> <span class="hljs-built_in">standardGeneric</span><span class="hljs-punctuation">(</span><span class="hljs-string">&quot;greet&quot;</span><span class="hljs-punctuation">)</span><span class="hljs-punctuation">)</span>
setMethod<span class="hljs-punctuation">(</span><span class="hljs-string">&quot;greet&quot;</span><span class="hljs-punctuation">,</span> <span class="hljs-string">&quot;Person&quot;</span><span class="hljs-punctuation">,</span> <span class="hljs-keyword">function</span><span class="hljs-punctuation">(</span>obj<span class="hljs-punctuation">,</span> ...<span class="hljs-punctuation">)</span> cat<span class="hljs-punctuation">(</span><span class="hljs-string">&quot;Hi&quot;</span><span class="hljs-punctuation">,</span> obj<span class="hljs-operator">@</span>name<span class="hljs-punctuation">,</span> <span class="hljs-string">&quot;\n&quot;</span><span class="hljs-punctuation">)</span><span class="hljs-punctuation">)</span>
""");
    }

    [Fact]
    public void R6Style()
    {
        AssertHighlighter("r",
"""
Person <- R6Class("Person",
  public = list(
    name = NULL,
    initialize = function(name) {
      self$name <- name
    }
  )
)
""",
"""
Person <span class="hljs-operator">&lt;-</span> R6Class<span class="hljs-punctuation">(</span><span class="hljs-string">&quot;Person&quot;</span><span class="hljs-punctuation">,</span>
  public <span class="hljs-operator">=</span> <span class="hljs-built_in">list</span><span class="hljs-punctuation">(</span>
    name <span class="hljs-operator">=</span> <span class="hljs-literal">NULL</span><span class="hljs-punctuation">,</span>
    initialize <span class="hljs-operator">=</span> <span class="hljs-keyword">function</span><span class="hljs-punctuation">(</span>name<span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
      self<span class="hljs-operator">$</span>name <span class="hljs-operator">&lt;-</span> name
    <span class="hljs-punctuation">}</span>
  <span class="hljs-punctuation">)</span>
<span class="hljs-punctuation">)</span>
""");
    }

    [Fact]
    public void ApplyFamily()
    {
        AssertHighlighter("r",
"""
sapply(1:5, function(i) i^2)
lapply(lst, `[[`, "a")
mapply(rep, 1:3, 3:1)
""",
"""
sapply<span class="hljs-punctuation">(</span><span class="hljs-number">1</span><span class="hljs-operator">:</span><span class="hljs-number">5</span><span class="hljs-punctuation">,</span> <span class="hljs-keyword">function</span><span class="hljs-punctuation">(</span>i<span class="hljs-punctuation">)</span> i<span class="hljs-operator">^</span><span class="hljs-number">2</span><span class="hljs-punctuation">)</span>
lapply<span class="hljs-punctuation">(</span>lst<span class="hljs-punctuation">,</span> `[[`<span class="hljs-punctuation">,</span> <span class="hljs-string">&quot;a&quot;</span><span class="hljs-punctuation">)</span>
mapply<span class="hljs-punctuation">(</span><span class="hljs-built_in">rep</span><span class="hljs-punctuation">,</span> <span class="hljs-number">1</span><span class="hljs-operator">:</span><span class="hljs-number">3</span><span class="hljs-punctuation">,</span> <span class="hljs-number">3</span><span class="hljs-operator">:</span><span class="hljs-number">1</span><span class="hljs-punctuation">)</span>
""");
    }

    [Fact]
    public void SwitchStmt()
    {
        AssertHighlighter("r",
"""
res <- switch(type,
  "a" = 1,
  "b" = 2,
  stop("unknown")
)
""",
"""
res <span class="hljs-operator">&lt;-</span> <span class="hljs-built_in">switch</span><span class="hljs-punctuation">(</span>type<span class="hljs-punctuation">,</span>
  <span class="hljs-string">&quot;a&quot;</span> <span class="hljs-operator">=</span> <span class="hljs-number">1</span><span class="hljs-punctuation">,</span>
  <span class="hljs-string">&quot;b&quot;</span> <span class="hljs-operator">=</span> <span class="hljs-number">2</span><span class="hljs-punctuation">,</span>
  stop<span class="hljs-punctuation">(</span><span class="hljs-string">&quot;unknown&quot;</span><span class="hljs-punctuation">)</span>
<span class="hljs-punctuation">)</span>
""");
    }

    [Fact]
    public void TryCatch()
    {
        AssertHighlighter("r",
"""
result <- tryCatch({
  risky()
}, error = function(e) {
  message("Error: ", conditionMessage(e))
  NULL
}, finally = {
  cleanup()
})
""",
"""
result <span class="hljs-operator">&lt;-</span> tryCatch<span class="hljs-punctuation">(</span><span class="hljs-punctuation">{</span>
  risky<span class="hljs-punctuation">(</span><span class="hljs-punctuation">)</span>
<span class="hljs-punctuation">}</span><span class="hljs-punctuation">,</span> error <span class="hljs-operator">=</span> <span class="hljs-keyword">function</span><span class="hljs-punctuation">(</span>e<span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
  message<span class="hljs-punctuation">(</span><span class="hljs-string">&quot;Error: &quot;</span><span class="hljs-punctuation">,</span> conditionMessage<span class="hljs-punctuation">(</span>e<span class="hljs-punctuation">)</span><span class="hljs-punctuation">)</span>
  <span class="hljs-literal">NULL</span>
<span class="hljs-punctuation">}</span><span class="hljs-punctuation">,</span> finally <span class="hljs-operator">=</span> <span class="hljs-punctuation">{</span>
  cleanup<span class="hljs-punctuation">(</span><span class="hljs-punctuation">)</span>
<span class="hljs-punctuation">}</span><span class="hljs-punctuation">)</span>
""");
    }

    [Fact]
    public void Formulas()
    {
        AssertHighlighter("r",
"""
model <- lm(y ~ x + I(x^2), data = df)
""",
"""
model <span class="hljs-operator">&lt;-</span> lm<span class="hljs-punctuation">(</span>y <span class="hljs-operator">~</span> x <span class="hljs-operator">+</span> I<span class="hljs-punctuation">(</span>x<span class="hljs-operator">^</span><span class="hljs-number">2</span><span class="hljs-punctuation">)</span><span class="hljs-punctuation">,</span> data <span class="hljs-operator">=</span> df<span class="hljs-punctuation">)</span>
""");
    }

    [Fact]
    public void Sequences()
    {
        AssertHighlighter("r",
"""
x <- 1:10
y <- seq(0, 1, by = 0.1)
z <- seq_len(5)
""",
"""
x <span class="hljs-operator">&lt;-</span> <span class="hljs-number">1</span><span class="hljs-operator">:</span><span class="hljs-number">10</span>
y <span class="hljs-operator">&lt;-</span> seq<span class="hljs-punctuation">(</span><span class="hljs-number">0</span><span class="hljs-punctuation">,</span> <span class="hljs-number">1</span><span class="hljs-punctuation">,</span> by <span class="hljs-operator">=</span> <span class="hljs-number">0.1</span><span class="hljs-punctuation">)</span>
z <span class="hljs-operator">&lt;-</span> <span class="hljs-built_in">seq_len</span><span class="hljs-punctuation">(</span><span class="hljs-number">5</span><span class="hljs-punctuation">)</span>
""");
    }

    [Fact]
    public void NumbersAfterPunct()
    {
        AssertHighlighter("r",
"""
f(1,2)
x[1]
{1}
a<-1
b<-2L
c(-1, +2)
""",
"""
f<span class="hljs-punctuation">(</span><span class="hljs-number">1</span><span class="hljs-punctuation">,</span><span class="hljs-number">2</span><span class="hljs-punctuation">)</span>
x<span class="hljs-punctuation">[</span><span class="hljs-number">1</span><span class="hljs-punctuation">]</span>
<span class="hljs-punctuation">{</span><span class="hljs-number">1</span><span class="hljs-punctuation">}</span>
a<span class="hljs-operator">&lt;-</span><span class="hljs-number">1</span>
b<span class="hljs-operator">&lt;-</span><span class="hljs-number">2L</span>
<span class="hljs-built_in">c</span><span class="hljs-punctuation">(</span><span class="hljs-operator">-</span><span class="hljs-number">1</span><span class="hljs-punctuation">,</span> <span class="hljs-operator">+</span><span class="hljs-number">2</span><span class="hljs-punctuation">)</span>
""");
    }

    [Fact]
    public void Dots()
    {
        AssertHighlighter("r",
"""
f <- function(...) list(...)
g <- function(..1) ..1
.x <- 1
x. <- 2
""",
"""
f <span class="hljs-operator">&lt;-</span> <span class="hljs-keyword">function</span><span class="hljs-punctuation">(</span>...<span class="hljs-punctuation">)</span> <span class="hljs-built_in">list</span><span class="hljs-punctuation">(</span>...<span class="hljs-punctuation">)</span>
g <span class="hljs-operator">&lt;-</span> <span class="hljs-keyword">function</span><span class="hljs-punctuation">(</span>..1<span class="hljs-punctuation">)</span> ..1
.x <span class="hljs-operator">&lt;-</span> <span class="hljs-number">1</span>
x. <span class="hljs-operator">&lt;-</span> <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("r",
"""
x <- "héllo"
""",
"""
x <span class="hljs-operator">&lt;-</span> <span class="hljs-string">&quot;héllo&quot;</span>
""");
    }
}
