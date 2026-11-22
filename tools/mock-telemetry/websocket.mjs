import { createHash } from "node:crypto";

const ACCEPT_KEY_GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
const MAX_FRAME = 1 << 20;
const OPCODE_TEXT = 0x1;
const OPCODE_CLOSE = 0x8;
const OPCODE_PING = 0x9;
const OPCODE_PONG = 0xa;
const CLOSE_NORMAL = 1000;
const CLOSE_PROTOCOL_ERROR = 1002;
const CLOSE_TOO_BIG = 1009;

export function acceptWebSocket(request, socket, onText) {
  const key = request.headers["sec-websocket-key"];
  if (request.headers.upgrade?.toLowerCase() !== "websocket" || !key) {
    socket.end("HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n");
    return null;
  }
  const accept = createHash("sha1").update(key + ACCEPT_KEY_GUID).digest("base64");
  socket.write(
    "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" + `Sec-WebSocket-Accept: ${accept}\r\n\r\n`,
  );
  socket.setNoDelay(true);

  let buffer = Buffer.alloc(0);
  let open = true;

  const send = (opcode, payload) => {
    if (!open) {
      return;
    }
    const length = payload.length;
    let header;
    if (length < 126) {
      header = Buffer.from([0x80 | opcode, length]);
    } else if (length < 65536) {
      header = Buffer.alloc(4);
      header[0] = 0x80 | opcode;
      header[1] = 126;
      header.writeUInt16BE(length, 2);
    } else {
      header = Buffer.alloc(10);
      header[0] = 0x80 | opcode;
      header[1] = 127;
      header.writeBigUInt64BE(BigInt(length), 2);
    }
    socket.write(Buffer.concat([header, payload]));
  };

  const close = (code) => {
    if (!open) {
      return;
    }
    const payload = Buffer.alloc(2);
    payload.writeUInt16BE(code, 0);
    send(OPCODE_CLOSE, payload);
    open = false;
    socket.end();
  };

  socket.on("data", (chunk) => {
    buffer = Buffer.concat([buffer, chunk]);
    while (buffer.length >= 2) {
      const fin = (buffer[0] & 0x80) !== 0;
      const opcode = buffer[0] & 0x0f;
      const masked = (buffer[1] & 0x80) !== 0;
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
      if (!fin || !masked || length > MAX_FRAME) {
        close(length > MAX_FRAME ? CLOSE_TOO_BIG : CLOSE_PROTOCOL_ERROR);
        return;
      }
      if (buffer.length < offset + 4 + length) return;
      const mask = buffer.subarray(offset, offset + 4);
      const payload = Buffer.from(buffer.subarray(offset + 4, offset + 4 + length));
      for (let i = 0; i < payload.length; i++) {
        payload[i] ^= mask[i % 4];
      }
      buffer = buffer.subarray(offset + 4 + length);
      if (opcode === OPCODE_TEXT) {
        onText(payload.toString("utf8"));
      } else if (opcode === OPCODE_PING) {
        send(OPCODE_PONG, payload);
      } else if (opcode === OPCODE_CLOSE) {
        close(CLOSE_NORMAL);
      }
    }
  });
  socket.on("close", () => {
    open = false;
  });
  socket.on("error", () => {
    open = false;
  });

  return {
    sendText: (text) => send(OPCODE_TEXT, Buffer.from(text, "utf8")),
    close,
  };
}
