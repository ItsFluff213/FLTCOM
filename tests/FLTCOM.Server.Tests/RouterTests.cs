using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace FLTCOM.Server.Tests
{
    public sealed class RouterTests : IDisposable
    {
        private const int Port = 47800;
        private const string Key = "dGhlIHNhbXBsZSBub25jZQ==";

        private readonly string _web = Path.Combine(Path.GetTempPath(), "fltcom-router-" + Guid.NewGuid().ToString("N"));
        private readonly StaticSite _site;

        public RouterTests()
        {
            Directory.CreateDirectory(_web);
            File.WriteAllText(Path.Combine(_web, "index.html"), "index");
            _site = StaticSite.Load(_web, Limits.Default.MaxSiteBytes);
        }

        public void Dispose() => Directory.Delete(_web, recursive: true);

        private static ParseStatus Parse(string head, out HttpRequest? request)
        {
            byte[] bytes = Encoding.Latin1.GetBytes(head);
            return HttpRequest.TryParse(bytes, bytes.Length, Limits.Default, out request, out _);
        }

        private Route Decide(string requestLine, params string[] headers) => Decide(true, requestLine, headers);

        private Route Decide(bool hasRoomForClient, string requestLine, params string[] headers)
        {
            Assert.Equal(ParseStatus.Ok, Parse(requestLine + "\r\n" + string.Join("\r\n", headers) + "\r\n\r\n", out HttpRequest? request));
            return Router.Decide(request!, Port, _site, hasRoomForClient);
        }

        private static string[] Upgrade(string? origin = "http://127.0.0.1:47800", string host = "127.0.0.1:47800", string version = "13", string? key = Key)
        {
            var headers = new[] { "Host: " + host, "Upgrade: websocket", "Connection: keep-alive, Upgrade", "Sec-WebSocket-Version: " + version }.ToList();
            if (origin != null)
            {
                headers.Add("Origin: " + origin);
            }
            if (key != null)
            {
                headers.Add("Sec-WebSocket-Key: " + key);
            }
            return headers.ToArray();
        }

        private static void AssertRefused(int status, Route route) => Assert.Equal(status, Assert.IsType<Route.Refuse>(route).Status);

        [Theory]
        [InlineData("127.0.0.1:47800")]
        [InlineData("localhost:47800")]
        [InlineData("LocalHost:47800")]
        public void OwnHostNamesAreServed(string host)
        {
            Assert.IsType<Route.File>(Decide("GET / HTTP/1.1", "Host: " + host));
        }

        [Theory]
        [InlineData("evil.example")]
        [InlineData("evil.example:47800")]
        [InlineData("127.0.0.1")]
        [InlineData("127.0.0.1:80")]
        [InlineData("localhost")]
        [InlineData("127.0.0.1:47800.evil.example")]
        [InlineData("192.168.1.20:47800")]
        [InlineData("[::1]:47800")]
        public void AnotherHostNameIsRefused(string host)
        {
            AssertRefused(403, Decide("GET / HTTP/1.1", "Host: " + host));
            AssertRefused(403, Decide("GET /ws HTTP/1.1", Upgrade(host: host)));
        }

        [Fact]
        public void AMissingOrRepeatedHostIsRefused()
        {
            AssertRefused(403, Decide("GET / HTTP/1.1", "Accept: */*"));
            AssertRefused(403, Decide("GET / HTTP/1.1", "Host: 127.0.0.1:47800", "Host: evil.example"));
        }

        [Theory]
        [InlineData("http://127.0.0.1:47800")]
        [InlineData("http://localhost:47800")]
        public void OwnOriginsMayOpenTheSocket(string origin)
        {
            Route.Upgrade upgrade = Assert.IsType<Route.Upgrade>(Decide("GET /ws HTTP/1.1", Upgrade(origin)));

            Assert.Equal("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", upgrade.AcceptKey);
        }

        [Theory]
        [InlineData("http://evil.example")]
        [InlineData("http://evil.example:47800")]
        [InlineData("https://127.0.0.1:47800")]
        [InlineData("http://127.0.0.1:47801")]
        [InlineData("http://127.0.0.1")]
        [InlineData("http://127.0.0.1:47800.evil.example")]
        [InlineData("http://localhost:47800/")]
        [InlineData("null")]
        [InlineData("")]
        public void AnotherOriginIsRefusedEverywhere(string origin)
        {
            AssertRefused(403, Decide("GET /ws HTTP/1.1", Upgrade(origin)));
            AssertRefused(403, Decide("GET / HTTP/1.1", "Host: 127.0.0.1:47800", "Origin: " + origin));
        }

        [Fact]
        public void TheSocketRequiresAnOriginAndPagesDoNot()
        {
            AssertRefused(403, Decide("GET /ws HTTP/1.1", Upgrade(origin: null)));
            Assert.IsType<Route.File>(Decide("GET /index.html HTTP/1.1", "Host: 127.0.0.1:47800"));
        }

        [Theory]
        [InlineData("12")]
        [InlineData("8")]
        [InlineData("13, 8")]
        [InlineData("")]
        public void OnlyVersion13IsUpgraded(string version)
        {
            AssertRefused(426, Decide("GET /ws HTTP/1.1", Upgrade(version: version)));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("short")]
        [InlineData("dGhlIHNhbXBsZSBub25jZQ")]
        [InlineData("!!!!!!!!!!!!!!!!!!!!!!==")]
        [InlineData("dGhlIHNhbXBsZSBub25jZSBpcyB0b28gbG9uZw==")]
        public void TheKeyMustBeSixteenBytesOfBase64(string? key)
        {
            AssertRefused(400, Decide("GET /ws HTTP/1.1", Upgrade(key: key)));
        }

        [Fact]
        public void APlainRequestForTheSocketPathIsNotUpgraded()
        {
            AssertRefused(426, Decide("GET /ws HTTP/1.1", "Host: 127.0.0.1:47800", "Origin: http://127.0.0.1:47800"));
            AssertRefused(405, Decide("POST /ws HTTP/1.1", Upgrade()));
        }

        [Fact]
        public void AFullServerRefusesOneMoreClient()
        {
            AssertRefused(503, Decide(false, "GET /ws HTTP/1.1", Upgrade()));
            Assert.IsType<Route.File>(Decide(false, "GET / HTTP/1.1", "Host: 127.0.0.1:47800"));
        }

        [Fact]
        public void TheQueryIsIgnored()
        {
            Assert.IsType<Route.File>(Decide("GET /index.html?v=3 HTTP/1.1", "Host: 127.0.0.1:47800"));
            Assert.IsType<Route.Upgrade>(Decide("GET /ws?tab=2 HTTP/1.1", Upgrade()));
        }

        [Fact]
        public void HeadGetsTheFileWithoutItsBody()
        {
            Assert.True(Assert.IsType<Route.File>(Decide("HEAD / HTTP/1.1", "Host: 127.0.0.1:47800")).HeadOnly);
            Assert.False(Assert.IsType<Route.File>(Decide("GET / HTTP/1.1", "Host: 127.0.0.1:47800")).HeadOnly);
        }

        [Theory]
        [InlineData("POST")]
        [InlineData("PUT")]
        [InlineData("DELETE")]
        [InlineData("OPTIONS")]
        [InlineData("get")]
        public void OtherMethodsAreRefused(string method)
        {
            AssertRefused(405, Decide(method + " / HTTP/1.1", "Host: 127.0.0.1:47800"));
        }

        [Fact]
        public void TheRefusalReasonCarriesAtMost80PrintableCharactersOfTheRequest()
        {
            Route.Refuse refuse = Assert.IsType<Route.Refuse>(Decide("GET /" + new string('a', 500) + " HTTP/1.1", "Host: 127.0.0.1:47800"));

            Assert.Equal(404, refuse.Status);
            Assert.Equal("no file /" + new string('a', 79), refuse.Reason);
        }

        [Theory]
        [InlineData("GET / HTTP/1.1\r\nHost: a\r\n")]
        [InlineData("GET / HTTP/1.1\r\n")]
        [InlineData("GET")]
        [InlineData("")]
        public void AnUnfinishedHeadNeedsMore(string head)
        {
            Assert.Equal(ParseStatus.NeedMore, Parse(head, out _));
        }

        [Theory]
        [InlineData("GET /\r\n\r\n")]
        [InlineData("GET / HTTP/1.0\r\n\r\n")]
        [InlineData("GET / HTTP/2\r\n\r\n")]
        [InlineData("GET  / HTTP/1.1\r\n\r\n")]
        [InlineData(" GET / HTTP/1.1\r\n\r\n")]
        [InlineData("GET / extra HTTP/1.1\r\n\r\n")]
        [InlineData("\r\nGET / HTTP/1.1\r\n\r\n")]
        [InlineData("GET / HTTP/1.1\r\nno colon here\r\n\r\n")]
        [InlineData("GET / HTTP/1.1\r\n: no name\r\n\r\n")]
        [InlineData("GET / HTTP/1.1\r\nHost : spaced\r\n\r\n")]
        [InlineData("GET / HTTP/1.1\r\nHost: a\r\n folded\r\n\r\n")]
        [InlineData("GET / HTTP/1.1\r\nHost: a\nX: b\r\n\r\n")]
        [InlineData("GET / HTTP/1.1\r\nHost: a\rX: b\r\n\r\n")]
        [InlineData("GET /\0 HTTP/1.1\r\n\r\n")]
        [InlineData("GET /é HTTP/1.1\r\n\r\n")]
        [InlineData("\u0016\u0003\u0001\u0002\u0000\u0001\u0000\u0001ü\u0003\u0003\r\n\r\n")]
        public void ABrokenHeadIsMalformed(string head)
        {
            Assert.Equal(ParseStatus.Malformed, Parse(head, out _));
        }

        [Fact]
        public void MoreThan64HeadersIsMalformed()
        {
            string headers = string.Concat(Enumerable.Range(0, 65).Select(i => "X-" + i + ": v\r\n"));

            Assert.Equal(ParseStatus.Malformed, Parse("GET / HTTP/1.1\r\n" + headers + "\r\n", out _));
            Assert.Equal(ParseStatus.Ok, Parse("GET / HTTP/1.1\r\n" + headers.Substring(headers.IndexOf('\n') + 1) + "\r\n", out _));
        }

        [Fact]
        public void AHeadThatFillsTheBufferWithoutEndingIsTooLarge()
        {
            string head = "GET /" + new string('a', Limits.Default.MaxRequestBytes) + " HTTP/1.1\r\n\r\n";

            Assert.Equal(ParseStatus.TooLarge, Parse(head.Substring(0, Limits.Default.MaxRequestBytes), out _));
        }

        [Fact]
        public void HeaderNamesIgnoreCaseAndValuesAreTrimmed()
        {
            Assert.Equal(ParseStatus.Ok, Parse("GET /a?b HTTP/1.1\r\nhOsT:  spaced out \t\r\nX-Empty:\r\n\r\nbody", out HttpRequest? request));

            Assert.Equal("GET", request!.Method);
            Assert.Equal("/a?b", request.Target);
            Assert.Equal("spaced out", request.Header("Host"));
            Assert.Equal(string.Empty, request.Header("x-empty"));
            Assert.Null(request.Header("Origin"));
        }
    }
}
