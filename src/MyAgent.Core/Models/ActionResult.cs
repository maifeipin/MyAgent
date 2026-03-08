using System.Collections.Generic;

namespace MyAgent.Core.Models;

public class ActionResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    
    // 执行产出的数据，将被引擎自动合并到 context.StateBag
    public Dictionary<string, object> OutputData { get; set; } = new();

    public static ActionResult Success() => new() { IsSuccess = true };
    public static ActionResult Success(Dictionary<string, object> data) => new() { IsSuccess = true, OutputData = data };
    public static ActionResult Fail(string error) => new() { IsSuccess = false, ErrorMessage = error };
}
