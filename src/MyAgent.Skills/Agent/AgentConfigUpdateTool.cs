using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MyAgent.Core.Attributes;
using MyAgent.Core.Interfaces;
using MyAgent.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MyAgent.Skills.Agent;

[SkillAction("agent.update_config")]
public class AgentConfigUpdateTool : IActionTool
{
    private readonly ILogger<AgentConfigUpdateTool> _logger;

    public string ActionType => "agent.update_config";

    public AgentConfigUpdateTool(ILogger<AgentConfigUpdateTool> logger)
    {
        _logger = logger;
    }

    public Task<ActionResult> ExecuteAsync(SkillExecutionContext context, JToken parameters, CancellationToken cancellationToken)
    {
        try
        {
            // 获取 UI 专门传递进来的配置修改委托
            var updater = context.EnvironmentArgs.TryGetValue("AgentConfigUpdater", out var u) ? u as Action<JToken> : null;
            if (updater == null)
            {
                return Task.FromResult(ActionResult.Fail("宿主环境未提供 AgentConfigUpdater 回调！更新自身配置失败。"));
            }

            // 提取目标变更指令，并传递回 ViewModel 层进行覆盖与写盘
            updater.Invoke(parameters);

            _logger.LogInformation("Agent 成功执行了对其自身设定的自修改命令.");
            return Task.FromResult(ActionResult.Success());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "agent.update_config 异常失败。");
            return Task.FromResult(ActionResult.Fail(ex.Message));
        }
    }
}
