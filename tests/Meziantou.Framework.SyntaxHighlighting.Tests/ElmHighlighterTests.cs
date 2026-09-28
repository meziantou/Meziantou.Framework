namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class ElmHighlighterTests
{    [Fact]
    public void Hello()
    {
        AssertHighlighter("elm",
"""
module Main exposing (main)

import Html exposing (text)


main =
    text "Hello, World!"
""",
"""
<span class="hljs-keyword">module</span> Main <span class="hljs-keyword">exposing</span> (main)

<span class="hljs-keyword">import</span> Html <span class="hljs-keyword">exposing</span> (text)


<span class="hljs-title">main</span> =
    text <span class="hljs-string">&quot;Hello, World!&quot;</span>
""");
    }

    [Fact]
    public void TheElmArchitecture()
    {
        AssertHighlighter("elm",
"""
module Counter exposing (Model, Msg(..), init, update, view)

import Browser
import Html exposing (Html, button, div, text)
import Html.Events exposing (onClick)


type alias Model =
    { count : Int }


type Msg
    = Increment
    | Decrement


init : Model
init =
    { count = 0 }


update : Msg -> Model -> Model
update msg model =
    case msg of
        Increment ->
            { model | count = model.count + 1 }

        Decrement ->
            { model | count = model.count - 1 }


view : Model -> Html Msg
view model =
    div []
        [ button [ onClick Decrement ] [ text "-" ]
        , div [] [ text (String.fromInt model.count) ]
        , button [ onClick Increment ] [ text "+" ]
        ]
""",
"""
<span class="hljs-keyword">module</span> Counter <span class="hljs-keyword">exposing</span> (<span class="hljs-type">Model</span>, <span class="hljs-type">Msg</span>(..), init, update, view)

<span class="hljs-keyword">import</span> Browser
<span class="hljs-keyword">import</span> Html <span class="hljs-keyword">exposing</span> (<span class="hljs-type">Html</span>, button, div, text)
<span class="hljs-keyword">import</span> Html.Events <span class="hljs-keyword">exposing</span> (onClick)


<span class="hljs-keyword">type</span> <span class="hljs-keyword">alias</span> <span class="hljs-type">Model</span> =
    { count : <span class="hljs-type">Int</span> }


<span class="hljs-keyword">type</span> <span class="hljs-type">Msg</span>
    = <span class="hljs-type">Increment</span>
    | <span class="hljs-type">Decrement</span>


<span class="hljs-title">init</span> : <span class="hljs-type">Model</span>
<span class="hljs-title">init</span> =
    { count = <span class="hljs-number">0</span> }


<span class="hljs-title">update</span> : <span class="hljs-type">Msg</span> -&gt; <span class="hljs-type">Model</span> -&gt; <span class="hljs-type">Model</span>
<span class="hljs-title">update</span> msg model =
    <span class="hljs-keyword">case</span> msg <span class="hljs-keyword">of</span>
        <span class="hljs-type">Increment</span> -&gt;
            { model | count = model.count + <span class="hljs-number">1</span> }

        <span class="hljs-type">Decrement</span> -&gt;
            { model | count = model.count - <span class="hljs-number">1</span> }


<span class="hljs-title">view</span> : <span class="hljs-type">Model</span> -&gt; <span class="hljs-type">Html</span> <span class="hljs-type">Msg</span>
<span class="hljs-title">view</span> model =
    div []
        [ button [ onClick <span class="hljs-type">Decrement</span> ] [ text <span class="hljs-string">&quot;-&quot;</span> ]
        , div [] [ text (<span class="hljs-type">String</span>.fromInt model.count) ]
        , button [ onClick <span class="hljs-type">Increment</span> ] [ text <span class="hljs-string">&quot;+&quot;</span> ]
        ]
""");
    }

    [Fact]
    public void PortModule_PortDeclarationsDoNotSwallowTheDocument()
    {
        AssertHighlighter("elm",
"""
port module Ports exposing (..)

import Json.Encode as E


port sendMessage : String -> Cmd msg


port messageReceiver : (String -> msg) -> Sub msg


subscriptions : Model -> Sub Msg
subscriptions _ =
    messageReceiver Recv
""",
"""
<span class="hljs-keyword">port</span> <span class="hljs-keyword">module</span> Ports <span class="hljs-keyword">exposing</span> (..)

<span class="hljs-keyword">import</span> Json.Encode <span class="hljs-keyword">as</span> E


<span class="hljs-keyword">port</span> sendMessage : String -&gt; Cmd msg


<span class="hljs-keyword">port</span> messageReceiver : (String -&gt; msg) -&gt; Sub msg


<span class="hljs-title">subscriptions</span> : <span class="hljs-type">Model</span> -&gt; <span class="hljs-type">Sub</span> <span class="hljs-type">Msg</span>
<span class="hljs-title">subscriptions</span> _ =
    messageReceiver <span class="hljs-type">Recv</span>
""");
    }

    [Fact]
    public void LetInAndIf()
    {
        AssertHighlighter("elm",
"""
distance : Float -> Float -> Float
distance x y =
    let
        dx =
            x * x

        dy =
            y * y
    in
    sqrt (dx + dy)


classify n =
    if n < 0 then
        "negative"

    else if n == 0 then
        "zero"

    else
        "positive"
""",
"""
<span class="hljs-title">distance</span> : <span class="hljs-type">Float</span> -&gt; <span class="hljs-type">Float</span> -&gt; <span class="hljs-type">Float</span>
<span class="hljs-title">distance</span> x y =
    <span class="hljs-keyword">let</span>
        dx =
            x * x

        dy =
            y * y
    <span class="hljs-keyword">in</span>
    sqrt (dx + dy)


<span class="hljs-title">classify</span> n =
    <span class="hljs-keyword">if</span> n &lt; <span class="hljs-number">0</span> <span class="hljs-keyword">then</span>
        <span class="hljs-string">&quot;negative&quot;</span>

    <span class="hljs-keyword">else</span> <span class="hljs-keyword">if</span> n == <span class="hljs-number">0</span> <span class="hljs-keyword">then</span>
        <span class="hljs-string">&quot;zero&quot;</span>

    <span class="hljs-keyword">else</span>
        <span class="hljs-string">&quot;positive&quot;</span>
""");
    }

    [Fact]
    public void Literals()
    {
        AssertHighlighter("elm",
"""
values =
    [ 42, -7, 3.14, 0x1F, 6.02e23 ]

chars =
    [ 'a', '\n', '\'' ]

strings =
    "tab\tnewline\n \"quoted\""

tuple =
    ( 1, "two", True )
""",
"""
<span class="hljs-title">values</span> =
    [ <span class="hljs-number">42</span>, <span class="hljs-number">-7</span>, <span class="hljs-number">3.14</span>, <span class="hljs-number">0x1F</span>, <span class="hljs-number">6.02e23</span> ]

<span class="hljs-title">chars</span> =
    [ <span class="hljs-string">&#x27;a&#x27;</span>, <span class="hljs-string">&#x27;\n&#x27;</span>, <span class="hljs-string">&#x27;\&#x27;&#x27;</span> ]

<span class="hljs-title">strings</span> =
    <span class="hljs-string">&quot;tab\tnewline\n \&quot;quoted\&quot;&quot;</span>

<span class="hljs-title">tuple</span> =
    ( <span class="hljs-number">1</span>, <span class="hljs-string">&quot;two&quot;</span>, <span class="hljs-type">True</span> )
""");
    }

    [Fact]
    public void MultilineString()
    {
        AssertHighlighter("elm",
""""
query =
    """
    query {
      user(id: "1") { name }
    }
    """

after =
    Just 1
"""",
"""
<span class="hljs-title">query</span> =
    <span class="hljs-string">&quot;&quot;&quot;
    query {
      user(id: &quot;1&quot;) { name }
    }
    &quot;&quot;&quot;</span>

<span class="hljs-title">after</span> =
    <span class="hljs-type">Just</span> <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("elm",
"""
-- line comment
{- block comment -}
{- outer {- nested -} still comment -}
{-| Documentation comment.

    TODO: write more
-}
add x y =
    x + y -- trailing
""",
"""
<span class="hljs-comment">-- line comment</span>
<span class="hljs-comment">{- block comment -}</span>
<span class="hljs-comment">{- outer <span class="hljs-comment">{- nested -}</span> still comment -}</span>
<span class="hljs-comment">{-| Documentation comment.

    <span class="hljs-doctag">TODO:</span> write more
-}</span>
<span class="hljs-title">add</span> x y =
    x + y <span class="hljs-comment">-- trailing</span>
""");
    }

    [Fact]
    public void Infix()
    {
        AssertHighlighter("elm",
"""
infix right 0 (<|) = apL
infixl 5 (++)
""",
"""
<span class="hljs-keyword">infix</span> right <span class="hljs-number">0</span> (&lt;|) = apL
<span class="hljs-keyword">infixl</span> <span class="hljs-number">5</span> (++)
""");
    }

    [Fact]
    public void CustomType()
    {
        AssertHighlighter("elm",
"""
type Maybe a
    = Just a
    | Nothing


withDefault : a -> Maybe a -> a
withDefault default maybe =
    case maybe of
        Just value ->
            value

        Nothing ->
            default
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-type">Maybe</span> a
    = <span class="hljs-type">Just</span> a
    | <span class="hljs-type">Nothing</span>


<span class="hljs-title">withDefault</span> : a -&gt; <span class="hljs-type">Maybe</span> a -&gt; a
<span class="hljs-title">withDefault</span> default maybe =
    <span class="hljs-keyword">case</span> maybe <span class="hljs-keyword">of</span>
        <span class="hljs-type">Just</span> value -&gt;
            value

        <span class="hljs-type">Nothing</span> -&gt;
            default
""");
    }

    [Fact]
    public void DeclarationKeywordsInsideIdentifiers()
    {
        AssertHighlighter("elm",
"""
important =
    reportError importantThing

viewType model =
    typeahead model.support

x = transport portion
""",
"""
<span class="hljs-title">important</span> =
    reportError importantThing

<span class="hljs-title">viewType</span> model =
    typeahead model.support

<span class="hljs-title">x</span> = transport portion
""");
    }

    [Fact]
    public void RecordsLambdasAndPipelines()
    {
        AssertHighlighter("elm",
"""
type alias User =
    { name : String
    , age : Int
    , email : Maybe String -- optional
    }

updateName user =
    { user | name = "Bob" }

lambda =
    List.map (\x -> x * 2) [ 1, 2, 3 ]

pipeline =
    [ 1, 2, 3 ]
        |> List.filter (\n -> modBy 2 n == 0)
        |> List.sum
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-keyword">alias</span> <span class="hljs-type">User</span> =
    { name : <span class="hljs-type">String</span>
    , age : <span class="hljs-type">Int</span>
    , email : <span class="hljs-type">Maybe</span> <span class="hljs-type">String</span> <span class="hljs-comment">-- optional</span>
    }

<span class="hljs-title">updateName</span> user =
    { user | name = <span class="hljs-string">&quot;Bob&quot;</span> }

<span class="hljs-title">lambda</span> =
    <span class="hljs-type">List</span>.map (\x -&gt; x * <span class="hljs-number">2</span>) [ <span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span> ]

<span class="hljs-title">pipeline</span> =
    [ <span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span> ]
        |&gt; <span class="hljs-type">List</span>.filter (\n -&gt; modBy <span class="hljs-number">2</span> n == <span class="hljs-number">0</span>)
        |&gt; <span class="hljs-type">List</span>.sum
""");
    }

    [Fact]
    public void EffectModule()
    {
        AssertHighlighter("elm",
"""
effect module Task where { command = MyCmd } exposing (Task, perform)
""",
"""
<span class="hljs-keyword">effect</span> <span class="hljs-keyword">module</span> Task <span class="hljs-keyword">where</span> { <span class="hljs-keyword">command</span> = MyCmd } <span class="hljs-keyword">exposing</span> (<span class="hljs-type">Task</span>, perform)
""");
    }

    [Fact]
    public void IllegalSemicolon()
    {
        AssertHighlighter("elm",
"""
x = 1;
y = 2
""",
"""
<span class="hljs-title">x</span> = <span class="hljs-number">1</span>;
<span class="hljs-title">y</span> = <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void Unterminated()
    {
        AssertHighlighter("elm",
"""
s = "unterminated
t = 1
{- unterminated comment
u = 2

""",
"""
<span class="hljs-title">s</span> = <span class="hljs-string">&quot;unterminated
t = 1
{- unterminated comment
u = 2
</span>
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("elm",
"""
greeting = "héllo ✓"
-- commentaire été
""",
"""
<span class="hljs-title">greeting</span> = <span class="hljs-string">&quot;héllo ✓&quot;</span>
<span class="hljs-comment">-- commentaire été</span>
""");
    }
}
