using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Storage;

namespace SDemo.App.Kb.Core;

/// <summary>
/// 引用登记：外部实体（Planner 待办、Meeting 议题、…）把自己对 KB 的引用写进 `kb_link`。
/// 没有这一步，`kb_refs` 只能看到 KB 文档之间的引用，影响面与引用保护都只成立一半。
/// </summary>
public sealed partial class KbService
{
    public RefRegistrationResult RegisterRef(RefRegistrationRequest request, string authenticatedAppId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var source = request.Source ?? throw KbException.Invalid("缺少 source。");
        if (string.IsNullOrWhiteSpace(authenticatedAppId))
        {
            throw new KbException(KbErrorCodes.UnauthorizedApp, "缺少调用方身份。");
        }

        if (!string.IsNullOrWhiteSpace(source.AppId)
            && !string.Equals(source.AppId, authenticatedAppId, StringComparison.Ordinal))
        {
            throw new KbException(
                KbErrorCodes.UnauthorizedApp,
                $"source.appId「{source.AppId}」与 token 绑定的「{authenticatedAppId}」不一致。");
        }

        var fromUri = ValidateFromUri(source.EntityUri);

        if (!KbUri.TryParse(request.To, out var target, out var error) || target is null)
        {
            throw KbException.Invalid($"引用 URI 不合法：{error}");
        }

        if (!target.HasVersion)
        {
            throw KbException.Invalid($"引用必须带版本：{request.To}（铁律 4）");
        }

        var rel = NormalizeRel(request.Rel);
        var now = Now;

        using var session = _store.Begin();
        var targetDoc = session.GetDoc(target.DocId, includeDeleted: true);
        var created = !session.ExistsExternalRef(fromUri, target.ToString(), rel);

        session.UpsertExternalRef(new LinkRow(
            KbIds.NewDocId(),
            string.Empty,
            fromUri,
            target.ToString(),
            rel,
            authenticatedAppId,
            target.DocId,
            target.VersionSeq,
            target.BlockId,
            now,
            now));

        session.WriteAudit(authenticatedAppId, "ref.register", target.DocId, $"{fromUri} → {target} ({rel})");
        session.Commit();

        return new RefRegistrationResult
        {
            FromUri = fromUri,
            ToUri = target.ToString(),
            Rel = rel,
            Created = created,
            TargetTitle = targetDoc?.Title,
        };
    }

    public RefRetractResult RetractRefs(RefRetractRequest request, string authenticatedAppId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var source = request.Source ?? throw KbException.Invalid("缺少 source。");
        if (string.IsNullOrWhiteSpace(authenticatedAppId))
        {
            throw new KbException(KbErrorCodes.UnauthorizedApp, "缺少调用方身份。");
        }

        if (!string.IsNullOrWhiteSpace(source.AppId)
            && !string.Equals(source.AppId, authenticatedAppId, StringComparison.Ordinal))
        {
            throw new KbException(
                KbErrorCodes.UnauthorizedApp,
                $"source.appId「{source.AppId}」与 token 绑定的「{authenticatedAppId}」不一致。");
        }

        var fromUri = ValidateFromUri(source.EntityUri);
        string? to = null;
        if (!string.IsNullOrWhiteSpace(request.To))
        {
            if (!KbUri.TryParse(request.To, out var parsed, out var error) || parsed is null)
            {
                throw KbException.Invalid($"引用 URI 不合法：{error}");
            }

            to = parsed.ToString();
        }

        using var session = _store.Begin();
        var removed = session.DeleteExternalRefs(
            fromUri,
            authenticatedAppId,
            to,
            string.IsNullOrWhiteSpace(request.Rel) ? null : NormalizeRel(request.Rel));

        if (removed > 0)
        {
            session.WriteAudit(authenticatedAppId, "ref.retract", null, $"{fromUri}; removed={removed}");
        }

        session.Commit();
        return new RefRetractResult { FromUri = fromUri, Removed = removed };
    }

    /// <summary>按来源实体列出它登记过的引用；普通 App 只能看自己的。</summary>
    public RefListResult ListRefs(string fromUri, string authenticatedAppId)
    {
        var source = ValidateFromUri(fromUri);
        var ownerFilter = IsPrivileged(authenticatedAppId) ? null : authenticatedAppId;

        using var session = _store.Read();
        var result = new RefListResult { FromUri = source };

        foreach (var link in session.ListExternalRefs(source, ownerFilter))
        {
            result.Items.Add(BuildRef(session, link));
        }

        return result;
    }

    private static string ValidateFromUri(string? entityUri)
    {
        var value = (entityUri ?? string.Empty).Trim();
        if (value.Length == 0 || value.Length > 300)
        {
            throw KbException.Invalid("source.entityUri 不能为空且不能超过 300 字符。");
        }

        if (!value.StartsWith("sdemo.", StringComparison.Ordinal)
            || value.Any(char.IsWhiteSpace)
            || value.Contains('#'))
        {
            throw KbException.Invalid($"source.entityUri 不是合法实体 URI：{value}");
        }

        return value;
    }

    private static string NormalizeRel(string? rel)
    {
        var value = (rel ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return "related";
        }

        if (value.Length > 32)
        {
            throw KbException.Invalid("rel 不能超过 32 字符。");
        }

        return value;
    }
}
