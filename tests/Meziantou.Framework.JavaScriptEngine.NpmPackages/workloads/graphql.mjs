// graphql-js: SDL parsing, schema building, validation, execution (fragments, variables, directives, interfaces,
// unions, lists, errors), introspection and printing
import * as graphql from "graphql";

const { buildSchema, graphqlSync, parse, print, validate, specifiedRules, getIntrospectionQuery, introspectionFromSchema, printSchema, buildClientSchema, visit, Kind } = graphql;

const schema = buildSchema(`
  """A thing with an id"""
  interface Node { id: ID! }
  enum Episode { NEWHOPE EMPIRE JEDI }
  type Character implements Node { id: ID! name: String! friends: [Character!]! appearsIn: [Episode!]! }
  type Starship implements Node { id: ID! name: String! length(unit: Unit = METER): Float }
  enum Unit { METER FOOT }
  union SearchResult = Character | Starship
  input ReviewInput { stars: Int! commentary: String }
  type Review { episode: Episode stars: Int! commentary: String }
  type Query {
    hero(episode: Episode): Character
    search(text: String!): [SearchResult!]!
    node(id: ID!): Node
    failing: String
  }
  type Mutation { createReview(episode: Episode, review: ReviewInput!): Review }
`);

const characters = {
  1: { id: "1", name: "Luke", friends: ["2", "3"], appearsIn: ["NEWHOPE", "EMPIRE", "JEDI"] },
  2: { id: "2", name: "Leia", friends: ["1"], appearsIn: ["NEWHOPE"] },
  3: { id: "3", name: "Han", friends: ["1", "2"], appearsIn: ["EMPIRE"] },
};
const starships = { 10: { id: "10", name: "Falcon", length: 34.37 } };
const resolveCharacter = id => ({ ...characters[id], friends: () => characters[id].friends.map(resolveCharacter) });

const rootValue = {
  hero: ({ episode }) => resolveCharacter(episode === "EMPIRE" ? 1 : 2),
  search: ({ text }) => [...Object.keys(characters).map(resolveCharacter), ...Object.values(starships).map(ship => ({ ...ship, length: ({ unit }) => unit === "FOOT" ? ship.length * 3.28084 : ship.length }))].filter(item => item.name.includes(text)),
  node: ({ id }) => characters[id] ? resolveCharacter(id) : starships[id],
  failing: () => { throw new Error("Resolver failed"); },
  createReview: ({ episode, review }) => ({ episode, ...review }),
};

const typeResolver = value => "friends" in value ? "Character" : "Starship";

const query = `
  query Hero($episode: Episode = EMPIRE, $withFriends: Boolean!) {
    hero(episode: $episode) { ...Fields friends @include(if: $withFriends) { name friends { name } } }
    search(text: "a") { __typename ... on Character { name appearsIn } ... on Starship { name feet: length(unit: FOOT) } }
    node(id: "10") { id ... on Starship { length } }
    failing
  }
  fragment Fields on Character { id name }
  mutation Review { createReview(episode: JEDI, review: { stars: 5, commentary: "Great" }) { episode stars commentary } }
`;

export default () => {
  const results = [];
  const run = (source, operationName, variableValues) => JSON.stringify(graphqlSync({ schema, source, rootValue, operationName, variableValues, typeResolver }));
  results.push(run(query, "Hero", { withFriends: true }));
  results.push(run(query, "Review"));
  results.push(run("{ hero { unknownField } search }"));
  results.push(run("query ($e: Episode!) { hero(episode: $e) { name } }", undefined, { e: "INVALID" }));

  const document = parse(query);
  results.push(print(visit(document, { [Kind.FIELD]: { leave: node => node.name.value === "name" ? { ...node, alias: { kind: Kind.NAME, value: "fullName" } } : undefined } })));
  results.push(JSON.stringify(validate(schema, parse("{ hero { name } hero(episode: 1) { id } }"), specifiedRules).map(error => error.message)));

  const introspection = graphqlSync({ schema, source: getIntrospectionQuery() });
  results.push(String(JSON.stringify(introspection.data) === JSON.stringify(introspectionFromSchema(schema))));
  results.push(printSchema(buildClientSchema(introspection.data)));
  return results.join("\n----\n");
};
