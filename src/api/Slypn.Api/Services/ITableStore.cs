using Azure.Data.Tables;

namespace Slypn.Api.Services;

/// <summary>
/// Single point of access to the SLYPN Azure Table Storage account. Table
/// handles are lazy SDK references — they don't require the table to exist
/// (creation happens in TableBootstrapper on startup).
///
/// Consumers must check <see cref="IsConfigured"/> before accessing handles;
/// otherwise an InvalidOperationException is thrown.
/// </summary>
public interface ITableStore
{
    bool IsConfigured { get; }

    TableClient Articles    { get; }
    TableClient Drafts      { get; }
    TableClient Events      { get; }
    TableClient Resources   { get; }
    TableClient Newsletters { get; }
    TableClient Members     { get; }
}
