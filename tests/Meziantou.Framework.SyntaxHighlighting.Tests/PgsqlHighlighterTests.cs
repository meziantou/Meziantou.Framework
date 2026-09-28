namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class PgsqlHighlighterTests
{
    [Fact]
    public void Select()
    {
        AssertHighlighter("pgsql",
"""
SELECT id, name, count(*) AS total
FROM public.users u
LEFT OUTER JOIN orders o ON o.user_id = u.id
WHERE u.active IS NOT NULL AND u.created_at > now() - interval '7 days'
GROUP BY id, name
HAVING count(*) > 10
ORDER BY total DESC NULLS LAST
LIMIT 10 OFFSET 5;
""",
"""
<span class="hljs-keyword">SELECT</span> id, <span class="hljs-type">name</span>, count(*) <span class="hljs-keyword">AS</span> total
<span class="hljs-keyword">FROM</span> <span class="hljs-built_in">public</span>.users u
<span class="hljs-keyword">LEFT OUTER JOIN</span> orders o <span class="hljs-keyword">ON</span> o.user_id = u.id
<span class="hljs-keyword">WHERE</span> u.active <span class="hljs-keyword">IS</span> <span class="hljs-keyword">NOT</span> <span class="hljs-keyword">NULL</span> <span class="hljs-keyword">AND</span> u.created_at &gt; now() - <span class="hljs-type">interval</span> <span class="hljs-string">&#x27;7 days&#x27;</span>
<span class="hljs-keyword">GROUP</span> <span class="hljs-keyword">BY</span> id, <span class="hljs-type">name</span>
<span class="hljs-keyword">HAVING</span> count(*) &gt; <span class="hljs-number">10</span>
<span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> total <span class="hljs-keyword">DESC</span> <span class="hljs-keyword">NULLS LAST</span>
<span class="hljs-keyword">LIMIT</span> <span class="hljs-number">10</span> <span class="hljs-keyword">OFFSET</span> <span class="hljs-number">5</span>;
""");
    }

    [Fact]
    public void CreateTable()
    {
        AssertHighlighter("pgsql",
"""
CREATE TABLE IF NOT EXISTS accounts (
    id bigserial PRIMARY KEY,
    email varchar(255) NOT NULL UNIQUE,
    balance numeric(12, 2) DEFAULT 0.00,
    created timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated timestamp without time zone,
    data jsonb,
    tags text[],
    owner_id integer REFERENCES users (id) ON DELETE CASCADE,
    CONSTRAINT positive CHECK (balance >= 0),
    FOREIGN KEY (owner_id) REFERENCES users (id) MATCH FULL ON UPDATE NO ACTION
) PARTITION BY RANGE (created);
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">TABLE</span> <span class="hljs-keyword">IF</span> <span class="hljs-keyword">NOT</span> <span class="hljs-keyword">EXISTS</span> accounts (
    id <span class="hljs-type">bigserial</span> <span class="hljs-keyword">PRIMARY KEY</span>,
    email <span class="hljs-type">varchar</span>(<span class="hljs-number">255</span>) <span class="hljs-keyword">NOT</span> <span class="hljs-keyword">NULL</span> <span class="hljs-keyword">UNIQUE</span>,
    balance <span class="hljs-type">numeric</span>(<span class="hljs-number">12</span>, <span class="hljs-number">2</span>) <span class="hljs-keyword">DEFAULT</span> <span class="hljs-number">0.00</span>,
    created <span class="hljs-type">timestamp</span> <span class="hljs-type">with time zone</span> <span class="hljs-keyword">DEFAULT</span> <span class="hljs-built_in">CURRENT_TIMESTAMP</span>,
    updated <span class="hljs-type">timestamp</span> <span class="hljs-type">without time zone</span>,
    data <span class="hljs-type">jsonb</span>,
    tags <span class="hljs-type">text</span>[],
    owner_id <span class="hljs-type">integer</span> <span class="hljs-keyword">REFERENCES</span> users (id) <span class="hljs-keyword">ON</span> <span class="hljs-keyword">DELETE</span> <span class="hljs-keyword">CASCADE</span>,
    <span class="hljs-keyword">CONSTRAINT</span> positive <span class="hljs-keyword">CHECK</span> (balance &gt;= <span class="hljs-number">0</span>),
    <span class="hljs-keyword">FOREIGN KEY</span> (owner_id) <span class="hljs-keyword">REFERENCES</span> users (id) <span class="hljs-keyword">MATCH FULL</span> <span class="hljs-keyword">ON</span> <span class="hljs-keyword">UPDATE</span> <span class="hljs-keyword">NO ACTION</span>
) <span class="hljs-keyword">PARTITION BY RANGE</span> (created);
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("pgsql",
"""
SELECT 'it''s', E'tab\there\'', U&'d\0061t\+000061', e'x\\y', 'multi
line';
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-string">&#x27;it&#x27;&#x27;s&#x27;</span>, <span class="hljs-string">E&#x27;tab\there\&#x27;&#x27;</span>, <span class="hljs-string">U&amp;&#x27;d\0061t\+000061&#x27;</span>, <span class="hljs-string">e&#x27;x\\y&#x27;</span>, <span class="hljs-string">&#x27;multi
line&#x27;</span>;
""");
    }

    // The content of a dollar-quoted string is highlighted as PL/pgSQL, even for a plain string.
    [Fact]
    public void DollarSimple()
    {
        AssertHighlighter("pgsql",
"""
SELECT $$it's a string$$, $tag$ with $$ inside $tag$;
""",
"""
<span class="hljs-keyword">SELECT</span> $$<span class="language-pgsql">it<span class="hljs-string">&#x27;s a string</span></span>$$, $tag$<span class="language-pgsql"> <span class="hljs-keyword">with</span> $$<span class="language-pgsql"> inside </span></span>$tag$;
""");
    }

    [Fact]
    public void DollarEmpty()
    {
        AssertHighlighter("pgsql",
"""
SELECT $$$$, $a$$a$;
""",
"""
<span class="hljs-keyword">SELECT</span> $$$$, $a$$a$;
""");
    }

    [Fact]
    public void FunctionPlpgsqlAfter()
    {
        AssertHighlighter("pgsql",
"""
CREATE OR REPLACE FUNCTION increment(i integer) RETURNS integer AS $$
BEGIN
    RETURN i + 1;
END;
$$ LANGUAGE plpgsql;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">OR REPLACE</span> <span class="hljs-keyword">FUNCTION</span> <span class="hljs-keyword">increment</span>(i integer) RETURNS integer AS $$
BEGIN
    RETURN i + <span class="hljs-number">1</span>;
<span class="hljs-keyword">END</span>;
$$<span class="language-pgsql"> <span class="hljs-keyword">LANGUAGE</span> plpgsql;</span>
""");
    }

    [Fact]
    public void FunctionPlpgsqlBefore()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION add(a integer, b integer) RETURNS integer
LANGUAGE plpgsql
IMMUTABLE
AS $body$
DECLARE
    result integer := 0;
BEGIN
    result := a + b;
    RAISE NOTICE 'result is %', result;
    RETURN result;
EXCEPTION
    WHEN division_by_zero THEN
        RAISE WARNING 'oops';
        RETURN NULL;
END;
$body$;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> <span class="hljs-keyword">add</span>(a <span class="hljs-type">integer</span>, b <span class="hljs-type">integer</span>) <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">integer</span>
<span class="hljs-keyword">LANGUAGE</span> plpgsql
<span class="hljs-keyword">IMMUTABLE</span>
<span class="hljs-keyword">AS</span> $body$<span class="language-pgsql">
<span class="hljs-keyword">DECLARE</span>
    result <span class="hljs-type">integer</span> := <span class="hljs-number">0</span>;
<span class="hljs-keyword">BEGIN</span>
    result := a + b;
    <span class="hljs-keyword">RAISE</span> <span class="hljs-keyword">NOTICE</span> <span class="hljs-string">&#x27;result is %&#x27;</span>, result;
    <span class="hljs-keyword">RETURN</span> result;
<span class="hljs-keyword">EXCEPTION</span>
    <span class="hljs-keyword">WHEN</span> <span class="hljs-built_in">division_by_zero</span> <span class="hljs-keyword">THEN</span>
        <span class="hljs-keyword">RAISE</span> <span class="hljs-built_in">WARNING</span> <span class="hljs-string">&#x27;oops&#x27;</span>;
        <span class="hljs-keyword">RETURN</span> <span class="hljs-keyword">NULL</span>;
<span class="hljs-keyword">END</span>;
</span>$body$;
""");
    }

    [Fact]
    public void FunctionSql()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION one() RETURNS integer AS $$
    SELECT 1 AS result;
$$ LANGUAGE SQL;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> one() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">integer</span> <span class="hljs-keyword">AS</span> $$<span class="language-pgsql">
    <span class="hljs-keyword">SELECT</span> <span class="hljs-number">1</span> <span class="hljs-keyword">AS</span> result;
</span>$$ <span class="hljs-keyword">LANGUAGE</span> <span class="hljs-keyword">SQL</span>;
""");
    }

    [Fact]
    public void FunctionPlperl()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION perl_max (integer, integer) RETURNS integer AS $$
    my ($x, $y) = @_;
    if (not defined $x) {
        return undef if not defined $y;
        return $y;
    }
    return $x if $x > $y;
    return $y;
$$ LANGUAGE plperl;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> perl_max (<span class="hljs-type">integer</span>, <span class="hljs-type">integer</span>) <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">integer</span> <span class="hljs-keyword">AS</span> $$<span class="language-perl">
    <span class="hljs-keyword">my</span> (<span class="hljs-variable">$x</span>, <span class="hljs-variable">$y</span>) = <span class="hljs-variable">@_</span>;
    <span class="hljs-keyword">if</span> (<span class="hljs-keyword">not</span> <span class="hljs-keyword">defined</span> <span class="hljs-variable">$x</span>) {
        <span class="hljs-keyword">return</span> <span class="hljs-keyword">undef</span> <span class="hljs-keyword">if</span> <span class="hljs-keyword">not</span> <span class="hljs-keyword">defined</span> <span class="hljs-variable">$y</span>;
        <span class="hljs-keyword">return</span> <span class="hljs-variable">$y</span>;
    }
    <span class="hljs-keyword">return</span> <span class="hljs-variable">$x</span> <span class="hljs-keyword">if</span> <span class="hljs-variable">$x</span> &gt; <span class="hljs-variable">$y</span>;
    <span class="hljs-keyword">return</span> <span class="hljs-variable">$y</span>;
</span>$$ <span class="hljs-keyword">LANGUAGE</span> plperl;
""");
    }

    [Fact]
    public void FunctionPlpython()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION pymax (a integer, b integer)
  RETURNS integer
AS $$
  if a > b:
    return a
  return b
$$ LANGUAGE plpython3u;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> pymax (a <span class="hljs-type">integer</span>, b <span class="hljs-type">integer</span>)
  <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">integer</span>
<span class="hljs-keyword">AS</span> $$<span class="language-python">
  <span class="hljs-keyword">if</span> a &gt; b:
    <span class="hljs-keyword">return</span> a
  <span class="hljs-keyword">return</span> b
</span>$$ <span class="hljs-keyword">LANGUAGE</span> plpython3u;
""");
    }

    [Fact]
    public void FunctionPlpythonBefore()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION pymax (a integer, b integer) RETURNS integer LANGUAGE plpython3u AS $$
  import math
  return max(a, b)
$$;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> pymax (a <span class="hljs-type">integer</span>, b <span class="hljs-type">integer</span>) <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">integer</span> <span class="hljs-keyword">LANGUAGE</span> plpython3u <span class="hljs-keyword">AS</span> $$<span class="language-python">
  <span class="hljs-keyword">import</span> math
  <span class="hljs-keyword">return</span> <span class="hljs-built_in">max</span>(a, b)
</span>$$;
""");
    }

    // No Tcl grammar: the body is plain text.
    [Fact]
    public void FunctionPltcl()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION tcl_max(integer, integer) RETURNS integer AS $$
    if {$1 > $2} {return $1}
    return $2
$$ LANGUAGE pltcl STRICT;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> tcl_max(<span class="hljs-type">integer</span>, <span class="hljs-type">integer</span>) <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">integer</span> <span class="hljs-keyword">AS</span> $$
    if {$1 &gt; $2} {return $1}
    return $2
$$ <span class="hljs-keyword">LANGUAGE</span> pltcl <span class="hljs-keyword">STRICT</span>;
""");
    }

    // Deviation from highlight.js, which only auto-detects among the languages it lists.
    [Fact]
    public void FunctionPlv8()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION plv8_test(keys text[], vals text[]) RETURNS json AS $$
    var o = {};
    for (var i = 0; i < keys.length; i++) {
        o[keys[i]] = vals[i];
    }
    return o;
$$ LANGUAGE plv8 IMMUTABLE STRICT;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> plv8_test(keys <span class="hljs-type">text</span>[], vals <span class="hljs-type">text</span>[]) <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">json</span> <span class="hljs-keyword">AS</span> $$<span class="language-javascript">
    <span class="hljs-keyword">var</span> o = {};
    <span class="hljs-keyword">for</span> (<span class="hljs-keyword">var</span> i = <span class="hljs-number">0</span>; i &lt; keys.<span class="hljs-property">length</span>; i++) {
        o[keys[i]] = vals[i];
    }
    <span class="hljs-keyword">return</span> o;
</span>$$ <span class="hljs-keyword">LANGUAGE</span> plv8 <span class="hljs-keyword">IMMUTABLE</span> <span class="hljs-keyword">STRICT</span>;
""");
    }

    [Fact]
    public void FunctionPlr()
    {
        AssertHighlighter("pgsql",
"""
CREATE OR REPLACE FUNCTION r_max (integer, integer) RETURNS integer AS '
    if (arg1 > arg2)
       return(arg1)
    else
       return(arg2)
' LANGUAGE 'plr' STRICT;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">OR REPLACE</span> <span class="hljs-keyword">FUNCTION</span> r_max (<span class="hljs-type">integer</span>, <span class="hljs-type">integer</span>) <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">integer</span> <span class="hljs-keyword">AS</span> <span class="hljs-string">&#x27;
    if (arg1 &gt; arg2)
       return(arg1)
    else
       return(arg2)
&#x27;</span> <span class="hljs-keyword">LANGUAGE</span> <span class="hljs-string">&#x27;plr&#x27;</span> <span class="hljs-keyword">STRICT</span>;
""");
    }

    [Fact]
    public void FunctionPlsh()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION concat(text, text) RETURNS text AS $$
#!/bin/sh
echo "$1$2"
$$ LANGUAGE plsh;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> concat(<span class="hljs-type">text</span>, <span class="hljs-type">text</span>) <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">text</span> <span class="hljs-keyword">AS</span> $$<span class="language-bash">
<span class="hljs-comment">#!/bin/sh</span>
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;$1<span class="hljs-variable">$2</span>&quot;</span>
</span>$$ <span class="hljs-keyword">LANGUAGE</span> plsh;
""");
    }

    [Fact]
    public void DoBlock()
    {
        AssertHighlighter("pgsql",
"""
DO $$
DECLARE
    r record;
BEGIN
    FOR r IN SELECT table_schema, table_name FROM information_schema.tables
             WHERE table_type = 'VIEW' AND table_schema = 'public'
    LOOP
        EXECUTE 'GRANT ALL ON ' || quote_ident(r.table_schema) || '.' || quote_ident(r.table_name) || ' TO webuser';
    END LOOP;
