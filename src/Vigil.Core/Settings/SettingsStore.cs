using System.Text.Json;
using System.Text.Json.Serialization;
using Vigil.Core.Diagnostics;

namespace Vigil.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON. Writes are atomic (temp file + replace) so a
/// crash or power loss can never leave a half-written settings file. A corrupt file is preserved as
/// <c>settings.json.bad</c> and defaults are used.
/// </summary>
public sealed class SettingsStore
{
    private const string LogCategory = "Settings";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();

    public SettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
    }

    public string FilePath { get; }

    public AppSettings Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return new AppSettings().Normalize();
                }

                string json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                return (settings ?? new AppSettings()).Normalize();
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            {
                Log.Warn(LogCategory, $"Settings file is invalid; using defaults. A copy was kept as {Path.GetFileName(FilePath)}.bad", ex);
                TryBackupCorrupt();
                return new AppSettings().Normalize();
            }
            catch (IOException ex)
            {
                Log.Warn(LogCategory, "Settings file could not be read; using defaults.", ex);
                return new AppSettings().Normalize();
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Warn(LogCategory, "Settings file is not accessible; using defaults.", ex);
                return new AppSettings().Normalize();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string json = Serialize(settings);
        lock (_gate)
        {
            string? directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(FilePath))
            {
                File.Replace(temp, FilePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temp, FilePath);
            }
        }
    }

    public static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, JsonOptions);

    public static AppSettings Deserialize(string json) =>
        (JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings()).Normalize();

    /// <summary>Deep copy via JSON round-trip.</summary>
    public static AppSettings Clone(AppSettings settings) => Deserialize(Serialize(settings));

    private void TryBackupCorrupt()
    {
        try
        {
            File.Copy(FilePath, FilePath + ".bad", overwrite: true);
        }
        catch
        {
            // best effort
        }
    }
}
