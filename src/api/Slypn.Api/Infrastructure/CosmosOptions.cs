namespace Slypn.Api.Infrastructure;

public sealed class CosmosOptions
{
    public const string SectionName = "Cosmos";

    public string? Endpoint { get; set; }
    public string? Key      { get; set; }
    public string  Database { get; set; } = "slypn";
}
