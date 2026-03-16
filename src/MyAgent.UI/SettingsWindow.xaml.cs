using System.Windows;

namespace MyAgent.UI;

public partial class SettingsWindow : Window
{
    private bool _isUpdatingPassword = false;

    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += SettingsWindow_Loaded;
        Unloaded += SettingsWindow_Unloaded;
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
        {
            SyncPasswordBoxToVm(vm);
            vm.PropertyChanged += Vm_PropertyChanged;
        }
    }

    private void SettingsWindow_Unloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
        {
            vm.PropertyChanged -= Vm_PropertyChanged;
        }
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.MainViewModel.AiApiKey))
        {
            if (DataContext is ViewModels.MainViewModel vm)
            {
                SyncPasswordBoxToVm(vm);
            }
        }
    }

    private void SyncPasswordBoxToVm(ViewModels.MainViewModel vm)
    {
        _isUpdatingPassword = true;
        ApiPasswordBox.Password = vm.AiApiKey;
        _isUpdatingPassword = false;
    }

    private void ApiPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingPassword) return;

        if (DataContext is ViewModels.MainViewModel vm)
        {
            vm.AiApiKey = ApiPasswordBox.Password;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
        {
            vm.SaveConfig();
        }
        Close();
    }

    private void OpenProfileManager_Click(object sender, RoutedEventArgs e)
    {
        var vm = DataContext as MyAgent.UI.ViewModels.MainViewModel;
        if (vm == null) return;

        var services = ((App)Application.Current).Services;
        var window = new ProfileManagerWindow
        {
            Owner = this,
            DataContext = new MyAgent.UI.ViewModels.ProfileManagerViewModel(vm, 
                Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<System.Net.Http.IHttpClientFactory>(services))
        };
        window.ShowDialog();
    }
}
