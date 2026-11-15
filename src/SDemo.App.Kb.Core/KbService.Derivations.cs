using SDemo.App.Kb.Contracts;

namespace SDemo.App.Kb.Core;

public sealed partial class KbService
{
    /// <summary>
    /// 待 OCR 的扫描件清单：只给 KB / Agent。返回占位块 id 与原件路径，
    /// Agent 直接读原件、OCR 后用 revise 替换占位块。
    /// </summary>
    public DerivationListResult ListDerivations(string actorApp, int limit = 100)
    {
        if (!IsPrivileged(actorApp))
        {
            throw new KbException(KbErrorCodes.Forbidden, "待识别清单只允许 KB 或 Agent。");
        }

        using var session = _store.Read();
        var result = new DerivationListResult();

        foreach (var doc in session.ListPendingDocs(limit))
        {
            var current = session.GetCurrentVersion(doc.Id);
            if (current is null)
            {
                continue;
            }

            var blocks = session.GetVersionBlocks(current.Id);
            var placeholder = blocks.FirstOrDefault();

            result.Items.Add(new DerivationItem
            {
                DocId = doc.Id,
                Title = doc.Title,
                Kind = doc.Kind,
                VersionSeq = current.Seq,
                PlaceholderBlockId = placeholder?.Id ?? string.Empty,
                CreatedAt = doc.CreatedAt,
                Artifacts = session.ListBlobsForDoc(doc.Id)
                    .Select(blob => new ArtifactView
                    {
                        Sha256 = blob.Sha256,
                        Mime = blob.Mime,
                        Size = blob.Size,
                        Name = blob.Name,
                        StoredPath = blob.StoredPath,
                    })
                    .ToList(),
            });
        }

        return result;
    }
}
