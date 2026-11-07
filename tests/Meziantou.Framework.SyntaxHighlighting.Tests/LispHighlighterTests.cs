namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class LispHighlighterTests
{    [Fact]
    public void Defun()
    {
        AssertHighlighter("lisp",
"""
(defun factorial (n)
  "Compute the factorial of N."
  (if (<= n 1)
      1
      (* n (factorial (- n 1)))))

(format t "~a~%" (factorial 10))
""",
"""
(<span class="hljs-name">defun</span> factorial (<span class="hljs-name">n</span>)
  <span class="hljs-string">&quot;Compute the factorial of N.&quot;</span>
  (<span class="hljs-name">if</span> (<span class="hljs-name">&lt;=</span> n <span class="hljs-number">1</span>)
      <span class="hljs-number">1</span>
      (<span class="hljs-name">*</span> n (<span class="hljs-name">factorial</span> (<span class="hljs-name">-</span> n <span class="hljs-number">1</span>)))))

(<span class="hljs-name">format</span> <span class="hljs-literal">t</span> <span class="hljs-string">&quot;~a~%&quot;</span> (<span class="hljs-name">factorial</span> <span class="hljs-number">10</span>))
""");
    }

    [Fact]
    public void SpecialVariables()
    {
        AssertHighlighter("lisp",
"""
(defvar *counter* 0)
(defparameter *max-size* 100 "Maximum size.")
(setf *counter* (1+ *counter*))
(let ((x 1) (y 2.5))
  (+ x y))
""",
"""
(<span class="hljs-name">defvar</span> *counter* <span class="hljs-number">0</span>)
(<span class="hljs-name">defparameter</span> *max-size* <span class="hljs-number">100</span> <span class="hljs-string">&quot;Maximum size.&quot;</span>)
(<span class="hljs-name">setf</span> *counter* (<span class="hljs-number">1</span>+ *counter*))
(<span class="hljs-name">let</span> ((<span class="hljs-name">x</span> <span class="hljs-number">1</span>) (<span class="hljs-name">y</span> <span class="hljs-number">2.5</span>))
  (<span class="hljs-name">+</span> x y))
""");
    }

    [Fact]
    public void KeywordsAndLambdaListKeywords()
    {
        AssertHighlighter("common-lisp",
"""
(make-instance 'person :name "Alice" :age 30)
(defun greet (name &optional (greeting "Hello") &key loud &rest args)
  (declare (ignore args))
  (if loud (string-upcase greeting) greeting))
""",
"""
(<span class="hljs-name">make-instance</span> &#x27;person <span class="hljs-symbol">:name</span> <span class="hljs-string">&quot;Alice&quot;</span> <span class="hljs-symbol">:age</span> <span class="hljs-number">30</span>)
(<span class="hljs-name">defun</span> greet (<span class="hljs-name">name</span> <span class="hljs-symbol">&amp;optional</span> (<span class="hljs-name">greeting</span> <span class="hljs-string">&quot;Hello&quot;</span>) <span class="hljs-symbol">&amp;key</span> loud <span class="hljs-symbol">&amp;rest</span> args)
  (<span class="hljs-name">declare</span> (<span class="hljs-name">ignore</span> args))
  (<span class="hljs-name">if</span> loud (<span class="hljs-name">string-upcase</span> greeting) greeting))
""");
    }

    [Fact]
    public void Quoting()
    {
        AssertHighlighter("lisp",
"""
'(1 2 3)
`(a ,b ,@c)
(quote (x y z))
'symbol
#'car
(mapcar #'1+ '(1 2 3))
(funcall #'cl-user::helper 5)
'|quoted symbol with spaces|
(list 'a 'b "c" :d)
""",
"""
&#x27;(<span class="hljs-number">1</span> <span class="hljs-number">2</span> <span class="hljs-number">3</span>)
`(a ,b ,@c)
(<span class="hljs-name">quote</span> (x y z))
&#x27;symbol
#&#x27;car
(<span class="hljs-name">mapcar</span> #&#x27;<span class="hljs-number">1</span>+ &#x27;(<span class="hljs-number">1</span> <span class="hljs-number">2</span> <span class="hljs-number">3</span>))
(<span class="hljs-name">funcall</span> #&#x27;cl-user::helper <span class="hljs-number">5</span>)
&#x27;|quoted symbol with spaces|
(<span class="hljs-name">list</span> &#x27;a &#x27;b <span class="hljs-string">&quot;c&quot;</span> <span class="hljs-symbol">:d</span>)
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("lisp",
"""
(list 42 -17 +3 3.14 1/2 6.02e23 1.5d0 2.0f-3 #b1010 #o777 #xFF #XdeadBEEF #c(1 2) #C(1.5 -2))
""",
"""
(<span class="hljs-name">list</span> <span class="hljs-number">42</span> <span class="hljs-number">-17</span> <span class="hljs-number">+3</span> <span class="hljs-number">3.14</span> <span class="hljs-number">1/2</span> <span class="hljs-number">6.02e23</span> <span class="hljs-number">1.5d0</span> <span class="hljs-number">2.0f-3</span> <span class="hljs-number">#b1010</span> <span class="hljs-number">#o777</span> <span class="hljs-number">#xFF</span> <span class="hljs-number">#XdeadBEEF</span> <span class="hljs-number">#c(1 2)</span> <span class="hljs-number">#C(1.5 -2)</span>)
""");
    }

    [Fact]
    public void Literals()
    {
        AssertHighlighter("lisp",
"""
(if t nil (list t nil))
(null nil)
(cons 'tee 'nilly)
""",
"""
(<span class="hljs-name">if</span> <span class="hljs-literal">t</span> <span class="hljs-literal">nil</span> (<span class="hljs-name">list</span> <span class="hljs-literal">t</span> <span class="hljs-literal">nil</span>))
(<span class="hljs-name">null</span> <span class="hljs-literal">nil</span>)
(<span class="hljs-name">cons</span> &#x27;tee &#x27;nilly)
""");
    }

    [Fact]
    public void Loop()
    {
        AssertHighlighter("lisp",
"""
(loop for i from 1 to 10
      when (evenp i)
        collect (* i i) into squares
      finally (return squares))
(dolist (item '(a b c))
  (print item))
""",
"""
(<span class="hljs-name">loop</span> for i from <span class="hljs-number">1</span> to <span class="hljs-number">10</span>
      when (<span class="hljs-name">evenp</span> i)
        collect (<span class="hljs-name">*</span> i i) into squares
      finally (<span class="hljs-name">return</span> squares))
(<span class="hljs-name">dolist</span> (<span class="hljs-name">item</span> &#x27;(a b c))
  (<span class="hljs-name">print</span> item))
""");
    }

    [Fact]
    public void Clos()
    {
        AssertHighlighter("lisp",
"""
(defclass point ()
  ((x :initarg :x :accessor point-x)
   (y :initarg :y :accessor point-y)))

(defmethod distance ((p point))
  (sqrt (+ (expt (point-x p) 2) (expt (point-y p) 2))))

(defgeneric area (shape)
  (:documentation "Area of SHAPE."))
""",
"""
(<span class="hljs-name">defclass</span> point ()
  ((<span class="hljs-name">x</span> <span class="hljs-symbol">:initarg</span> <span class="hljs-symbol">:x</span> <span class="hljs-symbol">:accessor</span> point-x)
   (<span class="hljs-name">y</span> <span class="hljs-symbol">:initarg</span> <span class="hljs-symbol">:y</span> <span class="hljs-symbol">:accessor</span> point-y)))

(<span class="hljs-name">defmethod</span> distance ((<span class="hljs-name">p</span> point))
  (<span class="hljs-name">sqrt</span> (<span class="hljs-name">+</span> (<span class="hljs-name">expt</span> (<span class="hljs-name">point-x</span> p) <span class="hljs-number">2</span>) (<span class="hljs-name">expt</span> (<span class="hljs-name">point-y</span> p) <span class="hljs-number">2</span>))))

(<span class="hljs-name">defgeneric</span> area (<span class="hljs-name">shape</span>)
  (<span class="hljs-symbol">:documentation</span> <span class="hljs-string">&quot;Area of SHAPE.&quot;</span>))
""");
    }

    [Fact]
    public void Macro()
    {
        AssertHighlighter("lisp",
"""
(defmacro with-timing (&body body)
  `(let ((start (get-internal-real-time)))
     (prog1 (progn ,@body)
       (format t "~d ms~%" (- (get-internal-real-time) start)))))
""",
"""
(<span class="hljs-name">defmacro</span> with-timing (<span class="hljs-name">&amp;body</span> body)
  `(let ((start (get-internal-real-time)))
     (prog1 (progn ,@body)
       (format <span class="hljs-literal">t</span> <span class="hljs-string">&quot;~d ms~%&quot;</span> (- (get-internal-real-time) start)))))
""");
    }

    [Fact]
    public void EmacsLisp()
    {
        AssertHighlighter("elisp",
"""
;;; init.el --- Emacs configuration -*- lexical-binding: t -*-
;; TODO: clean this up
(require 'package)
(setq inhibit-startup-screen t)
(add-hook 'prog-mode-hook #'display-line-numbers-mode)
(global-set-key (kbd "C-c g") 'magit-status)
(use-package org
  :ensure t
  :config
  (setq org-log-done 'time))
(defun my/insert-date ()
  (interactive)
  (insert (format-time-string "%Y-%m-%d")))
""",
"""
<span class="hljs-comment">;;; init.el --- Emacs configuration -*- lexical-binding: t -*-</span>
<span class="hljs-comment">;; <span class="hljs-doctag">TODO:</span> clean this up</span>
(<span class="hljs-name">require</span> &#x27;package)
(<span class="hljs-name">setq</span> inhibit-startup-screen <span class="hljs-literal">t</span>)
(<span class="hljs-name">add-hook</span> &#x27;prog-mode-hook #&#x27;display-line-numbers-mode)
(<span class="hljs-name">global-set-key</span> (<span class="hljs-name">kbd</span> <span class="hljs-string">&quot;C-c g&quot;</span>) &#x27;magit-status)
(<span class="hljs-name">use-package</span> org
  <span class="hljs-symbol">:ensure</span> <span class="hljs-literal">t</span>
  <span class="hljs-symbol">:config</span>
  (<span class="hljs-name">setq</span> org-log-done &#x27;time))
(<span class="hljs-name">defun</span> my/insert-date ()
  (<span class="hljs-name">interactive</span>)
  (<span class="hljs-name">insert</span> (<span class="hljs-name">format-time-string</span> <span class="hljs-string">&quot;%Y-%m-%d&quot;</span>)))
""");
    }

    [Fact]
    public void Comments_BlockComment()
    {
        AssertHighlighter("lisp",
"""
; one
;; two
;;; three
(foo) ; trailing
#| block comment |#
(bar)
""",
"""
<span class="hljs-comment">; one</span>
<span class="hljs-comment">;; two</span>
<span class="hljs-comment">;;; three</span>
(<span class="hljs-name">foo</span>) <span class="hljs-comment">; trailing</span>
<span class="hljs-comment">#| block comment |#</span>
(<span class="hljs-name">bar</span>)
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("lisp",
"""
(format nil "Hello, ~a!~%" "world")
(concatenate 'string "a \"quoted\" b" "multi
line")
""",
"""
(<span class="hljs-name">format</span> <span class="hljs-literal">nil</span> <span class="hljs-string">&quot;Hello, ~a!~%&quot;</span> <span class="hljs-string">&quot;world&quot;</span>)
(<span class="hljs-name">concatenate</span> &#x27;string <span class="hljs-string">&quot;a \&quot;quoted\&quot; b&quot;</span> <span class="hljs-string">&quot;multi
line&quot;</span>)
""");
    }

    [Fact]
    public void MultipleEscapeSymbols()
    {
        AssertHighlighter("lisp",
"""
(|Foo Bar| 1)
(setq |weird symbol| 2)
(list |a| |b|)
""",
"""
(<span class="hljs-name">|Foo Bar|</span> <span class="hljs-number">1</span>)
(<span class="hljs-name">setq</span> |weird symbol| <span class="hljs-number">2</span>)
(<span class="hljs-name">list</span> |a| |b|)
""");
    }

    [Fact]
    public void Shebang()
    {
        AssertHighlighter("lisp",
"""
#!/usr/bin/sbcl --script
(print "hi")
""",
"""
<span class="hljs-meta">#!/usr/bin/sbcl --script</span>
(<span class="hljs-name">print</span> <span class="hljs-string">&quot;hi&quot;</span>)
""");
    }

    [Fact]
    public void TopLevelAtoms()
    {
        AssertHighlighter("lisp",
"""
foo bar
42
"top"
:key
""",
"""
foo bar
<span class="hljs-number">42</span>
<span class="hljs-string">&quot;top&quot;</span>
:key
""");
    }

    [Fact]
    public void UnterminatedString()
    {
        AssertHighlighter("lisp",
"""
(defun f (x)
  (* x
(print "unterminated
(g 2)

""",
"""
(<span class="hljs-name">defun</span> f (<span class="hljs-name">x</span>)
  (<span class="hljs-name">*</span> x
(<span class="hljs-name">print</span> <span class="hljs-string">&quot;unterminated
(g 2)
</span>
""");
    }

    [Fact]
    public void LoneStar_DoesNotSwallowTheDocument()
    {
        AssertHighlighter("lisp",
"""
(reduce #'* '(1 2 3))
(apply '+ '(1 2))
(* 2 3)
(list 1 * 2)
(foo)
""",
"""
(<span class="hljs-name">reduce</span> #&#x27;* &#x27;(<span class="hljs-number">1</span> <span class="hljs-number">2</span> <span class="hljs-number">3</span>))
(<span class="hljs-name">apply</span> &#x27;+ &#x27;(<span class="hljs-number">1</span> <span class="hljs-number">2</span>))
(<span class="hljs-name">*</span> <span class="hljs-number">2</span> <span class="hljs-number">3</span>)
(<span class="hljs-name">list</span> <span class="hljs-number">1</span> * <span class="hljs-number">2</span>)
(<span class="hljs-name">foo</span>)
""");
    }

    [Fact]
    public void UnterminatedPipe()
    {
        AssertHighlighter("lisp",
"""
(format t "a|b")
(list |unterminated 1 2)
(bar)
""",
"""
(<span class="hljs-name">format</span> <span class="hljs-literal">t</span> <span class="hljs-string">&quot;a|b&quot;</span>)
(<span class="hljs-name">list</span> |unterminated <span class="hljs-number">1</span> <span class="hljs-number">2</span>)
(<span class="hljs-name">bar</span>)
""");
    }

    [Fact]
    public void Characters_BackslashEscapesTheNextCharacter()
    {
        AssertHighlighter("emacs-lisp",
"""
(case c
  (#\( 'open)
  (#\) 'close)
  (#\" 'quote)
  (#\; 'semicolon)
  (#\Space 'space))
(list #\a ?\( ?\" foo\ bar)
(bar "ok")
""",
"""
(<span class="hljs-name">case</span> c
  (<span class="hljs-name">#</span>\( &#x27;open)
  (<span class="hljs-name">#</span>\) &#x27;close)
  (<span class="hljs-name">#</span>\&quot; &#x27;quote)
  (<span class="hljs-name">#</span>\; &#x27;semicolon)
  (<span class="hljs-name">#</span>\Space &#x27;space))
(<span class="hljs-name">list</span> #\a ?\( ?\&quot; foo\ bar)
(<span class="hljs-name">bar</span> <span class="hljs-string">&quot;ok&quot;</span>)
""");
    }

    [Fact]
    public void BlockComments_Nest()
    {
        AssertHighlighter("lisp",
"""
#| outer #| nested |# still comment |#
(defun f () #| inline |# 1)
#| unterminated
(g)

""",
"""
<span class="hljs-comment">#| outer <span class="hljs-comment">#| nested |#</span> still comment |#</span>
(<span class="hljs-name">defun</span> f () <span class="hljs-comment">#| inline |#</span> <span class="hljs-number">1</span>)
<span class="hljs-comment">#| unterminated
(g)
</span>
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("lisp",
"""
(defun grüße (été) (format t "héllo ✓ ~a" été))
""",
"""
(<span class="hljs-name">defun</span> grüße (été) (<span class="hljs-name">format</span> <span class="hljs-literal">t</span> <span class="hljs-string">&quot;héllo ✓ ~a&quot;</span> été))
""");
    }
}
