using MongoDB.Bson.Serialization.Attributes;

namespace Factum.Backend.Models;

/// <summary>
/// Grabación de la sesión del caso (grabacion-sesion-completa §6.1): el switch del perito y la copia validada y
/// append-only del diario de Tatana (D-T1). <c>Case.RecordingSession</c> es null en todos los casos anteriores (sin
/// backfill). Todas las horas son UTC del reloj de la PC (Tatana); las del servidor van aparte (<see cref="SyncedAt"/>,
/// eventos). JSON: <c>recording_session</c> (el backend serializa los null).
/// </summary>
[BsonIgnoreExtraElements]
public sealed class RecordingSession
{
    /// <summary>Switch del perito (o forzado por el modo <c>required</c>).</summary>
    public bool Enabled { get; set; }
    public bool WithCamera { get; set; }
    /// <summary>Modo de la instalación cuando se cambió el switch (auditoría).</summary>
    public string ModeAtChange { get; set; } = "";
    public DateTime SettingsUpdatedAt { get; set; }

    /// <summary>Del diario de Tatana (32 hex).</summary>
    [BsonIgnoreIfNull] public string? SessionId { get; set; }
    /// <summary><c>android</c> | <c>ios</c>.</summary>
    [BsonIgnoreIfNull] public string? Platform { get; set; }
    /// <summary>Serial / UDID (= <c>Case.Device.Serial</c>).</summary>
    [BsonIgnoreIfNull] public string? ConnectionId { get; set; }
    [BsonIgnoreIfNull] public string? AgentVersion { get; set; }
    public int SegmentMinutes { get; set; }
    /// <summary><c>device</c> | <c>none</c>.</summary>
    [BsonIgnoreIfNull] public string? Audio { get; set; }
    [BsonIgnoreIfNull] public SessionCamera? Camera { get; set; }
    /// <summary>Último estado informado por Tatana (§5.3).</summary>
    [BsonIgnoreIfNull] public string? State { get; set; }
    /// <summary>Solo tramos cerrados (<c>saved</c>/<c>discarded</c>): el tramo en curso no llega al backend.</summary>
    public List<SessionSegment> Segments { get; set; } = [];
    public List<SessionGap> Gaps { get; set; } = [];
    public List<SessionMark> Marks { get; set; } = [];
    /// <summary>Hora del servidor del último sync (concurrencia optimista).</summary>
    [BsonIgnoreIfNull] public DateTime? SyncedAt { get; set; }
}

/// <summary>Cámara del procedimiento (F2).</summary>
[BsonIgnoreExtraElements]
public sealed class SessionCamera
{
    public string Name { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public int Fps { get; set; }
    public bool WithAudio { get; set; }
}

/// <summary>Tramo cerrado de la grabación de la sesión.</summary>
[BsonIgnoreExtraElements]
public sealed class SessionSegment
{
    public string Id { get; set; } = "";
    /// <summary><c>pantalla</c> | <c>camara</c>.</summary>
    public string Track { get; set; } = "";
    public int Index { get; set; }
    public string Filename { get; set; } = "";
    /// <summary><c>saved</c> | <c>discarded</c>.</summary>
    public string State { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime StoppedAt { get; set; }
    public double RecordedSeconds { get; set; }
    [BsonIgnoreIfNull] public double? DurationSeconds { get; set; }
    /// <summary><c>ok</c> | <c>incomplete</c> (solo <c>saved</c>).</summary>
    [BsonIgnoreIfNull] public string? Validation { get; set; }
    public bool Recovered { get; set; }
    [BsonIgnoreIfNull] public double? OverlapSeconds { get; set; }
    [BsonIgnoreIfNull] public string? StopReason { get; set; }
    [BsonIgnoreIfNull] public long? Size { get; set; }
    [BsonIgnoreIfNull] public string? Sha256 { get; set; }
    /// <summary>F2: configuración de la cámara con la que se grabó el tramo (solo tramos <c>camara</c>).</summary>
    [BsonIgnoreIfNull] public SessionCamera? Camera { get; set; }
}

/// <summary>Hueco de la grabación: <c>To</c> null mientras sigue abierto.</summary>
[BsonIgnoreExtraElements]
public sealed class SessionGap
{
    public string Id { get; set; } = "";
    public string Track { get; set; } = "";
    public DateTime From { get; set; }
    [BsonIgnoreIfNull] public DateTime? To { get; set; }
    public string Reason { get; set; } = "";
    public bool SegmentLost { get; set; }
}

/// <summary>Marca de momento (inmutable). Su texto nunca va a <c>case_events</c>.</summary>
[BsonIgnoreExtraElements]
public sealed class SessionMark
{
    public string Id { get; set; } = "";
    public DateTime At { get; set; }
    /// <summary>
    /// Tramo de pantalla que contiene la marca. F2 [DU1=A]: null si la marca se hizo con la pantalla sin grabar (solo
    /// con <see cref="CameraSegmentIndex"/>). Los documentos de F1 tienen el entero y se leen igual.
    /// </summary>
    public int? SegmentIndex { get; set; }
    /// <summary>Segundo del video de pantalla; null junto con <see cref="SegmentIndex"/>.</summary>
    [BsonIgnoreIfNull] public double? OffsetSeconds { get; set; }
    /// <summary>F2: tramo de cámara que contiene la marca (null si la cámara no grababa).</summary>
    [BsonIgnoreIfNull] public int? CameraSegmentIndex { get; set; }
    /// <summary>F2: segundo del video de cámara (junto con <see cref="CameraSegmentIndex"/>).</summary>
    [BsonIgnoreIfNull] public double? CameraOffsetSeconds { get; set; }
    [BsonIgnoreIfNull] public string? Text { get; set; }
    [BsonIgnoreIfNull] public string? Filename { get; set; }
    /// <summary><c>manual</c> | <c>captura</c>.</summary>
    public string Kind { get; set; } = "";
}
