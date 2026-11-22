using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FLTCOM.Server
{
    internal sealed class StaticFile
    {
        public StaticFile(string contentType, byte[] body)
        {
            ContentType = contentType;
            Body = body;
            Head = HttpResponse.FileHead(contentType, body.Length);
        }

        public string ContentType { get; }
        public byte[] Head { get; }
        public byte[] Body { get; }
    }

    /// <summary>
    /// The web root, read into memory once. A request never becomes a file system path: it is a key into the
    /// table built by listing the folder, so nothing outside the folder can be named.
    /// </summary>
    internal sealed class StaticSite
    {
        internal static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".html"] = "text/html; charset=utf-8",
            [".js"] = "text/javascript; charset=utf-8",
            [".css"] = "text/css; charset=utf-8",
            [".json"] = "application/json",
            [".map"] = "application/json",
            [".svg"] = "image/svg+xml",
            [".png"] = "image/png",
            [".ico"] = "image/x-icon",
            [".woff2"] = "font/woff2",
            [".txt"] = "text/plain; charset=utf-8",
        };

        /// <summary>Where art the game makes at runtime lives: body textures and craft models.</summary>
        public const string ArtPrefix = "/art/";

        private readonly Dictionary<string, StaticFile> _files;
        private readonly ConcurrentDictionary<string, StaticFile> _art = new ConcurrentDictionary<string, StaticFile>(StringComparer.Ordinal);

        private StaticSite(Dictionary<string, StaticFile> files)
        {
            _files = files;
        }

        /// <summary>Files with an extension outside ContentTypes, and anything behind a link or junction, are left out.</summary>
        public static StaticSite Load(string webRoot, long maxBytes)
        {
            var files = new Dictionary<string, StaticFile>(StringComparer.Ordinal);
            var folders = new Stack<KeyValuePair<string, string>>();
            if (Directory.Exists(webRoot))
            {
                folders.Push(new KeyValuePair<string, string>(webRoot, "/"));
            }
            long total = 0;
            while (folders.Count > 0)
            {
                KeyValuePair<string, string> folder = folders.Pop();
                foreach (string path in Directory.EnumerateFileSystemEntries(folder.Key))
                {
                    FileAttributes attributes = File.GetAttributes(path);
                    string key = folder.Value + Path.GetFileName(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        folders.Push(new KeyValuePair<string, string>(path, key + "/"));
                        continue;
                    }
                    if (!ContentTypes.TryGetValue(Path.GetExtension(path), out string? contentType))
                    {
                        continue;
                    }
                    total += new FileInfo(path).Length;
                    if (total > maxBytes)
                    {
                        throw new InvalidDataException("the web root holds more than " + maxBytes.ToString(CultureInfo.InvariantCulture) + " bytes");
                    }
                    files[key] = new StaticFile(contentType, File.ReadAllBytes(path));
                }
            }
            return new StaticSite(files);
        }

        /// <summary>Takes a path without its query, percent-decodes it once and looks it up. "/" is "/index.html".</summary>
        public StaticFile? Find(string path)
        {
            string? decoded = PercentDecode(path);
            if (decoded == null)
            {
                return null;
            }
            string key = decoded == "/" ? "/index.html" : decoded;
            return _files.TryGetValue(key, out StaticFile? file) || _art.TryGetValue(key, out file) ? file : null;
        }

        /// <summary>Serves a file made in memory at a path under ArtPrefix, replacing any earlier one. Safe from any thread.</summary>
        public void PutArt(string path, string contentType, byte[] body)
        {
            if (path == null || !path.StartsWith(ArtPrefix, StringComparison.Ordinal) || path.IndexOf("..", StringComparison.Ordinal) >= 0)
            {
                throw new ArgumentException("art lives under " + ArtPrefix, nameof(path));
            }
            _art[path] = new StaticFile(contentType ?? throw new ArgumentNullException(nameof(contentType)), body ?? throw new ArgumentNullException(nameof(body)));
        }

        public bool HasArt(string path) => _art.ContainsKey(path);

        private static string? PercentDecode(string path)
        {
            var bytes = new byte[path.Length];
            int count = 0;
            for (int i = 0; i < path.Length; i++)
            {
                char c = path[i];
                if (c > '~')
                {
                    return null;
                }
                if (c != '%')
                {
                    bytes[count++] = (byte)c;
                    continue;
                }
                int high = i + 2 < path.Length ? HexValue(path[i + 1]) : -1;
                int low = i + 2 < path.Length ? HexValue(path[i + 2]) : -1;
                if (high < 0 || low < 0)
                {
                    return null;
                }
                bytes[count++] = (byte)((high << 4) | low);
                i += 2;
            }
            return Utf8.TryDecode(bytes, 0, count, out string decoded) ? decoded : null;
        }

        private static int HexValue(char c) =>
            c >= '0' && c <= '9' ? c - '0'
            : c >= 'a' && c <= 'f' ? c - 'a' + 10
            : c >= 'A' && c <= 'F' ? c - 'A' + 10
            : -1;
    }
}
