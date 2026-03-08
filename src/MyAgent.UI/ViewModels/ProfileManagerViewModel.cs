using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;

namespace MyAgent.UI.ViewModels;

public partial class ProfileManagerViewModel : ObservableObject
{
    private readonly MainViewModel _mainViewModel;
    private readonly IHttpClientFactory _httpClientFactory;
    
    public ObservableCollection<AiProfile> AiProfiles { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfileSelected))]
    private AiProfile? _selectedEditableProfile;

    public bool IsProfileSelected => SelectedEditableProfile != null;

    [ObservableProperty]
    private string _connectionTestResult = "尚未测试";
    
    [ObservableProperty]
    private string _connectionTestColor = "Gray";

    public ProfileManagerViewModel(MainViewModel mainViewModel, IHttpClientFactory httpClientFactory)
    {
        _mainViewModel = mainViewModel;
        _httpClientFactory = httpClientFactory;
        
        // Deep copy to avoid immediate dirty writes to main viewmodel
        AiProfiles = new ObservableCollection<AiProfile>();
        foreach(var profile in _mainViewModel.AiProfiles)
        {
            AiProfiles.Add(new AiProfile 
            {
                Name = profile.Name,
                BaseUrl = profile.BaseUrl,
                ModelName = profile.ModelName,
                ApiKey = profile.ApiKey
            });
        }
    }

    [RelayCommand]
    private void CreateNewProfile()
    {
        var newProfile = new AiProfile { Name = "新建模型预设" };
        AiProfiles.Add(newProfile);
        SelectedEditableProfile = newProfile;
        ResetTestState();
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedEditableProfile != null)
        {
            AiProfiles.Remove(SelectedEditableProfile);
            SelectedEditableProfile = null;
            ResetTestState();
        }
    }

    private void ResetTestState()
    {
        ConnectionTestResult = "尚未测试";
        ConnectionTestColor = "Gray";
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (SelectedEditableProfile == null || string.IsNullOrWhiteSpace(SelectedEditableProfile.BaseUrl))
        {
            ConnectionTestResult = "请选定模型并确保 BaseUrl 不为空。";
            ConnectionTestColor = "Red";
            return;
        }

        ConnectionTestResult = "正在发起网络测试，请稍候...";
        ConnectionTestColor = "DarkOrange";

        try
        {
            var client = _httpClientFactory.CreateClient("AiModelsClient");
            if (!string.IsNullOrEmpty(SelectedEditableProfile.ApiKey))
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", SelectedEditableProfile.ApiKey);
            }

            // A tiny payload to ping completion endpoint
            var payload = new 
            {
                model = SelectedEditableProfile.ModelName,
                messages = new[] { new { role = "user", content = "Hi" } },
                max_tokens = 5
            };
            
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var res = await client.PostAsync(SelectedEditableProfile.BaseUrl, content);
            
            if (res.IsSuccessStatusCode)
            {
                ConnectionTestResult = $"连接成功。 ({res.StatusCode})";
                ConnectionTestColor = "Green";
            }
            else
            {
                 ConnectionTestResult = $"探针失败: {res.StatusCode} - {await res.Content.ReadAsStringAsync()}";
                 ConnectionTestColor = "Red";
            }
        }
        catch (Exception ex)
        {
            ConnectionTestResult = $"探针连接异常: {ex.Message}";
            ConnectionTestColor = "Red";
        }
    }

    [RelayCommand]
    private void SaveProfiles(Window window)
    {
        // sync back to MainViewModel
        _mainViewModel.AiProfiles.Clear();
        foreach (var p in AiProfiles)
        {
            _mainViewModel.AiProfiles.Add(new AiProfile 
            {
               Name = p.Name,
               BaseUrl = p.BaseUrl,
               ModelName = p.ModelName,
               ApiKey = p.ApiKey
            });
        }
        
        _mainViewModel.SaveConfig();
        MessageBox.Show("配置清单已保存", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        window?.Close();
    }
}
