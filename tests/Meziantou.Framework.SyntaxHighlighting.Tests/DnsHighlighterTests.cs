namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class DnsHighlighterTests
{
    [Fact]
    public void ZoneFile()
    {
        AssertHighlighter("dns",
"""
$ORIGIN example.com.
$TTL 3600
; SOA record
@   IN  SOA ns1.example.com. admin.example.com. (
        2024010101 ; serial
        7200       ; refresh
        3600       ; retry
        1209600    ; expire
        3600 )     ; minimum

@       IN  NS    ns1.example.com.
@       IN  NS    ns2.example.com.
@       IN  MX    10 mail.example.com.
@       IN  A     192.0.2.1
@       IN  AAAA  2001:db8::1
www     IN  CNAME example.com.
mail    IN  A     192.0.2.2
ftp  1h IN  A     198.51.100.7
@       IN  TXT   "v=spf1 mx -all"
_sip._tcp IN SRV  10 60 5060 sip.example.com.
""",
"""
<span class="hljs-meta">$ORIGIN</span> example.com.
<span class="hljs-meta">$TTL</span> <span class="hljs-number">3600</span>
<span class="hljs-comment">; SOA record</span>
@   <span class="hljs-keyword">IN</span>  <span class="hljs-keyword">SOA</span> ns1.example.com. admin.example.com. (
        <span class="hljs-number">2024010101</span> <span class="hljs-comment">; serial</span>
        <span class="hljs-number">7200</span>       <span class="hljs-comment">; refresh</span>
        <span class="hljs-number">3600</span>       <span class="hljs-comment">; retry</span>
        <span class="hljs-number">1209600</span>    <span class="hljs-comment">; expire</span>
        <span class="hljs-number">3600</span> )     <span class="hljs-comment">; minimum</span>

@       <span class="hljs-keyword">IN</span>  <span class="hljs-keyword">NS</span>    ns1.example.com.
@       <span class="hljs-keyword">IN</span>  <span class="hljs-keyword">NS</span>    ns2.example.com.
@       <span class="hljs-keyword">IN</span>  <span class="hljs-keyword">MX</span>    <span class="hljs-number">10</span> mail.example.com.
@       <span class="hljs-keyword">IN</span>  <span class="hljs-keyword">A</span>     <span class="hljs-number">192.0.2.1</span>
@       <span class="hljs-keyword">IN</span>  <span class="hljs-keyword">AAAA</span>  <span class="hljs-number">2001:db8::1</span>
www     <span class="hljs-keyword">IN</span>  <span class="hljs-keyword">CNAME</span> example.com.
mail    <span class="hljs-keyword">IN</span>  <span class="hljs-keyword">A</span>     <span class="hljs-number">192.0.2.2</span>
ftp  <span class="hljs-number">1h</span> <span class="hljs-keyword">IN</span>  <span class="hljs-keyword">A</span>     <span class="hljs-number">198.51.100.7</span>
@       <span class="hljs-keyword">IN</span>  <span class="hljs-keyword">TXT</span>   &quot;v=spf1 mx -all&quot;
_sip._tcp <span class="hljs-keyword">IN</span> <span class="hljs-keyword">SRV</span>  <span class="hljs-number">10</span> <span class="hljs-number">60</span> <span class="hljs-number">5060</span> sip.example.com.
""");
    }

    [Fact]
    public void Ipv6Addresses()
    {
        AssertHighlighter("dns",
"""
a IN AAAA ::1
b IN AAAA fe80::1ff:fe23:4567:890a
c IN AAAA 2001:0db8:85a3:0000:0000:8a2e:0370:7334
d IN AAAA ::ffff:192.0.2.128
""",
"""
a <span class="hljs-keyword">IN</span> <span class="hljs-keyword">AAAA</span> <span class="hljs-number">::1</span>
b <span class="hljs-keyword">IN</span> <span class="hljs-keyword">AAAA</span> <span class="hljs-number">fe80::1ff:fe23:4567:890a</span>
c <span class="hljs-keyword">IN</span> <span class="hljs-keyword">AAAA</span> <span class="hljs-number">2001:0db8:85a3:0000:0000:8a2e:0370:7334</span>
d <span class="hljs-keyword">IN</span> <span class="hljs-keyword">AAAA</span> <span class="hljs-number">::ffff:192</span>.<span class="hljs-number">0</span>.<span class="hljs-number">2</span>.<span class="hljs-number">128</span>
""");
    }

    [Fact]
    public void NumbersAndDirectives()
    {
        AssertHighlighter("dns",
"""
@ IN SOA ns1 admin ( 1 7200 3600 1209600 3600 )
$INCLUDE /etc/bind/other.zone
$GENERATE 1-10 host$ A 10.0.0.$
x IN A 10 20 30 40
""",
"""
@ <span class="hljs-keyword">IN</span> <span class="hljs-keyword">SOA</span> ns1 admin ( <span class="hljs-number">1</span> <span class="hljs-number">7200</span> <span class="hljs-number">3600</span> <span class="hljs-number">1209600</span> <span class="hljs-number">3600</span> )
<span class="hljs-meta">$INCLUDE</span> /etc/bind/other.zone
<span class="hljs-meta">$GENERATE</span> <span class="hljs-number">1</span>-<span class="hljs-number">10</span> host$ <span class="hljs-keyword">A</span> <span class="hljs-number">10</span>.<span class="hljs-number">0</span>.<span class="hljs-number">0</span>.$
x <span class="hljs-keyword">IN</span> <span class="hljs-keyword">A</span> <span class="hljs-number">10</span> <span class="hljs-number">20</span> <span class="hljs-number">30</span> <span class="hljs-number">40</span>
""");
    }

    [Fact]
    public void ZoneAlias()
    {
        AssertHighlighter("zone",
"""
@ IN NS ns1.
""",
"""
@ <span class="hljs-keyword">IN</span> <span class="hljs-keyword">NS</span> ns1.
""");
    }

    [Fact]
    public void Empty()
    {
        AssertHighlighter("dns", "", "");
    }
}
