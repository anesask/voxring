namespace Loupedeck.VoxRingPlugin.Actions;

using System.Runtime.Versioning;
using System.Text;
using Loupedeck.VoxRingPlugin.Models;
using Loupedeck.VoxRingPlugin.Services;
using Loupedeck.VoxRingPlugin.Services.Ai;

[SupportedOSPlatform("windows")]
public class VoiceAssistantAction : PluginDynamicCommand
{
    private new VoxRingPlugin Plugin => base.Plugin as VoxRingPlugin;

    private const int MinRecordingMs = 150;
    // 6 exchanges = 12 messages in context. Enough for coherent back-and-forth without growing the
    // prompt large enough to measurably slow down the first-token latency on edge API accounts.
    private const int MaxHistoryTurns = 6;

    private readonly List<ChatMessage> _history = new();
    private DateTime _recordingStartUtc;
    private DateTime _lastUsedUtc = DateTime.MinValue;
    private bool _isThinking;
    private bool _isSpeaking;

    private static readonly TimeSpan IdleResetTimeout = TimeSpan.FromMinutes(5);

    private static readonly string SystemPrompt =
        "You are a helpful voice assistant built into a mouse. " +
        "Respond in 1-3 concise sentences — the response will be read aloud via text-to-speech. " +
        "Be direct and conversational. Respond in the same language the user speaks.";

    public VoiceAssistantAction()
        : base("Voice Assistant", "Talk to AI built into your mouse. Tap to record, tap again for a spoken reply. Tap while speaking to stop.", "1 Voice")
    {
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
    {
        try { return PluginResources.ReadImage("bot.svg"); }
        catch { return base.GetCommandImage(actionParameter, imageSize); }
    }

    protected override async void RunCommand(String actionParameter)
    {
        // Tap while speaking: stop TTS immediately
        if (_isSpeaking)
        {
            TtsService.Instance.Stop();
            _isSpeaking = false;
            this.ActionImageChanged();
            return;
        }

        if (VoxRingState.IsProcessing || _isThinking) return;

        // Tap while another action is recording — don't interfere
        if (VoxRingState.IsRecording && VoxRingState.CurrentRecordingMode != RecordingMode.VoiceAssistant)
        {
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
            return;
        }

        if (!VoxRingState.IsRecording)
            StartRecording();
        else
            await StopAndRespondAsync();
    }

    private void StartRecording()
    {
        // Auto-reset history after 5 minutes of inactivity
        if (_history.Count > 0 && DateTime.UtcNow - _lastUsedUtc > IdleResetTimeout)
        {
            _history.Clear();
            PluginLog.Info("VoiceAssistant: conversation reset after idle timeout");
        }

        _isSpeaking = false;
        TtsService.Instance.Stop(); // stop any previous response

        Plugin.AudioRecorder.StartRecording();
        VoxRingState.IsRecording = true;
        VoxRingState.CurrentRecordingMode = RecordingMode.VoiceAssistant;
        _recordingStartUtc = DateTime.UtcNow;
        Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticRecordStart);
        this.ActionImageChanged();
    }

    private async Task StopAndRespondAsync()
    {
        var elapsedMs = (DateTime.UtcNow - _recordingStartUtc).TotalMilliseconds;
        if (elapsedMs < MinRecordingMs)
        {
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
            return;
        }

        var wavData = Plugin.AudioRecorder.StopRecording();
        VoxRingState.IsRecording = false;
        VoxRingState.CurrentRecordingMode = RecordingMode.None;
        VoxRingState.IsProcessing = true;
        _isThinking = true;
        this.ActionImageChanged();

        try
        {
            // 1. Transcribe
            var transcript = await Plugin.SpeechRecognition.RecognizeFromWavAsync(wavData);
            PluginLog.Info($"VoiceAssistant transcript: {transcript}");

            if (string.IsNullOrWhiteSpace(transcript))
            {
                _isThinking = false;
                VoxRingState.IsProcessing = false;
                this.ActionImageChanged();
                return;
            }

            // 2. AI response via current provider
            var provider = AiProviderRegistry.Current;
            if (!provider.IsAvailable)
            {
                PluginLog.Warning("VoiceAssistant: no AI provider configured");
                _isThinking = false;
                VoxRingState.IsProcessing = false;
                this.ActionImageChanged();
                TtsService.Instance.Speak("No AI provider configured. Please add an API key in settings.");
                return;
            }

            var response = await provider.ReformatAsync(transcript, BuildConversationalPrompt(), VoxRingState.SelectedLanguage);
            PluginLog.Info($"VoiceAssistant response: {response}");

            // 3. Update conversation history
            _history.Add(new ChatMessage { Role = "user", Content = transcript });
            _history.Add(new ChatMessage { Role = "assistant", Content = response });
            while (_history.Count > MaxHistoryTurns * 2)
                _history.RemoveAt(0);

            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticTranscriptionComplete);
            _lastUsedUtc = DateTime.UtcNow;

            // 4. Speak
            _isThinking = false;
            _isSpeaking = true;
            this.ActionImageChanged();

            TtsService.Instance.Speak(response, VoxRingState.SelectedLanguage);

            // Piper TTS doesn't expose audio duration or a completion callback.
            // Estimated at ~10 chars/sec plus a 3s safety buffer; the user can also tap to interrupt.
            // Future: Piper's subprocess stdout could emit a done signal; that would replace this estimate.
            var estimatedMs = (response.Length / 10.0) * 1000 + 3000;
            _ = Task.Delay(Math.Max((int)estimatedMs, 5000)).ContinueWith(_ =>
            {
                if (_isSpeaking) { _isSpeaking = false; this.ActionImageChanged(); }
            });
        }
        catch (Exception ex)
        {
            PluginLog.Error($"VoiceAssistant error: {ex.Message}");
            _isThinking = false;
            _isSpeaking = false;
        }
        finally
        {
            VoxRingState.IsProcessing = false;
            this.ActionImageChanged();
        }
    }

    // IAiProvider.ReformatAsync is single-turn: one system prompt + one user message.
    // Multi-turn history is encoded into the system prompt as a transcript block rather than
    // real chat messages. This is a workaround — the AI provider interface would need a proper
    // ChatAsync(List<ChatMessage>) overload to support native multi-turn conversation.
    // Future: add ChatAsync to IAiProvider, remove this encoding, enable streaming responses.
    private string BuildConversationalPrompt()
    {
        if (_history.Count == 0)
            return SystemPrompt;

        var sb = new StringBuilder(SystemPrompt);
        sb.AppendLine("\n\nConversation so far:");
        foreach (var msg in _history)
            sb.AppendLine($"{(msg.Role == "user" ? "User" : "Assistant")}: {msg.Content}");
        return sb.ToString();
    }

    protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
    {
        if (VoxRingState.IsRecording && VoxRingState.CurrentRecordingMode == RecordingMode.VoiceAssistant)
            return "Listening";
        if (_isThinking)
            return "Thinking...";
        if (_isSpeaking)
            return "Speaking";
        if (_history.Count > 0)
            return $"{_history.Count / 2} turns";
        return string.Empty;
    }
}
