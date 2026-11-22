const DOC_KEYS = ["description", "$comment"];
export const UNITS = ["m", "m/s", "s", "rad", "kg", "m3/s2", "1", "u"];

function fail(path, message) {
  throw new Error(`${path}: ${message}`);
}

function allowKeys(node, path, keys) {
  for (const key of Object.keys(node)) {
    if (!keys.includes(key) && !DOC_KEYS.includes(key)) {
      fail(path, `unsupported keyword "${key}"`);
    }
  }
}

function readType(node, path) {
  if (typeof node !== "object" || node === null || Array.isArray(node)) {
    fail(path, "expected a schema object");
  }
  if ("$ref" in node) {
    allowKeys(node, path, ["$ref"]);
    const match = /^#\/\$defs\/([A-Za-z][A-Za-z0-9]*)$/.exec(node.$ref);
    if (!match) {
      fail(path, `$ref must point at #/$defs/<Name>, got ${node.$ref}`);
    }
    return { kind: "ref", name: match[1] };
  }
  if ("anyOf" in node) {
    allowKeys(node, path, ["anyOf"]);
    const options = node.anyOf;
    const nullIndex = options.findIndex((option) => option.type === "null" && Object.keys(option).length === 1);
    if (options.length !== 2 || nullIndex === -1) {
      fail(path, 'anyOf is only for nullability: [<type>, {"type": "null"}]');
    }
    const inner = readType(options[1 - nullIndex], `${path}/anyOf/${1 - nullIndex}`);
    if (inner.kind === "nullable" || inner.kind === "array") {
      fail(path, "nullable arrays and nested nullability are not supported; use an empty array");
    }
    return { kind: "nullable", inner };
  }
  switch (node.type) {
    case "string":
      if ("enum" in node) {
        fail(path, "inline enums are not supported; declare the enum in $defs and $ref it");
      }
      allowKeys(node, path, ["type"]);
      return { kind: "string" };
    case "number":
      allowKeys(node, path, ["type", "x-unit"]);
      if (!UNITS.includes(node["x-unit"])) {
        fail(path, `number needs "x-unit", one of: ${UNITS.join(" ")}`);
      }
      return { kind: "number", unit: node["x-unit"] };
    case "boolean":
      allowKeys(node, path, ["type"]);
      return { kind: "boolean" };
    case "integer":
      allowKeys(node, path, ["type", "format"]);
      if (node.format !== "int32" && node.format !== "uint32") {
        fail(path, 'integer needs "format": "int32" or "uint32"');
      }
      return { kind: node.format };
    case "array":
      allowKeys(node, path, ["type", "items", "x-uniqueBy"]);
      return { kind: "array", items: readType(node.items, `${path}/items`), uniqueBy: node["x-uniqueBy"] ?? null };
    default:
      return fail(path, `unsupported type ${JSON.stringify(node.type)}`);
  }
}

function readObject(name, node, path) {
  allowKeys(node, path, ["type", "properties", "required", "additionalProperties"]);
  if (node.additionalProperties !== false) {
    fail(path, '"additionalProperties": false is required');
  }
  const names = Object.keys(node.properties ?? {});
  const required = node.required ?? [];
  const missing = names.filter((field) => !required.includes(field));
  if (missing.length > 0 || required.length !== names.length) {
    fail(path, `every property must be required (use anyOf with null for absence): ${missing.join(", ")}`);
  }
  let tag = null;
  const fields = [];
  for (const field of names) {
    const fieldPath = `${path}/properties/${field}`;
    const fieldNode = node.properties[field];
    if (!/^[a-z][A-Za-z0-9]*$/.test(field)) {
      fail(fieldPath, "property names are camelCase");
    }
    if ("const" in fieldNode) {
      allowKeys(fieldNode, fieldPath, ["const"]);
      if (tag !== null || typeof fieldNode.const !== "string") {
        fail(fieldPath, "an object has at most one const, and it is a string tag");
      }
      tag = { property: field, value: fieldNode.const };
      continue;
    }
    fields.push({ name: field, description: fieldNode.description ?? null, type: readType(fieldNode, fieldPath) });
  }
  return { name, kind: "object", description: node.description ?? null, fields, tag, union: null };
}

