// sql-formatter: formatting of several SQL dialects (a nearley grammar and a tokenizer of regular expressions)
import { format } from "sql-formatter";

const queries = [
  ["sql", "select a, b as c, count(*) from t1 inner join t2 on t1.id = t2.id where x = 1 and (y in (select z from u where w > 2) or v is null) group by a, b having count(*) > 1 order by a desc limit 10"],
  ["postgresql", "with recursive cte(n) as (select 1 union all select n + 1 from cte where n < 5) select n, row_number() over (partition by n % 2 order by n) from cte where data->>'key' = $1::text"],
  ["mysql", "insert into `users` (`name`, `email`) values ('a', 'b'), ('c', 'd') on duplicate key update `email` = values(`email`)"],
  ["tsql", "select top 10 [Id], [Name] from [dbo].[Users] with (nolock) where [Created] > @date; update t set x = 1 output inserted.x where id = 2"],
  ["sqlite", "create table if not exists t (id integer primary key autoincrement, name text not null default 'x', unique(name)); create index i on t(name)"],
  ["bigquery", "select struct(1 as a, 'b' as b) as s, array<int64>[1, 2] from `project.dataset.table` where _partitiontime > timestamp('2020-01-01')"],
];

export default () => {
  const results = queries.map(([language, query]) => format(query, { language }));
  results.push(format(queries[0][1], { language: "sql", keywordCase: "upper", tabWidth: 4, linesBetweenQueries: 2, expressionWidth: 30, logicalOperatorNewline: "after" }));
  results.push(format("select * from t where a = ? and b = ?", { language: "sql", params: ["1", "'x'"] }));
  results.push(format("select * from t where a = :a and b = :b", { language: "sql", paramTypes: { named: [":"] }, params: { a: "1", b: "'x'" } }));
  return results.join("\n----\n");
};
