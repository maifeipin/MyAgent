using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MyAgent.Core.Data.Models;
using MyAgent.Core.Data.Repositories;
using MyAgent.Core.Factories;
using MyAgent.Core.Models;
using Newtonsoft.Json.Linq;

namespace MyAgent.Core.Engine;

public class SkillEngine
{
    private readonly IActionFactory _actionFactory;
    private readonly IExecutionLogRepository _logRepository;
    private readonly ILogger<SkillEngine> _logger;

    public event Action<string>? OnProgressLog;

    public SkillEngine(IActionFactory actionFactory, IExecutionLogRepository logRepository, ILogger<SkillEngine> logger)
    {
        _actionFactory = actionFactory;
        _logRepository = logRepository;
        _logger = logger;
    }

    private void EmitLog(string message)
    {
        OnProgressLog?.Invoke(message);
    }

    /// <summary>
    /// 万物之源：按顺序执行 YAML 解析出的每一个步骤
    /// </summary>
    public async Task<ActionResult> ExecuteSkillAsync(SkillDefinition skillDef, int triggerMode, System.Collections.Generic.Dictionary<string, object>? envArgs, CancellationToken cancellationToken)
    {
        var context = new SkillExecutionContext
        {
            ExecutionId = Guid.NewGuid().ToString("N"),
            SkillId = skillDef.SkillId,
            CancellationToken = cancellationToken,
            Logger = EmitLog
        };
        
        if (envArgs != null)
        {
            foreach(var kv in envArgs) context.EnvironmentArgs[kv.Key] = kv.Value;
        }

        var executionLog = new ExecutionLog
        {
            Id = context.ExecutionId,
            SkillId = skillDef.SkillId,
            StartTime = DateTime.Now,
            TriggerMode = triggerMode,
            Status = 1 // 假设运行中，最终更新状态
        };

        await _logRepository.InsertExecutionLogAsync(executionLog);
        _logger.LogInformation("🚀 Started executing skill {SkillId} (Exec: {ExecId})", skillDef.SkillId, context.ExecutionId);
        EmitLog($"🚀 Started executing skill {skillDef.SkillId}");

        try
        {
            foreach (var step in skillDef.Workflow)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var stepResult = await ExecuteStepWithRetryAsync(context, step, cancellationToken);
                
                if (!stepResult.IsSuccess && step.OnError == "abort")
                {
                    _logger.LogError("❌ Step {StepId} failed and strategy is 'abort'. Stopping workflow.", step.StepId);
                    EmitLog($"❌ Step {step.StepId} failed. Stopping workflow.");
                    await _logRepository.UpdateExecutionLogStatusAsync(context.ExecutionId, 3, DateTime.Now);
                    return ActionResult.Fail($"Step {step.StepId} failed: {stepResult.ErrorMessage}");
                }
            }

            _logger.LogInformation("✅ Skill {SkillId} execution completed successfully.", skillDef.SkillId);
            EmitLog($"✅ Skill {skillDef.SkillId} execution completed successfully.");
            await _logRepository.UpdateExecutionLogStatusAsync(context.ExecutionId, 1, DateTime.Now);
            
            var finalResult = ActionResult.Success();
            foreach (var kvp in context.StateBag)
            {
                finalResult.OutputData[kvp.Key] = kvp.Value;
            }
            return finalResult;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("⚠️ Execution {ExecId} was manually cancelled.", context.ExecutionId);
            EmitLog($"⚠️ Execution was manually cancelled.");
            await _logRepository.UpdateExecutionLogStatusAsync(context.ExecutionId, 4, DateTime.Now);
            return ActionResult.Fail("Execution was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "🔥 Fatal error in execution {ExecId}", context.ExecutionId);
            EmitLog($"🔥 Fatal error: {ex.Message}");
            await _logRepository.UpdateExecutionLogStatusAsync(context.ExecutionId, 3, DateTime.Now);
            return ActionResult.Fail($"Fatal error: {ex.Message}");
        }
    }

