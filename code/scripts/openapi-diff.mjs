// Compares two OpenAPI documents by what a client can observe: operations, parameters,
// request bodies, response codes, and schemas. Used while the API is ported to ASP.NET
// Core (ADR-0006, section 9): the NestJS document is the base, the .NET one the candidate.
//
// Ignored on purpose: descriptions, examples, titles, formats, operation IDs, tags, and
// schema names, which differ between generators without changing what a client sees.
// OpenAPI 3.0 `nullable` and 3.1 `null` types are treated as the same thing, and so are
// `integer` and `number`, since the web app's generated types see both as `number`.
//
// Usage: node scripts/openapi-diff.mjs <base.json> <candidate.json> [--path /v1/worlds ...]

import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

const METHODS = ['get', 'put', 'post', 'delete', 'patch', 'head', 'options'];
const CONSTRAINTS = [
  'minLength',
  'maxLength',
  'pattern',
  'minimum',
  'maximum',
  'exclusiveMinimum',
  'exclusiveMaximum',
  'minItems',
  'maxItems',
  'uniqueItems',
];

/**
 * Returns one line per difference; an empty list means the documents match. With
 * `pathPrefixes` or `operations` (such as `GET /v1/me`), only the paths starting with
 * a prefix and the operations named are compared; with neither, everything is.
 */
export function diffOpenApi(
  base,
  candidate,
  { pathPrefixes = [], operations = [] } = {},
) {
  const diffs = [];
  const everything = pathPrefixes.length === 0 && operations.length === 0;
  const inScope = (path, method) =>
    everything ||
    pathPrefixes.some((prefix) => path.startsWith(prefix)) ||
    operations.includes(`${method.toUpperCase()} ${path}`);
  const paths = union(
    Object.keys(base.paths ?? {}),
    Object.keys(candidate.paths ?? {}),
  );

  for (const path of paths) {
    const a = base.paths?.[path];
    const b = candidate.paths?.[path];
    for (const method of METHODS) {
      if (!inScope(path, method)) continue;
      const opA = a?.[method];
      const opB = b?.[method];
      const where = `${method.toUpperCase()} ${path}`;
      if (!opA && !opB) continue;
      if (!opB) diffs.push(`${where}: missing in candidate`);
      else if (!opA) diffs.push(`${where}: only in candidate`);
      else
        compareOperation(
          { doc: base, op: opA, shared: a.parameters },
          { doc: candidate, op: opB, shared: b.parameters },
          where,
          diffs,
        );
    }
  }
  return diffs;
}

function compareOperation(a, b, where, diffs) {
  const paramsA = parameters(a.doc, a.shared, a.op.parameters);
  const paramsB = parameters(b.doc, b.shared, b.op.parameters);
  for (const key of union(paramsA.keys(), paramsB.keys())) {
    const pa = paramsA.get(key);
    const pb = paramsB.get(key);
    const at = `${where} parameter ${key}`;
    if (!pb) diffs.push(`${at}: missing in candidate`);
    else if (!pa) diffs.push(`${at}: only in candidate`);
    else {
      if (Boolean(pa.required) !== Boolean(pb.required))
        diffs.push(
          `${at}: required ${Boolean(pa.required)} → ${Boolean(pb.required)}`,
        );
      compareSchema(a.doc, pa.schema, b.doc, pb.schema, at, diffs);
    }
  }

  const bodyA = resolve(a.doc, a.op.requestBody);
  const bodyB = resolve(b.doc, b.op.requestBody);
  if (bodyA || bodyB) {
    const at = `${where} request body`;
    if (!bodyB) diffs.push(`${at}: missing in candidate`);
    else if (!bodyA) diffs.push(`${at}: only in candidate`);
    else {
      if (Boolean(bodyA.required) !== Boolean(bodyB.required))
        diffs.push(
          `${at}: required ${Boolean(bodyA.required)} → ${Boolean(bodyB.required)}`,
        );
      compareContent(a.doc, bodyA.content, b.doc, bodyB.content, at, diffs);
    }
  }

  const responsesA = a.op.responses ?? {};
  const responsesB = b.op.responses ?? {};
  for (const status of union(
    Object.keys(responsesA),
    Object.keys(responsesB),
  )) {
    const at = `${where} ${status}`;
    if (!responsesB[status]) diffs.push(`${at}: missing in candidate`);
    else if (!responsesA[status]) diffs.push(`${at}: only in candidate`);
    else
      compareContent(
        a.doc,
        resolve(a.doc, responsesA[status]).content,
        b.doc,
        resolve(b.doc, responsesB[status]).content,
        at,
        diffs,
      );
  }
}

