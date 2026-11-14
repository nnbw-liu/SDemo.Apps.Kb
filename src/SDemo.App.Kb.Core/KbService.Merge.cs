using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Storage;

namespace SDemo.App.Kb.Core;

/// <summary>
/// 多父合并：调用方把两支的冲突裁决成最终块序列，KB 只负责落成带两个父的版本。
/// KB 不做自动三路合并，也不调模型。
/// </summary>
public sealed partial class KbService
{
    public WriteResult Merge(KbMergeRequest request, string actorApp)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Blocks.Count == 0)
        {
            throw KbException.Invalid("合并必须给出已裁决的块序列。");
        }

        using var session = _store.Begin();
        var doc = session.GetDoc(request.DocId)
            ?? throw KbException.NotFound($"文档不存在：{request.DocId}");

        EnsureCanWrite(doc, actorApp);

        var current = session.GetCurrentVersion(doc.Id)
            ?? throw KbException.NotFound($"文档没有当前版本：{doc.Id}");

        if (request.ExpectedVersion > 0 && request.ExpectedVersion != current.Seq)
        {
            throw new KbException(
                KbErrorCodes.VersionConflict,
                $"expectedVersion={request.ExpectedVersion}，当前版本 v{current.Seq}。");
        }

        var left = session.GetVersionBySeq(doc.Id, request.LeftVersion)
            ?? throw new KbException(KbErrorCodes.VersionNotFound, $"没有 v{request.LeftVersion}。");
        var right = session.GetVersionBySeq(doc.Id, request.RightVersion)
            ?? throw new KbException(KbErrorCodes.VersionNotFound, $"没有 v{request.RightVersion}。");

        if (string.Equals(left.Id, right.Id, StringComparison.Ordinal))
        {
            throw KbException.Invalid("合并的两个父版本不能相同。");
        }

        var now = Now;
        var blocks = MaterializeAll(session, doc, request.Blocks, now);
        if (blocks.Count == 0)
        {
            throw KbException.Invalid("合并后的文档不能为空。");
        }

        var hash = KbText.ContentHash(blocks.Select(block => block.ContentHash));
        if (string.Equals(hash, current.ContentHash, StringComparison.Ordinal))
        {
            return new WriteResult
            {
                DocId = doc.Id,
                VersionId = current.Id,
                Seq = current.Seq,
                NamedVersion = current.NamedVersion,
                NoChange = true,
                CurrentVersionSeq = current.Seq,
                BlockIds = blocks.Select(block => block.Id).ToList(),
                BlockCount = blocks.Count,
            };
        }

        var author = AuthorFor(actorApp, request.RationaleSource, import: false);
        var namedVersion = ResolveNamedVersion(doc.Kind, current, request.NamedVersion);
        var currentBlocks = session.GetVersionBlocks(current.Id);

        var version = NewVersion(
            session,
            doc,
            current,
            blocks,
            hash,
            author,
            request.Rationale ?? "合并两个分叉版本",
            now,
            namedVersion,
            [left.Id, right.Id]);

        WriteChanges(
            session,
            doc,
            version.Id,
            KbDiff.BlockDiffs(currentBlocks, blocks),
            request.Rationale,
            request.RationaleSource,
            now);

        session.SetCurrentVersion(doc.Id, version.Id, now);
        session.WriteAudit(actorApp, "merge", doc.Id, $"v{version.Seq}; parents=v{left.Seq}+v{right.Seq}");
        MaybePrune(session, doc);
        session.Commit();

        return new WriteResult
        {
            DocId = doc.Id,
            VersionId = version.Id,
            Seq = version.Seq,
            NamedVersion = version.NamedVersion,
            CurrentVersionSeq = version.Seq,
            BlockIds = blocks.Select(block => block.Id).ToList(),
            BlockCount = blocks.Count,
        };
    }
}
