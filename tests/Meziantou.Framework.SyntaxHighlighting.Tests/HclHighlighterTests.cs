namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class HclHighlighterTests
{
    [Fact]
    public void Comments()
    {
        AssertHighlighter("hcl",
"""
# hash comment
// line comment
/* block
   comment */
x = 1 # trailing
y = 2 // trailing
# TODO: fix
""",
"""
<span class="hljs-comment"># hash comment</span>
<span class="hljs-comment">// line comment</span>
<span class="hljs-comment">/* block
   comment */</span>
<span class="hljs-attr">x</span> = <span class="hljs-number">1</span> <span class="hljs-comment"># trailing</span>
<span class="hljs-attr">y</span> = <span class="hljs-number">2</span> <span class="hljs-comment">// trailing</span>
<span class="hljs-comment"># <span class="hljs-doctag">TODO:</span> fix</span>
""");
    }

    [Fact]
    public void Attributes()
    {
        AssertHighlighter("hcl",
"""
name    = "web"
count   = 3
enabled = true
ratio   = 0.5
nothing = null
big     = 1e6
small   = 1.5E-3
""",
"""
<span class="hljs-attr">name</span>    = <span class="hljs-string">&quot;web&quot;</span>
<span class="hljs-attr">count</span>   = <span class="hljs-number">3</span>
<span class="hljs-attr">enabled</span> = <span class="hljs-literal">true</span>
<span class="hljs-attr">ratio</span>   = <span class="hljs-number">0.5</span>
<span class="hljs-attr">nothing</span> = <span class="hljs-literal">null</span>
<span class="hljs-attr">big</span>     = <span class="hljs-number">1e6</span>
<span class="hljs-attr">small</span>   = <span class="hljs-number">1.5E-3</span>
""");
    }

    [Fact]
    public void Resource()
    {
        AssertHighlighter("hcl",
"""
resource "aws_instance" "web" {
  ami           = "ami-0c55b159cbfafe1f0"
  instance_type = var.instance_type

  tags = {
    Name = "web-${var.env}"
  }
}
""",
"""
<span class="hljs-section">resource</span> <span class="hljs-string">&quot;aws_instance&quot;</span> <span class="hljs-string">&quot;web&quot;</span> {
  <span class="hljs-attr">ami</span>           = <span class="hljs-string">&quot;ami-0c55b159cbfafe1f0&quot;</span>
  <span class="hljs-attr">instance_type</span> = <span class="hljs-variable language_">var</span>.instance_type

  <span class="hljs-attr">tags</span> = {
    <span class="hljs-attr">Name</span> = <span class="hljs-string">&quot;web-<span class="hljs-subst">${<span class="hljs-variable language_">var</span>.env}</span>&quot;</span>
  }
}
""");
    }

    [Fact]
    public void TerraformBlock()
    {
        AssertHighlighter("hcl",
"""
terraform {
  required_version = ">= 1.5.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }

  backend "s3" {
    bucket = "my-state"
    key    = "prod/terraform.tfstate"
    region = "us-east-1"
  }
}
""",
"""
<span class="hljs-section">terraform</span> {
  <span class="hljs-attr">required_version</span> = <span class="hljs-string">&quot;&gt;= 1.5.0&quot;</span>

  <span class="hljs-section">required_providers</span> {
    <span class="hljs-attr">aws</span> = {
      <span class="hljs-attr">source</span>  = <span class="hljs-string">&quot;hashicorp/aws&quot;</span>
      <span class="hljs-attr">version</span> = <span class="hljs-string">&quot;~&gt; 5.0&quot;</span>
    }
  }

  <span class="hljs-section">backend</span> <span class="hljs-string">&quot;s3&quot;</span> {
    <span class="hljs-attr">bucket</span> = <span class="hljs-string">&quot;my-state&quot;</span>
    <span class="hljs-attr">key</span>    = <span class="hljs-string">&quot;prod/terraform.tfstate&quot;</span>
    <span class="hljs-attr">region</span> = <span class="hljs-string">&quot;us-east-1&quot;</span>
  }
}
""");
    }

    [Fact]
    public void Provider()
    {
        AssertHighlighter("hcl",
"""
provider "aws" {
  region = var.region
  alias  = "west"
}
""",
"""
<span class="hljs-section">provider</span> <span class="hljs-string">&quot;aws&quot;</span> {
  <span class="hljs-attr">region</span> = <span class="hljs-variable language_">var</span>.region
  <span class="hljs-attr">alias</span>  = <span class="hljs-string">&quot;west&quot;</span>
}
""");
    }

    [Fact]
    public void Variable()
    {
        AssertHighlighter("hcl",
"""
variable "instance_type" {
  description = "The EC2 instance type"
  type        = string
  default     = "t3.micro"
  sensitive   = false

  validation {
    condition     = can(regex("^t3\\.", var.instance_type))
    error_message = "Must be a t3 instance."
  }
}
""",
"""
<span class="hljs-section">variable</span> <span class="hljs-string">&quot;instance_type&quot;</span> {
  <span class="hljs-attr">description</span> = <span class="hljs-string">&quot;The EC2 instance type&quot;</span>
  <span class="hljs-attr">type</span>        = <span class="hljs-type">string</span>
  <span class="hljs-attr">default</span>     = <span class="hljs-string">&quot;t3.micro&quot;</span>
  <span class="hljs-attr">sensitive</span>   = <span class="hljs-literal">false</span>

  <span class="hljs-section">validation</span> {
    <span class="hljs-attr">condition</span>     = <span class="hljs-built_in">can</span>(<span class="hljs-built_in">regex</span>(<span class="hljs-string">&quot;^t3\\.&quot;</span>, <span class="hljs-variable language_">var</span>.instance_type))
    <span class="hljs-attr">error_message</span> = <span class="hljs-string">&quot;Must be a t3 instance.&quot;</span>
  }
}
""");
    }

    [Fact]
    public void TypeConstraints()
    {
        AssertHighlighter("hcl",
"""
variable "settings" {
  type = object({
    name    = string
    ports   = list(number)
    tags    = map(string)
    enabled = optional(bool, true)
    extra   = set(any)
    pair    = tuple([string, number])
  })
}
""",
"""
<span class="hljs-section">variable</span> <span class="hljs-string">&quot;settings&quot;</span> {
  <span class="hljs-attr">type</span> = <span class="hljs-type">object</span>({
    <span class="hljs-attr">name</span>    = <span class="hljs-type">string</span>
    <span class="hljs-attr">ports</span>   = <span class="hljs-type">list</span>(<span class="hljs-type">number</span>)
    <span class="hljs-attr">tags</span>    = <span class="hljs-type">map</span>(<span class="hljs-type">string</span>)
    <span class="hljs-attr">enabled</span> = <span class="hljs-type">optional</span>(<span class="hljs-type">bool</span>, <span class="hljs-literal">true</span>)
    <span class="hljs-attr">extra</span>   = <span class="hljs-type">set</span>(<span class="hljs-type">any</span>)
    <span class="hljs-attr">pair</span>    = <span class="hljs-type">tuple</span>([<span class="hljs-type">string</span>, <span class="hljs-type">number</span>])
  })
}
""");
    }

    [Fact]
    public void Output()
    {
        AssertHighlighter("hcl",
"""
output "instance_ip" {
  value       = aws_instance.web.public_ip
  description = "Public IP"
  sensitive   = true
  depends_on  = [aws_instance.web]
}
""",
"""
<span class="hljs-section">output</span> <span class="hljs-string">&quot;instance_ip&quot;</span> {
  <span class="hljs-attr">value</span>       = aws_instance.web.public_ip
  <span class="hljs-attr">description</span> = <span class="hljs-string">&quot;Public IP&quot;</span>
  <span class="hljs-attr">sensitive</span>   = <span class="hljs-literal">true</span>
  <span class="hljs-attr">depends_on</span>  = [aws_instance.web]
}
""");
    }

    [Fact]
    public void Locals()
    {
        AssertHighlighter("hcl",
"""
locals {
  common_tags = merge(var.tags, {
    Environment = var.env
    ManagedBy   = "terraform"
  })
  name_prefix = "${var.project}-${var.env}"
}
""",
"""
<span class="hljs-section">locals</span> {
  <span class="hljs-attr">common_tags</span> = <span class="hljs-built_in">merge</span>(<span class="hljs-variable language_">var</span>.tags, {
    <span class="hljs-attr">Environment</span> = <span class="hljs-variable language_">var</span>.env
    <span class="hljs-attr">ManagedBy</span>   = <span class="hljs-string">&quot;terraform&quot;</span>
  })
  <span class="hljs-attr">name_prefix</span> = <span class="hljs-string">&quot;<span class="hljs-subst">${<span class="hljs-variable language_">var</span>.project}</span>-<span class="hljs-subst">${<span class="hljs-variable language_">var</span>.env}</span>&quot;</span>
}
""");
    }

    [Fact]
    public void Module()
    {
        AssertHighlighter("hcl",
"""
module "vpc" {
  source  = "terraform-aws-modules/vpc/aws"
  version = "5.1.0"

  name = local.name_prefix
  cidr = "10.0.0.0/16"
  azs  = ["us-east-1a", "us-east-1b"]

  providers = {
    aws = aws.west
  }
}
""",
"""
<span class="hljs-section">module</span> <span class="hljs-string">&quot;vpc&quot;</span> {
  <span class="hljs-attr">source</span>  = <span class="hljs-string">&quot;terraform-aws-modules/vpc/aws&quot;</span>
  <span class="hljs-attr">version</span> = <span class="hljs-string">&quot;5.1.0&quot;</span>

  <span class="hljs-attr">name</span> = <span class="hljs-variable language_">local</span>.name_prefix
  <span class="hljs-attr">cidr</span> = <span class="hljs-string">&quot;10.0.0.0/16&quot;</span>
  <span class="hljs-attr">azs</span>  = [<span class="hljs-string">&quot;us-east-1a&quot;</span>, <span class="hljs-string">&quot;us-east-1b&quot;</span>]

  <span class="hljs-attr">providers</span> = {
    <span class="hljs-attr">aws</span> = aws.west
  }
}
""");
    }

    [Fact]
    public void DataSource()
    {
        AssertHighlighter("hcl",
"""
data "aws_ami" "ubuntu" {
  most_recent = true
  owners      = ["099720109477"]

  filter {
    name   = "name"
    values = ["ubuntu/images/*"]
  }
}
""",
"""
<span class="hljs-section">data</span> <span class="hljs-string">&quot;aws_ami&quot;</span> <span class="hljs-string">&quot;ubuntu&quot;</span> {
  <span class="hljs-attr">most_recent</span> = <span class="hljs-literal">true</span>
  <span class="hljs-attr">owners</span>      = [<span class="hljs-string">&quot;099720109477&quot;</span>]

  <span class="hljs-section">filter</span> {
    <span class="hljs-attr">name</span>   = <span class="hljs-string">&quot;name&quot;</span>
    <span class="hljs-attr">values</span> = [<span class="hljs-string">&quot;ubuntu/images/*&quot;</span>]
  }
}
""");
    }

    [Fact]
    public void ForExpressions()
    {
        AssertHighlighter("hcl",
"""
upper_names = [for s in var.names : upper(s)]
filtered    = [for s in var.names : s if s != ""]
by_id       = { for k, v in var.users : k => v.id }
grouped     = { for u in var.users : u.role => u.name... }
indexed     = [for i, v in var.list : "${i}=${v}"]
""",
"""
<span class="hljs-attr">upper_names</span> = [<span class="hljs-keyword">for</span> s <span class="hljs-keyword">in</span> <span class="hljs-variable language_">var</span>.names : <span class="hljs-built_in">upper</span>(s)]
<span class="hljs-attr">filtered</span>    = [<span class="hljs-keyword">for</span> s <span class="hljs-keyword">in</span> <span class="hljs-variable language_">var</span>.names : s <span class="hljs-keyword">if</span> s != <span class="hljs-string">&quot;&quot;</span>]
<span class="hljs-attr">by_id</span>       = { <span class="hljs-keyword">for</span> k, v <span class="hljs-keyword">in</span> <span class="hljs-variable language_">var</span>.users : k =&gt; v.id }
<span class="hljs-attr">grouped</span>     = { <span class="hljs-keyword">for</span> u <span class="hljs-keyword">in</span> <span class="hljs-variable language_">var</span>.users : u.role =&gt; u.name... }
<span class="hljs-attr">indexed</span>     = [<span class="hljs-keyword">for</span> i, v <span class="hljs-keyword">in</span> <span class="hljs-variable language_">var</span>.list : <span class="hljs-string">&quot;<span class="hljs-subst">${i}</span>=<span class="hljs-subst">${v}</span>&quot;</span>]
""");
    }

    [Fact]
    public void Operators()
    {
        AssertHighlighter("hcl",
"""
instance_type = var.env == "prod" ? "m5.large" : "t3.micro"
count         = var.create ? 1 : 0
enabled       = !var.disabled && (var.a || var.b)
size          = var.size >= 10 ? var.size : 10
neg           = -1
expr          = (var.a + var.b) * 2 / 3 % 4 - 5
""",
"""
<span class="hljs-attr">instance_type</span> = <span class="hljs-variable language_">var</span>.env == <span class="hljs-string">&quot;prod&quot;</span> ? <span class="hljs-string">&quot;m5.large&quot;</span> : <span class="hljs-string">&quot;t3.micro&quot;</span>
<span class="hljs-attr">count</span>         = <span class="hljs-variable language_">var</span>.create ? <span class="hljs-number">1</span> : <span class="hljs-number">0</span>
<span class="hljs-attr">enabled</span>       = !<span class="hljs-variable language_">var</span>.disabled &amp;&amp; (<span class="hljs-variable language_">var</span>.a || <span class="hljs-variable language_">var</span>.b)
<span class="hljs-attr">size</span>          = <span class="hljs-variable language_">var</span>.size &gt;= <span class="hljs-number">10</span> ? <span class="hljs-variable language_">var</span>.size : <span class="hljs-number">10</span>
<span class="hljs-attr">neg</span>           = -<span class="hljs-number">1</span>
<span class="hljs-attr">expr</span>          = (<span class="hljs-variable language_">var</span>.a + <span class="hljs-variable language_">var</span>.b) * <span class="hljs-number">2</span> / <span class="hljs-number">3</span> % <span class="hljs-number">4</span> - <span class="hljs-number">5</span>
""");
    }

    [Fact]
    public void DynamicBlock()
    {
        AssertHighlighter("hcl",
"""
resource "aws_security_group" "this" {
  name = "sg"

  dynamic "ingress" {
    for_each = var.ingress_rules
    iterator = rule
    content {
      from_port   = rule.value.from
      to_port     = rule.value.to
      protocol    = "tcp"
      cidr_blocks = rule.value.cidrs
    }
  }
}
""",
"""
<span class="hljs-section">resource</span> <span class="hljs-string">&quot;aws_security_group&quot;</span> <span class="hljs-string">&quot;this&quot;</span> {
  <span class="hljs-attr">name</span> = <span class="hljs-string">&quot;sg&quot;</span>

  <span class="hljs-section">dynamic</span> <span class="hljs-string">&quot;ingress&quot;</span> {
    <span class="hljs-attr">for_each</span> = <span class="hljs-variable language_">var</span>.ingress_rules
    <span class="hljs-attr">iterator</span> = rule
    <span class="hljs-section">content</span> {
      <span class="hljs-attr">from_port</span>   = rule.value.from
      <span class="hljs-attr">to_port</span>     = rule.value.to
      <span class="hljs-attr">protocol</span>    = <span class="hljs-string">&quot;tcp&quot;</span>
      <span class="hljs-attr">cidr_blocks</span> = rule.value.cidrs
    }
  }
}
""");
    }

    [Fact]
    public void CountAndForEach()
    {
        AssertHighlighter("hcl",
"""
resource "aws_instance" "server" {
  count = 4
  tags = {
    Name = "Server ${count.index}"
  }
}

resource "aws_iam_user" "u" {
  for_each = toset(["alice", "bob"])
  name     = each.key
  path     = each.value
}
""",
"""
<span class="hljs-section">resource</span> <span class="hljs-string">&quot;aws_instance&quot;</span> <span class="hljs-string">&quot;server&quot;</span> {
  <span class="hljs-attr">count</span> = <span class="hljs-number">4</span>
  <span class="hljs-attr">tags</span> = {
    <span class="hljs-attr">Name</span> = <span class="hljs-string">&quot;Server <span class="hljs-subst">${<span class="hljs-variable language_">count</span>.index}</span>&quot;</span>
  }
}

<span class="hljs-section">resource</span> <span class="hljs-string">&quot;aws_iam_user&quot;</span> <span class="hljs-string">&quot;u&quot;</span> {
  <span class="hljs-attr">for_each</span> = <span class="hljs-built_in">toset</span>([<span class="hljs-string">&quot;alice&quot;</span>, <span class="hljs-string">&quot;bob&quot;</span>])
  <span class="hljs-attr">name</span>     = <span class="hljs-variable language_">each</span>.key
  <span class="hljs-attr">path</span>     = <span class="hljs-variable language_">each</span>.value
}
""");
    }

    [Fact]
    public void Lifecycle()
    {
        AssertHighlighter("hcl",
"""
resource "aws_instance" "x" {
  lifecycle {
    create_before_destroy = true
    prevent_destroy       = false
    ignore_changes        = [tags, ami]
    replace_triggered_by  = [aws_instance.y.id]

    precondition {
      condition     = self.ami != ""
      error_message = "AMI required."
    }
  }
}
""",
"""
<span class="hljs-section">resource</span> <span class="hljs-string">&quot;aws_instance&quot;</span> <span class="hljs-string">&quot;x&quot;</span> {
  <span class="hljs-section">lifecycle</span> {
    <span class="hljs-attr">create_before_destroy</span> = <span class="hljs-literal">true</span>
    <span class="hljs-attr">prevent_destroy</span>       = <span class="hljs-literal">false</span>
    <span class="hljs-attr">ignore_changes</span>        = [tags, ami]
    <span class="hljs-attr">replace_triggered_by</span>  = [aws_instance.y.id]

    <span class="hljs-section">precondition</span> {
      <span class="hljs-attr">condition</span>     = <span class="hljs-variable language_">self</span>.ami != <span class="hljs-string">&quot;&quot;</span>
      <span class="hljs-attr">error_message</span> = <span class="hljs-string">&quot;AMI required.&quot;</span>
    }
  }
}
""");
    }

    [Fact]
    public void Heredoc()
    {
        AssertHighlighter("hcl",
"""
user_data = <<EOT
#!/bin/bash
echo "Hello ${var.name}"
EOT
next = 1
""",
"""
<span class="hljs-attr">user_data</span> = <span class="hljs-string">&lt;&lt;EOT
#!/bin/bash
echo &quot;Hello <span class="hljs-subst">${<span class="hljs-variable language_">var</span>.name}</span>&quot;
EOT</span>
<span class="hljs-attr">next</span> = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void IndentedHeredoc()
    {
        AssertHighlighter("hcl",
"""
resource "x" "y" {
  policy = <<-EOF
    {
      "Version": "2012-10-17",
      "Region": "${var.region}"
    }
    EOF
  other = true
}
""",
"""
<span class="hljs-section">resource</span> <span class="hljs-string">&quot;x&quot;</span> <span class="hljs-string">&quot;y&quot;</span> {
  <span class="hljs-attr">policy</span> = <span class="hljs-string">&lt;&lt;-EOF
    {
      &quot;Version&quot;: &quot;2012-10-17&quot;,
      &quot;Region&quot;: &quot;<span class="hljs-subst">${<span class="hljs-variable language_">var</span>.region}</span>&quot;
    }
    EOF</span>
  <span class="hljs-attr">other</span> = <span class="hljs-literal">true</span>
}
""");
    }

    [Fact]
    public void HeredocWithDirectives()
    {
        AssertHighlighter("hcl",
"""
config = <<-EOT
  %{ for ip in var.ips ~}
  server ${ip}
  %{ endfor ~}
  %{ if var.debug }debug = true%{ else }debug = false%{ endif }
  EOT
""",
"""
<span class="hljs-attr">config</span> = <span class="hljs-string">&lt;&lt;-EOT
  <span class="hljs-subst">%{ <span class="hljs-keyword">for</span> ip <span class="hljs-keyword">in</span> <span class="hljs-variable language_">var</span>.ips ~}</span>
  server <span class="hljs-subst">${ip}</span>
  <span class="hljs-subst">%{ <span class="hljs-keyword">endfor</span> ~}</span>
  <span class="hljs-subst">%{ <span class="hljs-keyword">if</span> <span class="hljs-variable language_">var</span>.debug }</span>debug = true<span class="hljs-subst">%{ <span class="hljs-keyword">else</span> }</span>debug = false<span class="hljs-subst">%{ <span class="hljs-keyword">endif</span> }</span>
  EOT</span>
""");
    }

    [Fact]
    public void HeredocMarkerMustBeAlone()
    {
        AssertHighlighter("hcl",
"""
x = <<EOT
EOTX
  EOT is not alone
EOT
y = 2
""",
"""
<span class="hljs-attr">x</span> = <span class="hljs-string">&lt;&lt;EOT
EOTX
  EOT is not alone
EOT</span>
<span class="hljs-attr">y</span> = <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void EmptyHeredoc()
    {
        AssertHighlighter("hcl",
"""
x = <<EOT
EOT
""",
"""
<span class="hljs-attr">x</span> = <span class="hljs-string">&lt;&lt;EOT
EOT</span>
""");
    }

    [Fact]
    public void StringEscapes()
    {
        AssertHighlighter("hcl",
"""
a = "quote \" backslash \\ newline \n tab \t unicode \u00e9 \U0001F600"
b = "literal $${not_interpolated} and %%{not_a_directive}"
""",
"""
<span class="hljs-attr">a</span> = <span class="hljs-string">&quot;quote \&quot; backslash \\ newline \n tab \t unicode \u00e9 \U0001F600&quot;</span>
<span class="hljs-attr">b</span> = <span class="hljs-string">&quot;literal $${not_interpolated} and %%{not_a_directive}&quot;</span>
""");
    }

    [Fact]
    public void Interpolation()
    {
        AssertHighlighter("hcl",
"""
name = "${var.prefix}-${lower(var.name)}-${count.index + 1}"
json = "${jsonencode({ a = 1, b = [1, 2] })}"
strip = "${~ var.x ~}"
nested = "${format("%s-%s", var.a, "${var.b}")}"
""",
"""
<span class="hljs-attr">name</span> = <span class="hljs-string">&quot;<span class="hljs-subst">${<span class="hljs-variable language_">var</span>.prefix}</span>-<span class="hljs-subst">${<span class="hljs-built_in">lower</span>(<span class="hljs-variable language_">var</span>.name)}</span>-<span class="hljs-subst">${<span class="hljs-variable language_">count</span>.index + <span class="hljs-number">1</span>}</span>&quot;</span>
<span class="hljs-attr">json</span> = <span class="hljs-string">&quot;<span class="hljs-subst">${<span class="hljs-built_in">jsonencode</span>({ <span class="hljs-attr">a</span> = <span class="hljs-number">1</span>, <span class="hljs-attr">b</span> = [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>] })}</span>&quot;</span>
<span class="hljs-attr">strip</span> = <span class="hljs-string">&quot;<span class="hljs-subst">${~ <span class="hljs-variable language_">var</span>.x ~}</span>&quot;</span>
<span class="hljs-attr">nested</span> = <span class="hljs-string">&quot;<span class="hljs-subst">${<span class="hljs-built_in">format</span>(<span class="hljs-string">&quot;%s-%s&quot;</span>, <span class="hljs-variable language_">var</span>.a, <span class="hljs-string">&quot;<span class="hljs-subst">${<span class="hljs-variable language_">var</span>.b}</span>&quot;</span>)}</span>&quot;</span>
""");
    }

    [Fact]
    public void TemplateDirectives()
    {
        AssertHighlighter("hcl",
"""
message = "Hello, %{ if var.name != "" }${var.name}%{ else }unnamed%{ endif }!"
list = "%{ for ip in var.ips }${ip} %{ endfor }"
""",
"""
<span class="hljs-attr">message</span> = <span class="hljs-string">&quot;Hello, <span class="hljs-subst">%{ <span class="hljs-keyword">if</span> <span class="hljs-variable language_">var</span>.name != <span class="hljs-string">&quot;&quot;</span> }</span><span class="hljs-subst">${<span class="hljs-variable language_">var</span>.name}</span><span class="hljs-subst">%{ <span class="hljs-keyword">else</span> }</span>unnamed<span class="hljs-subst">%{ <span class="hljs-keyword">endif</span> }</span>!&quot;</span>
<span class="hljs-attr">list</span> = <span class="hljs-string">&quot;<span class="hljs-subst">%{ <span class="hljs-keyword">for</span> ip <span class="hljs-keyword">in</span> <span class="hljs-variable language_">var</span>.ips }</span><span class="hljs-subst">${ip}</span> <span class="hljs-subst">%{ <span class="hljs-keyword">endfor</span> }</span>&quot;</span>
""");
    }

    [Fact]
    public void Functions()
    {
        AssertHighlighter("hcl",
"""
a = length(var.list)
b = lookup(var.map, "key", "default")
c = cidrsubnet(var.cidr, 8, 1)
d = file("${path.module}/script.sh")
e = templatefile("${path.module}/tpl.tftpl", { name = "x" })
f = try(var.obj.attr, null)
g = coalesce(var.a, var.b, "fallback")
h = provider::aws::arn_parse(var.arn)
i = jsondecode(file("config.json")).key
""",
"""
<span class="hljs-attr">a</span> = <span class="hljs-built_in">length</span>(<span class="hljs-variable language_">var</span>.list)
<span class="hljs-attr">b</span> = <span class="hljs-built_in">lookup</span>(<span class="hljs-variable language_">var</span>.map, <span class="hljs-string">&quot;key&quot;</span>, <span class="hljs-string">&quot;default&quot;</span>)
<span class="hljs-attr">c</span> = <span class="hljs-built_in">cidrsubnet</span>(<span class="hljs-variable language_">var</span>.cidr, <span class="hljs-number">8</span>, <span class="hljs-number">1</span>)
<span class="hljs-attr">d</span> = <span class="hljs-built_in">file</span>(<span class="hljs-string">&quot;<span class="hljs-subst">${<span class="hljs-variable language_">path</span>.module}</span>/script.sh&quot;</span>)
<span class="hljs-attr">e</span> = <span class="hljs-built_in">templatefile</span>(<span class="hljs-string">&quot;<span class="hljs-subst">${<span class="hljs-variable language_">path</span>.module}</span>/tpl.tftpl&quot;</span>, { <span class="hljs-attr">name</span> = <span class="hljs-string">&quot;x&quot;</span> })
<span class="hljs-attr">f</span> = <span class="hljs-built_in">try</span>(<span class="hljs-variable language_">var</span>.obj.attr, <span class="hljs-literal">null</span>)
<span class="hljs-attr">g</span> = <span class="hljs-built_in">coalesce</span>(<span class="hljs-variable language_">var</span>.a, <span class="hljs-variable language_">var</span>.b, <span class="hljs-string">&quot;fallback&quot;</span>)
<span class="hljs-attr">h</span> = <span class="hljs-built_in">provider::aws::arn_parse</span>(<span class="hljs-variable language_">var</span>.arn)
<span class="hljs-attr">i</span> = <span class="hljs-built_in">jsondecode</span>(<span class="hljs-built_in">file</span>(<span class="hljs-string">&quot;config.json&quot;</span>)).key
""");
    }

    [Fact]
    public void Splat()
    {
        AssertHighlighter("hcl",
"""
ids   = aws_instance.web[*].id
names = var.users.*.name
first = var.list[0]
key   = var.map["key"]
""",
"""
<span class="hljs-attr">ids</span>   = aws_instance.web[*].id
<span class="hljs-attr">names</span> = <span class="hljs-variable language_">var</span>.users.*.name
<span class="hljs-attr">first</span> = <span class="hljs-variable language_">var</span>.list[<span class="hljs-number">0</span>]
<span class="hljs-attr">key</span>   = <span class="hljs-variable language_">var</span>.map[<span class="hljs-string">&quot;key&quot;</span>]
""");
    }

    [Fact]
    public void Tfvars()
    {
        AssertHighlighter("hcl",
"""
region         = "eu-west-1"
instance_count = 3
enable_nat     = true
allowed_cidrs  = ["10.0.0.0/8", "192.168.0.0/16"]
tags = {
  Team  = "platform"
  "Cost-Center" = "1234"
}
""",
"""
<span class="hljs-attr">region</span>         = <span class="hljs-string">&quot;eu-west-1&quot;</span>
<span class="hljs-attr">instance_count</span> = <span class="hljs-number">3</span>
<span class="hljs-attr">enable_nat</span>     = <span class="hljs-literal">true</span>
<span class="hljs-attr">allowed_cidrs</span>  = [<span class="hljs-string">&quot;10.0.0.0/8&quot;</span>, <span class="hljs-string">&quot;192.168.0.0/16&quot;</span>]
<span class="hljs-attr">tags</span> = {
  <span class="hljs-attr">Team</span>  = <span class="hljs-string">&quot;platform&quot;</span>
  <span class="hljs-string">&quot;Cost-Center&quot;</span> = <span class="hljs-string">&quot;1234&quot;</span>
}
""");
    }

    [Fact]
    public void ImportMovedRemoved()
    {
        AssertHighlighter("hcl",
"""
import {
  to = aws_instance.web
  id = "i-1234567890"
}

moved {
  from = aws_instance.old
  to   = aws_instance.new
}

removed {
  from = aws_instance.gone
  lifecycle {
    destroy = false
  }
}
""",
"""
<span class="hljs-section">import</span> {
  <span class="hljs-attr">to</span> = aws_instance.web
  <span class="hljs-attr">id</span> = <span class="hljs-string">&quot;i-1234567890&quot;</span>
}

<span class="hljs-section">moved</span> {
  <span class="hljs-attr">from</span> = aws_instance.old
  <span class="hljs-attr">to</span>   = aws_instance.new
}

<span class="hljs-section">removed</span> {
  <span class="hljs-attr">from</span> = aws_instance.gone
  <span class="hljs-section">lifecycle</span> {
    <span class="hljs-attr">destroy</span> = <span class="hljs-literal">false</span>
  }
}
""");
    }

    [Fact]
    public void CheckBlock()
    {
        AssertHighlighter("hcl",
"""
check "health" {
  data "http" "site" {
    url = "https://example.com"
  }

  assert {
    condition     = data.http.site.status_code == 200
    error_message = "Site is down"
  }
}
""",
"""
<span class="hljs-section">check</span> <span class="hljs-string">&quot;health&quot;</span> {
  <span class="hljs-section">data</span> <span class="hljs-string">&quot;http&quot;</span> <span class="hljs-string">&quot;site&quot;</span> {
    <span class="hljs-attr">url</span> = <span class="hljs-string">&quot;https://example.com&quot;</span>
  }

  <span class="hljs-section">assert</span> {
    <span class="hljs-attr">condition</span>     = <span class="hljs-variable language_">data</span>.http.site.status_code == <span class="hljs-number">200</span>
    <span class="hljs-attr">error_message</span> = <span class="hljs-string">&quot;Site is down&quot;</span>
  }
}
""");
    }

    [Fact]
    public void Provisioner()
    {
        AssertHighlighter("hcl",
"""
resource "aws_instance" "web" {
  provisioner "local-exec" {
    command = "echo ${self.private_ip} >> private_ips.txt"
    when    = destroy
  }

  connection {
    type = "ssh"
    host = self.public_ip
  }
}
""",
"""
<span class="hljs-section">resource</span> <span class="hljs-string">&quot;aws_instance&quot;</span> <span class="hljs-string">&quot;web&quot;</span> {
  <span class="hljs-section">provisioner</span> <span class="hljs-string">&quot;local-exec&quot;</span> {
    <span class="hljs-attr">command</span> = <span class="hljs-string">&quot;echo <span class="hljs-subst">${<span class="hljs-variable language_">self</span>.private_ip}</span> &gt;&gt; private_ips.txt&quot;</span>
    <span class="hljs-attr">when</span>    = destroy
  }

  <span class="hljs-section">connection</span> {
    <span class="hljs-attr">type</span> = <span class="hljs-string">&quot;ssh&quot;</span>
    <span class="hljs-attr">host</span> = <span class="hljs-variable language_">self</span>.public_ip
  }
}
""");
    }

    [Fact]
    public void NomadJob()
    {
        AssertHighlighter("hcl",
"""
job "docs" {
  datacenters = ["dc1"]
  type        = "service"

  group "example" {
    count = 1
    task "server" {
      driver = "docker"
      config {
        image = "hashicorp/http-echo"
        args  = ["-listen", ":5678", "-text", "hello world"]
      }
      resources {
        cpu    = 500
        memory = 256
      }
    }
  }
}
""",
"""
<span class="hljs-section">job</span> <span class="hljs-string">&quot;docs&quot;</span> {
  <span class="hljs-attr">datacenters</span> = [<span class="hljs-string">&quot;dc1&quot;</span>]
  <span class="hljs-attr">type</span>        = <span class="hljs-string">&quot;service&quot;</span>

  <span class="hljs-section">group</span> <span class="hljs-string">&quot;example&quot;</span> {
    <span class="hljs-attr">count</span> = <span class="hljs-number">1</span>
    <span class="hljs-section">task</span> <span class="hljs-string">&quot;server&quot;</span> {
      <span class="hljs-attr">driver</span> = <span class="hljs-string">&quot;docker&quot;</span>
      <span class="hljs-section">config</span> {
        <span class="hljs-attr">image</span> = <span class="hljs-string">&quot;hashicorp/http-echo&quot;</span>
        <span class="hljs-attr">args</span>  = [<span class="hljs-string">&quot;-listen&quot;</span>, <span class="hljs-string">&quot;:5678&quot;</span>, <span class="hljs-string">&quot;-text&quot;</span>, <span class="hljs-string">&quot;hello world&quot;</span>]
      }
      <span class="hljs-section">resources</span> {
        <span class="hljs-attr">cpu</span>    = <span class="hljs-number">500</span>
        <span class="hljs-attr">memory</span> = <span class="hljs-number">256</span>
      }
    }
  }
}
""");
    }

    [Fact]
    public void PackerTemplate()
    {
        AssertHighlighter("hcl",
"""
source "amazon-ebs" "ubuntu" {
  ami_name      = "packer-${local.timestamp}"
  instance_type = "t2.micro"
}

build {
  sources = ["source.amazon-ebs.ubuntu"]
}
""",
"""
<span class="hljs-section">source</span> <span class="hljs-string">&quot;amazon-ebs&quot;</span> <span class="hljs-string">&quot;ubuntu&quot;</span> {
  <span class="hljs-attr">ami_name</span>      = <span class="hljs-string">&quot;packer-<span class="hljs-subst">${<span class="hljs-variable language_">local</span>.timestamp}</span>&quot;</span>
  <span class="hljs-attr">instance_type</span> = <span class="hljs-string">&quot;t2.micro&quot;</span>
}

<span class="hljs-section">build</span> {
  <span class="hljs-attr">sources</span> = [<span class="hljs-string">&quot;source.amazon-ebs.ubuntu&quot;</span>]
}
""");
    }

    [Fact]
    public void OneLineBlocks()
    {
        AssertHighlighter("hcl",
"""
resource "null_resource" "x" {}
locals { a = 1 }
""",
"""
<span class="hljs-section">resource</span> <span class="hljs-string">&quot;null_resource&quot;</span> <span class="hljs-string">&quot;x&quot;</span> {}
<span class="hljs-section">locals</span> { <span class="hljs-attr">a</span> = <span class="hljs-number">1</span> }
""");
    }

    [Fact]
    public void IdentifiersStartingWithKeywords()
    {
        AssertHighlighter("hcl",
"""
for_each = var.x
if_enabled = true
in_use = false
x = var.for
y = each.value.if
z = local.in-use
""",
"""
<span class="hljs-attr">for_each</span> = <span class="hljs-variable language_">var</span>.x
<span class="hljs-attr">if_enabled</span> = <span class="hljs-literal">true</span>
<span class="hljs-attr">in_use</span> = <span class="hljs-literal">false</span>
<span class="hljs-attr">x</span> = <span class="hljs-variable language_">var</span>.for
<span class="hljs-attr">y</span> = <span class="hljs-variable language_">each</span>.value.if
<span class="hljs-attr">z</span> = <span class="hljs-variable language_">local</span>.in-use
""");
    }

    [Fact]
    public void IdentifiersWithDashes()
    {
        AssertHighlighter("hcl",
"""
my-attr = 1
x = my-var - 1
""",
"""
<span class="hljs-attr">my-attr</span> = <span class="hljs-number">1</span>
<span class="hljs-attr">x</span> = my-var - <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void UnterminatedString()
    {
        AssertHighlighter("hcl",
"""
a = "unterminated
b = "ok"
""",
"""
<span class="hljs-attr">a</span> = <span class="hljs-string">&quot;unterminated</span>
<span class="hljs-attr">b</span> = <span class="hljs-string">&quot;ok&quot;</span>
""");
    }

    [Fact]
    public void ComparisonsAreNotAttributes()
    {
        AssertHighlighter("hcl",
"""
x = a == b
y = { for k, v in m : k => v }
z = a <= b
""",
"""
<span class="hljs-attr">x</span> = a == b
<span class="hljs-attr">y</span> = { <span class="hljs-keyword">for</span> k, v <span class="hljs-keyword">in</span> m : k =&gt; v }
<span class="hljs-attr">z</span> = a &lt;= b
""");
    }

    [Fact]
    public void ObjectKeysWithColons()
    {
        AssertHighlighter("hcl",
"""
x = {
  "a": 1,
  b: 2
}
""",
"""
<span class="hljs-attr">x</span> = {
  <span class="hljs-string">&quot;a&quot;</span>: <span class="hljs-number">1</span>,
  b: <span class="hljs-number">2</span>
}
""");
    }

    [Fact]
    public void EphemeralResource()
    {
        AssertHighlighter("hcl",
"""
ephemeral "aws_secretsmanager_secret_version" "db" {
  secret_id = aws_secretsmanager_secret.db.id
}
""",
"""
<span class="hljs-section">ephemeral</span> <span class="hljs-string">&quot;aws_secretsmanager_secret_version&quot;</span> <span class="hljs-string">&quot;db&quot;</span> {
  <span class="hljs-attr">secret_id</span> = aws_secretsmanager_secret.db.id
}
""");
    }

    [Fact]
    public void RealisticModule()
    {
        AssertHighlighter("hcl",
"""
terraform {
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }
}

variable "environment" {
  type    = string
  default = "dev"
}

locals {
  name = "app-${var.environment}"
  subnets = {
    for idx, az in data.aws_availability_zones.available.names :
    az => cidrsubnet("10.0.0.0/16", 8, idx)
  }
}

data "aws_availability_zones" "available" {
  state = "available"
}

resource "aws_subnet" "private" {
  for_each          = local.subnets
  vpc_id            = aws_vpc.main.id
  availability_zone = each.key
  cidr_block        = each.value

  tags = merge(local.tags, {
    Name = "${local.name}-private-${each.key}"
  })
}

output "subnet_ids" {
  value = [for s in aws_subnet.private : s.id]
}
""",
"""
<span class="hljs-section">terraform</span> {
  <span class="hljs-section">required_providers</span> {
    <span class="hljs-attr">aws</span> = {
      <span class="hljs-attr">source</span>  = <span class="hljs-string">&quot;hashicorp/aws&quot;</span>
      <span class="hljs-attr">version</span> = <span class="hljs-string">&quot;~&gt; 5.0&quot;</span>
    }
  }
}

<span class="hljs-section">variable</span> <span class="hljs-string">&quot;environment&quot;</span> {
  <span class="hljs-attr">type</span>    = <span class="hljs-type">string</span>
  <span class="hljs-attr">default</span> = <span class="hljs-string">&quot;dev&quot;</span>
}

<span class="hljs-section">locals</span> {
  <span class="hljs-attr">name</span> = <span class="hljs-string">&quot;app-<span class="hljs-subst">${<span class="hljs-variable language_">var</span>.environment}</span>&quot;</span>
  <span class="hljs-attr">subnets</span> = {
    <span class="hljs-keyword">for</span> idx, az <span class="hljs-keyword">in</span> <span class="hljs-variable language_">data</span>.aws_availability_zones.available.names :
    az =&gt; <span class="hljs-built_in">cidrsubnet</span>(<span class="hljs-string">&quot;10.0.0.0/16&quot;</span>, <span class="hljs-number">8</span>, idx)
  }
}

<span class="hljs-section">data</span> <span class="hljs-string">&quot;aws_availability_zones&quot;</span> <span class="hljs-string">&quot;available&quot;</span> {
  <span class="hljs-attr">state</span> = <span class="hljs-string">&quot;available&quot;</span>
}

<span class="hljs-section">resource</span> <span class="hljs-string">&quot;aws_subnet&quot;</span> <span class="hljs-string">&quot;private&quot;</span> {
  <span class="hljs-attr">for_each</span>          = <span class="hljs-variable language_">local</span>.subnets
  <span class="hljs-attr">vpc_id</span>            = aws_vpc.main.id
  <span class="hljs-attr">availability_zone</span> = <span class="hljs-variable language_">each</span>.key
  <span class="hljs-attr">cidr_block</span>        = <span class="hljs-variable language_">each</span>.value

  <span class="hljs-attr">tags</span> = <span class="hljs-built_in">merge</span>(<span class="hljs-variable language_">local</span>.tags, {
    <span class="hljs-attr">Name</span> = <span class="hljs-string">&quot;<span class="hljs-subst">${<span class="hljs-variable language_">local</span>.name}</span>-private-<span class="hljs-subst">${<span class="hljs-variable language_">each</span>.key}</span>&quot;</span>
  })
}

<span class="hljs-section">output</span> <span class="hljs-string">&quot;subnet_ids&quot;</span> {
  <span class="hljs-attr">value</span> = [<span class="hljs-keyword">for</span> s <span class="hljs-keyword">in</span> aws_subnet.private : s.id]
}
""");
    }
}