    private async Task<ActionResult> ExecuteStepWithRetryAsync(SkillExecutionContext context, WorkflowStep step, CancellationToken cancellationToken)
    {
        var actionTool = _actionFactory.CreateAction(step.Action);
        if (actionTool == null)
            return ActionResult.Fail($"Tool {step.Action} not found in registry.");

        // 参数插值引擎：动态替换 {{变量名}} 为上下文真实数据
        var interpolatedParams = (JObject)InterpolateToken(step.Params, context);
        
        int maxAttempts = step.RetryPolicy?.MaxAttempts ?? 1;
        int delayMs = step.RetryPolicy?.DelayMs ?? 1000;
        
        ActionResult lastResult = ActionResult.Fail("Not executed");

        var sw = Stopwatch.StartNew();

        for (int i = 1; i <= maxAttempts; i++)
        {
            try
            {
                _logger.LogInformation("  -> Executing Step [{StepId}] (Attempt {Attempt}): {Action}", step.StepId, i, step.Action);
                EmitLog($"  -> Executing Step [{step.StepId}] (Attempt {i}): {step.Action}");
                
                // 设置步骤级别的超时控制
                using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                stepCts.CancelAfter(step.TimeoutMs > 0 ? step.TimeoutMs : 30000);

                lastResult = await actionTool.ExecuteAsync(context, interpolatedParams, stepCts.Token);
                
                if (lastResult.IsSuccess)
                {
                    // 自动将执行产生的数据合并进入上下文大字典 (StateBag)
                    foreach (var kvp in lastResult.OutputData)
                    {
                        context.StateBag[kvp.Key] = kvp.Value;
                    }
                    break;
                }
                
                _logger.LogWarning("  -> Step [{StepId}] failed (Attempt {Attempt}): {Error}", step.StepId, i, lastResult.ErrorMessage);
                EmitLog($"  -> Step [{step.StepId}] failed (Attempt {i}): {lastResult.ErrorMessage}");
            }
            catch (Exception ex)
            {
                lastResult = ActionResult.Fail(ex.Message);
                _logger.LogError(ex, "  -> Step [{StepId}] threw exception on attempt {Attempt}", step.StepId, i);
                EmitLog($"  -> Step [{step.StepId}] threw exception: {ex.Message}");
            }

            if (i < maxAttempts)
            {
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        sw.Stop();
        
        // 记录 Step 级别日志
        var stepLog = new StepLog
        {
            ExecutionId = context.ExecutionId,
            StepId = step.StepId,
            ActionName = step.Action,
            DurationMs = (int)sw.ElapsedMilliseconds,
            Status = lastResult.IsSuccess ? 1 : 3,
            RawInput = interpolatedParams.ToString(Newtonsoft.Json.Formatting.None),
            RawOutput = lastResult.IsSuccess ? "Success" : lastResult.ErrorMessage
        };
        await _logRepository.InsertStepLogAsync(stepLog);

        return lastResult;
    }

    /// <summary>
    /// JToken 树形深度遍历插值引擎：负责把 {"username": "{{StateBag.User}}"} 变成上下文里的真实字符串
    /// </summary>
    private JToken InterpolateToken(JToken token, SkillExecutionContext context)
    {
        if (token == null) return new JObject();

        if (token.Type == JTokenType.Object)
        {
            var obj = new JObject();
            foreach (var prop in ((JObject)token).Properties())
            {
                obj.Add(prop.Name, InterpolateToken(prop.Value, context));
            }
            return obj;
        }
        if (token.Type == JTokenType.Array)
        {
            var arr = new JArray();
            foreach (var item in (JArray)token)
            {
                arr.Add(InterpolateToken(item, context));
            }
            return arr;
        }
        if (token.Type == JTokenType.String)
        {
            string strValue = token.Value<string>() ?? string.Empty;

            // 1. 精确匹配（整个字符串只有一个变量）：此时可以直接投递 JArray / JObject 等对象，不再降级转 String
            var exactMatch = Regex.Match(strValue, @"^\{\{StateBag\.([a-zA-Z0-9_\.]+)(?:\|([a-zA-Z]+))?\}\}$");
            if (exactMatch.Success)
            {
                var fullKey = exactMatch.Groups[1].Value;
                var format = exactMatch.Groups[2].Success ? exactMatch.Groups[2].Value.ToLower() : "";
                var parts = fullKey.Split('.');
                
                JToken? currentToken = null;
                if (context.StateBag.TryGetValue(parts[0], out var topObj) && topObj != null)
                {
                    currentToken = ResolveTokenPath(topObj, parts);
                }
                if (currentToken == null && context.EnvironmentArgs.TryGetValue(parts[0], out var envObj) && envObj != null)
                {
                    currentToken = ResolveTokenPath(envObj, parts);
                }

                if (currentToken != null)
                {
                    if (format == "json")
                    {
                        var jsonStr = currentToken.Type == JTokenType.String 
                                        ? currentToken.Value<string>()! 
                                        : currentToken.ToString(Newtonsoft.Json.Formatting.None);
                        return new JValue(Newtonsoft.Json.JsonConvert.SerializeObject(jsonStr));
                    }
                    return currentToken.DeepClone();
                }
            }

            // 2. 混合匹配（文本里零散穿插了变量段）：退化为传统的 String 文本正则插值
            var regex = new Regex(@"\{\{StateBag\.([a-zA-Z0-9_\.]+)(?:\|([a-zA-Z]+))?\}\}");
            string replacedStr = regex.Replace(strValue, match =>
            {
                var fullKey = match.Groups[1].Value;
                var format = match.Groups[2].Success ? match.Groups[2].Value.ToLower() : "";
                var parts = fullKey.Split('.');

                JToken? currentToken = null;
                if (context.StateBag.TryGetValue(parts[0], out var topObj) && topObj != null)
                {
                    currentToken = ResolveTokenPath(topObj, parts);
                }
                if (currentToken == null && context.EnvironmentArgs.TryGetValue(parts[0], out var envObj) && envObj != null)
                {
                    currentToken = ResolveTokenPath(envObj, parts);
                }
                
                if (currentToken == null) return match.Value;

                string val = currentToken.Type == JTokenType.String 
                              ? currentToken.Value<string>()! 
                              : (currentToken.Type == JTokenType.Null ? "" : currentToken.ToString(Newtonsoft.Json.Formatting.None));

                if (format == "json")
                {
                    return Newtonsoft.Json.JsonConvert.SerializeObject(val);
                }

                return val;
            });

            return new JValue(replacedStr);
        }

        return token.DeepClone();
    }

    private JToken? ResolveTokenPath(object root, string[] parts)
    {
        if (parts.Length == 1) 
        {
            return root is JToken jt ? jt : new JValue(root);
        }
        
        if (root is JToken jtoken)
        {
            var current = jtoken;
            for (int i = 1; i < parts.Length; i++)
            {
                if (current is JObject jobj && jobj.TryGetValue(parts[i], StringComparison.OrdinalIgnoreCase, out var child))
                {
                    current = child;
                }
                else return null;
            }
            return current;
        }
        return null;
    }
}
