namespace Loupedeck.VoxRingPlugin.Services;

using Loupedeck.VoxRingPlugin.Models;

// Facade: both engines are always instantiated but neither loads its model until LoadModel is called,
// so startup is cheap regardless of which engine the user picks. Whisper is preferred when loaded;
// Vosk is the fallback — chosen for speed (40MB, synchronous) when the Whisper model is missing.
// Future: add partial-result streaming to the interface for live transcription display during recording.
public sealed class SpeechRecognitionService : IDisposable
{
    private readonly VoskRecognitionService _vosk = new();
    private readonly WhisperRecognitionService _whisper = new();
    private bool _disposed;

    public bool IsVoskLoaded => _vosk.IsModelLoaded;
    public bool IsWhisperLoaded => _whisper.IsModelLoaded;

    public void LoadVoskModel(string modelPath) => _vosk.LoadModel(modelPath);
    public void LoadWhisperModel(string modelPath) => _whisper.LoadModel(modelPath);

    public async Task<string> RecognizeFromWavAsync(byte[] wavData)
    {
        var engine = VoxRingState.SelectedEngine;

        if (engine == SpeechEngine.Whisper && _whisper.IsModelLoaded)
        {
            PluginLog.Info("Using Whisper engine");
            return await _whisper.RecognizeFromWavAsync(wavData);
        }

        if (_vosk.IsModelLoaded)
        {
            if (engine == SpeechEngine.Whisper)
                PluginLog.Warning("Whisper model not loaded - falling back to Vosk");

            PluginLog.Info("Using Vosk engine");
            return await Task.Run(() => _vosk.RecognizeFromWav(wavData));
        }

        PluginLog.Warning("No speech engine loaded - skipping recognition");
        return string.Empty;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _vosk.Dispose();
        _whisper.Dispose();
    }
}
