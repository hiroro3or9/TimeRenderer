using System.Linq;
using System.Reflection;

using TimeRenderer.Models;
using TimeRenderer.ViewModels;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>AppSettingsへ項目を追加したとき、保存・復元の共通binding追加を必須にする。</summary>
public class AppSettingsMappingContractTests
{
    [Test]
    public async Task 全AppSettingsプロパティに重複のないマッピングが存在する()
    {
        var settingsProperties = typeof(AppSettings)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToArray();
        var mappedProperties = MainViewModel.MappedAppSettingsPropertyNames
            .OrderBy(name => name)
            .ToArray();

        await Assert.That(mappedProperties.Length).IsEqualTo(settingsProperties.Length);
        await Assert.That(mappedProperties.Distinct().Count()).IsEqualTo(mappedProperties.Length);
        for (var index = 0; index < settingsProperties.Length; index++)
        {
            await Assert.That(mappedProperties[index]).IsEqualTo(settingsProperties[index]);
        }
    }
}
