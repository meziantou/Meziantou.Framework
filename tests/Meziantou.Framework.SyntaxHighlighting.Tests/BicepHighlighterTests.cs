namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class BicepHighlighterTests
{
    [Fact]
    public void StorageAccount()
    {
        AssertHighlighter("bicep",
"""
metadata description = 'Creates a storage account and a web app'

@description('The prefix to use for the storage account name.')
@minLength(3)
@maxLength(11)
param storagePrefix string

param storageSKU string = 'Standard_LRS'
param location string = resourceGroup().location

var uniqueStorageName = '${storagePrefix}${uniqueString(resourceGroup().id)}'

resource stg 'Microsoft.Storage/storageAccounts@2025-06-01' = {
  name: uniqueStorageName
  location: location
  sku: {
    name: storageSKU
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
  }
}

output storageEndpoint object = stg.properties.primaryEndpoints
""",
"""
<span class="hljs-keyword">metadata</span> <span class="hljs-variable">description</span> = <span class="hljs-string">&#x27;Creates a storage account and a web app&#x27;</span>

<span class="hljs-meta">@description</span>(<span class="hljs-string">&#x27;The prefix to use for the storage account name.&#x27;</span>)
<span class="hljs-meta">@minLength</span>(<span class="hljs-number">3</span>)
<span class="hljs-meta">@maxLength</span>(<span class="hljs-number">11</span>)
<span class="hljs-keyword">param</span> <span class="hljs-variable">storagePrefix</span> <span class="hljs-type">string</span>

<span class="hljs-keyword">param</span> <span class="hljs-variable">storageSKU</span> <span class="hljs-type">string</span> = <span class="hljs-string">&#x27;Standard_LRS&#x27;</span>
<span class="hljs-keyword">param</span> <span class="hljs-variable">location</span> <span class="hljs-type">string</span> = <span class="hljs-built_in">resourceGroup</span>().location

<span class="hljs-keyword">var</span> <span class="hljs-variable">uniqueStorageName</span> = <span class="hljs-string">&#x27;<span class="hljs-subst">${storagePrefix}</span><span class="hljs-subst">${<span class="hljs-built_in">uniqueString</span>(<span class="hljs-built_in">resourceGroup</span>().id)}</span>&#x27;</span>

<span class="hljs-keyword">resource</span> <span class="hljs-variable">stg</span> <span class="hljs-string">&#x27;Microsoft.Storage/storageAccounts@2025-06-01&#x27;</span> = {
  <span class="hljs-attr">name</span>: uniqueStorageName
  <span class="hljs-attr">location</span>: location
  <span class="hljs-attr">sku</span>: {
    <span class="hljs-attr">name</span>: storageSKU
  }
  <span class="hljs-attr">kind</span>: <span class="hljs-string">&#x27;StorageV2&#x27;</span>
  <span class="hljs-attr">properties</span>: {
    <span class="hljs-attr">supportsHttpsTrafficOnly</span>: <span class="hljs-literal">true</span>
  }
}

<span class="hljs-keyword">output</span> <span class="hljs-variable">storageEndpoint</span> <span class="hljs-type">object</span> = stg.properties.primaryEndpoints
""");
    }

    [Fact]
    public void ModulesAndScope()
    {
        AssertHighlighter("bicep",
"""
targetScope = 'subscription'

module webModule './webApp.bicep' = {
  name: 'webDeploy'
  scope: resourceGroup('rg-web')
  params: {
    skuName: 'S1'
    location: location
  }
}

module registry 'br:myregistry.azurecr.io/bicep/modules/storage:v1' = {
  name: 'storageDeploy'
}
""",
"""
<span class="hljs-keyword">targetScope</span> = <span class="hljs-string">&#x27;subscription&#x27;</span>

<span class="hljs-keyword">module</span> <span class="hljs-variable">webModule</span> <span class="hljs-string">&#x27;./webApp.bicep&#x27;</span> = {
  <span class="hljs-attr">name</span>: <span class="hljs-string">&#x27;webDeploy&#x27;</span>
  <span class="hljs-attr">scope</span>: <span class="hljs-built_in">resourceGroup</span>(<span class="hljs-string">&#x27;rg-web&#x27;</span>)
  <span class="hljs-attr">params</span>: {
    <span class="hljs-attr">skuName</span>: <span class="hljs-string">&#x27;S1&#x27;</span>
    <span class="hljs-attr">location</span>: location
  }
}

<span class="hljs-keyword">module</span> <span class="hljs-variable">registry</span> <span class="hljs-string">&#x27;br:myregistry.azurecr.io/bicep/modules/storage:v1&#x27;</span> = {
  <span class="hljs-attr">name</span>: <span class="hljs-string">&#x27;storageDeploy&#x27;</span>
}
""");
    }

    [Fact]
    public void ChildResources()
    {
        AssertHighlighter("bicep",
"""
resource storage 'Microsoft.Storage/storageAccounts@2025-06-01' existing = {
  name: 'examplestorage'

  resource service 'fileServices' = {
    name: 'default'
  }
}

resource share 'Microsoft.Storage/storageAccounts/fileServices/shares@2025-06-01' = {
  name: 'exampleshare'
  parent: service
}

output shareId string = storage::service.id
""",
"""
<span class="hljs-keyword">resource</span> <span class="hljs-variable">storage</span> <span class="hljs-string">&#x27;Microsoft.Storage/storageAccounts@2025-06-01&#x27;</span> <span class="hljs-keyword">existing</span> = {
  <span class="hljs-attr">name</span>: <span class="hljs-string">&#x27;examplestorage&#x27;</span>

  <span class="hljs-keyword">resource</span> <span class="hljs-variable">service</span> <span class="hljs-string">&#x27;fileServices&#x27;</span> = {
    <span class="hljs-attr">name</span>: <span class="hljs-string">&#x27;default&#x27;</span>
  }
}

<span class="hljs-keyword">resource</span> <span class="hljs-variable">share</span> <span class="hljs-string">&#x27;Microsoft.Storage/storageAccounts/fileServices/shares@2025-06-01&#x27;</span> = {
  <span class="hljs-attr">name</span>: <span class="hljs-string">&#x27;exampleshare&#x27;</span>
  <span class="hljs-attr">parent</span>: service
}

<span class="hljs-keyword">output</span> <span class="hljs-variable">shareId</span> <span class="hljs-type">string</span> = storage::service.id
""");
    }

    [Fact]
    public void LoopsAndConditions()
    {
        AssertHighlighter("bicep",
"""
param deployZone bool
param storageCount int = 2

resource dnsZone 'Microsoft.Network/dnsZones@2023-07-01-preview' = if (deployZone) {
  name: 'myZone'
  location: 'global'
}

resource accounts 'Microsoft.Storage/storageAccounts@2025-06-01' = [for i in range(0, storageCount): if (i % 2 == 0) {
  name: 'sa0820${i}'
  kind: 'StorageV2'
}]

var names = [for (item, index) in items: {
  name: item.name
  tier: index < 2 ? 'Premium' : 'Standard'
}]

var sku = isProd ? 'Standard_GRS' : 'Standard_LRS'
var first = empty(values) ? null : values[0]
var safe = stg.?properties.?primaryEndpoints ?? {}
""",
"""
<span class="hljs-keyword">param</span> <span class="hljs-variable">deployZone</span> <span class="hljs-type">bool</span>
<span class="hljs-keyword">param</span> <span class="hljs-variable">storageCount</span> <span class="hljs-type">int</span> = <span class="hljs-number">2</span>

<span class="hljs-keyword">resource</span> <span class="hljs-variable">dnsZone</span> <span class="hljs-string">&#x27;Microsoft.Network/dnsZones@2023-07-01-preview&#x27;</span> = <span class="hljs-keyword">if</span> (deployZone) {
  <span class="hljs-attr">name</span>: <span class="hljs-string">&#x27;myZone&#x27;</span>
  <span class="hljs-attr">location</span>: <span class="hljs-string">&#x27;global&#x27;</span>
}

<span class="hljs-keyword">resource</span> <span class="hljs-variable">accounts</span> <span class="hljs-string">&#x27;Microsoft.Storage/storageAccounts@2025-06-01&#x27;</span> = [<span class="hljs-keyword">for</span> i <span class="hljs-keyword">in</span> <span class="hljs-built_in">range</span>(<span class="hljs-number">0</span>, storageCount): <span class="hljs-keyword">if</span> (i % <span class="hljs-number">2</span> == <span class="hljs-number">0</span>) {
  <span class="hljs-attr">name</span>: <span class="hljs-string">&#x27;sa0820<span class="hljs-subst">${i}</span>&#x27;</span>
  <span class="hljs-attr">kind</span>: <span class="hljs-string">&#x27;StorageV2&#x27;</span>
}]

<span class="hljs-keyword">var</span> <span class="hljs-variable">names</span> = [<span class="hljs-keyword">for</span> (item, index) <span class="hljs-keyword">in</span> items: {
  <span class="hljs-attr">name</span>: item.name
  <span class="hljs-attr">tier</span>: index &lt; <span class="hljs-number">2</span> ? <span class="hljs-string">&#x27;Premium&#x27;</span> : <span class="hljs-string">&#x27;Standard&#x27;</span>
}]

<span class="hljs-keyword">var</span> <span class="hljs-variable">sku</span> = isProd ? <span class="hljs-string">&#x27;Standard_GRS&#x27;</span> : <span class="hljs-string">&#x27;Standard_LRS&#x27;</span>
<span class="hljs-keyword">var</span> <span class="hljs-variable">first</span> = <span class="hljs-built_in">empty</span>(values) ? <span class="hljs-literal">null</span> : values[<span class="hljs-number">0</span>]
<span class="hljs-keyword">var</span> <span class="hljs-variable">safe</span> = stg.?properties.?primaryEndpoints ?? {}
""");
    }

    [Fact]
    public void UserDefinedTypesAndFunctions()
    {
        AssertHighlighter("bicep",
"""
@export()
type storageAccountSkuType = 'Standard_LRS' | 'Standard_GRS'

@sealed()
type storageAccountConfigType = {
  name: string
  sku: storageAccountSkuType
  tags: object?
  'odd-key': int
}

func buildUrl(https bool, hostname string, path string) string => '${https ? 'https' : 'http'}://${hostname}${empty(path) ? '' : '/${path}'}'

output azureUrl string = buildUrl(true, 'microsoft.com', 'azure')
""",
"""
<span class="hljs-meta">@export</span>()
<span class="hljs-keyword">type</span> <span class="hljs-title class_">storageAccountSkuType</span> = <span class="hljs-string">&#x27;Standard_LRS&#x27;</span> | <span class="hljs-string">&#x27;Standard_GRS&#x27;</span>

<span class="hljs-meta">@sealed</span>()
<span class="hljs-keyword">type</span> <span class="hljs-title class_">storageAccountConfigType</span> = {
  <span class="hljs-attr">name</span>: <span class="hljs-type">string</span>
  <span class="hljs-attr">sku</span>: storageAccountSkuType
  <span class="hljs-attr">tags</span>: <span class="hljs-type">object</span>?
  <span class="hljs-attr">&#x27;odd-key&#x27;</span>: <span class="hljs-type">int</span>
}

<span class="hljs-keyword">func</span> <span class="hljs-title function_">buildUrl</span>(https <span class="hljs-type">bool</span>, hostname <span class="hljs-type">string</span>, path <span class="hljs-type">string</span>) <span class="hljs-type">string</span> =&gt; <span class="hljs-string">&#x27;<span class="hljs-subst">${https ? <span class="hljs-string">&#x27;https&#x27;</span> : <span class="hljs-string">&#x27;http&#x27;</span>}</span>://<span class="hljs-subst">${hostname}</span><span class="hljs-subst">${<span class="hljs-built_in">empty</span>(path) ? <span class="hljs-string">&#x27;&#x27;</span> : <span class="hljs-string">&#x27;/<span class="hljs-subst">${path}</span>&#x27;</span>}</span>&#x27;</span>

<span class="hljs-keyword">output</span> <span class="hljs-variable">azureUrl</span> <span class="hljs-type">string</span> = <span class="hljs-title function_ invoke__">buildUrl</span>(<span class="hljs-literal">true</span>, <span class="hljs-string">&#x27;microsoft.com&#x27;</span>, <span class="hljs-string">&#x27;azure&#x27;</span>)
""");
    }

    [Fact]
    public void ImportsAndExtensions()
    {
        AssertHighlighter("bicep",
"""
import {myType, myVar} from 'exports.bicep'
import * as shared from './shared.bicep'
extension microsoftGraphV1
extension 'br:mcr.microsoft.com/bicep/extensions/microsoftgraph/v1.0:0.1.8-preview' as graph
import 'kubernetes@1.0.0' with {
  namespace: 'default'
  kubeConfig: kubeConfig
} as k8s
""",
"""
<span class="hljs-keyword">import</span> {myType, myVar} <span class="hljs-keyword">from</span> <span class="hljs-string">&#x27;exports.bicep&#x27;</span>
<span class="hljs-keyword">import</span> * <span class="hljs-keyword">as</span> shared <span class="hljs-keyword">from</span> <span class="hljs-string">&#x27;./shared.bicep&#x27;</span>
<span class="hljs-keyword">extension</span> microsoftGraphV1
<span class="hljs-keyword">extension</span> <span class="hljs-string">&#x27;br:mcr.microsoft.com/bicep/extensions/microsoftgraph/v1.0:0.1.8-preview&#x27;</span> <span class="hljs-keyword">as</span> graph
<span class="hljs-keyword">import</span> <span class="hljs-string">&#x27;kubernetes@1.0.0&#x27;</span> <span class="hljs-keyword">with</span> {
  <span class="hljs-attr">namespace</span>: <span class="hljs-string">&#x27;default&#x27;</span>
  <span class="hljs-attr">kubeConfig</span>: kubeConfig
} <span class="hljs-keyword">as</span> k8s
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("bicep",
"""
var escaped = 'it\'s a \\ backslash \n \t \u{1F600} \${not interpolated}'
var interpolated = 'Hello ${name}, you have ${length(items)} items'
var multi = '''
line one
it's ${not} interpolated
'''
var multiInterp = $'''
Hello ${name}!
'''
var multiDouble = $$'''
Literal ${x}, interpolated $${name}
'''
var unterminated = 'oops
var next = 1
""",
"""
<span class="hljs-keyword">var</span> <span class="hljs-variable">escaped</span> = <span class="hljs-string">&#x27;it\&#x27;s a \\ backslash \n \t \u{1F600} \${not interpolated}&#x27;</span>
<span class="hljs-keyword">var</span> <span class="hljs-variable">interpolated</span> = <span class="hljs-string">&#x27;Hello <span class="hljs-subst">${name}</span>, you have <span class="hljs-subst">${<span class="hljs-built_in">length</span>(items)}</span> items&#x27;</span>
<span class="hljs-keyword">var</span> <span class="hljs-variable">multi</span> = <span class="hljs-string">&#x27;&#x27;&#x27;
line one
it&#x27;s ${not} interpolated
&#x27;&#x27;&#x27;</span>
<span class="hljs-keyword">var</span> <span class="hljs-variable">multiInterp</span> = <span class="hljs-string">$&#x27;&#x27;&#x27;
Hello <span class="hljs-subst">${name}</span>!
&#x27;&#x27;&#x27;</span>
<span class="hljs-keyword">var</span> <span class="hljs-variable">multiDouble</span> = <span class="hljs-string">$$&#x27;&#x27;&#x27;
Literal ${x}, interpolated <span class="hljs-subst">$${name}</span>
&#x27;&#x27;&#x27;</span>
<span class="hljs-keyword">var</span> <span class="hljs-variable">unterminated</span> = <span class="hljs-string">&#x27;oops</span>
<span class="hljs-keyword">var</span> <span class="hljs-variable">next</span> = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void DecoratorsAndDirectives()
    {
        AssertHighlighter("bicep",
"""
#disable-next-line no-unused-params BCP081
@secure()
@sys.description('The admin password.')
param adminPassword string

@allowed([
  'dev'
  'prod'
])
param environmentName string = 'dev'

@batchSize(2)
@metadata({
  owner: 'team'
})
param count int
""",
"""
<span class="hljs-meta">#disable-next-line no-unused-params BCP081</span>
<span class="hljs-meta">@secure</span>()
<span class="hljs-meta">@sys.description</span>(<span class="hljs-string">&#x27;The admin password.&#x27;</span>)
<span class="hljs-keyword">param</span> <span class="hljs-variable">adminPassword</span> <span class="hljs-type">string</span>

<span class="hljs-meta">@allowed</span>([
  <span class="hljs-string">&#x27;dev&#x27;</span>
  <span class="hljs-string">&#x27;prod&#x27;</span>
])
<span class="hljs-keyword">param</span> <span class="hljs-variable">environmentName</span> <span class="hljs-type">string</span> = <span class="hljs-string">&#x27;dev&#x27;</span>

<span class="hljs-meta">@batchSize</span>(<span class="hljs-number">2</span>)
<span class="hljs-meta">@metadata</span>({
  <span class="hljs-attr">owner</span>: <span class="hljs-string">&#x27;team&#x27;</span>
})
<span class="hljs-keyword">param</span> <span class="hljs-variable">count</span> <span class="hljs-type">int</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("bicep",
"""
// This is your primary NIC.
/*
  This Bicep file assumes the key vault already exists.
*/
param existingKeyVaultName string // trailing
// TODO: review
""",
"""
<span class="hljs-comment">// This is your primary NIC.</span>
<span class="hljs-comment">/*
  This Bicep file assumes the key vault already exists.
*/</span>
<span class="hljs-keyword">param</span> <span class="hljs-variable">existingKeyVaultName</span> <span class="hljs-type">string</span> <span class="hljs-comment">// trailing</span>
<span class="hljs-comment">// <span class="hljs-doctag">TODO:</span> review</span>
""");
    }

    [Fact]
    public void LambdasAndFunctions()
    {
        AssertHighlighter("bicep",
"""
var enabled = filter(items, item => item.enabled)
var names = map(items, (item, i) => '${i}: ${item.name}')
var total = reduce(values, 0, (cur, next) => cur + next)
var keys = storage.listKeys().keys[0].value
var id = az.resourceGroup().id
var joined = sys.concat(a, b)
var custom = myFunc(1)
var obj = json('{"a": 1}')
output value string = string(10)
""",
"""
<span class="hljs-keyword">var</span> <span class="hljs-variable">enabled</span> = <span class="hljs-built_in">filter</span>(items, item =&gt; item.enabled)
<span class="hljs-keyword">var</span> <span class="hljs-variable">names</span> = <span class="hljs-built_in">map</span>(items, (item, i) =&gt; <span class="hljs-string">&#x27;<span class="hljs-subst">${i}</span>: <span class="hljs-subst">${item.name}</span>&#x27;</span>)
<span class="hljs-keyword">var</span> <span class="hljs-variable">total</span> = <span class="hljs-built_in">reduce</span>(values, <span class="hljs-number">0</span>, (cur, next) =&gt; cur + next)
<span class="hljs-keyword">var</span> <span class="hljs-variable">keys</span> = storage.<span class="hljs-built_in">listKeys</span>().keys[<span class="hljs-number">0</span>].value
<span class="hljs-keyword">var</span> <span class="hljs-variable">id</span> = az.<span class="hljs-built_in">resourceGroup</span>().id
<span class="hljs-keyword">var</span> <span class="hljs-variable">joined</span> = sys.<span class="hljs-built_in">concat</span>(a, b)
<span class="hljs-keyword">var</span> <span class="hljs-variable">custom</span> = <span class="hljs-title function_ invoke__">myFunc</span>(<span class="hljs-number">1</span>)
<span class="hljs-keyword">var</span> <span class="hljs-variable">obj</span> = <span class="hljs-built_in">json</span>(<span class="hljs-string">&#x27;{&quot;a&quot;: 1}&#x27;</span>)
<span class="hljs-keyword">output</span> <span class="hljs-variable">value</span> <span class="hljs-type">string</span> = <span class="hljs-built_in">string</span>(<span class="hljs-number">10</span>)
""");
    }

    [Fact]
    public void ObjectsAndArrays()
    {
        AssertHighlighter("bicep",
"""
var tags = {
  environment: 'prod'
  'cost-center': '1234'
  owner: {
    name: 'me'
    type: 'user'
  }
}
var inline = { a: 1, b: true, c: null }
var list = [
  'a'
  1
  false
]
var t = stg.type
""",
"""
<span class="hljs-keyword">var</span> <span class="hljs-variable">tags</span> = {
  <span class="hljs-attr">environment</span>: <span class="hljs-string">&#x27;prod&#x27;</span>
  <span class="hljs-attr">&#x27;cost-center&#x27;</span>: <span class="hljs-string">&#x27;1234&#x27;</span>
  <span class="hljs-attr">owner</span>: {
    <span class="hljs-attr">name</span>: <span class="hljs-string">&#x27;me&#x27;</span>
    <span class="hljs-attr">type</span>: <span class="hljs-string">&#x27;user&#x27;</span>
  }
}
<span class="hljs-keyword">var</span> <span class="hljs-variable">inline</span> = { <span class="hljs-attr">a</span>: <span class="hljs-number">1</span>, <span class="hljs-attr">b</span>: <span class="hljs-literal">true</span>, <span class="hljs-attr">c</span>: <span class="hljs-literal">null</span> }
<span class="hljs-keyword">var</span> <span class="hljs-variable">list</span> = [
  <span class="hljs-string">&#x27;a&#x27;</span>
  <span class="hljs-number">1</span>
  <span class="hljs-literal">false</span>
]
<span class="hljs-keyword">var</span> <span class="hljs-variable">t</span> = stg.type
""");
    }

    [Fact]
    public void ParametersFile()
    {
        AssertHighlighter("bicepparam",
"""
using './main.bicep'

param storageName = 'mystorage'
param location = readEnvironmentVariable('LOCATION', 'westus')
param secret = getSecret('sub', 'rg', 'kv', 'secret')
""",
"""
<span class="hljs-keyword">using</span> <span class="hljs-string">&#x27;./main.bicep&#x27;</span>

<span class="hljs-keyword">param</span> <span class="hljs-variable">storageName</span> = <span class="hljs-string">&#x27;mystorage&#x27;</span>
<span class="hljs-keyword">param</span> <span class="hljs-variable">location</span> = <span class="hljs-built_in">readEnvironmentVariable</span>(<span class="hljs-string">&#x27;LOCATION&#x27;</span>, <span class="hljs-string">&#x27;westus&#x27;</span>)
<span class="hljs-keyword">param</span> <span class="hljs-variable">secret</span> = <span class="hljs-built_in">getSecret</span>(<span class="hljs-string">&#x27;sub&#x27;</span>, <span class="hljs-string">&#x27;rg&#x27;</span>, <span class="hljs-string">&#x27;kv&#x27;</span>, <span class="hljs-string">&#x27;secret&#x27;</span>)
""");
    }
}
