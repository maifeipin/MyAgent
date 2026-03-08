using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MyAgent.Skills.Browser;

/// <summary>
/// 全局单例的 WebView2 托管容器。
/// 必须在 STA 线程中创建和运行，否则 WPF 的 WebView 组件会报错。
/// </summary>
public interface IBrowserRenderer
{
    Task NavigateAsync(string url);
    Task<string> ExecuteScriptAsync(string script);
}

public class BrowserRendererCore : IBrowserRenderer, IDisposable
{
    private WebView2? _webView;
    private readonly Thread _staThread;
    
    // 用于向 STA 消息泵发送任务
    private SynchronizationContext? _staSyncContext;
    private readonly ManualResetEventSlim _initializedEvent = new(false);

    public BrowserRendererCore()
    {
        _staThread = new Thread(RunStaMessagePump)
        {
            IsBackground = true,
            Name = "WebView2_STA_Thread"
        };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();

        // 阻塞等待 WebView2 初始化核心引擎完成
        _initializedEvent.Wait();
    }

    private void RunStaMessagePump()
    {
        // 强制创建 WPF Application 上下文以保证拥有合法的 Dispatcher 消息队列
        if (System.Windows.Application.Current == null)
        {
            new System.Windows.Application();
        }

        _staSyncContext = SynchronizationContext.Current;

        // 在 STA 线程上初始化控件
        InitializeWebViewAsync().GetAwaiter().GetResult();
        _initializedEvent.Set();

        // 挂起线程，启动 WPF 消息泵，监听后续调用
        System.Windows.Threading.Dispatcher.Run();
    }

    private async Task InitializeWebViewAsync()
    {
        _webView = new WebView2();
        CoreWebView2EnvironmentOptions options = null;

        // 探测进程环境或本地缓存中是否有代理需要应用
        var configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user_config.json");
        if (System.IO.File.Exists(configPath))
        {
            try
            {
                var json = System.IO.File.ReadAllText(configPath);
                var config = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, string>>(json);
                if (config != null && config.TryGetValue("EnableGlobalProxy", out var proxyEnabled) && proxyEnabled.Equals("True", StringComparison.OrdinalIgnoreCase))
                {
                    if (config.TryGetValue("GlobalProxyAddress", out var proxyAddr) && !string.IsNullOrWhiteSpace(proxyAddr))
                    {
                        options = new CoreWebView2EnvironmentOptions
                        {
                            AdditionalBrowserArguments = $"--proxy-server={proxyAddr} --proxy-bypass-list=<-loopback>"
                        };
                    }
                }
                
                if (options == null)
                {
                    options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--no-proxy-server" };
                }
            }
            catch { }
        }

        // 重要：由于前台 MainWindow 也起了一个 WebView2，两者若参数或路径不一致会在同一 userDataFolder 引发 COM 级互斥与崩塌挂起。
        // 为底层逻辑引擎单独划分出一片物理沙盒，实现完全异步双活。
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var userDataFolder = System.IO.Path.Combine(appData, "MyAgent", "WebView2Data_CoreEngine");

        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
        await _webView.EnsureCoreWebView2Async(env);
    }

    public async Task NavigateAsync(string url)
    {
        var tcs = new TaskCompletionSource();

        _staSyncContext?.Post(async _ =>
        {
            try
            {
                EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;
                handler = (s, e) =>
                {
                    _webView!.CoreWebView2.NavigationCompleted -= handler;
                    if (e.IsSuccess) tcs.SetResult();
                    else tcs.SetException(new Exception($"Navigation failed to {url} with error {e.WebErrorStatus}"));
                };
                
                _webView!.CoreWebView2.NavigationCompleted += handler;
                _webView.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, null);

        await tcs.Task;
    }

    public async Task<string> ExecuteScriptAsync(string script)
    {
        var tcs = new TaskCompletionSource<string>();

        _staSyncContext?.Post(async _ =>
        {
            try
            {
                var result = await _webView!.ExecuteScriptAsync(script);
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, null);

        return await tcs.Task;
    }

    public void Dispose()
    {
        _staSyncContext?.Post(_ =>
        {
            _webView?.Dispose();
            System.Windows.Threading.Dispatcher.ExitAllFrames();
        }, null);
    }
}
