namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class ClojureHighlighterTests
{
    [Fact]
    public void NsDecl()
    {
        AssertHighlighter("clojure",
"""
(ns my-app.core
  (:require [clojure.string :as str]
            [clojure.set :refer [union]])
  (:import (java.util Date UUID))
  (:gen-class))
""",
"""
(<span class="hljs-name"><span class="hljs-built_in">ns</span></span> my-app.core
  (<span class="hljs-symbol">:require</span> [clojure.string <span class="hljs-symbol">:as</span> str]
            [clojure.set <span class="hljs-symbol">:refer</span> [union]])
  (<span class="hljs-symbol">:import</span> (<span class="hljs-name">java.util</span> Date UUID))
  (<span class="hljs-symbol">:gen-class</span>))
""");
    }

    [Fact]
    public void Defn()
    {
        AssertHighlighter("clojure",
"""
(defn greet
  "Returns a greeting."
  [name]
  (str "Hello, " name "!"))

(defn- private-helper [x] (* x 2))

(defn multi-arity
  ([] (multi-arity 1))
  ([x] (inc x)))
""",
"""
(<span class="hljs-keyword">defn</span> <span class="hljs-title">greet</span>
  <span class="hljs-string">&quot;Returns a greeting.&quot;</span>
  [name]
  (<span class="hljs-name"><span class="hljs-built_in">str</span></span> <span class="hljs-string">&quot;Hello, &quot;</span> name <span class="hljs-string">&quot;!&quot;</span>))

(<span class="hljs-keyword">defn-</span> <span class="hljs-title">private-helper</span> [x] (<span class="hljs-name"><span class="hljs-built_in">*</span></span> x <span class="hljs-number">2</span>))

(<span class="hljs-keyword">defn</span> <span class="hljs-title">multi-arity</span>
  ([] (<span class="hljs-name">multi-arity</span> <span class="hljs-number">1</span>))
  ([x] (<span class="hljs-name"><span class="hljs-built_in">inc</span></span> x)))
""");
    }

    [Fact]
    public void DefValues()
    {
        AssertHighlighter("clojure",
"""
(def pi 3.14159)
(defonce state (atom {}))
(def ^:private secret "s")
(def ^{:doc "meta"} documented 1)
""",
"""
(<span class="hljs-keyword">def</span> <span class="hljs-title">pi</span> <span class="hljs-number">3.14159</span>)
(<span class="hljs-keyword">defonce</span> <span class="hljs-title">state</span> (<span class="hljs-name"><span class="hljs-built_in">atom</span></span> {}))
(<span class="hljs-keyword">def</span> ^<span class="hljs-symbol">:private</span> <span class="hljs-title">secret</span> <span class="hljs-string">&quot;s&quot;</span>)
(<span class="hljs-keyword">def</span> ^{<span class="hljs-symbol">:doc</span> <span class="hljs-string">&quot;meta&quot;</span>} <span class="hljs-title">documented</span> <span class="hljs-number">1</span>)
""");
    }

    [Fact]
    public void LetBinding()
    {
        AssertHighlighter("clojure",
"""
(let [a 1
      b (+ a 2)
      {:keys [x y] :or {x 0}} {:x 5}
      [first-item & rest-items] [1 2 3]]
  (println a b x y first-item rest-items))
""",
"""
(<span class="hljs-name"><span class="hljs-built_in">let</span></span> [a <span class="hljs-number">1</span>
      b (<span class="hljs-name"><span class="hljs-built_in">+</span></span> a <span class="hljs-number">2</span>)
      {<span class="hljs-symbol">:keys</span> [x y] <span class="hljs-symbol">:or</span> {x <span class="hljs-number">0</span>}} {<span class="hljs-symbol">:x</span> <span class="hljs-number">5</span>}
      [first-item &amp; rest-items] [<span class="hljs-number">1</span> <span class="hljs-number">2</span> <span class="hljs-number">3</span>]]
  (<span class="hljs-name">println</span> a b x y first-item rest-items))
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("clojure",
"""
42
-17
+5
3.14
1.5e10
4.2E-1M
42M
42N
0x2A
052
2r101010
36rZZ
1/2
-3/4
0
0.0
""",
"""
<span class="hljs-number">42</span>
<span class="hljs-number">-17</span>
<span class="hljs-number">+5</span>
<span class="hljs-number">3.14</span>
<span class="hljs-number">1.5e10</span>
<span class="hljs-number">4.2E-1M</span>
<span class="hljs-number">42M</span>
<span class="hljs-number">42N</span>
<span class="hljs-number">0x2A</span>
<span class="hljs-number">052</span>
<span class="hljs-number">2r101010</span>
<span class="hljs-number">36rZZ</span>
<span class="hljs-number">1/2</span>
<span class="hljs-number">-3/4</span>
<span class="hljs-number">0</span>
<span class="hljs-number">0.0</span>
""");
    }

    [Fact]
    public void StringsChars()
    {
        AssertHighlighter("clojure",
"""
"hello"
"escape \" quote \n newline"
"multi
line string"
\a
\newline
\space
\tab
\u00E9
\o377
\\
#"regex\d+"
#"[a-z]+\.\w*"
""",
"""
<span class="hljs-string">&quot;hello&quot;</span>
<span class="hljs-string">&quot;escape \&quot; quote \n newline&quot;</span>
<span class="hljs-string">&quot;multi
line string&quot;</span>
<span class="hljs-character">\a</span>
<span class="hljs-character">\newline</span>
<span class="hljs-character">\space</span>
<span class="hljs-character">\tab</span>
<span class="hljs-character">\u00E9</span>
<span class="hljs-character">\o377</span>
<span class="hljs-character">\\</span>
<span class="hljs-regex">#&quot;regex\d+&quot;</span>
<span class="hljs-regex">#&quot;[a-z]+\.\w*&quot;</span>
""");
    }

    [Fact]
    public void KeywordsSymbols()
    {
        AssertHighlighter("clojure",
"""
:keyword
::namespaced-keyword
:ns/qualified
:a.b/c
'symbol
'(1 2 3)
my-var
nil
true
false
""",
"""
<span class="hljs-symbol">:keyword</span>
<span class="hljs-symbol">::namespaced-keyword</span>
<span class="hljs-symbol">:ns/qualified</span>
<span class="hljs-symbol">:a.b/c</span>
&#x27;symbol
&#x27;(<span class="hljs-number">1</span> <span class="hljs-number">2</span> <span class="hljs-number">3</span>)
my-var
<span class="hljs-literal">nil</span>
<span class="hljs-literal">true</span>
<span class="hljs-literal">false</span>
""");
    }

    [Fact]
    public void Collections()
    {
        AssertHighlighter("clojure",
"""
[1 2 3]
{:a 1, :b 2}
#{1 2 3}
'(a b c)
#:person{:name "Jane" :age 30}
#::{:local 1}
{:nested {:map [1 {:deep true}]}}
""",
"""
[<span class="hljs-number">1</span> <span class="hljs-number">2</span> <span class="hljs-number">3</span>]
{<span class="hljs-symbol">:a</span> <span class="hljs-number">1</span><span class="hljs-punctuation">,</span> <span class="hljs-symbol">:b</span> <span class="hljs-number">2</span>}
#{<span class="hljs-number">1</span> <span class="hljs-number">2</span> <span class="hljs-number">3</span>}
&#x27;(<span class="hljs-name">a</span> b c)
#:person{<span class="hljs-symbol">:name</span> <span class="hljs-string">&quot;Jane&quot;</span> <span class="hljs-symbol">:age</span> <span class="hljs-number">30</span>}
#::{<span class="hljs-symbol">:local</span> <span class="hljs-number">1</span>}
{<span class="hljs-symbol">:nested</span> {<span class="hljs-symbol">:map</span> [<span class="hljs-number">1</span> {<span class="hljs-symbol">:deep</span> <span class="hljs-literal">true</span>}]}}
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("clojure",
"""
; A comment
;; Double semicolon
;;; Triple
(+ 1 2) ; trailing
#_(ignored form)
(comment
  (do-something))
; TODO: fix this
""",
"""
<span class="hljs-comment">; A comment</span>
<span class="hljs-comment">;; Double semicolon</span>
<span class="hljs-comment">;;; Triple</span>
(<span class="hljs-name"><span class="hljs-built_in">+</span></span> <span class="hljs-number">1</span> <span class="hljs-number">2</span>) <span class="hljs-comment">; trailing</span>
#_(<span class="hljs-name">ignored</span> form)
(<span class="hljs-name">comment</span>
  (<span class="hljs-name">do-something</span>))
<span class="hljs-comment">; <span class="hljs-doctag">TODO:</span> fix this</span>
""");
    }

    [Fact]
    public void Threading()
    {
        AssertHighlighter("clojure",
"""
(->> (range 10)
     (filter even?)
     (map #(* % %))
     (reduce +))

(-> {:a 1}
    (assoc :b 2)
    (update :a inc))

(some-> x .toString str/upper-case)
(cond-> x true inc)
""",
"""
(<span class="hljs-name"><span class="hljs-built_in">-&gt;&gt;</span></span> (<span class="hljs-name"><span class="hljs-built_in">range</span></span> <span class="hljs-number">10</span>)
     (<span class="hljs-name"><span class="hljs-built_in">filter</span></span> even?)
     (<span class="hljs-name"><span class="hljs-built_in">map</span></span> #(<span class="hljs-name"><span class="hljs-built_in">*</span></span> % %))
     (<span class="hljs-name"><span class="hljs-built_in">reduce</span></span> +))

(<span class="hljs-name"><span class="hljs-built_in">-&gt;</span></span> {<span class="hljs-symbol">:a</span> <span class="hljs-number">1</span>}
    (<span class="hljs-name"><span class="hljs-built_in">assoc</span></span> <span class="hljs-symbol">:b</span> <span class="hljs-number">2</span>)
    (<span class="hljs-name">update</span> <span class="hljs-symbol">:a</span> inc))

(<span class="hljs-name">some-&gt;</span> x .toString str/upper-case)
(<span class="hljs-name">cond-&gt;</span> x <span class="hljs-literal">true</span> inc)
""");
    }

    [Fact]
    public void AnonymousFn()
    {
        AssertHighlighter("clojure",
"""
(map #(+ %1 %2) [1 2] [3 4])
(filter (fn [x] (> x 0)) xs)
(fn named [a] a)
""",
"""
(<span class="hljs-name"><span class="hljs-built_in">map</span></span> #(<span class="hljs-name"><span class="hljs-built_in">+</span></span> %<span class="hljs-number">1</span> %<span class="hljs-number">2</span>) [<span class="hljs-number">1</span> <span class="hljs-number">2</span>] [<span class="hljs-number">3</span> <span class="hljs-number">4</span>])
(<span class="hljs-name"><span class="hljs-built_in">filter</span></span> (<span class="hljs-name"><span class="hljs-built_in">fn</span></span> [x] (<span class="hljs-name"><span class="hljs-built_in">&gt;</span></span> x <span class="hljs-number">0</span>)) xs)
(<span class="hljs-name"><span class="hljs-built_in">fn</span></span> named [a] a)
""");
    }

    [Fact]
    public void ProtocolsRecords()
    {
        AssertHighlighter("clojure",
"""
(defprotocol Shape
  (area [this])
  (perimeter [this]))

(defrecord Circle [radius]
  Shape
  (area [_] (* Math/PI radius radius))
  (perimeter [_] (* 2 Math/PI radius)))

(deftype Point [x y])

(extend-protocol Shape
  String
  (area [s] (count s)))
""",
"""
(<span class="hljs-keyword">defprotocol</span> <span class="hljs-title">Shape</span>
  (<span class="hljs-name">area</span> [this])
  (<span class="hljs-name">perimeter</span> [this]))

(<span class="hljs-keyword">defrecord</span> <span class="hljs-title">Circle</span> [radius]
  Shape
  (<span class="hljs-name">area</span> [_] (<span class="hljs-name"><span class="hljs-built_in">*</span></span> Math/PI radius radius))
  (<span class="hljs-name">perimeter</span> [_] (<span class="hljs-name"><span class="hljs-built_in">*</span></span> <span class="hljs-number">2</span> Math/PI radius)))

(<span class="hljs-keyword">deftype</span> <span class="hljs-title">Point</span> [x y])

(<span class="hljs-name"><span class="hljs-built_in">extend-protocol</span></span> Shape
  String
  (<span class="hljs-name">area</span> [s] (<span class="hljs-name"><span class="hljs-built_in">count</span></span> s)))
""");
    }

    [Fact]
    public void Multimethods()
    {
        AssertHighlighter("clojure",
"""
(defmulti area :shape)
(defmethod area :circle [{:keys [r]}] (* 3.14 r r))
(defmethod area :default [_] 0)
""",
"""
(<span class="hljs-keyword">defmulti</span> <span class="hljs-title">area</span> <span class="hljs-symbol">:shape</span>)
(<span class="hljs-keyword">defmethod</span> <span class="hljs-title">area</span> <span class="hljs-symbol">:circle</span> [{<span class="hljs-symbol">:keys</span> [r]}] (<span class="hljs-name"><span class="hljs-built_in">*</span></span> <span class="hljs-number">3.14</span> r r))
(<span class="hljs-keyword">defmethod</span> <span class="hljs-title">area</span> <span class="hljs-symbol">:default</span> [_] <span class="hljs-number">0</span>)
""");
    }

    [Fact]
    public void Macros()
    {
        AssertHighlighter("clojure",
"""
(defmacro unless [pred & body]
  `(if (not ~pred) (do ~@body)))

(defmacro with-timing [& body]
  `(let [start# (System/nanoTime)]
     ~@body))
""",
"""
(<span class="hljs-keyword">defmacro</span> <span class="hljs-title">unless</span> [pred &amp; body]
  `(<span class="hljs-name"><span class="hljs-built_in">if</span></span> (<span class="hljs-name"><span class="hljs-built_in">not</span></span> ~pred) (<span class="hljs-name"><span class="hljs-built_in">do</span></span> ~@body)))

(<span class="hljs-keyword">defmacro</span> <span class="hljs-title">with-timing</span> [&amp; body]
  `(<span class="hljs-name"><span class="hljs-built_in">let</span></span> [start# (<span class="hljs-name">System/nanoTime</span>)]
     ~@body))
""");
    }

    [Fact]
    public void JavaInterop()
    {
        AssertHighlighter("clojure",
"""
(.toUpperCase "hello")
(Math/abs -5)
(new java.util.Date)
(java.util.Date.)
(.. System (getProperties) (get "os.name"))
(doto (java.util.ArrayList.) (.add 1) (.add 2))
""",
"""
(<span class="hljs-name">.toUpperCase</span> <span class="hljs-string">&quot;hello&quot;</span>)
(<span class="hljs-name">Math/abs</span> <span class="hljs-number">-5</span>)
(<span class="hljs-name"><span class="hljs-built_in">new</span></span> java.util.Date)
(<span class="hljs-name">java.util.Date.</span>)
(<span class="hljs-name"><span class="hljs-built_in">..</span></span> System (<span class="hljs-name">getProperties</span>) (<span class="hljs-name"><span class="hljs-built_in">get</span></span> <span class="hljs-string">&quot;os.name&quot;</span>))
(<span class="hljs-name"><span class="hljs-built_in">doto</span></span> (<span class="hljs-name">java.util.ArrayList.</span>) (<span class="hljs-name">.add</span> <span class="hljs-number">1</span>) (<span class="hljs-name">.add</span> <span class="hljs-number">2</span>))
""");
    }

    [Fact]
    public void ControlFlow()
    {
        AssertHighlighter("clojure",
"""
(if (pos? x) "positive" "non-positive")
(when-not (empty? coll) (first coll))
(cond
  (< x 0) :negative
  (= x 0) :zero
  :else :positive)
(case x
  1 "one"
  2 "two"
  "many")
(loop [i 0 acc []]
  (if (< i 5)
    (recur (inc i) (conj acc i))
    acc))
""",
"""
(<span class="hljs-name"><span class="hljs-built_in">if</span></span> (<span class="hljs-name"><span class="hljs-built_in">pos?</span></span> x) <span class="hljs-string">&quot;positive&quot;</span> <span class="hljs-string">&quot;non-positive&quot;</span>)
(<span class="hljs-name"><span class="hljs-built_in">when-not</span></span> (<span class="hljs-name"><span class="hljs-built_in">empty?</span></span> coll) (<span class="hljs-name"><span class="hljs-built_in">first</span></span> coll))
(<span class="hljs-name"><span class="hljs-built_in">cond</span></span>
  (<span class="hljs-name"><span class="hljs-built_in">&lt;</span></span> x <span class="hljs-number">0</span>) <span class="hljs-symbol">:negative</span>
  (<span class="hljs-name"><span class="hljs-built_in">=</span></span> x <span class="hljs-number">0</span>) <span class="hljs-symbol">:zero</span>
  <span class="hljs-symbol">:else</span> <span class="hljs-symbol">:positive</span>)
(<span class="hljs-name"><span class="hljs-built_in">case</span></span> x
  <span class="hljs-number">1</span> <span class="hljs-string">&quot;one&quot;</span>
  <span class="hljs-number">2</span> <span class="hljs-string">&quot;two&quot;</span>
  <span class="hljs-string">&quot;many&quot;</span>)
(<span class="hljs-name"><span class="hljs-built_in">loop</span></span> [i <span class="hljs-number">0</span> acc []]
  (<span class="hljs-name"><span class="hljs-built_in">if</span></span> (<span class="hljs-name"><span class="hljs-built_in">&lt;</span></span> i <span class="hljs-number">5</span>)
    (<span class="hljs-name"><span class="hljs-built_in">recur</span></span> (<span class="hljs-name"><span class="hljs-built_in">inc</span></span> i) (<span class="hljs-name"><span class="hljs-built_in">conj</span></span> acc i))
    acc))
""");
    }

    [Fact]
    public void AtomsRefs()
    {
        AssertHighlighter("clojure",
"""
(def counter (atom 0))
(swap! counter inc)
(reset! counter 10)
@counter
(deref counter)
(dosync (alter account + 100))
""",
"""
(<span class="hljs-keyword">def</span> <span class="hljs-title">counter</span> (<span class="hljs-name"><span class="hljs-built_in">atom</span></span> <span class="hljs-number">0</span>))
(<span class="hljs-name"><span class="hljs-built_in">swap!</span></span> counter inc)
(<span class="hljs-name"><span class="hljs-built_in">reset!</span></span> counter <span class="hljs-number">10</span>)
@counter
(<span class="hljs-name"><span class="hljs-built_in">deref</span></span> counter)
(<span class="hljs-name"><span class="hljs-built_in">dosync</span></span> (<span class="hljs-name"><span class="hljs-built_in">alter</span></span> account + <span class="hljs-number">100</span>))
""");
    }

    [Fact]
    public void Metadata()
    {
        AssertHighlighter("clojure",
"""
(defn ^String to-str [^long x] (str x))
(def ^:dynamic *debug* false)
(binding [*debug* true] (run))
""",
"""
(<span class="hljs-keyword">defn</span> ^String <span class="hljs-title">to-str</span> [^long x] (<span class="hljs-name"><span class="hljs-built_in">str</span></span> x))
(<span class="hljs-keyword">def</span> ^<span class="hljs-symbol">:dynamic</span> <span class="hljs-title">*debug*</span> <span class="hljs-literal">false</span>)
(<span class="hljs-name">binding</span> [*debug* <span class="hljs-literal">true</span>] (<span class="hljs-name">run</span>))
""");
    }

    [Fact]
    public void TypeHints()
    {
        AssertHighlighter("clojure",
"""
(defn ^long square ^long [^long x] (* x x))
(def ^java.util.Map cache (java.util.HashMap.))
(defn ^{:tag String :private true} fmt [x] (str x))
""",
"""
(<span class="hljs-keyword">defn</span> ^long <span class="hljs-title">square</span> ^long [^long x] (<span class="hljs-name"><span class="hljs-built_in">*</span></span> x x))
(<span class="hljs-keyword">def</span> ^java.util.Map <span class="hljs-title">cache</span> (<span class="hljs-name">java.util.HashMap.</span>))
(<span class="hljs-keyword">defn</span> ^{<span class="hljs-symbol">:tag</span> String <span class="hljs-symbol">:private</span> <span class="hljs-literal">true</span>} <span class="hljs-title">fmt</span> [x] (<span class="hljs-name"><span class="hljs-built_in">str</span></span> x))
""");
    }

    [Fact]
    public void TryCatch()
    {
        AssertHighlighter("clojure",
"""
(try
  (/ 1 0)
  (catch ArithmeticException e
    (println "Error:" (.getMessage e)))
  (finally
    (println "done")))
""",
"""
(<span class="hljs-name"><span class="hljs-built_in">try</span></span>
  (/ <span class="hljs-number">1</span> <span class="hljs-number">0</span>)
  (<span class="hljs-name">catch</span> ArithmeticException e
    (<span class="hljs-name">println</span> <span class="hljs-string">&quot;Error:&quot;</span> (<span class="hljs-name">.getMessage</span> e)))
  (<span class="hljs-name">finally</span>
    (<span class="hljs-name">println</span> <span class="hljs-string">&quot;done&quot;</span>)))
""");
    }

    [Fact]
    public void ForDoseq()
    {
        AssertHighlighter("clojure",
"""
(for [x (range 3) y (range 3) :when (not= x y)] [x y])
(doseq [item items] (println item))
(dotimes [n 5] (print n))
""",
"""
(<span class="hljs-name"><span class="hljs-built_in">for</span></span> [x (<span class="hljs-name"><span class="hljs-built_in">range</span></span> <span class="hljs-number">3</span>) y (<span class="hljs-name"><span class="hljs-built_in">range</span></span> <span class="hljs-number">3</span>) <span class="hljs-symbol">:when</span> (<span class="hljs-name"><span class="hljs-built_in">not=</span></span> x y)] [x y])
(<span class="hljs-name"><span class="hljs-built_in">doseq</span></span> [item items] (<span class="hljs-name">println</span> item))
(<span class="hljs-name"><span class="hljs-built_in">dotimes</span></span> [n <span class="hljs-number">5</span>] (<span class="hljs-name">print</span> n))
""");
    }

    [Fact]
    public void EdnData()
    {
        AssertHighlighter("clojure",
"""
{:name "Widget"
 :price 9.99
 :tags #{"a" "b"}
 :created #inst "2024-01-01T00:00:00Z"
 :id #uuid "f81d4fae-7dec-11d0-a765-00a0c91e6bf6"
 :dims [10 20 30]}
""",
"""
{<span class="hljs-symbol">:name</span> <span class="hljs-string">&quot;Widget&quot;</span>
 <span class="hljs-symbol">:price</span> <span class="hljs-number">9.99</span>
 <span class="hljs-symbol">:tags</span> #{<span class="hljs-string">&quot;a&quot;</span> <span class="hljs-string">&quot;b&quot;</span>}
 <span class="hljs-symbol">:created</span> #inst <span class="hljs-string">&quot;2024-01-01T00:00:00Z&quot;</span>
 <span class="hljs-symbol">:id</span> #uuid <span class="hljs-string">&quot;f81d4fae-7dec-11d0-a765-00a0c91e6bf6&quot;</span>
 <span class="hljs-symbol">:dims</span> [<span class="hljs-number">10</span> <span class="hljs-number">20</span> <span class="hljs-number">30</span>]}
""");
    }

    [Fact]
    public void ReaderConditionals()
    {
        AssertHighlighter("clojure",
"""
#?(:clj (Math/sqrt 4) :cljs (js/Math.sqrt 4))
#?@(:clj [1 2] :cljs [3 4])
""",
"""
#?(<span class="hljs-symbol">:clj</span> (<span class="hljs-name">Math/sqrt</span> <span class="hljs-number">4</span>) <span class="hljs-symbol">:cljs</span> (<span class="hljs-name">js/Math.sqrt</span> <span class="hljs-number">4</span>))
#?@(<span class="hljs-symbol">:clj</span> [<span class="hljs-number">1</span> <span class="hljs-number">2</span>] <span class="hljs-symbol">:cljs</span> [<span class="hljs-number">3</span> <span class="hljs-number">4</span>])
""");
    }

    [Fact]
    public void EmptyForms()
    {
        AssertHighlighter("clojure",
"""
()
(def)
( spaced )
[]
{}
""",
"""
()
(<span class="hljs-keyword">def</span>)
( spaced )
[]
{}
""");
    }

    [Fact]
    public void TopLevelSymbols()
    {
        AssertHighlighter("clojure",
"""
foo
bar/baz
""",
"""
foo
bar/baz
""");
    }

    [Fact]
    public void DestructuringKeys()
    {
        AssertHighlighter("clojure",
"""
(defn handler [{:keys [request-method uri] :as req}]
  {:status 200 :body (str request-method " " uri)})
""",
"""
(<span class="hljs-keyword">defn</span> <span class="hljs-title">handler</span> [{<span class="hljs-symbol">:keys</span> [request-method uri] <span class="hljs-symbol">:as</span> req}]
  {<span class="hljs-symbol">:status</span> <span class="hljs-number">200</span> <span class="hljs-symbol">:body</span> (<span class="hljs-name"><span class="hljs-built_in">str</span></span> request-method <span class="hljs-string">&quot; &quot;</span> uri)})
""");
    }

    [Fact]
    public void CharacterOutsideTheBmp_IsNotSplit()
    {
        AssertHighlighter("clojure",
"""
(str \😀 \a)
""",
"""
(<span class="hljs-name"><span class="hljs-built_in">str</span></span> <span class="hljs-character">\😀</span> <span class="hljs-character">\a</span>)
""");
    }
}