END$$;
""",
"""
<span class="hljs-keyword">DO</span> $$<span class="language-pgsql">
<span class="hljs-keyword">DECLARE</span>
    r <span class="hljs-type">record</span>;
<span class="hljs-keyword">BEGIN</span>
    <span class="hljs-keyword">FOR</span> r <span class="hljs-keyword">IN</span> <span class="hljs-keyword">SELECT</span> table_schema, <span class="hljs-built_in">table_name</span> <span class="hljs-keyword">FROM</span> information_schema.<span class="hljs-keyword">tables</span>
             <span class="hljs-keyword">WHERE</span> table_type = <span class="hljs-string">&#x27;VIEW&#x27;</span> <span class="hljs-keyword">AND</span> table_schema = <span class="hljs-string">&#x27;public&#x27;</span>
    <span class="hljs-keyword">LOOP</span>
        <span class="hljs-keyword">EXECUTE</span> <span class="hljs-string">&#x27;GRANT ALL ON &#x27;</span> || quote_ident(r.table_schema) || <span class="hljs-string">&#x27;.&#x27;</span> || quote_ident(r.<span class="hljs-built_in">table_name</span>) || <span class="hljs-string">&#x27; TO webuser&#x27;</span>;
    <span class="hljs-keyword">END</span> <span class="hljs-keyword">LOOP</span>;
<span class="hljs-keyword">END</span></span>$$;
""");
    }

    [Fact]
    public void DoLanguage()
    {
        AssertHighlighter("pgsql",
"""
DO LANGUAGE plperl $$
  elog(NOTICE, "hello");
$$;
""",
"""
<span class="hljs-keyword">DO</span> <span class="hljs-keyword">LANGUAGE</span> plperl $$<span class="language-perl">
  elog(NOTICE, <span class="hljs-string">&quot;hello&quot;</span>);
</span>$$;
""");
    }

    [Fact]
    public void Trigger()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION emp_stamp() RETURNS trigger AS $emp_stamp$
    BEGIN
        IF NEW.empname IS NULL THEN
            RAISE EXCEPTION 'empname cannot be null';
        END IF;
        IF NEW.salary < 0 THEN
            RAISE EXCEPTION '% cannot have a negative salary', NEW.empname;
        END IF;
        NEW.last_date := current_timestamp;
        NEW.last_user := current_user;
        RETURN NEW;
    END;
$emp_stamp$ LANGUAGE plpgsql;

CREATE TRIGGER emp_stamp BEFORE INSERT OR UPDATE ON emp
    FOR EACH ROW EXECUTE FUNCTION emp_stamp();
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> emp_stamp() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">trigger</span> <span class="hljs-keyword">AS</span> $emp_stamp$<span class="language-pgsql">
    <span class="hljs-keyword">BEGIN</span>
        <span class="hljs-keyword">IF</span> <span class="hljs-built_in">NEW</span>.empname <span class="hljs-keyword">IS</span> <span class="hljs-keyword">NULL</span> <span class="hljs-keyword">THEN</span>
            <span class="hljs-keyword">RAISE</span> <span class="hljs-keyword">EXCEPTION</span> <span class="hljs-string">&#x27;empname cannot be null&#x27;</span>;
        <span class="hljs-keyword">END</span> <span class="hljs-keyword">IF</span>;
        <span class="hljs-keyword">IF</span> <span class="hljs-built_in">NEW</span>.salary &lt; <span class="hljs-number">0</span> <span class="hljs-keyword">THEN</span>
            <span class="hljs-keyword">RAISE</span> <span class="hljs-keyword">EXCEPTION</span> <span class="hljs-string">&#x27;% cannot have a negative salary&#x27;</span>, <span class="hljs-built_in">NEW</span>.empname;
        <span class="hljs-keyword">END</span> <span class="hljs-keyword">IF</span>;
        <span class="hljs-built_in">NEW</span>.last_date := <span class="hljs-built_in">current_timestamp</span>;
        <span class="hljs-built_in">NEW</span>.last_user := <span class="hljs-built_in">current_user</span>;
        <span class="hljs-keyword">RETURN</span> <span class="hljs-built_in">NEW</span>;
    <span class="hljs-keyword">END</span>;
</span>$emp_stamp$ <span class="hljs-keyword">LANGUAGE</span> plpgsql;

<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">TRIGGER</span> emp_stamp <span class="hljs-keyword">BEFORE</span> <span class="hljs-keyword">INSERT</span> <span class="hljs-keyword">OR</span> <span class="hljs-keyword">UPDATE</span> <span class="hljs-keyword">ON</span> emp
    <span class="hljs-keyword">FOR</span> <span class="hljs-keyword">EACH</span> <span class="hljs-keyword">ROW</span> <span class="hljs-keyword">EXECUTE</span> <span class="hljs-keyword">FUNCTION</span> emp_stamp();
""");
    }

    [Fact]
    public void PlpgsqlFeatures()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION f(p_id int) RETURNS SETOF record AS $$
<<outer>>
DECLARE
    v_row users%ROWTYPE;
    v_name users.name%TYPE;
    c CURSOR FOR SELECT * FROM users;
BEGIN
    #variable_conflict use_column
    PERFORM pg_sleep(1);
    GET DIAGNOSTICS v_count = ROW_COUNT;
    FOREACH x IN ARRAY $1 LOOP
        CONTINUE WHEN x IS NULL;
        EXIT outer WHEN x > 10;
    END LOOP;
    WHILE i < 10 LOOP i := i + 1; END LOOP;
    RETURN QUERY SELECT * FROM users WHERE id = $1;
    RETURN NEXT v_row;
    ASSERT FOUND, 'not found';
    IF SQLSTATE = '23505' THEN RAISE unique_violation USING MESSAGE = SQLERRM; END IF;
END outer;
$$ LANGUAGE plpgsql;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> f(p_id <span class="hljs-type">int</span>) <span class="hljs-keyword">RETURNS</span> <span class="hljs-keyword">SETOF</span> <span class="hljs-type">record</span> <span class="hljs-keyword">AS</span> $$<span class="language-pgsql">
<span class="hljs-symbol">&lt;&lt;outer&gt;&gt;</span>
<span class="hljs-keyword">DECLARE</span>
    v_row users<span class="hljs-meta">%ROWTYPE</span>;
    v_name users.name<span class="hljs-meta">%TYPE</span>;
    c <span class="hljs-keyword">CURSOR</span> <span class="hljs-keyword">FOR</span> <span class="hljs-keyword">SELECT</span> * <span class="hljs-keyword">FROM</span> users;
<span class="hljs-keyword">BEGIN</span>
    #variable_conflict use_column
    <span class="hljs-keyword">PERFORM</span> pg_sleep(<span class="hljs-number">1</span>);
    <span class="hljs-keyword">GET</span> <span class="hljs-keyword">DIAGNOSTICS</span> v_count = <span class="hljs-built_in">ROW_COUNT</span>;
    <span class="hljs-keyword">FOREACH</span> x <span class="hljs-keyword">IN</span> <span class="hljs-keyword">ARRAY</span> <span class="hljs-meta">$1</span> <span class="hljs-keyword">LOOP</span>
        <span class="hljs-keyword">CONTINUE</span> <span class="hljs-keyword">WHEN</span> x <span class="hljs-keyword">IS</span> <span class="hljs-keyword">NULL</span>;
        <span class="hljs-keyword">EXIT</span> <span class="hljs-keyword">outer</span> <span class="hljs-keyword">WHEN</span> x &gt; <span class="hljs-number">10</span>;
    <span class="hljs-keyword">END</span> <span class="hljs-keyword">LOOP</span>;
    <span class="hljs-keyword">WHILE</span> i &lt; <span class="hljs-number">10</span> <span class="hljs-keyword">LOOP</span> i := i + <span class="hljs-number">1</span>; <span class="hljs-keyword">END</span> <span class="hljs-keyword">LOOP</span>;
    <span class="hljs-keyword">RETURN QUERY</span> <span class="hljs-keyword">SELECT</span> * <span class="hljs-keyword">FROM</span> users <span class="hljs-keyword">WHERE</span> id = <span class="hljs-meta">$1</span>;
    <span class="hljs-keyword">RETURN NEXT</span> v_row;
    <span class="hljs-keyword">ASSERT</span> <span class="hljs-built_in">FOUND</span>, <span class="hljs-string">&#x27;not found&#x27;</span>;
    <span class="hljs-keyword">IF</span> <span class="hljs-built_in">SQLSTATE</span> = <span class="hljs-string">&#x27;23505&#x27;</span> <span class="hljs-keyword">THEN</span> <span class="hljs-keyword">RAISE</span> <span class="hljs-built_in">unique_violation</span> <span class="hljs-keyword">USING</span> MESSAGE = <span class="hljs-built_in">SQLERRM</span>; <span class="hljs-keyword">END</span> <span class="hljs-keyword">IF</span>;
