namespace SDemo.App.Kb.Storage;

/// <summary>连接工厂的薄门面：读用短连接，写用事务会话（<see cref="Begin"/>）。</summary>
public sealed class KbStore
{
    public KbStore(KbDatabase database)
    {
        Database = database;
        Database.EnsureSchema();
    }

    public KbDatabase Database { get; }

    public KbSession Begin() => new(Database, write: true);

    public KbSession Read() => new(Database, write: false);

    /// <summary>建库时的整体重建入口（schema 升级或索引损坏后自愈）。</summary>
    public void RebuildIndexes()
    {
        using var session = Begin();
        session.RebuildIndexes();
        session.Commit();
    }
}
