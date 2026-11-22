const HEADER = `#nullable enable
using System;
using System.Collections.Generic;

namespace FLTCOM.Core.Protocol
{
`;

const KEYWORDS = new Set(["base", "checked", "class", "default", "event", "fixed", "in", "is", "lock", "object", "operator", "out", "params", "ref", "string", "this"]);

const pascal = (name) => name[0].toUpperCase() + name.slice(1);
const param = (name) => (KEYWORDS.has(name) ? `@${name}` : name);

function xml(text) {
  return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

function doc(description, unit, indent) {
  const parts = [description, unit && unit !== "1" ? `Unit: ${unit}.` : null].filter(Boolean);
  return parts.length === 0 ? "" : `${indent}/// <summary>${xml(parts.join(" "))}</summary>\n`;
}

function csType(type) {
  switch (type.kind) {
    case "string":
      return "string";
    case "number":
      return "double";
    case "int32":
      return "int";
    case "uint32":
      return "uint";
    case "boolean":
      return "bool";
    case "ref":
      return type.name;
    case "array":
      return `IReadOnlyList<${csType(type.items)}>`;
    case "nullable":
      return `${csType(type.inner)}?`;
  }
  throw new Error(`unhandled type ${type.kind}`);
}

function isReference(type, defs) {
  return type.kind === "string" || type.kind === "array" || (type.kind === "ref" && defs.get(type.name).kind !== "enum");
}

function guard(type, value, name, defs) {
  if (type.kind === "number") {
    return `Require.Finite(${value}, ${JSON.stringify(name)})`;
  }
  if (type.kind === "nullable" && type.inner.kind === "number") {
    return `${value} is double ${name}Value ? Require.Finite(${name}Value, ${JSON.stringify(name)}) : (double?)null`;
  }
  if (type.kind === "array") {
    const method = isReference(type.items, defs) ? "Items" : "Values";
    const items = `Require.${method}(${value}, ${JSON.stringify(name)})`;
    return type.uniqueBy === null ? items : `Require.UniqueBy(${items}, item => item.${pascal(type.uniqueBy)}, ${JSON.stringify(name)})`;
  }
  if (isReference(type, defs)) {
    return `${value} ?? throw new ArgumentNullException(${JSON.stringify(name)})`;
  }
  if (type.kind === "ref") {
    return `Require.Defined(${value}, ${JSON.stringify(name)})`;
  }
  return value;
}

function writeStatement(type, value, defs, depth = 0) {
  switch (type.kind) {
    case "string":
      return `w.String(${value});`;
    case "number":
      return `w.Number(${value});`;
    case "int32":
    case "uint32":
      return `w.Number((long)${value});`;
    case "boolean":
      return `w.Bool(${value});`;
    case "ref":
      return defs.get(type.name).kind === "enum" ? `w.String(Wire.Name(${value}));` : `${value}.Write(w);`;
    case "array": {
      const item = `item${depth}`;
      return `w.BeginArray(); foreach (${csType(type.items)} ${item} in ${value}) { ${writeStatement(type.items, item, defs, depth + 1)} } w.EndArray();`;
    }
    case "nullable": {
      const inner = type.inner;
      const isValue = !isReference(inner, defs);
      const unwrapped = isValue ? `${value}.Value` : value;
      return `if (${value} is null) { w.Null(); } else { ${writeStatement(inner, unwrapped, defs, depth)} }`;
    }
  }
  throw new Error(`unhandled type ${type.kind}`);
}

function readExpression(type, node, path, defs, depth = 0) {
  switch (type.kind) {
    case "string":
      return `${node}.AsString(${path})`;
    case "number":
      return `${node}.AsNumber(${path})`;
    case "int32":
      return `${node}.AsInt(${path})`;
    case "uint32":
      return `${node}.AsUInt(${path})`;
    case "boolean":
      return `${node}.AsBool(${path})`;
    case "ref":
      return defs.get(type.name).kind === "enum"
        ? `Wire.Read${type.name}(${node}.AsString(${path}), ${path})`
        : `${type.name}.Read(${node}, ${path})`;
    case "array": {
      const item = `n${depth}`;
      const index = `i${depth}`;
      const items = `${node}.AsArray(${path}, (${item}, ${index}) => ${readExpression(type.items, item, `${path} + "[" + ${index} + "]"`, defs, depth + 1)})`;
      return type.uniqueBy === null
        ? items
        : `JsonNode.UniqueBy(${items}, item => item.${pascal(type.uniqueBy)}, ${path}, ${JSON.stringify(type.uniqueBy)})`;
    }
    case "nullable":
      return `${node}.Kind == JsonKind.Null ? null : ${isReference(type.inner, defs) ? "" : `(${csType(type)})`}${readExpression(type.inner, node, path, defs, depth)}`;
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
  const i1 = "    ";
  const i2 = "        ";
  const i3 = "            ";
  const base = def.union ? ` : ${def.union.name}` : "";
  const lines = [doc(def.description, null, i1) + `${i1}public sealed class ${def.name}${base}`, `${i1}{`];
  const keys = wireKeys(def);
  lines.push(`${i2}private static readonly string[] Fields = { ${keys.map((key) => JSON.stringify(key)).join(", ")} };`, "");

  const params = def.fields.map((field) => `${csType(field.type)} ${param(field.name)}`).join(", ");
  lines.push(`${i2}public ${def.name}(${params})`, `${i2}{`);
  for (const field of def.fields) {
    lines.push(`${i3}${pascal(field.name)} = ${guard(field.type, param(field.name), field.name, defs)};`);
  }
  lines.push(`${i2}}`, "");

  for (const field of def.fields) {
    const unit = field.type.unit ?? field.type.inner?.unit;
    lines.push(doc(field.description, unit, i2) + `${i2}public ${csType(field.type)} ${pascal(field.name)} { get; }`, "");
  }

  if (def.union) {
    const tag = def.tag.value;
    lines.push(
      `${i2}public override T Match<T>(${matchParams(def.union)}) => (${param(tag)} ?? throw new ArgumentNullException(nameof(${param(tag)})))(this);`,
      "",
    );
  }

  const override = def.union ? "override " : "";
  lines.push(`${i2}internal ${override}void Write(JsonWriter w)`, `${i2}{`, `${i3}w.BeginObject();`);
  if (def.union?.message) {
    lines.push(`${i3}w.Name("protocolVersion"); w.Number((long)ProtocolInfo.Version);`);
  }
  if (def.tag) {
    lines.push(`${i3}w.Name(${JSON.stringify(def.tag.property)}); w.String(${JSON.stringify(def.tag.value)});`);
  }
  for (const field of def.fields) {
    lines.push(`${i3}w.Name(${JSON.stringify(field.name)}); ${writeStatement(field.type, pascal(field.name), defs)}`);
  }
  lines.push(`${i3}w.EndObject();`, `${i2}}`, "");

  lines.push(`${i2}internal static ${def.union ? "new " : ""}${def.name} Read(JsonNode node, string path)`, `${i2}{`, `${i3}node.ExpectFields(path, Fields);`);
  const args = def.fields.map(
    (field) => `${i3}    ${readExpression(field.type, `node.Field(path, ${JSON.stringify(field.name)})`, `path + ".${field.name}"`, defs)}`,
  );
  lines.push(`${i3}return new ${def.name}(`, args.join(",\n") + ");", `${i2}}`, `${i1}}`, "");
  return lines.join("\n");
}

function matchParams(union) {
  return union.members.map((member) => `Func<${member.name}, T> ${param(member.tag.value)}`).join(", ");
}

function emitUnion(def) {
  const i1 = "    ";
  const i2 = "        ";
  const i3 = "            ";
  const lines = [doc(def.description, null, i1) + `${i1}public abstract class ${def.name}`, `${i1}{`];
  lines.push(`${i2}private protected ${def.name}()`, `${i2}{`, `${i2}}`, "");
  lines.push(`${i2}public abstract T Match<T>(${matchParams(def)});`, "");
  lines.push(`${i2}internal abstract void Write(JsonWriter w);`, "");
  lines.push(`${i2}internal static ${def.name} Read(JsonNode node, string path)`, `${i2}{`);
  lines.push(`${i3}string tag = node.Field(path, ${JSON.stringify(def.discriminator)}).AsString(path + ".${def.discriminator}");`);
  lines.push(`${i3}switch (tag)`, `${i3}{`);
  for (const member of def.members) {
    lines.push(`${i3}    case ${JSON.stringify(member.tag.value)}:`, `${i3}        return ${member.name}.Read(node, path);`);
  }
  const tags = def.members.map((member) => member.tag.value).join(", ");
  lines.push(
    `${i3}    default:`,
    `${i3}        throw new ProtocolException(path + ".${def.discriminator}: unknown value '" + tag + "', expected one of ${tags}");`,
    `${i3}}`,
    `${i2}}`,
    `${i1}}`,
    "",
  );
  return lines.join("\n");
}

function emitEnum(def) {
  const i1 = "    ";
  return (
    doc(def.description, null, i1) +
    `${i1}public enum ${def.name}\n${i1}{\n` +
    def.values.map((value) => `${i1}    ${pascal(value)},`).join("\n") +
    `\n${i1}}\n`
  );
}

function emitWire(enums) {
  const i1 = "    ";
  const i2 = "        ";
  const i3 = "            ";
  const lines = [`${i1}internal static class Wire`, `${i1}{`];
  for (const def of enums) {
    lines.push(`${i2}public static string Name(${def.name} value)`, `${i2}{`, `${i3}switch (value)`, `${i3}{`);
    for (const value of def.values) {
      lines.push(`${i3}    case ${def.name}.${pascal(value)}: return ${JSON.stringify(value)};`);
    }
    lines.push(`${i3}    default: throw new ArgumentOutOfRangeException(nameof(value), value, null);`, `${i3}}`, `${i2}}`, "");

    lines.push(`${i2}public static ${def.name} Read${def.name}(string value, string path)`, `${i2}{`, `${i3}switch (value)`, `${i3}{`);
    for (const value of def.values) {
      lines.push(`${i3}    case ${JSON.stringify(value)}: return ${def.name}.${pascal(value)};`);
    }
    lines.push(
      `${i3}    default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of ${def.values.join(", ")}");`,
      `${i3}}`,
      `${i2}}`,
      "",
    );
  }
  lines.push(`${i1}}`, "");
  return lines.join("\n");
}

function emitCodec(unions) {
  const i1 = "    ";
  const i2 = "        ";
  const i3 = "            ";
  const lines = [
    `${i1}/// <summary>Reads and writes protocol messages. Reading checks the protocol version before the shape.</summary>`,
    `${i1}public static class ProtocolJson`,
    `${i1}{`,
  ];
  for (const union of unions) {
    lines.push(
      `${i2}public static string Write(${union.name} message)`,
      `${i2}{`,
      `${i3}var w = new JsonWriter();`,
      `${i3}(message ?? throw new ArgumentNullException(nameof(message))).Write(w);`,
      `${i3}return w.ToString();`,
      `${i2}}`,
      "",
      `${i2}/// <exception cref="ProtocolVersionException">The message has another protocol version.</exception>`,
      `${i2}/// <exception cref="ProtocolException">The message is not valid JSON or does not match the schema.</exception>`,
      `${i2}public static ${union.name} Read${union.name}(string json)`,
      `${i2}{`,
      `${i3}JsonNode node = JsonNode.ParseMessage(json, ProtocolInfo.Version);`,
      `${i3}return ${union.name}.Read(node, "$");`,
      `${i2}}`,
      "",
    );
  }
  lines.push(`${i1}}`, "");
  return lines.join("\n");
}

export function emitCSharp(protocol) {
  const defs = new Map(protocol.defs.map((def) => [def.name, def]));
  const out = [
    HEADER,
    `    public static class ProtocolInfo\n    {\n        public const int Version = ${protocol.version};\n        public const int MaxDepth = ${protocol.maxDepth};\n    }\n`,
  ];
  for (const def of protocol.defs) {
    if (def.kind === "enum") {
      out.push(emitEnum(def));
    } else if (def.kind === "union") {
      out.push(emitUnion(def));
    } else {
      out.push(emitObject(def, defs));
    }
  }
  const unions = protocol.defs.filter((def) => def.kind === "union" && def.message);
  out.push(emitWire(protocol.defs.filter((def) => def.kind === "enum")));
  out.push(emitCodec(unions).replace(/\n$/, ""));
  out.push("}\n");
  return out.join("\n");
}
