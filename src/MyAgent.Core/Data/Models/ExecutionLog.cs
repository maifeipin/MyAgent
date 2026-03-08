using System;

namespace MyAgent.Core.Data.Models;

public class ExecutionLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SkillId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int Status { get; set; }     // 1: Success, 2: PartialSuccess, 3: Failed, 4: Cancelled
    public int TriggerMode { get; set; } // 1: Manual, 2: Schedule, 3: Event
}

public class StepLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ExecutionId { get; set; } = string.Empty;
    public string StepId { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public int DurationMs { get; set; }
    public int Status { get; set; }
    public string? RawInput { get; set; }
    public string? RawOutput { get; set; }
}
