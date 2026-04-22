namespace Loupedeck.VoxRingPlugin.Services;

using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

/// <summary>
/// Downloads and installs the Piper TTS binary and voice models.
///
/// Mirrors the pattern of <see cref="ModelManager"/> (Whisper/Vosk):
/// canonical paths under %LOCALAPPDATA%\VoxRing\tts, idempotent "Ensure" methods, one HttpClient,
/// progress callbacks. Runs in the background during plugin startup; if any step fails, TTS silently
/// falls back to SAPI at call time.
///
/// Installed layout:
///   %LOCALAPPDATA%\VoxRing\tts\piper\piper.exe     (+ espeak-ng-data, DLLs, etc.)
///   %LOCALAPPDATA%\VoxRing\tts\voices\en_US-amy-medium.onnx (+ .onnx.json)
///   %LOCALAPPDATA%\VoxRing\tts\voices\de_DE-thorsten-medium.onnx (+ .onnx.json)
/// </summary>
public static class PiperInstaller
{
    // v1.2.0 dropped the Windows asset (Linux/macOS only). 2023.11.14-2 is the last release
    // that shipped piper_windows_amd64.zip. See https://github.com/rhasspy/piper/releases
    private const string PiperZipUrl =
        "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip";

    // Voice models (Piper's official HuggingFace repo, medium-quality tier)
    private const string EnOnnxUrl =
        "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/amy/medium/en_US-amy-medium.onnx";
    private const string EnJsonUrl =
        "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/amy/medium/en_US-amy-medium.onnx.json";
    private const string DeOnnxUrl =
        "https://huggingface.co/rhasspy/piper-voices/resolve/main/de/de_DE/thorsten/medium/de_DE-thorsten-medium.onnx";
    private const string DeJsonUrl =
        "https://huggingface.co/rhasspy/piper-voices/resolve/main/de/de_DE/thorsten/medium/de_DE-thorsten-medium.onnx.json";

