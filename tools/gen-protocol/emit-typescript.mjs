function constName(name) {
  const snake = name.replace(/([a-z0-9])([A-Z])/g, "$1_$2").toUpperCase();
  return snake.endsWith("S") ? `${snake}_VALUES` : `${snake}S`;
}

function doc(description, unit, indent = "") {
  const parts = [description, unit && unit !== "1" ? `Unit: ${unit}.` : null].filter(Boolean);
  return parts.length === 0 ? "" : `${indent}/** ${parts.join(" ")} */\n`;
}

function tsType(type) {
  switch (type.kind) {
    case "string":
      return "string";
    case "number":
    case "int32":
    case "uint32":
      return "number";
    case "boolean":
      return "boolean";
    case "ref":
      return type.name;
    case "array":
      return `readonly ${tsType(type.items)}[]`;
    case "nullable":
      return `${tsType(type.inner)} | null`;
  }
  throw new Error(`unhandled type ${type.kind}`);
}

function checkExpression(type, value, path, defs) {
  switch (type.kind) {
    case "string":
    case "number":
    case "int32":
    case "uint32":
    case "boolean":
      return `${type.kind}Value(${value}, ${path})`;
    case "ref": {
      const def = defs.get(type.name);
      return def.kind === "enum"
        ? `oneOf(${value}, ${path}, ${constName(def.name)})`
        : `check${def.name}(${value}, ${path})`;
    }
    case "array": {
      const items = `arrayOf(${value}, ${path}, (item, itemPath) => ${checkExpression(type.items, "item", "itemPath", defs)})`;
      return type.uniqueBy === null ? items : `${items} && uniqueBy(${value} as readonly Record<string, unknown>[], ${path}, ${JSON.stringify(type.uniqueBy)})`;
    }
    case "nullable":
      return `${value} === null || ${checkExpression(type.inner, value, path, defs)}`;
  }
  throw new Error(`unhandled type ${type.kind}`);
}

function wireKeys(def) {
  const keys = def.fields.map((field) => field.name);
  if (def.tag) {
    keys.unshift(def.tag.property);
  }
  if (def.union?.message) {
    keys.unshift("protocolVersion");
  }
  return keys;
}

function emitObject(def, defs) {
  const lines = [doc(def.description) + `export interface ${def.name} {`];
  if (def.union?.message) {
    lines.push("  readonly protocolVersion: typeof PROTOCOL_VERSION;");
  }
  if (def.tag) {
    lines.push(`  readonly ${def.tag.property}: ${JSON.stringify(def.tag.value)};`);
  }
  for (const field of def.fields) {
    lines.push(doc(field.description, field.type.unit ?? field.type.inner?.unit, "  ") + `  readonly ${field.name}: ${tsType(field.type)};`);
  }
  lines.push("}", "");

  lines.push(`function check${def.name}(value: unknown, path: string): true {`);
  lines.push(`  const o = objectValue(value, path, ${JSON.stringify(wireKeys(def))});`);
  for (const field of def.fields) {
    lines.push(`  ${checkExpression(field.type, `o.${field.name}`, `path + ".${field.name}"`, defs)};`);
  }
  lines.push("  return true;", "}", "");
  return lines.join("\n");
}

function emitUnion(def) {
  const lines = [doc(def.description) + `export type ${def.name} = ${def.members.map((member) => member.name).join(" | ")};`, ""];
  const tags = def.members.map((member) => member.tag.value);
  const prop = def.discriminator;
  lines.push(`function check${def.name}(value: unknown, path: string): true {`);
  lines.push(`  switch ((value as { ${prop}?: unknown } | null)?.${prop}) {`);
  for (const member of def.members) {
    lines.push(`    case ${JSON.stringify(member.tag.value)}:`, `      return check${member.name}(value, path);`);
  }
  lines.push(`    default:`, `      return fail(path + ".${prop}", ${JSON.stringify(tags.map((tag) => JSON.stringify(tag)).join(" | "))});`);
  lines.push("  }", "}", "");
  if (def.message) {
    const fn = `parse${def.name}`;
    lines.push(
      `/** Parses one message. The protocol version is checked before the shape. */`,
      `export function ${fn}(text: string): Parsed<${def.name}> {`,
      `  return parseMessage(text, check${def.name}) as Parsed<${def.name}>;`,
      "}",
      "",
    );
  }
  return lines.join("\n");
}

