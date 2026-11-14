namespace SDemo.App.Kb.Contracts;

/// <summary>错误码。写入面与 MCP 共用一套，便于 Agent 判断「重试 / 放弃 / 找用户」。</summary>
public static class KbErrorCodes
{
    public const string InvalidRequest = "kb_invalid_request";
    public const string UnknownKind = "kb_unknown_kind";
    public const string UnknownRetention = "kb_unknown_retention";
    public const string UnauthorizedApp = "kb_unauthorized_app";
    public const string Forbidden = "kb_forbidden";
    public const string NotFound = "kb_not_found";
    public const string VersionNotFound = "kb_version_not_found";
    public const string BlockNotFound = "kb_block_not_found";
    public const string VersionConflict = "kb_version_conflict";
    public const string DocDeleted = "kb_doc_deleted";
    public const string ProjectionOutOfScope = "kb_projection_out_of_scope";
    public const string TooLarge = "kb_too_large";
    public const string Internal = "kb_internal";
}

/// <summary>契约级异常。Host 映射成 <c>{ "error": { code, message } }</c>。</summary>
public sealed class KbException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;

    public static KbException Invalid(string message) => new(KbErrorCodes.InvalidRequest, message);

    public static KbException NotFound(string message) => new(KbErrorCodes.NotFound, message);

    public static KbException Conflict(string message) => new(KbErrorCodes.VersionConflict, message);
}
