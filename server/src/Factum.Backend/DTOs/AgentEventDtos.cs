using System.ComponentModel.DataAnnotations;
using Factum.Backend.Models;

namespace Factum.Backend.DTOs;

public sealed record ReportAgentEventRequest(
    [Required] string Hostname,
    [Required] string OsUser,
    [Required] string AgentVersion,
    [Required] AgentMode Mode,
    [Required] AgentAction Action,
    string? CaseId,
    // wizard-concurrencia-pestanas DT11: solo con action = capture_failed (failed_action obligatorio).
    AgentAction? FailedAction = null,
    string? Reason = null
);
