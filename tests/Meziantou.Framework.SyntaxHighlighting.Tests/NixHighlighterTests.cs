namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class NixHighlighterTests
{
    [Fact]
    public void Derivation()
    {
        AssertHighlighter("nix",
"""
{ lib, stdenv, fetchFromGitHub, cmake }:

stdenv.mkDerivation rec {
  pname = "hello";
  version = "2.12.1";

  src = fetchFromGitHub {
    owner = "example";
    repo = pname;
    rev = "v${version}";
    hash = "sha256-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
  };

  nativeBuildInputs = [ cmake ];
  doCheck = true;

  meta = with lib; {
    description = "A program that produces a familiar, friendly greeting";
    license = licenses.gpl3Plus;
    platforms = platforms.all;
  };
}
""",
"""
{ lib, stdenv, fetchFromGitHub, cmake }:

stdenv.mkDerivation <span class="hljs-keyword">rec</span> {
  <span class="hljs-attr">pname</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;hello&quot;</span>;
  <span class="hljs-attr">version</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;2.12.1&quot;</span>;

  <span class="hljs-attr">src</span> <span class="hljs-operator">=</span> fetchFromGitHub {
    <span class="hljs-attr">owner</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;example&quot;</span>;
    <span class="hljs-attr">repo</span> <span class="hljs-operator">=</span> pname;
    <span class="hljs-attr">rev</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;v<span class="hljs-subst">${version}</span>&quot;</span>;
    <span class="hljs-attr">hash</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;sha256-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=&quot;</span>;
  };

  <span class="hljs-attr">nativeBuildInputs</span> <span class="hljs-operator">=</span> [ cmake ];
  <span class="hljs-attr">doCheck</span> <span class="hljs-operator">=</span> <span class="hljs-literal">true</span>;

  <span class="hljs-attr">meta</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">with</span> lib; {
    <span class="hljs-attr">description</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;A program that produces a familiar, friendly greeting&quot;</span>;
    <span class="hljs-attr">license</span> <span class="hljs-operator">=</span> licenses.gpl3Plus;
    <span class="hljs-attr">platforms</span> <span class="hljs-operator">=</span> platforms.all;
  };
}
""");
    }

    [Fact]
    public void Flake()
    {
        AssertHighlighter("nix",
"""
{
  description = "My flake";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs = { self, nixpkgs }:
    let
      system = "x86_64-linux";
      pkgs = import nixpkgs { inherit system; };
    in {
      packages.${system}.default = pkgs.hello;
      devShells.${system}.default = pkgs.mkShell {
        buildInputs = with pkgs; [ git nodejs_20 ];
      };
    };
}
""",
"""
{
  <span class="hljs-attr">description</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;My flake&quot;</span>;

  <span class="hljs-attr">inputs.nixpkgs.url</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;github:NixOS/nixpkgs/nixos-unstable&quot;</span>;

  <span class="hljs-attr">outputs</span> <span class="hljs-operator">=</span> { self, nixpkgs }:
    <span class="hljs-keyword">let</span>
      <span class="hljs-attr">system</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;x86_64-linux&quot;</span>;
      <span class="hljs-attr">pkgs</span> <span class="hljs-operator">=</span> <span class="hljs-built_in">import</span> nixpkgs { <span class="hljs-keyword">inherit</span> system; };
    <span class="hljs-keyword">in</span> {
      packages.${system}.default <span class="hljs-operator">=</span> pkgs.hello;
      devShells.${system}.default <span class="hljs-operator">=</span> pkgs.mkShell {
        <span class="hljs-attr">buildInputs</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">with</span> pkgs; [ git nodejs_20 ];
      };
    };
}
""");
    }

    [Fact]
    public void NixosConfiguration()
    {
        AssertHighlighter("nix",
"""
{ config, pkgs, ... }:
{
  imports = [ ./hardware-configuration.nix ];

  boot.loader.systemd-boot.enable = true;
  networking.hostName = "nixos"; # Define your hostname.
  time.timeZone = "Europe/Paris";

  users.users.alice = {
    isNormalUser = true;
    extraGroups = [ "wheel" ];
  };

  environment.systemPackages = with pkgs; [ vim wget ];
  system.stateVersion = "24.05";
}
""",
"""
{ config, pkgs, ... }:
{
  <span class="hljs-attr">imports</span> <span class="hljs-operator">=</span> [ <span class="hljs-symbol">./hardware-configuration.nix</span> ];

  <span class="hljs-attr">boot.loader.systemd-boot.enable</span> <span class="hljs-operator">=</span> <span class="hljs-literal">true</span>;
  <span class="hljs-attr">networking.hostName</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;nixos&quot;</span>; <span class="hljs-comment"># Define your hostname.</span>
  <span class="hljs-attr">time.timeZone</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;Europe/Paris&quot;</span>;

  <span class="hljs-attr">users.users.alice</span> <span class="hljs-operator">=</span> {
    <span class="hljs-attr">isNormalUser</span> <span class="hljs-operator">=</span> <span class="hljs-literal">true</span>;
    <span class="hljs-attr">extraGroups</span> <span class="hljs-operator">=</span> [ <span class="hljs-string">&quot;wheel&quot;</span> ];
  };

  <span class="hljs-attr">environment.systemPackages</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">with</span> pkgs; [ vim wget ];
  <span class="hljs-attr">system.stateVersion</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;24.05&quot;</span>;
}
""");
    }

    [Fact]
    public void IndentedString()
    {
        AssertHighlighter("nix",
"""
{
  script = ''
    echo "Hello ${name}"
    echo ''${notInterpolated}
    echo '''quoted'''
    cat > file <<EOF
    line
    EOF
  '';
  other = ''a''\nb'';
  after = 1;
}
""",
"""
{
  <span class="hljs-attr">script</span> <span class="hljs-operator">=</span> <span class="hljs-string">&#x27;&#x27;
    echo &quot;Hello <span class="hljs-subst">${name}</span>&quot;
    echo <span class="hljs-char escape_">&#x27;&#x27;$</span>{notInterpolated}
    echo <span class="hljs-char escape_">&#x27;&#x27;&#x27;</span>quoted<span class="hljs-char escape_">&#x27;&#x27;&#x27;</span>
    cat &gt; file &lt;&lt;EOF
    line
    EOF
  &#x27;&#x27;</span>;
  <span class="hljs-attr">other</span> <span class="hljs-operator">=</span> <span class="hljs-string">&#x27;&#x27;a<span class="hljs-char escape_">&#x27;&#x27;\n</span>b&#x27;&#x27;</span>;
  <span class="hljs-attr">after</span> <span class="hljs-operator">=</span> <span class="hljs-number">1</span>;
}
""");
    }

    [Fact]
    public void DoubleQuotedStringEscapes()
    {
        AssertHighlighter("nix",
"""
"a\"b \${x} \n \t ${toString y} end"
""",
"""
<span class="hljs-string">&quot;a<span class="hljs-char escape_">\&quot;</span>b <span class="hljs-char escape_">\$</span>{x} <span class="hljs-char escape_">\n</span> <span class="hljs-char escape_">\t</span> <span class="hljs-subst">${<span class="hljs-built_in">toString</span> y}</span> end&quot;</span>
""");
    }

    [Fact]
    public void Builtins()
    {
        AssertHighlighter("nix",
"""
builtins.mapAttrs (n: v: v) x
builtins.map f xs
builtins.elemAt xs 0
builtins.foldl' (a: b: a + b) 0 xs
builtins.fetchurl { url = "https://example.com/a.tar.gz"; }
builtins.readFileType ./x
builtins.unknownThing
builtins.toString 1
""",
"""
<span class="hljs-built_in">builtins.mapAttrs</span> (<span class="hljs-params">n:</span> <span class="hljs-params">v:</span> v) x
<span class="hljs-built_in">builtins.map</span> f xs
<span class="hljs-built_in">builtins.elemAt</span> xs <span class="hljs-number">0</span>
<span class="hljs-built_in">builtins.foldl&#x27;</span> (<span class="hljs-params">a:</span> <span class="hljs-params">b:</span> a <span class="hljs-operator">+</span> b) <span class="hljs-number">0</span> xs
<span class="hljs-built_in">builtins.fetchurl</span> { <span class="hljs-attr">url</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;https://example.com/a.tar.gz&quot;</span>; }
<span class="hljs-built_in">builtins.readFileType</span> <span class="hljs-symbol">./x</span>
<span class="hljs-built_in">builtins</span>.unknownThing
<span class="hljs-built_in">builtins.toString</span> <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Operators()
    {
        AssertHighlighter("nix",
"""
let
  a = 1 + 2 - 3 * 4 / 5;
  b = [ 1 2 ] ++ [ 3 ];
  c = { x = 1; } // { y = 2; };
  d = a == b && c != d || !e;
  e = x -> y;
  f = x ? y;
  g = 1-2;
  h = a -1;
  i = x |> f;
  j = a <= b;
in a
""",
"""
<span class="hljs-keyword">let</span>
  <span class="hljs-attr">a</span> <span class="hljs-operator">=</span> <span class="hljs-number">1</span> <span class="hljs-operator">+</span> <span class="hljs-number">2</span> <span class="hljs-operator">-</span> <span class="hljs-number">3</span> <span class="hljs-operator">*</span> <span class="hljs-number">4</span> <span class="hljs-symbol">/</span> <span class="hljs-number">5</span>;
  <span class="hljs-attr">b</span> <span class="hljs-operator">=</span> [ <span class="hljs-number">1</span> <span class="hljs-number">2</span> ] <span class="hljs-operator">++</span> [ <span class="hljs-number">3</span> ];
  <span class="hljs-attr">c</span> <span class="hljs-operator">=</span> { <span class="hljs-attr">x</span> <span class="hljs-operator">=</span> <span class="hljs-number">1</span>; } <span class="hljs-operator">//</span> { <span class="hljs-attr">y</span> <span class="hljs-operator">=</span> <span class="hljs-number">2</span>; };
  <span class="hljs-attr">d</span> <span class="hljs-operator">=</span> a <span class="hljs-operator">==</span> b <span class="hljs-operator">&amp;&amp;</span> c <span class="hljs-operator">!=</span> d <span class="hljs-operator">||</span> <span class="hljs-operator">!</span>e;
  <span class="hljs-attr">e</span> <span class="hljs-operator">=</span> x <span class="hljs-operator">-&gt;</span> y;
  <span class="hljs-attr">f</span> <span class="hljs-operator">=</span> x <span class="hljs-operator">?</span> y;
  <span class="hljs-attr">g</span> <span class="hljs-operator">=</span> <span class="hljs-number">1</span><span class="hljs-operator">-</span><span class="hljs-number">2</span>;
  <span class="hljs-attr">h</span> <span class="hljs-operator">=</span> a <span class="hljs-operator">-</span><span class="hljs-number">1</span>;
  <span class="hljs-attr">i</span> <span class="hljs-operator">=</span> x <span class="hljs-operator">|&gt;</span> f;
  <span class="hljs-attr">j</span> <span class="hljs-operator">=</span> a <span class="hljs-operator">&lt;=</span> b;
<span class="hljs-keyword">in</span> a
""");
    }

    [Fact]
    public void Paths()
    {
        AssertHighlighter("nix",
"""
[ ./foo.nix ../bar/baz.nix ~/config /etc/nixos <nixpkgs> <nixpkgs/lib> ./. ]
import ./default.nix;
""",
"""
[ <span class="hljs-symbol">./foo.nix</span> <span class="hljs-symbol">../bar/baz.nix</span> <span class="hljs-symbol">~/config</span> <span class="hljs-symbol">/etc/nixos</span> <span class="hljs-symbol">&lt;nixpkgs&gt;</span> <span class="hljs-symbol">&lt;nixpkgs/lib&gt;</span> <span class="hljs-symbol">./.</span> ]
<span class="hljs-built_in">import</span> <span class="hljs-symbol">./default.nix</span>;
""");
    }

    [Fact]
    public void PathEdgeCases()
    {
        AssertHighlighter("nix",
"""
a/b//c d
a/b/ d
./a/./b d
x//y d
/a/b/c)
~/a/b d
../../x d
a/./b;
a/b/c/d/e.f f
(a/b) ./x/y/z) ./x/../y ../x.. .../x ~/.config/a.b/
a//b//c d
/ //a
""",
"""
a<span class="hljs-operator">/</span>b<span class="hljs-symbol">//c</span> d
a<span class="hljs-operator">/</span>b<span class="hljs-symbol">/</span> d
<span class="hljs-symbol">./a/./b</span> d
x<span class="hljs-symbol">//y</span> d
<span class="hljs-operator">/</span>a<span class="hljs-operator">/</span>b<span class="hljs-operator">/</span>c)
<span class="hljs-symbol">~/a/b</span> d
<span class="hljs-symbol">../../x</span> d
a<span class="hljs-symbol">/./b</span>;
a<span class="hljs-symbol">/b/c/d/e.f</span> f
(a<span class="hljs-operator">/</span>b) .<span class="hljs-operator">/</span>x<span class="hljs-operator">/</span>y<span class="hljs-operator">/</span>z) <span class="hljs-symbol">./x/../y</span> <span class="hljs-symbol">../x..</span> .<span class="hljs-symbol">../x</span> ~<span class="hljs-operator">/</span>.config<span class="hljs-operator">/</span>a.b<span class="hljs-symbol">/</span>
a<span class="hljs-operator">//</span>b<span class="hljs-symbol">//c</span> d
<span class="hljs-symbol">/</span> <span class="hljs-operator">//</span>a
""");
    }

    [Fact]
    public void Functions()
    {
        AssertHighlighter("nix",
"""
let
  add = x: y: x + y;
  greet = { name ? "world", ... }@args: "hello ${name}";
  f = x: if x > 0 then x else -x;