function parameters(doc, shared = [], own = []) {
  const byKey = new Map();
  for (const raw of [...shared, ...own]) {
    const param = resolve(doc, raw);
    byKey.set(`${param.in}:${param.name}`, param);
  }
  return byKey;
}

function compareContent(
  docA,
  contentA = {},
  docB,
  contentB = {},
  where,
  diffs,
) {
  for (const type of union(Object.keys(contentA), Object.keys(contentB))) {
    const at = `${where} ${type}`;
    if (!contentB[type]) diffs.push(`${at}: missing in candidate`);
    else if (!contentA[type]) diffs.push(`${at}: only in candidate`);
    else
      compareSchema(
        docA,
        contentA[type].schema,
        docB,
        contentB[type].schema,
        at,
        diffs,
      );
  }
}

function compareSchema(
  docA,
  rawA,
  docB,
  rawB,
  where,
  diffs,
  pairs = new Set(),
) {
  const a = canonical(docA, rawA);
  const b = canonical(docB, rawB);
  if (!a && !b) return;
  if (!a || !b) {
    diffs.push(
      `${where}: schema ${a ? 'missing in candidate' : 'only in candidate'}`,
    );
    return;
  }
  const refPair = `${a.ref ?? ''}|${b.ref ?? ''}`;
  if (refPair !== '|') {
    // A recursive schema compares each pair of definitions once.
    if (pairs.has(refPair)) return;
    pairs = new Set(pairs).add(refPair);
  }

  if (a.type !== b.type)
    diffs.push(`${where}: type ${show(a.type)} → ${show(b.type)}`);
  if (a.nullable !== b.nullable)
    diffs.push(`${where}: nullable ${a.nullable} → ${b.nullable}`);
  if (!sameSet(a.enum, b.enum))
    diffs.push(`${where}: enum ${show(a.enum)} → ${show(b.enum)}`);
  for (const name of CONSTRAINTS)
    if (a.constraints[name] !== b.constraints[name])
      diffs.push(
        `${where}: ${name} ${show(a.constraints[name])} → ${show(b.constraints[name])}`,
      );

  for (const name of union(
    Object.keys(a.properties),
    Object.keys(b.properties),
  )) {
    const at = `${where}.${name}`;
    if (!(name in b.properties)) diffs.push(`${at}: missing in candidate`);
    else if (!(name in a.properties)) diffs.push(`${at}: only in candidate`);
    else {
      if (a.required.has(name) !== b.required.has(name))
        diffs.push(
          `${at}: required ${a.required.has(name)} → ${b.required.has(name)}`,
        );
      compareSchema(
        docA,
        a.properties[name],
        docB,
        b.properties[name],
        at,
        diffs,
        pairs,
      );
    }
  }

  if (a.items || b.items)
    compareSchema(docA, a.items, docB, b.items, `${where}[]`, diffs, pairs);

  const mapA =
    typeof a.additionalProperties === 'object' ? a.additionalProperties : null;
  const mapB =
    typeof b.additionalProperties === 'object' ? b.additionalProperties : null;
  if (mapA || mapB)
    compareSchema(docA, mapA, docB, mapB, `${where}{}`, diffs, pairs);

  const count = Math.max(a.variants.length, b.variants.length);
  for (let i = 0; i < count; i++)
    compareSchema(
      docA,
      a.variants[i],
      docB,
      b.variants[i],
      `${where} variant ${i + 1}`,
      diffs,
      pairs,
    );
}

