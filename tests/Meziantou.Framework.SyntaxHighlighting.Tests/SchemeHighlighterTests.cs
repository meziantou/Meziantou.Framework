namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class SchemeHighlighterTests
{    [Fact]
    public void Factorial()
    {
        AssertHighlighter("scheme",
"""
(define (factorial n)
  (if (= n 0)
      1
      (* n (factorial (- n 1)))))

(display (factorial 10))
(newline)
""",
"""
(<span class="hljs-name"><span class="hljs-built_in">define</span></span> (<span class="hljs-name">factorial</span> n)
  (<span class="hljs-name"><span class="hljs-built_in">if</span></span> (<span class="hljs-name"><span class="hljs-built_in">=</span></span> n <span class="hljs-number">0</span>)
      <span class="hljs-number">1</span>
      (<span class="hljs-name"><span class="hljs-built_in">*</span></span> n (<span class="hljs-name">factorial</span> (<span class="hljs-name"><span class="hljs-built_in">-</span></span> n <span class="hljs-number">1</span>)))))

(<span class="hljs-name"><span class="hljs-built_in">display</span></span> (<span class="hljs-name">factorial</span> <span class="hljs-number">10</span>))
(<span class="hljs-name"><span class="hljs-built_in">newline</span></span>)
""");
    }

    [Fact]
    public void Lambda()
    {
        AssertHighlighter("scm",
"""
(define square (lambda (x) (* x x)))
(map (lambda (x y) (+ x y)) '(1 2 3) '(4 5 6))
(let ([a 1] [b 2]) (+ a b))
(lambda args (apply + args))
(define-syntax swap!
  (syntax-rules ()
    ((_ a b) (let ((tmp a)) (set! a b) (set! b tmp)))))
""",
"""
(<span class="hljs-name"><span class="hljs-built_in">define</span></span> square (<span class="hljs-name"><span class="hljs-built_in">lambda</span></span> (x) (<span class="hljs-name"><span class="hljs-built_in">*</span></span> x x)))
(<span class="hljs-name"><span class="hljs-built_in">map</span></span> (<span class="hljs-name"><span class="hljs-built_in">lambda</span></span> (x y) (<span class="hljs-name"><span class="hljs-built_in">+</span></span> x y)) &#x27;(<span class="hljs-number">1</span> <span class="hljs-number">2</span> <span class="hljs-number">3</span>) &#x27;(<span class="hljs-number">4</span> <span class="hljs-number">5</span> <span class="hljs-number">6</span>))
(<span class="hljs-name"><span class="hljs-built_in">let</span></span> ([<span class="hljs-name">a</span> <span class="hljs-number">1</span>] [<span class="hljs-name">b</span> <span class="hljs-number">2</span>]) (<span class="hljs-name"><span class="hljs-built_in">+</span></span> a b))
(<span class="hljs-name"><span class="hljs-built_in">lambda</span></span> <span class="hljs-name">args</span> (apply + args))
(<span class="hljs-name"><span class="hljs-built_in">define-syntax</span></span> swap!
  (<span class="hljs-name"><span class="hljs-built_in">syntax-rules</span></span> ()
    ((<span class="hljs-name">_</span> a b) (<span class="hljs-name"><span class="hljs-built_in">let</span></span> ((<span class="hljs-name">tmp</span> a)) (<span class="hljs-name"><span class="hljs-built_in">set!</span></span> a b) (<span class="hljs-name"><span class="hljs-built_in">set!</span></span> b tmp)))))
""");
    }

    [Fact]
    public void NamedLetAndLetrec()
    {
        AssertHighlighter("scheme",
"""
(let loop ((i 0) (acc '()))
  (if (< i 10)
      (loop (+ i 1) (cons i acc))
      (reverse acc)))
(let* ((x 1) (y (+ x 1))) (list x y))
(letrec ((even? (lambda (n) (if (zero? n) #t (odd? (- n 1)))))
         (odd? (lambda (n) (if (zero? n) #f (even? (- n 1))))))
  (even? 100))
""",
"""
(<span class="hljs-name"><span class="hljs-built_in">let</span></span> loop ((<span class="hljs-name">i</span> <span class="hljs-number">0</span>) (<span class="hljs-name">acc</span> &#x27;()))
  (<span class="hljs-name"><span class="hljs-built_in">if</span></span> (<span class="hljs-name"><span class="hljs-built_in">&lt;</span></span> i <span class="hljs-number">10</span>)
      (<span class="hljs-name">loop</span> (<span class="hljs-name"><span class="hljs-built_in">+</span></span> i <span class="hljs-number">1</span>) (<span class="hljs-name"><span class="hljs-built_in">cons</span></span> i acc))
      (<span class="hljs-name"><span class="hljs-built_in">reverse</span></span> acc)))
(<span class="hljs-name"><span class="hljs-built_in">let*</span></span> ((<span class="hljs-name">x</span> <span class="hljs-number">1</span>) (<span class="hljs-name">y</span> (<span class="hljs-name"><span class="hljs-built_in">+</span></span> x <span class="hljs-number">1</span>))) (<span class="hljs-name"><span class="hljs-built_in">list</span></span> x y))
(<span class="hljs-name"><span class="hljs-built_in">letrec</span></span> ((<span class="hljs-name"><span class="hljs-built_in">even?</span></span> (<span class="hljs-name"><span class="hljs-built_in">lambda</span></span> (n) (<span class="hljs-name"><span class="hljs-built_in">if</span></span> (<span class="hljs-name"><span class="hljs-built_in">zero?</span></span> n) <span class="hljs-literal">#t</span> (<span class="hljs-name"><span class="hljs-built_in">odd?</span></span> (<span class="hljs-name"><span class="hljs-built_in">-</span></span> n <span class="hljs-number">1</span>)))))
         (<span class="hljs-name"><span class="hljs-built_in">odd?</span></span> (<span class="hljs-name"><span class="hljs-built_in">lambda</span></span> (n) (<span class="hljs-name"><span class="hljs-built_in">if</span></span> (<span class="hljs-name"><span class="hljs-built_in">zero?</span></span> n) <span class="hljs-literal">#f</span> (<span class="hljs-name"><span class="hljs-built_in">even?</span></span> (<span class="hljs-name"><span class="hljs-built_in">-</span></span> n <span class="hljs-number">1</span>))))))
  (<span class="hljs-name"><span class="hljs-built_in">even?</span></span> <span class="hljs-number">100</span>))
""");
    }

    [Fact]
    public void Literals_ComplexAndUppercaseHexNumbers()
    {
        AssertHighlighter("scheme",
"""
(list #t #f #\a #\space #\newline #\( "string" 'sym)
(vector 1 2.5 -3 +4 1/2 #b1010 #o777 #xff #xFF 1+2i -1.5-2/3i)
(quote (a b c))
`(1 ,(+ 1 1) ,@(list 3 4))
'(a "b" 1 #t 'c (nested list))
""",
"""
(<span class="hljs-name"><span class="hljs-built_in">list</span></span> <span class="hljs-literal">#t</span> <span class="hljs-literal">#f</span> <span class="hljs-literal">#\a</span> <span class="hljs-literal">#\space</span> <span class="hljs-literal">#\newline</span> <span class="hljs-literal">#\(</span> <span class="hljs-string">&quot;string&quot;</span> <span class="hljs-symbol">&#x27;sym</span>)
(<span class="hljs-name"><span class="hljs-built_in">vector</span></span> <span class="hljs-number">1</span> <span class="hljs-number">2.5</span> <span class="hljs-number">-3</span> <span class="hljs-number">+4</span> <span class="hljs-number">1/2</span> <span class="hljs-number">#b1010</span> <span class="hljs-number">#o777</span> <span class="hljs-number">#xff</span> <span class="hljs-number">#xFF</span> <span class="hljs-number">1+2i</span> <span class="hljs-number">-1.5-2/3i</span>)
(<span class="hljs-name"><span class="hljs-built_in">quote</span></span> (<span class="hljs-name">a</span> b c))
`(<span class="hljs-number">1</span> ,(+ <span class="hljs-number">1</span> <span class="hljs-number">1</span>) ,@(list <span class="hljs-number">3</span> <span class="hljs-number">4</span>))
&#x27;(a <span class="hljs-string">&quot;b&quot;</span> <span class="hljs-number">1</span> <span class="hljs-literal">#t</span> <span class="hljs-symbol">&#x27;c</span> (nested list))
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("scheme",
"""
(define s "hello \"world\"\n")
(define multi "line one
line two")
(string-append "a" "b")
""",
"""
(<span class="hljs-name"><span class="hljs-built_in">define</span></span> s <span class="hljs-string">&quot;hello \&quot;world\&quot;\n&quot;</span>)
(<span class="hljs-name"><span class="hljs-built_in">define</span></span> multi <span class="hljs-string">&quot;line one
line two&quot;</span>)
(<span class="hljs-name"><span class="hljs-built_in">string-append</span></span> <span class="hljs-string">&quot;a&quot;</span> <span class="hljs-string">&quot;b&quot;</span>)
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("scheme",
"""
; line comment
;; TODO: document this
(define x 1) ; trailing
#| block
   comment |#
(define y 2)
#;(ignored datum)
""",
"""
<span class="hljs-comment">; line comment</span>
<span class="hljs-comment">;; <span class="hljs-doctag">TODO:</span> document this</span>
(<span class="hljs-name"><span class="hljs-built_in">define</span></span> x <span class="hljs-number">1</span>) <span class="hljs-comment">; trailing</span>
<span class="hljs-comment">#| block
   comment |#</span>
(<span class="hljs-name"><span class="hljs-built_in">define</span></span> y <span class="hljs-number">2</span>)
#<span class="hljs-comment">;(ignored datum)</span>
""");
    }

    [Fact]
    public void Shebang()
    {
        AssertHighlighter("scheme",
"""
#!/usr/bin/env guile
!#
(display "hi")
""",
"""
<span class="hljs-meta">#!/usr/bin/env guile</span>
!#
(<span class="hljs-name"><span class="hljs-built_in">display</span></span> <span class="hljs-string">&quot;hi&quot;</span>)
""");
    }

    [Fact]
    public void TopLevelAtomsAreText()
    {
        AssertHighlighter("scheme",
"""
foo bar
(define z 3)
42 "top" 'quoted
""",
"""
foo bar
(<span class="hljs-name"><span class="hljs-built_in">define</span></span> z <span class="hljs-number">3</span>)
<span class="hljs-number">42</span> <span class="hljs-string">&quot;top&quot;</span> <span class="hljs-symbol">&#x27;quoted</span>
""");
    }

    [Fact]
    public void UnterminatedString()
    {
        AssertHighlighter("scheme",
"""
(define (f x)
  (+ x 1)
(define s "unterminated
(g 2)

""",
"""
(<span class="hljs-name"><span class="hljs-built_in">define</span></span> (<span class="hljs-name">f</span> x)
  (<span class="hljs-name"><span class="hljs-built_in">+</span></span> x <span class="hljs-number">1</span>)
(<span class="hljs-name"><span class="hljs-built_in">define</span></span> s <span class="hljs-string">&quot;unterminated
(g 2)
</span>
""");
    }

    [Fact]
    public void Continuations()
    {
        AssertHighlighter("scheme",
"""
(define-record-type point
  (make-point x y)
  point?
  (x point-x set-point-x!)
  (y point-y))
(call-with-current-continuation (lambda (k) (k 42)))
(call/cc (lambda (return) (for-each (lambda (x) (if (negative? x) (return x))) lst)))
""",
"""
(<span class="hljs-name">define-record-type</span> point
  (<span class="hljs-name">make-point</span> x y)
  point?
  (<span class="hljs-name">x</span> point-x set-point-x!)
  (<span class="hljs-name">y</span> point-y))
(<span class="hljs-name"><span class="hljs-built_in">call-with-current-continuation</span></span> (<span class="hljs-name"><span class="hljs-built_in">lambda</span></span> (k) (<span class="hljs-name">k</span> <span class="hljs-number">42</span>)))
(<span class="hljs-name"><span class="hljs-built_in">call/cc</span></span> (<span class="hljs-name"><span class="hljs-built_in">lambda</span></span> (return) (<span class="hljs-name"><span class="hljs-built_in">for-each</span></span> (<span class="hljs-name"><span class="hljs-built_in">lambda</span></span> (x) (<span class="hljs-name"><span class="hljs-built_in">if</span></span> (<span class="hljs-name"><span class="hljs-built_in">negative?</span></span> x) (<span class="hljs-name">return</span> x))) lst)))
""");
    }

    [Fact]
    public void Conditionals()
    {
        AssertHighlighter("scheme",
"""
(cond ((> x 0) 'positive)
      ((< x 0) 'negative)
      (else 'zero))
(case (* 2 3)
  ((2 3 5 7) 'prime)
  ((1 4 6 8 9) 'composite))
(when (pair? x) (display (car x)))
(unless (null? x) (display (cdr x)))
""",
"""
(<span class="hljs-name"><span class="hljs-built_in">cond</span></span> ((<span class="hljs-name"><span class="hljs-built_in">&gt;</span></span> x <span class="hljs-number">0</span>) <span class="hljs-symbol">&#x27;positive</span>)
      ((<span class="hljs-name"><span class="hljs-built_in">&lt;</span></span> x <span class="hljs-number">0</span>) <span class="hljs-symbol">&#x27;negative</span>)
      (<span class="hljs-name"><span class="hljs-built_in">else</span></span> <span class="hljs-symbol">&#x27;zero</span>))
(<span class="hljs-name"><span class="hljs-built_in">case</span></span> (<span class="hljs-name"><span class="hljs-built_in">*</span></span> <span class="hljs-number">2</span> <span class="hljs-number">3</span>)
  ((<span class="hljs-name">2</span> <span class="hljs-number">3</span> <span class="hljs-number">5</span> <span class="hljs-number">7</span>) <span class="hljs-symbol">&#x27;prime</span>)
  ((<span class="hljs-name">1</span> <span class="hljs-number">4</span> <span class="hljs-number">6</span> <span class="hljs-number">8</span> <span class="hljs-number">9</span>) <span class="hljs-symbol">&#x27;composite</span>))
(<span class="hljs-name"><span class="hljs-built_in">when</span></span> (<span class="hljs-name"><span class="hljs-built_in">pair?</span></span> x) (<span class="hljs-name"><span class="hljs-built_in">display</span></span> (<span class="hljs-name"><span class="hljs-built_in">car</span></span> x)))
(<span class="hljs-name"><span class="hljs-built_in">unless</span></span> (<span class="hljs-name"><span class="hljs-built_in">null?</span></span> x) (<span class="hljs-name"><span class="hljs-built_in">display</span></span> (<span class="hljs-name"><span class="hljs-built_in">cdr</span></span> x)))
""");
    }

    [Fact]
    public void LambdaInsideAName()
    {
        AssertHighlighter("scheme",
"""
(define (my-lambda-helper x) x)
(case-lambda ((x) x) ((x y) y))
(display "lambda")
""",
"""
(<span class="hljs-name"><span class="hljs-built_in">define</span></span> (<span class="hljs-name">my-lambda-helper</span> x) x)
(<span class="hljs-name"><span class="hljs-built_in">case-lambda</span></span> ((<span class="hljs-name">x</span>) x) ((<span class="hljs-name">x</span> y) y))
(<span class="hljs-name"><span class="hljs-built_in">display</span></span> <span class="hljs-string">&quot;lambda&quot;</span>)
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("scheme",
"""
(define (λ-test été) (string-append "héllo" "✓"))
""",
"""
(<span class="hljs-name"><span class="hljs-built_in">define</span></span> (<span class="hljs-name">λ-test</span> été) (<span class="hljs-name"><span class="hljs-built_in">string-append</span></span> <span class="hljs-string">&quot;héllo&quot;</span> <span class="hljs-string">&quot;✓&quot;</span>))
""");
    }

    [Fact]
    public void UnbalancedParentheses()
    {
        AssertHighlighter("scheme",
"""
(define x 1))
(define y 2)
]
""",
"""
(<span class="hljs-name"><span class="hljs-built_in">define</span></span> x <span class="hljs-number">1</span>))
(<span class="hljs-name"><span class="hljs-built_in">define</span></span> y <span class="hljs-number">2</span>)
]
""");
    }
}
