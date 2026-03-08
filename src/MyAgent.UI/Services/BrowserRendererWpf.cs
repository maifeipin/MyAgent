using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using MyAgent.Skills.Browser;
using MyAgent.UI;

namespace MyAgent.UI.Services;

/// <summary>
/// 当运行在有界面的 WPF 应用下时，使用真实的被放入窗口树中的 WebView2 进行导航与交互
/// </summary>
public class BrowserRendererWpf : IBrowserRenderer
{
    private readonly MainWindow _mainWindow;

    public BrowserRendererWpf(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public async Task NavigateAsync(string url)
    {
        var tcs = new TaskCompletionSource();

        await _mainWindow.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;
                handler = (s, e) =>
                {
                    _mainWindow.MainWebView.CoreWebView2.NavigationCompleted -= handler;
                    if (e.IsSuccess) tcs.SetResult();
                    else tcs.SetException(new Exception($"Navigation failed to {url} with error {e.WebErrorStatus}"));
                };
                
                _mainWindow.MainWebView.CoreWebView2.NavigationCompleted += handler;
                _mainWindow.MainWebView.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        await tcs.Task;
    }

    public async Task<string> ExecuteScriptAsync(string script)
    {
        var tcs = new TaskCompletionSource<string>();

        await _mainWindow.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var result = await _mainWindow.MainWebView.ExecuteScriptAsync(script);
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return await tcs.Task;
    }
}
