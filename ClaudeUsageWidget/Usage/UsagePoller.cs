namespace ClaudeUsageWidget.Usage;

/// <summary>
/// Polls usage on a background timer and keeps the last good snapshot on any
/// failure, so the UI never has to blank out for a transient network hiccup.
/// </summary>
public sealed class UsagePoller : IDisposable
{
    private readonly UsageClient _client;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts = new();

    private TaskCompletionSource<bool> _wake =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event Action<UsageSnapshot>? SnapshotUpdated;
    public event Action<string>? ErrorOccurred;

    public UsageSnapshot? Last { get; private set; }

    public UsagePoller(UsageClient client, TimeSpan interval)
    {
        _client = client;
        _interval = interval;
    }

    public void Start() => _ = RunLoopAsync(_cts.Token);

    /// <summary>Wakes the poller immediately instead of waiting out the current interval.</summary>
    public void TriggerNow() => _wake.TrySetResult(true);

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var snapshot = await _client.GetUsageAsync(ct);
                Last = snapshot;
                SnapshotUpdated?.Invoke(snapshot);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex.Message);
            }

            _wake = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                await Task.WhenAny(Task.Delay(_interval, ct), _wake.Task);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