<span class="hljs-keyword">END</span> <span class="hljs-keyword">outer</span>;
</span>$$ <span class="hljs-keyword">LANGUAGE</span> plpgsql;
""");
    }

    // Unlike highlight.js, which has an extra comment rule for English prose, the doctag is highlighted.
    [Fact]
    public void Comments()
    {
        AssertHighlighter("pgsql",
"""
-- line comment TODO: fix
SELECT 1; /* block
comment */ SELECT 2 -- trailing
/* nested /* not */ supported */
""",
"""
<span class="hljs-comment">-- line comment <span class="hljs-doctag">TODO:</span> fix</span>
<span class="hljs-keyword">SELECT</span> <span class="hljs-number">1</span>; <span class="hljs-comment">/* block
comment */</span> <span class="hljs-keyword">SELECT</span> <span class="hljs-number">2</span> <span class="hljs-comment">-- trailing</span>
<span class="hljs-comment">/* nested /* not */</span> supported */
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("pgsql",
"""
SELECT 42, 3.14, .5, 1e10, 2.5E-3, 0x1F, -7, x-1;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-number">42</span>, <span class="hljs-number">3.14</span>, <span class="hljs-number">.5</span>, <span class="hljs-number">1e10</span>, <span class="hljs-number">2.5E-3</span>, <span class="hljs-number">0x1F</span>, <span class="hljs-number">-7</span>, x<span class="hljs-number">-1</span>;
""");
    }

    [Fact]
    public void Types()
    {
        AssertHighlighter("pgsql",
"""
SELECT '1'::int4, '1'::bigint, 'a'::varchar(10), '{}'::jsonb, now()::timestamptz,
  '1 day'::interval day to second, interval hour, cast(x AS double precision), pg_class.oid, t.text, 'x'::text;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-string">&#x27;1&#x27;</span>::<span class="hljs-type">int4</span>, <span class="hljs-string">&#x27;1&#x27;</span>::<span class="hljs-type">bigint</span>, <span class="hljs-string">&#x27;a&#x27;</span>::<span class="hljs-type">varchar</span>(<span class="hljs-number">10</span>), <span class="hljs-string">&#x27;{}&#x27;</span>::<span class="hljs-type">jsonb</span>, now()::<span class="hljs-type">timestamptz</span>,
  <span class="hljs-string">&#x27;1 day&#x27;</span>::<span class="hljs-type">interval day to second</span>, <span class="hljs-type">interval hour</span>, cast(x <span class="hljs-keyword">AS</span> <span class="hljs-type">double</span> <span class="hljs-type">precision</span>), pg_class.oid, t.text, <span class="hljs-string">&#x27;x&#x27;</span>::<span class="hljs-type">text</span>;
""");
    }

    [Fact]
    public void Extract()
    {
        AssertHighlighter("pgsql",
"""
SELECT extract(year FROM ts), EXTRACT(EPOCH FROM now()), date_part('day', ts);
""",
"""
<span class="hljs-keyword">SELECT</span> extract(<span class="hljs-type">year</span> <span class="hljs-keyword">FROM</span> ts), EXTRACT(<span class="hljs-type">EPOCH</span> <span class="hljs-keyword">FROM</span> now()), date_part(<span class="hljs-string">&#x27;day&#x27;</span>, ts);
""");
    }

    [Fact]
    public void Sequences()
    {
        AssertHighlighter("pgsql",
"""
CREATE SEQUENCE serial START 101 INCREMENT BY 2 MINVALUE 1 MAXVALUE 1000 CACHE 20 NO CYCLE;
ALTER SEQUENCE s NO MAXVALUE NO MINVALUE;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">SEQUENCE</span> <span class="hljs-type">serial</span> <span class="hljs-keyword">START</span> <span class="hljs-number">101</span> <span class="hljs-keyword">INCREMENT</span> <span class="hljs-keyword">BY</span> <span class="hljs-number">2</span> <span class="hljs-keyword">MINVALUE</span> <span class="hljs-number">1</span> <span class="hljs-keyword">MAXVALUE</span> <span class="hljs-number">1000</span> <span class="hljs-keyword">CACHE</span> <span class="hljs-number">20</span> <span class="hljs-keyword">NO</span> <span class="hljs-keyword">CYCLE</span>;
<span class="hljs-keyword">ALTER</span> <span class="hljs-keyword">SEQUENCE</span> s <span class="hljs-keyword">NO MAXVALUE</span> <span class="hljs-keyword">NO MINVALUE</span>;
""");
    }

    [Fact]
    public void Window()
    {
        AssertHighlighter("pgsql",
"""
SELECT depname, empno, salary,
       rank() OVER (PARTITION BY depname ORDER BY salary DESC),
       sum(salary) OVER w,
       avg(salary) OVER (ORDER BY salary RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)
FROM empsalary
WINDOW w AS (PARTITION BY depname ORDER BY salary DESC ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING EXCLUDE TIES);
""",
"""
<span class="hljs-keyword">SELECT</span> depname, empno, salary,
       rank() <span class="hljs-keyword">OVER</span> (<span class="hljs-keyword">PARTITION</span> <span class="hljs-keyword">BY</span> depname <span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> salary <span class="hljs-keyword">DESC</span>),
       sum(salary) <span class="hljs-keyword">OVER</span> w,
       avg(salary) <span class="hljs-keyword">OVER</span> (<span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> salary <span class="hljs-keyword">RANGE</span> <span class="hljs-keyword">BETWEEN</span> <span class="hljs-keyword">UNBOUNDED</span> <span class="hljs-keyword">PRECEDING</span> <span class="hljs-keyword">AND</span> <span class="hljs-keyword">CURRENT</span> <span class="hljs-keyword">ROW</span>)
<span class="hljs-keyword">FROM</span> empsalary
<span class="hljs-keyword">WINDOW</span> w <span class="hljs-keyword">AS</span> (<span class="hljs-keyword">PARTITION</span> <span class="hljs-keyword">BY</span> depname <span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> salary <span class="hljs-keyword">DESC</span> <span class="hljs-keyword">ROWS</span> <span class="hljs-keyword">BETWEEN</span> <span class="hljs-number">1</span> <span class="hljs-keyword">PRECEDING</span> <span class="hljs-keyword">AND</span> <span class="hljs-number">1</span> <span class="hljs-keyword">FOLLOWING</span> <span class="hljs-keyword">EXCLUDE TIES</span>);
""");
    }

    [Fact]
    public void Cte()
    {
        AssertHighlighter("pgsql",
"""
WITH RECURSIVE t(n) AS (
    VALUES (1)
  UNION ALL
    SELECT n+1 FROM t WHERE n < 100
)
SELECT sum(n) FROM t;
""",
"""
<span class="hljs-keyword">WITH</span> <span class="hljs-keyword">RECURSIVE</span> t(n) <span class="hljs-keyword">AS</span> (
    <span class="hljs-keyword">VALUES</span> (<span class="hljs-number">1</span>)
  <span class="hljs-keyword">UNION</span> <span class="hljs-keyword">ALL</span>
    <span class="hljs-keyword">SELECT</span> n+<span class="hljs-number">1</span> <span class="hljs-keyword">FROM</span> t <span class="hljs-keyword">WHERE</span> n &lt; <span class="hljs-number">100</span>
)
<span class="hljs-keyword">SELECT</span> sum(n) <span class="hljs-keyword">FROM</span> t;
""");
    }

    [Fact]
    public void Upsert()
    {
        AssertHighlighter("pgsql",
"""
INSERT INTO distributors (did, dname) VALUES (5, 'Gizmo Transglobal'), (6, 'Associated Computing, Inc')
    ON CONFLICT (did) DO UPDATE SET dname = EXCLUDED.dname RETURNING *;
""",
"""
<span class="hljs-keyword">INSERT</span> <span class="hljs-keyword">INTO</span> distributors (did, dname) <span class="hljs-keyword">VALUES</span> (<span class="hljs-number">5</span>, <span class="hljs-string">&#x27;Gizmo Transglobal&#x27;</span>), (<span class="hljs-number">6</span>, <span class="hljs-string">&#x27;Associated Computing, Inc&#x27;</span>)
    <span class="hljs-keyword">ON</span> <span class="hljs-keyword">CONFLICT</span> (did) <span class="hljs-keyword">DO</span> <span class="hljs-keyword">UPDATE</span> <span class="hljs-keyword">SET</span> dname = EXCLUDED.dname <span class="hljs-keyword">RETURNING</span> *;
""");
    }

    [Fact]
    public void Grant()
    {
        AssertHighlighter("pgsql",
"""
GRANT SELECT, INSERT ON ALL TABLES IN SCHEMA public TO app_user;
REVOKE ALL PRIVILEGES ON DATABASE mydb FROM PUBLIC;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE ON SEQUENCES TO app_user;
CREATE ROLE admin WITH LOGIN SUPERUSER CREATEDB NOINHERIT PASSWORD 'secret' VALID UNTIL 'infinity';
""",
"""
<span class="hljs-keyword">GRANT</span> <span class="hljs-keyword">SELECT</span>, <span class="hljs-keyword">INSERT</span> <span class="hljs-keyword">ON</span> <span class="hljs-keyword">ALL</span> <span class="hljs-keyword">TABLES</span> <span class="hljs-keyword">IN</span> <span class="hljs-keyword">SCHEMA</span> <span class="hljs-built_in">public</span> <span class="hljs-keyword">TO</span> app_user;
<span class="hljs-keyword">REVOKE</span> <span class="hljs-keyword">ALL</span> <span class="hljs-keyword">PRIVILEGES</span> <span class="hljs-keyword">ON</span> <span class="hljs-keyword">DATABASE</span> mydb <span class="hljs-keyword">FROM</span> <span class="hljs-built_in">PUBLIC</span>;
<span class="hljs-keyword">ALTER</span> <span class="hljs-keyword">DEFAULT</span> <span class="hljs-keyword">PRIVILEGES</span> <span class="hljs-keyword">IN</span> <span class="hljs-keyword">SCHEMA</span> <span class="hljs-built_in">public</span> <span class="hljs-keyword">GRANT</span> <span class="hljs-keyword">USAGE</span> <span class="hljs-keyword">ON</span> <span class="hljs-keyword">SEQUENCES</span> <span class="hljs-keyword">TO</span> app_user;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">ROLE</span> <span class="hljs-keyword">admin</span> <span class="hljs-keyword">WITH</span> <span class="hljs-keyword">LOGIN</span> <span class="hljs-keyword">SUPERUSER</span> <span class="hljs-keyword">CREATEDB</span> <span class="hljs-keyword">NOINHERIT</span> <span class="hljs-keyword">PASSWORD</span> <span class="hljs-string">&#x27;secret&#x27;</span> <span class="hljs-keyword">VALID</span> <span class="hljs-keyword">UNTIL</span> <span class="hljs-string">&#x27;infinity&#x27;</span>;
""");
    }

    [Fact]
    public void Copy()
    {
        AssertHighlighter("pgsql",
"""
COPY country TO STDOUT (DELIMITER '|');
COPY country FROM '/usr1/proj/bray/sql/country_data' WITH (FORMAT csv, HEADER true);
\copy country FROM PROGRAM 'gunzip < /tmp/data.gz'
""",
"""
<span class="hljs-keyword">COPY</span> country <span class="hljs-keyword">TO STDOUT</span> (<span class="hljs-keyword">DELIMITER</span> <span class="hljs-string">&#x27;|&#x27;</span>);
<span class="hljs-keyword">COPY</span> country <span class="hljs-keyword">FROM</span> <span class="hljs-string">&#x27;/usr1/proj/bray/sql/country_data&#x27;</span> <span class="hljs-keyword">WITH</span> (<span class="hljs-keyword">FORMAT</span> csv, <span class="hljs-keyword">HEADER</span> <span class="hljs-keyword">true</span>);
\<span class="hljs-keyword">copy</span> country <span class="hljs-keyword">FROM PROGRAM</span> <span class="hljs-string">&#x27;gunzip &lt; /tmp/data.gz&#x27;</span>
""");
    }

    [Fact]
    public void Index()
    {
        AssertHighlighter("pgsql",
"""
CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS idx_name ON films USING btree (title COLLATE "C" DESC NULLS FIRST) INCLUDE (director) WHERE active;
CREATE INDEX ON t USING gin (to_tsvector('english', body));
REINDEX TABLE my_table;
VACUUM (VERBOSE, ANALYZE) onek;
EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) SELECT * FROM t;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">UNIQUE</span> <span class="hljs-keyword">INDEX</span> <span class="hljs-keyword">CONCURRENTLY</span> <span class="hljs-keyword">IF</span> <span class="hljs-keyword">NOT</span> <span class="hljs-keyword">EXISTS</span> idx_name <span class="hljs-keyword">ON</span> films <span class="hljs-keyword">USING</span> btree (title <span class="hljs-keyword">COLLATE</span> &quot;C&quot; <span class="hljs-keyword">DESC</span> <span class="hljs-keyword">NULLS FIRST</span>) <span class="hljs-keyword">INCLUDE</span> (director) <span class="hljs-keyword">WHERE</span> active;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">INDEX</span> <span class="hljs-keyword">ON</span> t <span class="hljs-keyword">USING</span> gin (to_tsvector(<span class="hljs-string">&#x27;english&#x27;</span>, body));
<span class="hljs-keyword">REINDEX</span> <span class="hljs-keyword">TABLE</span> my_table;
<span class="hljs-keyword">VACUUM</span> (<span class="hljs-keyword">VERBOSE</span>, <span class="hljs-keyword">ANALYZE</span>) onek;
<span class="hljs-keyword">EXPLAIN</span> (<span class="hljs-keyword">ANALYZE</span>, <span class="hljs-keyword">BUFFERS</span>, <span class="hljs-keyword">FORMAT JSON</span>) <span class="hljs-keyword">SELECT</span> * <span class="hljs-keyword">FROM</span> t;
""");
    }

    [Fact]
    public void QuotedIdentifiers()
    {
        AssertHighlighter("pgsql",
"""
SELECT "Select", "a""b", "from" FROM "Table" AS "t";
""",
"""
<span class="hljs-keyword">SELECT</span> &quot;Select&quot;, &quot;a&quot;&quot;b&quot;, &quot;from&quot; <span class="hljs-keyword">FROM</span> &quot;Table&quot; <span class="hljs-keyword">AS</span> &quot;t&quot;;
""");
    }

    [Fact]
    public void JsonOps()
    {
        AssertHighlighter("pgsql",
"""
SELECT data->>'name', data->'tags'->0, data #>> '{a,b}', data @> '{"a":1}'::jsonb, jsonb_build_object('a', 1), json_agg(x) FILTER (WHERE x > 0)
FROM t WHERE data ? 'key';
""",
"""
<span class="hljs-keyword">SELECT</span> data-&gt;&gt;<span class="hljs-string">&#x27;name&#x27;</span>, data-&gt;<span class="hljs-string">&#x27;tags&#x27;</span>-&gt;<span class="hljs-number">0</span>, data #&gt;&gt; <span class="hljs-string">&#x27;{a,b}&#x27;</span>, data @&gt; <span class="hljs-string">&#x27;{&quot;a&quot;:1}&#x27;</span>::<span class="hljs-type">jsonb</span>, jsonb_build_object(<span class="hljs-string">&#x27;a&#x27;</span>, <span class="hljs-number">1</span>), json_agg(x) <span class="hljs-keyword">FILTER</span> (<span class="hljs-keyword">WHERE</span> x &gt; <span class="hljs-number">0</span>)
<span class="hljs-keyword">FROM</span> t <span class="hljs-keyword">WHERE</span> data ? <span class="hljs-string">&#x27;key&#x27;</span>;
""");
    }

    [Fact]
    public void Arrays()
    {
        AssertHighlighter("pgsql",
"""
SELECT ARRAY[1,2,3], a[1:2], array_agg(DISTINCT x ORDER BY x), unnest(ARRAY['a','b']), '{1,2}'::int[];
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-keyword">ARRAY</span>[<span class="hljs-number">1</span>,<span class="hljs-number">2</span>,<span class="hljs-number">3</span>], a[<span class="hljs-number">1</span>:<span class="hljs-number">2</span>], array_agg(<span class="hljs-keyword">DISTINCT</span> x <span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> x), unnest(<span class="hljs-keyword">ARRAY</span>[<span class="hljs-string">&#x27;a&#x27;</span>,<span class="hljs-string">&#x27;b&#x27;</span>]), <span class="hljs-string">&#x27;{1,2}&#x27;</span>::<span class="hljs-type">int</span>[];
""");
    }

    [Fact]
    public void Transaction()
    {
        AssertHighlighter("pgsql",
"""
BEGIN ISOLATION LEVEL SERIALIZABLE READ ONLY;
SAVEPOINT sp1;
ROLLBACK TO SAVEPOINT sp1;
SET LOCAL statement_timeout = '5s';
SET SESSION NAMES 'utf8';
LOCK TABLE films IN SHARE ROW EXCLUSIVE MODE NOWAIT;
SELECT * FROM t FOR UPDATE SKIP LOCKED;
COMMIT;
""",
"""
<span class="hljs-keyword">BEGIN</span> <span class="hljs-keyword">ISOLATION</span> <span class="hljs-keyword">LEVEL</span> <span class="hljs-keyword">SERIALIZABLE</span> <span class="hljs-keyword">READ</span> <span class="hljs-keyword">ONLY</span>;
<span class="hljs-keyword">SAVEPOINT</span> sp1;
<span class="hljs-keyword">ROLLBACK</span> <span class="hljs-keyword">TO</span> <span class="hljs-keyword">SAVEPOINT</span> sp1;
<span class="hljs-keyword">SET</span> <span class="hljs-keyword">LOCAL</span> statement_timeout = <span class="hljs-string">&#x27;5s&#x27;</span>;
<span class="hljs-keyword">SET SESSION NAMES</span> <span class="hljs-string">&#x27;utf8&#x27;</span>;
<span class="hljs-keyword">LOCK</span> <span class="hljs-keyword">TABLE</span> films <span class="hljs-keyword">IN</span> <span class="hljs-keyword">SHARE</span> <span class="hljs-keyword">ROW</span> <span class="hljs-keyword">EXCLUSIVE MODE</span> <span class="hljs-keyword">NOWAIT</span>;
<span class="hljs-keyword">SELECT</span> * <span class="hljs-keyword">FROM</span> t <span class="hljs-keyword">FOR</span> <span class="hljs-keyword">UPDATE</span> <span class="hljs-keyword">SKIP LOCKED</span>;
<span class="hljs-keyword">COMMIT</span>;
""");
    }

    [Fact]
    public void Xml()
    {
        AssertHighlighter("pgsql",
"""
SELECT xmlelement(name foo, xmlattributes('xyz' as bar), 'content'), xmlparse(document '<a/>'), xmlserialize(content x AS text),
xpath('/a/text()', x), XMLTABLE('/rows/row' PASSING data COLUMNS id int PATH '@id', name text PATH 'name');
""",
"""
<span class="hljs-keyword">SELECT</span> xmlelement(<span class="hljs-keyword">name</span> foo, xmlattributes(<span class="hljs-string">&#x27;xyz&#x27;</span> <span class="hljs-keyword">as</span> bar), <span class="hljs-string">&#x27;content&#x27;</span>), xmlparse(<span class="hljs-keyword">document</span> <span class="hljs-string">&#x27;&lt;a/&gt;&#x27;</span>), xmlserialize(<span class="hljs-keyword">content</span> x <span class="hljs-keyword">AS</span> <span class="hljs-type">text</span>),
xpath(<span class="hljs-string">&#x27;/a/text()&#x27;</span>, x), XMLTABLE(<span class="hljs-string">&#x27;/rows/row&#x27;</span> <span class="hljs-keyword">PASSING</span> data <span class="hljs-keyword">COLUMNS</span> id <span class="hljs-type">int</span> <span class="hljs-keyword">PATH</span> <span class="hljs-string">&#x27;@id&#x27;</span>, <span class="hljs-type">name</span> <span class="hljs-type">text</span> <span class="hljs-keyword">PATH</span> <span class="hljs-string">&#x27;name&#x27;</span>);
""");
    }

    [Fact]
    public void DollarXmlJson()
    {
        AssertHighlighter("pgsql",
"""
SELECT $$<book><title>Manual</title></book>$$::xml, $j${"a": [1, 2, {"b": null}]}$j$::jsonb, $$[1,2]$$::json;
""",
"""
<span class="hljs-keyword">SELECT</span> $$<span class="language-xml"><span class="hljs-tag">&lt;<span class="hljs-name">book</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">title</span>&gt;</span>Manual<span class="hljs-tag">&lt;/<span class="hljs-name">title</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">book</span>&gt;</span></span>$$::<span class="hljs-type">xml</span>, $j$<span class="language-json"><span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;a&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">[</span><span class="hljs-number">1</span><span class="hljs-punctuation">,</span> <span class="hljs-number">2</span><span class="hljs-punctuation">,</span> <span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;b&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-literal"><span class="hljs-keyword">null</span></span><span class="hljs-punctuation">}</span><span class="hljs-punctuation">]</span><span class="hljs-punctuation">}</span></span>$j$::<span class="hljs-type">jsonb</span>, $$<span class="language-json"><span class="hljs-punctuation">[</span><span class="hljs-number">1</span><span class="hljs-punctuation">,</span><span class="hljs-number">2</span><span class="hljs-punctuation">]</span></span>$$::<span class="hljs-type">json</span>;
""");
    }

    [Fact]
    public void PgFunctions()
    {
        AssertHighlighter("pgsql",
"""
SELECT pg_size_pretty(pg_total_relation_size('t')), has_table_privilege('u', 't', 'select'), pg_catalog.pg_get_userbyid(1), version(), format('%s', x), current_setting('search_path');
""",
"""
<span class="hljs-keyword">SELECT</span> pg_size_pretty(pg_total_relation_size(<span class="hljs-string">&#x27;t&#x27;</span>)), has_table_privilege(<span class="hljs-string">&#x27;u&#x27;</span>, <span class="hljs-string">&#x27;t&#x27;</span>, <span class="hljs-string">&#x27;select&#x27;</span>), pg_catalog.pg_get_userbyid(<span class="hljs-number">1</span>), version(), format(<span class="hljs-string">&#x27;%s&#x27;</span>, x), current_setting(<span class="hljs-string">&#x27;search_path&#x27;</span>);
""");
    }

    [Fact]
    public void Alter()
    {
        AssertHighlighter("pgsql",
"""
ALTER TABLE ONLY measurement ATTACH PARTITION measurement_y2016m07 FOR VALUES FROM ('2016-07-01') TO ('2016-08-01');
ALTER TABLE t ALTER COLUMN c SET DATA TYPE text, ALTER COLUMN d SET STORAGE EXTERNAL, ENABLE ROW LEVEL SECURITY, FORCE ROW LEVEL SECURITY;
ALTER TABLE distributors RENAME COLUMN address TO city;
""",
"""
<span class="hljs-keyword">ALTER</span> <span class="hljs-keyword">TABLE</span> <span class="hljs-keyword">ONLY</span> measurement <span class="hljs-keyword">ATTACH PARTITION</span> measurement_y2016m07 <span class="hljs-keyword">FOR</span> <span class="hljs-keyword">VALUES</span> <span class="hljs-keyword">FROM</span> (<span class="hljs-string">&#x27;2016-07-01&#x27;</span>) <span class="hljs-keyword">TO</span> (<span class="hljs-string">&#x27;2016-08-01&#x27;</span>);
<span class="hljs-keyword">ALTER</span> <span class="hljs-keyword">TABLE</span> t <span class="hljs-keyword">ALTER</span> <span class="hljs-keyword">COLUMN</span> c <span class="hljs-keyword">SET DATA</span> <span class="hljs-keyword">TYPE</span> <span class="hljs-type">text</span>, <span class="hljs-keyword">ALTER</span> <span class="hljs-keyword">COLUMN</span> d <span class="hljs-keyword">SET</span> <span class="hljs-keyword">STORAGE EXTERNAL</span>, <span class="hljs-keyword">ENABLE</span> <span class="hljs-keyword">ROW</span> <span class="hljs-keyword">LEVEL</span> <span class="hljs-keyword">SECURITY</span>, <span class="hljs-keyword">FORCE ROW LEVEL SECURITY</span>;
<span class="hljs-keyword">ALTER</span> <span class="hljs-keyword">TABLE</span> distributors <span class="hljs-keyword">RENAME</span> <span class="hljs-keyword">COLUMN</span> address <span class="hljs-keyword">TO</span> city;
""");
    }

    [Fact]
    public void CreateType()
    {
        AssertHighlighter("pgsql",
"""
CREATE TYPE mood AS ENUM ('sad', 'ok', 'happy');
CREATE TYPE complex AS (r double precision, i double precision);
CREATE TYPE box (INTERNALLENGTH = 16, INPUT = my_box_in_function, OUTPUT = my_box_out_function);
CREATE DOMAIN us_postal_code AS TEXT CHECK(VALUE ~ '^\d{5}$');
CREATE CAST (bigint AS int4) WITH FUNCTION int4(bigint) AS ASSIGNMENT;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">TYPE</span> mood <span class="hljs-keyword">AS ENUM</span> (<span class="hljs-string">&#x27;sad&#x27;</span>, <span class="hljs-string">&#x27;ok&#x27;</span>, <span class="hljs-string">&#x27;happy&#x27;</span>);
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">TYPE</span> complex <span class="hljs-keyword">AS</span> (r <span class="hljs-type">double</span> <span class="hljs-type">precision</span>, i <span class="hljs-type">double</span> <span class="hljs-type">precision</span>);
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">TYPE</span> box (INTERNALLENGTH = <span class="hljs-number">16</span>, INPUT = my_box_in_function, OUTPUT = my_box_out_function);
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">DOMAIN</span> us_postal_code <span class="hljs-keyword">AS</span> <span class="hljs-type">TEXT</span> <span class="hljs-keyword">CHECK</span>(<span class="hljs-keyword">VALUE</span> ~ <span class="hljs-string">&#x27;^\d{5}$&#x27;</span>);
<span class="hljs-keyword">CREATE</span> CAST (<span class="hljs-type">bigint</span> <span class="hljs-keyword">AS</span> <span class="hljs-type">int4</span>) <span class="hljs-keyword">WITH</span> <span class="hljs-keyword">FUNCTION</span> <span class="hljs-type">int4</span>(<span class="hljs-type">bigint</span>) <span class="hljs-keyword">AS ASSIGNMENT</span>;
""");
    }

    [Fact]
    public void CreateAggregate()
    {
        AssertHighlighter("pgsql",
"""
CREATE AGGREGATE sum (complex) (sfunc = complex_add, stype = complex, initcond = '(0,0)');
CREATE COLLATION german (provider = libc, locale = 'de_DE');
CREATE DATABASE music OWNER = hal TEMPLATE = template0 ENCODING = 'UTF8' CONNECTION LIMIT = 10;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">AGGREGATE</span> sum (complex) (sfunc = complex_add, stype = complex, initcond = <span class="hljs-string">&#x27;(0,0)&#x27;</span>);
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">COLLATION</span> german (provider = libc, locale = <span class="hljs-string">&#x27;de_DE&#x27;</span>);
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">DATABASE</span> music OWNER = hal TEMPLATE = template0 ENCODING = <span class="hljs-string">&#x27;UTF8&#x27;</span> CONNECTION LIMIT = <span class="hljs-number">10</span>;
""");
    }

    [Fact]
    public void PolicyView()
    {
        AssertHighlighter("pgsql",
"""
CREATE POLICY account_managers ON accounts AS PERMISSIVE TO managers USING (manager = current_user);
CREATE MATERIALIZED VIEW mv AS SELECT * FROM t WITH NO DATA;
REFRESH MATERIALIZED VIEW CONCURRENTLY mv WITH DATA;
CREATE VIEW v WITH (security_barrier) AS SELECT 1 WITH CASCADED CHECK OPTION;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">POLICY</span> account_managers <span class="hljs-keyword">ON</span> accounts <span class="hljs-keyword">AS PERMISSIVE</span> <span class="hljs-keyword">TO</span> managers <span class="hljs-keyword">USING</span> (manager = <span class="hljs-built_in">current_user</span>);
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">MATERIALIZED</span> <span class="hljs-keyword">VIEW</span> mv <span class="hljs-keyword">AS</span> <span class="hljs-keyword">SELECT</span> * <span class="hljs-keyword">FROM</span> t <span class="hljs-keyword">WITH NO DATA</span>;
<span class="hljs-keyword">REFRESH</span> <span class="hljs-keyword">MATERIALIZED</span> <span class="hljs-keyword">VIEW</span> <span class="hljs-keyword">CONCURRENTLY</span> mv <span class="hljs-keyword">WITH DATA</span>;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">VIEW</span> v <span class="hljs-keyword">WITH</span> (security_barrier) <span class="hljs-keyword">AS</span> <span class="hljs-keyword">SELECT</span> <span class="hljs-number">1</span> <span class="hljs-keyword">WITH CASCADED CHECK OPTION</span>;
""");
    }

    [Fact]
    public void ListenNotify()
    {
        AssertHighlighter("pgsql",
"""
LISTEN virtual;
NOTIFY virtual, 'This is the payload';
UNLISTEN *;
PREPARE fooplan (int, text, bool, numeric) AS INSERT INTO foo VALUES($1, $2, $3, $4);
EXECUTE fooplan(1, 'Hunter Valley', 't', 200.00);
DEALLOCATE fooplan;
""",
"""
<span class="hljs-keyword">LISTEN</span> virtual;
<span class="hljs-keyword">NOTIFY</span> virtual, <span class="hljs-string">&#x27;This is the payload&#x27;</span>;
<span class="hljs-keyword">UNLISTEN</span> *;
<span class="hljs-keyword">PREPARE</span> fooplan (<span class="hljs-type">int</span>, <span class="hljs-type">text</span>, <span class="hljs-type">bool</span>, <span class="hljs-type">numeric</span>) <span class="hljs-keyword">AS</span> <span class="hljs-keyword">INSERT</span> <span class="hljs-keyword">INTO</span> foo <span class="hljs-keyword">VALUES</span>(<span class="hljs-meta">$1</span>, <span class="hljs-meta">$2</span>, <span class="hljs-meta">$3</span>, <span class="hljs-meta">$4</span>);
<span class="hljs-keyword">EXECUTE</span> fooplan(<span class="hljs-number">1</span>, <span class="hljs-string">&#x27;Hunter Valley&#x27;</span>, <span class="hljs-string">&#x27;t&#x27;</span>, <span class="hljs-number">200.00</span>);
<span class="hljs-keyword">DEALLOCATE</span> fooplan;
""");
    }

    [Fact]
    public void TimeZone()
    {
        AssertHighlighter("pgsql",
"""
SELECT now() AT TIME ZONE 'UTC', '2020-01-01'::timestamp AT TIME ZONE 'Europe/Paris', timezone('UTC', now()), date_trunc('hour', ts), localtimestamp, current_date;
""",
"""
<span class="hljs-keyword">SELECT</span> now() <span class="hljs-keyword">AT TIME ZONE</span> <span class="hljs-string">&#x27;UTC&#x27;</span>, <span class="hljs-string">&#x27;2020-01-01&#x27;</span>::<span class="hljs-type">timestamp</span> <span class="hljs-keyword">AT TIME ZONE</span> <span class="hljs-string">&#x27;Europe/Paris&#x27;</span>, timezone(<span class="hljs-string">&#x27;UTC&#x27;</span>, now()), date_trunc(<span class="hljs-string">&#x27;hour&#x27;</span>, ts), <span class="hljs-built_in">localtimestamp</span>, <span class="hljs-built_in">current_date</span>;
""");
    }

    [Fact]
    public void Booleans()
    {
        AssertHighlighter("pgsql",
"""
SELECT TRUE, false, NULL, 'NaN'::float, 'Infinity'::float, x IS TRUE, x IS NOT DISTINCT FROM y, x IS NOT UNKNOWN, a ISNULL, b NOTNULL;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-keyword">TRUE</span>, <span class="hljs-keyword">false</span>, <span class="hljs-keyword">NULL</span>, <span class="hljs-string">&#x27;NaN&#x27;</span>::<span class="hljs-type">float</span>, <span class="hljs-string">&#x27;Infinity&#x27;</span>::<span class="hljs-type">float</span>, x <span class="hljs-keyword">IS</span> <span class="hljs-keyword">TRUE</span>, x <span class="hljs-keyword">IS</span> <span class="hljs-keyword">NOT</span> <span class="hljs-keyword">DISTINCT</span> <span class="hljs-keyword">FROM</span> y, x <span class="hljs-keyword">IS NOT UNKNOWN</span>, a <span class="hljs-keyword">ISNULL</span>, b <span class="hljs-keyword">NOTNULL</span>;
""");
    }

    [Fact]
    public void Operators()
    {
        AssertHighlighter("pgsql",
"""
SELECT a || b, a ~* 'x', a !~ 'y', a <-> b, a && b, a::text, -a, a % 2, a ^ 2, |/ 25, @ -5, a << 1, a >= b, a <> b, a != b;
""",
"""
<span class="hljs-keyword">SELECT</span> a || b, a ~* <span class="hljs-string">&#x27;x&#x27;</span>, a !~ <span class="hljs-string">&#x27;y&#x27;</span>, a &lt;-&gt; b, a &amp;&amp; b, a::<span class="hljs-type">text</span>, -a, a % <span class="hljs-number">2</span>, a ^ <span class="hljs-number">2</span>, |/ <span class="hljs-number">25</span>, @ <span class="hljs-number">-5</span>, a &lt;&lt; <span class="hljs-number">1</span>, a &gt;= b, a &lt;&gt; b, a != b;
""");
    }

    [Fact]
    public void Case()
    {
        AssertHighlighter("pgsql",
"""
SELECT CASE WHEN a = 1 THEN 'one' WHEN a = 2 THEN 'two' ELSE 'other' END, COALESCE(a, 0), NULLIF(a, b), GREATEST(a, b), LEAST(1, 2) FROM test;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-keyword">CASE</span> <span class="hljs-keyword">WHEN</span> a = <span class="hljs-number">1</span> <span class="hljs-keyword">THEN</span> <span class="hljs-string">&#x27;one&#x27;</span> <span class="hljs-keyword">WHEN</span> a = <span class="hljs-number">2</span> <span class="hljs-keyword">THEN</span> <span class="hljs-string">&#x27;two&#x27;</span> <span class="hljs-keyword">ELSE</span> <span class="hljs-string">&#x27;other&#x27;</span> <span class="hljs-keyword">END</span>, COALESCE(a, <span class="hljs-number">0</span>), NULLIF(a, b), GREATEST(a, b), LEAST(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>) <span class="hljs-keyword">FROM</span> test;
""");
    }

    [Fact]
    public void ReturnsTable()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION get_users() RETURNS TABLE (id int, name text) AS $$
  SELECT id, name FROM users;
