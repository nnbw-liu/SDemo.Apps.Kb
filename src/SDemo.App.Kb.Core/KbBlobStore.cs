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
        var bytes = File.ReadAllBytes(info.FullName);
        var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));

        if (sha.Length > 0 && !string.Equals(sha, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw KbException.Invalid($"原件 hash 不一致：声明 {sha}，实际 {actual}。");
        }

        var storedPath = Store(actual, bytes, info.Name);
        return new BlobImport(actual, artifact.Mime, info.Length, artifact.Name ?? info.Name, storedPath);
    }

    /// <summary>导入界面拿不到本机路径时直接写字节；sha256 即文件名，重复内容天然去重。</summary>
    public BlobImport ImportBytes(byte[] bytes, string? mime, string name, string? sha256 = null)
    {
        var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.IsNullOrWhiteSpace(sha256)
            && !string.Equals(sha256, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw KbException.Invalid($"原件 hash 不一致：声明 {sha256}，实际 {actual}。");
        }

        var storedPath = Store(actual, bytes, name);
        return new BlobImport(actual, mime, bytes.Length, name, storedPath);
    }

    private string Store(string sha, byte[] bytes, string name)
    {
        var relative = Path.Combine(sha[..2], sha);
        var storedPath = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(storedPath)!);

        if (!File.Exists(storedPath))
        {
            File.WriteAllBytes(storedPath, bytes);
        }

        return storedPath;
    }
}

public sealed record BlobImport(string Sha256, string? Mime, long Size, string? Name, string? StoredPath);
