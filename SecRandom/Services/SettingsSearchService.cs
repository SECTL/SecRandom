using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services;
using SecRandom.Models;

namespace SecRandom.Services;

public class SettingsSearchService
{
    private ILogger<SettingsSearchService> _logger;
    public List<SettingsMetadata> SettingsMetadata { get; } = [];
    
    public SettingsSearchService(ILogger<SettingsSearchService> logger)
    {
        _logger = logger;
        GenerateMetadata();
    }

    public void GenerateMetadata()
    {
        SettingsMetadata.Clear();
        
        var resources = Assembly.GetExecutingAssembly().DefinedTypes
            .Where(info => info.Namespace?.StartsWith(@"SecRandom.Langs.SettingsPages") ?? false)
            .OrderBy(info => info.FullName ?? @"???")
            .ToList();
        
        foreach (var resourceType in resources)
        {
            // 解析设置界面
            var settingsPageResourceId = resourceType.FullName?
                .Replace(@"SecRandom.Langs.SettingsPages.", "").Replace(@".Resources", "");
            if (settingsPageResourceId == null) continue;
            
            var settingsPageClassName = @"SecRandom.Views.SettingsPages." + settingsPageResourceId + @"SettingsPage";
            var settingsPageInfo = PagesRegistryService.SettingsItems
                .First(info => info.SettingsPageType?.FullName == settingsPageClassName);
            
            SettingsMetadata.Add(new SettingsMetadata
            {
                IsPage = true,
                PageId = settingsPageInfo.Id,
                PageName = settingsPageInfo.Name,
                Id = settingsPageInfo.Id,
                Name = settingsPageInfo.Name
            });
            
            // 解析子设置
            var properties = resourceType.DeclaredProperties.ToList();

            List<string> rootSettings = [];
            Dictionary<string, List<string>> subSettings = [];
            foreach (var declaredProperty in properties)
            {
                if (!declaredProperty.Name.StartsWith(@"S_") ||
                    declaredProperty.Name.EndsWith(@"_R") ||
                    declaredProperty.Name.EndsWith(@"_D"))
                {
                    continue;
                }
                
                if (declaredProperty.Name.Count(c => c == '_') == 1)
                {
                    rootSettings.Add(declaredProperty.Name);
                }
                
                if (declaredProperty.Name.Count(c => c == '_') == 2)
                {
                    var parts = declaredProperty.Name.Split('_');
                    var category = parts[0] + @"_" + parts[1];
                    
                    if (!subSettings.ContainsKey(category))
                    {
                        subSettings[category] = [];
                    }
                    subSettings[category].Add(parts[2]);
                }
            }
            
            foreach (var rootId in rootSettings)
            {
                var rootName = (string)properties.First(property => property.Name == rootId).GetValue(null)!;
                var rootDescription = (string?)properties.FirstOrDefault(property => property.Name == rootId + "_D")?.GetValue(null) ?? string.Empty;
                SettingsMetadata.Add(new SettingsMetadata
                {
                    PageId = settingsPageInfo.Id,
                    PageName = settingsPageInfo.Name,
                    IsCategory = true,
                    CategoryId = rootId,
                    CategoryName = rootName,
                    Id = rootId,
                    Name = rootName,
                    Description = rootDescription
                });

                foreach (var subId in subSettings.GetValueOrDefault(rootId, []))
                {
                    var fullId = rootId + @"_" + subId;
                    var subDescription = (string?)properties.FirstOrDefault(property => property.Name == fullId + "_D")?.GetValue(null) ?? string.Empty;
                    SettingsMetadata.Add(new SettingsMetadata
                    {
                        PageId = settingsPageInfo.Id,
                        PageName = settingsPageInfo.Name,
                        CategoryId = rootId,
                        CategoryName = rootName,
                        Id = fullId,
                        Name = (string)properties.First(property => property.Name == fullId).GetValue(null)!,
                        Description = subDescription
                    });
                }
            }
        }
    }

    public void LogTestInformation()
    {
        foreach (var metadata in SettingsMetadata)
        {
            _logger.LogDebug(@"{Content} [{Id}]", metadata.ToString(), metadata.Id);
        }
    }
}