$$ LANGUAGE sql STABLE PARALLEL SAFE SECURITY DEFINER SET search_path = public COST 100 ROWS 10;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> get_users() <span class="hljs-keyword">RETURNS</span> <span class="hljs-keyword">TABLE</span> (id <span class="hljs-type">int</span>, <span class="hljs-type">name</span> <span class="hljs-type">text</span>) <span class="hljs-keyword">AS</span> $$<span class="language-pgsql">
  <span class="hljs-keyword">SELECT</span> id, <span class="hljs-type">name</span> <span class="hljs-keyword">FROM</span> users;
</span>$$ <span class="hljs-keyword">LANGUAGE</span> <span class="hljs-keyword">sql</span> <span class="hljs-keyword">STABLE</span> <span class="hljs-keyword">PARALLEL SAFE</span> <span class="hljs-keyword">SECURITY</span> <span class="hljs-keyword">DEFINER</span> <span class="hljs-keyword">SET</span> search_path = <span class="hljs-built_in">public</span> <span class="hljs-keyword">COST</span> <span class="hljs-number">100</span> <span class="hljs-keyword">ROWS</span> <span class="hljs-number">10</span>;
""");
    }

    [Fact]
    public void EventTrigger()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION abort_any_command() RETURNS event_trigger LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'command % is disabled', tg_tag;
END;
$$;
CREATE EVENT TRIGGER abort_ddl ON ddl_command_start EXECUTE FUNCTION abort_any_command();
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> abort_any_command() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">event_trigger</span> <span class="hljs-keyword">LANGUAGE</span> plpgsql <span class="hljs-keyword">AS</span> $$<span class="language-pgsql">
<span class="hljs-keyword">BEGIN</span>
  <span class="hljs-keyword">RAISE</span> <span class="hljs-keyword">EXCEPTION</span> <span class="hljs-string">&#x27;command % is disabled&#x27;</span>, <span class="hljs-built_in">tg_tag</span>;
<span class="hljs-keyword">END</span>;
</span>$$;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">EVENT TRIGGER</span> abort_ddl <span class="hljs-keyword">ON</span> ddl_command_start <span class="hljs-keyword">EXECUTE</span> <span class="hljs-keyword">FUNCTION</span> abort_any_command();
""");
    }

    [Fact]
    public void NestedDollar()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION f() RETURNS void AS $fn$
