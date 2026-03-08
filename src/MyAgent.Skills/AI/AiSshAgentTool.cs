using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MyAgent.Core.Attributes;
using MyAgent.Core.Interfaces;
using MyAgent.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Renci.SshNet;

namespace MyAgent.Skills.AI;

[SkillAction("ai.ssh_agent")]
public class AiSshAgentTool : IActionTool
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AiSshAgentTool> _logger;

    public string ActionType => "ai.ssh_agent";

    public AiSshAgentTool(IHttpClientFactory httpClientFactory, ILogger<AiSshAgentTool> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(SkillExecutionContext context, JToken parameters, CancellationToken cancellationToken)
    {
        string host = parameters["host"]?.ToString() ?? string.Empty;
        string username = parameters["username"]?.ToString() ?? string.Empty;
        string password = parameters["password"]?.ToString() ?? string.Empty;
        int port = int.TryParse(parameters["port"]?.ToString(), out int p) ? p : 22;
        string objective = parameters["objective"]?.ToString() ?? "请探索该机器";

        string model = context.EnvironmentArgs.TryGetValue("AiModelName", out var m) && !string.IsNullOrWhiteSpace(m?.ToString()) ? m.ToString()! : "qwen-plus";
        string apiKey = context.EnvironmentArgs.TryGetValue("AiApiKey", out var key) && !string.IsNullOrWhiteSpace(key?.ToString()) ? key.ToString()! : Environment.GetEnvironmentVariable("AI_API_KEY") ?? "";
        string baseUrl = context.EnvironmentArgs.TryGetValue("AiBaseUrl", out var url) && !string.IsNullOrWhiteSpace(url?.ToString()) ? url.ToString()! : "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions";

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(username))
            return ActionResult.Fail("SSH Agent missing 'host' or 'username'.");

        var connectionInfo = new ConnectionInfo(host, port, username, new PasswordAuthenticationMethod(username, password));

        using var client = new SshClient(connectionInfo);
        try
        {
            client.Connect();
            _logger.LogInformation("Connected to {Host}:{Port} as {User} for AI Agent Session.", host, port, username);
            
            // 重要：获取外部可能挂载的网页终端的读写钩子
            var ttyWriter = context.EnvironmentArgs.TryGetValue("VpsTerminalWriter", out var w) ? w as Action<string> : null;
            // var ttyReaderHost = context.EnvironmentArgs.TryGetValue("VpsTerminalReaderEvent", out var re) ? re : null; 
            // 提示：此处暂时省略用户直接敲键盘介入的互操作注册，后续可扩展。先保底实现 AI 闭环全托管。

            // 1. 开启一个带有 PTY 伪终端支持的长连接 ShellStream
            using var stream = client.CreateShellStream("xterm", 80, 24, 800, 600, 1024);
            
            StringBuilder sessionHistory = new StringBuilder(); // 让 AI 记住自己敲过什么
            StringBuilder screenBuffer = new StringBuilder();   // 屏幕当前的截面

            int maxLoops = 15; // 设置最多思考防环圈数
            int loopCount = 0;
            
            while (loopCount < maxLoops && !cancellationToken.IsCancellationRequested)
            {
                loopCount++;
                
                // 1. 智能等待：不断读取流推给前端，直至光标呈现出稳定的终端标志（标志着上一个指令执行完毕）
                bool isPromptReady = false;
                int waitCycle = 0;
                while (!isPromptReady && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, cancellationToken);
                    string incremental = stream.Read();
                    if (!string.IsNullOrEmpty(incremental))
                    {
                        screenBuffer.Append(incremental);
                        ttyWriter?.Invoke(incremental);
                    }

                    // 探索屏幕落底特征：是不是经典的光标提示符（忽略结尾的不可见空格退格）
                    string currentScreen = screenBuffer.ToString().TrimEnd(' ', '\r', '\n', '\t');
                    if (!string.IsNullOrEmpty(currentScreen))
                    {
                        char lastChar = currentScreen[currentScreen.Length - 1];
                        if (lastChar == '$' || lastChar == '#' || lastChar == '>')
                        {
                            // 探测到了疑似游标！为防止误判（比如正好打印了一句带#的执行日志），再静听500ms
                            await Task.Delay(500, cancellationToken);
                            string lateOutput = stream.Read();
                            if (!string.IsNullOrEmpty(lateOutput))
                            {
                                screenBuffer.Append(lateOutput);
                                ttyWriter?.Invoke(lateOutput);
                                // 有余波，说明是个长文本正在滚动输出里的符号，不是真正的输入等待
                                isPromptReady = false;
                            }
                            else
                            {
                                // 静听完毕且毫无动静，证明上游输出已经干涸，光标归位，可以唤醒 AI 大脑操作了！
                                isPromptReady = true;
                            }
                        }
                    }
                    
                    waitCycle++;
                    // 兜底泄流：如果死循环等待超过了 120 秒，有可能卡死在了 [y/N] 或者密码等待的特殊回显交互上被挂起。强行斩断进入研判！
                    if (waitCycle > 120) break;
                }

                _logger.LogInformation("--- Agent Loop {Loop} ---", loopCount);
                
                // 2. 构造对话发给大模型进行策略研判
                string prompt = $@"你是一个直接操作 Linux TTY (伪终端) 的超级 Agent。
你的终极目标是：{objective}

这是刚刚屏幕上刷新出的终端画面：
```text
{screenBuffer.ToString()}
```

请仔细阅读这段回显。
1. 若提示符在等待输入指令，请以 JSON 格式输出你接下来要向键盘敲入的**单一指令**。
2. 切记，你只是敲按键，不要在 JSON 外附加废话。指令末尾引擎会自动补回车。
3. 如果你发现已经彻底完成了《终极目标》，或者遇到了无法逾越的死档错误需要人类介入，请判断 command 为 'DONE'。

严格返回以下格式：
```json
{{
  ""thought"": ""这里写下你的思考：刚才那条命令成功了吗？我现在该敲什么命令推进目标？"",
  ""command"": ""apt update -y""
}}
```";
                var aiResult = await PerformAiReasoningAsync(prompt, model, apiKey, baseUrl, cancellationToken, ttyWriter);
                if (aiResult == null)
                {
                    ttyWriter?.Invoke("\r\n\x1b[1;31m[Agent Disconnected due to AI API Failure]\x1b[0m\r\n");
                    return ActionResult.Fail("AI API refused to answer or JSON parse failed.");
                }

                if (aiResult.Value.Command.Trim().Equals("DONE", StringComparison.OrdinalIgnoreCase))
                {
                    ttyWriter?.Invoke($"\x1b[1;32m{aiResult.Value.Command}\x1b[0m\r\n\r\n\x1b[1;32m[Agent Goal Achieved / Terminated]\x1b[0m\r\n");
                    break;
                }

                // 清空旧屏幕，准备接收新命令刷出的屏幕
                screenBuffer.Clear();
                
                // 提取纯净单行思考，供渲染前台标注日志
                string pureThoughtLine = $"# AI Thought: {aiResult.Value.Thought.Replace("\r", "").Replace("\n", " ")}";
                
                // 将大模型的决定推入 SSH 管道并敲下回车！
                // 仅抛出思考分析（自带变黄），不重复打印 Command（因为 SSH PTY 原生会自动回显 Command）
                ttyWriter?.Invoke($"\r\n\x1b[1;33m{pureThoughtLine}\x1b[0m\r\n");
                
                // 让终端真正的去执行：发送真实的命令
                sessionHistory.AppendLine($"[Command]: {aiResult.Value.Command}");
                stream.WriteLine(aiResult.Value.Command);
            }

            if (loopCount >= maxLoops && !cancellationToken.IsCancellationRequested)
            {
               ttyWriter?.Invoke("\r\n\x1b[1;31m[Agent Hit Maximum Loop Limit. Requesting Final Summary...]\x1b[0m\r\n");
               string summaryPrompt = $@"你是一个 Linux SSH 代理助手。当前达到最大循环次数 {maxLoops}。
你的终极目标是：{objective}
我们在刚刚的操作中执行了以下指令序列：
{sessionHistory}

请基于屏幕情况总结一下你做了什么、在哪条命令卡住了（或者为什么无法达成目标）。
严格返回以下格式：
```json
{{
  ""thought"": ""你的全中文总结..."",
  ""command"": ""DONE""
}}
```";
               var summaryResult = await PerformAiReasoningAsync(summaryPrompt, model, apiKey, baseUrl, cancellationToken, ttyWriter);
               string summary = summaryResult?.Thought ?? "无法生成总结报告。";
               ttyWriter?.Invoke($"\r\n\x1b[1;31m[Task Incomplete Summary]: {summary}\x1b[0m\r\n");
               
               context.StateBag["analysis_text"] = summary;
               client.Disconnect();
               return ActionResult.Fail($"Maximum loops reached. Final Summary: {summary}");
            }
            
            client.Disconnect();
            context.StateBag["analysis_text"] = "操作已成功结束，所有预期指令已投递。";
            return ActionResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AiSshAgentTool Execution Failed");
            return ActionResult.Fail(ex.Message);
        }
    }

    private async Task<(string Thought, string Command)?> PerformAiReasoningAsync(string prompt, string model, string apiKey, string baseUrl, CancellationToken token, Action<string>? ttyWriter)
    {
        try
        {
            var requestBody = new
            {
                model = model,
                messages = new[] { new { role = "user", content = prompt } }
            };

            var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
            var client = _httpClientFactory.CreateClient("AiModelsClient");
            using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.Content = content;

            var response = await client.SendAsync(request, token);
            var responseString = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode)
            {
                ttyWriter?.Invoke($"\r\n\x1b[1;31m[AI Network Error {response.StatusCode}]: {responseString}\x1b[0m\r\n");
                return null;
            }

            var jsonResult = JObject.Parse(responseString);
            var replyMessage = jsonResult["choices"]?[0]?["message"]?["content"]?.ToString();

            if (string.IsNullOrEmpty(replyMessage))
            {
                ttyWriter?.Invoke($"\r\n\x1b[1;31m[AI Error]: Empty content returned from model.\x1b[0m\r\n");
                return null;
            }

            // 抽离 ```json 包裹
            var match = Regex.Match(replyMessage, @"```(?:json)?\s*(.*?)\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            string cleanJson = match.Success ? match.Groups[1].Value.Trim() : replyMessage.Trim();
            
            // 兜底大模型偷懒没有写花括号的极端情况
            var jsonStartIndex = cleanJson.IndexOf('{');
            var jsonEndIndex = cleanJson.LastIndexOf('}');
            if (jsonStartIndex >= 0 && jsonEndIndex > jsonStartIndex)
            {
                cleanJson = cleanJson.Substring(jsonStartIndex, jsonEndIndex - jsonStartIndex + 1);
                var parsed = JObject.Parse(cleanJson);
                return (parsed["thought"]?.ToString() ?? "", parsed["command"]?.ToString() ?? "");
            }
            else
            {
                ttyWriter?.Invoke($"\r\n\x1b[1;31m[AI Parser Error]: Unable to extract JSON from reply: {replyMessage}\x1b[0m\r\n");
            }
            return null;
        }
        catch (Exception ex)
        {
            ttyWriter?.Invoke($"\r\n\x1b[1;31m[AI Exception]: {ex.Message}\x1b[0m\r\n");
            return null;
        }
    }
}
