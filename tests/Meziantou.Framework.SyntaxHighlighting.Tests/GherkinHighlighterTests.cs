namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class GherkinHighlighterTests
{
    [Fact]
    public void Feature()
    {
        AssertHighlighter("gherkin",
"""
Feature: Guess the word
  As a player
  I want to guess words
  So that I can have fun

  Scenario: Maker starts a game
    When the Maker starts a game
    Then the Maker waits for a Breaker to join
""",
"""
<span class="hljs-keyword">Feature</span>: Guess the word
  As a player
  I want to guess words
  So that I can have fun

  <span class="hljs-keyword">Scenario</span>: Maker starts a game
    <span class="hljs-keyword">When</span> the Maker starts a game
    <span class="hljs-keyword">Then</span> the Maker waits for a Breaker to join
""");
    }

    [Fact]
    public void Background()
    {
        AssertHighlighter("gherkin",
"""
Feature: Multiple site support

  Background:
    Given a global administrator named "Greg"
    And a blog named "Greg's anti-tax rants"
    And a customer named "Dr. Bill"

  Scenario: Dr. Bill posts to his own blog
    Given I am logged in as Dr. Bill
    When I try to post to "Expensive Therapy"
    Then I should see "Your article was published."
    But I should not see "Error"
""",
"""
<span class="hljs-keyword">Feature</span>: Multiple site support

  <span class="hljs-keyword">Background</span>:
    <span class="hljs-keyword">Given</span> a global administrator named <span class="hljs-string">&quot;Greg&quot;</span>
    <span class="hljs-keyword">And</span> a blog named <span class="hljs-string">&quot;Greg&#x27;s anti-tax rants&quot;</span>
    <span class="hljs-keyword">And</span> a customer named <span class="hljs-string">&quot;Dr. Bill&quot;</span>

  <span class="hljs-keyword">Scenario</span>: Dr. Bill posts to his own blog
    <span class="hljs-keyword">Given</span> I am logged in as Dr. Bill
    <span class="hljs-keyword">When</span> I try to post to <span class="hljs-string">&quot;Expensive Therapy&quot;</span>
    <span class="hljs-keyword">Then</span> I should see <span class="hljs-string">&quot;Your article was published.&quot;</span>
    <span class="hljs-keyword">But</span> I should not see <span class="hljs-string">&quot;Error&quot;</span>
""");
    }

    // Multi-word keywords are highlighted word by word.
    [Fact]
    public void ScenarioOutline()
    {
        AssertHighlighter("gherkin",
"""
Scenario Outline: eating
  Given there are <start> cucumbers
  When I eat <eat> cucumbers
  Then I should have <left> cucumbers

  Examples:
    | start | eat | left |
    |    12 |   5 |    7 |
    |    20 |   5 |   15 |
""",
"""
<span class="hljs-keyword">Scenario</span> <span class="hljs-keyword">Outline</span>: eating
  <span class="hljs-keyword">Given</span> there are <span class="hljs-variable">&lt;start&gt;</span> cucumbers
  <span class="hljs-keyword">When</span> I eat <span class="hljs-variable">&lt;eat&gt;</span> cucumbers
  <span class="hljs-keyword">Then</span> I should have <span class="hljs-variable">&lt;left&gt;</span> cucumbers

  <span class="hljs-keyword">Examples</span>:
    |<span class="hljs-string"> start </span>|<span class="hljs-string"> eat </span>|<span class="hljs-string"> left </span>|
    |<span class="hljs-string">    12 </span>|<span class="hljs-string">   5 </span>|<span class="hljs-string">    7 </span>|
    |<span class="hljs-string">    20 </span>|<span class="hljs-string">   5 </span>|<span class="hljs-string">   15 </span>|
""");
    }

    [Fact]
    public void ScenarioTemplateAndScenarios()
    {
        AssertHighlighter("gherkin",
"""
Scenario Template: templated
  Given <a>
Scenarios:
  | a |
  | x |
Ability: something
Business Need: something else
""",
"""
<span class="hljs-keyword">Scenario</span> <span class="hljs-keyword">Template</span>: templated
  <span class="hljs-keyword">Given</span> <span class="hljs-variable">&lt;a&gt;</span>
<span class="hljs-keyword">Scenarios</span>:
  |<span class="hljs-string"> a </span>|
  |<span class="hljs-string"> x </span>|
<span class="hljs-keyword">Ability</span>: something
<span class="hljs-keyword">Business</span> <span class="hljs-keyword">Need</span>: something else
""");
    }

    // As in highlight.js, an `@` starts a tag anywhere.
    [Fact]
    public void Tags()
    {
        AssertHighlighter("gherkin",
"""
@billing @important
Feature: Billing

  @slow@fast @wip:1 @issue-42
  Scenario: Pay
    Given email@example.com
""",
"""
<span class="hljs-meta">@billing</span> <span class="hljs-meta">@important</span>
<span class="hljs-keyword">Feature</span>: Billing

  <span class="hljs-meta">@slow</span><span class="hljs-meta">@fast</span> <span class="hljs-meta">@wip:1</span> <span class="hljs-meta">@issue-42</span>
  <span class="hljs-keyword">Scenario</span>: Pay
    <span class="hljs-keyword">Given</span> email<span class="hljs-meta">@example.com</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("gherkin",
"""
# language: en
Feature: Commented
  # TODO: write more scenarios
  Scenario: x # not a comment start?
    Given a step
""",
"""
<span class="hljs-comment"># language: en</span>
<span class="hljs-keyword">Feature</span>: Commented
  <span class="hljs-comment"># <span class="hljs-doctag">TODO:</span> write more scenarios</span>
  <span class="hljs-keyword">Scenario</span>: x <span class="hljs-comment"># not a comment start?</span>
    <span class="hljs-keyword">Given</span> a step
""");
    }

    [Fact]
    public void DocStrings()
    {
        AssertHighlighter("gherkin",
""""
Given a blog post named "Random" with Markdown body
  """
  Some Title, Eh?
  ===============
  Here is the first paragraph of my blog post. <b>Lorem</b> ipsum
  # not a comment
  """
Then done
"""",
"""
<span class="hljs-keyword">Given</span> a blog post named <span class="hljs-string">&quot;Random&quot;</span> with Markdown body
  <span class="hljs-string">&quot;&quot;&quot;
  Some Title, Eh?
  ===============
  Here is the first paragraph of my blog post. &lt;b&gt;Lorem&lt;/b&gt; ipsum
  # not a comment
  &quot;&quot;&quot;</span>
<span class="hljs-keyword">Then</span> done
""");
    }

    [Fact]
    public void DataTables()
    {
        AssertHighlighter("gherkin",
"""
Given the following users exist:
  | name   | email              | twitter         |
  | Aslak  | aslak@cucumber.io  | @aslak_hellesoy |
  | Julien | julien@cucumber.io | @jbpros         |
Then it works
""",
"""
<span class="hljs-keyword">Given</span> the following users exist:
  |<span class="hljs-string"> name   </span>|<span class="hljs-string"> email              </span>|<span class="hljs-string"> twitter         </span>|
  |<span class="hljs-string"> Aslak  </span>|<span class="hljs-string"> aslak@cucumber.io  </span>|<span class="hljs-string"> @aslak_hellesoy </span>|
  |<span class="hljs-string"> Julien </span>|<span class="hljs-string"> julien@cucumber.io </span>|<span class="hljs-string"> @jbpros         </span>|
<span class="hljs-keyword">Then</span> it works
""");
    }

    // Deviation from highlight.js, which highlights the following lines as cells when text follows the last `|`.
    [Fact]
    public void TableWithTrailingText()
    {
        AssertHighlighter("gherkin",
"""
| a | b |x
| c | d | trailing
Then next
""",
"""
|<span class="hljs-string"> a </span>|<span class="hljs-string"> b </span>|x
|<span class="hljs-string"> c </span>|<span class="hljs-string"> d </span>| trailing
<span class="hljs-keyword">Then</span> next
""");
    }

    [Fact]
    public void Asterisk()
    {
        AssertHighlighter("gherkin",
"""
Scenario: All done
  Given I am out shopping
  * I have eggs
  * I have milk
""",
"""
<span class="hljs-keyword">Scenario</span>: All done
  <span class="hljs-keyword">Given</span> I am out shopping
  <span class="hljs-symbol">*</span> I have eggs
  <span class="hljs-symbol">*</span> I have milk
""");
    }

    // As in highlight.js, an unterminated string continues on the next lines.
    [Fact]
    public void Strings()
    {
        AssertHighlighter("gherkin",
"""
Given "a \"quoted\" string" and "another"
When "unterminated
Then 'single quotes are text'
""",
"""
<span class="hljs-keyword">Given</span> <span class="hljs-string">&quot;a \&quot;quoted\&quot; string&quot;</span> and <span class="hljs-string">&quot;another&quot;</span>
<span class="hljs-keyword">When</span> <span class="hljs-string">&quot;unterminated
Then &#x27;single quotes are text&#x27;</span>
""");
    }

    // Deviation from highlight.js, whose placeholders can span lines.
    [Fact]
    public void Variables()
    {
        AssertHighlighter("gherkin",
"""
Given <user> has <count> items and a < b > c
Then <multi
line>
""",
"""
<span class="hljs-keyword">Given</span> <span class="hljs-variable">&lt;user&gt;</span> has <span class="hljs-variable">&lt;count&gt;</span> items and a <span class="hljs-variable">&lt; b &gt;</span> c
<span class="hljs-keyword">Then</span> &lt;multi
line&gt;
""");
    }

    [Fact]
    public void KeywordsAreCaseSensitive()
    {
        AssertHighlighter("gherkin",
"""
feature: lower
given when then and but
FEATURE: upper
Given When Then And But
Givens Whenever
""",
"""
feature: lower
given when then and but
FEATURE: upper
<span class="hljs-keyword">Given</span> <span class="hljs-keyword">When</span> <span class="hljs-keyword">Then</span> <span class="hljs-keyword">And</span> <span class="hljs-keyword">But</span>
Givens Whenever
""");
    }

    [Fact]
    public void UnterminatedDocString()
    {
        AssertHighlighter("gherkin",
""""
Given text
  """
  never closed
Then x
"""",
"""
<span class="hljs-keyword">Given</span> text
  <span class="hljs-string">&quot;&quot;&quot;
  never closed
Then x</span>
""");
    }

    // Deviation from highlight.js, which highlights the following lines as cells when a row has no closing `|`.
    [Fact]
    public void UnterminatedTable()
    {
        AssertHighlighter("gherkin",
"""
| a | b
Given x
| c |
""",
"""
|<span class="hljs-string"> a </span>| b
<span class="hljs-keyword">Given</span> x
|<span class="hljs-string"> c </span>|
""");
    }

    // Deviation from highlight.js, which highlights everything up to the next `>` as a placeholder.
    [Fact]
    public void ComparisonsAreNotPlaceholders()
    {
        AssertHighlighter("gherkin",
"""
Then the total is < 10
And the count is > 5
Given <a> and <b <c> and <>
""",
"""
<span class="hljs-keyword">Then</span> the total is &lt; 10
<span class="hljs-keyword">And</span> the count is &gt; 5
<span class="hljs-keyword">Given</span> <span class="hljs-variable">&lt;a&gt;</span> and &lt;b <span class="hljs-variable">&lt;c&gt;</span> and <span class="hljs-variable">&lt;&gt;</span>
""");
    }

    // Deviation from highlight.js, which highlights the following lines as cells when spaces follow the last `|`.
    [Fact]
    public void TableRowWithTrailingSpaces()
    {
        AssertHighlighter("gherkin",
"Examples:\n  | a | b |  \n  | c | d |\t\nThen next",
"<span class=\"hljs-keyword\">Examples</span>:\n  |<span class=\"hljs-string\"> a </span>|<span class=\"hljs-string\"> b </span>|  \n  |<span class=\"hljs-string\"> c </span>|<span class=\"hljs-string\"> d </span>|\t\n<span class=\"hljs-keyword\">Then</span> next");
    }

    [Fact]
    public void FeatureAlias()
    {
        AssertHighlighter("feature",
"""
Feature: Alias
  Scenario: s
    Given x
""",
"""
<span class="hljs-keyword">Feature</span>: Alias
  <span class="hljs-keyword">Scenario</span>: s
    <span class="hljs-keyword">Given</span> x
""");
    }

    // Unlike JavaScript's, .NET's `\w` matches non-ASCII letters, so `Givené` is a single word.
    [Fact]
    public void NonAscii()
    {
        AssertHighlighter("gherkin",
"""
Given café
When Givené
Then |é|x|é
""",
"""
<span class="hljs-keyword">Given</span> café
<span class="hljs-keyword">When</span> Givené
<span class="hljs-keyword">Then</span> |<span class="hljs-string">é</span>|<span class="hljs-string">x</span>|é
""");
    }
}