BEGIN
  EXECUTE format($q$ SELECT %I FROM t WHERE x = 'y' $q$, col);
  PERFORM $$inner$$;
END
$fn$ LANGUAGE plpgsql;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> f() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">void</span> <span class="hljs-keyword">AS</span> $fn$<span class="language-pgsql">
<span class="hljs-keyword">BEGIN</span>
  <span class="hljs-keyword">EXECUTE</span> format($q$<span class="language-pgsql"> <span class="hljs-keyword">SELECT</span> %I <span class="hljs-keyword">FROM</span> t <span class="hljs-keyword">WHERE</span> x = <span class="hljs-string">&#x27;y&#x27;</span> </span>$q$, col);
  <span class="hljs-keyword">PERFORM</span> $$<span class="language-pgsql"><span class="hljs-keyword">inner</span></span>$$;
<span class="hljs-keyword">END</span>
</span>$fn$ <span class="hljs-keyword">LANGUAGE</span> plpgsql;
""");
    }

    // Deviation from highlight.js, whose illegal pattern hid the opening delimiter of a tagged string after a space.
    [Fact]
    public void DollarTagAfterSpace()
    {
        AssertHighlighter("pgsql",
"""
SELECT $abc$ text $abc$ AS x, 1;
""",
"""
<span class="hljs-keyword">SELECT</span> $abc$<span class="language-pgsql"> <span class="hljs-type">text</span> </span>$abc$ <span class="hljs-keyword">AS</span> x, <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void UnterminatedString()
    {
        AssertHighlighter("pgsql",
"""
SELECT 'abc
FROM t;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-string">&#x27;abc
FROM t;</span>
""");
    }

    [Fact]
    public void UnterminatedDollar()
    {
        AssertHighlighter("pgsql",
"""
SELECT $x$ abc
FROM t;
""",
"""
<span class="hljs-keyword">SELECT</span> $x$<span class="language-pgsql"> abc
<span class="hljs-keyword">FROM</span> t;</span>
""");
    }

    [Fact]
    public void ManyNestedUnterminatedDollars_AreCappedInsteadOfOverflowingTheStack()
    {
        var code = string.Concat(Enumerable.Range(0, 5_000).Select(i => $"SELECT $t{i}$ ")) + "FROM t;";

        var result = HighlightWithFallbackDetection(code, "pgsql", out var isFallback);

        Assert.False(isFallback);
        Assert.Equal(32, CountOccurrences(result, "<span class=\"language-pgsql\">"));
        Assert.EndsWith("SELECT $t4999$ FROM t;" + string.Concat(Enumerable.Repeat("</span>", 32)), result, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void UnterminatedComment()
    {
        AssertHighlighter("pgsql",
"""
SELECT 1 /* abc
FROM t;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-number">1</span> <span class="hljs-comment">/* abc
FROM t;</span>
""");
    }

    // Illegal lexemes are ignored by default.
    [Fact]
    public void Illegal()
    {
        AssertHighlighter("pgsql",
"""
SELECT a:==b, x... , {{y}}, $foo, count (*) FROM t;
""",
"""
<span class="hljs-keyword">SELECT</span> a:==b, x... , {{y}}, $foo, count (*) <span class="hljs-keyword">FROM</span> t;
""");
    }

    [Fact]
    public void Labels()
    {
        AssertHighlighter("pgsql",
"""
<<block>>
BEGIN
  NULL;
END block;
""",
"""
<span class="hljs-symbol">&lt;&lt;block&gt;&gt;</span>
<span class="hljs-keyword">BEGIN</span>
  <span class="hljs-keyword">NULL</span>;
<span class="hljs-keyword">END</span> block;
""");
    }

    [Fact]
    public void PsqlMeta()
    {
        AssertHighlighter("pgsql",
"""
\c mydb
\dt+ public.*
\set ON_ERROR_STOP on
SELECT :'name', :var;
""",
"""
\c mydb
\dt+ <span class="hljs-built_in">public</span>.*
\<span class="hljs-keyword">set</span> ON_ERROR_STOP <span class="hljs-keyword">on</span>
<span class="hljs-keyword">SELECT</span> :<span class="hljs-string">&#x27;name&#x27;</span>, :var;
""");
    }

    [Fact]
    public void WithOrdinality()
    {
        AssertHighlighter("pgsql",
"""
SELECT * FROM unnest(ARRAY['a','b']) WITH ORDINALITY AS t(x, n);
SELECT * FROM generate_series(1, 10) AS s(i);
SELECT grouping(a), sum(b) FROM t GROUP BY GROUPING SETS ((a), ());
SELECT * FROM t TABLESAMPLE SYSTEM (10);
""",
"""
<span class="hljs-keyword">SELECT</span> * <span class="hljs-keyword">FROM</span> unnest(<span class="hljs-keyword">ARRAY</span>[<span class="hljs-string">&#x27;a&#x27;</span>,<span class="hljs-string">&#x27;b&#x27;</span>]) <span class="hljs-keyword">WITH ORDINALITY</span> <span class="hljs-keyword">AS</span> t(x, n);
<span class="hljs-keyword">SELECT</span> * <span class="hljs-keyword">FROM</span> generate_series(<span class="hljs-number">1</span>, <span class="hljs-number">10</span>) <span class="hljs-keyword">AS</span> s(i);
<span class="hljs-keyword">SELECT</span> grouping(a), sum(b) <span class="hljs-keyword">FROM</span> t <span class="hljs-keyword">GROUP</span> <span class="hljs-keyword">BY</span> <span class="hljs-keyword">GROUPING SETS</span> ((a), ());
<span class="hljs-keyword">SELECT</span> * <span class="hljs-keyword">FROM</span> t <span class="hljs-keyword">TABLESAMPLE</span> <span class="hljs-keyword">SYSTEM</span> (<span class="hljs-number">10</span>);
""");
    }

    [Fact]
    public void CaseInsensitive()
    {
        AssertHighlighter("pgsql",
"""
select Id from Users where Name ilike 'a%' order by 1 desc;
Create Or Replace Function f() Returns Int As $$ Begin Return 1; End $$ Language PLPGSQL;
""",
"""
<span class="hljs-keyword">select</span> Id <span class="hljs-keyword">from</span> Users <span class="hljs-keyword">where</span> <span class="hljs-type">Name</span> <span class="hljs-keyword">ilike</span> <span class="hljs-string">&#x27;a%&#x27;</span> <span class="hljs-keyword">order</span> <span class="hljs-keyword">by</span> <span class="hljs-number">1</span> <span class="hljs-keyword">desc</span>;
<span class="hljs-keyword">Create</span> <span class="hljs-keyword">Or Replace</span> <span class="hljs-keyword">Function</span> f() <span class="hljs-keyword">Returns</span> <span class="hljs-type">Int</span> <span class="hljs-keyword">As</span> $$<span class="language-pgsql"> <span class="hljs-keyword">Begin</span> <span class="hljs-keyword">Return</span> <span class="hljs-number">1</span>; <span class="hljs-keyword">End</span> </span>$$ <span class="hljs-keyword">Language</span> PLPGSQL;
""");
    }

    [Fact]
    public void SecurityLabel()
    {
        AssertHighlighter("pgsql",
"""
SECURITY LABEL FOR selinux ON TABLE mytable IS 'system_u:object_r:sepgsql_table_t:s0';
COMMENT ON TABLE mytable IS 'This is my table.';
COMMENT ON FUNCTION f() IS $$Doc with 'quotes'$$;
""",
"""
<span class="hljs-keyword">SECURITY LABEL</span> <span class="hljs-keyword">FOR</span> selinux <span class="hljs-keyword">ON</span> <span class="hljs-keyword">TABLE</span> mytable <span class="hljs-keyword">IS</span> <span class="hljs-string">&#x27;system_u:object_r:sepgsql_table_t:s0&#x27;</span>;
<span class="hljs-keyword">COMMENT</span> <span class="hljs-keyword">ON</span> <span class="hljs-keyword">TABLE</span> mytable <span class="hljs-keyword">IS</span> <span class="hljs-string">&#x27;This is my table.&#x27;</span>;
<span class="hljs-keyword">COMMENT</span> <span class="hljs-keyword">ON</span> <span class="hljs-keyword">FUNCTION</span> f() <span class="hljs-keyword">IS</span> $$<span class="language-pgsql">Doc <span class="hljs-keyword">with</span> <span class="hljs-string">&#x27;quotes&#x27;</span></span>$$;
""");
    }

    // Each dollar-quoted string is highlighted on its own: the unterminated string of the first one does not leak into the second.
    [Fact]
    public void TwoStringsApostrophe()
    {
        AssertHighlighter("pgsql",
"""
SELECT $$it's$$, $$ second $$;
""",
"""
<span class="hljs-keyword">SELECT</span> $$<span class="language-pgsql">it<span class="hljs-string">&#x27;s</span></span>$$, $$<span class="language-pgsql"> second </span>$$;
""");
    }

    [Fact]
    public void FunctionPlperlBeforeDo()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION f() RETURNS text LANGUAGE plperlu SECURITY DEFINER AS $perl$
  return "hi";
$perl$;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> f() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">text</span> <span class="hljs-keyword">LANGUAGE</span> plperlu <span class="hljs-keyword">SECURITY</span> <span class="hljs-keyword">DEFINER</span> <span class="hljs-keyword">AS</span> $perl$<span class="language-perl">
  <span class="hljs-keyword">return</span> <span class="hljs-string">&quot;hi&quot;</span>;
</span>$perl$;
""");
    }

    // An unknown language leaves the body as plain text.
    [Fact]
    public void FunctionC()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION add_one(integer) RETURNS integer
     AS 'DIRECTORY/funcs', 'add_one'
     LANGUAGE C STRICT;
CREATE FUNCTION f() RETURNS int AS $$some_symbol$$ LANGUAGE internal;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> add_one(<span class="hljs-type">integer</span>) <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">integer</span>
     <span class="hljs-keyword">AS</span> <span class="hljs-string">&#x27;DIRECTORY/funcs&#x27;</span>, <span class="hljs-string">&#x27;add_one&#x27;</span>
     <span class="hljs-keyword">LANGUAGE</span> C <span class="hljs-keyword">STRICT</span>;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> f() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">AS</span> $$some_symbol$$ <span class="hljs-keyword">LANGUAGE</span> <span class="hljs-type">internal</span>;
""");
    }

    [Fact]
    public void FunctionQuotedLang()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION f() RETURNS int AS $$ return 1 $$ LANGUAGE 'plpython3u';
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> f() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">AS</span> $$<span class="language-python"> <span class="hljs-keyword">return</span> <span class="hljs-number">1</span> </span>$$ <span class="hljs-keyword">LANGUAGE</span> <span class="hljs-string">&#x27;plpython3u&#x27;</span>;
""");
    }

    [Fact]
    public void FunctionLua()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION hello(name text) RETURNS text AS $$
  return "hello " .. name
$$ LANGUAGE pllua;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> hello(<span class="hljs-type">name</span> <span class="hljs-type">text</span>) <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">text</span> <span class="hljs-keyword">AS</span> $$<span class="language-lua">
  <span class="hljs-keyword">return</span> <span class="hljs-string">&quot;hello &quot;</span> .. name
</span>$$ <span class="hljs-keyword">LANGUAGE</span> pllua;
""");
    }

    [Fact]
    public void FunctionRubyJavaPhp()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION r() RETURNS int AS $$ x = [1, 2].map { |i| i * 2 }; x.sum $$ LANGUAGE plruby;
