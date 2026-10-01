using System.ComponentModel.DataAnnotations;
using Factum.Backend.Models;

namespace Factum.Backend.DTOs;

public sealed record ReportAgentEventRequest(
    [Required] string Hostname,
    [Required] string OsUser,
    [Required] string AgentVersion,
    [Required] AgentMode Mode,
    [Required] AgentAction Action,
    string? CaseId
);
