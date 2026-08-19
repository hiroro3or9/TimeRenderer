using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace TimeRenderer.Models;

/// <summary>
/// コミット履歴を手がかりに使うローカルリポジトリ1件。
///
/// 記録するのは<b>場所と紐づけだけ</b>で、コミットそのものは保存しない。
/// 履歴は元のリポジトリにあるのだから写しを持つ意味が薄く、
/// 持てば「TimeRenderer のデータフォルダにソースの断片が溜まる」ことになる。
///
/// <see cref="ProjectCodeId"/> を持たせているのは、
/// 「このリポジトリの作業はこの案件」という対応が実務ではほぼ固定だから。
/// 未記録の穴埋めでプロジェクトコードまで埋まると、選ぶ操作が1つ減る。
/// </summary>
public sealed class GitRepositoryInfo : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    private string _path = string.Empty;
    /// <summary>作業ディレクトリのパス（.git のある場所）</summary>
    public string Path
    {
        get => _path;
        set
        {
            if (SetProperty(ref _path, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(FolderName));
            }
        }
    }

    private string _name = string.Empty;
    /// <summary>表示名。空ならフォルダ名を使う（たいていフォルダ名で足りる）</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    private bool _isEnabled = true;
    /// <summary>このリポジトリを見に行くか。外しても設定は残す（一時的に外したいことがある）</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    private string? _projectCodeId;
    /// <summary>このリポジトリの作業に割り当てるプロジェクトコード（未設定なら null）</summary>
    public string? ProjectCodeId
    {
        get => _projectCodeId;
        set => SetProperty(ref _projectCodeId, value);
    }

    /// <summary>パスの末尾のフォルダ名。表示名が空のときの代わりに使う</summary>
    [JsonIgnore]
    public string FolderName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_path)) return string.Empty;

            try
            {
                // 末尾の区切り記号を落としてから取る（"C:\repo\" でも "repo" になるように）
                var trimmed = _path.TrimEnd(
                    System.IO.Path.DirectorySeparatorChar,
                    System.IO.Path.AltDirectorySeparatorChar);
                var name = System.IO.Path.GetFileName(trimmed);
                return string.IsNullOrEmpty(name) ? trimmed : name;
            }
            catch (ArgumentException)
            {
                // 不正な文字を含むパスでも一覧の描画は止めない
                return _path;
            }
        }
    }

    [JsonIgnore]
    public string DisplayName => _name.Length > 0 ? _name : FolderName;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
