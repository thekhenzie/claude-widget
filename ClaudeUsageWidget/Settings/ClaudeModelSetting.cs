using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeUsageWidget.Settings;

public enum ClaudeModelChoice
{
    Unknown,
    Opus,
    Sonnet,
}

/// <summary>
/// Reads and writes the "model" key in Claude Code's user settings
/// (~/.claude/settings.json), which is the default model new Claude Code
/// sessions start with. Only that one key is touched - every other setting
/// in the file is preserved as-is.
/// </summary>
public sealed class ClaudeModelSetting : IDisposable
{
    // Aliases Claude Code resolves to the latest model of each family.
    // "opus[1m]" keeps the 1M-context variant that was already configured.
    private const string OpusAlias = "opus[1m]";
    private const string SonnetAlias = "sonnet";

    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // Keep non-ASCII text (e.g. em dashes) and characters like '<' and '\''
        // readable instead of rewriting them as \uXXXX escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly FileSystemWatcher? _watcher;

    /// <summary>Raised (on a background thread) when settings.json changes on disk.</summary>
    public event Action? Changed;

    public ClaudeModelSetting()
    {
        if (!Directory.Exists(SettingsDirectory))
            return;

        // Picks up changes made outside the widget (hand edits, Claude Code itself).
        _watcher = new FileSystemWatcher(SettingsDirectory, "settings.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        _watcher.Changed += (_, _) => Changed?.Invoke();
        _watcher.Created += (_, _) => Changed?.Invoke();
        _watcher.Renamed += (_, _) => Changed?.Invoke();
        _watcher.EnableRaisingEvents = true;
    }

    public ClaudeModelChoice Read()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return ClaudeModelChoice.Opus; // no override - Claude Code's own default (Opus)

            var model = JsonNode.Parse(File.ReadAllText(SettingsPath))?["model"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(model) || model.Equals("default", StringComparison.OrdinalIgnoreCase))
                return ClaudeModelChoice.Opus;

            if (model.Contains("opus", StringComparison.OrdinalIgnoreCase))
                return ClaudeModelChoice.Opus;

            if (model.Contains("sonnet", StringComparison.OrdinalIgnoreCase))
                return ClaudeModelChoice.Sonnet;

            return ClaudeModelChoice.Unknown;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            // Mid-write by another process, or not valid JSON right now.
            return ClaudeModelChoice.Unknown;
        }
    }

    public void Write(ClaudeModelChoice choice)
    {
        var alias = choice switch
        {
            ClaudeModelChoice.Opus => OpusAlias,
            ClaudeModelChoice.Sonnet => SonnetAlias,
            _ => throw new ArgumentOutOfRangeException(nameof(choice)),
        };

        Directory.CreateDirectory(SettingsDirectory);

        var root = File.Exists(SettingsPath)
            ? JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject
              ?? throw new InvalidOperationException("settings.json is not a JSON object.")
            : new JsonObject();

        root["model"] = alias;

        // Write to a temp file and swap it in, so Claude Code never reads a
        // half-written settings.json.
        var tempPath = SettingsPath + ".widget.tmp";
        File.WriteAllText(tempPath, root.ToJsonString(WriteOptions));
        File.Move(tempPath, SettingsPath, overwrite: true);
    }

    public void Dispose() => _watcher?.Dispose();
}
