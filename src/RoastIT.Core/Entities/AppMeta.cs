namespace RoastIT.Core.Entities;

/// <summary>Key/value row of <c>app_meta</c>: app_id, schema_version, app_version, exported_at (plan §6.2).</summary>
public class AppMeta
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";

    public const string AppIdKey = "app_id";
    public const string AppIdValue = "roastline";
    public const string SchemaVersionKey = "schema_version";
    public const string AppVersionKey = "app_version";
    public const string ExportedAtKey = "exported_at";
}