const RUNTIME = `export type ParseError =
  | { readonly kind: "versionMismatch"; readonly received: number }
  | { readonly kind: "malformed"; readonly path: string; readonly expected: string };

export type Parsed<M> = { readonly ok: true; readonly message: M } | { readonly ok: false; readonly error: ParseError };

class Malformed {
  readonly path: string;
  readonly expected: string;
  constructor(path: string, expected: string) {
    this.path = path;
    this.expected = expected;
  }
}

function fail(path: string, expected: string): never {
  throw new Malformed(path, expected);
}

function objectValue(value: unknown, path: string, keys: readonly string[]): Record<string, unknown> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return fail(path, "object");
  }
  for (const key in value) {
    if (!keys.includes(key)) {
      fail(path + "." + key, "no such field");
    }
  }
  return value as Record<string, unknown>;
}

function stringValue(value: unknown, path: string): true {
  return typeof value === "string" || fail(path, "string");
}

function numberValue(value: unknown, path: string): true {
  return Number.isFinite(value) || fail(path, "finite number");
}

function int32Value(value: unknown, path: string): true {
  return (Number.isInteger(value) && (value as number) >= -2147483648 && (value as number) <= 2147483647) || fail(path, "int32");
}

function uint32Value(value: unknown, path: string): true {
  return (Number.isInteger(value) && (value as number) >= 0 && (value as number) <= 4294967295) || fail(path, "uint32");
}

function booleanValue(value: unknown, path: string): true {
  return typeof value === "boolean" || fail(path, "boolean");
}

function oneOf(value: unknown, path: string, values: readonly string[]): true {
  return values.includes(value as string) || fail(path, values.map((v) => JSON.stringify(v)).join(" | "));
}

function arrayOf(value: unknown, path: string, check: (item: unknown, itemPath: string) => true): true {
  if (!Array.isArray(value)) {
    return fail(path, "array");
  }
  for (let i = 0; i < value.length; i++) {
    check(value[i], path + "[" + i + "]");
  }
  return true;
}

function uniqueBy(items: readonly Record<string, unknown>[], path: string, key: string): true {
  const seen = new Set<unknown>();
  for (let i = 0; i < items.length; i++) {
    if (seen.has(items[i][key])) {
      fail(path + "[" + i + "]." + key, "unique value");
    }
    seen.add(items[i][key]);
  }
  return true;
}

function nestsTooDeep(text: string): boolean {
  let depth = 0;
  let inString = false;
  for (let i = 0; i < text.length; i++) {
    const c = text.charCodeAt(i);
    if (inString) {
      if (c === 0x5c) {
        i++;
      } else if (c === 0x22) {
        inString = false;
      }
    } else if (c === 0x22) {
      inString = true;
    } else if (c === 0x7b || c === 0x5b) {
      if (++depth > MAX_DEPTH) {
        return true;
      }
    } else if (c === 0x7d || c === 0x5d) {
      depth--;
    }
  }
  return false;
}

function parseMessage(text: string, check: (value: unknown, path: string) => true): Parsed<unknown> {
  if (nestsTooDeep(text)) {
    return { ok: false, error: { kind: "malformed", path: "$", expected: "JSON nested at most " + MAX_DEPTH + " deep" } };
  }
  let value: unknown;
  try {
    value = JSON.parse(text);
  } catch {
    return { ok: false, error: { kind: "malformed", path: "$", expected: "JSON" } };
  }
  const received = (value as { protocolVersion?: unknown } | null)?.protocolVersion;
  if (!Number.isInteger(received)) {
    return { ok: false, error: { kind: "malformed", path: "$.protocolVersion", expected: "int32" } };
  }
  if (received !== PROTOCOL_VERSION) {
    return { ok: false, error: { kind: "versionMismatch", received: received as number } };
  }
  try {
    check(value, "$");
  } catch (error) {
    if (error instanceof Malformed) {
      return { ok: false, error: { kind: "malformed", path: error.path, expected: error.expected } };
    }
    throw error;
  }
  return { ok: true, message: value };
}
`;

export function emitTypeScript(protocol) {
  const defs = new Map(protocol.defs.map((def) => [def.name, def]));

  const out = [`export const PROTOCOL_VERSION = ${protocol.version};`, `export const MAX_DEPTH = ${protocol.maxDepth};`, ""];
  for (const def of protocol.defs) {
    if (def.kind === "enum") {
      const list = constName(def.name);
      out.push(
        doc(def.description) + `export const ${list} = ${JSON.stringify(def.values)} as const;`,
        `export type ${def.name} = (typeof ${list})[number];`,
        "",
      );
    } else if (def.kind === "object") {
      out.push(emitObject(def, defs));
    } else {
      out.push(emitUnion(def));
    }
  }
  out.push(RUNTIME);
  return out.join("\n");
}
