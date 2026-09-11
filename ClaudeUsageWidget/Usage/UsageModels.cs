namespace ClaudeUsageWidget.Usage;

public sealed record UsageSnapshot(
    double? SessionUtilization,
    DateTimeOffset? SessionResetsAt,
    double? WeeklyUtilization,
    DateTimeOffset? WeeklyResetsAt,
    string? Status,
    DateTimeOffset FetchedAt);

public sealed class UsageUnavailableException : Exception
{
    public UsageUnavailableException(string message) : base(message)
    {
    }
}
