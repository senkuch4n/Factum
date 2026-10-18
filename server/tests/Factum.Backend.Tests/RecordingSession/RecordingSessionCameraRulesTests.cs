using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Cases;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Factum.Backend.Tests.SesionGrabacion;

/// <summary>
/// F2 (grabacion-sesion-completa-f2 §6.1–§6.2): cámara por tramo, ubicación de las marcas en la cámara y [DU1=A] marcas
/// sin <c>segment_index</c>.
/// </summary>
public sealed class RecordingSessionCameraRulesTests
{
    private static readonly DateTime Now = SessionData.T0.AddHours(5);
    private static readonly SessionCameraDto Cam = new("Cámara de la MacBook Air", 1280, 720, 15, false);

    private static SessionSegmentDto CamSeg(int index, SessionCameraDto? camera = null, char sha = 'c') =>
        SessionData.Segment(index, track: "camara", sha: sha, id: SessionData.Id(500 + index)) with { Camera = camera ?? Cam };

    private static SessionMarkDto CamMark(int n, int? camIndex = 2, double? camOffset = 470.3) =>
        SessionData.Mark(n) with { CameraSegmentIndex = camIndex, CameraOffsetSeconds = camOffset };

    private static string? Field(RecordingSessionSnapshotRequest s) => RecordingSessionRules.Validate(s, Now).Field;

    [Fact]
    public void TramoDeCamara_ConCamara_Valido()
    {
        var v = SessionData.Valid(SessionData.Snapshot([SessionData.Segment(1), CamSeg(1)]));
        var cam = v.Segments.Single(s => s.Track == "camara").Camera!;
        Assert.Equal("Cámara de la MacBook Air", cam.Name);
        Assert.Equal((1280, 720, 15, false), (cam.Width, cam.Height, cam.Fps, cam.WithAudio));
        Assert.Null(v.Segments.Single(s => s.Track == "pantalla").Camera);
    }

    [Fact]
    public void TramoDeCamara_Invalidos()
    {
        Assert.Equal("segments[0].camera", Field(SessionData.Snapshot([SessionData.Segment(1) with { Camera = Cam }])));
        Assert.Equal("segments[0].camera", Field(SessionData.Snapshot([CamSeg(1, Cam with { Name = "  " })])));
        Assert.Equal("segments[0].camera", Field(SessionData.Snapshot([CamSeg(1, Cam with { Name = new string('x', 201) })])));
        Assert.Equal("segments[0].camera", Field(SessionData.Snapshot([CamSeg(1, Cam with { Width = 0 })])));
        Assert.Equal("segments[0].camera", Field(SessionData.Snapshot([CamSeg(1, Cam with { Height = 10001 })])));
        Assert.Equal("segments[0].camera", Field(SessionData.Snapshot([CamSeg(1, Cam with { Fps = 241 })])));
        Assert.Equal("segments[0].camera", Field(SessionData.Snapshot([CamSeg(1, Cam with { Fps = null })])));
    }

    [Fact]
    public void Marca_UbicacionEnLaCamara_LosDosONinguno()
    {
        var v = SessionData.Valid(SessionData.Snapshot(marks: [CamMark(1)]));
        Assert.Equal((2, 470.3), (v.Marks[0].CameraSegmentIndex, v.Marks[0].CameraOffsetSeconds));
        Assert.Equal(1, v.Marks[0].SegmentIndex);

        Assert.Equal("marks[0].camera_segment_index", Field(SessionData.Snapshot(marks: [CamMark(1, camOffset: null)])));
        Assert.Equal("marks[0].camera_segment_index", Field(SessionData.Snapshot(marks: [CamMark(1, camIndex: null)])));
        Assert.Equal("marks[0].camera_segment_index", Field(SessionData.Snapshot(marks: [CamMark(1, camIndex: 0)])));
        Assert.Equal("marks[0].camera_segment_index", Field(SessionData.Snapshot(marks: [CamMark(1, camIndex: 10000)])));
        Assert.Equal("marks[0].camera_segment_index", Field(SessionData.Snapshot(marks: [CamMark(1, camOffset: -1)])));
        Assert.Equal("marks[0].camera_segment_index",
            Field(SessionData.Snapshot(marks: [CamMark(1, camOffset: double.NaN)])));
    }

    [Fact]
    public void Marca_SinSegmentIndex_SoloConCamara_DU1A()
    {
        var onlyCamera = CamMark(1) with { SegmentIndex = null, OffsetSeconds = null };
        var v = SessionData.Valid(SessionData.Snapshot(marks: [onlyCamera]));
        Assert.Null(v.Marks[0].SegmentIndex);
        Assert.Null(v.Marks[0].OffsetSeconds);
        Assert.Equal(2, v.Marks[0].CameraSegmentIndex);

        // Sin ninguna ubicación, o con uno de los dos campos de pantalla: segment_index.
        Assert.Equal("marks[0].segment_index",
            Field(SessionData.Snapshot(marks: [SessionData.Mark(1) with { SegmentIndex = null, OffsetSeconds = null }])));
        Assert.Equal("marks[0].segment_index",
            Field(SessionData.Snapshot(marks: [CamMark(1) with { SegmentIndex = null }])));
        Assert.Equal("marks[0].segment_index",
            Field(SessionData.Snapshot(marks: [CamMark(1) with { OffsetSeconds = null }])));
    }

