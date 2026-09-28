namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class SolidityHighlighterTests
{
    [Fact]
    public void PragmaAndImports()
    {
        AssertHighlighter("solidity",
"""
// SPDX-License-Identifier: MIT
pragma solidity ^0.8.20;
pragma abicoder v2;

import "./Foo.sol";
import {ERC20} from "@openzeppelin/contracts/token/ERC20/ERC20.sol";
import {Ownable as Owned, Context} from './access/Ownable.sol';
import * as Utils from "./Utils.sol";
""",
"""
<span class="hljs-comment">// SPDX-License-Identifier: MIT</span>
<span class="hljs-meta"><span class="hljs-keyword">pragma</span> <span class="hljs-keyword">solidity</span> ^0.8.20;</span>
<span class="hljs-meta"><span class="hljs-keyword">pragma</span> <span class="hljs-keyword">abicoder</span> v2;</span>

<span class="hljs-keyword">import</span> <span class="hljs-string">&quot;./Foo.sol&quot;</span>;
<span class="hljs-keyword">import</span> {ERC20} <span class="hljs-keyword">from</span> <span class="hljs-string">&quot;@openzeppelin/contracts/token/ERC20/ERC20.sol&quot;</span>;
<span class="hljs-keyword">import</span> {Ownable <span class="hljs-keyword">as</span> Owned, Context} <span class="hljs-keyword">from</span> <span class="hljs-string">&#x27;./access/Ownable.sol&#x27;</span>;
<span class="hljs-keyword">import</span> * <span class="hljs-keyword">as</span> Utils <span class="hljs-keyword">from</span> <span class="hljs-string">&quot;./Utils.sol&quot;</span>;
""");
    }

    [Fact]
    public void Contract()
    {
        AssertHighlighter("solidity",
"""
contract SimpleStorage {
    uint256 private storedData;
    address public owner;

    event DataStored(address indexed from, uint256 value);

    constructor() {
        owner = msg.sender;
    }

    function set(uint256 x) external {
        storedData = x;
        emit DataStored(msg.sender, x);
    }

    function get() public view returns (uint256) {
        return storedData;
    }
}
""",
"""
<span class="hljs-keyword">contract</span> <span class="hljs-title class_">SimpleStorage</span> {
    <span class="hljs-type">uint256</span> <span class="hljs-keyword">private</span> storedData;
    <span class="hljs-type">address</span> <span class="hljs-keyword">public</span> owner;

    <span class="hljs-keyword">event</span> <span class="hljs-title function_">DataStored</span>(<span class="hljs-type">address</span> <span class="hljs-keyword">indexed</span> from, <span class="hljs-type">uint256</span> value);

    <span class="hljs-keyword">constructor</span>() {
        owner = <span class="hljs-built_in">msg</span>.sender;
    }

    <span class="hljs-keyword">function</span> <span class="hljs-title function_">set</span>(<span class="hljs-type">uint256</span> x) <span class="hljs-keyword">external</span> {
        storedData = x;
        <span class="hljs-keyword">emit</span> <span class="hljs-title function_ invoke__">DataStored</span>(<span class="hljs-built_in">msg</span>.sender, x);
    }

    <span class="hljs-keyword">function</span> <span class="hljs-title function_">get</span>() <span class="hljs-keyword">public</span> <span class="hljs-keyword">view</span> <span class="hljs-keyword">returns</span> (<span class="hljs-type">uint256</span>) {
        <span class="hljs-keyword">return</span> storedData;
    }
}
""");
    }

    [Fact]
    public void Inheritance()
    {
        AssertHighlighter("solidity",
"""
abstract contract Base is Context, IERC20 {
}

contract MyToken is ERC20("MyToken", "MTK"), Ownable(msg.sender) {
    constructor() {
        _mint(msg.sender, 1_000_000 * 10 ** decimals());
    }
}

interface IERC20 {
    function totalSupply() external view returns (uint256);
}

library SafeMath {
    function add(uint256 a, uint256 b) internal pure returns (uint256) {
        return a + b;
    }
}
""",
"""
<span class="hljs-keyword">abstract</span> <span class="hljs-keyword">contract</span> <span class="hljs-title class_">Base</span> <span class="hljs-keyword">is</span> <span class="hljs-title class_ inherited__">Context</span>, <span class="hljs-title class_ inherited__">IERC20</span> {
}

<span class="hljs-keyword">contract</span> <span class="hljs-title class_">MyToken</span> <span class="hljs-keyword">is</span> <span class="hljs-title class_ inherited__">ERC20</span>(<span class="hljs-string">&quot;MyToken&quot;</span>, <span class="hljs-string">&quot;MTK&quot;</span>), <span class="hljs-title class_ inherited__">Ownable</span>(<span class="hljs-built_in">msg</span>.sender) {
    <span class="hljs-keyword">constructor</span>() {
        <span class="hljs-title function_ invoke__">_mint</span>(<span class="hljs-built_in">msg</span>.sender, <span class="hljs-number">1_000_000</span> * <span class="hljs-number">10</span> ** <span class="hljs-title function_ invoke__">decimals</span>());
    }
}

<span class="hljs-keyword">interface</span> <span class="hljs-title class_">IERC20</span> {
    <span class="hljs-keyword">function</span> <span class="hljs-title function_">totalSupply</span>() <span class="hljs-keyword">external</span> <span class="hljs-keyword">view</span> <span class="hljs-keyword">returns</span> (<span class="hljs-type">uint256</span>);
}

<span class="hljs-keyword">library</span> <span class="hljs-title class_">SafeMath</span> {
    <span class="hljs-keyword">function</span> <span class="hljs-title function_">add</span>(<span class="hljs-type">uint256</span> a, <span class="hljs-type">uint256</span> b) <span class="hljs-keyword">internal</span> <span class="hljs-keyword">pure</span> <span class="hljs-keyword">returns</span> (<span class="hljs-type">uint256</span>) {
        <span class="hljs-keyword">return</span> a + b;
    }
}
""");
    }

    [Fact]
    public void Types()
    {
        AssertHighlighter("solidity",
"""
uint8 a;
int256 b;
uint c;
bytes32 d;
bytes1 e;
bytes f;
string g;
bool h;
address payable i;
fixed128x18 j;
ufixed k;
mapping(address => uint256) public balances;
mapping(address owner => mapping(address spender => uint256)) private _allowances;
uint256[] public values;
uint24 notAType = uint24x;
""",
"""
<span class="hljs-type">uint8</span> a;
<span class="hljs-type">int256</span> b;
<span class="hljs-type">uint</span> c;
<span class="hljs-type">bytes32</span> d;
<span class="hljs-type">bytes1</span> e;
<span class="hljs-type">bytes</span> f;
<span class="hljs-type">string</span> g;
<span class="hljs-type">bool</span> h;
<span class="hljs-type">address</span> <span class="hljs-keyword">payable</span> i;
<span class="hljs-type">fixed128x18</span> j;
<span class="hljs-type">ufixed</span> k;
<span class="hljs-keyword">mapping</span>(<span class="hljs-type">address</span> =&gt; <span class="hljs-type">uint256</span>) <span class="hljs-keyword">public</span> balances;
<span class="hljs-keyword">mapping</span>(<span class="hljs-type">address</span> owner =&gt; <span class="hljs-keyword">mapping</span>(<span class="hljs-type">address</span> spender =&gt; <span class="hljs-type">uint256</span>)) <span class="hljs-keyword">private</span> _allowances;
<span class="hljs-type">uint256</span>[] <span class="hljs-keyword">public</span> values;
<span class="hljs-type">uint24</span> notAType = uint24x;
""");
    }

    [Fact]
    public void StructsEnumsAndErrors()
    {
        AssertHighlighter("solidity",
"""
struct Proposal {
    bytes32 name;
    uint voteCount;
}

enum State { Created, Locked, Inactive }

error InsufficientBalance(uint256 available, uint256 required);

type Price is uint128;

function f() {
    if (balance < amount) revert InsufficientBalance(balance, amount);
    uint error = 1;
}
""",
"""
<span class="hljs-keyword">struct</span> <span class="hljs-title class_">Proposal</span> {
    <span class="hljs-type">bytes32</span> name;
    <span class="hljs-type">uint</span> voteCount;
}

<span class="hljs-keyword">enum</span> <span class="hljs-title class_">State</span> { Created, Locked, Inactive }

<span class="hljs-keyword">error</span> <span class="hljs-title function_">InsufficientBalance</span>(<span class="hljs-type">uint256</span> available, <span class="hljs-type">uint256</span> required);

<span class="hljs-keyword">type</span> <span class="hljs-title class_">Price</span> <span class="hljs-keyword">is</span> <span class="hljs-type">uint128</span>;

<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>() {
    <span class="hljs-keyword">if</span> (balance &lt; amount) <span class="hljs-keyword">revert</span> <span class="hljs-title function_ invoke__">InsufficientBalance</span>(balance, amount);
    <span class="hljs-type">uint</span> error = <span class="hljs-number">1</span>;
}
""");
    }

    [Fact]
    public void Modifiers()
    {
        AssertHighlighter("solidity",
"""
modifier onlyOwner() {
    require(msg.sender == owner, "Not owner");
    _;
}

function withdraw() external onlyOwner nonReentrant {
    (bool success, ) = payable(msg.sender).call{value: address(this).balance}("");
    require(success);
}

receive() external payable {}
fallback() external payable {}
""",
"""
<span class="hljs-keyword">modifier</span> <span class="hljs-title function_">onlyOwner</span>() {
    <span class="hljs-built_in">require</span>(<span class="hljs-built_in">msg</span>.sender == owner, <span class="hljs-string">&quot;Not owner&quot;</span>);
    _;
}

<span class="hljs-keyword">function</span> <span class="hljs-title function_">withdraw</span>() <span class="hljs-keyword">external</span> onlyOwner nonReentrant {
    (<span class="hljs-type">bool</span> success, ) = <span class="hljs-keyword">payable</span>(<span class="hljs-built_in">msg</span>.sender).call{value: <span class="hljs-type">address</span>(<span class="hljs-variable language_">this</span>).balance}(<span class="hljs-string">&quot;&quot;</span>);
    <span class="hljs-built_in">require</span>(success);
}

<span class="hljs-keyword">receive</span>() <span class="hljs-keyword">external</span> <span class="hljs-keyword">payable</span> {}
<span class="hljs-keyword">fallback</span>() <span class="hljs-keyword">external</span> <span class="hljs-keyword">payable</span> {}
""");
    }

    [Fact]
    public void Globals()
    {
        AssertHighlighter("solidity",
"""
function g() public payable virtual override returns (bytes32) {
    uint256 t = block.timestamp + 1 days;
    uint256 fee = 1 ether + 5 gwei + 100 wei;
    address o = tx.origin;
    bytes memory data = abi.encodePacked(msg.sender, msg.value);
    bytes32 h = keccak256(data);
    assert(gasleft() > 0);
    super.g();
    delete balances[o];
    return h;
}
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">g</span>() <span class="hljs-keyword">public</span> <span class="hljs-keyword">payable</span> <span class="hljs-keyword">virtual</span> <span class="hljs-keyword">override</span> <span class="hljs-keyword">returns</span> (<span class="hljs-type">bytes32</span>) {
    <span class="hljs-type">uint256</span> t = <span class="hljs-built_in">block</span>.timestamp + <span class="hljs-number">1</span> <span class="hljs-literal">days</span>;
    <span class="hljs-type">uint256</span> fee = <span class="hljs-number">1</span> <span class="hljs-literal">ether</span> + <span class="hljs-number">5</span> <span class="hljs-literal">gwei</span> + <span class="hljs-number">100</span> <span class="hljs-literal">wei</span>;
    <span class="hljs-type">address</span> o = <span class="hljs-built_in">tx</span>.origin;
    <span class="hljs-type">bytes</span> <span class="hljs-keyword">memory</span> data = <span class="hljs-built_in">abi</span>.<span class="hljs-title function_ invoke__">encodePacked</span>(<span class="hljs-built_in">msg</span>.sender, <span class="hljs-built_in">msg</span>.value);
    <span class="hljs-type">bytes32</span> h = <span class="hljs-built_in">keccak256</span>(data);
    <span class="hljs-built_in">assert</span>(<span class="hljs-built_in">gasleft</span>() &gt; <span class="hljs-number">0</span>);
    <span class="hljs-variable language_">super</span>.<span class="hljs-title function_ invoke__">g</span>();
    <span class="hljs-keyword">delete</span> balances[o];
    <span class="hljs-keyword">return</span> h;
}
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("solidity",
"""
uint a = 42;
uint b = 1_000_000;
uint c = 0xff_ff;
uint d = 2e10;
uint e = 1.5e18;
uint f = .5 ether;
uint g = 1e-3;
address h = 0x5B38Da6a701c568545dCfcB03FcB875f56beddC4;
""",
"""
<span class="hljs-type">uint</span> a = <span class="hljs-number">42</span>;
<span class="hljs-type">uint</span> b = <span class="hljs-number">1_000_000</span>;
<span class="hljs-type">uint</span> c = <span class="hljs-number">0xff_ff</span>;
<span class="hljs-type">uint</span> d = <span class="hljs-number">2e10</span>;
<span class="hljs-type">uint</span> e = <span class="hljs-number">1.5e18</span>;
<span class="hljs-type">uint</span> f = <span class="hljs-number">.5</span> <span class="hljs-literal">ether</span>;
<span class="hljs-type">uint</span> g = <span class="hljs-number">1e-3</span>;
<span class="hljs-type">address</span> h = <span class="hljs-number">0x5B38Da6a701c568545dCfcB03FcB875f56beddC4</span>;
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("solidity",
"""
string a = "double \"quoted\" \n";
string b = 'single \'quoted\'';
bytes c = hex"00112233";
bytes d = hex'aabb';
string e = unicode"Hello 😃";
string f = "unterminated
uint next = 1;
""",
"""
<span class="hljs-type">string</span> a = <span class="hljs-string">&quot;double \&quot;quoted\&quot; \n&quot;</span>;
<span class="hljs-type">string</span> b = <span class="hljs-string">&#x27;single \&#x27;quoted\&#x27;&#x27;</span>;
<span class="hljs-type">bytes</span> c = <span class="hljs-string">hex&quot;00112233&quot;</span>;
<span class="hljs-type">bytes</span> d = <span class="hljs-string">hex&#x27;aabb&#x27;</span>;
<span class="hljs-type">string</span> e = <span class="hljs-string">unicode&quot;Hello 😃&quot;</span>;
<span class="hljs-type">string</span> f = <span class="hljs-string">&quot;unterminated</span>
<span class="hljs-type">uint</span> next = <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("solidity",
"""
// line comment
/* block
   comment */
/**/
/// @title A simple storage
/// @author Jane Doe
/// @notice Stores a value
/**
 * @dev Returns the value.
 * @param x The value.
 * @return The stored value.
 * @inheritdoc IStorage
 * @custom:security-contact security@example.com
 */
// @notice not NatSpec
""",
"""
<span class="hljs-comment">// line comment</span>
<span class="hljs-comment">/* block
   comment */</span>
<span class="hljs-comment">/**/</span>
<span class="hljs-comment">/// <span class="hljs-doctag">@title</span> A simple storage</span>
<span class="hljs-comment">/// <span class="hljs-doctag">@author</span> Jane Doe</span>
<span class="hljs-comment">/// <span class="hljs-doctag">@notice</span> Stores a value</span>
<span class="hljs-comment">/**
 * <span class="hljs-doctag">@dev</span> Returns the value.
 * <span class="hljs-doctag">@param</span> x The value.
 * <span class="hljs-doctag">@return</span> The stored value.
 * <span class="hljs-doctag">@inheritdoc</span> IStorage
 * <span class="hljs-doctag">@custom:security-contact</span> security@example.com
 */</span>
<span class="hljs-comment">// @notice not NatSpec</span>
""");
    }

    [Fact]
    public void Assembly()
    {
        AssertHighlighter("solidity",
"""
function sum(uint256[] memory data) public pure returns (uint256 result) {
    assembly ("memory-safe") {
        // Yul comment
        let len := mload(data)
        let ptr := add(data, 0x20)
        for { let i := 0 } lt(i, len) { i := add(i, 1) } {
            result := add(result, mload(add(ptr, mul(i, 32))))
        }
        switch result
        case 0 { revert(0, 0) }
        default { mstore(0x40, result) }
        function double(x) -> y {
            y := mul(x, 2)
        }
        let s := sload(owner.slot)
        if iszero(s) { leave }
    }
    uint256 after = result;
}
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">sum</span>(<span class="hljs-type">uint256</span>[] <span class="hljs-keyword">memory</span> data) <span class="hljs-keyword">public</span> <span class="hljs-keyword">pure</span> <span class="hljs-keyword">returns</span> (<span class="hljs-type">uint256</span> result) {
    <span class="hljs-keyword">assembly</span> (<span class="hljs-string">&quot;memory-safe&quot;</span>) {
        <span class="hljs-comment">// Yul comment</span>
        <span class="hljs-keyword">let</span> len := <span class="hljs-built_in">mload</span>(data)
        <span class="hljs-keyword">let</span> ptr := <span class="hljs-built_in">add</span>(data, <span class="hljs-number">0x20</span>)
        <span class="hljs-keyword">for</span> { <span class="hljs-keyword">let</span> i := <span class="hljs-number">0</span> } <span class="hljs-built_in">lt</span>(i, len) { i := <span class="hljs-built_in">add</span>(i, <span class="hljs-number">1</span>) } {
            result := <span class="hljs-built_in">add</span>(result, <span class="hljs-built_in">mload</span>(<span class="hljs-built_in">add</span>(ptr, <span class="hljs-built_in">mul</span>(i, <span class="hljs-number">32</span>))))
        }
        <span class="hljs-keyword">switch</span> result
        <span class="hljs-keyword">case</span> <span class="hljs-number">0</span> { <span class="hljs-built_in">revert</span>(<span class="hljs-number">0</span>, <span class="hljs-number">0</span>) }
        <span class="hljs-keyword">default</span> { <span class="hljs-built_in">mstore</span>(<span class="hljs-number">0x40</span>, result) }
        <span class="hljs-keyword">function</span> <span class="hljs-title function_">double</span>(x) -&gt; y {
            y := <span class="hljs-built_in">mul</span>(x, <span class="hljs-number">2</span>)
        }
        <span class="hljs-keyword">let</span> s := <span class="hljs-built_in">sload</span>(owner.slot)
        <span class="hljs-keyword">if</span> <span class="hljs-built_in">iszero</span>(s) { <span class="hljs-keyword">leave</span> }
    }
    <span class="hljs-type">uint256</span> after = result;
}
""");
    }

    [Fact]
    public void ControlFlow()
    {
        AssertHighlighter("solidity",
"""
function h(uint n) public {
    for (uint i = 0; i < n; i++) {
        if (i == 2) continue;
        else if (i > 5) break;
    }
    while (n > 0) { n--; }
    do { n++; } while (n < 3);
    unchecked { n += 1; }
    try token.transfer(to, 1) returns (bool ok) {
    } catch Error(string memory reason) {
    } catch (bytes memory lowLevel) {
    }
    Proposal memory p = Proposal({name: "x", voteCount: 0});
    uint max = type(uint256).max;
    using SafeMath for uint256;
    new Contract();
}
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">h</span>(<span class="hljs-type">uint</span> n) <span class="hljs-keyword">public</span> {
    <span class="hljs-keyword">for</span> (<span class="hljs-type">uint</span> i = <span class="hljs-number">0</span>; i &lt; n; i++) {
        <span class="hljs-keyword">if</span> (i == <span class="hljs-number">2</span>) <span class="hljs-keyword">continue</span>;
        <span class="hljs-keyword">else</span> <span class="hljs-keyword">if</span> (i &gt; <span class="hljs-number">5</span>) <span class="hljs-keyword">break</span>;
    }
    <span class="hljs-keyword">while</span> (n &gt; <span class="hljs-number">0</span>) { n--; }
    <span class="hljs-keyword">do</span> { n++; } <span class="hljs-keyword">while</span> (n &lt; <span class="hljs-number">3</span>);
    <span class="hljs-keyword">unchecked</span> { n += <span class="hljs-number">1</span>; }
    <span class="hljs-keyword">try</span> token.<span class="hljs-title function_ invoke__">transfer</span>(to, <span class="hljs-number">1</span>) <span class="hljs-keyword">returns</span> (<span class="hljs-type">bool</span> ok) {
    } <span class="hljs-keyword">catch</span> <span class="hljs-title function_ invoke__">Error</span>(<span class="hljs-type">string</span> <span class="hljs-keyword">memory</span> reason) {
    } <span class="hljs-keyword">catch</span> (<span class="hljs-type">bytes</span> <span class="hljs-keyword">memory</span> lowLevel) {
    }
    Proposal <span class="hljs-keyword">memory</span> p = <span class="hljs-title function_ invoke__">Proposal</span>({name: <span class="hljs-string">&quot;x&quot;</span>, voteCount: <span class="hljs-number">0</span>});
    <span class="hljs-type">uint</span> max = <span class="hljs-keyword">type</span>(<span class="hljs-type">uint256</span>).max;
    <span class="hljs-keyword">using</span> SafeMath <span class="hljs-keyword">for</span> <span class="hljs-type">uint256</span>;
    <span class="hljs-keyword">new</span> <span class="hljs-title function_ invoke__">Contract</span>();
}
""");
    }
}
