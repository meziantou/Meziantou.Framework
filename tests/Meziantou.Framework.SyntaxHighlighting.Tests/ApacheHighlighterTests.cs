namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class ApacheHighlighterTests
{
    [Fact]
    public void VirtualHost()
    {
        AssertHighlighter("apache",
"""
# Virtual host
<VirtualHost *:80>
    ServerName www.example.com
    ServerAlias example.com
    DocumentRoot "/var/www/html"
    ErrorLog ${APACHE_LOG_DIR}/error.log
    CustomLog ${APACHE_LOG_DIR}/access.log combined

    <Directory "/var/www/html">
        Options -Indexes +FollowSymLinks
        AllowOverride All
        Require all granted
    </Directory>
</VirtualHost>
""",
"""
<span class="hljs-comment"># Virtual host</span>
<span class="hljs-section">&lt;VirtualHost *<span class="hljs-number">:80</span>&gt;</span>
    <span class="hljs-attribute">ServerName</span> www.example.com
    <span class="hljs-attribute">ServerAlias</span> example.com
    <span class="hljs-attribute">DocumentRoot</span> <span class="hljs-string">&quot;/var/www/html&quot;</span>
    <span class="hljs-attribute">ErrorLog</span> <span class="hljs-variable">${APACHE_LOG_DIR}</span>/error.log
    <span class="hljs-attribute">CustomLog</span> <span class="hljs-variable">${APACHE_LOG_DIR}</span>/access.log combined

    <span class="hljs-section">&lt;Directory <span class="hljs-string">&quot;/var/www/html&quot;</span>&gt;</span>
        <span class="hljs-attribute">Options</span> -Indexes +FollowSymLinks
        <span class="hljs-attribute">AllowOverride</span> <span class="hljs-literal">All</span>
        <span class="hljs-attribute">Require</span> <span class="hljs-literal">all</span> granted
    <span class="hljs-section">&lt;/Directory&gt;</span>
<span class="hljs-section">&lt;/VirtualHost&gt;</span>
""");
    }

    [Fact]
    public void RewriteRules()
    {
        AssertHighlighter("apache",
"""
RewriteEngine On
RewriteBase /
RewriteCond %{HTTPS} off
RewriteCond %{HTTP_HOST} ^www\.(.*)$ [NC]
RewriteRule ^(.*)$ https://%1/$1 [R=301,L]
RewriteRule ^index\.php$ - [L]
RewriteCond %{REQUEST_FILENAME} !-f
RewriteRule . /index.php [L]
""",
"""
<span class="hljs-attribute">RewriteEngine</span> <span class="hljs-literal">On</span>
<span class="hljs-attribute">RewriteBase</span> /
<span class="hljs-attribute">RewriteCond</span> <span class="hljs-variable">%{HTTPS}</span> <span class="hljs-literal">off</span>
<span class="hljs-attribute">RewriteCond</span> <span class="hljs-variable">%{HTTP_HOST}</span> ^www\.(.*)$<span class="hljs-meta"> [NC]</span>
<span class="hljs-attribute">RewriteRule</span> ^(.*)$ https://%<span class="hljs-number">1</span>/$<span class="hljs-number">1</span><span class="hljs-meta"> [R=301,L]</span>
<span class="hljs-attribute">RewriteRule</span> ^index\.php$ -<span class="hljs-meta"> [L]</span>
<span class="hljs-attribute">RewriteCond</span> <span class="hljs-variable">%{REQUEST_FILENAME}</span> !-f
<span class="hljs-attribute">RewriteRule</span> . /index.php<span class="hljs-meta"> [L]</span>
""");
    }

    [Fact]
    public void AccessControlAndAddresses()
    {
        AssertHighlighter("apache",
"""
Order deny,allow
Deny from all
Allow from 192.168.0.1 10.0.0.0/8
Listen 127.0.0.1:8080
Listen 443
Timeout 300
KeepAlive On
LoadModule rewrite_module modules/mod_rewrite.so
<IfModule mod_ssl.c>
  SSLEngine on
</IfModule>
<VirtualHost 10.1.2.3:443>
</VirtualHost>
""",
"""
<span class="hljs-attribute">Order</span> <span class="hljs-literal">deny</span>,<span class="hljs-literal">allow</span>
<span class="hljs-attribute">Deny</span> from <span class="hljs-literal">all</span>
<span class="hljs-attribute">Allow</span> from <span class="hljs-number">192.168.0.1</span> <span class="hljs-number">10.0.0.0</span>/<span class="hljs-number">8</span>
<span class="hljs-attribute">Listen</span> <span class="hljs-number">127.0.0.1:8080</span>
<span class="hljs-attribute">Listen</span> <span class="hljs-number">443</span>
<span class="hljs-attribute">Timeout</span> <span class="hljs-number">300</span>
<span class="hljs-attribute">KeepAlive</span> <span class="hljs-literal">On</span>
<span class="hljs-attribute">LoadModule</span> rewrite_module modules/mod_rewrite.so
<span class="hljs-section">&lt;IfModule mod_ssl.c&gt;</span>
  <span class="hljs-attribute">SSLEngine</span> <span class="hljs-literal">on</span>
<span class="hljs-section">&lt;/IfModule&gt;</span>
<span class="hljs-section">&lt;VirtualHost <span class="hljs-number">10.1.2.3:443</span>&gt;</span>
<span class="hljs-section">&lt;/VirtualHost&gt;</span>
""");
    }

    [Fact]
    public void HeadersAndStrings()
    {
        AssertHighlighter("apache",
"""
Header set X-Frame-Options "SAMEORIGIN"
Header always set Strict-Transport-Security "max-age=63072000; includeSubDomains"
ErrorDocument 404 /404.html
SetEnvIf User-Agent ".*MSIE.*" nokeepalive
AddType application/x-httpd-php .php
""",
"""
<span class="hljs-attribute">Header</span> set X-Frame-Options <span class="hljs-string">&quot;SAMEORIGIN&quot;</span>
<span class="hljs-attribute">Header</span> always set Strict-Transport-Security <span class="hljs-string">&quot;max-age=63072000; includeSubDomains&quot;</span>
<span class="hljs-attribute">ErrorDocument</span> <span class="hljs-number">404</span> /<span class="hljs-number">404</span>.html
<span class="hljs-attribute">SetEnvIf</span> User-Agent <span class="hljs-string">&quot;.*MSIE.*&quot;</span> nokeepalive
<span class="hljs-attribute">AddType</span> application/x-httpd-php .php
""");
    }

    [Fact]
    public void LineContinuation()
    {
        AssertHighlighter("apache",
"""
RewriteRule ^old/(.*)$ \
    /new/$1 [R=301,L]
ServerName x
""",
"""
<span class="hljs-attribute">RewriteRule</span> ^old/(.*)$ <span class="hljs-punctuation">\
</span>    /new/$<span class="hljs-number">1</span><span class="hljs-meta"> [R=301,L]</span>
<span class="hljs-attribute">ServerName</span> x
""");
    }

    [Fact]
    public void LineContinuationInString()
    {
        AssertHighlighter("apache",
"""
Header always set Content-Security-Policy "default-src 'self'; \
    script-src 'self'" env=HTTPS
ServerName x
""",
"""
<span class="hljs-attribute">Header</span> always set Content-Security-Policy <span class="hljs-string">&quot;default-src &#x27;self&#x27;; \
    script-src &#x27;self&#x27;&quot;</span> env=HTTPS
<span class="hljs-attribute">ServerName</span> x
""");
    }

    [Fact]
    public void NestedVariables()
    {
        AssertHighlighter("apache",
"""
RewriteCond %{ENV:%{X}} y
RewriteRule ^ ${map:%1} [L]
""",
"""
<span class="hljs-attribute">RewriteCond</span> <span class="hljs-variable">%{ENV:<span class="hljs-variable">%{X}</span>}</span> y
<span class="hljs-attribute">RewriteRule</span> ^ <span class="hljs-variable">${map:<span class="hljs-number">%1</span>}</span><span class="hljs-meta"> [L]</span>
""");
    }

    [Fact]
    public void FlagsEndAtEndOfDirective()
    {
        AssertHighlighter("apache",
"""
RewriteRule [0-9]+ x
RewriteRule ^ - [L] # comment
ServerName a
Listen 80
""",
"""
<span class="hljs-attribute">RewriteRule</span><span class="hljs-meta"> [0-9]+ x</span>
<span class="hljs-attribute">RewriteRule</span> ^ -<span class="hljs-meta"> [L] # comment</span>
<span class="hljs-attribute">ServerName</span> a
<span class="hljs-attribute">Listen</span> <span class="hljs-number">80</span>
""");
    }

    [Fact]
    public void VariableEndsAtEndOfDirective()
    {
        AssertHighlighter("apache",
"""
RewriteCond %{HTTP_HOST x
ServerName a
Listen 80
""",
"""
<span class="hljs-attribute">RewriteCond</span> <span class="hljs-variable">%{HTTP_HOST x</span>
<span class="hljs-attribute">ServerName</span> a
<span class="hljs-attribute">Listen</span> <span class="hljs-number">80</span>
""");
    }

    [Fact]
    public void UnterminatedSection()
    {
        AssertHighlighter("apache",
"""
<Directory "/x"
ServerName a
""",
"""
<span class="hljs-section">&lt;Directory <span class="hljs-string">&quot;/x&quot;</span>
ServerName a</span>
""");
    }

    [Fact]
    public void CaseInsensitive()
    {
        AssertHighlighter("apache",
"""
SERVERNAME x ON
servername y Off
""",
"""
<span class="hljs-attribute">SERVERNAME</span> x <span class="hljs-literal">ON</span>
<span class="hljs-attribute">servername</span> y <span class="hljs-literal">Off</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("apache",
"""
Options None # not a comment
  # indented comment
""",
"""
<span class="hljs-attribute">Options</span> None # not a comment
  <span class="hljs-comment"># indented comment</span>
""");
    }

    [Fact]
    public void ApacheconfAlias()
    {
        AssertHighlighter("apacheconf",
"""
ServerName x
""",
"""
<span class="hljs-attribute">ServerName</span> x
""");
    }

    [Fact]
    public void Empty()
    {
        AssertHighlighter("apache", "", "");
    }
}
