namespace Loupedeck.VoxRingPlugin.Services;

using System.Runtime.InteropServices;
using Whisper.net;

public sealed class WhisperRecognitionService : IDisposable
{
    private WhisperFactory _factory;
    private bool _disposed;

    public bool IsModelLoaded => _factory != null;

    private string _loadedModelPath;

    public void LoadModel(string modelPath)
    {
        // Skip if same model already loaded
        if (_factory != null && _loadedModelPath == modelPath)
            return;

        // Dispose previous factory if switching models
        if (_factory != null)
        {
            PluginLog.Info($"Unloading previous Whisper model: {_loadedModelPath}");
            _factory.Dispose();
            _factory = null;
            _loadedModelPath = null;
        }

        if (!File.Exists(modelPath))
        {
            PluginLog.Error($"Whisper model not found at: {modelPath}");
            return;
        }

        try
        {
            // RuntimeOptions.LibraryPath must be set by the caller (VoxRingPlugin.OnLoad) before
            // this call. WhisperFactory initializes the native whisper.dll on first use; if the
            // path is wrong it throws a DllNotFoundException that is not caught here.
            _factory = WhisperFactory.FromPath(modelPath);
            _loadedModelPath = modelPath;
            PluginLog.Info($"Whisper model loaded from: {modelPath}");
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Whisper model load failed: {ex.Message}");
        }
    }

    public async Task<string> RecognizeFromWavAsync(byte[] wavData)
    {
        if (wavData == null || wavData.Length == 0)
            return string.Empty;

        if (_factory == null)
        {
            PluginLog.Warning("Whisper model not loaded - skipping recognition");
            return string.Empty;
        }

        try
        {
            // Pass the WAV data directly - Whisper.net ProcessAsync accepts WAV streams
            using var wavStream = new MemoryStream(wavData);

            var language = Models.VoxRingState.SelectedLanguage;
            var builder = _factory.CreateBuilder()
                .WithThreads(Environment.ProcessorCount)
                // WithPrompt biases Whisper toward literal transcription and reduces hallucinated
                // punctuation or filler phrases ("Thank you for watching.") on short recordings.
                // Future: make this user-configurable for domain-specific vocabulary (names, jargon).
                .WithPrompt("VoxRing voice transcription. Transcribe exactly what was said.");

            // "auto" language detection is accurate but noticeably slower; specifying en/de skips
            // the detection step. "auto" is kept as the fallback so other languages still work.
            if (language == "en")
                builder.WithLanguage("en");
            else if (language == "de")
                builder.WithLanguage("de");
            else
                builder.WithLanguage("auto");

            using var processor = builder.Build();

            var segments = new List<string>();
            string detectedLang = null;
            await foreach (var segment in processor.ProcessAsync(wavStream))
            {
                // Language tag comes from the first segment; subsequent segments inherit it.
                // Storing it in VoxRingState lets DictateAction flash the detected language on the button.
                if (detectedLang == null && !string.IsNullOrEmpty(segment.Language))
                    detectedLang = segment.Language;
                segments.Add(segment.Text);
            }

            Models.VoxRingState.DetectedLanguage = detectedLang;
            return string.Join(" ", segments).Trim();
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Whisper recognition failed: {ex.Message}");
            return string.Empty;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _factory?.Dispose();
        _factory = null;
    }
}
