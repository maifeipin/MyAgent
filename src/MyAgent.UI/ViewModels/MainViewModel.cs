using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Markdig;
using Microsoft.Extensions.Logging;
using MyAgent.Core.Config;
using MyAgent.Core.Engine;
using MyAgent.Core.Models;
using Newtonsoft.Json.Linq;

namespace MyAgent.UI.ViewModels;

public class AiProfile
{
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string ModelName { get; set; } = "";
    public string ApiKey { get; set; } = "";
}

public partial class MainViewModel : ObservableObject
{
    private readonly ISkillConfigReader _configReader;
    private readonly SkillEngine _engine;
    private readonly ILogger<MainViewModel> _logger;

    public ObservableCollection<SkillDefinition> Skills { get; } = new();

    [ObservableProperty]
    private SkillDefinition? _selectedSkill;

    // 当用户手动切换技能时，立即触发保存
    partial void OnSelectedSkillChanged(SkillDefinition? value)
    {
        if (value != null)
        {
            SaveConfig();
        }
    }

    // AI Configuration
    [ObservableProperty]
    private string _aiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";

    [ObservableProperty]
    private string _aiModelName = "gemini-2.5-flash";

    [ObservableProperty]
    private string _aiApiKey = "";

    [ObservableProperty]
    private string _webhookUrl = "";

    [ObservableProperty]
    private bool _enableWebhookPush = true;

    // Proxy Settings
    [ObservableProperty]
    private bool _enableGlobalProxy = false;

    [ObservableProperty]
    private string _globalProxyAddress = "socks5://127.0.0.1:1080";

    // AI Profiles PRE-SET
    public ObservableCollection<AiProfile> AiProfiles { get; } = new();

    [ObservableProperty]
    private AiProfile? _selectedAiProfile;

    partial void OnSelectedAiProfileChanged(AiProfile? value)
    {
        if (value != null)
        {
            AiBaseUrl = value.BaseUrl;
            AiModelName = value.ModelName;
            AiApiKey = value.ApiKey;
        }
    }

    partial void OnAiBaseUrlChanged(string value) => SyncToSelectedProfile();
    partial void OnAiModelNameChanged(string value) => SyncToSelectedProfile();
    partial void OnAiApiKeyChanged(string value) => SyncToSelectedProfile();

    private void SyncToSelectedProfile()
    {
        if (SelectedAiProfile != null)
        {
            SelectedAiProfile.BaseUrl = AiBaseUrl;
            SelectedAiProfile.ModelName = AiModelName;
            SelectedAiProfile.ApiKey = AiApiKey;
        }
    }

    // UI View State
    [ObservableProperty]
    private int _rightTabIndex = 0;

    [ObservableProperty]
    private string _browserStatusText = "[就绪] 浏览器内核已闲置并等待 AI 指令";

    public event Action<string>? OnMarkdownReportReady;

    // Execution Context Variables
    [ObservableProperty]
    private string _paramTargetUrl = "https://github.com/trending";

    [ObservableProperty]
    private string _paramPrompt = "请总结当前网页的内容";

    [ObservableProperty]
    private string _executionLogText = "Ready.\n";

    // VPS Console States
    [ObservableProperty]
    private string _paramVpsHost = "";

    [ObservableProperty]
    private string _paramVpsPort = "22";

    [ObservableProperty]
    private string _paramVpsUsername = "root";

    [ObservableProperty]
    private string _paramVpsPassword = "";

    [ObservableProperty]
    private string _vpsLogText = "VPS 终端已就绪 (凭证仅存在本机内存，绝不上传到云端大模型，也绝不保存到硬盘)...\n";

    private CancellationTokenSource? _executionCts;

    // ---------------------------------------------------------------------
    // WebSSH 前台真实 TTY 交互通道挂载 (供 MainWindow 取用并供底盘 AiSshAgentTool 注入)
    // ---------------------------------------------------------------------
    public Action<string>? WriteToVpsTerminalAction { get; private set; }
    public event Action<string>? OnVpsTerminalInputEvent;