CREATE FUNCTION j() RETURNS int AS $$ return java.lang.Math.max(1, 2); $$ LANGUAGE pljava;
CREATE FUNCTION p() RETURNS int AS $$ $x = array(1, 2); return count($x); $$ LANGUAGE plphp;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> r() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">AS</span> $$<span class="language-ruby"> x = [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>].map { |<span class="hljs-params">i</span>| i * <span class="hljs-number">2</span> }; x.sum </span>$$ <span class="hljs-keyword">LANGUAGE</span> plruby;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> j() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">AS</span> $$<span class="language-java"> <span class="hljs-keyword">return</span> java.lang.Math.max(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>); </span>$$ <span class="hljs-keyword">LANGUAGE</span> pljava;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> p() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">AS</span> $$<span class="language-php"> <span class="hljs-variable">$x</span> = <span class="hljs-keyword">array</span>(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>); <span class="hljs-keyword">return</span> <span class="hljs-title function_ invoke__">count</span>(<span class="hljs-variable">$x</span>); </span>$$ <span class="hljs-keyword">LANGUAGE</span> plphp;
""");
    }

    [Fact]
    public void ReturnsSetof()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION f(OUT a int, INOUT b text, VARIADIC c int[]) RETURNS SETOF record LANGUAGE sql RETURNS NULL ON NULL INPUT AS $$ SELECT 1, 'x' $$;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> f(<span class="hljs-keyword">OUT</span> a <span class="hljs-type">int</span>, <span class="hljs-keyword">INOUT</span> b <span class="hljs-type">text</span>, <span class="hljs-keyword">VARIADIC</span> c <span class="hljs-type">int</span>[]) <span class="hljs-keyword">RETURNS</span> <span class="hljs-keyword">SETOF</span> <span class="hljs-type">record</span> <span class="hljs-keyword">LANGUAGE</span> <span class="hljs-keyword">sql</span> <span class="hljs-keyword">RETURNS</span> <span class="hljs-keyword">NULL</span> <span class="hljs-keyword">ON</span> <span class="hljs-keyword">NULL</span> <span class="hljs-keyword">INPUT</span> <span class="hljs-keyword">AS</span> $$<span class="language-pgsql"> <span class="hljs-keyword">SELECT</span> <span class="hljs-number">1</span>, <span class="hljs-string">&#x27;x&#x27;</span> </span>$$;
""");
    }

    [Fact]
    public void Merge()
    {
        AssertHighlighter("pgsql",
"""
MERGE INTO customer_account ca
USING recent_transactions t
ON t.customer_id = ca.customer_id
WHEN MATCHED THEN
  UPDATE SET balance = balance + transaction_value
WHEN NOT MATCHED THEN
  INSERT (customer_id, balance)
  VALUES (t.customer_id, t.transaction_value);
""",
"""
MERGE <span class="hljs-keyword">INTO</span> customer_account ca
<span class="hljs-keyword">USING</span> recent_transactions t
<span class="hljs-keyword">ON</span> t.customer_id = ca.customer_id
<span class="hljs-keyword">WHEN</span> MATCHED <span class="hljs-keyword">THEN</span>
  <span class="hljs-keyword">UPDATE</span> <span class="hljs-keyword">SET</span> balance = balance + transaction_value
<span class="hljs-keyword">WHEN</span> <span class="hljs-keyword">NOT</span> MATCHED <span class="hljs-keyword">THEN</span>
  <span class="hljs-keyword">INSERT</span> (customer_id, balance)
  <span class="hljs-keyword">VALUES</span> (t.customer_id, t.transaction_value);
""");
    }

    [Fact]
    public void LateralJoin()
    {
        AssertHighlighter("pgsql",
"""
SELECT * FROM a CROSS JOIN LATERAL (SELECT * FROM b WHERE b.a_id = a.id LIMIT 1) sub
RIGHT JOIN c USING (id) NATURAL FULL OUTER JOIN d INNER JOIN e ON true;
""",
"""
<span class="hljs-keyword">SELECT</span> * <span class="hljs-keyword">FROM</span> a <span class="hljs-keyword">CROSS</span> <span class="hljs-keyword">JOIN</span> <span class="hljs-keyword">LATERAL</span> (<span class="hljs-keyword">SELECT</span> * <span class="hljs-keyword">FROM</span> b <span class="hljs-keyword">WHERE</span> b.a_id = a.id <span class="hljs-keyword">LIMIT</span> <span class="hljs-number">1</span>) sub
<span class="hljs-keyword">RIGHT JOIN</span> c <span class="hljs-keyword">USING</span> (id) <span class="hljs-keyword">NATURAL</span> <span class="hljs-keyword">FULL</span> <span class="hljs-keyword">OUTER</span> <span class="hljs-keyword">JOIN</span> d <span class="hljs-keyword">INNER</span> <span class="hljs-keyword">JOIN</span> e <span class="hljs-keyword">ON</span> <span class="hljs-keyword">true</span>;
""");
    }

    [Fact]
    public void Exceptions()
    {
        AssertHighlighter("pgsql",
"""
BEGIN
  INSERT INTO t VALUES (1);
EXCEPTION
  WHEN unique_violation OR foreign_key_violation THEN
    GET STACKED DIAGNOSTICS msg = MESSAGE_TEXT, detail = PG_EXCEPTION_DETAIL;
  WHEN OTHERS THEN
    RAISE;
END;
""",
"""
<span class="hljs-keyword">BEGIN</span>
  <span class="hljs-keyword">INSERT</span> <span class="hljs-keyword">INTO</span> t <span class="hljs-keyword">VALUES</span> (<span class="hljs-number">1</span>);
<span class="hljs-keyword">EXCEPTION</span>
  <span class="hljs-keyword">WHEN</span> <span class="hljs-built_in">unique_violation</span> <span class="hljs-keyword">OR</span> <span class="hljs-built_in">foreign_key_violation</span> <span class="hljs-keyword">THEN</span>
    <span class="hljs-keyword">GET</span> <span class="hljs-keyword">STACKED</span> <span class="hljs-keyword">DIAGNOSTICS</span> msg = <span class="hljs-built_in">MESSAGE_TEXT</span>, detail = PG_EXCEPTION_DETAIL;
  <span class="hljs-keyword">WHEN</span> OTHERS <span class="hljs-keyword">THEN</span>
    <span class="hljs-keyword">RAISE</span>;
<span class="hljs-keyword">END</span>;
""");
    }

    [Fact]
    public void CreateExtension()
    {
        AssertHighlighter("pgsql",
"""
CREATE EXTENSION IF NOT EXISTS "uuid-ossp" WITH SCHEMA public VERSION '1.1';
CREATE FOREIGN DATA WRAPPER dummy;
CREATE SERVER myserver FOREIGN DATA WRAPPER postgres_fdw OPTIONS (host 'foo', dbname 'foodb', port '5432');
CREATE USER MAPPING FOR bob SERVER foo OPTIONS (user 'bob', password 'secret');
IMPORT FOREIGN SCHEMA foreign_films FROM SERVER film_server INTO films;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">EXTENSION</span> <span class="hljs-keyword">IF</span> <span class="hljs-keyword">NOT</span> <span class="hljs-keyword">EXISTS</span> &quot;uuid-ossp&quot; <span class="hljs-keyword">WITH</span> <span class="hljs-keyword">SCHEMA</span> <span class="hljs-built_in">public</span> <span class="hljs-keyword">VERSION</span> <span class="hljs-string">&#x27;1.1&#x27;</span>;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FOREIGN DATA</span> <span class="hljs-keyword">WRAPPER</span> dummy;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">SERVER</span> myserver <span class="hljs-keyword">FOREIGN DATA</span> <span class="hljs-keyword">WRAPPER</span> postgres_fdw <span class="hljs-keyword">OPTIONS</span> (host <span class="hljs-string">&#x27;foo&#x27;</span>, dbname <span class="hljs-string">&#x27;foodb&#x27;</span>, port <span class="hljs-string">&#x27;5432&#x27;</span>);
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">USER</span> <span class="hljs-keyword">MAPPING</span> <span class="hljs-keyword">FOR</span> bob <span class="hljs-keyword">SERVER</span> foo <span class="hljs-keyword">OPTIONS</span> (<span class="hljs-keyword">user</span> <span class="hljs-string">&#x27;bob&#x27;</span>, <span class="hljs-keyword">password</span> <span class="hljs-string">&#x27;secret&#x27;</span>);
<span class="hljs-keyword">IMPORT</span> <span class="hljs-keyword">FOREIGN</span> <span class="hljs-keyword">SCHEMA</span> foreign_films <span class="hljs-keyword">FROM</span> <span class="hljs-keyword">SERVER</span> film_server <span class="hljs-keyword">INTO</span> films;
""");
    }

    [Fact]
    public void TextSearch()
    {
        AssertHighlighter("pgsql",
"""
CREATE TEXT SEARCH CONFIGURATION my_search_config (copy = pg_catalog.english);
SELECT to_tsvector('english', 'The Fat Rats') @@ to_tsquery('fat & rat');
SELECT ts_rank(tsv, query) FROM docs, plainto_tsquery('cat') query;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">TEXT SEARCH</span> <span class="hljs-keyword">CONFIGURATION</span> my_search_config (copy = pg_catalog.english);
<span class="hljs-keyword">SELECT</span> to_tsvector(<span class="hljs-string">&#x27;english&#x27;</span>, <span class="hljs-string">&#x27;The Fat Rats&#x27;</span>) @@ to_tsquery(<span class="hljs-string">&#x27;fat &amp; rat&#x27;</span>);
<span class="hljs-keyword">SELECT</span> ts_rank(tsv, query) <span class="hljs-keyword">FROM</span> docs, plainto_tsquery(<span class="hljs-string">&#x27;cat&#x27;</span>) query;
""");
    }

    [Fact]
    public void ReturnsTrigger()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION f() RETURNS trigger AS $$ BEGIN RETURN NEW; END $$ LANGUAGE plpgsql;
CREATE FUNCTION h() RETURNS language_handler AS 'plpgsql' LANGUAGE C;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> f() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">trigger</span> <span class="hljs-keyword">AS</span> $$<span class="language-pgsql"> <span class="hljs-keyword">BEGIN</span> <span class="hljs-keyword">RETURN</span> <span class="hljs-built_in">NEW</span>; <span class="hljs-keyword">END</span> </span>$$ <span class="hljs-keyword">LANGUAGE</span> plpgsql;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> h() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">language_handler</span> <span class="hljs-keyword">AS</span> <span class="hljs-string">&#x27;plpgsql&#x27;</span> <span class="hljs-keyword">LANGUAGE</span> C;
""");
    }

    [Fact]
    public void RangeTypes()
    {
        AssertHighlighter("pgsql",
"""
SELECT int4range(1, 10), '[2020-01-01,2021-01-01)'::daterange, tstzrange(now(), now() + interval '1 hour'), numrange(1.0, 2.0) && numrange(1.5, 3.0);
CREATE TABLE r (during tsrange, EXCLUDE USING gist (during WITH &&));
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-type">int4range</span>(<span class="hljs-number">1</span>, <span class="hljs-number">10</span>), <span class="hljs-string">&#x27;[2020-01-01,2021-01-01)&#x27;</span>::<span class="hljs-type">daterange</span>, <span class="hljs-type">tstzrange</span>(now(), now() + <span class="hljs-type">interval</span> <span class="hljs-string">&#x27;1 hour&#x27;</span>), <span class="hljs-type">numrange</span>(<span class="hljs-number">1.0</span>, <span class="hljs-number">2.0</span>) &amp;&amp; <span class="hljs-type">numrange</span>(<span class="hljs-number">1.5</span>, <span class="hljs-number">3.0</span>);
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">TABLE</span> r (during <span class="hljs-type">tsrange</span>, <span class="hljs-keyword">EXCLUDE</span> <span class="hljs-keyword">USING</span> gist (during <span class="hljs-keyword">WITH</span> &amp;&amp;));
""");
    }

    [Fact]
    public void DollarBodyWithOtherDollars()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION f() RETURNS text AS $a$
  SELECT $b$x$b$ || $$y$$;
$a$ LANGUAGE sql;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> f() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">text</span> <span class="hljs-keyword">AS</span> $a$<span class="language-pgsql">
  <span class="hljs-keyword">SELECT</span> $b$<span class="language-pgsql">x</span>$b$ || $$<span class="language-pgsql">y</span>$$;
</span>$a$ <span class="hljs-keyword">LANGUAGE</span> <span class="hljs-keyword">sql</span>;
""");
    }

    [Fact]
    public void DollarJsonWs()
    {
        AssertHighlighter("pgsql",
"""
SELECT $$ {"k": "v"} $$::jsonb, $$ <x/> $$::xml;
""",
"""
<span class="hljs-keyword">SELECT</span> $$<span class="language-json"> <span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;k&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;v&quot;</span><span class="hljs-punctuation">}</span> </span>$$::<span class="hljs-type">jsonb</span>, $$<span class="language-xml"> <span class="hljs-tag">&lt;<span class="hljs-name">x</span>/&gt;</span> </span>$$::<span class="hljs-type">xml</span>;
""");
    }

    [Fact]
    public void TwoFunctions()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION a() RETURNS int AS $$ my $x = 1; return $x; $$ LANGUAGE plperl;
CREATE FUNCTION b() RETURNS int AS $$ BEGIN RETURN 1; END $$ LANGUAGE plpgsql;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> a() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">AS</span> $$<span class="language-perl"> <span class="hljs-keyword">my</span> <span class="hljs-variable">$x</span> = <span class="hljs-number">1</span>; <span class="hljs-keyword">return</span> <span class="hljs-variable">$x</span>; </span>$$ <span class="hljs-keyword">LANGUAGE</span> plperl;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> b() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">AS</span> $$<span class="language-pgsql"> <span class="hljs-keyword">BEGIN</span> <span class="hljs-keyword">RETURN</span> <span class="hljs-number">1</span>; <span class="hljs-keyword">END</span> </span>$$ <span class="hljs-keyword">LANGUAGE</span> plpgsql;
""");
    }

    // Only a LANGUAGE clause right before AS (or the body of DO) selects the language.
    [Fact]
    public void LanguageOnComment()
    {
        AssertHighlighter("pgsql",
"""
COMMENT ON LANGUAGE plperl IS $$Perl procedural language$$;
""",
"""
<span class="hljs-keyword">COMMENT</span> <span class="hljs-keyword">ON</span> <span class="hljs-keyword">LANGUAGE</span> plperl <span class="hljs-keyword">IS</span> $$<span class="language-pgsql">Perl <span class="hljs-keyword">procedural</span> <span class="hljs-keyword">language</span></span>$$;
""");
    }

    [Fact]
    public void DoAfterLanguage()
    {
        AssertHighlighter("pgsql",
"""
DO $$ print "x" $$ LANGUAGE plperl;
""",
"""
<span class="hljs-keyword">DO</span> $$<span class="language-perl"> <span class="hljs-keyword">print</span> <span class="hljs-string">&quot;x&quot;</span> </span>$$ <span class="hljs-keyword">LANGUAGE</span> plperl;
""");
    }

    // `AS$$` is an identifier in PostgreSQL, so the dollar quote starts at the second `$$`.
    [Fact]
    public void AsNoSpace()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION f() RETURNS int AS$$ SELECT 1 $$LANGUAGE sql;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> f() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">AS</span>$$ <span class="hljs-keyword">SELECT</span> <span class="hljs-number">1</span> $$<span class="language-pgsql"><span class="hljs-keyword">LANGUAGE</span> <span class="hljs-keyword">sql</span>;</span>
""");
    }

    [Fact]
    public void IncludeRangeFormat()
    {
        AssertHighlighter("pgsql",
"""
CREATE TABLE t (a int, UNIQUE (a) INCLUDE (b));
SELECT format('%s', 1), family(inet '1.2.3.4'), version();
SELECT sum(x) OVER (ORDER BY y RANGE 5 PRECEDING), sum(x) OVER (ORDER BY y RANGE CURRENT ROW), range_merge(a, b), RANGE FROM t;
CREATE TYPE floatrange AS RANGE (subtype = float8, subtype_diff = float8mi);
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">TABLE</span> t (a <span class="hljs-type">int</span>, <span class="hljs-keyword">UNIQUE</span> (a) <span class="hljs-keyword">INCLUDE</span> (b));
<span class="hljs-keyword">SELECT</span> format(<span class="hljs-string">&#x27;%s&#x27;</span>, <span class="hljs-number">1</span>), family(<span class="hljs-type">inet</span> <span class="hljs-string">&#x27;1.2.3.4&#x27;</span>), version();
<span class="hljs-keyword">SELECT</span> sum(x) <span class="hljs-keyword">OVER</span> (<span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> y <span class="hljs-keyword">RANGE</span> <span class="hljs-number">5</span> <span class="hljs-keyword">PRECEDING</span>), sum(x) <span class="hljs-keyword">OVER</span> (<span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> y <span class="hljs-keyword">RANGE</span> <span class="hljs-keyword">CURRENT</span> <span class="hljs-keyword">ROW</span>), range_merge(a, b), RANGE <span class="hljs-keyword">FROM</span> t;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">TYPE</span> floatrange <span class="hljs-keyword">AS RANGE</span> (subtype = <span class="hljs-type">float8</span>, subtype_diff = float8mi);
""");
    }

    [Fact]
    public void DisableBeforeEquals()
    {
        AssertHighlighter("pgsql",
"""
CREATE TEXT SEARCH PARSER p (START = prsd_start, GETTOKEN = prsd_nexttoken, END = prsd_end, LEXTYPES = prsd_lextype);
CREATE DATABASE d WITH OWNER = u TEMPLATE = t ENCODING = 'UTF8' TABLESPACE = ts CONNECTION LIMIT = 5;
RAISE EXCEPTION 'x' USING ERRCODE = 'unique_violation', COLUMN = 'c', CONSTRAINT = 'k', TABLE = 't', SCHEMA = 's';
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">TEXT SEARCH</span> <span class="hljs-keyword">PARSER</span> p (START = prsd_start, GETTOKEN = prsd_nexttoken, END = prsd_end, LEXTYPES = prsd_lextype);
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">DATABASE</span> d <span class="hljs-keyword">WITH</span> OWNER = u TEMPLATE = t ENCODING = <span class="hljs-string">&#x27;UTF8&#x27;</span> TABLESPACE = ts CONNECTION LIMIT = <span class="hljs-number">5</span>;
<span class="hljs-keyword">RAISE</span> <span class="hljs-keyword">EXCEPTION</span> <span class="hljs-string">&#x27;x&#x27;</span> <span class="hljs-keyword">USING</span> ERRCODE = <span class="hljs-string">&#x27;unique_violation&#x27;</span>, COLUMN = <span class="hljs-string">&#x27;c&#x27;</span>, CONSTRAINT = <span class="hljs-string">&#x27;k&#x27;</span>, TABLE = <span class="hljs-string">&#x27;t&#x27;</span>, SCHEMA = <span class="hljs-string">&#x27;s&#x27;</span>;
""");
    }

    // Like highlight.js, everything after MINVALUE up to the next number is skipped.
    [Fact]
    public void SequenceRunaway()
    {
        AssertHighlighter("pgsql",
"""
SELECT MINVALUE FROM t WHERE a = 'x' AND b IN (SELECT c FROM d);
SELECT 1;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-keyword">MINVALUE</span> FROM t WHERE a = &#x27;x&#x27; AND b IN (SELECT c FROM d);
SELECT <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void PseudoReturns()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION a() RETURNS event_trigger AS 'x' LANGUAGE C;
CREATE FUNCTION b() RETURNS fdw_handler AS 'x' LANGUAGE C;
CREATE FUNCTION c() RETURNS index_am_handler AS 'x' LANGUAGE C;
CREATE FUNCTION d() RETURNS tsm_handler AS 'x' LANGUAGE C;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> a() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">event_trigger</span> <span class="hljs-keyword">AS</span> <span class="hljs-string">&#x27;x&#x27;</span> <span class="hljs-keyword">LANGUAGE</span> C;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> b() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">fdw_handler</span> <span class="hljs-keyword">AS</span> <span class="hljs-string">&#x27;x&#x27;</span> <span class="hljs-keyword">LANGUAGE</span> C;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> c() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">index_am_handler</span> <span class="hljs-keyword">AS</span> <span class="hljs-string">&#x27;x&#x27;</span> <span class="hljs-keyword">LANGUAGE</span> C;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> d() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">tsm_handler</span> <span class="hljs-keyword">AS</span> <span class="hljs-string">&#x27;x&#x27;</span> <span class="hljs-keyword">LANGUAGE</span> C;
""");
    }

    [Fact]
    public void IntervalFields()
    {
        AssertHighlighter("pgsql",
"""
SELECT INTERVAL YEAR TO MONTH, interval minute to second, INTERVAL '1' DAY, interval month;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-type">INTERVAL YEAR TO MONTH</span>, <span class="hljs-type">interval minute to second</span>, <span class="hljs-type">INTERVAL</span> <span class="hljs-string">&#x27;1&#x27;</span> DAY, <span class="hljs-type">interval month</span>;
""");
    }

    [Fact]
    public void DotTypes()
    {
        AssertHighlighter("pgsql",
"""
SELECT a.int, b.date, c.name, t.oid, t.xml FROM t;
""",
"""
<span class="hljs-keyword">SELECT</span> a.int, b.date, c.name, t.oid, t.xml <span class="hljs-keyword">FROM</span> t;
""");
    }

    [Fact]
    public void HasPrivilege()
    {
        AssertHighlighter("pgsql",
"""
SELECT has_schema_privilege('public', 'USAGE'), HAS_ANY_COLUMN_PRIVILEGE('t', 'SELECT'), pg_catalog.pg_class, PG_TYPEOF(x);
""",
"""
<span class="hljs-keyword">SELECT</span> has_schema_privilege(<span class="hljs-string">&#x27;public&#x27;</span>, <span class="hljs-string">&#x27;USAGE&#x27;</span>), HAS_ANY_COLUMN_PRIVILEGE(<span class="hljs-string">&#x27;t&#x27;</span>, <span class="hljs-string">&#x27;SELECT&#x27;</span>), pg_catalog.pg_class, PG_TYPEOF(x);
""");
    }

    // Deviation from highlight.js: the escape-string prefix cannot be the end of an identifier.
    [Fact]
    public void EstringAfterIdent()
    {
        AssertHighlighter("pgsql",
"""
SELECT date'2020-01-01', time'10:00', type'x\'y', some'thing';
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-type">date</span><span class="hljs-string">&#x27;2020-01-01&#x27;</span>, <span class="hljs-type">time</span><span class="hljs-string">&#x27;10:00&#x27;</span>, <span class="hljs-keyword">type</span><span class="hljs-string">&#x27;x\&#x27;</span>y<span class="hljs-string">&#x27;, some&#x27;</span>thing<span class="hljs-string">&#x27;;</span>
""");
    }

    // A dollar-quoted string that is not a function body is XML or JSON when it starts like it, and PL/pgSQL otherwise.
    [Fact]
    public void Heuristics()
    {
        AssertHighlighter("pgsql",
"""
SELECT $$<<lbl>> BEGIN NULL; END$$, $$ <root/>$$, $x$[1]$x$, $$ plain words $$;
""",
"""
<span class="hljs-keyword">SELECT</span> $$<span class="language-pgsql"><span class="hljs-symbol">&lt;&lt;lbl&gt;&gt;</span> <span class="hljs-keyword">BEGIN</span> <span class="hljs-keyword">NULL</span>; <span class="hljs-keyword">END</span></span>$$, $$<span class="language-xml"> <span class="hljs-tag">&lt;<span class="hljs-name">root</span>/&gt;</span></span>$$, $x$<span class="language-json"><span class="hljs-punctuation">[</span><span class="hljs-number">1</span><span class="hljs-punctuation">]</span></span>$x$, $$<span class="language-pgsql"> plain words </span>$$;
""");
    }

    // The language is read from the LANGUAGE clause, before or after the body, quoted or not.
    [Fact]
    public void LanguageVariants()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION a() RETURNS int AS $$ my $x; $$ IMMUTABLE LANGUAGE plperl;
