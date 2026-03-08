using System;
using System.Collections.Generic;
using System.Threading;

namespace MyAgent.Core.Models;

public class SkillExecutionContext
{
    public string ExecutionId { get; set; } = Guid.NewGuid().ToString("N");
    public string SkillId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; } = DateTime.Now;
    
    // 运行时环境变量 (例如：全局超时, 账号密码等配置信息)
    public Dictionary<string, object> EnvironmentArgs { get; set; } = new();
    
    // 步骤间数据传递 (Step 1 提取的数据，存入此处供 Step 2 的 AI 总结使用)
    public Dictionary<string, object> StateBag { get; set; } = new();
    
    // 取消令牌，用于响应全局中止事件
    public CancellationToken CancellationToken { get; set; }

    // 独立日志反馈通道：支持底层原子工具将内部详细日志（如 SSH 回显）反哺给 UI
    public Action<string>? Logger { get; set; }
}
