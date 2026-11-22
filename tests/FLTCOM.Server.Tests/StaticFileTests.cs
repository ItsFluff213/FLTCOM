using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using FLTCOM.Core.Protocol;
using Xunit;

namespace FLTCOM.Server.Tests
{
    public sealed class StaticFileTests : IDisposable
    {
        private const string Secret = "the secret outside web";

        private readonly string _root = Path.Combine(Path.GetTempPath(), "fltcom-static-" + Guid.NewGuid().ToString("N"));
        private readonly string _web;
        private readonly StaticSite _site;

        public StaticFileTests()
        {
            _web = Path.Combine(_root, "web");
            Directory.CreateDirectory(Path.Combine(_web, "assets"));
            File.WriteAllText(Path.Combine(_root, "secret.txt"), Secret);
            File.WriteAllText(Path.Combine(_web, "index.html"), "<!doctype html><title>index</title>");
            File.WriteAllText(Path.Combine(_web, "assets", "app.js"), "console.log(1)");
            File.WriteAllText(Path.Combine(_web, "two words.txt"), "spaced");
            File.WriteAllText(Path.Combine(_web, "data.bin"), "no content type for this");
            foreach (string extension in StaticSite.ContentTypes.Keys)
            {
                File.WriteAllText(Path.Combine(_web, "sample" + extension), extension);
            }
            _site = StaticSite.Load(_web, Limits.Default.MaxSiteBytes);
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);

        private static string Body(StaticFile? file) => Encoding.UTF8.GetString(Assert.IsType<StaticFile>(file).Body);

        [Theory]
        [InlineData("/../secret.txt")]
        [InlineData("/../../Windows/win.ini")]
        [InlineData("/%2e%2e/secret.txt")]
        [InlineData("/%2E%2E/secret.txt")]
        [InlineData("/..%2fsecret.txt")]
        [InlineData("/..%5csecret.txt")]
        [InlineData("/..\\secret.txt")]
        [InlineData("/%252e%252e/secret.txt")]
        [InlineData("/assets/../../secret.txt")]
        [InlineData("/assets/../index.html")]
        [InlineData("/./index.html")]
        [InlineData("//secret.txt")]
        [InlineData("//index.html")]
        [InlineData("/web/index.html")]
        [InlineData("secret.txt")]
        [InlineData("index.html")]
        [InlineData("")]
        [InlineData("/index.html%00")]
        [InlineData("/%00")]
        [InlineData("/index.html\0")]
        [InlineData("/%zz")]
        [InlineData("/%2")]
        [InlineData("/%")]
        [InlineData("/index.html%")]
        [InlineData("/%ff%fe")]
        [InlineData("/ind\u00e9x.html")]
        [InlineData("/C:/Windows/win.ini")]
        [InlineData("C:\\Windows\\win.ini")]
        [InlineData("/etc/passwd")]
        public void APathOutsideTheListedFilesFindsNothing(string path)
        {
            Assert.Null(_site.Find(path));
        }

        [Fact]
        public void ArtPutAtRuntimeIsFoundAndReplaced()
        {
            Assert.Null(_site.Find("/art/body/Kerbin.png"));

            _site.PutArt("/art/body/Kerbin.png", "image/png", Encoding.UTF8.GetBytes("first"));
            _site.PutArt("/art/body/Kerbin.png", "image/png", Encoding.UTF8.GetBytes("second"));

            Assert.Equal("second", Body(_site.Find("/art/body/Kerbin.png")));
            Assert.Equal("image/png", _site.Find("/art/body/Kerbin.png")!.ContentType);
            Assert.True(_site.HasArt("/art/body/Kerbin.png"));
        }

        [Theory]
        [InlineData("/index.html")]
        [InlineData("/art/../index.html")]
        [InlineData("art/x.png")]
        public void ArtOutsideTheArtFolderIsRefused(string path)
        {
            Assert.Throws<ArgumentException>(() => _site.PutArt(path, "image/png", new byte[1]));
        }