    private const string EnVoiceLabel = "English (Amy, medium)";
    private const string DeVoiceLabel = "Deutsch (Thorsten, medium)";

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(30) };

    // ---------- Downloaded location (fallback if plugin is shipped without bundle) ----------
    public static string TtsRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoxRing", "tts");
    public static string PiperDir { get; } = Path.Combine(TtsRoot, "piper");
    public static string VoicesDir { get; } = Path.Combine(TtsRoot, "voices");
    public static string PiperExe { get; } = Path.Combine(PiperDir, "piper.exe");
    public static string EnVoicePath { get; } = Path.Combine(VoicesDir, "en_US-amy-medium.onnx");
    public static string DeVoicePath { get; } = Path.Combine(VoicesDir, "de_DE-thorsten-medium.onnx");

    // ---------- Bundled location (shipped inside the .lplug4 package) ----------
    // Resolved from the plugin's .link file at first access; null if we can't locate the plugin.
    private static readonly Lazy<string> _bundledRoot = new(ResolveBundledRoot);
    public static string BundledTtsRoot => _bundledRoot.Value;
    public static string BundledPiperDir => BundledTtsRoot == null ? null : Path.Combine(BundledTtsRoot, "piper");
    public static string BundledVoicesDir => BundledTtsRoot == null ? null : Path.Combine(BundledTtsRoot, "voices");
    public static string BundledPiperExe => BundledPiperDir == null ? null : Path.Combine(BundledPiperDir, "piper.exe");
    public static string BundledEnVoicePath => BundledVoicesDir == null ? null : Path.Combine(BundledVoicesDir, "en_US-amy-medium.onnx");
    public static string BundledDeVoicePath => BundledVoicesDir == null ? null : Path.Combine(BundledVoicesDir, "de_DE-thorsten-medium.onnx");

    private static bool FileExistsSafe(string path) => !string.IsNullOrEmpty(path) && File.Exists(path);

    public static bool IsBundledPiperPresent => FileExistsSafe(BundledPiperExe);
    public static bool IsBundledEnVoicePresent => FileExistsSafe(BundledEnVoicePath) && FileExistsSafe(BundledEnVoicePath + ".json");
    public static bool IsBundledDeVoicePresent => FileExistsSafe(BundledDeVoicePath) && FileExistsSafe(BundledDeVoicePath + ".json");

    public static bool IsDownloadedPiperPresent => File.Exists(PiperExe);
    public static bool IsDownloadedEnVoicePresent => File.Exists(EnVoicePath) && File.Exists(EnVoicePath + ".json");
    public static bool IsDownloadedDeVoicePresent => File.Exists(DeVoicePath) && File.Exists(DeVoicePath + ".json");

    // ---------- Aggregate view: true if either source has it ----------
    public static bool IsBinaryPresent => IsBundledPiperPresent || IsDownloadedPiperPresent;
    public static bool IsEnVoicePresent => IsBundledEnVoicePresent || IsDownloadedEnVoicePresent;
    public static bool IsDeVoicePresent => IsBundledDeVoicePresent || IsDownloadedDeVoicePresent;
    public static bool IsFullyInstalled => IsBinaryPresent && (IsEnVoicePresent || IsDeVoicePresent);

    // ---------- "Active" paths — prefer bundled so the demo works offline out of the box ----------
    public static string ActivePiperExe =>
        IsBundledPiperPresent ? BundledPiperExe :
        IsDownloadedPiperPresent ? PiperExe : null;

    public static string ActivePiperDir =>
        ActivePiperExe == null ? null : Path.GetDirectoryName(ActivePiperExe);

    public static string ActiveVoicesDir =>
        (IsBundledEnVoicePresent || IsBundledDeVoicePresent) ? BundledVoicesDir :
        (IsDownloadedEnVoicePresent || IsDownloadedDeVoicePresent) ? VoicesDir : null;

    public static string PiperBinarySource =>
        IsBundledPiperPresent ? "Bundled" :
        IsDownloadedPiperPresent ? "Downloaded" : "None";
    public static string EnVoiceSource =>
        IsBundledEnVoicePresent ? "Bundled" :
        IsDownloadedEnVoicePresent ? "Downloaded" : "None";
    public static string DeVoiceSource =>
        IsBundledDeVoicePresent ? "Bundled" :
        IsDownloadedDeVoicePresent ? "Downloaded" : "None";

    private static string ResolveBundledRoot()
    {
        try
        {
            var pluginsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Logi", "LogiPluginService", "Plugins");
            var linkFile = Path.Combine(pluginsDir, "VoxRingPlugin.link");
            if (!File.Exists(linkFile)) return null;

            var pluginBase = File.ReadAllText(linkFile).Trim();
            // CopyPackage target in the csproj drops src/package/tts/* into <pluginBase>/tts/*
            var ttsDir = Path.Combine(pluginBase, "tts");
            return Directory.Exists(ttsDir) ? ttsDir : null;
        }
        catch
        {
            return null;
        }
    }

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(PiperDir);
        Directory.CreateDirectory(VoicesDir);
    }

    public static async Task EnsureInstalledAsync(Action<string> onProgress = null)
    {
        try
        {
            EnsureDirectories();

            // Only download pieces that aren't already bundled or previously downloaded.
            if (!IsBinaryPresent)
                await DownloadPiperBinaryAsync(onProgress);

            if (!IsEnVoicePresent)
                await DownloadVoiceAsync(EnVoiceLabel, EnOnnxUrl, EnJsonUrl, EnVoicePath, onProgress);

            if (!IsDeVoicePresent)
                await DownloadVoiceAsync(DeVoiceLabel, DeOnnxUrl, DeJsonUrl, DeVoicePath, onProgress);

            if (IsFullyInstalled)
            {
                PluginLog.Info($"Piper ready (bin={PiperBinarySource}, en={EnVoiceSource}, de={DeVoiceSource})");
                onProgress?.Invoke("Piper TTS ready");
            }
            else
            {
                onProgress?.Invoke("Piper install incomplete");
            }
        }
        catch (Exception ex)
        {
            PluginLog.Warning(ex, "Piper install failed — will fall back to SAPI at call time");
            onProgress?.Invoke($"Install failed: {ex.Message}");
        }
    }

    private static async Task DownloadPiperBinaryAsync(Action<string> onProgress)
    {
        onProgress?.Invoke("Downloading Piper binary (~25 MB)...");
        PluginLog.Info($"Downloading Piper binary from {PiperZipUrl}");

        var zipPath = Path.Combine(PiperDir, "piper-download.zip");
        using (var response = await _http.GetAsync(PiperZipUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            using var fs = File.Create(zipPath);
            await response.Content.CopyToAsync(fs);
        }

        onProgress?.Invoke("Extracting Piper binary...");

        var tempExtract = Path.Combine(PiperDir, "extract-temp");
        if (Directory.Exists(tempExtract))
            Directory.Delete(tempExtract, true);
        ZipFile.ExtractToDirectory(zipPath, tempExtract);

        // Piper's zip extracts to a "piper/" subfolder; flatten its contents directly into PiperDir.
        var piperSubdir = Path.Combine(tempExtract, "piper");
        var sourceDir = Directory.Exists(piperSubdir) ? piperSubdir : tempExtract;

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(PiperDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }

        try { Directory.Delete(tempExtract, true); } catch { }
        try { File.Delete(zipPath); } catch { }

        if (!File.Exists(PiperExe))
            throw new FileNotFoundException($"Piper extraction completed but {PiperExe} not found");

        PluginLog.Info($"Piper binary installed at {PiperExe}");
    }

    private static async Task DownloadVoiceAsync(string label, string onnxUrl, string jsonUrl, string onnxPath, Action<string> onProgress)
    {
        onProgress?.Invoke($"Downloading voice: {label} (~63 MB)...");
        PluginLog.Info($"Downloading Piper voice {label} from {onnxUrl}");

        using (var response = await _http.GetAsync(onnxUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            using var fs = File.Create(onnxPath);
            await response.Content.CopyToAsync(fs);
        }

        using (var response = await _http.GetAsync(jsonUrl))
        {
            response.EnsureSuccessStatusCode();
            using var fs = File.Create(onnxPath + ".json");
            await response.Content.CopyToAsync(fs);
        }

        PluginLog.Info($"Piper voice installed: {onnxPath}");
    }
}
