using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace MyAgent.Core.Models;

public class SkillDefinition
{
    public string SchemaVersion { get; set; } = "1.1";
    public string SkillId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public TriggerDefinition Trigger { get; set; } = new();
    public List<WorkflowStep> Workflow { get; set; } = new();
    public List<SuccessCriteria> SuccessCriteria { get; set; } = new();
}

public class TriggerDefinition
{
    public string Type { get; set; } = "manual"; // schedule, manual, event, api
    public string? Cron { get; set; }
    public List<string> Keywords { get; set; } = new();
}

public class WorkflowStep
{
    public string StepId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    
    // 映射到具体的 IActionTool.ActionType (例如 "browser.navigate")
    public string Action { get; set; } = string.Empty;
    
    // 该 Action 所需要的具体参数，使用 JObject 方便后期反射装配
    public JObject Params { get; set; } = new();
    
    public int TimeoutMs { get; set; } = 30000;
    
    public RetryPolicy? RetryPolicy { get; set; }
    
    public string OnError { get; set; } = "abort"; // continue, abort, fallback
    
    public FallbackAction? FallbackAction { get; set; }
}

public class RetryPolicy
{
    public int MaxAttempts { get; set; } = 1;
    public int DelayMs { get; set; } = 1000;
}

public class FallbackAction
{
    public string Action { get; set; } = string.Empty;
    public JObject Params { get; set; } = new();
}

public class SuccessCriteria
{
    public string Type { get; set; } = string.Empty;
    public JObject Condition { get; set; } = new();
}
