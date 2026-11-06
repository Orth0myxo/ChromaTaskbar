using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ChromaTaskbar.Core;

public sealed class TranslucentTbSettingsController : IDisposable
{
    private static readonly TimeSpan DefaultWriteInterval = TimeSpan.FromSeconds(1);
    private readonly string _settingsPath;
    private readonly string _backupDirectory;
    private readonly TimeSpan _writeInterval;
    private JsonObject? _settings;
    private byte[]? _originalBytes;
    private byte[]? _lastWrittenBytes;
    private DateTime? _lastWriteUtc;
    private bool _dirty;
    private bool _hasWrittenChanges;
    private bool _backupCreated;
    private bool _paused;
    private bool _disposed;

    public TranslucentTbSettingsController(
        string? settingsPath = null,
        string? backupDirectory = null,
        TimeSpan? writeInterval = null)
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _settingsPath = settingsPath ?? Path.Combine(
            localAppData,
            "Packages",
            "28017CharlesMilette.TranslucentTB_v826wp6bftszj",
            "RoamingState",
            "settings.json");
        _backupDirectory = backupDirectory ?? Path.Combine(localAppData, "ChromaTaskbar", "Backups");
        _writeInterval = writeInterval ?? DefaultWriteInterval;
    }

    public string? BackupPath { get; private set; }

    public bool IsInitialized => _settings is not null;

    public bool IsReady => IsInitialized && _backupCreated;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsReady)
        {
            return;
        }

        if (_settings is null)
        {
            _originalBytes = File.ReadAllBytes(_settingsPath);
            _settings = ParseSettings(_originalBytes);
            _lastWrittenBytes = _originalBytes;
        }

        Directory.CreateDirectory(_backupDirectory);
        BackupPath = Path.Combine(
            _backupDirectory,
            $"TranslucentTB.settings.{DateTime.UtcNow:yyyyMMddTHHmmssfffffffZ}.json");
        File.WriteAllBytes(BackupPath, _originalBytes!);
        _backupCreated = true;
    }

    public bool ApplyColor(RgbColor color)
    {
        EnsureCanWrite();
        string hex = $"{color.ToHex()}FF";
        SetAppearance("visible_window_appearance", enabled: true, hex);
        SetAppearance("maximized_window_appearance", enabled: true, hex);
        return TryWriteChanges();
    }

    public bool SetDesktopAppearance()
    {
        EnsureCanWrite();
        JsonObject desktop = GetAppearance("desktop_appearance");
        SetString(desktop, "accent", "clear");
        SetString(desktop, "color", "#00000000");
        SetAppearanceEnabled("visible_window_appearance", enabled: false);
        SetAppearanceEnabled("maximized_window_appearance", enabled: false);
        return TryWriteChanges();
    }

    public void Pause()
    {
        if (!IsReady || _paused || _disposed)
        {
            return;
        }

        RestoreOriginal();
        _paused = true;
    }

    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsReady || !_paused)
        {
            return;
        }

        _settings = ParseSettings(_originalBytes!);
        _lastWrittenBytes = _originalBytes;
        _dirty = false;
        _hasWrittenChanges = false;
        _paused = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!_paused)
        {
            RestoreOriginal();
        }

        _disposed = true;
    }

    private void EnsureCanWrite()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_settings is null)
        {
            throw new InvalidOperationException("Start the TranslucentTB settings controller before applying changes.");
        }

        if (!_backupCreated)
        {
            throw new InvalidOperationException("The original TranslucentTB settings backup is unavailable; settings were not changed.");
        }

        if (_paused)
        {
            throw new InvalidOperationException("The TranslucentTB settings controller is paused.");
        }
    }

    private static JsonObject ParseSettings(byte[] bytes)
    {
        string json = Encoding.UTF8.GetString(bytes);
        if (json.Length > 0 && json[0] == '\uFEFF')
        {
            json = json[1..];
        }

        JsonNode? parsed = JsonNode.Parse(
            json,
            nodeOptions: null,
            documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

        return parsed as JsonObject ?? throw new InvalidDataException(
            "TranslucentTB settings must contain a JSON object.");
    }

    private void SetAppearance(string name, bool enabled, string color)
    {
        JsonObject appearance = GetAppearance(name);
        SetBoolean(appearance, "enabled", enabled);
        SetString(appearance, "accent", "opaque");
        SetString(appearance, "color", color);
    }

    private void SetAppearanceEnabled(string name, bool enabled)
    {
        SetBoolean(GetAppearance(name), "enabled", enabled);
    }

    private JsonObject GetAppearance(string name)
    {
        if (_settings![name] is JsonObject appearance)
        {
            return appearance;
        }

        appearance = new JsonObject();
        _settings[name] = appearance;
        _dirty = true;
        return appearance;
    }

    private void SetBoolean(JsonObject appearance, string name, bool value)
    {
        if (appearance[name] is JsonValue current && current.TryGetValue(out bool existing) && existing == value)
        {
            return;
        }

        appearance[name] = value;
        _dirty = true;
    }

    private void SetString(JsonObject appearance, string name, string value)
    {
        if (appearance[name] is JsonValue current && current.TryGetValue(out string? existing) && existing == value)
        {
            return;
        }

        appearance[name] = value;
        _dirty = true;
    }

    private bool TryWriteChanges()
    {
        if (!_dirty)
        {
            return false;
        }

        DateTime now = DateTime.UtcNow;
        if (_lastWriteUtc is DateTime lastWrite && now - lastWrite < _writeInterval)
        {
            return false;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(_settings!.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        if (_lastWrittenBytes is not null && bytes.AsSpan().SequenceEqual(_lastWrittenBytes))
        {
            _dirty = false;
            return false;
        }

        WriteAtomically(bytes);
        _lastWriteUtc = now;
        _lastWrittenBytes = bytes;
        _dirty = false;
        _hasWrittenChanges = true;
        return true;
    }

    private void RestoreOriginal()
    {
        if (!_hasWrittenChanges || _originalBytes is null)
        {
            return;
        }

        if (_lastWriteUtc is DateTime lastWrite)
        {
            TimeSpan remaining = _writeInterval - (DateTime.UtcNow - lastWrite);
            if (remaining > TimeSpan.Zero)
            {
                Thread.Sleep(remaining);
            }
        }

        WriteAtomically(_originalBytes);
        _lastWriteUtc = DateTime.UtcNow;
        _lastWrittenBytes = _originalBytes;
        _dirty = false;
        _hasWrittenChanges = false;
    }

    private void WriteAtomically(byte[] bytes)
    {
        string temporaryPath = _settingsPath + ".chromataskbar.tmp";
        File.WriteAllBytes(temporaryPath, bytes);
        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }
}