CREATE FUNCTION b() RETURNS int LANGUAGE "plperl" AS $$ my $y; $$;
CREATE FUNCTION c() RETURNS int AS $$ import os $$ LANGUAGE "plpython3u";
DO LANGUAGE plpython3u $$ plpy.notice('x') $$;
CREATE FUNCTION d() RETURNS int LANGUAGE plr AS $$ x <- c(1, 2) $$;
CREATE FUNCTION e() RETURNS int AS $$ println!("hi"); Ok(Some(1)) $$ LANGUAGE plrust;
CREATE FUNCTION f() RETURNS int AS $$ (define x 1) $$ LANGUAGE plscheme;
CREATE FUNCTION g() RETURNS int AS $$ let x = 1; $$ LANGUAGE pljs;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> a() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">AS</span> $$<span class="language-perl"> <span class="hljs-keyword">my</span> <span class="hljs-variable">$x</span>; </span>$$ <span class="hljs-keyword">IMMUTABLE</span> <span class="hljs-keyword">LANGUAGE</span> plperl;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> b() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">LANGUAGE</span> &quot;plperl&quot; <span class="hljs-keyword">AS</span> $$<span class="language-perl"> <span class="hljs-keyword">my</span> <span class="hljs-variable">$y</span>; </span>$$;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> c() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">AS</span> $$<span class="language-python"> <span class="hljs-keyword">import</span> os </span>$$ <span class="hljs-keyword">LANGUAGE</span> &quot;plpython3u&quot;;
<span class="hljs-keyword">DO</span> <span class="hljs-keyword">LANGUAGE</span> plpython3u $$<span class="language-python"> plpy.notice(<span class="hljs-string">&#x27;x&#x27;</span>) </span>$$;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> d() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">LANGUAGE</span> plr <span class="hljs-keyword">AS</span> $$<span class="language-r"> x <span class="hljs-operator">&lt;-</span> <span class="hljs-built_in">c</span><span class="hljs-punctuation">(</span><span class="hljs-number">1</span><span class="hljs-punctuation">,</span> <span class="hljs-number">2</span><span class="hljs-punctuation">)</span> </span>$$;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> e() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">AS</span> $$<span class="language-rust"> <span class="hljs-built_in">println!</span>(<span class="hljs-string">&quot;hi&quot;</span>); <span class="hljs-literal">Ok</span>(<span class="hljs-literal">Some</span>(<span class="hljs-number">1</span>)) </span>$$ <span class="hljs-keyword">LANGUAGE</span> plrust;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> f() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">AS</span> $$ (define x 1) $$ <span class="hljs-keyword">LANGUAGE</span> plscheme;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> g() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">AS</span> $$<span class="language-javascript"> <span class="hljs-keyword">let</span> x = <span class="hljs-number">1</span>; </span>$$ <span class="hljs-keyword">LANGUAGE</span> pljs;
""");
    }

    [Fact]
    public void UppercaseTags()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION f() RETURNS int AS $BODY$ BEGIN RETURN 1; END $BODY$ LANGUAGE PLPGSQL;
CREATE FUNCTION g() RETURNS int AS $Body$ SELECT 1 $body$ $Body$ LANGUAGE SQL;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> f() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">AS</span> $BODY$<span class="language-pgsql"> <span class="hljs-keyword">BEGIN</span> <span class="hljs-keyword">RETURN</span> <span class="hljs-number">1</span>; <span class="hljs-keyword">END</span> </span>$BODY$ <span class="hljs-keyword">LANGUAGE</span> PLPGSQL;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> g() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">AS</span> $Body$<span class="language-pgsql"> <span class="hljs-keyword">SELECT</span> <span class="hljs-number">1</span> $body$<span class="language-pgsql"> </span></span>$Body$ <span class="hljs-keyword">LANGUAGE</span> <span class="hljs-keyword">SQL</span>;
""");
    }

    [Fact]
    public void UnterminatedBodyLangBefore()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION f() RETURNS int LANGUAGE plperl AS $$
  my $x = 1;
  return $x;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> f() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">LANGUAGE</span> plperl <span class="hljs-keyword">AS</span> $$<span class="language-perl">
  <span class="hljs-keyword">my</span> <span class="hljs-variable">$x</span> = <span class="hljs-number">1</span>;
  <span class="hljs-keyword">return</span> <span class="hljs-variable">$x</span>;</span>
""");
    }

    [Fact]
    public void DollarFunctionTag()
    {
        AssertHighlighter("pgsql",
"""
CREATE OR REPLACE FUNCTION public.f()
 RETURNS integer
 LANGUAGE plpgsql
AS $function$
BEGIN
  RETURN 1;
END;
$function$
;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">OR REPLACE</span> <span class="hljs-keyword">FUNCTION</span> <span class="hljs-built_in">public</span>.f()
 <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">integer</span>
 <span class="hljs-keyword">LANGUAGE</span> plpgsql
<span class="hljs-keyword">AS</span> $function$<span class="language-pgsql">
<span class="hljs-keyword">BEGIN</span>
  <span class="hljs-keyword">RETURN</span> <span class="hljs-number">1</span>;