    public void RegisterVpsTerminal(MyAgent.UI.MainWindow window)
    {
        // 挂载写方法：允许后续引擎内的组件直接调用 ViewModel 的委托向 MainWindow 的 Xterm 抛字
        WriteToVpsTerminalAction = window.WriteToVpsTerminal;

        // 挂载读方法：订阅 Xterm 用户键盘敲击的内容，用于底层 ShellStream 拉取反馈给服务器
        window.OnVpsTerminalInput += (data) => 
        {
            OnVpsTerminalInputEvent?.Invoke(data);
        };
    }
    // ---------------------------------------------------------------------

    public MainViewModel(ISkillConfigReader configReader, SkillEngine engine, ILogger<MainViewModel> logger)
    {
        _configReader = configReader;
        _engine = engine;
        _logger = logger;
        
        _engine.OnProgressLog += LogMessage;

        InitializeAiProfiles();
        LoadConfig();
    }

    private void InitializeAiProfiles()
    {
        if (AiProfiles.Count > 0) return; // Already loaded from config

        AiProfiles.Add(new AiProfile
        {
            Name = "[推荐] 阿里云 DashScope (Qwen-Plus)",
            BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions",
            ModelName = "qwen-plus",
            ApiKey = ""
        });

        AiProfiles.Add(new AiProfile
        {
            Name = "[推荐] Google Gemini (2.5-Flash)",
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
            ModelName = "gemini-2.5-flash",
            ApiKey = ""
        });
    }

    private string GetConfigFilePath() => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user_config.json");

