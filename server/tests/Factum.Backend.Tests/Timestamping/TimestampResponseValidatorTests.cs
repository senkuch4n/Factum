using System.Formats.Asn1;
using System.Security.Cryptography;
using Factum.Backend.Models;
using Factum.Backend.Services.Timestamping;

namespace Factum.Backend.Tests.Timestamping;

/// <summary>
/// <see cref="TimestampResponseValidator"/> (sellado-tiempo §5.2, §11.2) contra respuestas reales versionadas en
/// <c>Fixtures/Tsa/</c>: la request se reconstruye con el MISMO nonce y hash del pedido original. Sin red.
/// </summary>
public sealed class TimestampResponseValidatorTests
{
    private static readonly TsaFixtures.Info Fx = TsaFixtures.Load();

    public static TheoryData<string, string, string> Hosts => new()
    {
        { "freetsa", "freetsa.org", "Free TSA" },
        { "digicert", "timestamp.digicert.com", "DigiCert" },
    };

    [Theory]
    [MemberData(nameof(Hosts))]
    public void Fixture_EsGranted_ConLosDatosEsperados(string key, string host, string authority)
    {
        var v = TimestampResponseValidator.Validate(Fx.Request(key), Fx.Hash, Fx.Tsr(key), 200, host);

        Assert.True(v.IsValid, v.Failure?.Reason);
        Assert.Equal(Fx.GenTimes[key], v.Data!.GenTime);
        Assert.Equal(DateTimeKind.Utc, v.Data.GenTime.Kind);
        Assert.Equal(Fx.Serials[key], v.Data.SerialNumber);
        Assert.Equal(Fx.Policies[key], v.Data.PolicyOid);
        Assert.Equal(authority, v.Data.Authority);
        Assert.False(string.IsNullOrWhiteSpace(v.Data.SignerSubject));
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void OtroNonce_EsInvalida(string key, string host, string _)
    {
        var other = Fx.Nonce(key).ToArray();
        other[0] ^= 0xFF;
        var v = TimestampResponseValidator.Validate(Fx.Request(key, nonce: other), Fx.Hash, Fx.Tsr(key), 200, host);
        Assert.Equal(TimestampErrorKinds.InvalidResponse, v.Failure?.Kind);
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void OtroHash_EsInvalida(string key, string host, string _)
    {
        var otherHash = SHA256.HashData("otro contenido"u8);
        var v = TimestampResponseValidator.Validate(Fx.Request(key, hash: otherHash), otherHash, Fx.Tsr(key), 200, host);
        Assert.Equal(TimestampErrorKinds.InvalidResponse, v.Failure?.Kind);
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void CertReqFalse_ContraFixtureConCertificados_EsInvalida(string key, string host, string _)
    {
        var v = TimestampResponseValidator.Validate(Fx.Request(key, certReq: false), Fx.Hash, Fx.Tsr(key), 200, host);
        Assert.Equal(TimestampErrorKinds.InvalidResponse, v.Failure?.Kind);
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void BytesDeMas_EsInvalida(string key, string host, string _)
    {
        var body = Fx.Tsr(key).Concat(new byte[] { 0x00, 0x00 }).ToArray();
        var v = TimestampResponseValidator.Validate(Fx.Request(key), Fx.Hash, body, 200, host);
        Assert.Equal(TimestampErrorKinds.InvalidResponse, v.Failure?.Kind);
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void UltimoByteAlterado_NoEsGranted(string key, string host, string _)
    {
        var body = Fx.Tsr(key);
        body[^1] ^= 0x01; // cae en la firma
        var v = TimestampResponseValidator.Validate(Fx.Request(key), Fx.Hash, body, 200, host);
        Assert.False(v.IsValid);
    }

    [Fact]
    public void Basura_EsInvalida()
    {
        var v = TimestampResponseValidator.Validate(Fx.Request("freetsa"), Fx.Hash, [0x30, 0x03, 0x02, 0x01, 0x02]);
        Assert.Equal(TimestampErrorKinds.InvalidResponse, v.Failure?.Kind);
    }

    [Fact]
    public void EstadoRejection_EsRejected()
    {
        // TimeStampResp ::= SEQUENCE { status PKIStatusInfo ::= SEQUENCE { status INTEGER (2 = rejection) } }
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        using (w.PushSequence())
            w.WriteInteger(2);
        var v = TimestampResponseValidator.Validate(Fx.Request("freetsa"), Fx.Hash, w.Encode());
        Assert.Equal(TimestampErrorKinds.Rejected, v.Failure?.Kind);
    }

    [Fact]
    public void CuerpoVacio_EsInvalida() =>
        Assert.Equal(TimestampErrorKinds.InvalidResponse,
            TimestampResponseValidator.Validate(Fx.Request("freetsa"), Fx.Hash, []).Failure?.Kind);

    [Fact]
    public void CuerpoDeMasDe64KiB_EsInvalida() =>
        Assert.Equal(TimestampErrorKinds.InvalidResponse,
            TimestampResponseValidator.Validate(Fx.Request("freetsa"), Fx.Hash, new byte[64 * 1024 + 1]).Failure?.Kind);

    [Fact]
    public void Http503_EsHttpError()
    {
        var v = TimestampResponseValidator.Validate(Fx.Request("freetsa"), Fx.Hash, null, 503);
        Assert.Equal(TimestampErrorKinds.HttpError, v.Failure?.Kind);
        Assert.Equal("HTTP 503", v.Failure?.Reason);
    }

    [Fact]
    public void AutoridadDesconocida_UsaElFirmante()
    {
        var v = TimestampResponseValidator.Validate(Fx.Request("freetsa"), Fx.Hash, Fx.Tsr("freetsa"), 200,
            "tsa.otra.example");
        Assert.True(v.IsValid);
        // Host fuera del catálogo: sale el O del firmante (el de FreeTSA es "O=Free TSA"), no el host.
        Assert.Equal("Free TSA", v.Data!.Authority);
        var dg = TimestampResponseValidator.Validate(Fx.Request("digicert"), Fx.Hash, Fx.Tsr("digicert"), 200,
            "tsa.otra.example");
        Assert.Equal("DigiCert, Inc.", dg.Data!.Authority);
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x00, 0x0A, 0x1B }, "0A1B")]
    [InlineData(new byte[] { 0x7F }, "7F")]
    [InlineData(new byte[] { 0x00 }, "00")]
    public void SerialHex_SinCerosALaIzquierda(byte[] raw, string expected) =>
        Assert.Equal(expected, TimestampResponseValidator.SerialHex(raw));
}
