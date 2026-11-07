namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class ProtobufHighlighterTests
{
    [Fact]
    public void Proto3Header()
    {
        AssertHighlighter("protobuf",
"""
syntax = "proto3";

package example.v1;

import "google/protobuf/timestamp.proto";
import public "other.proto";
import weak "legacy.proto";

option go_package = "github.com/example/api/v1;apiv1";
option java_multiple_files = true;
option optimize_for = SPEED;
""",
"""
<span class="hljs-keyword">syntax</span> = <span class="hljs-string">&quot;proto3&quot;</span>;

<span class="hljs-keyword">package</span> example.v1;

<span class="hljs-keyword">import</span> <span class="hljs-string">&quot;google/protobuf/timestamp.proto&quot;</span>;
<span class="hljs-keyword">import</span> <span class="hljs-keyword">public</span> <span class="hljs-string">&quot;other.proto&quot;</span>;
<span class="hljs-keyword">import</span> <span class="hljs-keyword">weak</span> <span class="hljs-string">&quot;legacy.proto&quot;</span>;

<span class="hljs-keyword">option</span> go_package = <span class="hljs-string">&quot;github.com/example/api/v1;apiv1&quot;</span>;
<span class="hljs-keyword">option</span> java_multiple_files = <span class="hljs-literal">true</span>;
<span class="hljs-keyword">option</span> optimize_for = SPEED;
""");
    }

    [Fact]
    public void Proto2Header()
    {
        AssertHighlighter("protobuf",
"""
syntax = "proto2";
package tutorial;
""",
"""
<span class="hljs-keyword">syntax</span> = <span class="hljs-string">&quot;proto2&quot;</span>;
<span class="hljs-keyword">package</span> tutorial;
""");
    }

    [Fact]
    public void Edition()
    {
        AssertHighlighter("protobuf",
"""
edition = "2023";

package com.example;

option features.field_presence = EXPLICIT;

message Foo {
  int32 x = 1 [features.field_presence = IMPLICIT];
}
""",
"""
<span class="hljs-keyword">edition</span> = <span class="hljs-string">&quot;2023&quot;</span>;

<span class="hljs-keyword">package</span> com.example;

<span class="hljs-keyword">option</span> features.field_presence = EXPLICIT;

<span class="hljs-keyword">message </span><span class="hljs-title class_">Foo</span> {
  <span class="hljs-type">int32</span> x = <span class="hljs-number">1</span> [features.field_presence = IMPLICIT];
}
""");
    }

    [Fact]
    public void EditionVisibility()
    {
        AssertHighlighter("protobuf",
"""
edition = "2024";

export message Public {}
local enum Private { PRIVATE_UNSPECIFIED = 0; }
message M { string export = 1; bool local = 2; }
""",
"""
<span class="hljs-keyword">edition</span> = <span class="hljs-string">&quot;2024&quot;</span>;

<span class="hljs-keyword">export</span> <span class="hljs-keyword">message </span><span class="hljs-title class_">Public</span> {}
<span class="hljs-keyword">local</span> <span class="hljs-keyword">enum </span><span class="hljs-title class_">Private</span> { PRIVATE_UNSPECIFIED = <span class="hljs-number">0</span>; }
<span class="hljs-keyword">message </span><span class="hljs-title class_">M</span> { <span class="hljs-type">string</span> export = <span class="hljs-number">1</span>; <span class="hljs-type">bool</span> local = <span class="hljs-number">2</span>; }
""");
    }

    [Fact]
    public void Message()
    {
        AssertHighlighter("protobuf",
"""
message Person {
  string name = 1;
  int32 id = 2;
  string email = 3;
  repeated string phones = 4;
  bool active = 5;
  bytes data = 6;
  double score = 7;
  float ratio = 8;
}
""",
"""
<span class="hljs-keyword">message </span><span class="hljs-title class_">Person</span> {
  <span class="hljs-type">string</span> name = <span class="hljs-number">1</span>;
  <span class="hljs-type">int32</span> id = <span class="hljs-number">2</span>;
  <span class="hljs-type">string</span> email = <span class="hljs-number">3</span>;
  <span class="hljs-keyword">repeated</span> <span class="hljs-type">string</span> phones = <span class="hljs-number">4</span>;
  <span class="hljs-type">bool</span> active = <span class="hljs-number">5</span>;
  <span class="hljs-type">bytes</span> data = <span class="hljs-number">6</span>;
  <span class="hljs-type">double</span> score = <span class="hljs-number">7</span>;
  <span class="hljs-type">float</span> ratio = <span class="hljs-number">8</span>;
}
""");
    }

    [Fact]
    public void ScalarTypes()
    {
        AssertHighlighter("protobuf",
"""
message Scalars {
  int64 a = 1;
  uint32 b = 2;
  uint64 c = 3;
  sint32 d = 4;
  sint64 e = 5;
  fixed32 f = 6;
  fixed64 g = 7;
  sfixed32 h = 8;
  sfixed64 i = 9;
}
""",
"""
<span class="hljs-keyword">message </span><span class="hljs-title class_">Scalars</span> {
  <span class="hljs-type">int64</span> a = <span class="hljs-number">1</span>;
  <span class="hljs-type">uint32</span> b = <span class="hljs-number">2</span>;
  <span class="hljs-type">uint64</span> c = <span class="hljs-number">3</span>;
  <span class="hljs-type">sint32</span> d = <span class="hljs-number">4</span>;
  <span class="hljs-type">sint64</span> e = <span class="hljs-number">5</span>;
  <span class="hljs-type">fixed32</span> f = <span class="hljs-number">6</span>;
  <span class="hljs-type">fixed64</span> g = <span class="hljs-number">7</span>;
  <span class="hljs-type">sfixed32</span> h = <span class="hljs-number">8</span>;
  <span class="hljs-type">sfixed64</span> i = <span class="hljs-number">9</span>;
}
""");
    }

    [Fact]
    public void Proto2Labels()
    {
        AssertHighlighter("protobuf",
"""
message SearchRequest {
  required string query = 1;
  optional int32 page_number = 2 [default = 1];
  optional int32 result_per_page = 3 [default = 10];
  repeated group Result = 4 {
    required string url = 5;
  }
}
""",
"""
<span class="hljs-keyword">message </span><span class="hljs-title class_">SearchRequest</span> {
  <span class="hljs-keyword">required</span> <span class="hljs-type">string</span> query = <span class="hljs-number">1</span>;
  <span class="hljs-keyword">optional</span> <span class="hljs-type">int32</span> page_number = <span class="hljs-number">2</span> [default = <span class="hljs-number">1</span>];
  <span class="hljs-keyword">optional</span> <span class="hljs-type">int32</span> result_per_page = <span class="hljs-number">3</span> [default = <span class="hljs-number">10</span>];
  <span class="hljs-keyword">repeated</span> <span class="hljs-keyword">group</span> Result = <span class="hljs-number">4</span> {
    <span class="hljs-keyword">required</span> <span class="hljs-type">string</span> url = <span class="hljs-number">5</span>;
  }
}
""");
    }

    [Fact]
    public void Enum()
    {
        AssertHighlighter("protobuf",
"""
enum Corpus {
  CORPUS_UNSPECIFIED = 0;
  CORPUS_UNIVERSAL = 1;
  CORPUS_WEB = 2;
}
""",
"""
<span class="hljs-keyword">enum </span><span class="hljs-title class_">Corpus</span> {
  CORPUS_UNSPECIFIED = <span class="hljs-number">0</span>;
  CORPUS_UNIVERSAL = <span class="hljs-number">1</span>;
  CORPUS_WEB = <span class="hljs-number">2</span>;
}
""");
    }

    [Fact]
    public void EnumWithOptions()
    {
        AssertHighlighter("protobuf",
"""
enum EnumAllowingAlias {
  option allow_alias = true;
  EAA_UNSPECIFIED = 0;
  EAA_STARTED = 1;
  EAA_RUNNING = 1; // alias
  EAA_FINISHED = 2 [deprecated = true];
}
""",
"""
<span class="hljs-keyword">enum </span><span class="hljs-title class_">EnumAllowingAlias</span> {
  <span class="hljs-keyword">option</span> allow_alias = <span class="hljs-literal">true</span>;
  EAA_UNSPECIFIED = <span class="hljs-number">0</span>;
  EAA_STARTED = <span class="hljs-number">1</span>;
  EAA_RUNNING = <span class="hljs-number">1</span>; <span class="hljs-comment">// alias</span>
  EAA_FINISHED = <span class="hljs-number">2</span> [deprecated = <span class="hljs-literal">true</span>];
}
""");
    }

    [Fact]
    public void NestedTypes()
    {
        AssertHighlighter("protobuf",
"""
message Outer {
  message Inner {
    int64 ival = 1;
  }
  enum Kind {
    KIND_UNKNOWN = 0;
  }
  Inner inner = 1;
  Kind kind = 2;
  Outer.Inner other = 3;
}
""",
"""
<span class="hljs-keyword">message </span><span class="hljs-title class_">Outer</span> {
  <span class="hljs-keyword">message </span><span class="hljs-title class_">Inner</span> {
    <span class="hljs-type">int64</span> ival = <span class="hljs-number">1</span>;
  }
  <span class="hljs-keyword">enum </span><span class="hljs-title class_">Kind</span> {
    KIND_UNKNOWN = <span class="hljs-number">0</span>;
  }
  Inner inner = <span class="hljs-number">1</span>;
  Kind kind = <span class="hljs-number">2</span>;
  Outer.Inner other = <span class="hljs-number">3</span>;
}
""");
    }

    [Fact]
    public void Service()
    {
        AssertHighlighter("protobuf",
"""
service Greeter {
  rpc SayHello (HelloRequest) returns (HelloReply) {}
  rpc SayHelloAgain(HelloRequest) returns (HelloReply);
  rpc Stream (stream Req) returns (stream Resp);
  rpc Get(GetRequest) returns (google.protobuf.Empty) {
    option (google.api.http) = { get: "/v1/{name=items/*}" };
  }
}
""",
"""
<span class="hljs-keyword">service </span><span class="hljs-title class_">Greeter</span> {
  <span class="hljs-function"><span class="hljs-keyword">rpc</span> <span class="hljs-title function_">SayHello</span> (HelloRequest) <span class="hljs-keyword">returns</span> (HelloReply) </span>{}
  <span class="hljs-function"><span class="hljs-keyword">rpc</span> <span class="hljs-title function_">SayHelloAgain</span>(HelloRequest) <span class="hljs-keyword">returns</span> (HelloReply)</span>;
  <span class="hljs-function"><span class="hljs-keyword">rpc</span> <span class="hljs-title function_">Stream</span> (<span class="hljs-keyword">stream</span> Req) <span class="hljs-keyword">returns</span> (<span class="hljs-keyword">stream</span> Resp)</span>;
  <span class="hljs-function"><span class="hljs-keyword">rpc</span> <span class="hljs-title function_">Get</span>(GetRequest) <span class="hljs-keyword">returns</span> (google.protobuf.Empty) </span>{
    <span class="hljs-keyword">option</span> (google.api.http) = { get: <span class="hljs-string">&quot;/v1/{name=items/*}&quot;</span> };
  }
}
""");
    }

    [Fact]
    public void RpcWithOptions()
    {
        AssertHighlighter("protobuf",
"""
service S {
  rpc List(ListRequest) returns (ListResponse) {
    option deprecated = true;
    option idempotency_level = NO_SIDE_EFFECTS;
  }
}
""",
"""
<span class="hljs-keyword">service </span><span class="hljs-title class_">S</span> {
  <span class="hljs-function"><span class="hljs-keyword">rpc</span> <span class="hljs-title function_">List</span>(ListRequest) <span class="hljs-keyword">returns</span> (ListResponse) </span>{
    <span class="hljs-keyword">option</span> deprecated = <span class="hljs-literal">true</span>;
    <span class="hljs-keyword">option</span> idempotency_level = NO_SIDE_EFFECTS;
  }
}
""");
    }

    [Fact]
    public void Oneof()
    {
        AssertHighlighter("protobuf",
"""
message SampleMessage {
  oneof test_oneof {
    string name = 4;
    SubMessage sub_message = 9;
  }
}
""",
"""
<span class="hljs-keyword">message </span><span class="hljs-title class_">SampleMessage</span> {
  <span class="hljs-keyword">oneof</span> test_oneof {
    <span class="hljs-type">string</span> name = <span class="hljs-number">4</span>;
    SubMessage sub_message = <span class="hljs-number">9</span>;
  }
}
""");
    }

    [Fact]
    public void Map()
    {
        AssertHighlighter("protobuf",
"""
message MapMessage {
  map<string, Project> projects = 3;
  map<int32, string> names = 4;
}
""",
"""
<span class="hljs-keyword">message </span><span class="hljs-title class_">MapMessage</span> {
  <span class="hljs-keyword">map</span>&lt;<span class="hljs-type">string</span>, Project&gt; projects = <span class="hljs-number">3</span>;
  <span class="hljs-keyword">map</span>&lt;<span class="hljs-type">int32</span>, <span class="hljs-type">string</span>&gt; names = <span class="hljs-number">4</span>;
}
""");
    }

    [Fact]
    public void Reserved()
    {
        AssertHighlighter("protobuf",
"""
message Foo {
  reserved 2, 15, 9 to 11, 40 to max;
  reserved "foo", "bar";
  reserved baz;
}
""",
"""
<span class="hljs-keyword">message </span><span class="hljs-title class_">Foo</span> {
  <span class="hljs-keyword">reserved</span> <span class="hljs-number">2</span>, <span class="hljs-number">15</span>, <span class="hljs-number">9</span> <span class="hljs-keyword">to</span> <span class="hljs-number">11</span>, <span class="hljs-number">40</span> <span class="hljs-keyword">to</span> <span class="hljs-keyword">max</span>;
  <span class="hljs-keyword">reserved</span> <span class="hljs-string">&quot;foo&quot;</span>, <span class="hljs-string">&quot;bar&quot;</span>;
  <span class="hljs-keyword">reserved</span> baz;
}
""");
    }

    [Fact]
    public void ReservedContexts()
    {
        AssertHighlighter("protobuf",
"""
message Foo { reserved 1; }
message Bar {
  // comment
  reserved 2 to 5; // trailing
  bool reserved = 3;
  string extensions = 4;
  int32 extend = 5;
}
""",
"""
<span class="hljs-keyword">message </span><span class="hljs-title class_">Foo</span> { <span class="hljs-keyword">reserved</span> <span class="hljs-number">1</span>; }
<span class="hljs-keyword">message </span><span class="hljs-title class_">Bar</span> {
  <span class="hljs-comment">// comment</span>
  <span class="hljs-keyword">reserved</span> <span class="hljs-number">2</span> <span class="hljs-keyword">to</span> <span class="hljs-number">5</span>; <span class="hljs-comment">// trailing</span>
  <span class="hljs-type">bool</span> reserved = <span class="hljs-number">3</span>;
  <span class="hljs-type">string</span> extensions = <span class="hljs-number">4</span>;
  <span class="hljs-type">int32</span> extend = <span class="hljs-number">5</span>;
}
""");
    }

    [Fact]
    public void Extensions()
    {
        AssertHighlighter("protobuf",
"""
message Foo {
  extensions 100 to 199;
  extensions 1000 to max [verification = UNVERIFIED];
}

extend Foo {
  optional int32 bar = 126;
}
""",
"""
<span class="hljs-keyword">message </span><span class="hljs-title class_">Foo</span> {
  <span class="hljs-keyword">extensions</span> <span class="hljs-number">100</span> <span class="hljs-keyword">to</span> <span class="hljs-number">199</span>;
  <span class="hljs-keyword">extensions</span> <span class="hljs-number">1000</span> <span class="hljs-keyword">to</span> <span class="hljs-keyword">max</span> [verification = UNVERIFIED];
}

<span class="hljs-keyword">extend</span> Foo {
  <span class="hljs-keyword">optional</span> <span class="hljs-type">int32</span> bar = <span class="hljs-number">126</span>;
}
""");
    }

    [Fact]
    public void FieldOptions()
    {
        AssertHighlighter("protobuf",
"""
message Foo {
  int32 old_field = 6 [deprecated = true];
  string json = 7 [json_name = "jsonName", (my_option) = "x"];
  repeated int32 packed = 8 [packed = true];
  optional float f = 9 [default = 1.5];
  optional int32 neg = 10 [default = -1];
  optional uint32 hex = 11 [default = 0x1F];
}
""",
"""
<span class="hljs-keyword">message </span><span class="hljs-title class_">Foo</span> {
  <span class="hljs-type">int32</span> old_field = <span class="hljs-number">6</span> [deprecated = <span class="hljs-literal">true</span>];
  <span class="hljs-type">string</span> json = <span class="hljs-number">7</span> [json_name = <span class="hljs-string">&quot;jsonName&quot;</span>, (my_option) = <span class="hljs-string">&quot;x&quot;</span>];
  <span class="hljs-keyword">repeated</span> <span class="hljs-type">int32</span> packed = <span class="hljs-number">8</span> [packed = <span class="hljs-literal">true</span>];
  <span class="hljs-keyword">optional</span> <span class="hljs-type">float</span> f = <span class="hljs-number">9</span> [default = <span class="hljs-number">1.5</span>];
  <span class="hljs-keyword">optional</span> <span class="hljs-type">int32</span> neg = <span class="hljs-number">10</span> [default = <span class="hljs-number">-1</span>];
  <span class="hljs-keyword">optional</span> <span class="hljs-type">uint32</span> hex = <span class="hljs-number">11</span> [default = <span class="hljs-number">0x1F</span>];
}
""");
    }

    [Fact]
    public void CustomOptions()
    {
        AssertHighlighter("protobuf",
"""
import "google/protobuf/descriptor.proto";

extend google.protobuf.MessageOptions {
  optional string my_option = 51234;
}

message MyMessage {
  option (my_option) = "Hello world!";
}
""",
"""
<span class="hljs-keyword">import</span> <span class="hljs-string">&quot;google/protobuf/descriptor.proto&quot;</span>;

<span class="hljs-keyword">extend</span> google.protobuf.MessageOptions {
  <span class="hljs-keyword">optional</span> <span class="hljs-type">string</span> my_option = <span class="hljs-number">51234</span>;
}

<span class="hljs-keyword">message </span><span class="hljs-title class_">MyMessage</span> {
  <span class="hljs-keyword">option</span> (my_option) = <span class="hljs-string">&quot;Hello world!&quot;</span>;
}
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("protobuf",
"""
// Line comment
/* Block
 * comment
 */
message A { // trailing
  int32 x = 1; /* inline */
}
// TODO: something
""",
"""
<span class="hljs-comment">// Line comment</span>
<span class="hljs-comment">/* Block
 * comment
 */</span>
<span class="hljs-keyword">message </span><span class="hljs-title class_">A</span> { <span class="hljs-comment">// trailing</span>
  <span class="hljs-type">int32</span> x = <span class="hljs-number">1</span>; <span class="hljs-comment">/* inline */</span>
}
<span class="hljs-comment">// <span class="hljs-doctag">TODO:</span> something</span>
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("protobuf",
"""
option java_package = "com.example.\"quoted\"";
option a = 'single';
""",
"""
<span class="hljs-keyword">option</span> java_package = <span class="hljs-string">&quot;com.example.\&quot;quoted\&quot;&quot;</span>;
<span class="hljs-keyword">option</span> a = <span class="hljs-string">&#x27;single&#x27;</span>;
""");
    }

    [Fact]
    public void Proto3Optional()
    {
        AssertHighlighter("protobuf",
"""
message Foo {
  optional string name = 1;
}
""",
"""
<span class="hljs-keyword">message </span><span class="hljs-title class_">Foo</span> {
  <span class="hljs-keyword">optional</span> <span class="hljs-type">string</span> name = <span class="hljs-number">1</span>;
}
""");
    }

    [Fact]
    public void WellKnownTypes()
    {
        AssertHighlighter("protobuf",
"""
import "google/protobuf/any.proto";
message Event {
  google.protobuf.Timestamp created_at = 1;
  google.protobuf.Any payload = 2;
  google.protobuf.Duration ttl = 3;
}
""",
"""
<span class="hljs-keyword">import</span> <span class="hljs-string">&quot;google/protobuf/any.proto&quot;</span>;
<span class="hljs-keyword">message </span><span class="hljs-title class_">Event</span> {
  google.protobuf.Timestamp created_at = <span class="hljs-number">1</span>;
  google.protobuf.Any payload = <span class="hljs-number">2</span>;
  google.protobuf.Duration ttl = <span class="hljs-number">3</span>;
}
""");
    }

    [Fact]
    public void FieldNamesLikeContextualKeywords()
    {
        AssertHighlighter("protobuf",
"""
message Mail {
  string to = 1;
  int32 max = 2;
  bool public = 3;
  string syntax = 4;
  string stream = 5;
  string map = 6;
  string last_message = 7;
  some_service service_list = 8;
}
""",
"""
<span class="hljs-keyword">message </span><span class="hljs-title class_">Mail</span> {
  <span class="hljs-type">string</span> to = <span class="hljs-number">1</span>;
  <span class="hljs-type">int32</span> max = <span class="hljs-number">2</span>;
  <span class="hljs-type">bool</span> public = <span class="hljs-number">3</span>;
  <span class="hljs-type">string</span> syntax = <span class="hljs-number">4</span>;
  <span class="hljs-type">string</span> stream = <span class="hljs-number">5</span>;
  <span class="hljs-type">string</span> map = <span class="hljs-number">6</span>;
  <span class="hljs-type">string</span> last_message = <span class="hljs-number">7</span>;
  some_service service_list = <span class="hljs-number">8</span>;
}
""");
    }

    [Fact]
    public void ImportModifiers()
    {
        AssertHighlighter("protobuf",
"""
import "a.proto";
import  public  "b.proto";
message M { bool weak = 1; bool public = 2; }
""",
"""
<span class="hljs-keyword">import</span> <span class="hljs-string">&quot;a.proto&quot;</span>;
<span class="hljs-keyword">import</span>  <span class="hljs-keyword">public</span>  <span class="hljs-string">&quot;b.proto&quot;</span>;
<span class="hljs-keyword">message </span><span class="hljs-title class_">M</span> { <span class="hljs-type">bool</span> weak = <span class="hljs-number">1</span>; <span class="hljs-type">bool</span> public = <span class="hljs-number">2</span>; }
""");
    }

    [Fact]
    public void EnumOnOneLine()
    {
        AssertHighlighter("protobuf",
"""
enum E { A = 0; B = 1; }
""",
"""
<span class="hljs-keyword">enum </span><span class="hljs-title class_">E</span> { A = <span class="hljs-number">0</span>; B = <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void MessageNamesWithDigits()
    {
        AssertHighlighter("protobuf",
"""
message V2Request {}
message Empty {}
""",
"""
<span class="hljs-keyword">message </span><span class="hljs-title class_">V2Request</span> {}
<span class="hljs-keyword">message </span><span class="hljs-title class_">Empty</span> {}
""");
    }

    [Fact]
    public void UppercaseTypeIsNotAnEnumValue()
    {
        AssertHighlighter("protobuf",
"""
message M {
  FOO bar = 1;
}
""",
"""
<span class="hljs-keyword">message </span><span class="hljs-title class_">M</span> {
  FOO bar = <span class="hljs-number">1</span>;
}
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("protobuf",
"""
message N {
  optional double d = 1 [default = 1e10];
  optional double e = 2 [default = inf];
  optional double f = 3 [default = nan];
  optional int64 big = 4 [default = 9223372036854775807];
}
""",
"""
<span class="hljs-keyword">message </span><span class="hljs-title class_">N</span> {
  <span class="hljs-keyword">optional</span> <span class="hljs-type">double</span> d = <span class="hljs-number">1</span> [default = <span class="hljs-number">1e10</span>];
  <span class="hljs-keyword">optional</span> <span class="hljs-type">double</span> e = <span class="hljs-number">2</span> [default = inf];
  <span class="hljs-keyword">optional</span> <span class="hljs-type">double</span> f = <span class="hljs-number">3</span> [default = nan];
  <span class="hljs-keyword">optional</span> <span class="hljs-type">int64</span> big = <span class="hljs-number">4</span> [default = <span class="hljs-number">9223372036854775807</span>];
}
""");
    }

    [Fact]
    public void RealisticFile()
    {
        AssertHighlighter("protobuf",
"""
syntax = "proto3";

package acme.inventory.v1;

import "google/protobuf/field_mask.proto";
import "google/protobuf/timestamp.proto";

option csharp_namespace = "Acme.Inventory.V1";

// Manages the items of a warehouse.
service InventoryService {
  // Gets an item by its id.
  rpc GetItem(GetItemRequest) returns (Item);
  rpc ListItems(ListItemsRequest) returns (ListItemsResponse);
  rpc UpdateItem(UpdateItemRequest) returns (Item);
  rpc WatchItems(WatchItemsRequest) returns (stream ItemEvent);
}

message Item {
  string id = 1;
  string name = 2;
  int32 quantity = 3;
  map<string, string> labels = 4;
  google.protobuf.Timestamp update_time = 5;

  enum State {
    STATE_UNSPECIFIED = 0;
    STATE_ACTIVE = 1;
    STATE_ARCHIVED = 2;
  }
  State state = 6;

  reserved 7, 8;
  reserved "sku";
}

message UpdateItemRequest {
  Item item = 1;
  google.protobuf.FieldMask update_mask = 2;
}
""",
"""
<span class="hljs-keyword">syntax</span> = <span class="hljs-string">&quot;proto3&quot;</span>;

<span class="hljs-keyword">package</span> acme.inventory.v1;

<span class="hljs-keyword">import</span> <span class="hljs-string">&quot;google/protobuf/field_mask.proto&quot;</span>;
<span class="hljs-keyword">import</span> <span class="hljs-string">&quot;google/protobuf/timestamp.proto&quot;</span>;

<span class="hljs-keyword">option</span> csharp_namespace = <span class="hljs-string">&quot;Acme.Inventory.V1&quot;</span>;

<span class="hljs-comment">// Manages the items of a warehouse.</span>
<span class="hljs-keyword">service </span><span class="hljs-title class_">InventoryService</span> {
  <span class="hljs-comment">// Gets an item by its id.</span>
  <span class="hljs-function"><span class="hljs-keyword">rpc</span> <span class="hljs-title function_">GetItem</span>(GetItemRequest) <span class="hljs-keyword">returns</span> (Item)</span>;
  <span class="hljs-function"><span class="hljs-keyword">rpc</span> <span class="hljs-title function_">ListItems</span>(ListItemsRequest) <span class="hljs-keyword">returns</span> (ListItemsResponse)</span>;
  <span class="hljs-function"><span class="hljs-keyword">rpc</span> <span class="hljs-title function_">UpdateItem</span>(UpdateItemRequest) <span class="hljs-keyword">returns</span> (Item)</span>;
  <span class="hljs-function"><span class="hljs-keyword">rpc</span> <span class="hljs-title function_">WatchItems</span>(WatchItemsRequest) <span class="hljs-keyword">returns</span> (<span class="hljs-keyword">stream</span> ItemEvent)</span>;
}

<span class="hljs-keyword">message </span><span class="hljs-title class_">Item</span> {
  <span class="hljs-type">string</span> id = <span class="hljs-number">1</span>;
  <span class="hljs-type">string</span> name = <span class="hljs-number">2</span>;
  <span class="hljs-type">int32</span> quantity = <span class="hljs-number">3</span>;
  <span class="hljs-keyword">map</span>&lt;<span class="hljs-type">string</span>, <span class="hljs-type">string</span>&gt; labels = <span class="hljs-number">4</span>;
  google.protobuf.Timestamp update_time = <span class="hljs-number">5</span>;

  <span class="hljs-keyword">enum </span><span class="hljs-title class_">State</span> {
    STATE_UNSPECIFIED = <span class="hljs-number">0</span>;
    STATE_ACTIVE = <span class="hljs-number">1</span>;
    STATE_ARCHIVED = <span class="hljs-number">2</span>;
  }
  State state = <span class="hljs-number">6</span>;

  <span class="hljs-keyword">reserved</span> <span class="hljs-number">7</span>, <span class="hljs-number">8</span>;
  <span class="hljs-keyword">reserved</span> <span class="hljs-string">&quot;sku&quot;</span>;
}

<span class="hljs-keyword">message </span><span class="hljs-title class_">UpdateItemRequest</span> {
  Item item = <span class="hljs-number">1</span>;
  google.protobuf.FieldMask update_mask = <span class="hljs-number">2</span>;
}
""");
    }
}
