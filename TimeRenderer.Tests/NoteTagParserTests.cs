using TimeRenderer.Helpers;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace TimeRenderer.Tests;

/// <summary>ふりかえり本文のタグ抽出と照合規則を固定する。</summary>
public class NoteTagParserTests
{
    [Test]
    public async Task 空の本文からはタグを返さない()
    {
        await Assert.That(NoteTagParser.Extract(null)).IsEmpty();
        await Assert.That(NoteTagParser.Extract("   ")).IsEmpty();
    }

    [Test]
    public async Task 日本語の区切り記号でタグを切り書かれた順に返す()
    {
        var tags = NoteTagParser.Extract("今日は #設計、#実装。最後に #確認！");

        await Assert.That(tags).IsEquivalentTo(["設計", "実装", "確認"]);
        await Assert.That(tags[0]).IsEqualTo("設計");
        await Assert.That(tags[1]).IsEqualTo("実装");
        await Assert.That(tags[2]).IsEqualTo("確認");
    }

    [Test]
    public async Task 大文字小文字だけが違う重複は最初の表記を残す()
    {
        var tags = NoteTagParser.Extract("#Review #review #REVIEW");

        await Assert.That(tags.Count).IsEqualTo(1);
        await Assert.That(tags[0]).IsEqualTo("Review");
    }

    [Test]
    public async Task HasTagは大文字小文字を区別せず完全一致する()
    {
        var note = "#Refactor #テスト";

        await Assert.That(NoteTagParser.HasTag(note, "refactor")).IsTrue();
        await Assert.That(NoteTagParser.HasTag(note, "テスト")).IsTrue();
        await Assert.That(NoteTagParser.HasTag(note, "test")).IsFalse();
        await Assert.That(NoteTagParser.HasTag(note, "Ref")).IsFalse();
    }
}