        [Fact]
        public void TheSecretsOwnAbsolutePathFindsNothing()
        {
            string absolute = Path.Combine(_root, "secret.txt");

            Assert.Null(_site.Find(absolute));
            Assert.Null(_site.Find("/" + absolute));
            Assert.Null(_site.Find("/" + absolute.Replace('\\', '/')));
            Assert.Null(_site.Find(Uri.EscapeDataString(absolute)));
        }

        [Fact]
        public void RootIsTheIndexPage()
        {
            Assert.Equal("<!doctype html><title>index</title>", Body(_site.Find("/")));
            Assert.Same(_site.Find("/"), _site.Find("/index.html"));
        }

        [Fact]
        public void FilesInSubfoldersAndEscapedNamesAreFound()
        {
            Assert.Equal("console.log(1)", Body(_site.Find("/assets/app.js")));
            Assert.Equal("console.log(1)", Body(_site.Find("/assets/%61pp.js")));
            Assert.Equal("spaced", Body(_site.Find("/two%20words.txt")));
        }

        [Fact]
        public void AFileWithNoContentTypeIsNotServed()
        {
            Assert.Null(_site.Find("/data.bin"));
        }

        [Theory]
        [InlineData(".html", "text/html; charset=utf-8")]
        [InlineData(".js", "text/javascript; charset=utf-8")]
        [InlineData(".css", "text/css; charset=utf-8")]
        [InlineData(".json", "application/json")]
        [InlineData(".map", "application/json")]
        [InlineData(".svg", "image/svg+xml")]
        [InlineData(".png", "image/png")]
        [InlineData(".ico", "image/x-icon")]
        [InlineData(".woff2", "font/woff2")]
        [InlineData(".txt", "text/plain; charset=utf-8")]
        public void EachExtensionHasItsContentType(string extension, string contentType)
        {
            StaticFile file = Assert.IsType<StaticFile>(_site.Find("/sample" + extension));

            Assert.Equal(contentType, file.ContentType);
            Assert.Contains("\r\nContent-Type: " + contentType + "\r\n", Encoding.ASCII.GetString(file.Head));
            Assert.Contains("\r\nContent-Length: " + extension.Length + "\r\n", Encoding.ASCII.GetString(file.Head));
        }

        [Fact]
        public void TheTableCoversExactlyTheTestedExtensions()
        {
            Assert.Equal(10, StaticSite.ContentTypes.Count);
        }

        [Fact]
        public void AMissingWebRootServesNothing()
        {
            StaticSite empty = StaticSite.Load(Path.Combine(_root, "absent"), Limits.Default.MaxSiteBytes);

            Assert.Null(empty.Find("/"));
        }

        [Fact]
        public void AWebRootOverTheSizeLimitIsRefused()
        {
            Assert.Throws<InvalidDataException>(() => StaticSite.Load(_web, maxBytes: 20));
        }

        [Fact]
        public void TraversalOverTheWireGets404AndNeverTheSecret()
        {
            using HttpServer server = HttpServer.Start(new ServerOptions(0, _web, new ServerHello("test", null, 1), _ => { }));
            string host = "127.0.0.1:" + server.Port;

            foreach (string target in new[] { "/../../Windows/win.ini", "/../secret.txt", "/%2e%2e/secret.txt", "/..%5csecret.txt" })
            {
                string response = RawRequest(server.Port, "GET " + target + " HTTP/1.1\r\nHost: " + host + "\r\n\r\n");

                Assert.StartsWith("HTTP/1.1 404 Not Found\r\n", response);
                Assert.DoesNotContain(Secret, response);
            }
            Assert.EndsWith("<!doctype html><title>index</title>", RawRequest(server.Port, "GET / HTTP/1.1\r\nHost: " + host + "\r\n\r\n"));
        }

        internal static string RawRequest(int port, string request)
        {
            using var client = new TcpClient();
            client.ReceiveTimeout = 5000;
            client.Connect("127.0.0.1", port);
            NetworkStream stream = client.GetStream();
            stream.Write(Encoding.Latin1.GetBytes(request));
            var response = new MemoryStream();
            stream.CopyTo(response);
            return Encoding.Latin1.GetString(response.ToArray());
        }
    }
}
