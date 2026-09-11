using System.Net.Http;
using System.Threading;
using System.Windows;
using ClaudeUsageWidget.Auth;
using ClaudeUsageWidget.UI;
using ClaudeUsageWidget.Usage;

namespace ClaudeUsageWidget;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "Global\\ClaudeUsageWidget_SingleInstance";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private Mutex? _singleInstanceMutex;
    private HttpClient? _http;
    private UsagePoller? _poller;
    private DockedBarWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        _http = new HttpClient();
        var credentials = new CredentialStore(_http);
        var usageClient = new UsageClient(_http, credentials);
        _poller = new UsagePoller(usageClient, PollInterval);

        _window = new DockedBarWindow(_poller);
        _window.Show();

        _poller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _poller?.Dispose();
        _http?.Dispose();

        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }
}
