using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MyAgent.Core.Models;
using Newtonsoft.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MyAgent.Core.Config;

public interface ISkillConfigReader
{
    Task<IEnumerable<SkillDefinition>> LoadAllSkillsAsync(string configDirectory);
    Task<SkillDefinition?> LoadSkillAsync(string filePath);
}

public class SkillConfigReader : ISkillConfigReader
{
    private readonly IDeserializer _yamlDeserializer;

    public SkillConfigReader()
    {
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public async Task<IEnumerable<SkillDefinition>> LoadAllSkillsAsync(string configDirectory)
    {
        if (!Directory.Exists(configDirectory))
            return Enumerable.Empty<SkillDefinition>();

        var skills = new List<SkillDefinition>();
        var files = Directory.GetFiles(configDirectory, "*.yaml", SearchOption.AllDirectories);
        
        foreach (var file in files)
        {
            try
            {
                var skill = await LoadSkillAsync(file);
                if (skill != null)
                {
                    skills.Add(skill);
                }
            }
            catch (Exception ex)
            {
                // In a real app we would log this parse error via ILogger
                Console.WriteLine($"Error loading skill {file}: {ex.Message}");
            }
        }
        
        return skills;
    }

    public async Task<SkillDefinition?> LoadSkillAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var yamlText = await File.ReadAllTextAsync(filePath);
        
        // Strategy: YamlDotNet parses YAML to a Dict/Object structure. 
        // We serialize it to JSON and then use Newtonsoft.Json to map back to our strong typed class.
        // This is robust for mapping dynamic JObject properties like Params.
        
        var yamlObject = _yamlDeserializer.Deserialize(new StringReader(yamlText));
        if (yamlObject == null) return null;

        var json = JsonConvert.SerializeObject(yamlObject, Newtonsoft.Json.Formatting.None);
        
        var settings = new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
            {
                NamingStrategy = new Newtonsoft.Json.Serialization.SnakeCaseNamingStrategy()
            }
        };
        var definition = JsonConvert.DeserializeObject<SkillDefinition>(json, settings);

        return definition;
    }
}
