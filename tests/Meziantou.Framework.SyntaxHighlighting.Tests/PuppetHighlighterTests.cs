namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class PuppetHighlighterTests
{
    [Fact]
    public void ClassWithResources()
    {
        AssertHighlighter("puppet",
"""
# Manage nginx
class nginx (
  String $version = 'latest',
  Integer $port = 80,
) inherits nginx::params {
  package { 'nginx':
    ensure => $version,
  }

  file { '/etc/nginx/nginx.conf':
    ensure  => file,
    owner   => 'root',
    group   => 'root',
    mode    => '0644',
    content => template('nginx/nginx.conf.erb'),
    require => Package['nginx'],
    notify  => Service['nginx'],
  }

  service { 'nginx':
    ensure => running,
    enable => true,
  }
}
""",
"""
<span class="hljs-comment"># Manage nginx</span>
<span class="hljs-keyword">class</span> <span class="hljs-title">nginx</span> (
  <span class="hljs-title">String</span> <span class="hljs-variable">$version</span> = <span class="hljs-string">&#x27;latest&#x27;</span>,
  <span class="hljs-title">Integer</span> <span class="hljs-variable">$port</span> = 80,
) <span class="hljs-title">inherits</span> <span class="hljs-title">nginx::params</span> {
  <span class="hljs-keyword">package</span> { <span class="hljs-string">&#x27;nginx&#x27;</span>:
    <span class="hljs-attr">ensure</span> =&gt; <span class="hljs-variable">$version</span>,
  }

  <span class="hljs-keyword">file</span> { <span class="hljs-string">&#x27;/etc/nginx/nginx.conf&#x27;</span>:
    <span class="hljs-attr">ensure</span>  =&gt; file,
    <span class="hljs-attr">owner</span>   =&gt; <span class="hljs-string">&#x27;root&#x27;</span>,
    <span class="hljs-attr">group</span>   =&gt; <span class="hljs-string">&#x27;root&#x27;</span>,
    <span class="hljs-attr">mode</span>    =&gt; <span class="hljs-string">&#x27;0644&#x27;</span>,
    <span class="hljs-attr">content</span> =&gt; template(<span class="hljs-string">&#x27;nginx/nginx.conf.erb&#x27;</span>),
    <span class="hljs-attr">require</span> =&gt; Package[<span class="hljs-string">&#x27;nginx&#x27;</span>],
    <span class="hljs-attr">notify</span>  =&gt; Service[<span class="hljs-string">&#x27;nginx&#x27;</span>],
  }

  <span class="hljs-keyword">service</span> { <span class="hljs-string">&#x27;nginx&#x27;</span>:
    <span class="hljs-attr">ensure</span> =&gt; <span class="hljs-literal">running</span>,
    <span class="hljs-attr">enable</span> =&gt; <span class="hljs-keyword">true</span>,
  }
}
""");
    }

    [Fact]
    public void DefinedType()
    {
        AssertHighlighter("puppet",
"""
define apache::vhost (
  $port,
  $docroot,
) {
  file { "/etc/apache2/sites-enabled/${title}.conf":
    ensure  => present,
    content => "port $port",
  }
}
""",
"""
<span class="hljs-keyword">define</span> <span class="hljs-section">apache</span>::vhost (
  <span class="hljs-variable">$port</span>,
  <span class="hljs-variable">$docroot</span>,
) {
  <span class="hljs-keyword">file</span> { <span class="hljs-string">&quot;/etc/apache2/sites-enabled/${title}.conf&quot;</span>:
    <span class="hljs-attr">ensure</span>  =&gt; <span class="hljs-literal">present</span>,
    <span class="hljs-attr">content</span> =&gt; <span class="hljs-string">&quot;port <span class="hljs-variable">$port</span>&quot;</span>,
  }
}
""");
    }

    [Fact]
    public void NodeDefinition()
    {
        AssertHighlighter("puppet",
"""
node 'web01.example.com' {
  include nginx
  class { 'ntp':
    servers => ['0.pool.ntp.org'],
  }
}
""",
"""
node <span class="hljs-string">&#x27;web01.example.com&#x27;</span> {
  include nginx
  <span class="hljs-keyword">class</span> { <span class="hljs-string">&#x27;ntp&#x27;</span>:
    servers =&gt; [<span class="hljs-string">&#x27;0.pool.ntp.org&#x27;</span>],
  }
}
""");
    }

    [Fact]
    public void CodeAfterResource()
    {
        AssertHighlighter("puppet",
"""
file { 'a': ensure => present }
$x = 1
include foo
# comment
exec { 'refresh':
  command     => '/usr/bin/true',
  refreshonly => true,
  timeout     => 300,
  tries       => 0x1F,
}
""",
"""
<span class="hljs-keyword">file</span> { <span class="hljs-string">&#x27;a&#x27;</span>: <span class="hljs-attr">ensure</span> =&gt; <span class="hljs-literal">present</span> }
<span class="hljs-variable">$x</span> = 1
include foo
<span class="hljs-comment"># comment</span>
<span class="hljs-keyword">exec</span> { <span class="hljs-string">&#x27;refresh&#x27;</span>:
  <span class="hljs-attr">command</span>     =&gt; <span class="hljs-string">&#x27;/usr/bin/true&#x27;</span>,
  <span class="hljs-attr">refreshonly</span> =&gt; <span class="hljs-keyword">true</span>,
  <span class="hljs-attr">timeout</span>     =&gt; <span class="hljs-number">300</span>,
  <span class="hljs-attr">tries</span>       =&gt; <span class="hljs-number">0x1F</span>,
}
""");
    }

    [Fact]
    public void Conditionals()
    {
        AssertHighlighter("puppet",
"""
if $facts['os']['family'] == 'RedHat' {
  $pkg = 'httpd'
} elsif $osfamily == 'Debian' {
  $pkg = 'apache2'
} else {
  fail('Unsupported')
}
case $::operatingsystem {
  'CentOS': { $x = 1 }
  default:  { $x = 2 }
}
""",
"""
if <span class="hljs-variable">$facts</span>[<span class="hljs-string">&#x27;os&#x27;</span>][<span class="hljs-string">&#x27;family&#x27;</span>] == <span class="hljs-string">&#x27;RedHat&#x27;</span> {
  <span class="hljs-variable">$pkg</span> = <span class="hljs-string">&#x27;httpd&#x27;</span>
} elsif <span class="hljs-variable">$osfamily</span> == <span class="hljs-string">&#x27;Debian&#x27;</span> {
  <span class="hljs-variable">$pkg</span> = <span class="hljs-string">&#x27;apache2&#x27;</span>
} <span class="hljs-keyword">else</span> {
  fail(<span class="hljs-string">&#x27;Unsupported&#x27;</span>)
}
case <span class="hljs-variable">$::operatingsystem</span> {
  <span class="hljs-string">&#x27;CentOS&#x27;</span>: { <span class="hljs-variable">$x</span> = 1 }
  default:  { <span class="hljs-variable">$x</span> = 2 }
}
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("puppet",
"""
$a = "escaped \" ${b} $c"
$d = 'single \' quote'
$e = "unterminated
user { 'bob': uid => 1001, gid => 1001, shell => '/bin/bash' }
""",
"""
<span class="hljs-variable">$a</span> = <span class="hljs-string">&quot;escaped \&quot; ${b} <span class="hljs-variable">$c</span>&quot;</span>
<span class="hljs-variable">$d</span> = <span class="hljs-string">&#x27;single \&#x27; quote&#x27;</span>
<span class="hljs-variable">$e</span> = <span class="hljs-string">&quot;unterminated
user { &#x27;bob&#x27;: uid =&gt; 1001, gid =&gt; 1001, shell =&gt; &#x27;/bin/bash&#x27; }</span>
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("puppet",
"""
user { 'x': uid => 0755, gid => 12_000, x => 1.5, y => 0 }
""",
"""
<span class="hljs-keyword">user</span> { <span class="hljs-string">&#x27;x&#x27;</span>: <span class="hljs-attr">uid</span> =&gt; <span class="hljs-number">0755</span>, <span class="hljs-attr">gid</span> =&gt; <span class="hljs-number">12_000</span>, <span class="hljs-attr">x</span> =&gt; <span class="hljs-number">1.5</span>, <span class="hljs-attr">y</span> =&gt; <span class="hljs-number">0</span> }
""");
    }

    [Fact]
    public void PpAlias()
    {
        AssertHighlighter("pp",
"""
$x = 1
""",
"""
<span class="hljs-variable">$x</span> = 1
""");
    }

    [Fact]
    public void Empty()
    {
        AssertHighlighter("puppet", "", "");
    }
}
