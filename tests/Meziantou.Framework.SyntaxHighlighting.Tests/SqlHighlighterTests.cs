namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class SqlHighlighterTests
{

    [Fact]
    public void Select_Star()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void Select_Columns()
    {
        AssertHighlighter("sql",
"""
SELECT id, name, email FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> id, name, email <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void Select_Distinct()
    {
        AssertHighlighter("sql",
"""
SELECT DISTINCT country FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-keyword">DISTINCT</span> country <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void Select_Aliased()
    {
        AssertHighlighter("sql",
"""
SELECT id AS user_id, name AS full_name FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> id <span class="hljs-keyword">AS</span> user_id, name <span class="hljs-keyword">AS</span> full_name <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void Select_AliasNoAs()
    {
        AssertHighlighter("sql",
"""
SELECT id user_id FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> id user_id <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void Select_Expression()
    {
        AssertHighlighter("sql",
"""
SELECT first_name || ' ' || last_name FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> first_name <span class="hljs-operator">||</span> <span class="hljs-string">&#x27; &#x27;</span> <span class="hljs-operator">||</span> last_name <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void Select_Literal()
    {
        AssertHighlighter("sql",
"""
SELECT 1;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Select_FromAliased()
    {
        AssertHighlighter("sql",
"""
SELECT u.id FROM users AS u;
""",
"""
<span class="hljs-keyword">SELECT</span> u.id <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">AS</span> u;
""");
    }

    [Fact]
    public void Select_CaseSensitive()
    {
        AssertHighlighter("sql",
"""
select id from Users;
""",
"""
<span class="hljs-keyword">select</span> id <span class="hljs-keyword">from</span> Users;
""");
    }

    [Fact]
    public void Select_MixedCase()
    {
        AssertHighlighter("sql",
"""
Select Id From Users;
""",
"""
<span class="hljs-keyword">Select</span> Id <span class="hljs-keyword">From</span> Users;
""");
    }

    [Fact]
    public void Where_Equal()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users WHERE id = 1;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> id <span class="hljs-operator">=</span> <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Where_NotEqualBang()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users WHERE status != 'archived';
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> status <span class="hljs-operator">!=</span> <span class="hljs-string">&#x27;archived&#x27;</span>;
""");
    }

    [Fact]
    public void Where_NotEqualAngle()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users WHERE status <> 'archived';
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> status <span class="hljs-operator">&lt;&gt;</span> <span class="hljs-string">&#x27;archived&#x27;</span>;
""");
    }

    [Fact]
    public void Where_Comparison()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM products WHERE price > 100 AND stock <= 5;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> products <span class="hljs-keyword">WHERE</span> price <span class="hljs-operator">&gt;</span> <span class="hljs-number">100</span> <span class="hljs-keyword">AND</span> stock <span class="hljs-operator">&lt;=</span> <span class="hljs-number">5</span>;
""");
    }

    [Fact]
    public void Where_IsNull()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users WHERE deleted_at IS NULL;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> deleted_at <span class="hljs-keyword">IS</span> <span class="hljs-keyword">NULL</span>;
""");
    }

    [Fact]
    public void Where_IsNotNull()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users WHERE email IS NOT NULL;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> email <span class="hljs-keyword">IS</span> <span class="hljs-keyword">NOT NULL</span>;
""");
    }

    [Fact]
    public void Where_And()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users WHERE active = TRUE AND age >= 18;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> active <span class="hljs-operator">=</span> <span class="hljs-literal">TRUE</span> <span class="hljs-keyword">AND</span> age <span class="hljs-operator">&gt;=</span> <span class="hljs-number">18</span>;
""");
    }

    [Fact]
    public void Where_Or()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users WHERE country = 'US' OR country = 'CA';
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> country <span class="hljs-operator">=</span> <span class="hljs-string">&#x27;US&#x27;</span> <span class="hljs-keyword">OR</span> country <span class="hljs-operator">=</span> <span class="hljs-string">&#x27;CA&#x27;</span>;
""");
    }

    [Fact]
    public void Where_Not()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users WHERE NOT active;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> <span class="hljs-keyword">NOT</span> active;
""");
    }

    [Fact]
    public void Where_Between()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM orders WHERE total BETWEEN 100 AND 500;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> orders <span class="hljs-keyword">WHERE</span> total <span class="hljs-keyword">BETWEEN</span> <span class="hljs-number">100</span> <span class="hljs-keyword">AND</span> <span class="hljs-number">500</span>;
""");
    }

    [Fact]
    public void Where_NotBetween()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM orders WHERE total NOT BETWEEN 0 AND 100;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> orders <span class="hljs-keyword">WHERE</span> total <span class="hljs-keyword">NOT</span> <span class="hljs-keyword">BETWEEN</span> <span class="hljs-number">0</span> <span class="hljs-keyword">AND</span> <span class="hljs-number">100</span>;
""");
    }

    [Fact]
    public void Where_InList()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users WHERE country IN ('US', 'CA', 'UK');
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> country <span class="hljs-keyword">IN</span> (<span class="hljs-string">&#x27;US&#x27;</span>, <span class="hljs-string">&#x27;CA&#x27;</span>, <span class="hljs-string">&#x27;UK&#x27;</span>);
""");
    }

    [Fact]
    public void Where_NotIn()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users WHERE country NOT IN ('US', 'CA');
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> country <span class="hljs-keyword">NOT</span> <span class="hljs-keyword">IN</span> (<span class="hljs-string">&#x27;US&#x27;</span>, <span class="hljs-string">&#x27;CA&#x27;</span>);
""");
    }

    [Fact]
    public void Where_Like()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users WHERE email LIKE '%@example.com';
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> email <span class="hljs-keyword">LIKE</span> <span class="hljs-string">&#x27;%@example.com&#x27;</span>;
""");
    }

    [Fact]
    public void Where_NotLike()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users WHERE name NOT LIKE 'test%';
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> name <span class="hljs-keyword">NOT</span> <span class="hljs-keyword">LIKE</span> <span class="hljs-string">&#x27;test%&#x27;</span>;
""");
    }

    [Fact]
    public void Where_Ilike()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users WHERE name ILIKE '%alice%';
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> name ILIKE <span class="hljs-string">&#x27;%alice%&#x27;</span>;
""");
    }

    [Fact]
    public void Where_EscapeQuote()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users WHERE name = 'O''Brien';
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> name <span class="hljs-operator">=</span> <span class="hljs-string">&#x27;O&#x27;&#x27;Brien&#x27;</span>;
""");
    }

    [Fact]
    public void OrderGroupLimit_OrderBy()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users ORDER BY created_at;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> created_at;
""");
    }

    [Fact]
    public void OrderGroupLimit_OrderByDesc()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users ORDER BY created_at DESC;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> created_at <span class="hljs-keyword">DESC</span>;
""");
    }

    [Fact]
    public void OrderGroupLimit_OrderByMulti()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users ORDER BY country ASC, name DESC;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> country <span class="hljs-keyword">ASC</span>, name <span class="hljs-keyword">DESC</span>;
""");
    }

    [Fact]
    public void OrderGroupLimit_OrderByNullsFirst()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users ORDER BY last_login DESC NULLS LAST;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> last_login <span class="hljs-keyword">DESC</span> <span class="hljs-keyword">NULLS LAST</span>;
""");
    }

    [Fact]
    public void OrderGroupLimit_GroupBy()
    {
        AssertHighlighter("sql",
"""
SELECT country, COUNT(*) FROM users GROUP BY country;
""",
"""
<span class="hljs-keyword">SELECT</span> country, <span class="hljs-built_in">COUNT</span>(<span class="hljs-operator">*</span>) <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">GROUP</span> <span class="hljs-keyword">BY</span> country;
""");
    }

    [Fact]
    public void OrderGroupLimit_GroupByMulti()
    {
        AssertHighlighter("sql",
"""
SELECT country, city, COUNT(*) FROM users GROUP BY country, city;
""",
"""
<span class="hljs-keyword">SELECT</span> country, city, <span class="hljs-built_in">COUNT</span>(<span class="hljs-operator">*</span>) <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">GROUP</span> <span class="hljs-keyword">BY</span> country, city;
""");
    }

    [Fact]
    public void OrderGroupLimit_GroupByHaving()
    {
        AssertHighlighter("sql",
"""
SELECT country, COUNT(*) AS c FROM users GROUP BY country HAVING COUNT(*) > 10;
""",
"""
<span class="hljs-keyword">SELECT</span> country, <span class="hljs-built_in">COUNT</span>(<span class="hljs-operator">*</span>) <span class="hljs-keyword">AS</span> c <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">GROUP</span> <span class="hljs-keyword">BY</span> country <span class="hljs-keyword">HAVING</span> <span class="hljs-built_in">COUNT</span>(<span class="hljs-operator">*</span>) <span class="hljs-operator">&gt;</span> <span class="hljs-number">10</span>;
""");
    }

    [Fact]
    public void OrderGroupLimit_Limit()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users LIMIT 10;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users LIMIT <span class="hljs-number">10</span>;
""");
    }

    [Fact]
    public void OrderGroupLimit_LimitOffset()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users LIMIT 10 OFFSET 20;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users LIMIT <span class="hljs-number">10</span> <span class="hljs-keyword">OFFSET</span> <span class="hljs-number">20</span>;
""");
    }

    [Fact]
    public void OrderGroupLimit_FetchFirst()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users FETCH FIRST 10 ROWS ONLY;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">FETCH</span> <span class="hljs-keyword">FIRST</span> <span class="hljs-number">10</span> <span class="hljs-keyword">ROWS</span> <span class="hljs-keyword">ONLY</span>;
