using System.Net;
using System.Security.Cryptography;
using Fluester.Engine;

namespace Fluester.Core.Tests;

/// <summary>Update-Suche: Versionen vergleichen, Prüfsummen lesen, Paket geprüft laden (mit lokalem Testserver).</summary>
public class UpdatesTests
{
    [Theory]
    [InlineData("1.3.2", "1.3.1", true)]
    [InlineData("1.10.0", "1.9.9", true)]     // Zahlen, nicht Text vergleichen
    [InlineData("v2.0.0", "1.3.1", true)]
    [InlineData("1.3.1", "1.3.1", false)]
    [InlineData("1.3.0", "1.3.1", false)]
    [InlineData("1.3", "1.3.0", false)]       // fehlende Stelle = 0
    [InlineData("1.3.1", "0.0.214", true)]    // CI-Bauten haben 0.0.<Lauf>
    [InlineData("kaputt", "1.0.0", false)]
    [InlineData(null, "1.0.0", false)]
    [InlineData("1.0.0", "dev", false)]
    public void IsNewer(string? latest, string current, bool expected) => Assert.Equal(expected, Updates.IsNewer(latest, current));

    [Fact]
    public void ParseLatestReadsTheTag()
    {
        Assert.Equal("1.3.1", Updates.ParseLatest("""{ "tag_name": "v1.3.1", "name": "Flüster 1.3.1" }"""));
        Assert.Equal("2.0.0", Updates.ParseLatest("""{ "tag_name": "2.0.0" }"""));
        Assert.Null(Updates.ParseLatest("""{ "message": "API rate limit exceeded" }"""));
        Assert.Null(Updates.ParseLatest("""{ "tag_name": "nightly" }"""));
        Assert.Null(Updates.ParseLatest("<html>"));
        Assert.Null(Updates.ParseLatest("[]"));
    }

    [Fact]
    public void ChecksumForFindsTheRightLine()
    {
        var a = new string('a', 64);
        var b = new string('B', 64);
        var sums = $"{a}  Fluester-macOS.zip\n{b} *Fluester-Windows-x64.zip\r\n\nkaputt Fluester-Linux-x64.tar.gz\n";
        Assert.Equal(a, Updates.ChecksumFor(sums, "Fluester-macOS.zip"));
        Assert.Equal(b.ToLowerInvariant(), Updates.ChecksumFor(sums, "Fluester-Windows-x64.zip"));
        Assert.Null(Updates.ChecksumFor(sums, "Fluester-Linux-x64.tar.gz"));
        Assert.Null(Updates.ChecksumFor(sums, "Fluester-Linux-arm64.tar.gz"));
    }

    /// <summary>Kleiner HTTP-Server für die Release-Dateien.</summary>
    sealed class Server : IDisposable
    {
        readonly HttpListener listener = new();
        public readonly Dictionary<string, byte[]> Files = new();
        public readonly string Base;

        public Server()
        {
            int port = 0;
            for (int i = 0; i < 20 && port == 0; i++)
            {
                var p = Random.Shared.Next(20000, 60000);
                try { listener.Prefixes.Clear(); listener.Prefixes.Add($"http://127.0.0.1:{p}/"); listener.Start(); port = p; }
                catch (HttpListenerException) { }
            }
            Base = $"http://127.0.0.1:{port}";
            _ = Task.Run(async () =>
            {
                while (listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await listener.GetContextAsync(); } catch { return; }
                    var name = ctx.Request.Url!.AbsolutePath.TrimStart('/');
                    if (Files.TryGetValue(name, out var body)) { ctx.Response.ContentLength64 = body.Length; await ctx.Response.OutputStream.WriteAsync(body); }
                    else ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                }
            });
        }

        public void Dispose() => listener.Close();
    }

    static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    [Fact]
    public async Task DownloadChecksTheChecksum()
    {
        using var server = new Server();
        using var h = new TempHome();
        var zip = new byte[300_000];
        Random.Shared.NextBytes(zip);
        server.Files["latest.json"] = """{ "tag_name": "v9.9.9" }"""u8.ToArray();
        server.Files["Fluester-Windows-x64.zip"] = zip;
        server.Files["SHA256SUMS.txt"] = System.Text.Encoding.ASCII.GetBytes($"{Sha(zip)}  Fluester-Windows-x64.zip\n");
        Environment.SetEnvironmentVariable("FLUESTER_UPDATE_BASE", server.Base);
        try
        {
            Assert.Equal("9.9.9", await Updates.LatestAsync());
            double last = 0;
            var path = await Updates.DownloadAsync("9.9.9", "Fluester-Windows-x64.zip", h.Dir, new SyncProgress(p => last = p));
            Assert.Equal(zip, File.ReadAllBytes(path));
            Assert.Equal(1, last);

            // Beschädigt oder ausgetauscht: Prüfsumme passt nicht → Fehler, Datei weg
            server.Files["Fluester-Windows-x64.zip"] = zip[..^1];
            var e = await Assert.ThrowsAsync<FluesterException>(() => Updates.DownloadAsync("9.9.9", "Fluester-Windows-x64.zip", h.Dir, null));
            Assert.Contains("Prüfsumme", e.Message);
            Assert.False(File.Exists(path));

            // Paket fehlt in den Prüfsummen
            await Assert.ThrowsAsync<FluesterException>(() => Updates.DownloadAsync("9.9.9", "Fluester-macOS.zip", h.Dir, null));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FLUESTER_UPDATE_BASE", null);
        }
    }

    sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