    private void LoadConfig()
    {
        try
        {
            var path = GetConfigFilePath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var config = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, string>>(json);
                if (config != null)
                {
                    if (config.TryGetValue("AiBaseUrl", out var url)) AiBaseUrl = url;
                    if (config.TryGetValue("AiModelName", out var model)) AiModelName = model;
                    if (config.TryGetValue("AiApiKey", out var key)) AiApiKey = key;
                    if (config.TryGetValue("WebhookUrl", out var hook)) WebhookUrl = hook;
                    
                    if (config.TryGetValue("EnableWebhookPush", out var enablePushStr) && bool.TryParse(enablePushStr, out var enablePush))
                    {
                        EnableWebhookPush = enablePush;
                    }

                    if (config.TryGetValue("ParamTargetUrl", out var target)) ParamTargetUrl = target;
                    if (config.TryGetValue("ParamPrompt", out var prompt)) ParamPrompt = prompt;
                    
                    if (config.TryGetValue("EnableGlobalProxy", out var proxyEnabled) && bool.TryParse(proxyEnabled, out var enableProxy))
                    {
                        EnableGlobalProxy = enableProxy;
                    }
                    if (config.TryGetValue("GlobalProxyAddress", out var proxyAddr)) GlobalProxyAddress = proxyAddr;

                    if (config.TryGetValue("LastSelectedSkillId", out var skillId))
                    {
                        _lastSelectedSkillId = skillId;
                    }

                    // Load full profile list if available
                    if (config.TryGetValue("AiProfilesJson", out var profilesJson))
                    {
                        var loadedProfiles = Newtonsoft.Json.JsonConvert.DeserializeObject<ObservableCollection<AiProfile>>(profilesJson);
                        if (loadedProfiles != null)
                        {
                            AiProfiles.Clear();
                            foreach (var p in loadedProfiles) AiProfiles.Add(p);
                        }
                    }
                }
            }
        }
        catch { /* ignore */ }
    }

    private string? _lastSelectedSkillId;

    public void SaveConfig()
    {
        try
        {
            var config = new System.Collections.Generic.Dictionary<string, string>
            {
                ["AiBaseUrl"] = AiBaseUrl,
                ["AiModelName"] = AiModelName,
                ["AiApiKey"] = AiApiKey,
                ["WebhookUrl"] = WebhookUrl,
                ["EnableWebhookPush"] = EnableWebhookPush.ToString(),
                ["EnableGlobalProxy"] = EnableGlobalProxy.ToString(),
                ["GlobalProxyAddress"] = GlobalProxyAddress,
                ["ParamTargetUrl"] = ParamTargetUrl,
                ["ParamPrompt"] = ParamPrompt,
                ["LastSelectedSkillId"] = SelectedSkill?.SkillId ?? "",
                ["AiProfilesJson"] = Newtonsoft.Json.JsonConvert.SerializeObject(AiProfiles)
            };
            File.WriteAllText(GetConfigFilePath(), Newtonsoft.Json.JsonConvert.SerializeObject(config, Newtonsoft.Json.Formatting.Indented));
            
            ApplyGlobalProxySettings();
        }
        catch { /* ignore */ }
    }

    private void ApplyGlobalProxySettings()
    {
        try
        {
            if (EnableGlobalProxy && !string.IsNullOrWhiteSpace(GlobalProxyAddress))
            {
                var proxy = new System.Net.WebProxy(GlobalProxyAddress)
                {
                    BypassProxyOnLocal = true
                };
                System.Net.WebRequest.DefaultWebProxy = proxy;
                System.Net.Http.HttpClient.DefaultProxy = proxy;
            }
            else
            {
                System.Net.WebRequest.DefaultWebProxy = System.Net.WebRequest.GetSystemWebProxy();
                System.Net.Http.HttpClient.DefaultProxy = System.Net.WebRequest.GetSystemWebProxy();
            }
        }
        catch 
        {
            LogMessage("⚠️ 全局网络代理规则注入系统失败。请检查地址格式 (支持 http:// 或 socks5://)。");
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var settingsWin = new SettingsWindow
        {
            Owner = Application.Current.MainWindow,
            DataContext = this
        };
        settingsWin.ShowDialog();
    }

    [RelayCommand]
    private async Task LoadSkillsAsync()
    {
        Skills.Clear();
        string configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "skills");
        
        // Ensure default skill is generated if missing
        EnsureDefaultSkillCreated(configDir);

        var loadedSkills = await _configReader.LoadAllSkillsAsync(configDir);
        foreach (var skill in loadedSkills)
        {
            Skills.Add(skill);
            // 恢复上一次选择的技能
            if (!string.IsNullOrEmpty(_lastSelectedSkillId) && skill.SkillId == _lastSelectedSkillId)
            {
                SelectedSkill = skill;
            }
        }

        if (SelectedSkill == null && Skills.Count > 0)
        {
            SelectedSkill = Skills[0];
        }

        LogMessage($"Loaded {Skills.Count} skills.");
    }

    [RelayCommand]
    private async Task TestVpsConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(ParamVpsHost) || string.IsNullOrWhiteSpace(ParamVpsUsername) || string.IsNullOrWhiteSpace(ParamVpsPassword))
        {
            MessageBox.Show("请先填写完整的服务器 IP、账户与密码！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LogMessage($"\n--- 探针测试: 正在尝试连接 {ParamVpsUsername}@{ParamVpsHost}:{ParamVpsPort} ---");

        try
        {
            int port = int.TryParse(ParamVpsPort, out int p) ? p : 22;
            var connectionInfo = new Renci.SshNet.ConnectionInfo(ParamVpsHost, port, ParamVpsUsername, new Renci.SshNet.PasswordAuthenticationMethod(ParamVpsUsername, ParamVpsPassword));
            connectionInfo.Timeout = TimeSpan.FromSeconds(10); // 10秒超时

            await Task.Run(() =>
            {
                using (var client = new Renci.SshNet.SshClient(connectionInfo))
                {
                    client.Connect();
                    if (client.IsConnected)
                    {
                        var result = client.RunCommand("uname -a");
                        LogMessage($"[SSH 探针] 成功穿透! 目标机器内核信息: {result.Result}");
                    }
                    client.Disconnect();
                }
            });
        }
        catch (Exception ex)
        {
            LogMessage($"[SSH 探针] 连接失败验证未通过，错误原因: {ex.Message}");
            MessageBox.Show($"连接失败:\n{ex.Message}", "连接报错", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ExecuteSkillAsync()
    {
        if (SelectedSkill == null)
        {
            MessageBox.Show("请先在左侧选择一个技能！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _executionCts?.Cancel();
        _executionCts = new CancellationTokenSource();

        LogMessage($"\n--- Starting Execution: {SelectedSkill.Name} ---");

        // Determine final AI settings
        string finalApiKey = AiApiKey;
        string finalBaseUrl = AiBaseUrl;
        if (SelectedAiProfile != null)
        {
            finalApiKey = SelectedAiProfile.ApiKey ?? string.Empty;
            finalBaseUrl = SelectedAiProfile.BaseUrl ?? string.Empty;
        }

        var envArgs = new System.Collections.Generic.Dictionary<string, object>
        {
            { "AiApiKey", finalApiKey ?? string.Empty },
            { "AiModelName", SelectedAiProfile?.ModelName ?? string.Empty },
            { "AiBaseUrl", finalBaseUrl ?? string.Empty },
            { "VpsHost", ParamVpsHost ?? string.Empty },
            { "VpsPort", ParamVpsPort ?? string.Empty },
            { "VpsUsername", ParamVpsUsername ?? string.Empty },
            { "VpsPassword", ParamVpsPassword ?? string.Empty },
            { "EnableWebhookPush", EnableWebhookPush.ToString() },
            { "TargetUrl", ParamTargetUrl ?? string.Empty },
            { "UserPrompt", ParamPrompt ?? string.Empty },
            
            // 【Phase 18 新增】将大模型自身的主体配置作为环境变量暴露出给模型模板！
            { "CurrentAiModel", AiModelName ?? string.Empty },
            { "CurrentAiBaseUrl", AiBaseUrl ?? string.Empty },
            { "GlobalProxyEnabled", EnableGlobalProxy.ToString() },
            { "GlobalProxyAddress", GlobalProxyAddress ?? string.Empty },

            // 【Phase 18 新增】植入一个赋予 AI 修改本体的强悍神阶特权闭包
            { "AgentConfigUpdater", new Action<JToken>(payload => 
                {
                    Application.Current.Dispatcher.Invoke(() => 
                    {
                        if (payload["EnableGlobalProxy"] != null)
                            EnableGlobalProxy = payload.Value<bool>("EnableGlobalProxy");
                        if (payload["GlobalProxyAddress"] != null)
                            GlobalProxyAddress = payload.Value<string>("GlobalProxyAddress");
                        if (payload["AiModelName"] != null)
                            AiModelName = payload.Value<string>("AiModelName");
                        if (payload["AiBaseUrl"] != null)
                            AiBaseUrl = payload.Value<string>("AiBaseUrl");
                        if (payload["WebhookUrl"] != null)
                            WebhookUrl = payload.Value<string>("WebhookUrl");
                        if (payload["AiApiKey"] != null)
                            AiApiKey = payload.Value<string>("AiApiKey");
                        
                        SaveConfig(); // 修改落盘定档
                        LogMessage("⚡ [Self-Evolution] MyAgent 核心引擎成功使用回调覆写并重刷了系统配置！");
                    });
                }) 
            }
        };

        // 如果当前 UI 成功挂载了 Xterm 的输入输出钩子，则将这两个委托注入给底层运行时，允许神盾级的工具强行接管
        if (WriteToVpsTerminalAction != null)
        {
            envArgs["VpsTerminalWriter"] = WriteToVpsTerminalAction;
            envArgs["VpsTerminalReaderEvent"] = this; // 通过这层拿到订阅句柄
        }

        SaveConfig();

        try
        {
            // Execute inside a background task to prevent UI freeze
            var result = await Task.Run(async () =>
            {
                return await _engine.ExecuteSkillAsync(SelectedSkill, 0, envArgs, _executionCts.Token);
            });

            if (result.IsSuccess)
            {
                LogMessage("Execution Completed Successfully.");
                if (result.OutputData.TryGetValue("analysis_text", out var textResult) && textResult != null)
                {
                    LogMessage($"\n[AI Analysis Result]\n{textResult}");
                    
                    // Convert Markdown to HTML for the Report Tab
                    var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
                    var htmlBody = Markdown.ToHtml(textResult.ToString() ?? "", pipeline);
                    var fullHtml = $@"
                        <html>
                        <head>
                            <style>
                                body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; padding: 20px; line-height: 1.6; color: #333; }}
                                h1, h2, h3 {{ color: #2c3e50; border-bottom: 1px solid #eee; padding-bottom: 10px; }}
                                pre {{ background: #f8f9fa; padding: 15px; border-radius: 5px; overflow-x: auto; }}
                                code {{ font-family: Consolas, monospace; background: #f1f3f5; padding: 2px 5px; border-radius: 3px; }}
                                blockquote {{ border-left: 4px solid #007bff; margin: 0; padding-left: 15px; color: #6c757d; }}
                            </style>
                        </head>
                        <body>{htmlBody}</body>
                        </html>";

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        RightTabIndex = 1; // 自动翻转到富文本标签页
                        OnMarkdownReportReady?.Invoke(fullHtml);
                    });
                }
            }
            else
            {
                LogMessage($"Execution Failed: {result.ErrorMessage}");
            }
        }
        catch (OperationCanceledException)
        {
            LogMessage("Execution Cancelled.");
        }
        catch (Exception ex)
        {
            LogMessage($"Exception: {ex.Message}");
        }
    }

    public void LogMessage(string message)
    {
        // Simple log appending
        Application.Current.Dispatcher.Invoke(() =>
        {
            ExecutionLogText += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
            
            if (message.Contains("[SSH]") || message.Contains("[SFTP]"))
            {
                VpsLogText += message + "\n";
            }
        });
    }

    private void EnsureDefaultSkillCreated(string configDir)
    {
        if (!Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }

        var webReaderFile = Path.Combine(configDir, "web_read_ai.yaml");
        if (!File.Exists(webReaderFile))
        {
            string yaml1 = @"
schema_version: '1.1'
skill_id: 'sample.web_reader'
name: '网页浏览与 AI 洞察'
description: '打开目标网页，读取 DOM 内容后反馈给指定的 AI 模型。'
trigger:
  type: 'manual'
workflow:
  - step_id: 'ai_intent_parse'
    name: 'AI URL 意图推理'
    action: 'ai.analyze'
    params:
      prompt: >
        请分析用户的意图，决定我们需要访问哪个目标网址（URL）。
        用户在输入框预设的网址参数: ""{{StateBag.TargetUrl}}""
        用户的自然语言要求: ""{{StateBag.UserPrompt}}""

        如果用户的话中明确提到了想访问某种确定的网站（例如“打开百度”、“查一下 IP 的网站”、“进入新浪”），请你直接帮他推理出一个最合适的确切 URL（例如 https://www.baidu.com 或 https://www.ip138.com）。
        如果用户没有特殊要求或者只提供了一个合法的域名，就尽量原样使用输入框的预设网址参数，但要确保它带有 http(s):// 前缀。

        强制返回 JSON 格式：
        ```json
        {
          ""target_url"": ""你推断出的最终URL地址""
        }
        ```
    timeout_ms: 30000

  - step_id: 'nav_01'
    name: '导航到网页'
    action: 'browser.navigate'
    params:
      url: '{{StateBag.analysis_json.target_url}}'
    timeout_ms: 60000

  - step_id: 'extract_01'
    name: '提取正文'
    action: 'perception.dom'
    params:
      selector: 'body'
      extract_type: 'text'
    timeout_ms: 5000

  - step_id: 'ai_analyze_01'
    name: 'AI总结'
    action: 'ai.analyze'
    params:
      prompt: '{{StateBag.UserPrompt}}。\n\n以下是网页内容:\n{{StateBag.text}}'
    timeout_ms: 120000
";
            File.WriteAllText(webReaderFile, yaml1.Trim());
        }

        var diagnoseFile = Path.Combine(configDir, "system_diagnosis.yaml");
        if (!File.Exists(diagnoseFile))
        {
            string yaml2 = @"
schema_version: '1.1'
skill_id: 'system.diagnose'
name: 'Agent 系统环境自我诊断'
description: '将当前用户大模型配置和执行日志等交由 AI 提供专业的排错分析和建议。'
trigger:
  type: 'manual'
workflow:
  - step_id: 'ai_analyze_diagnose'
    name: 'AI 深度检测分析'
    action: 'ai.analyze'
    params:
      prompt: |
        【系统级指令】你现在是 MyAgent 自动化桌面级系统的一名资深支持专家架构师。
        用户正在抱怨他的自动化流程或者环境遇到了一些问题。请仔细分析下面的用户环境状态变量和用户的描述，给出结构化、高可用的诊断报告与解决方案：

        ### [当前环境配置]
        1. BaseUrl 网关：{{StateBag.AiBaseUrl}}
        2. Model 模型名称：{{StateBag.AiModelName}}
        3. 目标执行地址：{{StateBag.TargetUrl}}
        4. API 密钥凭证填写长度：{{StateBag.AiApiKey}} (请通过检查变量长度及有无帮助用户判断是否漏填，不要直接打印出内容以免泄露)

        ### [用户输入或近期报错日志]
        {{StateBag.UserPrompt}}

        ### [操作建议]
        请分析：
        1. 【环境探针】：大模型请求网关和模型名字的书写是否正常（例如 Gemini 需要配合 generativelanguage，DashScope 需要配合 aliyuncs 等，给出警告如果出现不匹配）。
        2. 【核心分析】：结合上方的环境和用户的报错日志，分析出引发错误的最本质根源（例如，Token是否欠费？URL是不是无法连通？页面反爬风控太强？或者是没有合法密钥引发 HTTP 401）。
        3. 【操作建议】：给用户提供 1-3 步直接可以上手的傻瓜式后续操作。请注意语气亲和、专业。
    timeout_ms: 120000

  - step_id: 'notify_markdown_webhook'
    name: '企微/飞书 Webhook 广播'
    action: 'notify.webhook'
    params:
      enabled: '{{StateBag.EnableWebhookPush}}'
      url: '{{StateBag.WebhookUrl}}'
      payload: |
        {
           ""msgtype"": ""markdown"",
           ""markdown"": {
               ""content"": {{StateBag.analysis_text|json}}
           }
        }
    timeout_ms: 8000
";
            File.WriteAllText(diagnoseFile, yaml2.Trim());
        }

        var evolveFile = Path.Combine(configDir, "agent_evolution.yaml");
        if (!File.Exists(evolveFile))
        {
            string yaml3 = @"
schema_version: '1.1'
skill_id: 'system.evolve'
name: 'Agent 灵魂进化与自适应配置'
description: '允许 AI 通过提示词理解您的需求，自动编写并注入新的技能结构 (YAML) 或修改系统配置，实现自我进化。'
trigger:
  type: 'manual'
workflow:
  - step_id: 'ai_generate_config'
    name: 'AI 极客思维与代码生成'
    action: 'ai.analyze'
    params:
      prompt: |
        【系统级指令】你现在是 MyAgent 的元级核心架构师 (Meta-Architect)。
        你拥有对系统配置文件和技能库 (`config/skills/*.yaml`) 的直接写入权限。
        
        用户的诉求是：
        {{StateBag.UserPrompt}}
        
        请你按下述要求分两部分回答：
        
        第一部分：【配置与使用指南】（Markdown 排版）
        针对用户刚才提出的需求（例如开通飞书 webhook 提醒或者添加钉钉），使用友好的大白话，结合 Markdown 的无序列表、加粗等，一步步告诉用户应该怎么去官方平台申请、怎么拿到 Key 或 URL，以及后续如何在 UI 面板里配置它们。

        第二部分：【底层注入载荷】
        在指南写完后，请务必在一对带有格式的 ```json 代码块中，输出实际需要系统帮用户建立的物理文件数据。
        JSON 结构严格为：
        {
          ""file_path"": ""你要写入的相对文件路径，例如 config/skills/my_new_skill.yaml 或者 user_config.json"",
          ""file_content"": ""你要写入的完整文件内容文本（例如 YAML 内容参数体，一定要严格符合项目当前 YAML `schema_version: '1.1'` 格式规约）""
        }
    timeout_ms: 120000

  - step_id: 'apply_evolution'
    name: '向宿主注入新的配置'
    action: 'os.file_write'
    params:
      path: '{{StateBag.analysis_json.file_path}}'
      content: '{{StateBag.analysis_json.file_content}}'
    timeout_ms: 5000

  - step_id: 'notify_markdown_webhook'
    name: '企微/飞书 Webhook 广播'
    action: 'notify.webhook'
    params:
      enabled: '{{StateBag.EnableWebhookPush}}'
      url: '{{StateBag.WebhookUrl}}'
      payload: |
        {
           ""msgtype"": ""markdown"",
           ""markdown"": {
               ""content"": ""🔥 **灵魂进化完成**：已成功注入并加载新的技能配置文件：`{{StateBag.analysis_json.file_path}}`""
           }
        }
    timeout_ms: 8000
";
            File.WriteAllText(evolveFile, yaml3.Trim());
        }

        var devopsFile = Path.Combine(configDir, "agent_devops.yaml");
        if (!File.Exists(devopsFile))
        {
            string yaml4 = @"
schema_version: '1.1'
skill_id: 'system.devops'
name: '云端 VPS 全自动运维专家'
description: '向云端服务器发起 SSH 连接，并让大模型分析需求为您编写和物理执行自动化架构/环境部署脚本。'
trigger:
  type: 'manual'
workflow:
  - step_id: 'ai_generate_sh'
    name: 'AI 分析并架构部署蓝图'
    action: 'ai.analyze'
    params:
      prompt: |
        【重要系统级指令】你是一名顶级 Linux 架构师与系统极客。
        【执行上下文环境】你当前的内核被集成在一个拥有 SSH.NET 原生支持的桌面 Agent 系统内。在此次运行中，你并非只是在纸上谈兵！当你以特定的 JSON 格式返回 Bash 指令栈后，底层的执行器会自动为你接管目标服务器，静默执行你的指令，并在后台自动扫描捕获全部的 stdout 与 stderr 数据流。
        
        下面是远端用户的业务场景诉求：
        === 目标诉求 ===
        {{StateBag.UserPrompt}}
        === 诉求结束 ===
        
        请仔细分析，并将你要在宿主机上操作的动作以 JSON 格式原样输出至 ```json ... ``` 代码块中，供后台执行引擎接管。
        你的响应数据结构必须且仅限形如以下形式：
        {
          ""commands"": [
             ""apt-get update -y"",
             ""# 这里是你真正为用户量身定制的 Bash 指令集合！切记：所有后续指令请务必使用静默和免交互安全安装参数（如 -y），绝对禁止触发诸如 nano、vim 或者需要 stdin 等待的阻塞式操作，否则会卡死执行器！""
          ],
          ""file_writes"": [
             {
               ""path"": ""/root/my_script.js"",
               ""content"": ""// 包含要直接推送到目标物理机上的核心业务文件代码内容""
             }
          ]
        }
        
        最后一次强调：这里生成的载荷会即刻被真实物理机执行，不要输出诸如“这只是个例子”、“请把上面的路径替换”之类的占位符或者过度谨慎的人设废话。务必一次性生成完整可用的部署或排错干货代码！
    timeout_ms: 60000

  - step_id: 'ssh_execute'
    name: '与目标服务器物理连接并传发载荷'
    action: 'os.ssh_execute'
    params:
      host: '{{StateBag.VpsHost}}'
      port: '{{StateBag.VpsPort}}'
      username: '{{StateBag.VpsUsername}}'
      password: '{{StateBag.VpsPassword}}'
      commands: '{{StateBag.analysis_json.commands}}'
      file_writes: '{{StateBag.analysis_json.file_writes}}'
    timeout_ms: 120000
";
            File.WriteAllText(devopsFile, yaml4.Trim());
        }
    }
}