    [Fact]
    public void Merge_MarcaConOtraUbicacionEnLaCamara_Conflict()
    {
        var a = RecordingSessionRules.Merge(SessionData.Enabled(),
            SessionData.Valid(SessionData.Snapshot(marks: [CamMark(1)]))).Merged!;
        Assert.False(RecordingSessionRules.Merge(a, SessionData.Valid(SessionData.Snapshot(marks: [CamMark(1)]))).Changes!.Any);
        Assert.Equal("marks", RecordingSessionRules.Merge(a,
            SessionData.Valid(SessionData.Snapshot(marks: [CamMark(1, camIndex: 3)]))).ConflictField);
        Assert.Equal("marks", RecordingSessionRules.Merge(a,
            SessionData.Valid(SessionData.Snapshot(marks: [CamMark(1, camOffset: 470.4)]))).ConflictField);
        Assert.Equal("marks", RecordingSessionRules.Merge(a,
            SessionData.Valid(SessionData.Snapshot(marks: [SessionData.Mark(1)]))).ConflictField);

        var onlyCamera = RecordingSessionRules.Merge(SessionData.Enabled(), SessionData.Valid(SessionData.Snapshot(
            marks: [CamMark(2) with { SegmentIndex = null, OffsetSeconds = null }]))).Merged!;
        Assert.Equal("marks", RecordingSessionRules.Merge(onlyCamera,
            SessionData.Valid(SessionData.Snapshot(marks: [CamMark(2)]))).ConflictField);
    }

    [Fact]
    public void Merge_CamaraDelTramo_SeCompletaUnaVez_DistintaConflict()
    {
        var sinCamara = CamSeg(1) with { Camera = null };
        var a = RecordingSessionRules.Merge(SessionData.Enabled(),
            SessionData.Valid(SessionData.Snapshot([sinCamara]))).Merged!;
        Assert.Null(a.Segments[0].Camera);

        var b = RecordingSessionRules.Merge(a, SessionData.Valid(SessionData.Snapshot([CamSeg(1)]))).Merged!;
        Assert.Equal("Cámara de la MacBook Air", b.Segments[0].Camera!.Name);

        // Igual: sin cambios. Otra: conflicto. Un snapshot viejo sin cámara no la borra.
        Assert.False(RecordingSessionRules.Merge(b, SessionData.Valid(SessionData.Snapshot([CamSeg(1)]))).IsConflict);
        Assert.Equal("segments.camera", RecordingSessionRules.Merge(b,
            SessionData.Valid(SessionData.Snapshot([CamSeg(1, Cam with { Fps = 30 })]))).ConflictField);
        var stale = RecordingSessionRules.Merge(b, SessionData.Valid(SessionData.Snapshot([sinCamara])));
        Assert.Equal(15, stale.Merged!.Segments[0].Camera!.Fps);
    }

    [Fact]
    public void Copy_NoCompartenInstancias()
    {
        var a = RecordingSessionRules.Merge(SessionData.Enabled(),
            SessionData.Valid(SessionData.Snapshot([CamSeg(1)], marks: [CamMark(1)]))).Merged!;
        var b = RecordingSessionRules.Merge(a, SessionData.Valid(SessionData.Snapshot([CamSeg(2, sha: 'd')]))).Merged!;
        Assert.NotSame(a.Segments[0].Camera, b.Segments[0].Camera);
        Assert.Equal(470.3, b.Marks[0].CameraOffsetSeconds);
    }

    [Fact]
    public void Bson_DocumentoDeF1_SeLeeConLosCamposAnulables()
    {
        // Documento de F1: segment_index entero, offset doble, sin campos de cámara.
        var doc = new BsonDocument
        {
            ["Id"] = "m", ["At"] = SessionData.T0, ["SegmentIndex"] = 3, ["OffsetSeconds"] = 12.5, ["Kind"] = "manual",
        };
        var m = BsonSerializer.Deserialize<SessionMark>(doc);
        Assert.Equal(3, m.SegmentIndex);
        Assert.Equal(12.5, m.OffsetSeconds);
        Assert.Null(m.CameraSegmentIndex);

        // Una marca solo de cámara no escribe offset_seconds ni campos de cámara vacíos en los tramos.
        var only = new SessionMark { Id = "x", At = SessionData.T0, CameraSegmentIndex = 1, CameraOffsetSeconds = 2, Kind = "manual" };
        var bson = only.ToBsonDocument();
        Assert.False(bson.Contains("OffsetSeconds"));
        Assert.True(bson.Contains("CameraSegmentIndex"));
        Assert.False(new SessionSegment { Id = "s" }.ToBsonDocument().Contains("Camera"));
    }
}