""");
    }

    [Fact]
    public void OrderGroupLimit_TopSqlServer()
    {
        AssertHighlighter("sql",
"""
SELECT TOP 10 * FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> TOP <span class="hljs-number">10</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void OrderGroupLimit_GroupingSets()
    {
        AssertHighlighter("sql",
"""
SELECT country, city, COUNT(*) FROM users GROUP BY GROUPING SETS ((country, city), (country), ());
""",
"""
<span class="hljs-keyword">SELECT</span> country, city, <span class="hljs-built_in">COUNT</span>(<span class="hljs-operator">*</span>) <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">GROUP</span> <span class="hljs-keyword">BY</span> <span class="hljs-keyword">GROUPING SETS</span> ((country, city), (country), ());
""");
    }

    [Fact]
    public void OrderGroupLimit_Cube()
    {
        AssertHighlighter("sql",
"""
SELECT country, city, SUM(total) FROM orders GROUP BY CUBE (country, city);
""",
"""
<span class="hljs-keyword">SELECT</span> country, city, <span class="hljs-built_in">SUM</span>(total) <span class="hljs-keyword">FROM</span> orders <span class="hljs-keyword">GROUP</span> <span class="hljs-keyword">BY</span> <span class="hljs-keyword">CUBE</span> (country, city);
""");
    }

    [Fact]
    public void OrderGroupLimit_Rollup()
    {
        AssertHighlighter("sql",
"""
SELECT country, SUM(total) FROM orders GROUP BY ROLLUP (country, region);
""",
"""
<span class="hljs-keyword">SELECT</span> country, <span class="hljs-built_in">SUM</span>(total) <span class="hljs-keyword">FROM</span> orders <span class="hljs-keyword">GROUP</span> <span class="hljs-keyword">BY</span> <span class="hljs-keyword">ROLLUP</span> (country, region);
""");
    }

    [Fact]
    public void Join_Inner()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM orders INNER JOIN users ON orders.user_id = users.id;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> orders <span class="hljs-keyword">INNER</span> <span class="hljs-keyword">JOIN</span> users <span class="hljs-keyword">ON</span> orders.user_id <span class="hljs-operator">=</span> users.id;
""");
    }

    [Fact]
    public void Join_Implicit()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM orders JOIN users ON orders.user_id = users.id;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> orders <span class="hljs-keyword">JOIN</span> users <span class="hljs-keyword">ON</span> orders.user_id <span class="hljs-operator">=</span> users.id;
""");
    }

    [Fact]
    public void Join_Left()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users LEFT JOIN orders ON users.id = orders.user_id;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">LEFT</span> <span class="hljs-keyword">JOIN</span> orders <span class="hljs-keyword">ON</span> users.id <span class="hljs-operator">=</span> orders.user_id;
""");
    }

    [Fact]
    public void Join_LeftOuter()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users LEFT OUTER JOIN orders ON users.id = orders.user_id;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">LEFT</span> <span class="hljs-keyword">OUTER</span> <span class="hljs-keyword">JOIN</span> orders <span class="hljs-keyword">ON</span> users.id <span class="hljs-operator">=</span> orders.user_id;
""");
    }

    [Fact]
    public void Join_Right()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users RIGHT JOIN orders ON users.id = orders.user_id;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">RIGHT</span> <span class="hljs-keyword">JOIN</span> orders <span class="hljs-keyword">ON</span> users.id <span class="hljs-operator">=</span> orders.user_id;
""");
    }

    [Fact]
    public void Join_FullOuter()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users FULL OUTER JOIN orders ON users.id = orders.user_id;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">FULL</span> <span class="hljs-keyword">OUTER</span> <span class="hljs-keyword">JOIN</span> orders <span class="hljs-keyword">ON</span> users.id <span class="hljs-operator">=</span> orders.user_id;
""");
    }

    [Fact]
    public void Join_Cross()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM colors CROSS JOIN sizes;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> colors <span class="hljs-keyword">CROSS</span> <span class="hljs-keyword">JOIN</span> sizes;
""");
    }

    [Fact]
    public void Join_Self()
    {
        AssertHighlighter("sql",
"""
SELECT a.name, b.name FROM users a JOIN users b ON a.manager_id = b.id;
""",
"""
<span class="hljs-keyword">SELECT</span> a.name, b.name <span class="hljs-keyword">FROM</span> users a <span class="hljs-keyword">JOIN</span> users b <span class="hljs-keyword">ON</span> a.manager_id <span class="hljs-operator">=</span> b.id;
""");
    }

    [Fact]
    public void Join_Using()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM orders JOIN users USING (user_id);
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> orders <span class="hljs-keyword">JOIN</span> users <span class="hljs-keyword">USING</span> (user_id);
""");
    }

    [Fact]
    public void Join_Multiple()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM orders o JOIN users u ON o.user_id = u.id JOIN products p ON o.product_id = p.id;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> orders o <span class="hljs-keyword">JOIN</span> users u <span class="hljs-keyword">ON</span> o.user_id <span class="hljs-operator">=</span> u.id <span class="hljs-keyword">JOIN</span> products p <span class="hljs-keyword">ON</span> o.product_id <span class="hljs-operator">=</span> p.id;
