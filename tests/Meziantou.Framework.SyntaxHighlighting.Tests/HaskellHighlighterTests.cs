namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class HaskellHighlighterTests
{
    [Fact]
    public void ModuleHeader()
    {
        AssertHighlighter("haskell",
"""
{-# LANGUAGE OverloadedStrings #-}
{-# LANGUAGE ScopedTypeVariables, GADTs #-}
{-# OPTIONS_GHC -Wall #-}
module Main (main, Shape(..), area) where

import Data.List (sortBy, foldl')
import qualified Data.Map.Strict as Map
import Data.Maybe hiding (fromJust)
import Control.Monad
""",
"""
<span class="hljs-meta">{-# LANGUAGE OverloadedStrings #-}</span>
<span class="hljs-meta">{-# LANGUAGE ScopedTypeVariables, GADTs #-}</span>
<span class="hljs-meta">{-# OPTIONS_GHC -Wall #-}</span>
<span class="hljs-keyword">module</span> Main (<span class="hljs-title">main</span>, <span class="hljs-type">Shape(..)</span>, <span class="hljs-title">area</span>) <span class="hljs-keyword">where</span>

<span class="hljs-keyword">import</span> Data.List (<span class="hljs-title">sortBy</span>, <span class="hljs-title">foldl&#x27;</span>)
<span class="hljs-keyword">import</span> <span class="hljs-keyword">qualified</span> Data.Map.Strict <span class="hljs-keyword">as</span> Map
<span class="hljs-keyword">import</span> Data.Maybe <span class="hljs-keyword">hiding</span> (<span class="hljs-title">fromJust</span>)
<span class="hljs-keyword">import</span> Control.Monad
""");
    }

    [Fact]
    public void TypeSignatures()
    {
        AssertHighlighter("haskell",
"""
main :: IO ()
main = putStrLn "Hello, World!"

add :: Int -> Int -> Int
add x y = x + y

map' :: (a -> b) -> [a] -> [b]
map' _ [] = []
map' f (x:xs) = f x : map' f xs
""",
"""
<span class="hljs-title">main</span> :: <span class="hljs-type">IO</span> ()
<span class="hljs-title">main</span> = putStrLn <span class="hljs-string">&quot;Hello, World!&quot;</span>

<span class="hljs-title">add</span> :: <span class="hljs-type">Int</span> -&gt; <span class="hljs-type">Int</span> -&gt; <span class="hljs-type">Int</span>
<span class="hljs-title">add</span> x y = x + y

<span class="hljs-title">map&#x27;</span> :: (a -&gt; b) -&gt; [a] -&gt; [b]
<span class="hljs-title">map&#x27;</span> _ [] = []
<span class="hljs-title">map&#x27;</span> f (x:xs) = f x : map&#x27; f xs
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("haskell",
"""
-- A line comment
--- Triple dash comment
x = 1 -- trailing comment
{- A block comment -}
{- Nested {- inner -} still comment -}
{-
  Multi-line
  {- nested
     {- deeper -}
  -}
-}
-- TODO: fix this
y = 2
""",
"""
<span class="hljs-comment">-- A line comment</span>
<span class="hljs-comment">--- Triple dash comment</span>
<span class="hljs-title">x</span> = <span class="hljs-number">1</span> <span class="hljs-comment">-- trailing comment</span>
<span class="hljs-comment">{- A block comment -}</span>
<span class="hljs-comment">{- Nested <span class="hljs-comment">{- inner -}</span> still comment -}</span>
<span class="hljs-comment">{-
  Multi-line
  <span class="hljs-comment">{- nested
     <span class="hljs-comment">{- deeper -}</span>
  -}</span>
-}</span>
<span class="hljs-comment">-- <span class="hljs-doctag">TODO:</span> fix this</span>
<span class="hljs-title">y</span> = <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void OperatorsWithDashes()
    {
        AssertHighlighter("haskell",
"""
x --> y
a <-- b
(|--) = undefined
c -- comment
d --| haddock
e |-- f
""",
"""
<span class="hljs-title">x</span> --&gt; y
<span class="hljs-title">a</span> &lt;-- b
(|<span class="hljs-comment">--) = undefined</span>
<span class="hljs-title">c</span> <span class="hljs-comment">-- comment</span>
<span class="hljs-title">d</span> <span class="hljs-comment">--| haddock</span>
<span class="hljs-title">e</span> |<span class="hljs-comment">-- f</span>
""");
    }

    [Fact]
    public void Haddock()
    {
        AssertHighlighter("haskell",
"""
-- | The main function.
-- Does things.
main :: IO ()

-- ^ Argument doc
data T = T -- ^ constructor doc
""",
"""
<span class="hljs-comment">-- | The main function.</span>
<span class="hljs-comment">-- Does things.</span>
<span class="hljs-title">main</span> :: <span class="hljs-type">IO</span> ()

<span class="hljs-comment">-- ^ Argument doc</span>
<span class="hljs-class"><span class="hljs-keyword">data</span> <span class="hljs-type">T</span> = <span class="hljs-type">T</span> <span class="hljs-comment">-- ^ constructor doc</span></span>
""");
    }

    [Fact]
    public void WhereLet()
    {
        AssertHighlighter("haskell",
"""
circleArea :: Double -> Double
circleArea r = piVal * r * r
  where
    piVal = 3.14159

compute :: Int -> Int
compute n =
  let a = n * 2
      b = a + 1
  in a + b
""",
"""
<span class="hljs-title">circleArea</span> :: <span class="hljs-type">Double</span> -&gt; <span class="hljs-type">Double</span>
<span class="hljs-title">circleArea</span> r = piVal * r * r
  <span class="hljs-keyword">where</span>
    piVal = <span class="hljs-number">3.14159</span>

<span class="hljs-title">compute</span> :: <span class="hljs-type">Int</span> -&gt; <span class="hljs-type">Int</span>
<span class="hljs-title">compute</span> n =
  <span class="hljs-keyword">let</span> a = n * <span class="hljs-number">2</span>
      b = a + <span class="hljs-number">1</span>
  <span class="hljs-keyword">in</span> a + b
""");
    }

    [Fact]
    public void DataTypes()
    {
        AssertHighlighter("haskell",
"""
data Shape = Circle Double | Rectangle Double Double
  deriving (Show, Eq)

data Person = Person
  { name :: String
  , age  :: Int
  } deriving (Show)

newtype Wrapper a = Wrapper { unwrap :: a }

type Name = String
type family Elem c
""",
"""
<span class="hljs-class"><span class="hljs-keyword">data</span> <span class="hljs-type">Shape</span> = <span class="hljs-type">Circle</span> <span class="hljs-type">Double</span> | <span class="hljs-type">Rectangle</span> <span class="hljs-type">Double</span> <span class="hljs-type">Double</span></span>
  <span class="hljs-keyword">deriving</span> (<span class="hljs-type">Show</span>, <span class="hljs-type">Eq</span>)

<span class="hljs-class"><span class="hljs-keyword">data</span> <span class="hljs-type">Person</span> = <span class="hljs-type">Person</span></span>
  { name :: <span class="hljs-type">String</span>
  , age  :: <span class="hljs-type">Int</span>
  } <span class="hljs-keyword">deriving</span> (<span class="hljs-type">Show</span>)

<span class="hljs-class"><span class="hljs-keyword">newtype</span> <span class="hljs-type">Wrapper</span> a = <span class="hljs-type">Wrapper</span> { <span class="hljs-title">unwrap</span> :: <span class="hljs-title">a</span> }</span>

<span class="hljs-class"><span class="hljs-keyword">type</span> <span class="hljs-type">Name</span> = <span class="hljs-type">String</span></span>
<span class="hljs-class"><span class="hljs-keyword">type</span> <span class="hljs-keyword">family</span> <span class="hljs-type">Elem</span> c</span>
""");
    }

    [Fact]
    public void Typeclasses()
    {
        AssertHighlighter("haskell",
"""
class Show a => Pretty a where
  pretty :: a -> String
  pretty = show

instance Pretty Bool where
  pretty True = "yes"
  pretty False = "no"

class (Eq a, Ord a) => Container f a where
  empty :: f a

instance (Show a) => Pretty (Maybe a) where
  pretty Nothing = "none"
  pretty (Just x) = show x
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-type">Show</span> a =&gt; <span class="hljs-type">Pretty</span> a <span class="hljs-keyword">where</span></span>
  pretty :: a -&gt; <span class="hljs-type">String</span>
  pretty = show
<span class="hljs-class">
<span class="hljs-keyword">instance</span> <span class="hljs-type">Pretty</span> <span class="hljs-type">Bool</span> <span class="hljs-keyword">where</span></span>
  pretty <span class="hljs-type">True</span> = <span class="hljs-string">&quot;yes&quot;</span>
  pretty <span class="hljs-type">False</span> = <span class="hljs-string">&quot;no&quot;</span>
<span class="hljs-class">
<span class="hljs-keyword">class</span> (<span class="hljs-type">Eq</span> <span class="hljs-title">a</span>, <span class="hljs-type">Ord</span> <span class="hljs-title">a</span>) =&gt; <span class="hljs-type">Container</span> f a <span class="hljs-keyword">where</span></span>
  empty :: f a
<span class="hljs-class">
<span class="hljs-keyword">instance</span> (<span class="hljs-type">Show</span> <span class="hljs-title">a</span>) =&gt; <span class="hljs-type">Pretty</span> (<span class="hljs-type">Maybe</span> <span class="hljs-title">a</span>) <span class="hljs-keyword">where</span></span>
  pretty <span class="hljs-type">Nothing</span> = <span class="hljs-string">&quot;none&quot;</span>
  pretty (<span class="hljs-type">Just</span> x) = show x
""");
    }

    [Fact]
    public void CaseGuards()
    {
        AssertHighlighter("haskell",
"""
classify :: Int -> String
classify n
  | n < 0 = "negative"
  | n == 0 = "zero"
  | otherwise = "positive"

describe :: Maybe Int -> String
describe m = case m of
  Just x | x > 10 -> "big"
         | otherwise -> "small"
  Nothing -> "none"
""",
"""
<span class="hljs-title">classify</span> :: <span class="hljs-type">Int</span> -&gt; <span class="hljs-type">String</span>
<span class="hljs-title">classify</span> n
  | n &lt; <span class="hljs-number">0</span> = <span class="hljs-string">&quot;negative&quot;</span>
  | n == <span class="hljs-number">0</span> = <span class="hljs-string">&quot;zero&quot;</span>
  | otherwise = <span class="hljs-string">&quot;positive&quot;</span>

<span class="hljs-title">describe</span> :: <span class="hljs-type">Maybe</span> <span class="hljs-type">Int</span> -&gt; <span class="hljs-type">String</span>
<span class="hljs-title">describe</span> m = <span class="hljs-keyword">case</span> m <span class="hljs-keyword">of</span>
  <span class="hljs-type">Just</span> x | x &gt; <span class="hljs-number">10</span> -&gt; <span class="hljs-string">&quot;big&quot;</span>
         | otherwise -&gt; <span class="hljs-string">&quot;small&quot;</span>
  <span class="hljs-type">Nothing</span> -&gt; <span class="hljs-string">&quot;none&quot;</span>
""");
    }

    [Fact]
    public void DoNotation()
    {
        AssertHighlighter("haskell",
"""
main :: IO ()
main = do
  line <- getLine
  let n = read line :: Int
  if n > 0
    then putStrLn "positive"
    else putStrLn "non-positive"
  mapM_ print [1..n]
  return ()
""",
"""
<span class="hljs-title">main</span> :: <span class="hljs-type">IO</span> ()
<span class="hljs-title">main</span> = <span class="hljs-keyword">do</span>
  line &lt;- getLine
  <span class="hljs-keyword">let</span> n = read line :: <span class="hljs-type">Int</span>
  <span class="hljs-keyword">if</span> n &gt; <span class="hljs-number">0</span>
    <span class="hljs-keyword">then</span> putStrLn <span class="hljs-string">&quot;positive&quot;</span>
    <span class="hljs-keyword">else</span> putStrLn <span class="hljs-string">&quot;non-positive&quot;</span>
  mapM_ print [<span class="hljs-number">1</span>..n]
  return ()
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("haskell",
"""
a = 42
b = 3.14
c = 1.5e10
d = 2.0e-3
e = 0xFF
f = 0XdeadBEEF
g = 0o755
h = 0b1010
i = 1_000_000
j = 0x_ff_ff
k = 0x1.8p3
l = 6.022_140e23
""",
"""
<span class="hljs-title">a</span> = <span class="hljs-number">42</span>
<span class="hljs-title">b</span> = <span class="hljs-number">3.14</span>
<span class="hljs-title">c</span> = <span class="hljs-number">1.5e10</span>
<span class="hljs-title">d</span> = <span class="hljs-number">2.0e-3</span>
<span class="hljs-title">e</span> = <span class="hljs-number">0xFF</span>
<span class="hljs-title">f</span> = <span class="hljs-number">0XdeadBEEF</span>
<span class="hljs-title">g</span> = <span class="hljs-number">0o755</span>
<span class="hljs-title">h</span> = <span class="hljs-number">0b1010</span>
<span class="hljs-title">i</span> = <span class="hljs-number">1_000_000</span>
<span class="hljs-title">j</span> = <span class="hljs-number">0x_ff_ff</span>
<span class="hljs-title">k</span> = <span class="hljs-number">0x1.8p3</span>
<span class="hljs-title">l</span> = <span class="hljs-number">6.022_140e23</span>
""");
    }

    [Fact]
    public void StringsChars()
    {
        AssertHighlighter("haskell",
"""
s = "hello"
t = "escape \" quote \n newline \t tab"
u = "unicode \x41 \1234 \SOH"
c1 = 'a'
c2 = '\n'
c3 = '\''
c4 = '\\'
prime' = x'
f' x' = x' + 1
""",
"""
<span class="hljs-title">s</span> = <span class="hljs-string">&quot;hello&quot;</span>
<span class="hljs-title">t</span> = <span class="hljs-string">&quot;escape \&quot; quote \n newline \t tab&quot;</span>
<span class="hljs-title">u</span> = <span class="hljs-string">&quot;unicode \x41 \1234 \SOH&quot;</span>
<span class="hljs-title">c1</span> = <span class="hljs-string">&#x27;a&#x27;</span>
<span class="hljs-title">c2</span> = <span class="hljs-string">&#x27;<span class="hljs-char escape_">\n</span>&#x27;</span>
<span class="hljs-title">c3</span> = <span class="hljs-string">&#x27;<span class="hljs-char escape_">\&#x27;</span>&#x27;</span>
<span class="hljs-title">c4</span> = <span class="hljs-string">&#x27;<span class="hljs-char escape_">\\</span>&#x27;</span>
<span class="hljs-title">prime&#x27;</span> = x&#x27;
<span class="hljs-title">f&#x27;</span> x&#x27; = x&#x27; + <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Lambdas()
    {
        AssertHighlighter("haskell",
"""
squares = map (\x -> x * x) [1..10]
compose = (.) . (.)
pointFree = sum . filter even . map (*2)
section = (+ 1)
backticks = 10 `div` 3
""",
"""
<span class="hljs-title">squares</span> = map (\x -&gt; x * x) [<span class="hljs-number">1</span>..<span class="hljs-number">10</span>]
<span class="hljs-title">compose</span> = (.) . (.)
<span class="hljs-title">pointFree</span> = sum . filter even . map (*<span class="hljs-number">2</span>)
<span class="hljs-title">section</span> = (+ <span class="hljs-number">1</span>)
<span class="hljs-title">backticks</span> = <span class="hljs-number">10</span> `div` <span class="hljs-number">3</span>
""");
    }

    [Fact]
    public void ListComprehension()
    {
        AssertHighlighter("haskell",
"""
pythag n = [(a, b, c) | c <- [1..n], b <- [1..c], a <- [1..b], a^2 + b^2 == c^2]
evens = [x | x <- [1..], even x]
""",
"""
<span class="hljs-title">pythag</span> n = [(a, b, c) | c &lt;- [<span class="hljs-number">1</span>..n], b &lt;- [<span class="hljs-number">1</span>..c], a &lt;- [<span class="hljs-number">1</span>..b], a^<span class="hljs-number">2</span> + b^<span class="hljs-number">2</span> == c^<span class="hljs-number">2</span>]
<span class="hljs-title">evens</span> = [x | x &lt;- [<span class="hljs-number">1</span>..], even x]
""");
    }

    [Fact]
    public void Gadts()
    {
        AssertHighlighter("haskell",
"""
data Expr a where
  IntE  :: Int -> Expr Int
  BoolE :: Bool -> Expr Bool
  If    :: Expr Bool -> Expr a -> Expr a -> Expr a

eval :: Expr a -> a
eval (IntE n) = n
eval (If c t e) = if eval c then eval t else eval e
""",
"""
<span class="hljs-class"><span class="hljs-keyword">data</span> <span class="hljs-type">Expr</span> a <span class="hljs-keyword">where</span></span>
  <span class="hljs-type">IntE</span>  :: <span class="hljs-type">Int</span> -&gt; <span class="hljs-type">Expr</span> <span class="hljs-type">Int</span>
  <span class="hljs-type">BoolE</span> :: <span class="hljs-type">Bool</span> -&gt; <span class="hljs-type">Expr</span> <span class="hljs-type">Bool</span>
  <span class="hljs-type">If</span>    :: <span class="hljs-type">Expr</span> <span class="hljs-type">Bool</span> -&gt; <span class="hljs-type">Expr</span> a -&gt; <span class="hljs-type">Expr</span> a -&gt; <span class="hljs-type">Expr</span> a

<span class="hljs-title">eval</span> :: <span class="hljs-type">Expr</span> a -&gt; a
<span class="hljs-title">eval</span> (<span class="hljs-type">IntE</span> n) = n
<span class="hljs-title">eval</span> (<span class="hljs-type">If</span> c t e) = <span class="hljs-keyword">if</span> eval c <span class="hljs-keyword">then</span> eval t <span class="hljs-keyword">else</span> eval e
""");
    }

    [Fact]
    public void ForallType()
    {
        AssertHighlighter("haskell",
"""
{-# LANGUAGE RankNTypes #-}
applyAll :: forall a. (forall b. b -> b) -> a -> a
applyAll f x = f x
""",
"""
<span class="hljs-meta">{-# LANGUAGE RankNTypes #-}</span>
<span class="hljs-title">applyAll</span> :: <span class="hljs-keyword">forall</span> a. (<span class="hljs-keyword">forall</span> b. b -&gt; b) -&gt; a -&gt; a
<span class="hljs-title">applyAll</span> f x = f x
""");
    }

    [Fact]
    public void MonadInstance()
    {
        AssertHighlighter("haskell",
"""
newtype State s a = State { runState :: s -> (a, s) }

instance Functor (State s) where
  fmap f (State g) = State $ \s -> let (a, s') = g s in (f a, s')

instance Applicative (State s) where
  pure a = State $ \s -> (a, s)
  State f <*> State g = State $ \s ->
    let (h, s1) = f s
        (a, s2) = g s1
    in (h a, s2)
""",
"""
<span class="hljs-class"><span class="hljs-keyword">newtype</span> <span class="hljs-type">State</span> s a = <span class="hljs-type">State</span> { <span class="hljs-title">runState</span> :: <span class="hljs-title">s</span> -&gt; (<span class="hljs-title">a</span>, <span class="hljs-title">s</span>) }</span>
<span class="hljs-class">
<span class="hljs-keyword">instance</span> <span class="hljs-type">Functor</span> (<span class="hljs-type">State</span> <span class="hljs-title">s</span>) <span class="hljs-keyword">where</span></span>
  fmap f (<span class="hljs-type">State</span> g) = <span class="hljs-type">State</span> $ \s -&gt; <span class="hljs-keyword">let</span> (a, s&#x27;) = g s <span class="hljs-keyword">in</span> (f a, s&#x27;)
<span class="hljs-class">
<span class="hljs-keyword">instance</span> <span class="hljs-type">Applicative</span> (<span class="hljs-type">State</span> <span class="hljs-title">s</span>) <span class="hljs-keyword">where</span></span>
  pure a = <span class="hljs-type">State</span> $ \s -&gt; (a, s)
  <span class="hljs-type">State</span> f &lt;*&gt; <span class="hljs-type">State</span> g = <span class="hljs-type">State</span> $ \s -&gt;
    <span class="hljs-keyword">let</span> (h, s1) = f s
        (a, s2) = g s1
    <span class="hljs-keyword">in</span> (h a, s2)
""");
    }

    [Fact]
    public void InfixDecl()
    {
        AssertHighlighter("haskell",
"""
infixl 6 <+>
infixr 5 ++
infix 4 `elem`
(<+>) :: Int -> Int -> Int
a <+> b = a + b
""",
"""
<span class="hljs-keyword">infixl</span> <span class="hljs-number">6</span> &lt;+&gt;
<span class="hljs-keyword">infixr</span> <span class="hljs-number">5</span> ++
<span class="hljs-keyword">infix</span> <span class="hljs-number">4</span> `elem`
(&lt;+&gt;) :: <span class="hljs-type">Int</span> -&gt; <span class="hljs-type">Int</span> -&gt; <span class="hljs-type">Int</span>
<span class="hljs-title">a</span> &lt;+&gt; b = a + b
""");
    }

    [Fact]
    public void Foreign()
    {
        AssertHighlighter("haskell",
"""
foreign import ccall "math.h sin" c_sin :: CDouble -> CDouble
foreign export ccall safe hsFun :: CInt -> IO CInt
""",
"""
<span class="hljs-keyword">foreign</span> <span class="hljs-keyword">import</span> <span class="hljs-keyword">ccall</span> <span class="hljs-string">&quot;math.h sin&quot;</span> c_sin :: <span class="hljs-type">CDouble</span> -&gt; <span class="hljs-type">CDouble</span>
<span class="hljs-keyword">foreign</span> <span class="hljs-keyword">export</span> <span class="hljs-keyword">ccall</span> <span class="hljs-keyword">safe</span> hsFun :: <span class="hljs-type">CInt</span> -&gt; <span class="hljs-type">IO</span> <span class="hljs-type">CInt</span>
""");
    }

    [Fact]
    public void DefaultDecl()
    {
        AssertHighlighter("haskell",
"""
default (Integer, Double)
""",
"""
<span class="hljs-keyword">default</span> (<span class="hljs-type">Integer</span>, <span class="hljs-type">Double</span>)
""");
    }

    [Fact]
    public void Preprocessor()
    {
        AssertHighlighter("haskell",
"""
#if defined(DEBUG)
debug = True
#else
debug = False
#endif
""",
"""
<span class="hljs-meta">#if defined(DEBUG)</span>
<span class="hljs-title">debug</span> = <span class="hljs-type">True</span>
<span class="hljs-meta">#else</span>
<span class="hljs-title">debug</span> = <span class="hljs-type">False</span>
<span class="hljs-meta">#endif</span>
""");
    }

    [Fact]
    public void Shebang()
    {
        AssertHighlighter("haskell",
"""
#!/usr/bin/env runhaskell
main = print 42
""",
"""
<span class="hljs-meta">#!/usr/bin/env runhaskell</span>
<span class="hljs-title">main</span> = print <span class="hljs-number">42</span>
""");
    }

    [Fact]
    public void DerivingStrategies()
    {
        AssertHighlighter("haskell",
"""
data Color = Red | Green | Blue
  deriving stock (Eq, Show)
  deriving anyclass (ToJSON)

newtype Age = Age Int deriving newtype (Num)
""",
"""
<span class="hljs-class"><span class="hljs-keyword">data</span> <span class="hljs-type">Color</span> = <span class="hljs-type">Red</span> | <span class="hljs-type">Green</span> | <span class="hljs-type">Blue</span></span>
  <span class="hljs-keyword">deriving</span> stock (<span class="hljs-type">Eq</span>, <span class="hljs-type">Show</span>)
  <span class="hljs-keyword">deriving</span> anyclass (<span class="hljs-type">ToJSON</span>)

<span class="hljs-class"><span class="hljs-keyword">newtype</span> <span class="hljs-type">Age</span> = <span class="hljs-type">Age</span> <span class="hljs-type">Int</span> <span class="hljs-keyword">deriving</span> <span class="hljs-keyword">newtype</span> (<span class="hljs-type">Num</span>)</span>
""");
    }

    [Fact]
    public void RecordsSyntax()
    {
        AssertHighlighter("haskell",
"""
p = Person { name = "Alice", age = 30 }
p' = p { age = 31 }
getName Person{name = n} = n
""",
"""
<span class="hljs-title">p</span> = <span class="hljs-type">Person</span> { name = <span class="hljs-string">&quot;Alice&quot;</span>, age = <span class="hljs-number">30</span> }
<span class="hljs-title">p&#x27;</span> = p { age = <span class="hljs-number">31</span> }
<span class="hljs-title">getName</span> <span class="hljs-type">Person</span>{name = n} = n
""");
    }

    [Fact]
    public void TypeclassConstraints()
    {
        AssertHighlighter("haskell",
"""
sortOn' :: (Ord b) => (a -> b) -> [a] -> [a]
sortOn' f = sortBy (comparing f)

elem' :: (Eq a, Foldable t) => a -> t a -> Bool
elem' = any . (==)
""",
"""
<span class="hljs-title">sortOn&#x27;</span> :: (<span class="hljs-type">Ord</span> b) =&gt; (a -&gt; b) -&gt; [a] -&gt; [a]
<span class="hljs-title">sortOn&#x27;</span> f = sortBy (comparing f)

<span class="hljs-title">elem&#x27;</span> :: (<span class="hljs-type">Eq</span> a, <span class="hljs-type">Foldable</span> t) =&gt; a -&gt; t a -&gt; <span class="hljs-type">Bool</span>
<span class="hljs-title">elem&#x27;</span> = any . (==)
""");
    }

    [Fact]
    public void QualifiedNames()
    {
        AssertHighlighter("haskell",
"""
m = Map.fromList [(1, "a")]
v = Map.lookup 1 m
x = Data.List.sort [3, 1, 2]
y = Prelude.map
""",
"""
<span class="hljs-title">m</span> = <span class="hljs-type">Map</span>.fromList [(<span class="hljs-number">1</span>, <span class="hljs-string">&quot;a&quot;</span>)]
<span class="hljs-title">v</span> = <span class="hljs-type">Map</span>.lookup <span class="hljs-number">1</span> m
<span class="hljs-title">x</span> = <span class="hljs-type">Data</span>.<span class="hljs-type">List</span>.sort [<span class="hljs-number">3</span>, <span class="hljs-number">1</span>, <span class="hljs-number">2</span>]
<span class="hljs-title">y</span> = <span class="hljs-type">Prelude</span>.map
""");
    }

    [Fact]
    public void MdoProc()
    {
        AssertHighlighter("haskell",
"""
f = mdo
  xs <- return (1 : xs)
  return xs
""",
"""
<span class="hljs-title">f</span> = <span class="hljs-keyword">mdo</span>
  xs &lt;- return (<span class="hljs-number">1</span> : xs)
  return xs
""");
    }

    [Fact]
    public void UnicodeOperators()
    {
        AssertHighlighter("haskell",
"""
x ∘ y = x . y
a → b
f ∷ Int → Int
""",
"""
<span class="hljs-title">x</span> ∘ y = x . y
<span class="hljs-title">a</span> → b
<span class="hljs-title">f</span> ∷ <span class="hljs-type">Int</span> → <span class="hljs-type">Int</span>
""");
    }

    [Fact]
    public void TemplateHaskell()
    {
        AssertHighlighter("haskell",
"""
makeLenses ''Person
$(deriveJSON defaultOptions ''User)
[quasi| some text |]
""",
"""
<span class="hljs-title">makeLenses</span> &#x27;&#x27;<span class="hljs-type">Person</span>
$(deriveJSON defaultOptions &#x27;&#x27;<span class="hljs-type">User</span>)
[quasi| some text |]
""");
    }

    [Fact]
    public void UnterminatedBlockComment()
    {
        AssertHighlighter("haskell",
"""
x = 1
{- unterminated {- nested -}
y = 2
""",
"""
<span class="hljs-title">x</span> = <span class="hljs-number">1</span>
<span class="hljs-comment">{- unterminated <span class="hljs-comment">{- nested -}</span>
y = 2</span>
""");
    }

    [Fact]
    public void ModuleExportsMultiline()
    {
        AssertHighlighter("haskell",
"""
module Data.Stack
  ( Stack
  , push
  , pop
  -- * Queries
  , isEmpty
  ) where
""",
"""
<span class="hljs-keyword">module</span> Data.Stack
  ( <span class="hljs-type">Stack</span>
  , <span class="hljs-title">push</span>
  , <span class="hljs-title">pop</span>
  <span class="hljs-comment">-- * Queries</span>
  , <span class="hljs-title">isEmpty</span>
  ) <span class="hljs-keyword">where</span>
""");
    }

    [Fact]
    public void PragmasInline()
    {
        AssertHighlighter("haskell",
"""
{-# INLINE foo #-}
foo :: Int -> Int
foo = id
data P = P {-# UNPACK #-} !Int {-# UNPACK #-} !Int
{-# SPECIALIZE bar :: Int -> Int #-}
""",
"""
<span class="hljs-meta">{-# INLINE foo #-}</span>
<span class="hljs-title">foo</span> :: <span class="hljs-type">Int</span> -&gt; <span class="hljs-type">Int</span>
<span class="hljs-title">foo</span> = id
<span class="hljs-class"><span class="hljs-keyword">data</span> <span class="hljs-type">P</span> = <span class="hljs-type">P</span> <span class="hljs-meta">{-# UNPACK #-}</span> !<span class="hljs-type">Int</span> <span class="hljs-meta">{-# UNPACK #-}</span> !<span class="hljs-type">Int</span></span>
<span class="hljs-meta">{-# SPECIALIZE bar :: Int -&gt; Int #-}</span>
""");
    }

    [Fact]
    public void IndentedClass()
    {
        AssertHighlighter("haskell",
"""
module M where


  class Foo a where
    foo :: a -> a
""",
"""
<span class="hljs-keyword">module</span> M <span class="hljs-keyword">where</span>
<span class="hljs-class">

  <span class="hljs-keyword">class</span> <span class="hljs-type">Foo</span> a <span class="hljs-keyword">where</span></span>
    foo :: a -&gt; a
""");
    }

    [Fact]
    public void WhereInWord()
    {
        AssertHighlighter("haskell",
"""
nowhere = somewhere
module Foo.Bar where
""",
"""
<span class="hljs-title">nowhere</span> = somewhere
<span class="hljs-keyword">module</span> Foo.Bar <span class="hljs-keyword">where</span>
""");
    }
}
