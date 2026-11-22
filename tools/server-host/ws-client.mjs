import { randomBytes } from "node:crypto";
import { connect } from "node:net";

const OPCODE_TEXT = 0x1;
const OPCODE_CLOSE = 0x8;

export function encodeClientFrame(opcode, payload, { fin = true } = {}) {
  const length = payload.length;
  const lengthBytes = length < 126 ? 0 : length < 65536 ? 2 : 8;
  const header = Buffer.alloc(2 + lengthBytes + 4);
  header[0] = (fin ? 0x80 : 0) | opcode;
  if (lengthBytes === 0) {
    header[1] = 0x80 | length;
  } else if (lengthBytes === 2) {
    header[1] = 0x80 | 126;
    header.writeUInt16BE(length, 2);
  } else {
    header[1] = 0x80 | 127;
    header.writeBigUInt64BE(BigInt(length), 2);
  }
  const mask = randomBytes(4);
  mask.copy(header, 2 + lengthBytes);
  const body = Buffer.from(payload);
  for (let i = 0; i < body.length; i++) {
    body[i] ^= mask[i % 4];
  }
  return Buffer.concat([header, body]);
}

/**
 * Opens a WebSocket over a raw TCP socket, so callers control the Origin header and can send
 * frames a browser never would. Resolves once the server answers the upgrade.
 * `onFrame` gets each server frame with the time its first byte arrived.
 */
export function openSocket({ port, host = "127.0.0.1", origin = `http://127.0.0.1:${port}`, path = "/ws", onFrame = () => {} }) {
  return new Promise((resolve, reject) => {
    const socket = connect({ port, host });
    socket.setNoDelay(true);
    let head = Buffer.alloc(0);
    let buffer = Buffer.alloc(0);
    let upgraded = false;
    let frameStart = null;
    let closeCode = null;
    const closed = new Promise((done) => socket.on("close", () => done(closeCode)));

    const send = (bytes) => socket.write(bytes);
    const client = {
      socket,
      closed,
      sendText: (text) => send(encodeClientFrame(OPCODE_TEXT, Buffer.from(text, "utf8"))),
      sendFrame: (opcode, payload, options) => send(encodeClientFrame(opcode, payload, options)),
      close: (code = 1000) => {
        const payload = Buffer.alloc(2);
        payload.writeUInt16BE(code, 0);
        send(encodeClientFrame(OPCODE_CLOSE, payload));
      },
    };

    const readFrames = () => {
      while (buffer.length >= 2) {
        frameStart ??= performance.now();
        let length = buffer[1] & 0x7f;
        let offset = 2;
        if (length === 126) {
          if (buffer.length < 4) return;
          length = buffer.readUInt16BE(2);
          offset = 4;
        } else if (length === 127) {
          if (buffer.length < 10) return;
          length = Number(buffer.readBigUInt64BE(2));
          offset = 10;
        }
        if (buffer.length < offset + length) return;
        const opcode = buffer[0] & 0x0f;
        const payload = buffer.subarray(offset, offset + length);
        buffer = buffer.subarray(offset + length);
        if (opcode === OPCODE_CLOSE && payload.length >= 2) {
          closeCode = payload.readUInt16BE(0);
        }
        onFrame({ opcode, payload, firstByteAt: frameStart, completeAt: performance.now() });
        frameStart = null;
      }
    };

    socket.on("data", (chunk) => {
      if (upgraded) {
        buffer = Buffer.concat([buffer, chunk]);
        readFrames();
        return;
      }
      head = Buffer.concat([head, chunk]);
      const end = head.indexOf("\r\n\r\n");
      if (end < 0) return;
      const status = head.subarray(0, head.indexOf("\r\n")).toString();
      if (!status.startsWith("HTTP/1.1 101")) {
        socket.destroy();
        reject(new Error(status));
        return;
      }
      upgraded = true;
      buffer = head.subarray(end + 4);
      resolve(client);
      readFrames();
    });
    socket.on("error", (error) => {
      if (!upgraded) reject(error);
    });
    const headers = [
      `GET ${path} HTTP/1.1`,
      `Host: ${host}:${port}`,
      "Upgrade: websocket",
      "Connection: Upgrade",
      `Sec-WebSocket-Key: ${randomBytes(16).toString("base64")}`,
      "Sec-WebSocket-Version: 13",
    ];
    if (origin !== null) headers.push(`Origin: ${origin}`);
    socket.write(headers.join("\r\n") + "\r\n\r\n");
  });
}

export { OPCODE_TEXT };
