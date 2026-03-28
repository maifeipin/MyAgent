using System;
using System.Windows;
using System.Threading.Tasks;
using System.ComponentModel;
using Microsoft.Web.WebView2.Core;
using MyAgent.UI.ViewModels;

namespace MyAgent.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow
        {
            Owner = this,
            DataContext = this.DataContext // Shared context
        };
        settingsWindow.ShowDialog();
    }

    private void OpenSkillManager_Click(object sender, RoutedEventArgs e)
    {
        var vm = DataContext as MyAgent.UI.ViewModels.MainViewModel;
        if (vm == null) return;

        var window = new SkillManagerWindow
        {
            Owner = this,
            DataContext = new MyAgent.UI.ViewModels.SkillManagerViewModel(vm)
        };
        window.ShowDialog();
    }

    private void VpsPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is System.Windows.Controls.PasswordBox pb)
        {
            vm.ParamVpsPassword = pb.Password;
        }
    }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Initialize WebView2 environment as soon as the window is created
        InitializeWebViewAsync();

        // Load configured skills
        Loaded += (s, e) =>
        {
            _viewModel.LoadSkillsCommand.Execute(null);
        };

        // Subscribe to rich text report events
        _viewModel.OnMarkdownReportReady += (html) =>
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await MarkdownReportWebView.EnsureCoreWebView2Async();
                MarkdownReportWebView.NavigateToString(html);
            });
        };
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.UseDarkWebMode))
        {
            _ = Dispatcher.InvokeAsync(async () => await ApplyWebThemeAsync());
        }
    }

    private async Task ApplyWebThemeAsync()
    {
        var scheme = _viewModel.UseDarkWebMode
            ? CoreWebView2PreferredColorScheme.Dark
            : CoreWebView2PreferredColorScheme.Light;

        if (MainWebView.CoreWebView2 != null)
        {
            MainWebView.CoreWebView2.Profile.PreferredColorScheme = scheme;
        }

        if (MarkdownReportWebView.CoreWebView2 != null)
        {
            MarkdownReportWebView.CoreWebView2.Profile.PreferredColorScheme = scheme;

            var script = _viewModel.UseDarkWebMode
                ? @"(function(){
                    document.documentElement.style.colorScheme = 'dark';
                    const styleId='myagent-report-theme';
                    let style=document.getElementById(styleId);
                    if(!style){style=document.createElement('style');style.id=styleId;document.head.appendChild(style);} 
                    style.textContent = `
                        body { background:#121212 !important; color:#E8E8E8 !important; }
                        h1,h2,h3 { color:#FFFFFF !important; border-bottom-color:#333 !important; }
                        pre { background:#1E1E1E !important; color:#E8E8E8 !important; }
                        code { background:#2A2A2A !important; color:#E8E8E8 !important; }
                        blockquote { color:#BDBDBD !important; border-left-color:#5DA9FF !important; }
                        a { color:#7CB7FF !important; }
                    `;
                })();"
                : @"(function(){
                    document.documentElement.style.colorScheme = 'light';
                    const styleId='myagent-report-theme';
                    let style=document.getElementById(styleId);
                    if(!style){style=document.createElement('style');style.id=styleId;document.head.appendChild(style);} 
                    style.textContent = `
                        body { background:#FFFFFF !important; color:#333333 !important; }
                        h1,h2,h3 { color:#2c3e50 !important; border-bottom-color:#eee !important; }
                        pre { background:#f8f9fa !important; color:#333333 !important; }
                        code { background:#f1f3f5 !important; color:#333333 !important; }
                        blockquote { color:#6c757d !important; border-left-color:#007bff !important; }
                        a { color:#0d6efd !important; }
                    `;
                })();";

            try
            {
                await MarkdownReportWebView.ExecuteScriptAsync(script);
            }
            catch
            {
            }
        }
    }

    private async void InitializeWebViewAsync()
    {
        try
        {
            // Configure user data folder to local app data so it retains cookies/logins
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var userDataFolder = System.IO.Path.Combine(appData, "MyAgent", "WebView2Data");
            
            CoreWebView2EnvironmentOptions options = new CoreWebView2EnvironmentOptions();
            
            // Apply Proxy if Enabled
            if (_viewModel.EnableGlobalProxy && !string.IsNullOrWhiteSpace(_viewModel.GlobalProxyAddress))
            {
                // 注意：由于是作为 COM 组件传参，绝不能画蛇添足加上双引号，否则 Chromium 会去解析一个带引号的乱码地址导致卡死
                options.AdditionalBrowserArguments = $"--proxy-server={_viewModel.GlobalProxyAddress} --proxy-bypass-list=<-loopback>";
                _viewModel.LogMessage($"[Proxy Gateway] MainWindow UI WebView2 Subsystem will be initialized under Proxy: {_viewModel.GlobalProxyAddress}");
            }
            else
            {
                // 如果全局开关被关掉，为了防止残留于 userDataFolder 的配置复活，我们显式传入无代理模式参数
                options.AdditionalBrowserArguments = "--no-proxy-server";
            }

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
            await MainWebView.EnsureCoreWebView2Async(env);
            await MarkdownReportWebView.EnsureCoreWebView2Async(env);

            MarkdownReportWebView.NavigationCompleted += async (s, e) =>
            {
                if (e.IsSuccess)
                {
                    await ApplyWebThemeAsync();
                }
            };
            
            // 绑定生命周期系统钩子，交由 ViewModel 提供状态栏回显
            MainWebView.NavigationStarting += (s, e) => 
            {
                _viewModel.BrowserStatusText = $"[加载中] 正在寻址并前往: {e.Uri}";
            };
            MainWebView.ContentLoading += (s, e) =>
            {
                _viewModel.BrowserStatusText = $"[渲染中] 正在解析 DOM 节点树与阻塞资源...";
            };
            MainWebView.SourceChanged += (s, e) =>
            {
                if (MainWebView.Source != null)
                    _viewModel.BrowserStatusText = $"[浏览中] 当前动态路由到达: {MainWebView.Source}";
            };
            MainWebView.NavigationCompleted += (s, e) => 
            {
                if (e.IsSuccess)
                    _viewModel.BrowserStatusText = $"[加载完成] " + (MainWebView.Source?.ToString() ?? "页面已就绪");
                else
                    _viewModel.BrowserStatusText = $"[被阻断] 加载失败，驱动程序报告 HTTP 异常栈: {e.WebErrorStatus}";
            };
            
            // 初始化专门为 WebSSH 准备的附属终端黑框
            await VpsTerminalWebView.EnsureCoreWebView2Async(env);
            string terminalHtmlPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Resources", "ssh_terminal.html");
            if (System.IO.File.Exists(terminalHtmlPath))
            {
                VpsTerminalWebView.Source = new System.Uri(terminalHtmlPath);
                
                // 将 MainWindow 实例传给 ViewModel 中的 VpsTerminalProxy，允许它双向代理
                _viewModel.RegisterVpsTerminal(this);
            }
            
            await ApplyWebThemeAsync();
            _viewModel.LogMessage("WebView2 subsystem initialized.");
        }
        catch (Exception ex)
        {
            _viewModel.LogMessage($"WebView2 init error: {ex.Message}");
        }
    }

    /// <summary>
    /// 后端 C# 向前端 Xterm.js 控制台下发字符（如真实的终端返回日志）
    /// </summary>
    public void WriteToVpsTerminal(string data)
    {
        Dispatcher.Invoke(() =>
        {
            if (VpsTerminalWebView.CoreWebView2 != null)
            {
                // 将传入字符中的反斜杠和单双引号保护，转成 JS 安全字符串
                string safeData = Newtonsoft.Json.JsonConvert.SerializeObject(data);
                VpsTerminalWebView.CoreWebView2.ExecuteScriptAsync($"writeToTerminal({safeData});");
            }
        });
    }

    /// <summary>
    /// 前端 Xterm.js 键盘输入按键时触发向由后台 SSH 转发的回调
    /// </summary>
    public event Action<string>? OnVpsTerminalInput;

    private void VpsTerminalWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string json = e.TryGetWebMessageAsString();
            var payload = Newtonsoft.Json.Linq.JObject.Parse(json);
            if (payload["type"]?.ToString() == "input")
            {
                string? data = payload["data"]?.ToString();
                if (!string.IsNullOrEmpty(data))
                {
                    OnVpsTerminalInput?.Invoke(data);
                }
            }
            else if (payload["type"]?.ToString() == "resize")
            {
                // 可以加给后端的 ShellStream WindowSize 扩展发送 TTY Resize 信号
            }
        }
        catch { }
    }
}