""");
    }

    [Fact]
    public void Join_Natural()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM orders NATURAL JOIN users;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> orders <span class="hljs-keyword">NATURAL</span> <span class="hljs-keyword">JOIN</span> users;
""");
    }

    [Fact]
    public void Join_Lateral()
    {
        AssertHighlighter("sql",
"""
SELECT u.id, t.total FROM users u, LATERAL (SELECT SUM(amount) AS total FROM orders WHERE user_id = u.id) t;
""",
"""
<span class="hljs-keyword">SELECT</span> u.id, t.total <span class="hljs-keyword">FROM</span> users u, <span class="hljs-keyword">LATERAL</span> (<span class="hljs-keyword">SELECT</span> <span class="hljs-built_in">SUM</span>(amount) <span class="hljs-keyword">AS</span> total <span class="hljs-keyword">FROM</span> orders <span class="hljs-keyword">WHERE</span> user_id <span class="hljs-operator">=</span> u.id) t;
""");
    }

    [Fact]
    public void Subquery_InWhere()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users WHERE id IN (SELECT user_id FROM orders);
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> id <span class="hljs-keyword">IN</span> (<span class="hljs-keyword">SELECT</span> user_id <span class="hljs-keyword">FROM</span> orders);
""");
    }

    [Fact]
    public void Subquery_Exists()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users u WHERE EXISTS (SELECT 1 FROM orders o WHERE o.user_id = u.id);
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users u <span class="hljs-keyword">WHERE</span> <span class="hljs-keyword">EXISTS</span> (<span class="hljs-keyword">SELECT</span> <span class="hljs-number">1</span> <span class="hljs-keyword">FROM</span> orders o <span class="hljs-keyword">WHERE</span> o.user_id <span class="hljs-operator">=</span> u.id);
""");
    }

    [Fact]
    public void Subquery_NotExists()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users u WHERE NOT EXISTS (SELECT 1 FROM orders o WHERE o.user_id = u.id);
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users u <span class="hljs-keyword">WHERE</span> <span class="hljs-keyword">NOT</span> <span class="hljs-keyword">EXISTS</span> (<span class="hljs-keyword">SELECT</span> <span class="hljs-number">1</span> <span class="hljs-keyword">FROM</span> orders o <span class="hljs-keyword">WHERE</span> o.user_id <span class="hljs-operator">=</span> u.id);
""");
    }

    [Fact]
    public void Subquery_InFrom()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM (SELECT id, name FROM users WHERE active) AS active_users;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> (<span class="hljs-keyword">SELECT</span> id, name <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> active) <span class="hljs-keyword">AS</span> active_users;
""");
    }

    [Fact]
    public void Subquery_ScalarInSelect()
    {
        AssertHighlighter("sql",
"""
SELECT id, (SELECT COUNT(*) FROM orders WHERE user_id = users.id) AS order_count FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> id, (<span class="hljs-keyword">SELECT</span> <span class="hljs-built_in">COUNT</span>(<span class="hljs-operator">*</span>) <span class="hljs-keyword">FROM</span> orders <span class="hljs-keyword">WHERE</span> user_id <span class="hljs-operator">=</span> users.id) <span class="hljs-keyword">AS</span> order_count <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void Subquery_Correlated()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users u WHERE u.salary > (SELECT AVG(salary) FROM users WHERE department = u.department);
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users u <span class="hljs-keyword">WHERE</span> u.salary <span class="hljs-operator">&gt;</span> (<span class="hljs-keyword">SELECT</span> <span class="hljs-built_in">AVG</span>(salary) <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> department <span class="hljs-operator">=</span> u.department);
""");
    }

    [Fact]
    public void Subquery_Any()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM products WHERE price > ANY (SELECT price FROM products WHERE category = 'A');
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> products <span class="hljs-keyword">WHERE</span> price <span class="hljs-operator">&gt;</span> <span class="hljs-keyword">ANY</span> (<span class="hljs-keyword">SELECT</span> price <span class="hljs-keyword">FROM</span> products <span class="hljs-keyword">WHERE</span> category <span class="hljs-operator">=</span> <span class="hljs-string">&#x27;A&#x27;</span>);
""");
    }

    [Fact]
    public void Subquery_All()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM products WHERE price > ALL (SELECT price FROM products WHERE category = 'A');
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> products <span class="hljs-keyword">WHERE</span> price <span class="hljs-operator">&gt;</span> <span class="hljs-keyword">ALL</span> (<span class="hljs-keyword">SELECT</span> price <span class="hljs-keyword">FROM</span> products <span class="hljs-keyword">WHERE</span> category <span class="hljs-operator">=</span> <span class="hljs-string">&#x27;A&#x27;</span>);
""");
    }

    [Fact]
    public void Cte_Single()
    {
        AssertHighlighter("sql",
"""
WITH active_users AS (
  SELECT id, name FROM users WHERE active
)
SELECT * FROM active_users;
""",
"""
<span class="hljs-keyword">WITH</span> active_users <span class="hljs-keyword">AS</span> (
  <span class="hljs-keyword">SELECT</span> id, name <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> active
)
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> active_users;
""");
    }

    [Fact]
    public void Cte_Multiple()
    {
        AssertHighlighter("sql",
"""
WITH
  active_users AS (SELECT id FROM users WHERE active),
  recent_orders AS (SELECT id FROM orders WHERE created_at > CURRENT_DATE - INTERVAL '30 days')
SELECT * FROM active_users JOIN recent_orders USING (id);
""",
"""
<span class="hljs-keyword">WITH</span>
  active_users <span class="hljs-keyword">AS</span> (<span class="hljs-keyword">SELECT</span> id <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> active),
  recent_orders <span class="hljs-keyword">AS</span> (<span class="hljs-keyword">SELECT</span> id <span class="hljs-keyword">FROM</span> orders <span class="hljs-keyword">WHERE</span> created_at <span class="hljs-operator">&gt;</span> <span class="hljs-built_in">CURRENT_DATE</span> <span class="hljs-operator">-</span> <span class="hljs-type">INTERVAL</span> <span class="hljs-string">&#x27;30 days&#x27;</span>)
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> active_users <span class="hljs-keyword">JOIN</span> recent_orders <span class="hljs-keyword">USING</span> (id);
""");
    }

    [Fact]
    public void Cte_Recursive()
    {
        AssertHighlighter("sql",
"""
WITH RECURSIVE subordinates AS (
  SELECT id, manager_id FROM employees WHERE id = 1
  UNION ALL
  SELECT e.id, e.manager_id FROM employees e JOIN subordinates s ON e.manager_id = s.id
)
SELECT * FROM subordinates;
""",
"""
<span class="hljs-keyword">WITH</span> <span class="hljs-keyword">RECURSIVE</span> subordinates <span class="hljs-keyword">AS</span> (
  <span class="hljs-keyword">SELECT</span> id, manager_id <span class="hljs-keyword">FROM</span> employees <span class="hljs-keyword">WHERE</span> id <span class="hljs-operator">=</span> <span class="hljs-number">1</span>
  <span class="hljs-keyword">UNION</span> <span class="hljs-keyword">ALL</span>
  <span class="hljs-keyword">SELECT</span> e.id, e.manager_id <span class="hljs-keyword">FROM</span> employees e <span class="hljs-keyword">JOIN</span> subordinates s <span class="hljs-keyword">ON</span> e.manager_id <span class="hljs-operator">=</span> s.id
)
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> subordinates;
""");
    }

    [Fact]
    public void Cte_MaterializedHint()
    {
        AssertHighlighter("sql",
"""
WITH cte AS MATERIALIZED (SELECT * FROM users)
SELECT * FROM cte;
""",
"""
<span class="hljs-keyword">WITH</span> cte <span class="hljs-keyword">AS</span> MATERIALIZED (<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users)
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> cte;
""");
    }

    [Fact]
    public void Insert_Values()
    {
        AssertHighlighter("sql",
"""
INSERT INTO users (name, email) VALUES ('Alice', 'alice@example.com');
""",
"""
<span class="hljs-keyword">INSERT INTO</span> users (name, email) <span class="hljs-keyword">VALUES</span> (<span class="hljs-string">&#x27;Alice&#x27;</span>, <span class="hljs-string">&#x27;alice@example.com&#x27;</span>);
""");
    }

    [Fact]
    public void Insert_ValuesMulti()
    {
        AssertHighlighter("sql",
"""
INSERT INTO users (name, email) VALUES
  ('Alice', 'alice@example.com'),
  ('Bob', 'bob@example.com');
""",
"""
<span class="hljs-keyword">INSERT INTO</span> users (name, email) <span class="hljs-keyword">VALUES</span>
  (<span class="hljs-string">&#x27;Alice&#x27;</span>, <span class="hljs-string">&#x27;alice@example.com&#x27;</span>),
  (<span class="hljs-string">&#x27;Bob&#x27;</span>, <span class="hljs-string">&#x27;bob@example.com&#x27;</span>);
""");
    }

    [Fact]
    public void Insert_NoColumns()
    {
        AssertHighlighter("sql",
"""
INSERT INTO logs VALUES ('login', CURRENT_TIMESTAMP);
""",
"""
<span class="hljs-keyword">INSERT INTO</span> logs <span class="hljs-keyword">VALUES</span> (<span class="hljs-string">&#x27;login&#x27;</span>, <span class="hljs-built_in">CURRENT_TIMESTAMP</span>);
""");
    }

    [Fact]
    public void Insert_FromSelect()
    {
        AssertHighlighter("sql",
"""
INSERT INTO archived_users (id, name) SELECT id, name FROM users WHERE deleted_at IS NOT NULL;
""",
"""
<span class="hljs-keyword">INSERT INTO</span> archived_users (id, name) <span class="hljs-keyword">SELECT</span> id, name <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> deleted_at <span class="hljs-keyword">IS</span> <span class="hljs-keyword">NOT NULL</span>;
""");
    }

    [Fact]
    public void Insert_OnConflictDoNothing()
    {
        AssertHighlighter("sql",
"""
INSERT INTO users (id, name) VALUES (1, 'Alice') ON CONFLICT (id) DO NOTHING;
""",
"""
<span class="hljs-keyword">INSERT INTO</span> users (id, name) <span class="hljs-keyword">VALUES</span> (<span class="hljs-number">1</span>, <span class="hljs-string">&#x27;Alice&#x27;</span>) <span class="hljs-keyword">ON</span> CONFLICT (id) DO NOTHING;
""");
    }

    [Fact]
    public void Insert_OnConflictUpdate()
    {
        AssertHighlighter("sql",
"""
INSERT INTO users (id, name) VALUES (1, 'Alice') ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name;
""",
"""
<span class="hljs-keyword">INSERT INTO</span> users (id, name) <span class="hljs-keyword">VALUES</span> (<span class="hljs-number">1</span>, <span class="hljs-string">&#x27;Alice&#x27;</span>) <span class="hljs-keyword">ON</span> CONFLICT (id) DO <span class="hljs-keyword">UPDATE</span> <span class="hljs-keyword">SET</span> name <span class="hljs-operator">=</span> EXCLUDED.name;
""");
    }

    [Fact]
    public void Insert_OnDuplicateKey()
    {
        AssertHighlighter("sql",
"""
INSERT INTO users (id, name) VALUES (1, 'Alice') ON DUPLICATE KEY UPDATE name = VALUES(name);
""",
"""
<span class="hljs-keyword">INSERT INTO</span> users (id, name) <span class="hljs-keyword">VALUES</span> (<span class="hljs-number">1</span>, <span class="hljs-string">&#x27;Alice&#x27;</span>) <span class="hljs-keyword">ON</span> DUPLICATE KEY <span class="hljs-keyword">UPDATE</span> name <span class="hljs-operator">=</span> <span class="hljs-keyword">VALUES</span>(name);
""");
    }

    [Fact]
    public void Insert_Returning()
    {
        AssertHighlighter("sql",
"""
INSERT INTO users (name) VALUES ('Alice') RETURNING id, created_at;
""",
"""
<span class="hljs-keyword">INSERT INTO</span> users (name) <span class="hljs-keyword">VALUES</span> (<span class="hljs-string">&#x27;Alice&#x27;</span>) RETURNING id, created_at;
""");
    }

    [Fact]
    public void Update_Simple()
    {
        AssertHighlighter("sql",
"""
UPDATE users SET name = 'Alice' WHERE id = 1;
""",
"""
<span class="hljs-keyword">UPDATE</span> users <span class="hljs-keyword">SET</span> name <span class="hljs-operator">=</span> <span class="hljs-string">&#x27;Alice&#x27;</span> <span class="hljs-keyword">WHERE</span> id <span class="hljs-operator">=</span> <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Update_Multiple()
    {
        AssertHighlighter("sql",
"""
UPDATE users SET name = 'Alice', email = 'alice@example.com' WHERE id = 1;
""",
"""
<span class="hljs-keyword">UPDATE</span> users <span class="hljs-keyword">SET</span> name <span class="hljs-operator">=</span> <span class="hljs-string">&#x27;Alice&#x27;</span>, email <span class="hljs-operator">=</span> <span class="hljs-string">&#x27;alice@example.com&#x27;</span> <span class="hljs-keyword">WHERE</span> id <span class="hljs-operator">=</span> <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Update_Expression()
    {
        AssertHighlighter("sql",
"""
UPDATE products SET stock = stock - 1 WHERE id = 42;
""",
"""
<span class="hljs-keyword">UPDATE</span> products <span class="hljs-keyword">SET</span> stock <span class="hljs-operator">=</span> stock <span class="hljs-operator">-</span> <span class="hljs-number">1</span> <span class="hljs-keyword">WHERE</span> id <span class="hljs-operator">=</span> <span class="hljs-number">42</span>;
""");
    }

    [Fact]
    public void Update_FromJoin()
    {
        AssertHighlighter("sql",
"""
UPDATE users u SET total_orders = o.count FROM (SELECT user_id, COUNT(*) AS count FROM orders GROUP BY user_id) o WHERE u.id = o.user_id;
""",
"""
<span class="hljs-keyword">UPDATE</span> users u <span class="hljs-keyword">SET</span> total_orders <span class="hljs-operator">=</span> o.count <span class="hljs-keyword">FROM</span> (<span class="hljs-keyword">SELECT</span> user_id, <span class="hljs-built_in">COUNT</span>(<span class="hljs-operator">*</span>) <span class="hljs-keyword">AS</span> count <span class="hljs-keyword">FROM</span> orders <span class="hljs-keyword">GROUP</span> <span class="hljs-keyword">BY</span> user_id) o <span class="hljs-keyword">WHERE</span> u.id <span class="hljs-operator">=</span> o.user_id;
""");
    }

    [Fact]
    public void Update_Returning()
    {
        AssertHighlighter("sql",
"""
UPDATE users SET active = FALSE WHERE last_login < CURRENT_DATE - INTERVAL '1 year' RETURNING id;
""",
"""
<span class="hljs-keyword">UPDATE</span> users <span class="hljs-keyword">SET</span> active <span class="hljs-operator">=</span> <span class="hljs-literal">FALSE</span> <span class="hljs-keyword">WHERE</span> last_login <span class="hljs-operator">&lt;</span> <span class="hljs-built_in">CURRENT_DATE</span> <span class="hljs-operator">-</span> <span class="hljs-type">INTERVAL</span> <span class="hljs-string">&#x27;1 year&#x27;</span> RETURNING id;
""");
    }

    [Fact]
    public void Delete_Simple()
    {
        AssertHighlighter("sql",
"""
DELETE FROM users WHERE id = 1;
""",
"""
<span class="hljs-keyword">DELETE</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> id <span class="hljs-operator">=</span> <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Delete_All()
    {
        AssertHighlighter("sql",
"""
DELETE FROM logs;
""",
"""
<span class="hljs-keyword">DELETE</span> <span class="hljs-keyword">FROM</span> logs;
""");
    }

    [Fact]
    public void Delete_Returning()
    {
        AssertHighlighter("sql",
"""
DELETE FROM users WHERE active = FALSE RETURNING id, email;
""",
"""
<span class="hljs-keyword">DELETE</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> active <span class="hljs-operator">=</span> <span class="hljs-literal">FALSE</span> RETURNING id, email;
""");
    }

    [Fact]
    public void Delete_WithJoin()
    {
        AssertHighlighter("sql",
"""
DELETE FROM orders WHERE user_id IN (SELECT id FROM users WHERE active = FALSE);
""",
"""
<span class="hljs-keyword">DELETE</span> <span class="hljs-keyword">FROM</span> orders <span class="hljs-keyword">WHERE</span> user_id <span class="hljs-keyword">IN</span> (<span class="hljs-keyword">SELECT</span> id <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> active <span class="hljs-operator">=</span> <span class="hljs-literal">FALSE</span>);
""");
    }

    [Fact]
    public void Delete_Truncate()
    {
        AssertHighlighter("sql",
"""
TRUNCATE TABLE logs;
""",
"""
<span class="hljs-keyword">TRUNCATE</span> <span class="hljs-keyword">TABLE</span> logs;
""");
    }

    [Fact]
    public void Delete_TruncateCascade()
    {
        AssertHighlighter("sql",
"""
TRUNCATE TABLE users CASCADE;
""",
"""
<span class="hljs-keyword">TRUNCATE</span> <span class="hljs-keyword">TABLE</span> users CASCADE;
""");
    }

    [Fact]
    public void Merge_Basic()
    {
        AssertHighlighter("sql",
"""
MERGE INTO target t USING source s ON t.id = s.id
WHEN MATCHED THEN UPDATE SET t.name = s.name
WHEN NOT MATCHED THEN INSERT (id, name) VALUES (s.id, s.name);
""",
"""
<span class="hljs-keyword">MERGE</span> <span class="hljs-keyword">INTO</span> target t <span class="hljs-keyword">USING</span> source s <span class="hljs-keyword">ON</span> t.id <span class="hljs-operator">=</span> s.id
<span class="hljs-keyword">WHEN</span> MATCHED <span class="hljs-keyword">THEN</span> <span class="hljs-keyword">UPDATE</span> <span class="hljs-keyword">SET</span> t.name <span class="hljs-operator">=</span> s.name
<span class="hljs-keyword">WHEN</span> <span class="hljs-keyword">NOT</span> MATCHED <span class="hljs-keyword">THEN</span> <span class="hljs-keyword">INSERT</span> (id, name) <span class="hljs-keyword">VALUES</span> (s.id, s.name);
""");
    }

    [Fact]
    public void Ddl_CreateTable()
    {
        AssertHighlighter("sql",
"""
CREATE TABLE users (id INT PRIMARY KEY, name VARCHAR(100));
""",
"""
<span class="hljs-keyword">CREATE TABLE</span> users (id <span class="hljs-type">INT</span> <span class="hljs-keyword">PRIMARY KEY</span>, name <span class="hljs-type">VARCHAR</span>(<span class="hljs-number">100</span>));
""");
    }

    [Fact]
    public void Ddl_CreateTableMulti()
    {
        AssertHighlighter("sql",
"""
CREATE TABLE users (
  id INT PRIMARY KEY,
  name VARCHAR(100) NOT NULL,
  email VARCHAR(255) UNIQUE,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
""",
"""
<span class="hljs-keyword">CREATE TABLE</span> users (
  id <span class="hljs-type">INT</span> <span class="hljs-keyword">PRIMARY KEY</span>,
  name <span class="hljs-type">VARCHAR</span>(<span class="hljs-number">100</span>) <span class="hljs-keyword">NOT NULL</span>,
  email <span class="hljs-type">VARCHAR</span>(<span class="hljs-number">255</span>) <span class="hljs-keyword">UNIQUE</span>,
  created_at <span class="hljs-type">TIMESTAMP</span> <span class="hljs-keyword">DEFAULT</span> <span class="hljs-built_in">CURRENT_TIMESTAMP</span>
);
""");
    }

    [Fact]
    public void Ddl_CreateTableIfNotExists()
    {
        AssertHighlighter("sql",
"""
CREATE TABLE IF NOT EXISTS users (id INT, name VARCHAR(100));
""",
"""
<span class="hljs-keyword">CREATE TABLE</span> IF <span class="hljs-keyword">NOT</span> <span class="hljs-keyword">EXISTS</span> users (id <span class="hljs-type">INT</span>, name <span class="hljs-type">VARCHAR</span>(<span class="hljs-number">100</span>));
""");
    }

    [Fact]
    public void Ddl_CreateTableForeignKey()
    {
        AssertHighlighter("sql",
"""
CREATE TABLE orders (
  id INT PRIMARY KEY,
  user_id INT REFERENCES users(id) ON DELETE CASCADE,
  total DECIMAL(10, 2)
);
""",
"""
<span class="hljs-keyword">CREATE TABLE</span> orders (
  id <span class="hljs-type">INT</span> <span class="hljs-keyword">PRIMARY KEY</span>,
  user_id <span class="hljs-type">INT</span> <span class="hljs-keyword">REFERENCES</span> users(id) <span class="hljs-keyword">ON</span> <span class="hljs-keyword">DELETE</span> CASCADE,
  total <span class="hljs-type">DECIMAL</span>(<span class="hljs-number">10</span>, <span class="hljs-number">2</span>)
);
""");
    }

    [Fact]
    public void Ddl_CreateTableCheck()
    {
        AssertHighlighter("sql",
"""
CREATE TABLE products (id INT, price DECIMAL CHECK (price > 0));
""",
"""
<span class="hljs-keyword">CREATE TABLE</span> products (id <span class="hljs-type">INT</span>, price <span class="hljs-type">DECIMAL</span> <span class="hljs-keyword">CHECK</span> (price <span class="hljs-operator">&gt;</span> <span class="hljs-number">0</span>));
""");
    }

    [Fact]
    public void Ddl_CreateTableTemporary()
    {
        AssertHighlighter("sql",
"""
CREATE TEMPORARY TABLE temp_users (id INT, name VARCHAR(100));
""",
"""
<span class="hljs-keyword">CREATE</span> TEMPORARY <span class="hljs-keyword">TABLE</span> temp_users (id <span class="hljs-type">INT</span>, name <span class="hljs-type">VARCHAR</span>(<span class="hljs-number">100</span>));
""");
    }

    [Fact]
    public void Ddl_AlterAddColumn()
    {
        AssertHighlighter("sql",
"""
ALTER TABLE users ADD COLUMN phone VARCHAR(20);
""",
"""
<span class="hljs-keyword">ALTER TABLE</span> users <span class="hljs-keyword">ADD</span> <span class="hljs-keyword">COLUMN</span> phone <span class="hljs-type">VARCHAR</span>(<span class="hljs-number">20</span>);
""");
    }

    [Fact]
    public void Ddl_AlterDropColumn()
    {
        AssertHighlighter("sql",
"""
ALTER TABLE users DROP COLUMN phone;
""",
"""
<span class="hljs-keyword">ALTER TABLE</span> users <span class="hljs-keyword">DROP</span> <span class="hljs-keyword">COLUMN</span> phone;
""");
    }

    [Fact]
    public void Ddl_AlterRenameColumn()
    {
        AssertHighlighter("sql",
"""
ALTER TABLE users RENAME COLUMN name TO full_name;
""",
"""
<span class="hljs-keyword">ALTER TABLE</span> users RENAME <span class="hljs-keyword">COLUMN</span> name <span class="hljs-keyword">TO</span> full_name;
""");
    }

    [Fact]
    public void Ddl_AlterAlterColumn()
    {
        AssertHighlighter("sql",
"""
ALTER TABLE users ALTER COLUMN name TYPE TEXT;
""",
"""
<span class="hljs-keyword">ALTER TABLE</span> users <span class="hljs-keyword">ALTER</span> <span class="hljs-keyword">COLUMN</span> name TYPE TEXT;
""");
    }

    [Fact]
    public void Ddl_AlterRenameTable()
    {
        AssertHighlighter("sql",
"""
ALTER TABLE users RENAME TO accounts;
""",
"""
<span class="hljs-keyword">ALTER TABLE</span> users RENAME <span class="hljs-keyword">TO</span> accounts;
""");
    }

    [Fact]
    public void Ddl_DropTable()
    {
        AssertHighlighter("sql",
"""
DROP TABLE users;
""",
"""
<span class="hljs-keyword">DROP</span> <span class="hljs-keyword">TABLE</span> users;
""");
    }

    [Fact]
    public void Ddl_DropTableIfExists()
    {
        AssertHighlighter("sql",
"""
DROP TABLE IF EXISTS users CASCADE;
""",
"""
<span class="hljs-keyword">DROP</span> <span class="hljs-keyword">TABLE</span> IF <span class="hljs-keyword">EXISTS</span> users CASCADE;
""");
    }

    [Fact]
    public void Ddl_CreateIndex()
    {
        AssertHighlighter("sql",
"""
CREATE INDEX idx_users_email ON users (email);
""",
"""
<span class="hljs-keyword">CREATE</span> INDEX idx_users_email <span class="hljs-keyword">ON</span> users (email);
""");
    }

    [Fact]
    public void Ddl_CreateUniqueIndex()
    {
        AssertHighlighter("sql",
"""
CREATE UNIQUE INDEX idx_users_email ON users (email);
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">UNIQUE</span> INDEX idx_users_email <span class="hljs-keyword">ON</span> users (email);
""");
    }

    [Fact]
    public void Ddl_CreateIndexConcurrent()
    {
        AssertHighlighter("sql",
"""
CREATE INDEX CONCURRENTLY idx_users_country ON users (country);
""",
"""
<span class="hljs-keyword">CREATE</span> INDEX CONCURRENTLY idx_users_country <span class="hljs-keyword">ON</span> users (country);
""");
    }

    [Fact]
    public void Ddl_DropIndex()
    {
        AssertHighlighter("sql",
"""
DROP INDEX idx_users_email;
""",
"""
<span class="hljs-keyword">DROP</span> INDEX idx_users_email;
""");
    }

    [Fact]
    public void Ddl_CreateView()
    {
        AssertHighlighter("sql",
"""
CREATE VIEW active_users AS SELECT id, name FROM users WHERE active;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">VIEW</span> active_users <span class="hljs-keyword">AS</span> <span class="hljs-keyword">SELECT</span> id, name <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> active;
""");
    }

    [Fact]
    public void Ddl_CreateOrReplaceView()
    {
        AssertHighlighter("sql",
"""
CREATE OR REPLACE VIEW active_users AS SELECT id FROM users WHERE active;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">OR</span> REPLACE <span class="hljs-keyword">VIEW</span> active_users <span class="hljs-keyword">AS</span> <span class="hljs-keyword">SELECT</span> id <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> active;
""");
    }

    [Fact]
    public void Ddl_CreateMaterializedView()
    {
        AssertHighlighter("sql",
"""
CREATE MATERIALIZED VIEW user_stats AS SELECT country, COUNT(*) FROM users GROUP BY country;
""",
"""
<span class="hljs-keyword">CREATE</span> MATERIALIZED <span class="hljs-keyword">VIEW</span> user_stats <span class="hljs-keyword">AS</span> <span class="hljs-keyword">SELECT</span> country, <span class="hljs-built_in">COUNT</span>(<span class="hljs-operator">*</span>) <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">GROUP</span> <span class="hljs-keyword">BY</span> country;
""");
    }

    [Fact]
    public void Ddl_DropView()
    {
        AssertHighlighter("sql",
"""
DROP VIEW active_users;
""",
"""
<span class="hljs-keyword">DROP</span> <span class="hljs-keyword">VIEW</span> active_users;
""");
    }

    [Fact]
    public void Ddl_CreateSequence()
    {
        AssertHighlighter("sql",
"""
CREATE SEQUENCE user_id_seq START WITH 1 INCREMENT BY 1;
""",
"""
<span class="hljs-keyword">CREATE</span> SEQUENCE user_id_seq <span class="hljs-keyword">START</span> <span class="hljs-keyword">WITH</span> <span class="hljs-number">1</span> INCREMENT <span class="hljs-keyword">BY</span> <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Ddl_CreateSchema()
    {
        AssertHighlighter("sql",
"""
CREATE SCHEMA reporting;
""",
"""
<span class="hljs-keyword">CREATE</span> SCHEMA reporting;
""");
    }

    [Fact]
    public void Ddl_CreateDatabase()
    {
        AssertHighlighter("sql",
"""
CREATE DATABASE myapp;
""",
"""
<span class="hljs-keyword">CREATE</span> DATABASE myapp;
""");
    }

    [Fact]
    public void Ddl_CreateFunction()
    {
        AssertHighlighter("sql",
"""
CREATE FUNCTION add(a INT, b INT) RETURNS INT AS $$ SELECT a + b; $$ LANGUAGE SQL;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> <span class="hljs-keyword">add</span>(a <span class="hljs-type">INT</span>, b <span class="hljs-type">INT</span>) <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">INT</span> <span class="hljs-keyword">AS</span> $$ <span class="hljs-keyword">SELECT</span> a <span class="hljs-operator">+</span> b; $$ <span class="hljs-keyword">LANGUAGE</span> <span class="hljs-keyword">SQL</span>;
""");
    }

    [Fact]
    public void Ddl_CreateProcedure()
    {
        AssertHighlighter("sql",
"""
CREATE PROCEDURE archive_user(user_id INT) AS BEGIN UPDATE users SET archived = TRUE WHERE id = user_id; END;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">PROCEDURE</span> archive_user(user_id <span class="hljs-type">INT</span>) <span class="hljs-keyword">AS</span> <span class="hljs-keyword">BEGIN</span> <span class="hljs-keyword">UPDATE</span> users <span class="hljs-keyword">SET</span> archived <span class="hljs-operator">=</span> <span class="hljs-literal">TRUE</span> <span class="hljs-keyword">WHERE</span> id <span class="hljs-operator">=</span> user_id; <span class="hljs-keyword">END</span>;
""");
    }

    [Fact]
    public void Ddl_CreateTrigger()
    {
        AssertHighlighter("sql",
"""
CREATE TRIGGER update_modified BEFORE UPDATE ON users FOR EACH ROW EXECUTE FUNCTION set_modified_timestamp();
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">TRIGGER</span> update_modified BEFORE <span class="hljs-keyword">UPDATE</span> <span class="hljs-keyword">ON</span> users <span class="hljs-keyword">FOR</span> <span class="hljs-keyword">EACH</span> <span class="hljs-type">ROW</span> <span class="hljs-keyword">EXECUTE</span> <span class="hljs-keyword">FUNCTION</span> set_modified_timestamp();
""");
    }

    [Fact]
    public void DataType_IntVariants()
    {
        AssertHighlighter("sql",
"""
CREATE TABLE t (a TINYINT, b SMALLINT, c INT, d BIGINT);
""",
"""
<span class="hljs-keyword">CREATE TABLE</span> t (a TINYINT, b <span class="hljs-type">SMALLINT</span>, c <span class="hljs-type">INT</span>, d <span class="hljs-type">BIGINT</span>);
""");
    }

    [Fact]
    public void DataType_Decimal()
    {
        AssertHighlighter("sql",
"""
CREATE TABLE t (price DECIMAL(10, 2), tax NUMERIC(5, 4));
""",
"""
<span class="hljs-keyword">CREATE TABLE</span> t (price <span class="hljs-type">DECIMAL</span>(<span class="hljs-number">10</span>, <span class="hljs-number">2</span>), tax <span class="hljs-type">NUMERIC</span>(<span class="hljs-number">5</span>, <span class="hljs-number">4</span>));
""");
    }

    [Fact]
    public void DataType_FloatDouble()
    {
        AssertHighlighter("sql",
"""
CREATE TABLE t (a REAL, b FLOAT, c DOUBLE PRECISION);
""",
"""
<span class="hljs-keyword">CREATE TABLE</span> t (a <span class="hljs-type">REAL</span>, b <span class="hljs-type">FLOAT</span>, c <span class="hljs-type">DOUBLE PRECISION</span>);
""");
    }

    [Fact]
    public void DataType_CharText()
    {
        AssertHighlighter("sql",
"""
CREATE TABLE t (a CHAR(10), b VARCHAR(255), c TEXT);
""",
"""
<span class="hljs-keyword">CREATE TABLE</span> t (a <span class="hljs-type">CHAR</span>(<span class="hljs-number">10</span>), b <span class="hljs-type">VARCHAR</span>(<span class="hljs-number">255</span>), c TEXT);
""");
    }

    [Fact]
    public void DataType_Date()
    {
        AssertHighlighter("sql",
"""
CREATE TABLE t (a DATE, b TIME, c TIMESTAMP, d TIMESTAMPTZ);
""",
"""
<span class="hljs-keyword">CREATE TABLE</span> t (a <span class="hljs-type">DATE</span>, b <span class="hljs-type">TIME</span>, c <span class="hljs-type">TIMESTAMP</span>, d TIMESTAMPTZ);
""");
    }

    [Fact]
    public void DataType_Boolean()
    {
        AssertHighlighter("sql",
"""
CREATE TABLE t (active BOOLEAN DEFAULT TRUE);
""",
"""
<span class="hljs-keyword">CREATE TABLE</span> t (active <span class="hljs-type">BOOLEAN</span> <span class="hljs-keyword">DEFAULT</span> <span class="hljs-literal">TRUE</span>);
""");
    }

    [Fact]
    public void DataType_Json()
    {
        AssertHighlighter("sql",
"""
CREATE TABLE t (data JSON, attrs JSONB);
""",
"""
<span class="hljs-keyword">CREATE TABLE</span> t (data JSON, attrs JSONB);
""");
    }

    [Fact]
    public void DataType_Uuid()
    {
        AssertHighlighter("sql",
"""
CREATE TABLE t (id UUID PRIMARY KEY DEFAULT gen_random_uuid());
""",
"""
<span class="hljs-keyword">CREATE TABLE</span> t (id UUID <span class="hljs-keyword">PRIMARY KEY</span> <span class="hljs-keyword">DEFAULT</span> gen_random_uuid());
""");
    }

    [Fact]
    public void DataType_Array()
    {
        AssertHighlighter("sql",
"""
CREATE TABLE t (tags TEXT[], scores INT[]);
""",
"""
<span class="hljs-keyword">CREATE TABLE</span> t (tags TEXT[], scores <span class="hljs-type">INT</span>[]);
""");
    }

    [Fact]
    public void DataType_Serial()
    {
        AssertHighlighter("sql",
"""
CREATE TABLE t (id SERIAL PRIMARY KEY);
""",
"""
<span class="hljs-keyword">CREATE TABLE</span> t (id SERIAL <span class="hljs-keyword">PRIMARY KEY</span>);
""");
    }

    [Fact]
    public void DataType_Identity()
    {
        AssertHighlighter("sql",
"""
CREATE TABLE t (id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY);
""",
"""
<span class="hljs-keyword">CREATE TABLE</span> t (id <span class="hljs-type">INT</span> GENERATED ALWAYS <span class="hljs-keyword">AS</span> <span class="hljs-keyword">IDENTITY</span> <span class="hljs-keyword">PRIMARY KEY</span>);
""");
    }

    [Fact]
    public void Aggregate_Count()
    {
        AssertHighlighter("sql",
"""
SELECT COUNT(*) FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-built_in">COUNT</span>(<span class="hljs-operator">*</span>) <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void Aggregate_CountColumn()
    {
        AssertHighlighter("sql",
"""
SELECT COUNT(email) FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-built_in">COUNT</span>(email) <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void Aggregate_CountDistinct()
    {
        AssertHighlighter("sql",
"""
SELECT COUNT(DISTINCT country) FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-built_in">COUNT</span>(<span class="hljs-keyword">DISTINCT</span> country) <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void Aggregate_Sum()
    {
        AssertHighlighter("sql",
"""
SELECT SUM(total) FROM orders;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-built_in">SUM</span>(total) <span class="hljs-keyword">FROM</span> orders;
""");
    }

    [Fact]
    public void Aggregate_Avg()
    {
        AssertHighlighter("sql",
"""
SELECT AVG(price) FROM products;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-built_in">AVG</span>(price) <span class="hljs-keyword">FROM</span> products;
""");
    }

    [Fact]
    public void Aggregate_MinMax()
    {
        AssertHighlighter("sql",
"""
SELECT MIN(price), MAX(price) FROM products;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-built_in">MIN</span>(price), <span class="hljs-built_in">MAX</span>(price) <span class="hljs-keyword">FROM</span> products;
""");
    }

    [Fact]
    public void Aggregate_StringAgg()
    {
        AssertHighlighter("sql",
"""
SELECT STRING_AGG(name, ', ') FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> STRING_AGG(name, <span class="hljs-string">&#x27;, &#x27;</span>) <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void Aggregate_ArrayAgg()
    {
        AssertHighlighter("sql",
"""
SELECT ARRAY_AGG(id) FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-built_in">ARRAY_AGG</span>(id) <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void Aggregate_JsonAgg()
    {
        AssertHighlighter("sql",
"""
SELECT JSON_AGG(row_to_json(users)) FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> JSON_AGG(row_to_json(users)) <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void Window_RowNumber()
    {
        AssertHighlighter("sql",
"""
SELECT id, ROW_NUMBER() OVER (ORDER BY created_at) FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> id, <span class="hljs-built_in">ROW_NUMBER</span>() <span class="hljs-keyword">OVER</span> (<span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> created_at) <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void Window_Rank()
    {
        AssertHighlighter("sql",
"""
SELECT id, RANK() OVER (ORDER BY score DESC) FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> id, <span class="hljs-built_in">RANK</span>() <span class="hljs-keyword">OVER</span> (<span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> score <span class="hljs-keyword">DESC</span>) <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void Window_DenseRank()
    {
        AssertHighlighter("sql",
"""
SELECT id, DENSE_RANK() OVER (ORDER BY score DESC) FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> id, <span class="hljs-built_in">DENSE_RANK</span>() <span class="hljs-keyword">OVER</span> (<span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> score <span class="hljs-keyword">DESC</span>) <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void Window_PartitionBy()
    {
        AssertHighlighter("sql",
"""
SELECT id, country, ROW_NUMBER() OVER (PARTITION BY country ORDER BY created_at) FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> id, country, <span class="hljs-built_in">ROW_NUMBER</span>() <span class="hljs-keyword">OVER</span> (<span class="hljs-keyword">PARTITION</span> <span class="hljs-keyword">BY</span> country <span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> created_at) <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void Window_Lag()
    {
        AssertHighlighter("sql",
"""
SELECT id, LAG(score, 1) OVER (ORDER BY date) AS prev FROM scores;
""",
"""
<span class="hljs-keyword">SELECT</span> id, <span class="hljs-built_in">LAG</span>(score, <span class="hljs-number">1</span>) <span class="hljs-keyword">OVER</span> (<span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> <span class="hljs-type">date</span>) <span class="hljs-keyword">AS</span> prev <span class="hljs-keyword">FROM</span> scores;
""");
    }

    [Fact]
    public void Window_Lead()
    {
        AssertHighlighter("sql",
"""
SELECT id, LEAD(score, 1, 0) OVER (ORDER BY date) AS next FROM scores;
""",
"""
<span class="hljs-keyword">SELECT</span> id, <span class="hljs-built_in">LEAD</span>(score, <span class="hljs-number">1</span>, <span class="hljs-number">0</span>) <span class="hljs-keyword">OVER</span> (<span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> <span class="hljs-type">date</span>) <span class="hljs-keyword">AS</span> next <span class="hljs-keyword">FROM</span> scores;
""");
    }

    [Fact]
    public void Window_SumWindow()
    {
        AssertHighlighter("sql",
"""
SELECT id, SUM(amount) OVER (PARTITION BY user_id ORDER BY date) AS running_total FROM orders;
""",
"""
<span class="hljs-keyword">SELECT</span> id, <span class="hljs-built_in">SUM</span>(amount) <span class="hljs-keyword">OVER</span> (<span class="hljs-keyword">PARTITION</span> <span class="hljs-keyword">BY</span> user_id <span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> <span class="hljs-type">date</span>) <span class="hljs-keyword">AS</span> running_total <span class="hljs-keyword">FROM</span> orders;
""");
    }

    [Fact]
    public void Window_Frame()
    {
        AssertHighlighter("sql",
"""
SELECT id, AVG(price) OVER (ORDER BY date ROWS BETWEEN 6 PRECEDING AND CURRENT ROW) AS ma7 FROM prices;
""",
"""
<span class="hljs-keyword">SELECT</span> id, <span class="hljs-built_in">AVG</span>(price) <span class="hljs-keyword">OVER</span> (<span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> <span class="hljs-type">date</span> <span class="hljs-keyword">ROWS</span> <span class="hljs-keyword">BETWEEN</span> <span class="hljs-number">6</span> PRECEDING <span class="hljs-keyword">AND</span> <span class="hljs-keyword">CURRENT</span> <span class="hljs-type">ROW</span>) <span class="hljs-keyword">AS</span> ma7 <span class="hljs-keyword">FROM</span> prices;
""");
    }

    [Fact]
    public void Window_NamedWindow()
    {
        AssertHighlighter("sql",
"""
SELECT id, ROW_NUMBER() OVER w, RANK() OVER w FROM users WINDOW w AS (ORDER BY score DESC);
""",
"""
<span class="hljs-keyword">SELECT</span> id, <span class="hljs-built_in">ROW_NUMBER</span>() <span class="hljs-keyword">OVER</span> w, <span class="hljs-built_in">RANK</span>() <span class="hljs-keyword">OVER</span> w <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WINDOW</span> w <span class="hljs-keyword">AS</span> (<span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> score <span class="hljs-keyword">DESC</span>);
""");
    }

    [Fact]
    public void ScalarFunction_Upper()
    {
        AssertHighlighter("sql",
"""
SELECT UPPER(name) FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-built_in">UPPER</span>(name) <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void ScalarFunction_Lower()
    {
        AssertHighlighter("sql",
"""
SELECT LOWER(email) FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-built_in">LOWER</span>(email) <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void ScalarFunction_Concat()
    {
        AssertHighlighter("sql",
"""
SELECT CONCAT(first_name, ' ', last_name) FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> CONCAT(first_name, <span class="hljs-string">&#x27; &#x27;</span>, last_name) <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void ScalarFunction_Substring()
    {
        AssertHighlighter("sql",
"""
SELECT SUBSTRING(name FROM 1 FOR 3) FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-built_in">SUBSTRING</span>(name <span class="hljs-keyword">FROM</span> <span class="hljs-number">1</span> <span class="hljs-keyword">FOR</span> <span class="hljs-number">3</span>) <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void ScalarFunction_Length()
    {
        AssertHighlighter("sql",
"""
SELECT LENGTH(name) FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> LENGTH(name) <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void ScalarFunction_Trim()
    {
        AssertHighlighter("sql",
"""
SELECT TRIM(BOTH ' ' FROM name) FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-built_in">TRIM</span>(<span class="hljs-keyword">BOTH</span> <span class="hljs-string">&#x27; &#x27;</span> <span class="hljs-keyword">FROM</span> name) <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void ScalarFunction_Replace()
    {
        AssertHighlighter("sql",
"""
SELECT REPLACE(phone, '-', '') FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> REPLACE(phone, <span class="hljs-string">&#x27;-&#x27;</span>, <span class="hljs-string">&#x27;&#x27;</span>) <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void ScalarFunction_Coalesce()
    {
        AssertHighlighter("sql",
"""
SELECT COALESCE(nickname, name, 'Unknown') FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-built_in">COALESCE</span>(nickname, name, <span class="hljs-string">&#x27;Unknown&#x27;</span>) <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void ScalarFunction_NullIf()
    {
        AssertHighlighter("sql",
"""
SELECT NULLIF(score, 0) FROM games;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-built_in">NULLIF</span>(score, <span class="hljs-number">0</span>) <span class="hljs-keyword">FROM</span> games;
""");
    }

    [Fact]
    public void ScalarFunction_Cast()
    {
        AssertHighlighter("sql",
"""
SELECT CAST(id AS VARCHAR) FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-built_in">CAST</span>(id <span class="hljs-keyword">AS</span> <span class="hljs-type">VARCHAR</span>) <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void ScalarFunction_CastShorthand()
    {
        AssertHighlighter("sql",
"""
SELECT id::TEXT FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> id::TEXT <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void ScalarFunction_Now()
    {
        AssertHighlighter("sql",
"""
SELECT NOW();
""",
"""
<span class="hljs-keyword">SELECT</span> NOW();
""");
    }

    [Fact]
    public void ScalarFunction_CurrentDate()
    {
        AssertHighlighter("sql",
"""
SELECT CURRENT_DATE;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-built_in">CURRENT_DATE</span>;
""");
    }

    [Fact]
    public void ScalarFunction_CurrentTimestamp()
    {
        AssertHighlighter("sql",
"""
SELECT CURRENT_TIMESTAMP;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-built_in">CURRENT_TIMESTAMP</span>;
""");
    }

    [Fact]
    public void ScalarFunction_Extract()
    {
        AssertHighlighter("sql",
"""
SELECT EXTRACT(YEAR FROM created_at) FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-built_in">EXTRACT</span>(<span class="hljs-keyword">YEAR</span> <span class="hljs-keyword">FROM</span> created_at) <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void ScalarFunction_DateTrunc()
    {
        AssertHighlighter("sql",
"""
SELECT DATE_TRUNC('month', created_at) FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> DATE_TRUNC(<span class="hljs-string">&#x27;month&#x27;</span>, created_at) <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void ScalarFunction_IntervalLit()
    {
        AssertHighlighter("sql",
"""
SELECT created_at + INTERVAL '7 days' FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> created_at <span class="hljs-operator">+</span> <span class="hljs-type">INTERVAL</span> <span class="hljs-string">&#x27;7 days&#x27;</span> <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void Case_Simple()
    {
        AssertHighlighter("sql",
"""
SELECT id, CASE WHEN active THEN 'yes' ELSE 'no' END AS status FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> id, <span class="hljs-keyword">CASE</span> <span class="hljs-keyword">WHEN</span> active <span class="hljs-keyword">THEN</span> <span class="hljs-string">&#x27;yes&#x27;</span> <span class="hljs-keyword">ELSE</span> <span class="hljs-string">&#x27;no&#x27;</span> <span class="hljs-keyword">END</span> <span class="hljs-keyword">AS</span> status <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void Case_Searched()
    {
        AssertHighlighter("sql",
"""
SELECT id, CASE WHEN score >= 90 THEN 'A' WHEN score >= 80 THEN 'B' ELSE 'C' END AS grade FROM students;
""",
"""
<span class="hljs-keyword">SELECT</span> id, <span class="hljs-keyword">CASE</span> <span class="hljs-keyword">WHEN</span> score <span class="hljs-operator">&gt;=</span> <span class="hljs-number">90</span> <span class="hljs-keyword">THEN</span> <span class="hljs-string">&#x27;A&#x27;</span> <span class="hljs-keyword">WHEN</span> score <span class="hljs-operator">&gt;=</span> <span class="hljs-number">80</span> <span class="hljs-keyword">THEN</span> <span class="hljs-string">&#x27;B&#x27;</span> <span class="hljs-keyword">ELSE</span> <span class="hljs-string">&#x27;C&#x27;</span> <span class="hljs-keyword">END</span> <span class="hljs-keyword">AS</span> grade <span class="hljs-keyword">FROM</span> students;
""");
    }

    [Fact]
    public void Case_Value()
    {
        AssertHighlighter("sql",
"""
SELECT id, CASE status WHEN 1 THEN 'active' WHEN 0 THEN 'inactive' END FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> id, <span class="hljs-keyword">CASE</span> status <span class="hljs-keyword">WHEN</span> <span class="hljs-number">1</span> <span class="hljs-keyword">THEN</span> <span class="hljs-string">&#x27;active&#x27;</span> <span class="hljs-keyword">WHEN</span> <span class="hljs-number">0</span> <span class="hljs-keyword">THEN</span> <span class="hljs-string">&#x27;inactive&#x27;</span> <span class="hljs-keyword">END</span> <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void Case_Nested()
    {
        AssertHighlighter("sql",
"""
SELECT CASE WHEN a > 0 THEN CASE WHEN b > 0 THEN 'both' ELSE 'a' END ELSE 'neither' END FROM t;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-keyword">CASE</span> <span class="hljs-keyword">WHEN</span> a <span class="hljs-operator">&gt;</span> <span class="hljs-number">0</span> <span class="hljs-keyword">THEN</span> <span class="hljs-keyword">CASE</span> <span class="hljs-keyword">WHEN</span> b <span class="hljs-operator">&gt;</span> <span class="hljs-number">0</span> <span class="hljs-keyword">THEN</span> <span class="hljs-string">&#x27;both&#x27;</span> <span class="hljs-keyword">ELSE</span> <span class="hljs-string">&#x27;a&#x27;</span> <span class="hljs-keyword">END</span> <span class="hljs-keyword">ELSE</span> <span class="hljs-string">&#x27;neither&#x27;</span> <span class="hljs-keyword">END</span> <span class="hljs-keyword">FROM</span> t;
""");
    }

    [Fact]
    public void Set_Union()
    {
        AssertHighlighter("sql",
"""
SELECT id FROM users UNION SELECT id FROM customers;
""",
"""
<span class="hljs-keyword">SELECT</span> id <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">UNION</span> <span class="hljs-keyword">SELECT</span> id <span class="hljs-keyword">FROM</span> customers;
""");
    }

    [Fact]
    public void Set_UnionAll()
    {
        AssertHighlighter("sql",
"""
SELECT id FROM users UNION ALL SELECT id FROM customers;
""",
"""
<span class="hljs-keyword">SELECT</span> id <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">UNION</span> <span class="hljs-keyword">ALL</span> <span class="hljs-keyword">SELECT</span> id <span class="hljs-keyword">FROM</span> customers;
""");
    }

    [Fact]
    public void Set_Intersect()
    {
        AssertHighlighter("sql",
"""
SELECT id FROM users INTERSECT SELECT id FROM customers;
""",
"""
<span class="hljs-keyword">SELECT</span> id <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">INTERSECT</span> <span class="hljs-keyword">SELECT</span> id <span class="hljs-keyword">FROM</span> customers;
""");
    }

    [Fact]
    public void Set_Except()
    {
        AssertHighlighter("sql",
"""
SELECT id FROM users EXCEPT SELECT id FROM banned;
""",
"""
<span class="hljs-keyword">SELECT</span> id <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">EXCEPT</span> <span class="hljs-keyword">SELECT</span> id <span class="hljs-keyword">FROM</span> banned;
""");
    }

    [Fact]
    public void Set_Minus()
    {
        AssertHighlighter("sql",
"""
SELECT id FROM users MINUS SELECT id FROM banned;
""",
"""
<span class="hljs-keyword">SELECT</span> id <span class="hljs-keyword">FROM</span> users MINUS <span class="hljs-keyword">SELECT</span> id <span class="hljs-keyword">FROM</span> banned;
""");
    }

    [Fact]
    public void Transaction_BeginCommit()
    {
        AssertHighlighter("sql",
"""
BEGIN;
INSERT INTO users (name) VALUES ('Alice');
COMMIT;
""",
"""
<span class="hljs-keyword">BEGIN</span>;
<span class="hljs-keyword">INSERT INTO</span> users (name) <span class="hljs-keyword">VALUES</span> (<span class="hljs-string">&#x27;Alice&#x27;</span>);
<span class="hljs-keyword">COMMIT</span>;
""");
    }

    [Fact]
    public void Transaction_StartCommit()
    {
        AssertHighlighter("sql",
"""
START TRANSACTION;
UPDATE users SET active = FALSE WHERE id = 1;
COMMIT;
""",
"""
<span class="hljs-keyword">START</span> TRANSACTION;
<span class="hljs-keyword">UPDATE</span> users <span class="hljs-keyword">SET</span> active <span class="hljs-operator">=</span> <span class="hljs-literal">FALSE</span> <span class="hljs-keyword">WHERE</span> id <span class="hljs-operator">=</span> <span class="hljs-number">1</span>;
<span class="hljs-keyword">COMMIT</span>;
""");
    }

    [Fact]
    public void Transaction_Rollback()
    {
        AssertHighlighter("sql",
"""
BEGIN;
DELETE FROM users;
ROLLBACK;
""",
"""
<span class="hljs-keyword">BEGIN</span>;
<span class="hljs-keyword">DELETE</span> <span class="hljs-keyword">FROM</span> users;
<span class="hljs-keyword">ROLLBACK</span>;
""");
    }

    [Fact]
    public void Transaction_Savepoint()
    {
        AssertHighlighter("sql",
"""
BEGIN;
INSERT INTO users (name) VALUES ('Alice');
SAVEPOINT sp1;
INSERT INTO users (name) VALUES ('Bob');
ROLLBACK TO sp1;
COMMIT;
""",
"""
<span class="hljs-keyword">BEGIN</span>;
<span class="hljs-keyword">INSERT INTO</span> users (name) <span class="hljs-keyword">VALUES</span> (<span class="hljs-string">&#x27;Alice&#x27;</span>);
<span class="hljs-keyword">SAVEPOINT</span> sp1;
<span class="hljs-keyword">INSERT INTO</span> users (name) <span class="hljs-keyword">VALUES</span> (<span class="hljs-string">&#x27;Bob&#x27;</span>);
<span class="hljs-keyword">ROLLBACK</span> <span class="hljs-keyword">TO</span> sp1;
<span class="hljs-keyword">COMMIT</span>;
""");
    }

    [Fact]
    public void Transaction_IsolationLevel()
    {
        AssertHighlighter("sql",
"""
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
""",
"""
<span class="hljs-keyword">SET</span> TRANSACTION ISOLATION LEVEL SERIALIZABLE;
""");
    }

    [Fact]
    public void Permission_Grant()
    {
        AssertHighlighter("sql",
"""
GRANT SELECT, INSERT ON users TO app_role;
""",
"""
<span class="hljs-keyword">GRANT</span> <span class="hljs-keyword">SELECT</span>, <span class="hljs-keyword">INSERT</span> <span class="hljs-keyword">ON</span> users <span class="hljs-keyword">TO</span> app_role;
""");
    }

    [Fact]
    public void Permission_GrantAll()
    {
        AssertHighlighter("sql",
"""
GRANT ALL PRIVILEGES ON DATABASE myapp TO admin;
""",
"""
<span class="hljs-keyword">GRANT</span> <span class="hljs-keyword">ALL</span> PRIVILEGES <span class="hljs-keyword">ON</span> DATABASE myapp <span class="hljs-keyword">TO</span> admin;
""");
    }

    [Fact]
    public void Permission_Revoke()
    {
        AssertHighlighter("sql",
"""
REVOKE INSERT ON users FROM guest;
""",
"""
<span class="hljs-keyword">REVOKE</span> <span class="hljs-keyword">INSERT</span> <span class="hljs-keyword">ON</span> users <span class="hljs-keyword">FROM</span> guest;
""");
    }

    [Fact]
    public void Permission_CreateRole()
    {
        AssertHighlighter("sql",
"""
CREATE ROLE analyst WITH LOGIN PASSWORD 'secret';
""",
"""
<span class="hljs-keyword">CREATE</span> ROLE analyst <span class="hljs-keyword">WITH</span> LOGIN PASSWORD <span class="hljs-string">&#x27;secret&#x27;</span>;
""");
    }

    [Fact]
    public void Permission_AlterRole()
    {
        AssertHighlighter("sql",
"""
ALTER ROLE analyst WITH PASSWORD 'newsecret';
""",
"""
<span class="hljs-keyword">ALTER</span> ROLE analyst <span class="hljs-keyword">WITH</span> PASSWORD <span class="hljs-string">&#x27;newsecret&#x27;</span>;
""");
    }

    [Fact]
    public void Identifier_QuotedDouble()
    {
        AssertHighlighter("sql",
"""
SELECT "user_id" FROM "Users";
""",
"""
<span class="hljs-keyword">SELECT</span> &quot;user_id&quot; <span class="hljs-keyword">FROM</span> &quot;Users&quot;;
""");
    }

    [Fact]
    public void Identifier_QuotedBacktick()
    {
        AssertHighlighter("sql",
"""
SELECT `user_id` FROM `users`;
""",
"""
<span class="hljs-keyword">SELECT</span> `user_id` <span class="hljs-keyword">FROM</span> `users`;
""");
    }

    [Fact]
    public void Identifier_QualifiedSchema()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM public.users;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> public.users;
""");
    }

    [Fact]
    public void Identifier_QualifiedDatabase()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM myapp.public.users;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> myapp.public.users;
""");
    }

    [Fact]
    public void Identifier_WithUnderscore()
    {
        AssertHighlighter("sql",
"""
SELECT user_id, first_name FROM user_accounts;
""",
"""
<span class="hljs-keyword">SELECT</span> user_id, first_name <span class="hljs-keyword">FROM</span> user_accounts;
""");
    }

    [Fact]
    public void Literal_StringSingle()
    {
        AssertHighlighter("sql",
"""
SELECT 'hello';
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-string">&#x27;hello&#x27;</span>;
""");
    }

    [Fact]
    public void Literal_StringEscape()
    {
        AssertHighlighter("sql",
"""
SELECT 'O''Brien';
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-string">&#x27;O&#x27;&#x27;Brien&#x27;</span>;
""");
    }

    [Fact]
    public void Literal_StringEscapeBackslash()
    {
        AssertHighlighter("sql",
"""
SELECT E'line1\nline2';
""",
"""
<span class="hljs-keyword">SELECT</span> E<span class="hljs-string">&#x27;line1\nline2&#x27;</span>;
""");
    }

    [Fact]
    public void Literal_NumberInt()
    {
        AssertHighlighter("sql",
"""
SELECT 42;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-number">42</span>;
""");
    }

    [Fact]
    public void Literal_NumberFloat()
    {
        AssertHighlighter("sql",
"""
SELECT 3.14;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-number">3.14</span>;
""");
    }

    [Fact]
    public void Literal_NumberExponent()
    {
        AssertHighlighter("sql",
"""
SELECT 1.5e10;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-number">1.5e10</span>;
""");
    }

    [Fact]
    public void Literal_BooleanTrue()
    {
        AssertHighlighter("sql",
"""
SELECT TRUE;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-literal">TRUE</span>;
""");
    }

    [Fact]
    public void Literal_BooleanFalse()
    {
        AssertHighlighter("sql",
"""
SELECT FALSE;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-literal">FALSE</span>;
""");
    }

    [Fact]
    public void Literal_Null()
    {
        AssertHighlighter("sql",
"""
SELECT NULL;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-keyword">NULL</span>;
""");
    }

    [Fact]
    public void Literal_DateLiteral()
    {
        AssertHighlighter("sql",
"""
SELECT DATE '2026-05-26';
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-type">DATE</span> <span class="hljs-string">&#x27;2026-05-26&#x27;</span>;
""");
    }

    [Fact]
    public void Literal_TimestampLiteral()
    {
        AssertHighlighter("sql",
"""
SELECT TIMESTAMP '2026-05-26 10:30:00';
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-type">TIMESTAMP</span> <span class="hljs-string">&#x27;2026-05-26 10:30:00&#x27;</span>;
""");
    }

    [Fact]
    public void Literal_IntervalLiteral()
    {
        AssertHighlighter("sql",
"""
SELECT INTERVAL '1 day 2 hours';
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-type">INTERVAL</span> <span class="hljs-string">&#x27;1 day 2 hours&#x27;</span>;
""");
    }

    [Fact]
    public void JsonOps_PathText()
    {
        AssertHighlighter("sql",
"""
SELECT data->>'name' FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> data<span class="hljs-operator">-</span><span class="hljs-operator">&gt;&gt;</span><span class="hljs-string">&#x27;name&#x27;</span> <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void JsonOps_PathJson()
    {
        AssertHighlighter("sql",
"""
SELECT data->'address' FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> data<span class="hljs-operator">-</span><span class="hljs-operator">&gt;</span><span class="hljs-string">&#x27;address&#x27;</span> <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void JsonOps_PathChain()
    {
        AssertHighlighter("sql",
"""
SELECT data->'address'->>'city' FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> data<span class="hljs-operator">-</span><span class="hljs-operator">&gt;</span><span class="hljs-string">&#x27;address&#x27;</span><span class="hljs-operator">-</span><span class="hljs-operator">&gt;&gt;</span><span class="hljs-string">&#x27;city&#x27;</span> <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void JsonOps_Contains()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users WHERE data @> '{"role": "admin"}';
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> data @<span class="hljs-operator">&gt;</span> <span class="hljs-string">&#x27;{&quot;role&quot;: &quot;admin&quot;}&#x27;</span>;
""");
    }

    [Fact]
    public void JsonOps_KeyExists()
    {
        AssertHighlighter("sql",
"""
SELECT * FROM users WHERE data ? 'email';
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> data ? <span class="hljs-string">&#x27;email&#x27;</span>;
""");
    }

    [Fact]
    public void Operator_Concat()
    {
        AssertHighlighter("sql",
"""
SELECT 'Hello ' || name FROM users;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-string">&#x27;Hello &#x27;</span> <span class="hljs-operator">||</span> name <span class="hljs-keyword">FROM</span> users;
""");
    }

    [Fact]
    public void Operator_Modulo()
    {
        AssertHighlighter("sql",
"""
SELECT 10 % 3;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-number">10</span> <span class="hljs-operator">%</span> <span class="hljs-number">3</span>;
""");
    }

    [Fact]
    public void Operator_Power()
    {
        AssertHighlighter("sql",
"""
SELECT 2 ^ 10;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-number">2</span> <span class="hljs-operator">^</span> <span class="hljs-number">10</span>;
""");
    }

    [Fact]
    public void Operator_BitwiseAnd()
    {
        AssertHighlighter("sql",
"""
SELECT 12 & 10;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-number">12</span> <span class="hljs-operator">&amp;</span> <span class="hljs-number">10</span>;
""");
    }

    [Fact]
    public void Operator_BitwiseOr()
    {
        AssertHighlighter("sql",
"""
SELECT 12 | 10;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-number">12</span> <span class="hljs-operator">|</span> <span class="hljs-number">10</span>;
""");
    }

    [Fact]
    public void Comment_LineDouble()
    {
        AssertHighlighter("sql",
"""
-- a comment
""",
"""
<span class="hljs-comment">-- a comment</span>
""");
    }

    [Fact]
    public void Comment_LineInline()
    {
        AssertHighlighter("sql",
"""
SELECT 1; -- trailing
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-number">1</span>; <span class="hljs-comment">-- trailing</span>
""");
    }

    [Fact]
    public void Comment_Block()
    {
        AssertHighlighter("sql",
"""
/* a block comment */
""",
"""
<span class="hljs-comment">/* a block comment */</span>
""");
    }

    [Fact]
    public void Comment_BlockMulti()
    {
        AssertHighlighter("sql",
"""
/*
  multi
  line
*/
""",
"""
<span class="hljs-comment">/*
  multi
  line
*/</span>
""");
    }

    [Fact]
    public void Comment_BlockInside()
    {
        AssertHighlighter("sql",
"""
SELECT 1 /* inline */, 2;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-number">1</span> <span class="hljs-comment">/* inline */</span>, <span class="hljs-number">2</span>;
""");
    }

    [Fact]
    public void Composite_ReportQuery()
    {
        AssertHighlighter("sql",
"""
WITH monthly AS (
  SELECT DATE_TRUNC('month', created_at) AS month, COUNT(*) AS signups
  FROM users
  GROUP BY 1
)
SELECT month, signups, LAG(signups, 1) OVER (ORDER BY month) AS prev_month
FROM monthly
ORDER BY month;
""",
"""
<span class="hljs-keyword">WITH</span> monthly <span class="hljs-keyword">AS</span> (
  <span class="hljs-keyword">SELECT</span> DATE_TRUNC(<span class="hljs-string">&#x27;month&#x27;</span>, created_at) <span class="hljs-keyword">AS</span> <span class="hljs-keyword">month</span>, <span class="hljs-built_in">COUNT</span>(<span class="hljs-operator">*</span>) <span class="hljs-keyword">AS</span> signups
  <span class="hljs-keyword">FROM</span> users
  <span class="hljs-keyword">GROUP</span> <span class="hljs-keyword">BY</span> <span class="hljs-number">1</span>
)
<span class="hljs-keyword">SELECT</span> <span class="hljs-keyword">month</span>, signups, <span class="hljs-built_in">LAG</span>(signups, <span class="hljs-number">1</span>) <span class="hljs-keyword">OVER</span> (<span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> <span class="hljs-keyword">month</span>) <span class="hljs-keyword">AS</span> prev_month
<span class="hljs-keyword">FROM</span> monthly
<span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> <span class="hljs-keyword">month</span>;
""");
    }

    [Fact]
    public void Composite_AnalyticsQuery()
    {
        AssertHighlighter("sql",
"""
SELECT
  u.country,
  COUNT(DISTINCT u.id) AS users,
  COUNT(o.id) AS orders,
  COALESCE(SUM(o.total), 0) AS revenue
FROM users u
LEFT JOIN orders o ON o.user_id = u.id
WHERE u.created_at >= CURRENT_DATE - INTERVAL '30 days'
GROUP BY u.country
HAVING COUNT(o.id) > 0
ORDER BY revenue DESC
LIMIT 10;
""",
"""
<span class="hljs-keyword">SELECT</span>
  u.country,
  <span class="hljs-built_in">COUNT</span>(<span class="hljs-keyword">DISTINCT</span> u.id) <span class="hljs-keyword">AS</span> users,
  <span class="hljs-built_in">COUNT</span>(o.id) <span class="hljs-keyword">AS</span> orders,
  <span class="hljs-built_in">COALESCE</span>(<span class="hljs-built_in">SUM</span>(o.total), <span class="hljs-number">0</span>) <span class="hljs-keyword">AS</span> revenue
<span class="hljs-keyword">FROM</span> users u
<span class="hljs-keyword">LEFT</span> <span class="hljs-keyword">JOIN</span> orders o <span class="hljs-keyword">ON</span> o.user_id <span class="hljs-operator">=</span> u.id
<span class="hljs-keyword">WHERE</span> u.created_at <span class="hljs-operator">&gt;=</span> <span class="hljs-built_in">CURRENT_DATE</span> <span class="hljs-operator">-</span> <span class="hljs-type">INTERVAL</span> <span class="hljs-string">&#x27;30 days&#x27;</span>
<span class="hljs-keyword">GROUP</span> <span class="hljs-keyword">BY</span> u.country
<span class="hljs-keyword">HAVING</span> <span class="hljs-built_in">COUNT</span>(o.id) <span class="hljs-operator">&gt;</span> <span class="hljs-number">0</span>
<span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> revenue <span class="hljs-keyword">DESC</span>
LIMIT <span class="hljs-number">10</span>;
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("sql",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("sql",
"""
-- just a comment
""",
"""
<span class="hljs-comment">-- just a comment</span>
""");
    }

    [Fact]
    public void SpecialEdge_Semicolons()
    {
        AssertHighlighter("sql",
"""
SELECT 1; SELECT 2; SELECT 3;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-number">1</span>; <span class="hljs-keyword">SELECT</span> <span class="hljs-number">2</span>; <span class="hljs-keyword">SELECT</span> <span class="hljs-number">3</span>;
""");
    }

    [Fact]
    public void SpecialEdge_TrailingSpace()
    {
        AssertHighlighter("sql",
"""
SELECT 1;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-number">1</span>;
""");
    }
}
