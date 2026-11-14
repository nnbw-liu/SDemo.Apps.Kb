using System.Security.Cryptography;
using SDemo.App.Kb.Contracts;

namespace SDemo.App.Kb.Core;

/// <summary>
/// 原件库：sha256 内容寻址，拷贝进 <c>blobs/&lt;sha 前两位&gt;/&lt;sha&gt;</c>。
/// 去重天然免费；「仅索引不拷贝」与上传 URL 属于 P4-d，先只处理本机路径。
/// </summary>
public sealed class KbBlobStore
{
    private readonly string _root;

    public KbBlobStore(string dataDirectory)
    {
        _root = KbPaths.BlobsDirectory(dataDirectory);
        Directory.CreateDirectory(_root);
    }

    public BlobImport? Import(IngestArtifact artifact)
    {
        if (artifact is null)
        {
            return null;
        }

        var sha = (artifact.Sha256 ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(artifact.Path))
        {
            if (sha.Length == 0)
            {
                return null;
            }

            return new BlobImport(sha, artifact.Mime, artifact.Size, artifact.Name, null);
        }

        if (!File.Exists(artifact.Path))
        {
            throw KbException.Invalid($"原件不存在：{artifact.Path}");
        }

        var info = new FileInfo(artifact.Path);
        var actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(info.FullName)));

        if (sha.Length > 0 && !string.Equals(sha, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw KbException.Invalid($"原件 hash 不一致：声明 {sha}，实际 {actual}。");
        }

        var relative = Path.Combine(actual[..2], actual);
        var storedPath = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(storedPath)!);

        if (!File.Exists(storedPath))
        {
            File.Copy(info.FullName, storedPath, overwrite: false);
        }

        return new BlobImport(actual, artifact.Mime, info.Length, artifact.Name ?? info.Name, storedPath);
    }
}

public sealed record BlobImport(string Sha256, string? Mime, long Size, string? Name, string? StoredPath);
