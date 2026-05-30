using Slypn.Api.Models;

namespace Slypn.Api.Services;

public interface IMockDataService
{
    IReadOnlyList<Article> Articles { get; }
    IReadOnlyList<BlogPost> BlogPosts { get; }
    IReadOnlyList<CommunityEvent> Events { get; }
    IReadOnlyList<Resource> Resources { get; }
    IReadOnlyList<Newsletter> Newsletters { get; }
}
