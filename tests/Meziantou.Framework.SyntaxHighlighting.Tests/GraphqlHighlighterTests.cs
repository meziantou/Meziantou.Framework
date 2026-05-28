namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class GraphqlHighlighterTests
{

    [Fact]
    public void Query_AnonymousSimple()
    {
        AssertHighlighter("graphql",
"""
{
  user {
    name
  }
}
""",
"""
<span class="hljs-punctuation">{</span>
  user <span class="hljs-punctuation">{</span>
    name
  <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Query_NamedSimple()
    {
        AssertHighlighter("graphql",
"""
query GetUser {
  user {
    name
    email
  }
}
""",
"""
<span class="hljs-keyword">query</span> GetUser <span class="hljs-punctuation">{</span>
  user <span class="hljs-punctuation">{</span>
    name
    email
  <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Query_WithArgument()
    {
        AssertHighlighter("graphql",
"""
query {
  user(id: 1) {
    name
  }
}
""",
"""
<span class="hljs-keyword">query</span> <span class="hljs-punctuation">{</span>
  user<span class="hljs-punctuation">(</span><span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> <span class="hljs-number">1</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
    name
  <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Query_MultiField()
    {
        AssertHighlighter("graphql",
"""
query {
  id
  name
  email
  createdAt
}
""",
"""
<span class="hljs-keyword">query</span> <span class="hljs-punctuation">{</span>
  id
  name
  email
  createdAt
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Query_NestedSelection()
    {
        AssertHighlighter("graphql",
"""
query {
  user(id: 1) {
    name
    address {
      street
      city
      country
    }
  }
}
""",
"""
<span class="hljs-keyword">query</span> <span class="hljs-punctuation">{</span>
  user<span class="hljs-punctuation">(</span><span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> <span class="hljs-number">1</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
    name
    address <span class="hljs-punctuation">{</span>
      street
      city
      country
    <span class="hljs-punctuation">}</span>
  <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Query_AliasField()
    {
        AssertHighlighter("graphql",
"""
query {
  alice: user(id: 1) { name }
  bob:   user(id: 2) { name }
}
""",
"""
<span class="hljs-keyword">query</span> <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">alice</span><span class="hljs-punctuation">:</span> user<span class="hljs-punctuation">(</span><span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> <span class="hljs-number">1</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span> name <span class="hljs-punctuation">}</span>
  <span class="hljs-symbol">bob</span><span class="hljs-punctuation">:</span>   user<span class="hljs-punctuation">(</span><span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> <span class="hljs-number">2</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span> name <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Query_WithVariables()
    {
        AssertHighlighter("graphql",
"""
query GetUser($id: ID!) {
  user(id: $id) {
    name
  }
}
""",
"""
<span class="hljs-keyword">query</span> GetUser<span class="hljs-punctuation">(</span><span class="hljs-variable">$id</span>: ID<span class="hljs-punctuation">!</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
  user<span class="hljs-punctuation">(</span><span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> <span class="hljs-variable">$id</span>) <span class="hljs-punctuation">{</span>
    name
  <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Query_DefaultVariable()
    {
        AssertHighlighter("graphql",
"""
query Search($q: String = "hello") {
  search(query: $q) { id }
}
""",
"""
<span class="hljs-keyword">query</span> Search<span class="hljs-punctuation">(</span><span class="hljs-variable">$q</span>: String <span class="hljs-punctuation">=</span> <span class="hljs-string">&quot;hello&quot;</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
  search<span class="hljs-punctuation">(</span><span class="hljs-symbol">query</span><span class="hljs-punctuation">:</span> <span class="hljs-variable">$q</span>) <span class="hljs-punctuation">{</span> id <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Query_ListArgument()
    {
        AssertHighlighter("graphql",
"""
query {
  users(ids: [1, 2, 3]) { name }
}
""",
"""
<span class="hljs-keyword">query</span> <span class="hljs-punctuation">{</span>
  users<span class="hljs-punctuation">(</span><span class="hljs-symbol">ids</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">[</span><span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span><span class="hljs-punctuation">]</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span> name <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Query_NestedObjectArg()
    {
        AssertHighlighter("graphql",
"""
query {
  posts(filter: { status: PUBLISHED, tags: ["news"] }) { id }
}
""",
"""
<span class="hljs-keyword">query</span> <span class="hljs-punctuation">{</span>
  posts<span class="hljs-punctuation">(</span><span class="hljs-symbol">filter</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">{</span> <span class="hljs-symbol">status</span><span class="hljs-punctuation">:</span> PUBLISHED, <span class="hljs-symbol">tags</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">[</span><span class="hljs-string">&quot;news&quot;</span><span class="hljs-punctuation">]</span> <span class="hljs-punctuation">}</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span> id <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Query_Pagination()
    {
        AssertHighlighter("graphql",
"""
query Page($after: String, $first: Int = 10) {
  users(after: $after, first: $first) {
    edges {
      cursor
      node { id name }
    }
    pageInfo { hasNextPage endCursor }
  }
}
""",
"""
<span class="hljs-keyword">query</span> Page<span class="hljs-punctuation">(</span><span class="hljs-variable">$after</span>: String, <span class="hljs-variable">$first</span>: Int <span class="hljs-punctuation">=</span> <span class="hljs-number">10</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
  users<span class="hljs-punctuation">(</span><span class="hljs-symbol">after</span><span class="hljs-punctuation">:</span> <span class="hljs-variable">$after</span>, <span class="hljs-symbol">first</span><span class="hljs-punctuation">:</span> <span class="hljs-variable">$first</span>) <span class="hljs-punctuation">{</span>
    edges <span class="hljs-punctuation">{</span>
      cursor
      node <span class="hljs-punctuation">{</span> id name <span class="hljs-punctuation">}</span>
    <span class="hljs-punctuation">}</span>
    pageInfo <span class="hljs-punctuation">{</span> hasNextPage endCursor <span class="hljs-punctuation">}</span>
  <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Mutation_Simple()
    {
        AssertHighlighter("graphql",
"""
mutation {
  createUser(input: { name: "alice" }) { id }
}
""",
"""
<span class="hljs-keyword">mutation</span> <span class="hljs-punctuation">{</span>
  createUser<span class="hljs-punctuation">(</span><span class="hljs-symbol">input</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">{</span> <span class="hljs-symbol">name</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;alice&quot;</span> <span class="hljs-punctuation">}</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span> id <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Mutation_Named()
    {
        AssertHighlighter("graphql",
"""
mutation CreateUser($name: String!) {
  createUser(name: $name) { id name }
}
""",
"""
<span class="hljs-keyword">mutation</span> CreateUser<span class="hljs-punctuation">(</span><span class="hljs-variable">$name</span>: String<span class="hljs-punctuation">!</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
  createUser<span class="hljs-punctuation">(</span><span class="hljs-symbol">name</span><span class="hljs-punctuation">:</span> <span class="hljs-variable">$name</span>) <span class="hljs-punctuation">{</span> id name <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Mutation_Multiple()
    {
        AssertHighlighter("graphql",
"""
mutation Bulk {
  a: createUser(name: "alice") { id }
  b: createUser(name: "bob") { id }
}
""",
"""
<span class="hljs-keyword">mutation</span> Bulk <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">a</span><span class="hljs-punctuation">:</span> createUser<span class="hljs-punctuation">(</span><span class="hljs-symbol">name</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;alice&quot;</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span> id <span class="hljs-punctuation">}</span>
  <span class="hljs-symbol">b</span><span class="hljs-punctuation">:</span> createUser<span class="hljs-punctuation">(</span><span class="hljs-symbol">name</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;bob&quot;</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span> id <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Mutation_Delete()
    {
        AssertHighlighter("graphql",
"""
mutation Delete($id: ID!) {
  deleteUser(id: $id) { success }
}
""",
"""
<span class="hljs-keyword">mutation</span> Delete<span class="hljs-punctuation">(</span><span class="hljs-variable">$id</span>: ID<span class="hljs-punctuation">!</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
  deleteUser<span class="hljs-punctuation">(</span><span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> <span class="hljs-variable">$id</span>) <span class="hljs-punctuation">{</span> success <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Subscription_Simple()
    {
        AssertHighlighter("graphql",
"""
subscription {
  onMessage {
    id
    text
    user { name }
  }
}
""",
"""
<span class="hljs-keyword">subscription</span> <span class="hljs-punctuation">{</span>
  onMessage <span class="hljs-punctuation">{</span>
    id
    text
    user <span class="hljs-punctuation">{</span> name <span class="hljs-punctuation">}</span>
  <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Subscription_Named()
    {
        AssertHighlighter("graphql",
"""
subscription RoomMessages($room: ID!) {
  messageAdded(room: $room) { id text }
}
""",
"""
<span class="hljs-keyword">subscription</span> RoomMessages<span class="hljs-punctuation">(</span><span class="hljs-variable">$room</span>: ID<span class="hljs-punctuation">!</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
  messageAdded<span class="hljs-punctuation">(</span><span class="hljs-symbol">room</span><span class="hljs-punctuation">:</span> <span class="hljs-variable">$room</span>) <span class="hljs-punctuation">{</span> id text <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Fragment_Definition()
    {
        AssertHighlighter("graphql",
"""
fragment UserFields on User {
  id
  name
  email
}
""",
"""
<span class="hljs-keyword">fragment</span> UserFields <span class="hljs-keyword">on</span> User <span class="hljs-punctuation">{</span>
  id
  name
  email
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Fragment_Spread()
    {
        AssertHighlighter("graphql",
"""
query {
  user { ...UserFields }
}
""",
"""
<span class="hljs-keyword">query</span> <span class="hljs-punctuation">{</span>
  user <span class="hljs-punctuation">{</span> <span class="hljs-punctuation">...</span>UserFields <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Fragment_InlineSimple()
    {
        AssertHighlighter("graphql",
"""
query {
  user {
    ... on Admin { role }
    ... on Member { joinedAt }
  }
}
""",
"""
<span class="hljs-keyword">query</span> <span class="hljs-punctuation">{</span>
  user <span class="hljs-punctuation">{</span>
    <span class="hljs-punctuation">...</span> <span class="hljs-keyword">on</span> Admin <span class="hljs-punctuation">{</span> role <span class="hljs-punctuation">}</span>
    <span class="hljs-punctuation">...</span> <span class="hljs-keyword">on</span> Member <span class="hljs-punctuation">{</span> joinedAt <span class="hljs-punctuation">}</span>
  <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Fragment_InlineGuarded()
    {
        AssertHighlighter("graphql",
"""
query {
  search { ... on User @include(if: $expandUsers) { name } }
}
""",
"""
<span class="hljs-keyword">query</span> <span class="hljs-punctuation">{</span>
  search <span class="hljs-punctuation">{</span> <span class="hljs-punctuation">...</span> <span class="hljs-keyword">on</span> User <span class="hljs-meta">@include</span><span class="hljs-punctuation">(</span><span class="hljs-symbol">if</span><span class="hljs-punctuation">:</span> <span class="hljs-variable">$expandUsers</span>) <span class="hljs-punctuation">{</span> name <span class="hljs-punctuation">}</span> <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Fragment_NestedSpread()
    {
        AssertHighlighter("graphql",
"""
fragment Card on User {
  ...Avatar
  bio
}
""",
"""
<span class="hljs-keyword">fragment</span> Card <span class="hljs-keyword">on</span> User <span class="hljs-punctuation">{</span>
  <span class="hljs-punctuation">...</span>Avatar
  bio
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Directive_Include()
    {
        AssertHighlighter("graphql",
"""
query GetUser($withEmail: Boolean!) {
  user {
    name
    email @include(if: $withEmail)
  }
}
""",
"""
<span class="hljs-keyword">query</span> GetUser<span class="hljs-punctuation">(</span><span class="hljs-variable">$withEmail</span>: Boolean<span class="hljs-punctuation">!</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
  user <span class="hljs-punctuation">{</span>
    name
    email <span class="hljs-meta">@include</span><span class="hljs-punctuation">(</span><span class="hljs-symbol">if</span><span class="hljs-punctuation">:</span> <span class="hljs-variable">$withEmail</span>)
  <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Directive_Skip()
    {
        AssertHighlighter("graphql",
"""
query GetUser($skipBio: Boolean!) {
  user {
    name
    bio @skip(if: $skipBio)
  }
}
""",
"""
<span class="hljs-keyword">query</span> GetUser<span class="hljs-punctuation">(</span><span class="hljs-variable">$skipBio</span>: Boolean<span class="hljs-punctuation">!</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
  user <span class="hljs-punctuation">{</span>
    name
    bio <span class="hljs-meta">@skip</span><span class="hljs-punctuation">(</span><span class="hljs-symbol">if</span><span class="hljs-punctuation">:</span> <span class="hljs-variable">$skipBio</span>)
  <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Directive_Deprecated()
    {
        AssertHighlighter("graphql",
"""
type User {
  oldName: String @deprecated(reason: "Use displayName")
  displayName: String!
}
""",
"""
<span class="hljs-keyword">type</span> User <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">oldName</span><span class="hljs-punctuation">:</span> String <span class="hljs-meta">@deprecated</span><span class="hljs-punctuation">(</span><span class="hljs-symbol">reason</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;Use displayName&quot;</span><span class="hljs-punctuation">)</span>
  <span class="hljs-symbol">displayName</span><span class="hljs-punctuation">:</span> String<span class="hljs-punctuation">!</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Directive_Custom()
    {
        AssertHighlighter("graphql",
"""
query {
  user @cached(ttl: 60) { name }
}
""",
"""
<span class="hljs-keyword">query</span> <span class="hljs-punctuation">{</span>
  user <span class="hljs-meta">@cached</span><span class="hljs-punctuation">(</span><span class="hljs-symbol">ttl</span><span class="hljs-punctuation">:</span> <span class="hljs-number">60</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span> name <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Sdl_TypeBasic()
    {
        AssertHighlighter("graphql",
"""
type User {
  id: ID!
  name: String!
  email: String
}
""",
"""
<span class="hljs-keyword">type</span> User <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> ID<span class="hljs-punctuation">!</span>
  <span class="hljs-symbol">name</span><span class="hljs-punctuation">:</span> String<span class="hljs-punctuation">!</span>
  <span class="hljs-symbol">email</span><span class="hljs-punctuation">:</span> String
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Sdl_TypeListNonNull()
    {
        AssertHighlighter("graphql",
"""
type Query {
  users: [User!]!
}
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-keyword">Query</span> <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">users</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">[</span>User<span class="hljs-punctuation">!</span><span class="hljs-punctuation">]</span><span class="hljs-punctuation">!</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Sdl_TypeWithArgs()
    {
        AssertHighlighter("graphql",
"""
type Query {
  user(id: ID!): User
  search(query: String!, limit: Int = 10): [User!]!
}
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-keyword">Query</span> <span class="hljs-punctuation">{</span>
  user<span class="hljs-punctuation">(</span><span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> ID<span class="hljs-punctuation">!</span><span class="hljs-punctuation">)</span><span class="hljs-punctuation">:</span> User
  search<span class="hljs-punctuation">(</span><span class="hljs-symbol">query</span><span class="hljs-punctuation">:</span> String<span class="hljs-punctuation">!</span>, <span class="hljs-symbol">limit</span><span class="hljs-punctuation">:</span> Int <span class="hljs-punctuation">=</span> <span class="hljs-number">10</span><span class="hljs-punctuation">)</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">[</span>User<span class="hljs-punctuation">!</span><span class="hljs-punctuation">]</span><span class="hljs-punctuation">!</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Sdl_TypeImplements()
    {
        AssertHighlighter("graphql",
"""
type User implements Node & Timestamped {
  id: ID!
  createdAt: DateTime!
}
""",
"""
<span class="hljs-keyword">type</span> User implements Node &amp; Timestamped <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> ID<span class="hljs-punctuation">!</span>
  <span class="hljs-symbol">createdAt</span><span class="hljs-punctuation">:</span> DateTime<span class="hljs-punctuation">!</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Sdl_Interface()
    {
        AssertHighlighter("graphql",
"""
interface Node {
  id: ID!
}
""",
"""
<span class="hljs-keyword">interface</span> Node <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> ID<span class="hljs-punctuation">!</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Sdl_InterfaceMultiple()
    {
        AssertHighlighter("graphql",
"""
interface Timestamped {
  createdAt: DateTime!
  updatedAt: DateTime!
}
""",
"""
<span class="hljs-keyword">interface</span> Timestamped <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">createdAt</span><span class="hljs-punctuation">:</span> DateTime<span class="hljs-punctuation">!</span>
  <span class="hljs-symbol">updatedAt</span><span class="hljs-punctuation">:</span> DateTime<span class="hljs-punctuation">!</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Sdl_Union()
    {
        AssertHighlighter("graphql",
"""
union SearchResult = User | Post | Comment
""",
"""
<span class="hljs-keyword">union</span> SearchResult <span class="hljs-punctuation">=</span> User <span class="hljs-punctuation">|</span> Post <span class="hljs-punctuation">|</span> Comment
""");
    }

    [Fact]
    public void Sdl_UnionPipeFirst()
    {
        AssertHighlighter("graphql",
"""
union SearchResult =
  | User
  | Post
  | Comment
""",
"""
<span class="hljs-keyword">union</span> SearchResult <span class="hljs-punctuation">=</span>
  <span class="hljs-punctuation">|</span> User
  <span class="hljs-punctuation">|</span> Post
  <span class="hljs-punctuation">|</span> Comment
""");
    }

    [Fact]
    public void Sdl_EnumBasic()
    {
        AssertHighlighter("graphql",
"""
enum Status {
  ACTIVE
  INACTIVE
  PENDING
}
""",
"""
<span class="hljs-keyword">enum</span> Status <span class="hljs-punctuation">{</span>
  ACTIVE
  INACTIVE
  PENDING
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Sdl_EnumDeprecated()
    {
        AssertHighlighter("graphql",
"""
enum Status {
  ACTIVE
  OLD @deprecated(reason: "Use ACTIVE")
}
""",
"""
<span class="hljs-keyword">enum</span> Status <span class="hljs-punctuation">{</span>
  ACTIVE
  OLD <span class="hljs-meta">@deprecated</span><span class="hljs-punctuation">(</span><span class="hljs-symbol">reason</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;Use ACTIVE&quot;</span><span class="hljs-punctuation">)</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Sdl_InputBasic()
    {
        AssertHighlighter("graphql",
"""
input CreateUserInput {
  name: String!
  email: String
  age: Int = 0
}
""",
"""
<span class="hljs-keyword">input</span> CreateUserInput <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">name</span><span class="hljs-punctuation">:</span> String<span class="hljs-punctuation">!</span>
  <span class="hljs-symbol">email</span><span class="hljs-punctuation">:</span> String
  <span class="hljs-symbol">age</span><span class="hljs-punctuation">:</span> Int <span class="hljs-punctuation">=</span> <span class="hljs-number">0</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Sdl_ScalarCustom()
    {
        AssertHighlighter("graphql",
"""
scalar DateTime
""",
"""
<span class="hljs-keyword">scalar</span> DateTime
""");
    }

    [Fact]
    public void Sdl_ScalarUrl()
    {
        AssertHighlighter("graphql",
"""
scalar URL
""",
"""
<span class="hljs-keyword">scalar</span> URL
""");
    }

    [Fact]
    public void Sdl_SchemaDef()
    {
        AssertHighlighter("graphql",
"""
schema {
  query: Query
  mutation: Mutation
  subscription: Subscription
}
""",
"""
<span class="hljs-keyword">schema</span> <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">query</span><span class="hljs-punctuation">:</span> <span class="hljs-keyword">Query</span>
  <span class="hljs-symbol">mutation</span><span class="hljs-punctuation">:</span> <span class="hljs-keyword">Mutation</span>
  <span class="hljs-symbol">subscription</span><span class="hljs-punctuation">:</span> <span class="hljs-keyword">Subscription</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Sdl_DirectiveDef()
    {
        AssertHighlighter("graphql",
"""
directive @auth(role: Role!) on FIELD_DEFINITION | OBJECT
""",
"""
<span class="hljs-keyword">directive</span> <span class="hljs-meta">@auth</span><span class="hljs-punctuation">(</span><span class="hljs-symbol">role</span><span class="hljs-punctuation">:</span> Role<span class="hljs-punctuation">!</span><span class="hljs-punctuation">)</span> <span class="hljs-keyword">on</span> FIELD_DEFINITION <span class="hljs-punctuation">|</span> OBJECT
""");
    }

    [Fact]
    public void Sdl_ExtendType()
    {
        AssertHighlighter("graphql",
"""
extend type User {
  posts: [Post!]!
}
""",
"""
extend <span class="hljs-keyword">type</span> User <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">posts</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">[</span>Post<span class="hljs-punctuation">!</span><span class="hljs-punctuation">]</span><span class="hljs-punctuation">!</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Sdl_ExtendInterface()
    {
        AssertHighlighter("graphql",
"""
extend interface Node {
  uuid: ID!
}
""",
"""
extend <span class="hljs-keyword">interface</span> Node <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">uuid</span><span class="hljs-punctuation">:</span> ID<span class="hljs-punctuation">!</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Federation_Key()
    {
        AssertHighlighter("graphql",
"""
type User @key(fields: "id") {
  id: ID!
  email: String!
}
""",
"""
<span class="hljs-keyword">type</span> User <span class="hljs-meta">@key</span><span class="hljs-punctuation">(</span><span class="hljs-symbol">fields</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;id&quot;</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> ID<span class="hljs-punctuation">!</span>
  <span class="hljs-symbol">email</span><span class="hljs-punctuation">:</span> String<span class="hljs-punctuation">!</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Federation_KeyComposite()
    {
        AssertHighlighter("graphql",
"""
type Order @key(fields: "id customer { id }") {
  id: ID!
  customer: Customer!
}
""",
"""
<span class="hljs-keyword">type</span> Order <span class="hljs-meta">@key</span><span class="hljs-punctuation">(</span><span class="hljs-symbol">fields</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;id customer { id }&quot;</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> ID<span class="hljs-punctuation">!</span>
  <span class="hljs-symbol">customer</span><span class="hljs-punctuation">:</span> Customer<span class="hljs-punctuation">!</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Federation_External()
    {
        AssertHighlighter("graphql",
"""
extend type User @key(fields: "id") {
  id: ID! @external
  reviews: [Review!]!
}
""",
"""
extend <span class="hljs-keyword">type</span> User <span class="hljs-meta">@key</span><span class="hljs-punctuation">(</span><span class="hljs-symbol">fields</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;id&quot;</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> ID<span class="hljs-punctuation">!</span> <span class="hljs-meta">@external</span>
  <span class="hljs-symbol">reviews</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">[</span>Review<span class="hljs-punctuation">!</span><span class="hljs-punctuation">]</span><span class="hljs-punctuation">!</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Federation_Requires()
    {
        AssertHighlighter("graphql",
"""
type Product @key(fields: "id") {
  id: ID!
  weight: Float @external
  shippingCost: Float @requires(fields: "weight")
}
""",
"""
<span class="hljs-keyword">type</span> Product <span class="hljs-meta">@key</span><span class="hljs-punctuation">(</span><span class="hljs-symbol">fields</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;id&quot;</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> ID<span class="hljs-punctuation">!</span>
  <span class="hljs-symbol">weight</span><span class="hljs-punctuation">:</span> Float <span class="hljs-meta">@external</span>
  <span class="hljs-symbol">shippingCost</span><span class="hljs-punctuation">:</span> Float <span class="hljs-meta">@requires</span><span class="hljs-punctuation">(</span><span class="hljs-symbol">fields</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;weight&quot;</span><span class="hljs-punctuation">)</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Federation_Provides()
    {
        AssertHighlighter("graphql",
"""
type Review {
  id: ID!
  author: User @provides(fields: "name")
}
""",
"""
<span class="hljs-keyword">type</span> Review <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> ID<span class="hljs-punctuation">!</span>
  <span class="hljs-symbol">author</span><span class="hljs-punctuation">:</span> User <span class="hljs-meta">@provides</span><span class="hljs-punctuation">(</span><span class="hljs-symbol">fields</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;name&quot;</span><span class="hljs-punctuation">)</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Federation_Shareable()
    {
        AssertHighlighter("graphql",
"""
type Position @shareable {
  x: Float!
  y: Float!
}
""",
"""
<span class="hljs-keyword">type</span> Position <span class="hljs-meta">@shareable</span> <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">x</span><span class="hljs-punctuation">:</span> Float<span class="hljs-punctuation">!</span>
  <span class="hljs-symbol">y</span><span class="hljs-punctuation">:</span> Float<span class="hljs-punctuation">!</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Federation_Inaccessible()
    {
        AssertHighlighter("graphql",
"""
type User {
  internalId: ID @inaccessible
  id: ID!
}
""",
"""
<span class="hljs-keyword">type</span> User <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">internalId</span><span class="hljs-punctuation">:</span> ID <span class="hljs-meta">@inaccessible</span>
  <span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> ID<span class="hljs-punctuation">!</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void ScalarLiteral_IntArg()
    {
        AssertHighlighter("graphql",
"""
query { user(id: 42) { id } }
""",
"""
<span class="hljs-keyword">query</span> <span class="hljs-punctuation">{</span> user<span class="hljs-punctuation">(</span><span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> <span class="hljs-number">42</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span> id <span class="hljs-punctuation">}</span> <span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void ScalarLiteral_FloatArg()
    {
        AssertHighlighter("graphql",
"""
query { products(maxPrice: 9.99) { id } }
""",
"""
<span class="hljs-keyword">query</span> <span class="hljs-punctuation">{</span> products<span class="hljs-punctuation">(</span><span class="hljs-symbol">maxPrice</span><span class="hljs-punctuation">:</span> <span class="hljs-number">9.99</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span> id <span class="hljs-punctuation">}</span> <span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void ScalarLiteral_StringArg()
    {
        AssertHighlighter("graphql",
"""
query { search(query: "hello world") { id } }
""",
"""
<span class="hljs-keyword">query</span> <span class="hljs-punctuation">{</span> search<span class="hljs-punctuation">(</span><span class="hljs-symbol">query</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;hello world&quot;</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span> id <span class="hljs-punctuation">}</span> <span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void ScalarLiteral_BlockString()
    {
        AssertHighlighter("graphql",
""""
query {
  search(query: """
    multi-line
    block string
  """) { id }
}
"""",
""""
<span class="hljs-keyword">query</span> <span class="hljs-punctuation">{</span>
  search<span class="hljs-punctuation">(</span><span class="hljs-symbol">query</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;&quot;&quot;
    multi-line
    block string
  &quot;&quot;&quot;</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span> id <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
"""");
    }

    [Fact]
    public void ScalarLiteral_BooleanTrue()
    {
        AssertHighlighter("graphql",
"""
query { users(active: true) { id } }
""",
"""
<span class="hljs-keyword">query</span> <span class="hljs-punctuation">{</span> users<span class="hljs-punctuation">(</span><span class="hljs-symbol">active</span><span class="hljs-punctuation">:</span> <span class="hljs-literal">true</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span> id <span class="hljs-punctuation">}</span> <span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void ScalarLiteral_BooleanFalse()
    {
        AssertHighlighter("graphql",
"""
query { users(active: false) { id } }
""",
"""
<span class="hljs-keyword">query</span> <span class="hljs-punctuation">{</span> users<span class="hljs-punctuation">(</span><span class="hljs-symbol">active</span><span class="hljs-punctuation">:</span> <span class="hljs-literal">false</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span> id <span class="hljs-punctuation">}</span> <span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void ScalarLiteral_NullValue()
    {
        AssertHighlighter("graphql",
"""
mutation { updateUser(id: 1, email: null) { id } }
""",
"""
<span class="hljs-keyword">mutation</span> <span class="hljs-punctuation">{</span> updateUser<span class="hljs-punctuation">(</span><span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> <span class="hljs-number">1</span>, <span class="hljs-symbol">email</span><span class="hljs-punctuation">:</span> <span class="hljs-literal">null</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span> id <span class="hljs-punctuation">}</span> <span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void ScalarLiteral_EnumValue()
    {
        AssertHighlighter("graphql",
"""
query { posts(status: PUBLISHED) { id } }
""",
"""
<span class="hljs-keyword">query</span> <span class="hljs-punctuation">{</span> posts<span class="hljs-punctuation">(</span><span class="hljs-symbol">status</span><span class="hljs-punctuation">:</span> PUBLISHED<span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span> id <span class="hljs-punctuation">}</span> <span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void ScalarLiteral_ListEmpty()
    {
        AssertHighlighter("graphql",
"""
query { users(ids: []) { id } }
""",
"""
<span class="hljs-keyword">query</span> <span class="hljs-punctuation">{</span> users<span class="hljs-punctuation">(</span><span class="hljs-symbol">ids</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">[</span><span class="hljs-punctuation">]</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span> id <span class="hljs-punctuation">}</span> <span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void ScalarLiteral_ObjectLiteral()
    {
        AssertHighlighter("graphql",
"""
mutation { createPost(input: { title: "Hi", tags: ["news", "intro"] }) { id } }
""",
"""
<span class="hljs-keyword">mutation</span> <span class="hljs-punctuation">{</span> createPost<span class="hljs-punctuation">(</span><span class="hljs-symbol">input</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">{</span> <span class="hljs-symbol">title</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;Hi&quot;</span>, <span class="hljs-symbol">tags</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">[</span><span class="hljs-string">&quot;news&quot;</span>, <span class="hljs-string">&quot;intro&quot;</span><span class="hljs-punctuation">]</span> <span class="hljs-punctuation">}</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span> id <span class="hljs-punctuation">}</span> <span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Comment_Hash()
    {
        AssertHighlighter("graphql",
"""
# this is a comment
query { user { id } }
""",
"""
<span class="hljs-comment"># this is a comment</span>
<span class="hljs-keyword">query</span> <span class="hljs-punctuation">{</span> user <span class="hljs-punctuation">{</span> id <span class="hljs-punctuation">}</span> <span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Comment_DocstringField()
    {
        AssertHighlighter("graphql",
"""
type User {
  "The unique identifier"
  id: ID!
}
""",
"""
<span class="hljs-keyword">type</span> User <span class="hljs-punctuation">{</span>
  <span class="hljs-string">&quot;The unique identifier&quot;</span>
  <span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> ID<span class="hljs-punctuation">!</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Comment_DocstringBlock()
    {
        AssertHighlighter("graphql",
""""
type User {
  """
  The user's display name.
  Falls back to the email if unset.
  """
  name: String
}
"""",
""""
<span class="hljs-keyword">type</span> User <span class="hljs-punctuation">{</span>
  <span class="hljs-string">&quot;&quot;&quot;
  The user&#x27;s display name.
  Falls back to the email if unset.
  &quot;&quot;&quot;</span>
  <span class="hljs-symbol">name</span><span class="hljs-punctuation">:</span> String
<span class="hljs-punctuation">}</span>
"""");
    }

    [Fact]
    public void Composite_FullSchema()
    {
        AssertHighlighter("graphql",
"""
schema {
  query: Query
  mutation: Mutation
}

scalar DateTime

interface Node {
  id: ID!
}

enum Role {
  ADMIN
  MEMBER
}

type User implements Node {
  id: ID!
  name: String!
  email: String
  role: Role!
  createdAt: DateTime!
  posts: [Post!]!
}

type Post implements Node {
  id: ID!
  title: String!
  body: String!
  author: User!
}

input CreateUserInput {
  name: String!
  email: String
  role: Role = MEMBER
}

type Query {
  me: User
  user(id: ID!): User
  posts(authorId: ID, limit: Int = 10): [Post!]!
}

type Mutation {
  createUser(input: CreateUserInput!): User!
  deleteUser(id: ID!): Boolean!
}
""",
"""
<span class="hljs-keyword">schema</span> <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">query</span><span class="hljs-punctuation">:</span> <span class="hljs-keyword">Query</span>
  <span class="hljs-symbol">mutation</span><span class="hljs-punctuation">:</span> <span class="hljs-keyword">Mutation</span>
<span class="hljs-punctuation">}</span>

<span class="hljs-keyword">scalar</span> DateTime

<span class="hljs-keyword">interface</span> Node <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> ID<span class="hljs-punctuation">!</span>
<span class="hljs-punctuation">}</span>

<span class="hljs-keyword">enum</span> Role <span class="hljs-punctuation">{</span>
  ADMIN
  MEMBER
<span class="hljs-punctuation">}</span>

<span class="hljs-keyword">type</span> User implements Node <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> ID<span class="hljs-punctuation">!</span>
  <span class="hljs-symbol">name</span><span class="hljs-punctuation">:</span> String<span class="hljs-punctuation">!</span>
  <span class="hljs-symbol">email</span><span class="hljs-punctuation">:</span> String
  <span class="hljs-symbol">role</span><span class="hljs-punctuation">:</span> Role<span class="hljs-punctuation">!</span>
  <span class="hljs-symbol">createdAt</span><span class="hljs-punctuation">:</span> DateTime<span class="hljs-punctuation">!</span>
  <span class="hljs-symbol">posts</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">[</span>Post<span class="hljs-punctuation">!</span><span class="hljs-punctuation">]</span><span class="hljs-punctuation">!</span>
<span class="hljs-punctuation">}</span>

<span class="hljs-keyword">type</span> Post implements Node <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> ID<span class="hljs-punctuation">!</span>
  <span class="hljs-symbol">title</span><span class="hljs-punctuation">:</span> String<span class="hljs-punctuation">!</span>
  <span class="hljs-symbol">body</span><span class="hljs-punctuation">:</span> String<span class="hljs-punctuation">!</span>
  <span class="hljs-symbol">author</span><span class="hljs-punctuation">:</span> User<span class="hljs-punctuation">!</span>
<span class="hljs-punctuation">}</span>

<span class="hljs-keyword">input</span> CreateUserInput <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">name</span><span class="hljs-punctuation">:</span> String<span class="hljs-punctuation">!</span>
  <span class="hljs-symbol">email</span><span class="hljs-punctuation">:</span> String
  <span class="hljs-symbol">role</span><span class="hljs-punctuation">:</span> Role <span class="hljs-punctuation">=</span> MEMBER
<span class="hljs-punctuation">}</span>

<span class="hljs-keyword">type</span> <span class="hljs-keyword">Query</span> <span class="hljs-punctuation">{</span>
  <span class="hljs-symbol">me</span><span class="hljs-punctuation">:</span> User
  user<span class="hljs-punctuation">(</span><span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> ID<span class="hljs-punctuation">!</span><span class="hljs-punctuation">)</span><span class="hljs-punctuation">:</span> User
  posts<span class="hljs-punctuation">(</span><span class="hljs-symbol">authorId</span><span class="hljs-punctuation">:</span> ID, <span class="hljs-symbol">limit</span><span class="hljs-punctuation">:</span> Int <span class="hljs-punctuation">=</span> <span class="hljs-number">10</span><span class="hljs-punctuation">)</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">[</span>Post<span class="hljs-punctuation">!</span><span class="hljs-punctuation">]</span><span class="hljs-punctuation">!</span>
<span class="hljs-punctuation">}</span>

<span class="hljs-keyword">type</span> <span class="hljs-keyword">Mutation</span> <span class="hljs-punctuation">{</span>
  createUser<span class="hljs-punctuation">(</span><span class="hljs-symbol">input</span><span class="hljs-punctuation">:</span> CreateUserInput<span class="hljs-punctuation">!</span><span class="hljs-punctuation">)</span><span class="hljs-punctuation">:</span> User<span class="hljs-punctuation">!</span>
  deleteUser<span class="hljs-punctuation">(</span><span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> ID<span class="hljs-punctuation">!</span><span class="hljs-punctuation">)</span><span class="hljs-punctuation">:</span> Boolean<span class="hljs-punctuation">!</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Composite_PaginatedQuery()
    {
        AssertHighlighter("graphql",
"""
query SearchUsers($q: String!, $after: String, $first: Int = 20) {
  search(query: $q, after: $after, first: $first) {
    pageInfo {
      hasNextPage
      endCursor
    }
    edges {
      cursor
      node {
        ... on User {
          id
          name
          email
        }
      }
    }
  }
}
""",
"""
<span class="hljs-keyword">query</span> SearchUsers<span class="hljs-punctuation">(</span><span class="hljs-variable">$q</span>: String<span class="hljs-punctuation">!</span>, <span class="hljs-variable">$after</span>: String, <span class="hljs-variable">$first</span>: Int <span class="hljs-punctuation">=</span> <span class="hljs-number">20</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
  search<span class="hljs-punctuation">(</span><span class="hljs-symbol">query</span><span class="hljs-punctuation">:</span> <span class="hljs-variable">$q</span>, <span class="hljs-symbol">after</span><span class="hljs-punctuation">:</span> <span class="hljs-variable">$after</span>, <span class="hljs-symbol">first</span><span class="hljs-punctuation">:</span> <span class="hljs-variable">$first</span>) <span class="hljs-punctuation">{</span>
    pageInfo <span class="hljs-punctuation">{</span>
      hasNextPage
      endCursor
    <span class="hljs-punctuation">}</span>
    edges <span class="hljs-punctuation">{</span>
      cursor
      node <span class="hljs-punctuation">{</span>
        <span class="hljs-punctuation">...</span> <span class="hljs-keyword">on</span> User <span class="hljs-punctuation">{</span>
          id
          name
          email
        <span class="hljs-punctuation">}</span>
      <span class="hljs-punctuation">}</span>
    <span class="hljs-punctuation">}</span>
  <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Composite_OperationWithFragments()
    {
        AssertHighlighter("graphql",
"""
query GetDashboard($userId: ID!) {
  user(id: $userId) {
    ...UserCard
    posts(limit: 5) {
      ...PostSummary
    }
  }
}

fragment UserCard on User {
  id
  name
  avatar
}

fragment PostSummary on Post {
  id
  title
  excerpt
  author {
    ...UserCard
  }
}
""",
"""
<span class="hljs-keyword">query</span> GetDashboard<span class="hljs-punctuation">(</span><span class="hljs-variable">$userId</span>: ID<span class="hljs-punctuation">!</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
  user<span class="hljs-punctuation">(</span><span class="hljs-symbol">id</span><span class="hljs-punctuation">:</span> <span class="hljs-variable">$userId</span>) <span class="hljs-punctuation">{</span>
    <span class="hljs-punctuation">...</span>UserCard
    posts<span class="hljs-punctuation">(</span><span class="hljs-symbol">limit</span><span class="hljs-punctuation">:</span> <span class="hljs-number">5</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">{</span>
      <span class="hljs-punctuation">...</span>PostSummary
    <span class="hljs-punctuation">}</span>
  <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>

<span class="hljs-keyword">fragment</span> UserCard <span class="hljs-keyword">on</span> User <span class="hljs-punctuation">{</span>
  id
  name
  avatar
<span class="hljs-punctuation">}</span>

<span class="hljs-keyword">fragment</span> PostSummary <span class="hljs-keyword">on</span> Post <span class="hljs-punctuation">{</span>
  id
  title
  excerpt
  author <span class="hljs-punctuation">{</span>
    <span class="hljs-punctuation">...</span>UserCard
  <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("graphql",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("graphql",
"""
# just a comment
""",
"""
<span class="hljs-comment"># just a comment</span>
""");
    }

    [Fact]
    public void SpecialEdge_TrailingNewline()
    {
        AssertHighlighter("graphql",
"""
query { user { id } }

""",
"""
<span class="hljs-keyword">query</span> <span class="hljs-punctuation">{</span> user <span class="hljs-punctuation">{</span> id <span class="hljs-punctuation">}</span> <span class="hljs-punctuation">}</span>

""");
    }
}
