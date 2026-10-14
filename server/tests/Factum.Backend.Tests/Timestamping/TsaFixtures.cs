using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Text;
using System.Text.Json;
using Factum.Backend.Services.Timestamping;

namespace Factum.Backend.Tests.Timestamping;

/// <summary>
/// Fixtures RFC 3161 reales (sellado-tiempo §11.1). Generador local de una sola vez: solo corre con
/// <c>FACTUM_TSA_FIXTURES_DIR=&lt;carpeta&gt;</c> (sin la variable no hace nada, así CI nunca sale a la red). Los
/// archivos generados se copian a <c>Fixtures/Tsa/</c>; ver <c>Fixtures/Tsa/README.md</c>.
/// </summary>
public sealed class TsaFixtures
{
    public const string DataUtf8 = "factum-fixture-sellado-tiempo";

    public static readonly IReadOnlyList<(string Key, string Url)> Sources =
    [
        ("freetsa", "https://freetsa.org/tsr"),
        ("digicert", "http://timestamp.digicert.com"),
    ];

    public static string Dir => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tsa");

    /// <summary>Datos de <c>fixtures.json</c>.</summary>
    public sealed record Info(string Sha256, Dictionary<string, string> Nonces, Dictionary<string, string> Serials,
        Dictionary<string, DateTime> GenTimes, Dictionary<string, string> Policies)
    {
        public byte[] Hash => Convert.FromHexString(Sha256);
        public byte[] Tsr(string key) => File.ReadAllBytes(Path.Combine(Dir, key + ".tsr"));
        public byte[] Nonce(string key) => Convert.FromHexString(Nonces[key]);

        public Rfc3161TimestampRequest Request(string key, byte[]? nonce = null, byte[]? hash = null,
            bool certReq = true) =>
            Rfc3161TimestampRequest.CreateFromHash(hash ?? Hash, HashAlgorithmName.SHA256, requestedPolicyId: null,
                nonce: nonce ?? Nonce(key), requestSignerCertificates: certReq);
    }

    public static Info Load()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir, "fixtures.json")));
        var root = doc.RootElement;
        Dictionary<string, string> Map(string name) => root.GetProperty(name).EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString()!);
        return new Info(root.GetProperty("sha256").GetString()!, Map("nonces"), Map("serials"),
            root.GetProperty("gen_times").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDateTime().ToUniversalTime()),
            Map("policies"));
    }

    [Fact]
    public async Task Generar_SiHayCarpeta()
    {
        var outDir = Environment.GetEnvironmentVariable("FACTUM_TSA_FIXTURES_DIR");
        if (string.IsNullOrWhiteSpace(outDir)) return;
        Directory.CreateDirectory(outDir);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(DataUtf8));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var nonces = new Dictionary<string, string>();
        var serials = new Dictionary<string, string>();
        var genTimes = new Dictionary<string, string>();
        var policies = new Dictionary<string, string>();
        foreach (var (key, url) in Sources)
        {
            var nonce = RandomNumberGenerator.GetBytes(8);
            var req = Rfc3161TimestampRequest.CreateFromHash(hash, HashAlgorithmName.SHA256, null, nonce, true);
            using var content = new ByteArrayContent(req.Encode());
            content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");
            using var resp = await http.PostAsync(url, content);
            var body = await resp.Content.ReadAsByteArrayAsync();
            var v = TimestampResponseValidator.Validate(req, hash, body, (int)resp.StatusCode, new Uri(url).Host);
            Assert.True(v.IsValid, v.Failure?.Reason);
            await File.WriteAllBytesAsync(Path.Combine(outDir, key + ".tsr"), body);
            nonces[key] = Convert.ToHexStringLower(nonce);
            serials[key] = v.Data!.SerialNumber;
            genTimes[key] = v.Data.GenTime.ToString("O");
            policies[key] = v.Data.PolicyOid;
        }
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["data_utf8"] = DataUtf8,
            ["sha256"] = Convert.ToHexStringLower(hash),
            ["nonces"] = nonces,
            ["serials"] = serials,
            ["gen_times"] = genTimes,
            ["policies"] = policies,
        }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(outDir, "fixtures.json"), json + "\n");
    }
}
