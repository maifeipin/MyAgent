using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAgent.Core.Models;
using YamlDotNet.Serialization;

namespace MyAgent.UI.ViewModels;

public partial class SkillManagerViewModel : ObservableObject
{
    private readonly MainViewModel _mainViewModel;
    private readonly string _skillsDirectory;
    private readonly IDeserializer _yamlDeserializer;

    public ObservableCollection<string> SkillFiles { get; } = new();

    private string? _selectedFile;
    public string? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (SetProperty(ref _selectedFile, value))
            {
                OnPropertyChanged(nameof(IsSkillSelected));
                LoadSkillContent();
            }
        }
    }

    public bool IsSkillSelected => !string.IsNullOrEmpty(SelectedFile);

    [ObservableProperty]
    private string _selectedFilePath = "";

    [ObservableProperty]
    private string _editorContent = "";

    [ObservableProperty]
    private string _validationError = "";

    public SkillManagerViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
        _skillsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "skills");
        
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        RefreshFileList();
    }

    private void RefreshFileList()
    {
        SkillFiles.Clear();
        if (Directory.Exists(_skillsDirectory))
        {
            var files = Directory.GetFiles(_skillsDirectory, "*.yaml")
                                 .Select(Path.GetFileName)
                                 .Where(f => f != null)
                                 .Cast<string>();
            foreach (var f in files)
            {
                SkillFiles.Add(f);
            }
        }
    }

    private void LoadSkillContent()
    {
        ValidationError = "";
        if (string.IsNullOrEmpty(SelectedFile))
        {
            SelectedFilePath = "";
            EditorContent = "";
            return;
        }

        SelectedFilePath = Path.Combine(_skillsDirectory, SelectedFile);
        if (File.Exists(SelectedFilePath))
        {
            EditorContent = File.ReadAllText(SelectedFilePath);
        }
    }

    [RelayCommand]
    private void CreateNewSkill()
    {
        string newFileName = $"new_skill_{DateTime.Now.Ticks}.yaml";
        SelectedFilePath = Path.Combine(_skillsDirectory, newFileName);
        SelectedFile = newFileName; // Pseudo selection
        
        EditorContent = @"schema_version: '1.1'
skill_id: 'custom.my_new_skill'
name: '我的自定义新技能'
description: ''
trigger:
  type: 'manual'
workflow:
  - step_id: 'step_1'
    name: '示范动作'
    action: ''
    params: {}
";
        ValidationError = "";
    }

    [RelayCommand]
    private void DeleteSkill()
    {
        if (string.IsNullOrEmpty(SelectedFilePath)) return;

        var res = MessageBox.Show($"确定要从磁盘永久删除 {Path.GetFileName(SelectedFilePath)} 吗？", "警告", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res == MessageBoxResult.Yes)
        {
            if (File.Exists(SelectedFilePath))
            {
                File.Delete(SelectedFilePath);
            }
            RefreshFileList();
            SelectedFile = null;
            
            // Force reload in main ui
            _mainViewModel.LoadSkillsCommand.Execute(null);
        }
    }

    [RelayCommand]
    private void SaveSkill(Window window)
    {
        if (string.IsNullOrEmpty(SelectedFilePath)) return;

        ValidationError = "";

        // Core Firewall - Validation Phase
        try
        {
            var def = _yamlDeserializer.Deserialize<SkillDefinition>(EditorContent);
            if (def == null || string.IsNullOrWhiteSpace(def.SkillId))
            {
                ValidationError = "❌ 保存被强行拦截：YAML 格式无效或丢失必需字段 `skill_id`。";
                return;
            }
            if (def.SchemaVersion != "1.1")
            {
                ValidationError = "❌ 保存被强行拦截：必须声明 `schema_version: '1.1'`。";
                return;
            }
        }
        catch (Exception ex)
        {
            ValidationError = $"❌ 语法层崩溃拦截 (反序列化失败):\n{ex.Message}";
            return;
        }

        // Passed validation, flush to disk
        try
        {
            if (!Directory.Exists(_skillsDirectory)) Directory.CreateDirectory(_skillsDirectory);
            File.WriteAllText(SelectedFilePath, EditorContent);
            
            // Refresh main interface seamlessly
            _mainViewModel.LoadSkillsCommand.Execute(null);
            RefreshFileList();
            
            MessageBox.Show("校验通过！配置已强制落盘，主界面已成功实现热重载。", "操作成功", MessageBoxButton.OK, MessageBoxImage.Information);
            window?.Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"写入物理磁盘失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
