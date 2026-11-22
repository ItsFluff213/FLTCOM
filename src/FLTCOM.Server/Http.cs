using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FLTCOM.Server
{
    internal enum ParseStatus
    {
        NeedMore,
        Ok,
        Malformed,
        TooLarge,
    }

    internal sealed class HttpRequest
    {
        private readonly Dictionary<string, string> _headers;

        private HttpRequest(string method, string target, Dictionary<string, string> headers)
        {
            Method = method;
            Target = target;
            _headers = headers;
        }

        public string Method { get; }

        /// <summary>The request target as sent, not decoded.</summary>
        public string Target { get; }

        /// <summary>A header sent more than once comes back as its values joined with ", ".</summary>
        public string? Header(string name) => _headers.TryGetValue(name, out string? value) ? value : null;

        public static ParseStatus TryParse(byte[] buffer, int count, Limits limits, out HttpRequest? request, out int headLength)
        {
            request = null;
            headLength = 0;
            int end = EndOfHead(buffer, count);
            if (end < 0)
            {
                return count >= limits.MaxRequestBytes ? ParseStatus.TooLarge : ParseStatus.NeedMore;
            }
            headLength = end + 4;
            for (int i = 0; i < end; i++)
            {
                bool lineBreak = buffer[i] == '\r' && buffer[i + 1] == '\n';
                bool printable = (buffer[i] >= ' ' && buffer[i] <= '~') || buffer[i] == '\t';
                if (lineBreak)
                {
                    i++;
                }
                else if (!printable)
                {
                    return ParseStatus.Malformed;
                }
            }
            string[] lines = Encoding.ASCII.GetString(buffer, 0, end).Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] requestLine = lines[0].Split(' ');
            if (requestLine.Length != 3 || requestLine[0].Length == 0 || requestLine[1].Length == 0 || requestLine[2] != "HTTP/1.1"
                || lines.Length - 1 > limits.MaxRequestHeaders)
            {
                return ParseStatus.Malformed;
            }
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                string name = colon > 0 ? lines[i].Substring(0, colon) : string.Empty;
                if (name.Length == 0 || name.IndexOfAny(new[] { ' ', '\t' }) >= 0)
                {
                    return ParseStatus.Malformed;
                }
                string value = lines[i].Substring(colon + 1).Trim(' ', '\t');
                headers[name] = headers.TryGetValue(name, out string? earlier) ? earlier + ", " + value : value;
            }
            request = new HttpRequest(requestLine[0], requestLine[1], headers);
            return ParseStatus.Ok;
        }

        private static int EndOfHead(byte[] buffer, int count)
        {
            for (int i = 0; i + 3 < count; i++)
            {
                if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
                {
                    return i;
                }
            }
            return -1;
        }
    }

    /// <summary>What to do with one request. Closed: these three cases are all there are.</summary>
    internal abstract class Route
    {
        private Route()
        {
        }

        public sealed class Upgrade : Route
        {
            public Upgrade(string acceptKey) => AcceptKey = acceptKey;
            public string AcceptKey { get; }
        }

        public sealed class File : Route
        {
            public File(StaticFile value, bool headOnly)
            {
                Value = value;
                HeadOnly = headOnly;
            }

            public StaticFile Value { get; }
            public bool HeadOnly { get; }
        }

        public sealed class Refuse : Route
        {
            public Refuse(int status, string reason)
            {
                Status = status;
                Reason = reason;
            }

            public int Status { get; }

            /// <summary>For the log. Holds at most 80 printable characters taken from the request.</summary>
            public string Reason { get; }
        }
    }

    internal static class Router
    {
        public const string WebSocketPath = "/ws";

        /// <summary>
        /// Host must name this server, which stops DNS rebinding. Origin, when present, must be this server's own
        /// page, and /ws requires it, which stops other sites' pages from opening the socket.
        /// </summary>
        public static Route Decide(HttpRequest request, int port, StaticSite site, bool hasRoomForClient)
        {
            string suffix = ":" + port.ToString(CultureInfo.InvariantCulture);
            string? host = request.Header("Host");
            if (host != "127.0.0.1" + suffix && !string.Equals(host, "localhost" + suffix, StringComparison.OrdinalIgnoreCase))
            {
                return new Route.Refuse(403, "host not allowed");
            }
            string? origin = request.Header("Origin");
            if (origin != null && origin != "http://127.0.0.1" + suffix && origin != "http://localhost" + suffix)
            {
                return new Route.Refuse(403, "origin not allowed");
            }
            int query = request.Target.IndexOf('?');
            string path = query < 0 ? request.Target : request.Target.Substring(0, query);
            if (path == WebSocketPath)
            {
                return DecideUpgrade(request, origin, hasRoomForClient);
            }
            bool headOnly = request.Method == "HEAD";
            if (request.Method != "GET" && !headOnly)
            {
                return new Route.Refuse(405, "method not allowed");
            }
            StaticFile? file = site.Find(path);
            return file == null ? new Route.Refuse(404, "no file " + HttpServer.Printable(path)) : (Route)new Route.File(file, headOnly);
        }

        private static Route DecideUpgrade(HttpRequest request, string? origin, bool hasRoomForClient)
        {
            if (request.Method != "GET")
            {
                return new Route.Refuse(405, "method not allowed");
            }
            if (origin == null)
            {
                return new Route.Refuse(403, "websocket without origin");
            }
            if (!string.Equals(request.Header("Upgrade"), "websocket", StringComparison.OrdinalIgnoreCase)
                || !HasToken(request.Header("Connection"), "upgrade"))
            {
                return new Route.Refuse(426, "not a websocket upgrade");
            }
            if (request.Header("Sec-WebSocket-Version") != "13")
            {
                return new Route.Refuse(426, "websocket version is not 13");
            }
            string key = request.Header("Sec-WebSocket-Key") ?? string.Empty;
            if (!IsSixteenBytesOfBase64(key))
            {
                return new Route.Refuse(400, "bad websocket key");
            }
            if (!hasRoomForClient)
            {
                return new Route.Refuse(503, "too many clients");
            }
            return new Route.Upgrade(Frame.AcceptKey(key));
        }

        private static bool HasToken(string? header, string token)
        {
            foreach (string part in (header ?? string.Empty).Split(','))
            {
                if (string.Equals(part.Trim(), token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsSixteenBytesOfBase64(string key)
        {
            try
            {
                return key.Length == 24 && Convert.FromBase64String(key).Length == 16;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }

    internal static class HttpResponse
    {
        private static readonly Dictionary<int, string> StatusLines = new Dictionary<int, string>
        {
            [400] = "400 Bad Request\r\n",
            [403] = "403 Forbidden\r\n",
            [404] = "404 Not Found\r\n",
            [405] = "405 Method Not Allowed\r\nAllow: GET, HEAD\r\n",
            [426] = "426 Upgrade Required\r\nUpgrade: websocket\r\nSec-WebSocket-Version: 13\r\n",
            [503] = "503 Service Unavailable\r\n",
        };

        public static byte[] Refusal(int status) =>
            Encoding.ASCII.GetBytes("HTTP/1.1 " + StatusLines[status] + "Content-Length: 0\r\nConnection: close\r\n\r\n");

        public static byte[] SwitchingProtocols(string acceptKey) =>
            Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + acceptKey + "\r\n\r\n");

        public static byte[] FileHead(string contentType, int length) =>
            Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: " + contentType
                + "\r\nContent-Length: " + length.ToString(CultureInfo.InvariantCulture)
                + "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n");
    }
}
