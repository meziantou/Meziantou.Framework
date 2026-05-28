namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class NginxHighlighterTests
{

    [Fact]
    public void Directive_WorkerProcesses()
    {
        AssertHighlighter("nginx",
"""
worker_processes auto;
""",
"""
<span class="hljs-attribute">worker_processes</span> auto;
""");
    }

    [Fact]
    public void Directive_WorkerConnections()
    {
        AssertHighlighter("nginx",
"""
events {
  worker_connections 1024;
}
""",
"""
<span class="hljs-section">events</span> {
  <span class="hljs-attribute">worker_connections</span> <span class="hljs-number">1024</span>;
}
""");
    }

    [Fact]
    public void Directive_WorkerRlimitNofile()
    {
        AssertHighlighter("nginx",
"""
worker_rlimit_nofile 65535;
""",
"""
<span class="hljs-attribute">worker_rlimit_nofile</span> <span class="hljs-number">65535</span>;
""");
    }

    [Fact]
    public void Directive_ErrorLog()
    {
        AssertHighlighter("nginx",
"""
error_log /var/log/nginx/error.log warn;
""",
"""
<span class="hljs-attribute">error_log</span> /var/log/nginx/<span class="hljs-literal">error</span>.log <span class="hljs-literal">warn</span>;
""");
    }

    [Fact]
    public void Directive_AccessLog()
    {
        AssertHighlighter("nginx",
"""
access_log /var/log/nginx/access.log combined;
""",
"""
<span class="hljs-attribute">access_log</span> /var/log/nginx/access.log combined;
""");
    }

    [Fact]
    public void Directive_AccessLogOff()
    {
        AssertHighlighter("nginx",
"""
access_log off;
""",
"""
<span class="hljs-attribute">access_log</span> <span class="hljs-literal">off</span>;
""");
    }

    [Fact]
    public void Directive_Listen()
    {
        AssertHighlighter("nginx",
"""
listen 80;
""",
"""
<span class="hljs-attribute">listen</span> <span class="hljs-number">80</span>;
""");
    }

    [Fact]
    public void Directive_ListenIpPort()
    {
        AssertHighlighter("nginx",
"""
listen 127.0.0.1:8080;
""",
"""
<span class="hljs-attribute">listen</span> <span class="hljs-number">127.0.0.1:8080</span>;
""");
    }

    [Fact]
    public void Directive_ListenSslHttp2()
    {
        AssertHighlighter("nginx",
"""
listen 443 ssl http2;
""",
"""
<span class="hljs-attribute">listen</span> <span class="hljs-number">443</span> ssl http2;
""");
    }

    [Fact]
    public void Directive_ListenDefault()
    {
        AssertHighlighter("nginx",
"""
listen 80 default_server;
""",
"""
<span class="hljs-attribute">listen</span> <span class="hljs-number">80</span> default_server;
""");
    }

    [Fact]
    public void Directive_ListenIpv6()
    {
        AssertHighlighter("nginx",
"""
listen [::]:80;
""",
"""
<span class="hljs-attribute">listen</span> [::]:<span class="hljs-number">80</span>;
""");
    }

    [Fact]
    public void Directive_ServerName()
    {
        AssertHighlighter("nginx",
"""
server_name example.com www.example.com;
""",
"""
<span class="hljs-attribute">server_name</span> example.com www.example.com;
""");
    }

    [Fact]
    public void Directive_ServerNameWildcard()
    {
        AssertHighlighter("nginx",
"""
server_name *.example.com;
""",
"""
<span class="hljs-attribute">server_name</span> <span class="hljs-regexp">*.example.com</span>;
""");
    }

    [Fact]
    public void Directive_Root()
    {
        AssertHighlighter("nginx",
"""
root /var/www/html;
""",
"""
<span class="hljs-attribute">root</span> /var/www/html;
""");
    }

    [Fact]
    public void Directive_Index()
    {
        AssertHighlighter("nginx",
"""
index index.html index.htm;
""",
"""
<span class="hljs-attribute">index</span> index.html index.htm;
""");
    }

    [Fact]
    public void Directive_Return()
    {
        AssertHighlighter("nginx",
"""
return 301 https://$host$request_uri;
""",
"""
<span class="hljs-attribute">return</span> <span class="hljs-number">301</span> https://<span class="hljs-variable">$host</span><span class="hljs-variable">$request_uri</span>;
""");
    }

    [Fact]
    public void Directive_ErrorPage()
    {
        AssertHighlighter("nginx",
"""
error_page 404 /404.html;
""",
"""
<span class="hljs-attribute">error_page</span> <span class="hljs-number">404</span> /<span class="hljs-number">404</span>.html;
""");
    }

    [Fact]
    public void Directive_ErrorPageMulti()
    {
        AssertHighlighter("nginx",
"""
error_page 500 502 503 504 /50x.html;
""",
"""
<span class="hljs-attribute">error_page</span> <span class="hljs-number">500</span> <span class="hljs-number">502</span> <span class="hljs-number">503</span> <span class="hljs-number">504</span> /50x.html;
""");
    }

    [Fact]
    public void Directive_TryFiles()
    {
        AssertHighlighter("nginx",
"""
try_files $uri $uri/ /index.html;
""",
"""
<span class="hljs-attribute">try_files</span> <span class="hljs-variable">$uri</span> <span class="hljs-variable">$uri</span>/ /index.html;
""");
    }

    [Fact]
    public void Directive_TryFilesProxy()
    {
        AssertHighlighter("nginx",
"""
try_files $uri @proxy;
""",
"""
<span class="hljs-attribute">try_files</span> <span class="hljs-variable">$uri</span> <span class="hljs-variable">@proxy</span>;
""");
    }

    [Fact]
    public void Directive_Include()
    {
        AssertHighlighter("nginx",
"""
include /etc/nginx/conf.d/*.conf;
""",
"""
<span class="hljs-attribute">include</span> /etc/nginx/conf.d/<span class="hljs-regexp">*.conf</span>;
""");
    }

    [Fact]
    public void Directive_IncludeMimes()
    {
        AssertHighlighter("nginx",
"""
include mime.types;
""",
"""
<span class="hljs-attribute">include</span> mime.types;
""");
    }

    [Fact]
    public void Directive_DefaultType()
    {
        AssertHighlighter("nginx",
"""
default_type application/octet-stream;
""",
"""
<span class="hljs-attribute">default_type</span> application/octet-stream;
""");
    }

    [Fact]
    public void Directive_Sendfile()
    {
        AssertHighlighter("nginx",
"""
sendfile on;
""",
"""
<span class="hljs-attribute">sendfile</span> <span class="hljs-literal">on</span>;
""");
    }

    [Fact]
    public void Directive_TcpNopush()
    {
        AssertHighlighter("nginx",
"""
tcp_nopush on;
""",
"""
<span class="hljs-attribute">tcp_nopush</span> <span class="hljs-literal">on</span>;
""");
    }

    [Fact]
    public void Directive_TcpNodelay()
    {
        AssertHighlighter("nginx",
"""
tcp_nodelay on;
""",
"""
<span class="hljs-attribute">tcp_nodelay</span> <span class="hljs-literal">on</span>;
""");
    }

    [Fact]
    public void Directive_KeepaliveTimeout()
    {
        AssertHighlighter("nginx",
"""
keepalive_timeout 65;
""",
"""
<span class="hljs-attribute">keepalive_timeout</span> <span class="hljs-number">65</span>;
""");
    }

    [Fact]
    public void Directive_ClientMaxBodySize()
    {
        AssertHighlighter("nginx",
"""
client_max_body_size 50M;
""",
"""
<span class="hljs-attribute">client_max_body_size</span> <span class="hljs-number">50M</span>;
""");
    }

    [Fact]
    public void Directive_ServerTokens()
    {
        AssertHighlighter("nginx",
"""
server_tokens off;
""",
"""
<span class="hljs-attribute">server_tokens</span> <span class="hljs-literal">off</span>;
""");
    }

    [Fact]
    public void Directive_AddHeader()
    {
        AssertHighlighter("nginx",
"""
add_header X-Frame-Options DENY always;
""",
"""
<span class="hljs-attribute">add_header</span> X-Frame-Options DENY always;
""");
    }

    [Fact]
    public void Directive_AddHeaderCsp()
    {
        AssertHighlighter("nginx",
"""
add_header Content-Security-Policy "default-src 'self'" always;
""",
"""
<span class="hljs-attribute">add_header</span> Content-Security-Policy <span class="hljs-string">&quot;default-src &#x27;self&#x27;&quot;</span> always;
""");
    }

    [Fact]
    public void Directive_Charset()
    {
        AssertHighlighter("nginx",
"""
charset utf-8;
""",
"""
<span class="hljs-attribute">charset</span> utf-<span class="hljs-number">8</span>;
""");
    }

    [Fact]
    public void Directive_Rewrite()
    {
        AssertHighlighter("nginx",
"""
rewrite ^/old/(.*)$ /new/$1 permanent;
""",
"""
<span class="hljs-attribute">rewrite</span><span class="hljs-regexp"> ^/old/(.*)$</span> /new/<span class="hljs-variable">$1</span> <span class="hljs-literal">permanent</span>;
""");
    }

    [Fact]
    public void Directive_RewriteLast()
    {
        AssertHighlighter("nginx",
"""
rewrite ^/api/(.*)$ /v2/api/$1 last;
""",
"""
<span class="hljs-attribute">rewrite</span><span class="hljs-regexp"> ^/api/(.*)$</span> /v2/api/<span class="hljs-variable">$1</span> <span class="hljs-literal">last</span>;
""");
    }

    [Fact]
    public void Directive_IfCondition()
    {
        AssertHighlighter("nginx",
"""
if ($host = example.com) {
  return 301 https://www.example.com$request_uri;
}
""",
"""
<span class="hljs-attribute">if</span> (<span class="hljs-variable">$host</span> = example.com) {
  <span class="hljs-attribute">return</span> <span class="hljs-number">301</span> https://www.example.com<span class="hljs-variable">$request_uri</span>;
}
""");
    }

    [Fact]
    public void Directive_IfFileExists()
    {
        AssertHighlighter("nginx",
"""
if (-f $request_filename) {
  break;
}
""",
"""
if (-f $request_filename) {
  break;
}
""");
    }

    [Fact]
    public void Directive_SetVariable()
    {
        AssertHighlighter("nginx",
"""
set $mobile 0;
""",
"""
<span class="hljs-attribute">set</span> <span class="hljs-variable">$mobile</span> <span class="hljs-number">0</span>;
""");
    }

    [Fact]
    public void Directive_AllowDeny()
    {
        AssertHighlighter("nginx",
"""
allow 192.168.0.0/16;
deny  all;
""",
"""
<span class="hljs-attribute">allow</span> <span class="hljs-number">192.168.0.0</span>/<span class="hljs-number">16</span>;
<span class="hljs-attribute">deny</span>  all;
""");
    }

    [Fact]
    public void Directive_AuthBasic()
    {
        AssertHighlighter("nginx",
"""
auth_basic "Restricted";
auth_basic_user_file /etc/nginx/.htpasswd;
""",
"""
<span class="hljs-attribute">auth_basic</span> <span class="hljs-string">&quot;Restricted&quot;</span>;
<span class="hljs-attribute">auth_basic_user_file</span> /etc/nginx/.htpasswd;
""");
    }

    [Fact]
    public void Directive_Resolver()
    {
        AssertHighlighter("nginx",
"""
resolver 8.8.8.8 1.1.1.1 valid=300s ipv6=off;
""",
"""
<span class="hljs-attribute">resolver</span> <span class="hljs-number">8.8.8.8</span> <span class="hljs-number">1.1.1.1</span> valid=<span class="hljs-number">300s</span> ipv6=<span class="hljs-literal">off</span>;
""");
    }

    [Fact]
    public void Context_Events()
    {
        AssertHighlighter("nginx",
"""
events {
  worker_connections 1024;
  multi_accept on;
  use epoll;
}
""",
"""
<span class="hljs-section">events</span> {
  <span class="hljs-attribute">worker_connections</span> <span class="hljs-number">1024</span>;
  <span class="hljs-attribute">multi_accept</span> <span class="hljs-literal">on</span>;
  <span class="hljs-attribute">use</span> <span class="hljs-literal">epoll</span>;
}
""");
    }

    [Fact]
    public void Context_Http()
    {
        AssertHighlighter("nginx",
"""
http {
  include mime.types;
  default_type application/octet-stream;
  sendfile on;
}
""",
"""
<span class="hljs-section">http</span> {
  <span class="hljs-attribute">include</span> mime.types;
  <span class="hljs-attribute">default_type</span> application/octet-stream;
  <span class="hljs-attribute">sendfile</span> <span class="hljs-literal">on</span>;
}
""");
    }

    [Fact]
    public void Context_Server()
    {
        AssertHighlighter("nginx",
"""
server {
  listen 80;
  server_name example.com;
}
""",
"""
<span class="hljs-section">server</span> {
  <span class="hljs-attribute">listen</span> <span class="hljs-number">80</span>;
  <span class="hljs-attribute">server_name</span> example.com;
}
""");
    }

    [Fact]
    public void Context_LocationExact()
    {
        AssertHighlighter("nginx",
"""
location = /favicon.ico {
  log_not_found off;
  access_log off;
}
""",
"""
<span class="hljs-section">location</span> = /favicon.ico {
  <span class="hljs-attribute">log_not_found</span> <span class="hljs-literal">off</span>;
  <span class="hljs-attribute">access_log</span> <span class="hljs-literal">off</span>;
}
""");
    }

    [Fact]
    public void Context_LocationPrefix()
    {
        AssertHighlighter("nginx",
"""
location /static/ {
  root /var/www;
  expires 30d;
}
""",
"""
<span class="hljs-section">location</span> /static/ {
  <span class="hljs-attribute">root</span> /var/www;
  <span class="hljs-attribute">expires</span> <span class="hljs-number">30d</span>;
}
""");
    }

    [Fact]
    public void Context_LocationRegex()
    {
        AssertHighlighter("nginx",
"""
location ~ \.php$ {
  fastcgi_pass unix:/run/php-fpm.sock;
}
""",
"""
<span class="hljs-section">location</span> <span class="hljs-regexp">~ \.php$</span> {
  <span class="hljs-attribute">fastcgi_pass</span> unix:/run/php-fpm.sock;
}
""");
    }

    [Fact]
    public void Context_LocationRegexCaseInsens()
    {
        AssertHighlighter("nginx",
"""
location ~* \.(?:jpg|png|gif|svg|webp)$ {
  expires 1y;
  add_header Cache-Control "public, immutable";
}
""",
"""
<span class="hljs-section">location</span> <span class="hljs-regexp">~* \.(?:jpg|png|gif|svg|webp)$</span> {
  <span class="hljs-attribute">expires</span> <span class="hljs-number">1y</span>;
  <span class="hljs-attribute">add_header</span> Cache-Control <span class="hljs-string">&quot;public, immutable&quot;</span>;
}
""");
    }

    [Fact]
    public void Context_LocationLongestPrefix()
    {
        AssertHighlighter("nginx",
"""
location ^~ /admin/ {
  return 403;
}
""",
"""
<span class="hljs-section">location</span><span class="hljs-regexp"> ^~</span> /admin/ {
  <span class="hljs-attribute">return</span> <span class="hljs-number">403</span>;
}
""");
    }

    [Fact]
    public void Context_LocationNamed()
    {
        AssertHighlighter("nginx",
"""
location @proxy {
  proxy_pass http://backend;
}
""",
"""
<span class="hljs-section">location</span> <span class="hljs-variable">@proxy</span> {
  <span class="hljs-attribute">proxy_pass</span> http://backend;
}
""");
    }

    [Fact]
    public void Context_LocationNested()
    {
        AssertHighlighter("nginx",
"""
location /api/ {
  proxy_pass http://api;

  location /api/health {
    access_log off;
    return 200 "ok\n";
  }
}
""",
"""
<span class="hljs-section">location</span> /api/ {
  <span class="hljs-attribute">proxy_pass</span> http://api;

  <span class="hljs-section">location</span> /api/health {
    <span class="hljs-attribute">access_log</span> <span class="hljs-literal">off</span>;
    <span class="hljs-attribute">return</span> <span class="hljs-number">200</span> <span class="hljs-string">&quot;ok\n&quot;</span>;
  }
}
""");
    }

    [Fact]
    public void Context_UpstreamRoundRobin()
    {
        AssertHighlighter("nginx",
"""
upstream backend {
  server 10.0.0.1:8080;
  server 10.0.0.2:8080;
  server 10.0.0.3:8080;
}
""",
"""
<span class="hljs-section">upstream</span> backend {
  <span class="hljs-attribute">server</span> <span class="hljs-number">10.0.0.1:8080</span>;
  <span class="hljs-attribute">server</span> <span class="hljs-number">10.0.0.2:8080</span>;
  <span class="hljs-attribute">server</span> <span class="hljs-number">10.0.0.3:8080</span>;
}
""");
    }

    [Fact]
    public void Context_UpstreamLeastConn()
    {
        AssertHighlighter("nginx",
"""
upstream backend {
  least_conn;
  server backend1.example.com weight=3;
  server backend2.example.com weight=1 max_fails=2 fail_timeout=30s;
  server backend3.example.com backup;
}
""",
"""
upstream backend {
  least_conn;
  server backend1.example.com weight=3;
  server backend2.example.com weight=1 max_fails=2 fail_timeout=30s;
  server backend3.example.com backup;
}
""");
    }

    [Fact]
    public void Context_UpstreamIpHash()
    {
        AssertHighlighter("nginx",
"""
upstream backend {
  ip_hash;
  server 10.0.0.1:8080;
  server 10.0.0.2:8080;
}
""",
"""
upstream backend {
  ip_hash;
  server 10.0.0.1:8080;
  server 10.0.0.2:8080;
}
""");
    }

    [Fact]
    public void Context_UpstreamUnixSocket()
    {
        AssertHighlighter("nginx",
"""
upstream app {
  server unix:/run/app.sock;
}
""",
"""
<span class="hljs-section">upstream</span> app {
  <span class="hljs-attribute">server</span> unix:/run/app.sock;
}
""");
    }

    [Fact]
    public void Context_UpstreamKeepalive()
    {
        AssertHighlighter("nginx",
"""
upstream api {
  server api.internal:8080;
  keepalive 32;
}
""",
"""
<span class="hljs-section">upstream</span> api {
  <span class="hljs-attribute">server</span> api.internal:<span class="hljs-number">8080</span>;
  <span class="hljs-attribute">keepalive</span> <span class="hljs-number">32</span>;
}
""");
    }

    [Fact]
    public void Context_MapBlock()
    {
        AssertHighlighter("nginx",
"""
map $http_user_agent $is_bot {
  default       0;
  ~*googlebot   1;
  ~*bingbot     1;
  ~*duckduckbot 1;
}
""",
"""
map $http_user_agent $is_bot {
  default       0;
  ~*googlebot   1;
  ~*bingbot     1;
  ~*duckduckbot 1;
}
""");
    }

    [Fact]
    public void Context_MapHostnames()
    {
        AssertHighlighter("nginx",
"""
map $http_host $backend {
  hostnames;
  *.example.com         app;
  api.example.com       api;
  default               app;
}
""",
"""
map $http_host $backend {
  hostnames;
  *.example.com         app;
  api.example.com       api;
  default               app;
}
""");
    }

    [Fact]
    public void Context_StreamBlock()
    {
        AssertHighlighter("nginx",
"""
stream {
  upstream db {
    server 10.0.0.10:5432;
  }
  server {
    listen 5432;
    proxy_pass db;
  }
}
""",
"""
<span class="hljs-section">stream</span> {
  <span class="hljs-section">upstream</span> db {
    <span class="hljs-attribute">server</span> <span class="hljs-number">10.0.0.10:5432</span>;
  }
  <span class="hljs-section">server</span> {
    <span class="hljs-attribute">listen</span> <span class="hljs-number">5432</span>;
    <span class="hljs-attribute">proxy_pass</span> db;
  }
}
""");
    }

    [Fact]
    public void Context_LimitReqZone()
    {
        AssertHighlighter("nginx",
"""
http {
  limit_req_zone $binary_remote_addr zone=api:10m rate=10r/s;
}
""",
"""
<span class="hljs-section">http</span> {
  <span class="hljs-attribute">limit_req_zone</span> <span class="hljs-variable">$binary_remote_addr</span> zone=api:<span class="hljs-number">10m</span> rate=10r/s;
}
""");
    }

    [Fact]
    public void Context_LimitConnZone()
    {
        AssertHighlighter("nginx",
"""
http {
  limit_conn_zone $binary_remote_addr zone=addr:10m;
}
""",
"""
<span class="hljs-section">http</span> {
  <span class="hljs-attribute">limit_conn_zone</span> <span class="hljs-variable">$binary_remote_addr</span> zone=addr:<span class="hljs-number">10m</span>;
}
""");
    }

    [Fact]
    public void Variable_Host()
    {
        AssertHighlighter("nginx",
"""
return 301 https://$host$request_uri;
""",
"""
<span class="hljs-attribute">return</span> <span class="hljs-number">301</span> https://<span class="hljs-variable">$host</span><span class="hljs-variable">$request_uri</span>;
""");
    }

    [Fact]
    public void Variable_RemoteAddr()
    {
        AssertHighlighter("nginx",
"""
add_header X-Real-IP $remote_addr;
""",
"""
<span class="hljs-attribute">add_header</span> X-Real-IP <span class="hljs-variable">$remote_addr</span>;
""");
    }

    [Fact]
    public void Variable_RequestUri()
    {
        AssertHighlighter("nginx",
"""
access_log /var/log/nginx/uri.log $request_uri;
""",
"""
<span class="hljs-attribute">access_log</span> /var/log/nginx/uri.log <span class="hljs-variable">$request_uri</span>;
""");
    }

    [Fact]
    public void Variable_Args()
    {
        AssertHighlighter("nginx",
"""
rewrite ^ /search?$args last;
""",
"""
<span class="hljs-attribute">rewrite</span><span class="hljs-regexp"> ^</span> /search?<span class="hljs-variable">$args</span> <span class="hljs-literal">last</span>;
""");
    }

    [Fact]
    public void Variable_HttpHeader()
    {
        AssertHighlighter("nginx",
"""
set $auth $http_authorization;
""",
"""
<span class="hljs-attribute">set</span> <span class="hljs-variable">$auth</span> <span class="hljs-variable">$http_authorization</span>;
""");
    }

    [Fact]
    public void Variable_ServerName()
    {
        AssertHighlighter("nginx",
"""
add_header X-Served-By $server_name;
""",
"""
<span class="hljs-attribute">add_header</span> X-Served-By <span class="hljs-variable">$server_name</span>;
""");
    }

    [Fact]
    public void Variable_Uri()
    {
        AssertHighlighter("nginx",
"""
try_files $uri /index.html;
""",
"""
<span class="hljs-attribute">try_files</span> <span class="hljs-variable">$uri</span> /index.html;
""");
    }

    [Fact]
    public void Variable_DocumentRoot()
    {
        AssertHighlighter("nginx",
"""
alias $document_root/static/;
""",
"""
<span class="hljs-attribute">alias</span> <span class="hljs-variable">$document_root</span>/static/;
""");
    }

    [Fact]
    public void Variable_Scheme()
    {
        AssertHighlighter("nginx",
"""
proxy_set_header X-Forwarded-Proto $scheme;
""",
"""
<span class="hljs-attribute">proxy_set_header</span> X-Forwarded-Proto <span class="hljs-variable">$scheme</span>;
""");
    }

    [Fact]
    public void Variable_BinaryRemoteAddr()
    {
        AssertHighlighter("nginx",
"""
limit_req_zone $binary_remote_addr zone=api:10m rate=5r/s;
""",
"""
<span class="hljs-attribute">limit_req_zone</span> <span class="hljs-variable">$binary_remote_addr</span> zone=api:<span class="hljs-number">10m</span> rate=5r/s;
""");
    }

    [Fact]
    public void Variable_Custom()
    {
        AssertHighlighter("nginx",
"""
set $upstream_app "http://backend";
proxy_pass $upstream_app;
""",
"""
<span class="hljs-attribute">set</span> <span class="hljs-variable">$upstream_app</span> <span class="hljs-string">&quot;http://backend&quot;</span>;
<span class="hljs-attribute">proxy_pass</span> <span class="hljs-variable">$upstream_app</span>;
""");
    }

    [Fact]
    public void Proxy_PassSimple()
    {
        AssertHighlighter("nginx",
"""
location /api/ {
  proxy_pass http://backend;
}
""",
"""
<span class="hljs-section">location</span> /api/ {
  <span class="hljs-attribute">proxy_pass</span> http://backend;
}
""");
    }

    [Fact]
    public void Proxy_PassHttps()
    {
        AssertHighlighter("nginx",
"""
location /api/ {
  proxy_pass https://api.example.com;
}
""",
"""
<span class="hljs-section">location</span> /api/ {
  <span class="hljs-attribute">proxy_pass</span> https://api.example.com;
}
""");
    }

    [Fact]
    public void Proxy_PassUpstream()
    {
        AssertHighlighter("nginx",
"""
location /api/ {
  proxy_pass http://api_backend;
}
""",
"""
<span class="hljs-section">location</span> /api/ {
  <span class="hljs-attribute">proxy_pass</span> http://api_backend;
}
""");
    }

    [Fact]
    public void Proxy_SetHeaders()
    {
        AssertHighlighter("nginx",
"""
location / {
  proxy_set_header Host              $host;
  proxy_set_header X-Real-IP         $remote_addr;
  proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
  proxy_set_header X-Forwarded-Proto $scheme;
}
""",
"""
<span class="hljs-section">location</span> / {
  <span class="hljs-attribute">proxy_set_header</span> Host              <span class="hljs-variable">$host</span>;
  <span class="hljs-attribute">proxy_set_header</span> X-Real-IP         <span class="hljs-variable">$remote_addr</span>;
  <span class="hljs-attribute">proxy_set_header</span> X-Forwarded-For   <span class="hljs-variable">$proxy_add_x_forwarded_for</span>;
  <span class="hljs-attribute">proxy_set_header</span> X-Forwarded-Proto <span class="hljs-variable">$scheme</span>;
}
""");
    }

    [Fact]
    public void Proxy_Timeouts()
    {
        AssertHighlighter("nginx",
"""
proxy_connect_timeout 5s;
proxy_send_timeout    60s;
proxy_read_timeout    60s;
""",
"""
<span class="hljs-attribute">proxy_connect_timeout</span> <span class="hljs-number">5s</span>;
<span class="hljs-attribute">proxy_send_timeout</span>    <span class="hljs-number">60s</span>;
<span class="hljs-attribute">proxy_read_timeout</span>    <span class="hljs-number">60s</span>;
""");
    }

    [Fact]
    public void Proxy_BufferingOff()
    {
        AssertHighlighter("nginx",
"""
proxy_buffering off;
""",
"""
<span class="hljs-attribute">proxy_buffering</span> <span class="hljs-literal">off</span>;
""");
    }

    [Fact]
    public void Proxy_BufferSettings()
    {
        AssertHighlighter("nginx",
"""
proxy_buffers       8 16k;
proxy_buffer_size   32k;
proxy_busy_buffers_size 64k;
""",
"""
<span class="hljs-attribute">proxy_buffers</span>       <span class="hljs-number">8</span> <span class="hljs-number">16k</span>;
<span class="hljs-attribute">proxy_buffer_size</span>   <span class="hljs-number">32k</span>;
<span class="hljs-attribute">proxy_busy_buffers_size</span> <span class="hljs-number">64k</span>;
""");
    }

    [Fact]
    public void Proxy_NextUpstream()
    {
        AssertHighlighter("nginx",
"""
proxy_next_upstream error timeout invalid_header http_500 http_502 http_503;
""",
"""
<span class="hljs-attribute">proxy_next_upstream</span> <span class="hljs-literal">error</span> timeout invalid_header http_500 http_502 http_503;
""");
    }

    [Fact]
    public void Proxy_WebSocket()
    {
        AssertHighlighter("nginx",
"""
location /ws/ {
  proxy_pass http://ws_backend;
  proxy_http_version 1.1;
  proxy_set_header Upgrade $http_upgrade;
  proxy_set_header Connection "upgrade";
  proxy_read_timeout 1d;
}
""",
"""
<span class="hljs-section">location</span> /ws/ {
  <span class="hljs-attribute">proxy_pass</span> http://ws_backend;
  <span class="hljs-attribute">proxy_http_version</span> <span class="hljs-number">1</span>.<span class="hljs-number">1</span>;
  <span class="hljs-attribute">proxy_set_header</span> Upgrade <span class="hljs-variable">$http_upgrade</span>;
  <span class="hljs-attribute">proxy_set_header</span> Connection <span class="hljs-string">&quot;upgrade&quot;</span>;
  <span class="hljs-attribute">proxy_read_timeout</span> <span class="hljs-number">1d</span>;
}
""");
    }

    [Fact]
    public void Proxy_Cache()
    {
        AssertHighlighter("nginx",
"""
proxy_cache_path /var/cache/nginx levels=1:2 keys_zone=my_cache:10m max_size=1g inactive=60m;
proxy_cache my_cache;
proxy_cache_valid 200 302 10m;
proxy_cache_valid 404      1m;
""",
"""
<span class="hljs-attribute">proxy_cache_path</span> /var/cache/nginx levels=<span class="hljs-number">1</span>:<span class="hljs-number">2</span> keys_zone=my_cache:<span class="hljs-number">10m</span> max_size=<span class="hljs-number">1g</span> inactive=<span class="hljs-number">60m</span>;
<span class="hljs-attribute">proxy_cache</span> my_cache;
<span class="hljs-attribute">proxy_cache_valid</span> <span class="hljs-number">200</span> <span class="hljs-number">302</span> <span class="hljs-number">10m</span>;
<span class="hljs-attribute">proxy_cache_valid</span> <span class="hljs-number">404</span>      <span class="hljs-number">1m</span>;
""");
    }

    [Fact]
    public void FastCgi_Php()
    {
        AssertHighlighter("nginx",
"""
location ~ \.php$ {
  include       fastcgi_params;
  fastcgi_pass  unix:/run/php-fpm.sock;
  fastcgi_index index.php;
  fastcgi_param SCRIPT_FILENAME $document_root$fastcgi_script_name;
}
""",
"""
<span class="hljs-section">location</span> <span class="hljs-regexp">~ \.php$</span> {
  <span class="hljs-attribute">include</span>       fastcgi_params;
  <span class="hljs-attribute">fastcgi_pass</span>  unix:/run/php-fpm.sock;
  <span class="hljs-attribute">fastcgi_index</span> index.php;
  <span class="hljs-attribute">fastcgi_param</span> SCRIPT_FILENAME <span class="hljs-variable">$document_root</span><span class="hljs-variable">$fastcgi_script_name</span>;
}
""");
    }

    [Fact]
    public void FastCgi_CachePath()
    {
        AssertHighlighter("nginx",
"""
fastcgi_cache_path /var/cache/nginx/fcgi levels=1:2 keys_zone=phpcache:100m inactive=60m;
""",
"""
<span class="hljs-attribute">fastcgi_cache_path</span> /var/cache/nginx/fcgi levels=<span class="hljs-number">1</span>:<span class="hljs-number">2</span> keys_zone=phpcache:<span class="hljs-number">100m</span> inactive=<span class="hljs-number">60m</span>;
""");
    }

    [Fact]
    public void FastCgi_CacheValid()
    {
        AssertHighlighter("nginx",
"""
fastcgi_cache_valid 200 1h;
fastcgi_cache_valid 404 1m;
""",
"""
<span class="hljs-attribute">fastcgi_cache_valid</span> <span class="hljs-number">200</span> <span class="hljs-number">1h</span>;
<span class="hljs-attribute">fastcgi_cache_valid</span> <span class="hljs-number">404</span> <span class="hljs-number">1m</span>;
""");
    }

    [Fact]
    public void Ssl_CertAndKey()
    {
        AssertHighlighter("nginx",
"""
ssl_certificate     /etc/ssl/example.crt;
ssl_certificate_key /etc/ssl/example.key;
""",
"""
<span class="hljs-attribute">ssl_certificate</span>     /etc/ssl/example.crt;
<span class="hljs-attribute">ssl_certificate_key</span> /etc/ssl/example.key;
""");
    }

    [Fact]
    public void Ssl_Protocols()
    {
        AssertHighlighter("nginx",
"""
ssl_protocols TLSv1.2 TLSv1.3;
""",
"""
<span class="hljs-attribute">ssl_protocols</span> TLSv1.<span class="hljs-number">2</span> TLSv1.<span class="hljs-number">3</span>;
""");
    }

    [Fact]
    public void Ssl_Ciphers()
    {
        AssertHighlighter("nginx",
"""
ssl_ciphers           ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256;
ssl_prefer_server_ciphers on;
""",
"""
<span class="hljs-attribute">ssl_ciphers</span>           ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256;
<span class="hljs-attribute">ssl_prefer_server_ciphers</span> <span class="hljs-literal">on</span>;
""");
    }

    [Fact]
    public void Ssl_SessionCache()
    {
        AssertHighlighter("nginx",
"""
ssl_session_cache shared:SSL:10m;
ssl_session_timeout 1d;
ssl_session_tickets off;
""",
"""
<span class="hljs-attribute">ssl_session_cache</span> shared:SSL:<span class="hljs-number">10m</span>;
<span class="hljs-attribute">ssl_session_timeout</span> <span class="hljs-number">1d</span>;
<span class="hljs-attribute">ssl_session_tickets</span> <span class="hljs-literal">off</span>;
""");
    }

    [Fact]
    public void Ssl_OcspStapling()
    {
        AssertHighlighter("nginx",
"""
ssl_stapling        on;
ssl_stapling_verify on;
resolver            1.1.1.1 8.8.8.8 valid=300s;
""",
"""
<span class="hljs-attribute">ssl_stapling</span>        <span class="hljs-literal">on</span>;
<span class="hljs-attribute">ssl_stapling_verify</span> <span class="hljs-literal">on</span>;
<span class="hljs-attribute">resolver</span>            <span class="hljs-number">1.1.1.1</span> <span class="hljs-number">8.8.8.8</span> valid=<span class="hljs-number">300s</span>;
""");
    }

    [Fact]
    public void Gzip_Enable()
    {
        AssertHighlighter("nginx",
"""
gzip on;
""",
"""
<span class="hljs-attribute">gzip</span> <span class="hljs-literal">on</span>;
""");
    }

    [Fact]
    public void Gzip_FullConfig()
    {
        AssertHighlighter("nginx",
"""
gzip            on;
gzip_vary       on;
gzip_min_length 1024;
gzip_comp_level 6;
gzip_proxied    expired no-cache no-store private auth;
gzip_types      text/plain text/css text/xml application/json application/javascript application/xml+rss;
""",
"""
<span class="hljs-attribute">gzip</span>            <span class="hljs-literal">on</span>;
<span class="hljs-attribute">gzip_vary</span>       <span class="hljs-literal">on</span>;
<span class="hljs-attribute">gzip_min_length</span> <span class="hljs-number">1024</span>;
<span class="hljs-attribute">gzip_comp_level</span> <span class="hljs-number">6</span>;
<span class="hljs-attribute">gzip_proxied</span>    expired <span class="hljs-literal">no</span>-cache <span class="hljs-literal">no</span>-store private auth;
<span class="hljs-attribute">gzip_types</span>      text/plain text/css text/xml application/json application/javascript application/xml+rss;
""");
    }

    [Fact]
    public void Gzip_StaticEnabled()
    {
        AssertHighlighter("nginx",
"""
gzip_static on;
""",
"""
<span class="hljs-attribute">gzip_static</span> <span class="hljs-literal">on</span>;
""");
    }

    [Fact]
    public void Comment_FullLine()
    {
        AssertHighlighter("nginx",
"""
# this is a comment
""",
"""
<span class="hljs-comment"># this is a comment</span>
""");
    }

    [Fact]
    public void Comment_Inline()
    {
        AssertHighlighter("nginx",
"""
listen 80;  # default HTTP port
""",
"""
<span class="hljs-attribute">listen</span> <span class="hljs-number">80</span>;  <span class="hljs-comment"># default HTTP port</span>
""");
    }

    [Fact]
    public void Comment_AboveDirective()
    {
        AssertHighlighter("nginx",
"""
# default catch-all
server {
  listen 80 default_server;
  return 404;
}
""",
"""
<span class="hljs-comment"># default catch-all</span>
<span class="hljs-section">server</span> {
  <span class="hljs-attribute">listen</span> <span class="hljs-number">80</span> default_server;
  <span class="hljs-attribute">return</span> <span class="hljs-number">404</span>;
}
""");
    }

    [Fact]
    public void Composite_StaticSite()
    {
        AssertHighlighter("nginx",
"""
server {
  listen 80;
  server_name example.com;

  root  /var/www/example;
  index index.html;

  location / {
    try_files $uri $uri/ =404;
  }

  location ~* \.(?:css|js|svg|png|jpg|woff2)$ {
    expires 30d;
    add_header Cache-Control "public, immutable";
  }
}
""",
"""
<span class="hljs-section">server</span> {
  <span class="hljs-attribute">listen</span> <span class="hljs-number">80</span>;
  <span class="hljs-attribute">server_name</span> example.com;

  <span class="hljs-attribute">root</span>  /var/www/example;
  <span class="hljs-attribute">index</span> index.html;

  <span class="hljs-section">location</span> / {
    <span class="hljs-attribute">try_files</span> <span class="hljs-variable">$uri</span> <span class="hljs-variable">$uri</span>/ =<span class="hljs-number">404</span>;
  }

  <span class="hljs-section">location</span> <span class="hljs-regexp">~* \.(?:css|js|svg|png|jpg|woff2)$</span> {
    <span class="hljs-attribute">expires</span> <span class="hljs-number">30d</span>;
    <span class="hljs-attribute">add_header</span> Cache-Control <span class="hljs-string">&quot;public, immutable&quot;</span>;
  }
}
""");
    }

    [Fact]
    public void Composite_ReverseProxy()
    {
        AssertHighlighter("nginx",
"""
upstream app_backend {
  least_conn;
  server 10.0.0.10:8080 max_fails=2 fail_timeout=30s;
  server 10.0.0.11:8080 max_fails=2 fail_timeout=30s;
  keepalive 32;
}

server {
  listen      443 ssl http2;
  server_name app.example.com;

  ssl_certificate     /etc/ssl/app.crt;
  ssl_certificate_key /etc/ssl/app.key;
  ssl_protocols       TLSv1.2 TLSv1.3;

  client_max_body_size 50M;

  location / {
    proxy_pass http://app_backend;

    proxy_http_version 1.1;
    proxy_set_header Host              $host;
    proxy_set_header X-Real-IP         $remote_addr;
    proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;

    proxy_connect_timeout 5s;
    proxy_read_timeout    60s;
  }
}
""",
"""
upstream app_backend {
  least_conn;
  server 10.0.0.10:8080 max_fails=2 fail_timeout=30s;
  server 10.0.0.11:8080 max_fails=2 fail_timeout=30s;
  keepalive 32;
}

server {
  listen      443 ssl http2;
  server_name app.example.com;

  ssl_certificate     /etc/ssl/app.crt;
  ssl_certificate_key /etc/ssl/app.key;
  ssl_protocols       TLSv1.2 TLSv1.3;

  client_max_body_size 50M;

  location / {
    proxy_pass http://app_backend;

    proxy_http_version 1.1;
    proxy_set_header Host              $host;
    proxy_set_header X-Real-IP         $remote_addr;
    proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;

    proxy_connect_timeout 5s;
    proxy_read_timeout    60s;
  }
}
""");
    }

    [Fact]
    public void Composite_HttpsRedirect()
    {
        AssertHighlighter("nginx",
"""
server {
  listen 80 default_server;
  listen [::]:80 default_server;
  server_name _;
  return 301 https://$host$request_uri;
}
""",
"""
<span class="hljs-section">server</span> {
  <span class="hljs-attribute">listen</span> <span class="hljs-number">80</span> default_server;
  <span class="hljs-attribute">listen</span> [::]:<span class="hljs-number">80</span> default_server;
  <span class="hljs-attribute">server_name</span> _;
  <span class="hljs-attribute">return</span> <span class="hljs-number">301</span> https://<span class="hljs-variable">$host</span><span class="hljs-variable">$request_uri</span>;
}
""");
    }

    [Fact]
    public void Composite_PhpFpmSite()
    {
        AssertHighlighter("nginx",
"""
server {
  listen 80;
  server_name blog.example.com;

  root /var/www/blog;
  index index.php index.html;

  location / {
    try_files $uri $uri/ /index.php?$args;
  }

  location ~ \.php$ {
    include       fastcgi_params;
    fastcgi_pass  unix:/run/php-fpm.sock;
    fastcgi_index index.php;
    fastcgi_param SCRIPT_FILENAME $document_root$fastcgi_script_name;
  }

  location ~ /\.ht {
    deny all;
  }
}
""",
"""
<span class="hljs-section">server</span> {
  <span class="hljs-attribute">listen</span> <span class="hljs-number">80</span>;
  <span class="hljs-attribute">server_name</span> blog.example.com;

  <span class="hljs-attribute">root</span> /var/www/blog;
  <span class="hljs-attribute">index</span> index.php index.html;

  <span class="hljs-section">location</span> / {
    <span class="hljs-attribute">try_files</span> <span class="hljs-variable">$uri</span> <span class="hljs-variable">$uri</span>/ /index.php?<span class="hljs-variable">$args</span>;
  }

  <span class="hljs-section">location</span> <span class="hljs-regexp">~ \.php$</span> {
    <span class="hljs-attribute">include</span>       fastcgi_params;
    <span class="hljs-attribute">fastcgi_pass</span>  unix:/run/php-fpm.sock;
    <span class="hljs-attribute">fastcgi_index</span> index.php;
    <span class="hljs-attribute">fastcgi_param</span> SCRIPT_FILENAME <span class="hljs-variable">$document_root</span><span class="hljs-variable">$fastcgi_script_name</span>;
  }

  <span class="hljs-section">location</span> <span class="hljs-regexp">~ /\.ht</span> {
    <span class="hljs-attribute">deny</span> all;
  }
}
""");
    }

    [Fact]
    public void Composite_RateLimiting()
    {
        AssertHighlighter("nginx",
"""
http {
  limit_req_zone  $binary_remote_addr zone=api:10m rate=10r/s;
  limit_conn_zone $binary_remote_addr zone=conn:10m;

  server {
    listen 443 ssl http2;
    server_name api.example.com;

    location / {
      limit_req  zone=api burst=20 nodelay;
      limit_conn conn 10;
      proxy_pass http://api_backend;
    }
  }
}
""",
"""
<span class="hljs-section">http</span> {
  <span class="hljs-attribute">limit_req_zone</span>  <span class="hljs-variable">$binary_remote_addr</span> zone=api:<span class="hljs-number">10m</span> rate=10r/s;
  <span class="hljs-attribute">limit_conn_zone</span> <span class="hljs-variable">$binary_remote_addr</span> zone=conn:<span class="hljs-number">10m</span>;

  <span class="hljs-section">server</span> {
    <span class="hljs-attribute">listen</span> <span class="hljs-number">443</span> ssl http2;
    <span class="hljs-attribute">server_name</span> api.example.com;

    <span class="hljs-section">location</span> / {
      <span class="hljs-attribute">limit_req</span>  zone=api burst=<span class="hljs-number">20</span> nodelay;
      <span class="hljs-attribute">limit_conn</span> conn <span class="hljs-number">10</span>;
      <span class="hljs-attribute">proxy_pass</span> http://api_backend;
    }
  }
}
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("nginx",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("nginx",
"""
# just a comment
""",
"""
<span class="hljs-comment"># just a comment</span>
""");
    }

    [Fact]
    public void SpecialEdge_EmptyBlock()
    {
        AssertHighlighter("nginx",
"""
server {}
""",
"""
<span class="hljs-section">server</span> {}
""");
    }

    [Fact]
    public void SpecialEdge_TrailingNewline()
    {
        AssertHighlighter("nginx",
"""
worker_processes auto;

""",
"""
<span class="hljs-attribute">worker_processes</span> auto;

""");
    }

    [Fact]
    public void SpecialEdge_MultiSemicolons()
    {
        AssertHighlighter("nginx",
"""
listen 80; listen 443 ssl;
""",
"""
<span class="hljs-attribute">listen</span> <span class="hljs-number">80</span>; <span class="hljs-attribute">listen</span> <span class="hljs-number">443</span> ssl;
""");
    }
}
