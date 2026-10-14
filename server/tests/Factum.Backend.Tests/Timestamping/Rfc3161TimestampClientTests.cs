using System.Formats.Asn1;
using System.Net;
using System.Security.Cryptography.Pkcs;
using System.Text;
using Factum.Backend.Models;
using Factum.Backend.Services.Timestamping;
using Factum.Backend.Tests.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace Factum.Backend.Tests.Timestamping;

/// <summary>
/// <see cref="Rfc3161TimestampClient"/> (sellado-tiempo §5.1, §5.3, §11.2) con un <see cref="HttpMessageHandler"/>
/// doble y el nonce inyectado: fallback entre URLs, timeouts, presupuesto, clasificación del fallo y credenciales.
/// Sin red: las respuestas válidas son los fixtures reales.
/// </summary>
public sealed class Rfc3161TimestampClientTests
{
    private static readonly TsaFixtures.Info Fx = TsaFixtures.Load();

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public List<(Uri Url, string? Auth, byte[] Body, string? ContentType, string Accept)> Seen { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsByteArrayAsync(ct);
            Seen.Add((request.RequestUri!, request.Headers.Authorization?.ToString(), body,
                request.Content.Headers.ContentType?.MediaType, request.Headers.Accept.ToString()));
            return await respond(request, ct);
        }
    }

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static Rfc3161TimestampClient Client(Handler handler, int timeoutSeconds, string key, params string[] urls)
    {
        var settings = TimestampingOptions.Settings.Disabled with
        {
            Enabled = true,
            Urls = urls.Select(u => new Uri(u)).ToList(),
            TimeoutSeconds = timeoutSeconds,
        };
        return new Rfc3161TimestampClient(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, settings,
            NullLogger<Rfc3161TimestampClient>.Instance, new MutableTimeProvider(), () => Fx.Nonce(key));
    }

    [Fact]
    public async Task Primera503_SegundaFixture_GrantedConLaSegunda()
    {
        var handler = new Handler((req, _) => Task.FromResult(req.RequestUri!.Host == "tsa.caida.example"
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Ok(Fx.Tsr("digicert"))));
        var res = await Client(handler, 10, "digicert", "https://tsa.caida.example/tsr", "http://timestamp.digicert.com")
            .RequestAsync(Fx.Sha256, TimeSpan.FromSeconds(20), default);

        Assert.True(res.Granted, res.Failure?.Reason);
        Assert.Equal("http://timestamp.digicert.com/", res.TsaUrl);
        Assert.Equal("DigiCert", res.Data!.Authority);
        Assert.Equal(Fx.Tsr("digicert"), res.Tsr);
        Assert.Equal(2, handler.Seen.Count);
    }

    [Fact]
    public async Task PrimeraExcedeElTimeout_PasaALaSegunda()
    {
        var handler = new Handler(async (req, ct) =>
        {
            if (req.RequestUri!.Host == "tsa.lenta.example") await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Ok(Fx.Tsr("freetsa"));
        });
        var res = await Client(handler, 2, "freetsa", "https://tsa.lenta.example/tsr", "https://freetsa.org/tsr")
            .RequestAsync(Fx.Sha256, TimeSpan.FromSeconds(10), default);

        Assert.True(res.Granted, res.Failure?.Reason);
        Assert.Equal("https://freetsa.org/tsr", res.TsaUrl);
    }

    [Fact]
    public async Task PresupuestoAgotadoAntesDeLaSegunda_Unreachable()
    {
        var handler = new Handler(async (req, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Ok(Fx.Tsr("freetsa"));
        });
        var res = await Client(handler, 10, "freetsa", "https://tsa.lenta.example/tsr", "https://freetsa.org/tsr")
            .RequestAsync(Fx.Sha256, TimeSpan.FromSeconds(1), default);

        Assert.False(res.Granted);
        Assert.Equal(TimestampErrorKinds.Unreachable, res.Failure!.Kind);
        Assert.Single(handler.Seen); // la segunda no se probó
        Assert.Contains("tsa.lenta.example: tiempo agotado", res.Failure.Reason);
    }

    [Fact]
    public async Task InvalidaEnUnaYRedCaidaEnLaOtra_KindInvalidResponse()
    {
        var handler = new Handler((req, _) => req.RequestUri!.Host == "tsa.rota.example"
            ? Task.FromResult(Ok([0x30, 0x03, 0x02, 0x01, 0x02]))
            : throw new HttpRequestException("sin red"));
        var res = await Client(handler, 5, "freetsa", "https://tsa.rota.example/tsr", "https://tsa.caida.example/tsr")
            .RequestAsync(Fx.Sha256, TimeSpan.FromSeconds(10), default);

        Assert.Equal(TimestampErrorKinds.InvalidResponse, res.Failure!.Kind);
        Assert.Contains("tsa.caida.example: sin conexión", res.Failure.Reason);
        Assert.True(res.Failure.Reason.Length <= 300);
    }

    [Fact]
    public async Task UrlConCredenciales_MandaBasic_YNoLasExpone()
    {
        var handler = new Handler((req, _) => req.RequestUri!.Host == "tsa.privada.example"
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))
            : Task.FromResult(Ok(Fx.Tsr("freetsa"))));
        var client = Client(handler, 5, "freetsa", "https://usuario:cl%40ve@tsa.privada.example/tsr",
            "https://usuario:clave@freetsa.org/tsr");
        var res = await client.RequestAsync(Fx.Sha256, TimeSpan.FromSeconds(10), default);

        Assert.True(res.Granted);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("usuario:cl@ve")), handler.Seen[0].Auth);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("usuario:clave")), handler.Seen[1].Auth);
        // Ni la URL pedida ni la guardada llevan el userinfo.
        Assert.All(handler.Seen, s => Assert.Empty(s.Url.UserInfo));
        Assert.Equal("https://freetsa.org/tsr", res.TsaUrl);
        Assert.DoesNotContain("clave", res.TsaUrl);

        var failing = Client(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))),
            5, "freetsa", "https://usuario:clave@tsa.privada.example/tsr");
        var fail = await failing.RequestAsync(Fx.Sha256, TimeSpan.FromSeconds(10), default);
        Assert.Equal(TimestampErrorKinds.HttpError, fail.Failure!.Kind);
        Assert.DoesNotContain("clave", fail.Failure.Reason);
        Assert.DoesNotContain("usuario", fail.Failure.Reason);
    }

    [Fact]
    public async Task ElPedido_EsTimeStampReq_ConCertReqNonceEImprint()
    {
        var handler = new Handler((_, _) => Task.FromResult(Ok(Fx.Tsr("freetsa"))));
        await Client(handler, 5, "freetsa", "https://freetsa.org/tsr")
            .RequestAsync(Fx.Sha256, TimeSpan.FromSeconds(10), default);

        var seen = Assert.Single(handler.Seen);
        Assert.Equal("application/timestamp-query", seen.ContentType);
        Assert.Contains("application/timestamp-reply", seen.Accept);
        Assert.True(Rfc3161TimestampRequest.TryDecode(seen.Body, out var req, out var consumed));
        Assert.Equal(seen.Body.Length, consumed);
        Assert.True(req!.RequestSignerCertificate);
        // El nonce viaja como INTEGER positivo: con el bit alto prendido lleva un 0x00 adelante.
        Assert.Equal(Fx.Nonce("freetsa"), req.GetNonce()!.Value.ToArray().SkipWhile(b => b == 0).ToArray());
        Assert.Equal(Fx.Hash, req.GetMessageHash().ToArray());
        Assert.Equal(TimestampResponseValidator.Sha256Oid, req.HashAlgorithmId.Value);
        // Solo viaja el hash: el cuerpo es chico (≈ 60 bytes) y no contiene nada más que el TimeStampReq.
        Assert.True(seen.Body.Length < 100);
        _ = new AsnReader(seen.Body, AsnEncodingRules.DER).ReadSequence();
    }
}