<span class="hljs-keyword">END</span>;
</span>$function$
;
""");
    }

    // Deviation from highlight.js: a dollar quote cannot start inside an identifier.
    [Fact]
    public void DollarParamsAndIdents()
    {
        AssertHighlighter("pgsql",
"""
SELECT $1, $12, a$b, x$$y, $_$ z $_$;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-meta">$1</span>, <span class="hljs-meta">$12</span>, a$b, x$$y, $_$<span class="language-pgsql"> z </span>$_$;
""");
    }

    [Fact]
    public void LabelsSpaced()
    {
        AssertHighlighter("pgsql",
"""
<< my_label >>
LOOP EXIT my_label; END LOOP;
""",
"""
<span class="hljs-symbol">&lt;&lt; my_label &gt;&gt;</span>
<span class="hljs-keyword">LOOP</span> <span class="hljs-keyword">EXIT</span> my_label; <span class="hljs-keyword">END</span> <span class="hljs-keyword">LOOP</span>;
""");
    }

    [Fact]
    public void CompilerOption()
    {
        AssertHighlighter("pgsql",
"""
#print_strict_params on
#variable_conflict error
SELECT 1;
""",
"""
<span class="hljs-meta">#print_strict_params on</span>
<span class="hljs-meta">#variable_conflict error</span>
<span class="hljs-keyword">SELECT</span> <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void TypesPathKeywords()
    {
        AssertHighlighter("pgsql",
"""
SELECT * FROM XMLTABLE('/x' PASSING d COLUMNS a integer PATH 'a', b varchar(10) PATH 'b', c path PATH 'c', d xml PATH '.');
""",
"""
<span class="hljs-keyword">SELECT</span> * <span class="hljs-keyword">FROM</span> XMLTABLE(<span class="hljs-string">&#x27;/x&#x27;</span> <span class="hljs-keyword">PASSING</span> d <span class="hljs-keyword">COLUMNS</span> a <span class="hljs-type">integer</span> <span class="hljs-keyword">PATH</span> <span class="hljs-string">&#x27;a&#x27;</span>, b <span class="hljs-type">varchar</span>(<span class="hljs-number">10</span>) <span class="hljs-type">PATH</span> <span class="hljs-string">&#x27;b&#x27;</span>, c <span class="hljs-keyword">path</span> <span class="hljs-keyword">PATH</span> <span class="hljs-string">&#x27;c&#x27;</span>, d <span class="hljs-type">xml</span> <span class="hljs-keyword">PATH</span> <span class="hljs-string">&#x27;.&#x27;</span>);
""");
    }

    [Fact]
    public void ExtractMultiple()
    {
        AssertHighlighter("pgsql",
"""
SELECT EXTRACT(isodow FROM d), EXTRACT(TIMEZONE_HOUR FROM ts), extract ( century from x ), EXTRACT(day
""",
"""
<span class="hljs-keyword">SELECT</span> EXTRACT(<span class="hljs-type">isodow</span> <span class="hljs-keyword">FROM</span> d), EXTRACT(<span class="hljs-type">TIMEZONE_HOUR</span> <span class="hljs-keyword">FROM</span> ts), extract ( <span class="hljs-type">century</span> <span class="hljs-keyword">from</span> x ), EXTRACT(<span class="hljs-type">day</span>
""");
    }

    [Fact]
    public void XmlMisc()
    {
        AssertHighlighter("pgsql",
"""
SELECT XMLPI(NAME php, 'echo "hello world";'), XMLSERIALIZE(DOCUMENT d AS text), x IS DOCUMENT, x IS NOT DOCUMENT, XMLROOT(x, VERSION '1.0', STANDALONE YES), XMLROOT(x, version no value, standalone no value);
SET XML OPTION DOCUMENT; SELECT xmlexists('//x' PASSING BY REF d), xmlagg(x ORDER BY y);
""",
"""
<span class="hljs-keyword">SELECT</span> XMLPI(<span class="hljs-keyword">NAME</span> php, <span class="hljs-string">&#x27;echo &quot;hello world&quot;;&#x27;</span>), XMLSERIALIZE(<span class="hljs-keyword">DOCUMENT</span> d <span class="hljs-keyword">AS</span> <span class="hljs-type">text</span>), x <span class="hljs-keyword">IS DOCUMENT</span>, x <span class="hljs-keyword">IS NOT DOCUMENT</span>, XMLROOT(x, <span class="hljs-keyword">VERSION</span> <span class="hljs-string">&#x27;1.0&#x27;</span>, <span class="hljs-keyword">STANDALONE YES</span>), XMLROOT(x, <span class="hljs-keyword">version</span> <span class="hljs-keyword">no</span> <span class="hljs-keyword">value</span>, <span class="hljs-keyword">standalone no</span> <span class="hljs-keyword">value</span>);
<span class="hljs-keyword">SET</span> <span class="hljs-keyword">XML OPTION DOCUMENT</span>; <span class="hljs-keyword">SELECT</span> xmlexists(<span class="hljs-string">&#x27;//x&#x27;</span> <span class="hljs-keyword">PASSING</span> <span class="hljs-keyword">BY</span> <span class="hljs-keyword">REF</span> d), xmlagg(x <span class="hljs-keyword">ORDER</span> <span class="hljs-keyword">BY</span> y);
""");
    }

    [Fact]
    public void CopyProgram()
    {
        AssertHighlighter("pgsql",
"""
COPY t FROM STDIN; COPY t TO PROGRAM 'gzip > /tmp/x.gz'; COPY (SELECT 1) TO STDOUT WITH (FORMAT csv, FORCE_QUOTE *, FORCE_NOT_NULL (a), ENCODING 'UTF8');
""",
"""
<span class="hljs-keyword">COPY</span> t <span class="hljs-keyword">FROM STDIN</span>; <span class="hljs-keyword">COPY</span> t <span class="hljs-keyword">TO PROGRAM</span> <span class="hljs-string">&#x27;gzip &gt; /tmp/x.gz&#x27;</span>; <span class="hljs-keyword">COPY</span> (<span class="hljs-keyword">SELECT</span> <span class="hljs-number">1</span>) <span class="hljs-keyword">TO STDOUT</span> <span class="hljs-keyword">WITH</span> (<span class="hljs-keyword">FORMAT</span> csv, <span class="hljs-keyword">FORCE_QUOTE</span> *, <span class="hljs-keyword">FORCE_NOT_NULL</span> (a), <span class="hljs-keyword">ENCODING</span> <span class="hljs-string">&#x27;UTF8&#x27;</span>);
""");
    }

    [Fact]
    public void FetchCursor()
    {
        AssertHighlighter("pgsql",
"""
DECLARE c SCROLL CURSOR WITH HOLD FOR SELECT 1; DECLARE d NO SCROLL CURSOR FOR SELECT 2; DECLARE b BINARY CURSOR WITHOUT HOLD FOR SELECT 3;
FETCH NEXT FROM c; FETCH PRIOR FROM c; MOVE FORWARD 5 IN c; FETCH BACKWARD ALL FROM c; MOVE ABSOLUTE 1 IN c; CLOSE c;
""",
"""
<span class="hljs-keyword">DECLARE</span> c <span class="hljs-keyword">SCROLL CURSOR</span> <span class="hljs-keyword">WITH HOLD</span> <span class="hljs-keyword">FOR</span> <span class="hljs-keyword">SELECT</span> <span class="hljs-number">1</span>; <span class="hljs-keyword">DECLARE</span> d <span class="hljs-keyword">NO SCROLL CURSOR</span> <span class="hljs-keyword">FOR</span> <span class="hljs-keyword">SELECT</span> <span class="hljs-number">2</span>; <span class="hljs-keyword">DECLARE</span> b <span class="hljs-keyword">BINARY CURSOR</span> <span class="hljs-keyword">WITHOUT HOLD</span> <span class="hljs-keyword">FOR</span> <span class="hljs-keyword">SELECT</span> <span class="hljs-number">3</span>;
<span class="hljs-keyword">FETCH NEXT</span> <span class="hljs-keyword">FROM</span> c; <span class="hljs-keyword">FETCH PRIOR</span> <span class="hljs-keyword">FROM</span> c; <span class="hljs-keyword">MOVE FORWARD</span> <span class="hljs-number">5</span> <span class="hljs-keyword">IN</span> c; <span class="hljs-keyword">FETCH BACKWARD</span> <span class="hljs-keyword">ALL</span> <span class="hljs-keyword">FROM</span> c; <span class="hljs-keyword">MOVE ABSOLUTE</span> <span class="hljs-number">1</span> <span class="hljs-keyword">IN</span> c; <span class="hljs-keyword">CLOSE</span> c;
""");
    }

    [Fact]
    public void MiscKeywords()
    {
        AssertHighlighter("pgsql",
"""
DISCARD PLANS; DISCARD ALL; CREATE TEMP TABLE t (a int) ON COMMIT PRESERVE ROWS; CREATE TABLE t2 (LIKE t INCLUDING ALL EXCLUDING INDEXES);
SET SESSION NAMES 'x'; SET NAMES 'y'; SET CONSTRAINTS ALL DEFERRED; SET CATALOG 'x'; SELECT * FROM t FOR NO KEY UPDATE; SELECT * FROM t FOR KEY SHARE;
ALTER TABLE t ALTER c SET STORAGE MAIN; CREATE TRIGGER x AFTER UPDATE ON t REFERENCING OLD TABLE AS o NEW TABLE AS n FOR EACH STATEMENT EXECUTE PROCEDURE f();
ALTER TABLE t DETACH PARTITION p; GRANT r TO u GRANTED BY admin; CREATE FUNCTION f() RETURNS int PARALLEL RESTRICTED AS 'x' LANGUAGE sql;
SELECT a IS NOT UNKNOWN, b IS UNKNOWN; CREATE CAST (a AS b) WITHOUT FUNCTION AS IMPLICIT; CREATE POLICY p ON t AS RESTRICTIVE USING (true);
SELECT * FROM t WITH (strip whitespace); SELECT xmlparse(content 'x' preserve whitespace);
""",
"""
<span class="hljs-keyword">DISCARD PLANS</span>; <span class="hljs-keyword">DISCARD</span> <span class="hljs-keyword">ALL</span>; <span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">TEMP</span> <span class="hljs-keyword">TABLE</span> t (a <span class="hljs-type">int</span>) <span class="hljs-keyword">ON</span> <span class="hljs-keyword">COMMIT</span> <span class="hljs-keyword">PRESERVE ROWS</span>; <span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">TABLE</span> t2 (<span class="hljs-keyword">LIKE</span> t <span class="hljs-keyword">INCLUDING ALL</span> <span class="hljs-keyword">EXCLUDING INDEXES</span>);
<span class="hljs-keyword">SET SESSION NAMES</span> <span class="hljs-string">&#x27;x&#x27;</span>; <span class="hljs-keyword">SET NAMES</span> <span class="hljs-string">&#x27;y&#x27;</span>; <span class="hljs-keyword">SET CONSTRAINTS</span> <span class="hljs-keyword">ALL</span> <span class="hljs-keyword">DEFERRED</span>; <span class="hljs-keyword">SET CATALOG</span> <span class="hljs-string">&#x27;x&#x27;</span>; <span class="hljs-keyword">SELECT</span> * <span class="hljs-keyword">FROM</span> t <span class="hljs-keyword">FOR NO KEY</span> <span class="hljs-keyword">UPDATE</span>; <span class="hljs-keyword">SELECT</span> * <span class="hljs-keyword">FROM</span> t <span class="hljs-keyword">FOR KEY</span> <span class="hljs-keyword">SHARE</span>;
<span class="hljs-keyword">ALTER</span> <span class="hljs-keyword">TABLE</span> t <span class="hljs-keyword">ALTER</span> c <span class="hljs-keyword">SET</span> <span class="hljs-keyword">STORAGE MAIN</span>; <span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">TRIGGER</span> x <span class="hljs-keyword">AFTER</span> <span class="hljs-keyword">UPDATE</span> <span class="hljs-keyword">ON</span> t <span class="hljs-keyword">REFERENCING OLD</span> <span class="hljs-keyword">TABLE</span> <span class="hljs-keyword">AS</span> o <span class="hljs-built_in">NEW</span> <span class="hljs-keyword">TABLE</span> <span class="hljs-keyword">AS</span> n <span class="hljs-keyword">FOR</span> <span class="hljs-keyword">EACH</span> <span class="hljs-keyword">STATEMENT</span> <span class="hljs-keyword">EXECUTE</span> <span class="hljs-keyword">PROCEDURE</span> f();
<span class="hljs-keyword">ALTER</span> <span class="hljs-keyword">TABLE</span> t <span class="hljs-keyword">DETACH PARTITION</span> p; <span class="hljs-keyword">GRANT</span> r <span class="hljs-keyword">TO</span> u <span class="hljs-keyword">GRANTED BY</span> <span class="hljs-keyword">admin</span>; <span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> f() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">PARALLEL RESTRICTED</span> <span class="hljs-keyword">AS</span> <span class="hljs-string">&#x27;x&#x27;</span> <span class="hljs-keyword">LANGUAGE</span> <span class="hljs-keyword">sql</span>;
<span class="hljs-keyword">SELECT</span> a <span class="hljs-keyword">IS NOT UNKNOWN</span>, b <span class="hljs-keyword">IS UNKNOWN</span>; <span class="hljs-keyword">CREATE</span> CAST (a <span class="hljs-keyword">AS</span> b) <span class="hljs-keyword">WITHOUT</span> <span class="hljs-keyword">FUNCTION</span> <span class="hljs-keyword">AS IMPLICIT</span>; <span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">POLICY</span> p <span class="hljs-keyword">ON</span> t <span class="hljs-keyword">AS RESTRICTIVE</span> <span class="hljs-keyword">USING</span> (<span class="hljs-keyword">true</span>);
<span class="hljs-keyword">SELECT</span> * <span class="hljs-keyword">FROM</span> t <span class="hljs-keyword">WITH</span> (<span class="hljs-keyword">strip whitespace</span>); <span class="hljs-keyword">SELECT</span> xmlparse(<span class="hljs-keyword">content</span> <span class="hljs-string">&#x27;x&#x27;</span> <span class="hljs-keyword">preserve whitespace</span>);
""");
    }

    [Fact]
    public void NestedBlockCommentAndString()
    {
        AssertHighlighter("pgsql",
"""
/* a 'b' "c" $$ d */ SELECT '/* not a comment */', "-- not a comment", $$ -- also not $$;
""",
"""
<span class="hljs-comment">/* a &#x27;b&#x27; &quot;c&quot; $$ d */</span> <span class="hljs-keyword">SELECT</span> <span class="hljs-string">&#x27;/* not a comment */&#x27;</span>, &quot;-- not a comment&quot;, $$<span class="language-pgsql"> <span class="hljs-comment">-- also not </span></span>$$;
""");
    }

    // Deviation from highlight.js: the closing delimiter is not part of the body.
    [Fact]
    public void DollarInPerlBodyPid()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION f() RETURNS int AS $body$ return $$; $body$ LANGUAGE plperl;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> f() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">AS</span> $body$<span class="language-perl"> <span class="hljs-keyword">return</span> <span class="hljs-variable">$$</span>; </span>$body$ <span class="hljs-keyword">LANGUAGE</span> plperl;
""");
    }

    [Fact]
    public void BashConcatVars()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION f() RETURNS text AS $$
#!/bin/sh
echo "$HOME$PATH$USER"
$$ LANGUAGE plsh;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> f() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">text</span> <span class="hljs-keyword">AS</span> $$<span class="language-bash">
<span class="hljs-comment">#!/bin/sh</span>
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;$HOME$PATH<span class="hljs-variable">$USER</span>&quot;</span>
</span>$$ <span class="hljs-keyword">LANGUAGE</span> plsh;
""");
    }

    [Fact]
    public void LanguageBeforeOtherStatement()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION a() RETURNS int LANGUAGE plperl AS $$ 1 $$; SELECT $$ SELECT 1 $$;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> a() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">LANGUAGE</span> plperl <span class="hljs-keyword">AS</span> $$<span class="language-perl"> <span class="hljs-number">1</span> </span>$$; <span class="hljs-keyword">SELECT</span> $$<span class="language-pgsql"> <span class="hljs-keyword">SELECT</span> <span class="hljs-number">1</span> </span>$$;
""");
    }

    [Fact]
    public void TabsAndNewlines()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION f()	RETURNS int
	LANGUAGE	plpython3u
	AS
$$
return 1
$$;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> f()	<span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span>
	<span class="hljs-keyword">LANGUAGE</span>	plpython3u
	<span class="hljs-keyword">AS</span>
$$<span class="language-python">
<span class="hljs-keyword">return</span> <span class="hljs-number">1</span>
</span>$$;
""");
    }

    [Fact]
    public void DoBlockWithNestedFunction()
    {
        AssertHighlighter("pgsql",
"""
DO $do$
BEGIN
  EXECUTE $q$CREATE FUNCTION f() RETURNS int AS $f$ my $x = 1; $f$ LANGUAGE plperl$q$;
END
$do$;
""",
"""
<span class="hljs-keyword">DO</span> $do$<span class="language-pgsql">
<span class="hljs-keyword">BEGIN</span>
  <span class="hljs-keyword">EXECUTE</span> $q$<span class="language-pgsql"><span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> f() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">int</span> <span class="hljs-keyword">AS</span> $f$<span class="language-perl"> <span class="hljs-keyword">my</span> <span class="hljs-variable">$x</span> = <span class="hljs-number">1</span>; </span>$f$ <span class="hljs-keyword">LANGUAGE</span> plperl</span>$q$;
<span class="hljs-keyword">END</span>
</span>$do$;
""");
    }

    // The LANGUAGE clause after a body is not looked for past another body (`AS $x$`): the first function is PL/pgSQL.
    [Fact]
    public void BodyContainingAnotherBody()
    {
        AssertHighlighter("pgsql",
"""
CREATE FUNCTION f() RETURNS text AS $$ return "AS $x$ y $x$"; $$ LANGUAGE plperl;
CREATE FUNCTION g() RETURNS text LANGUAGE plperl AS $$ return "AS $x$ y $x$"; $$;
""",
"""
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> f() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">text</span> <span class="hljs-keyword">AS</span> $$<span class="language-pgsql"> <span class="hljs-keyword">return</span> &quot;AS $x$ y $x$&quot;; </span>$$ <span class="hljs-keyword">LANGUAGE</span> plperl;
<span class="hljs-keyword">CREATE</span> <span class="hljs-keyword">FUNCTION</span> g() <span class="hljs-keyword">RETURNS</span> <span class="hljs-type">text</span> <span class="hljs-keyword">LANGUAGE</span> plperl <span class="hljs-keyword">AS</span> $$<span class="language-perl"> <span class="hljs-keyword">return</span> <span class="hljs-string">&quot;AS $x$ y $x$&quot;</span>; </span>$$;
""");
    }

    [Theory]
    [InlineData("pgsql")]
    [InlineData("postgres")]
    [InlineData("postgresql")]
    [InlineData("PostgreSQL")]
    public void Aliases(string language)
    {
        AssertHighlighter(language,
"""
SELECT 1;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void CrLf()
    {
        AssertHighlighter("pgsql",
            "SELECT 1;\r\n-- c\r\nSELECT $$a\r\nb$$;\r\n",
            "<span class=\"hljs-keyword\">SELECT</span> <span class=\"hljs-number\">1</span>;\r\n<span class=\"hljs-comment\">-- c</span>\r\n<span class=\"hljs-keyword\">SELECT</span> $$<span class=\"language-pgsql\">a\r\nb</span>$$;\r\n");
    }

    [Fact]
    public void UnterminatedDollarQuotedStringsNestedDeeply_AreHighlightedAsPlainTextPastAFewLevels()
    {
        var code = string.Concat(Enumerable.Range(0, 2_000).Select(i => $"AS $a{i}$ "));

        var result = HighlightWithFallbackDetection(code, "pgsql", out var isFallback);

        Assert.False(isFallback);
        Assert.StartsWith("<span class=\"hljs-keyword\">AS</span> $a0$<span class=\"language-pgsql\"> <span class=\"hljs-keyword\">AS</span> $a1$", result, StringComparison.Ordinal);
        Assert.EndsWith("AS $a1999$ " + string.Concat(Enumerable.Repeat("</span>", 32)), result, StringComparison.Ordinal);
    }
}
