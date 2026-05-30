namespace Slypn.Api.Infrastructure;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Connection string for dev (Azurite emulator) and key-based prod.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Container that holds article/media uploads.</summary>
    public string MediaContainer { get; set; } = "media";

    /// <summary>How long a generated read SAS URL is valid for.</summary>
    public TimeSpan ReadSasLifetime { get; set; } = TimeSpan.FromMinutes(15);
}