/**
 * Reduces a schema to what matters for the comparison. A single-member `allOf` (how
 * NestJS writes a nullable reference) and an `anyOf`/`oneOf` of one schema and `null`
 * (how OpenAPI 3.1 writes it) both become that schema, marked nullable.
 */
function canonical(doc, raw, nullable = false) {
  if (raw === undefined || raw === null) return null;
  const schema = resolve(doc, raw);
  let isNullable = nullable || schema.nullable === true;

  const members = schema.allOf ?? schema.anyOf ?? schema.oneOf;
  if (members) {
    const nonNull = members.filter(
      (member) => resolve(doc, member).type !== 'null',
    );
    if (nonNull.length < members.length) isNullable = true;
    if (nonNull.length === 1 && !schema.properties) {
      const inner = canonical(doc, nonNull[0], isNullable);
      return { ...inner, ref: inner.ref ?? raw.$ref };
    }
  }

  let type = schema.type;
  if (Array.isArray(type)) {
    if (type.includes('null')) isNullable = true;
    const rest = type.filter((t) => t !== 'null');
    type = rest.length === 1 ? rest[0] : rest.sort().join('|');
  }
  if (type === 'integer') type = 'number';
  if (type === undefined && schema.properties) type = 'object';
  // An enum without a type is the type of its values (ASP.NET Core writes it so).
  if (type === undefined && Array.isArray(schema.enum)) {
    const kinds = new Set(
      schema.enum
        .filter((value) => value !== null)
        .map((value) => (typeof value === 'string' ? 'string' : 'number')),
    );
    if (kinds.size === 1) type = [...kinds][0];
  }

  const constraints = {};
  for (const name of CONSTRAINTS) constraints[name] = schema[name];

  return {
    ref: raw.$ref,
    type,
    nullable: isNullable,
    enum: schema.enum?.filter((value) => value !== null),
    constraints,
    properties: schema.properties ?? {},
    required: new Set(schema.required ?? []),
    items: schema.items,
    additionalProperties: schema.additionalProperties,
    variants: members && members.length > 1 ? members : [],
  };
}

function resolve(doc, value) {
  let current = value;
  const seen = new Set();
  while (current?.$ref) {
    if (seen.has(current.$ref))
      throw new Error(`Circular $ref ${current.$ref}`);
    seen.add(current.$ref);
    current = current.$ref
      .replace(/^#\//, '')
      .split('/')
      .map((part) => part.replace(/~1/g, '/').replace(/~0/g, '~'))
      .reduce((node, part) => node?.[part], doc);
  }
  return current;
}

function union(a, b) {
  return [...new Set([...a, ...b])].sort();
}

function sameSet(a, b) {
  if (!a && !b) return true;
  if (!a || !b || a.length !== b.length) return false;
  const values = new Set(a.map((v) => JSON.stringify(v)));
  return b.every((v) => values.has(JSON.stringify(v)));
}

function show(value) {
  return value === undefined ? 'none' : JSON.stringify(value);
}

function main(argv) {
  const files = [];
  const pathPrefixes = [];
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === '--path') pathPrefixes.push(argv[++i]);
    else files.push(argv[i]);
  }
  if (files.length !== 2 || pathPrefixes.includes(undefined)) {
    console.error(
      'Usage: node scripts/openapi-diff.mjs <base.json> <candidate.json> [--path /v1/worlds ...]',
    );
    return 2;
  }
  const [base, candidate] = files.map((file) =>
    JSON.parse(readFileSync(file, 'utf8')),
  );
  const diffs = diffOpenApi(base, candidate, { pathPrefixes });
  for (const line of diffs) console.log(`- ${line}`);
  console.log(
    diffs.length === 0
      ? 'No differences.'
      : `${diffs.length} difference${diffs.length === 1 ? '' : 's'}.`,
  );
  return diffs.length === 0 ? 0 : 1;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href)
  process.exitCode = main(process.argv.slice(2));
