namespace Loupedeck.VoxRingPlugin.Services;

using System.Text.Json;
using NAudio.Wave;
using Vosk;

public sealed class VoskRecognitionService : IDisposable
{
    private Model _model;
    private bool _disposed;

    public bool IsModelLoaded => _model != null;

    public void LoadModel(string modelPath)
    {
        if (_model != null)
            return;

        if (!Directory.Exists(modelPath))
        {
            PluginLog.Error($"Vosk model not found at: {modelPath}");
            return;
        }

        // SetLogLevel(-1) suppresses Vosk's extremely verbose native stdout output.
        // Without this, Logi Plugin Service logs fill with Vosk model-load diagnostics.
        global::Vosk.Vosk.SetLogLevel(-1);
        _model = new Model(modelPath);
        PluginLog.Info($"Vosk model loaded from: {modelPath}");
    }

    public string RecognizeFromWav(byte[] wavData)
    {
        if (wavData == null || wavData.Length == 0)
            return string.Empty;

        if (_model == null)
        {
            PluginLog.Warning("Vosk model not loaded - skipping recognition");
            return string.Empty;
        }

        try
        {
            using var stream = new MemoryStream(wavData);
            using var reader = new WaveFileReader(stream);
            using var rec = new VoskRecognizer(_model, reader.WaveFormat.SampleRate);

            var buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                // AcceptWaveform returns true when Vosk has a hypothesis ready; we discard interim
                // results and call FinalResult() once after the full audio is fed.
                // Future: use PartialResult() inside this loop for live transcription display.
                rec.AcceptWaveform(buffer, bytesRead);
            }

            var resultJson = rec.FinalResult();
            return ExtractText(resultJson);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Vosk recognition failed: {ex.Message}");
            return string.Empty;
        }
    }

    private static string ExtractText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("text", out var text)
                ? text.GetString()?.Trim() ?? string.Empty
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _model?.Dispose();
        _model = null;
    }
}
