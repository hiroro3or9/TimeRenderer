using System;
using System.Collections.Generic;
using System.IO;

using TimeRenderer.Services;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>実データから隔離したフォルダで、JSON保存・復旧契約を検証する。</summary>
public class JsonFileRepositoryTests
{
    private const string FileName = "test-data.json";

    [Test]
    public async Task 本体もバックアップも無ければ初回起動として扱う()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var result = JsonFileRepository.LoadFromDirectorySync<Dictionary<string, int>>(
                directory, FileName);

            await Assert.That(result.Status).IsEqualTo(LoadStatus.NotFound);
            await Assert.That(result.Value).IsNull();
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Test]
    public async Task 保存した内容を正常に読み戻し一時ファイルを残さない()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            JsonFileRepository.SaveToDirectorySync(
                directory, FileName, new Dictionary<string, int> { ["count"] = 3 });

            var result = JsonFileRepository.LoadFromDirectorySync<Dictionary<string, int>>(
                directory, FileName);

            await Assert.That(result.Status).IsEqualTo(LoadStatus.Loaded);
            await Assert.That(result.Value!["count"]).IsEqualTo(3);
            await Assert.That(File.Exists(Path.Combine(directory, FileName + ".tmp"))).IsFalse();
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Test]
    public async Task 本体が壊れたら直前のバックアップから復旧する()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            JsonFileRepository.SaveToDirectorySync(
                directory, FileName, new Dictionary<string, int> { ["version"] = 1 });
            JsonFileRepository.SaveToDirectorySync(
                directory, FileName, new Dictionary<string, int> { ["version"] = 2 });
            File.WriteAllText(Path.Combine(directory, FileName), "{ broken json");

            var result = JsonFileRepository.LoadFromDirectorySync<Dictionary<string, int>>(
                directory, FileName);

            await Assert.That(result.Status).IsEqualTo(LoadStatus.RecoveredFromBackup);
            await Assert.That(result.Value!["version"]).IsEqualTo(1);
            await Assert.That(result.SourceFile).IsEqualTo(FileName + ".bak");
            await Assert.That(result.Message).Contains("バックアップ");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Test]
    public async Task 本体もバックアップも壊れていれば失敗を明示する()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, FileName), "{ broken target");
            File.WriteAllText(Path.Combine(directory, FileName + ".bak"), "{ broken backup");

            var result = JsonFileRepository.LoadFromDirectorySync<Dictionary<string, int>>(
                directory, FileName);

            await Assert.That(result.Status).IsEqualTo(LoadStatus.Failed);
            await Assert.That(result.IsUsable).IsFalse();
            await Assert.That(result.Message).Contains("いずれも読み込めませんでした");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Test]
    public async Task 保存できなければ失敗を呼び出し元へ返す()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var blocker = Path.Combine(directory, "not-a-directory");
            File.WriteAllText(blocker, "file");

            var saved = JsonFileRepository.SaveToDirectorySync(
                blocker,
                FileName,
                new Dictionary<string, int> { ["count"] = 1 },
                notifyOnFailure: false);

            await Assert.That(saved).IsFalse();
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "TimeRenderer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TimeRenderer.Tests"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(directory);

        if (target.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }
    }
}