in assert add 1 2 == 3; greet { }
""",
"""
<span class="hljs-keyword">let</span>
  <span class="hljs-attr">add</span> <span class="hljs-operator">=</span> <span class="hljs-params">x:</span> <span class="hljs-params">y:</span> x <span class="hljs-operator">+</span> y;
  <span class="hljs-attr">greet</span> <span class="hljs-operator">=</span> { name <span class="hljs-operator">?</span> <span class="hljs-string">&quot;world&quot;</span>, ... }@<span class="hljs-params">args:</span> <span class="hljs-string">&quot;hello <span class="hljs-subst">${name}</span>&quot;</span>;
  <span class="hljs-attr">f</span> <span class="hljs-operator">=</span> <span class="hljs-params">x:</span> <span class="hljs-keyword">if</span> x <span class="hljs-operator">&gt;</span> <span class="hljs-number">0</span> <span class="hljs-keyword">then</span> x <span class="hljs-keyword">else</span> <span class="hljs-operator">-</span>x;
<span class="hljs-keyword">in</span> <span class="hljs-keyword">assert</span> add <span class="hljs-number">1</span> <span class="hljs-number">2</span> <span class="hljs-operator">==</span> <span class="hljs-number">3</span>; greet { }
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("nix",
"""
# line comment
/* block
   comment TODO: fix */
/** doc comment
  # Example
*/
x = 1;
""",
"""
<span class="hljs-comment"># line comment</span>
<span class="hljs-comment">/* block
   comment <span class="hljs-doctag">TODO:</span> fix */</span>
<span class="hljs-comment">/** doc comment
  # Example
*/</span>
<span class="hljs-attr">x</span> <span class="hljs-operator">=</span> <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Repl()
    {
        AssertHighlighter("nix",
"""
nix-repl> :t 1
an integer

nix-repl> :l <nixpkgs>
Added 12345 variables.

nix-repl> 1 + 2
3
""",
"""
<span class="hljs-meta prompt_">nix-repl&gt;</span> <span class="hljs-meta">:t</span> <span class="hljs-number">1</span>
an integer

<span class="hljs-meta prompt_">nix-repl&gt;</span> <span class="hljs-meta">:l</span> <span class="hljs-symbol">&lt;nixpkgs&gt;</span>
Added <span class="hljs-number">12345</span> variables.

<span class="hljs-meta prompt_">nix-repl&gt;</span> <span class="hljs-number">1</span> <span class="hljs-operator">+</span> <span class="hljs-number">2</span>
<span class="hljs-number">3</span>
""");
    }

    [Fact]
    public void AttributesAndDashedIdentifiers()
    {
        AssertHighlighter("nix",
"""
{
  foo-bar = 1;
  "quoted" = 2;
  a.b.c = 3;
  x = with-in: lib-map;
  inherit (pkgs) git;
  y = rec { a = 1; b = a; };
}
""",
"""
{
  <span class="hljs-attr">foo-bar</span> <span class="hljs-operator">=</span> <span class="hljs-number">1</span>;
  <span class="hljs-string">&quot;quoted&quot;</span> <span class="hljs-operator">=</span> <span class="hljs-number">2</span>;
  <span class="hljs-attr">a.b.c</span> <span class="hljs-operator">=</span> <span class="hljs-number">3</span>;
  <span class="hljs-attr">x</span> <span class="hljs-operator">=</span> <span class="hljs-params">with-in:</span> lib-map;
  <span class="hljs-keyword">inherit</span> (pkgs) git;
  <span class="hljs-attr">y</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">rec</span> { <span class="hljs-attr">a</span> <span class="hljs-operator">=</span> <span class="hljs-number">1</span>; <span class="hljs-attr">b</span> <span class="hljs-operator">=</span> a; };
}
""");
    }

    [Fact]
    public void AttributeAtStartOfLine()
    {
        AssertHighlighter("nix",
"""
foo.bar = 1;
x = 2;
nix-repl> y = 3
""",
"""
<span class="hljs-attr">foo.bar</span> <span class="hljs-operator">=</span> <span class="hljs-number">1</span>;
<span class="hljs-attr">x</span> <span class="hljs-operator">=</span> <span class="hljs-number">2</span>;
<span class="hljs-meta prompt_">nix-repl&gt;</span> y <span class="hljs-operator">=</span> <span class="hljs-number">3</span>
""");
    }

    [Fact]
    public void UnterminatedString()
    {
        AssertHighlighter("nix",
"""
x = "abc
y = 2;
""",
"""
<span class="hljs-attr">x</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;abc
y = 2;</span>
""");
    }

    [Fact]
    public void UnterminatedAntiquote()
    {
        AssertHighlighter("nix",
"""
x = "${foo
bar"; y = 1;
""",
"""
<span class="hljs-attr">x</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;<span class="hljs-subst">${foo
bar<span class="hljs-string">&quot;; y = 1;</span></span></span>
""");
    }

    [Fact]
    public void OrDefault()
    {
        AssertHighlighter("nix",
"""
x.y or "default"
""",
"""
x.y <span class="hljs-keyword">or</span> <span class="hljs-string">&quot;default&quot;</span>
""");
    }

    [Fact]
    public void NestedAntiquote()
    {
        AssertHighlighter("nix",
"""
"${lib.concatMapStringsSep "," (x: "${x}-y") xs}"
""",
"""
<span class="hljs-string">&quot;<span class="hljs-subst">${lib.concatMapStringsSep <span class="hljs-string">&quot;,&quot;</span> (<span class="hljs-params">x:</span> <span class="hljs-string">&quot;<span class="hljs-subst">${x}</span>-y&quot;</span>) xs}</span>&quot;</span>
""");
    }

    [Fact]
    public void NixosAlias()
    {
        AssertHighlighter("nixos",
"""
{ a = true; }
""",
"""
{ <span class="hljs-attr">a</span> <span class="hljs-operator">=</span> <span class="hljs-literal">true</span>; }
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("nix",
"""
[ 1 2.5 0.1 1e3 007 ]
""",
"""
[ <span class="hljs-number">1</span> <span class="hljs-number">2.5</span> <span class="hljs-number">0.1</span> <span class="hljs-number">1</span>e3 <span class="hljs-number">007</span> ]
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("nix",
"""
{ name = "héllo wörld 🎉"; é = 1; }
""",
"""
{ <span class="hljs-attr">name</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;héllo wörld 🎉&quot;</span>; é <span class="hljs-operator">=</span> <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Empty()
    {
        AssertHighlighter("nix", "", "");
    }
}