function readDef(name, node) {
  const path = `#/$defs/${name}`;
  if (!/^[A-Z][A-Za-z0-9]*$/.test(name)) {
    fail(path, "definition names are PascalCase");
  }
  if (node.type === "object") {
    return readObject(name, node, path);
  }
  if (node.type === "string" && "enum" in node) {
    allowKeys(node, path, ["type", "enum"]);
    for (const value of node.enum) {
      if (typeof value !== "string" || !/^[a-z][A-Za-z0-9]*$/.test(value)) {
        fail(path, `enum values are camelCase strings, got ${JSON.stringify(value)}`);
      }
    }
    return { name, kind: "enum", description: node.description ?? null, values: node.enum };
  }
  if ("oneOf" in node) {
    allowKeys(node, path, ["oneOf", "discriminator", "x-messages"]);
    const discriminator = node.discriminator?.propertyName;
    if (typeof discriminator !== "string" || Object.keys(node.discriminator).length !== 1) {
      fail(path, 'a union needs "discriminator": {"propertyName": "<tag property>"}');
    }
    const members = node.oneOf.map((member, index) => {
      const type = readType(member, `${path}/oneOf/${index}`);
      if (type.kind !== "ref") {
        fail(path, "oneOf members must be $refs to object definitions");
      }
      return type.name;
    });
    if ("x-messages" in node && node["x-messages"] !== true) {
      fail(path, '"x-messages" is true or absent');
    }
    return { name, kind: "union", description: node.description ?? null, discriminator, members, message: node["x-messages"] === true };
  }
  return fail(path, "a definition is an object, a string enum, or a oneOf union");
}

function forEachRef(type, visit) {
  if (type.kind === "ref") {
    visit(type.name);
  } else if (type.kind === "array") {
    forEachRef(type.items, visit);
  } else if (type.kind === "nullable") {
    forEachRef(type.inner, visit);
  }
}

function checkUniqueBy(array, defs, path) {
  const item = array.items.kind === "ref" ? defs.get(array.items.name) : null;
  const key = item?.kind === "object" ? item.fields.find((field) => field.name === array.uniqueBy) : null;
  if (!key || !["string", "int32", "uint32"].includes(key.type.kind)) {
    fail(path, '"x-uniqueBy" names a string or integer property of the array\'s object items');
  }
}

export function readSchema(schema) {
  allowKeys(schema, "#", ["$schema", "$id", "title", "x-protocolVersion", "x-maxDepth", "$defs"]);
  const version = schema["x-protocolVersion"];
  if (!Number.isInteger(version) || version < 1) {
    fail("#/x-protocolVersion", "must be a positive integer");
  }
  const maxDepth = schema["x-maxDepth"];
  if (!Number.isInteger(maxDepth) || maxDepth < 1) {
    fail("#/x-maxDepth", "must be a positive integer");
  }

  const defs = new Map();
  for (const [name, node] of Object.entries(schema.$defs ?? {})) {
    defs.set(name, readDef(name, node));
  }

  const referenced = new Set();
  for (const def of defs.values()) {
    if (def.kind !== "object") {
      continue;
    }
    for (const field of def.fields) {
      const fieldPath = `#/$defs/${def.name}/properties/${field.name}`;
      forEachRef(field.type, (target) => {
        if (!defs.has(target)) {
          fail(fieldPath, `unknown $ref ${target}`);
        }
        referenced.add(target);
      });
      if (field.type.kind === "array" && field.type.uniqueBy !== null) {
        checkUniqueBy(field.type, defs, fieldPath);
      }
    }
  }

  for (const union of defs.values()) {
    if (union.kind !== "union") {
      continue;
    }
    const path = `#/$defs/${union.name}`;
    if (union.message && referenced.has(union.name)) {
      fail(path, "a message union is a top-level message set; no field can refer to it");
    }
    if (union.message && union.discriminator !== "type") {
      fail(path, 'a message union is discriminated by "type"');
    }
    const tags = new Set();
    for (const member of union.members) {
      const object = defs.get(member);
      if (!object || object.kind !== "object") {
        fail(path, `${member} is not an object definition`);
      }
      if (object.union) {
        fail(path, `${member} is already a member of ${object.union.name}`);
      }
      if (referenced.has(member)) {
        fail(path, `${member} is a union member and cannot be referenced directly`);
      }
      if (object.tag?.property !== union.discriminator) {
        fail(`#/$defs/${member}`, `needs "${union.discriminator}": {"const": "<tag>"}`);
      }
      if (tags.has(object.tag.value)) {
        fail(path, `duplicate tag "${object.tag.value}"`);
      }
      tags.add(object.tag.value);
      object.union = union;
      if (union.message) {
        const versionField = object.fields.find((field) => field.name === "protocolVersion");
        if (versionField?.type.kind !== "int32") {
          fail(`#/$defs/${member}`, 'a message needs "protocolVersion": {"type": "integer", "format": "int32"}');
        }
        object.fields = object.fields.filter((field) => field.name !== "protocolVersion");
      }
    }
    union.members = union.members.map((member) => defs.get(member));
  }

  for (const def of defs.values()) {
    if (def.kind === "object" && def.tag !== null && !def.union) {
      fail(`#/$defs/${def.name}`, "a type with a const tag must belong to a union");
    }
  }

  return { version, maxDepth, defs: [...defs.values()] };
}
