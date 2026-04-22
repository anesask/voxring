namespace Loupedeck.VoxRingPlugin
{
    using System;
    using System.IO;
    using Loupedeck.VoxRingPlugin.Destinations;
    using Loupedeck.VoxRingPlugin.Helpers;
    using Loupedeck.VoxRingPlugin.Models;
    using Loupedeck.VoxRingPlugin.Services;

    public class VoxRingPlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => true;
        public override Boolean HasNoApplication => true;

        // Haptic event names - must match eventMapping.yaml
        public const string HapticRecordStart = "record_start";
        public const string HapticRecordPulse = "record_pulse";
        public const string HapticRecordAutoStop = "record_auto_stop";
        public const string HapticTranscriptionComplete = "transcription_complete";
        public const string HapticDialTick = "dial_tick";
        public const string HapticSendSuccess = "send_success";
        public const string HapticSendFailure = "send_failure";

        // Plugin settings keys
        private const string SettingSpeechEngine = "SpeechEngine";
        private const string SettingLanguage = "Language";
        private const string SettingMicrophoneDevice = "MicrophoneDevice";
        private const string SettingMaxRecordingSeconds = "MaxRecordingSeconds";
        private const string SettingWhisperModelSize = "WhisperModelSize";
        private const string SettingAiProvider = "AiProvider";
        private const string SettingClaudeApiKey = "ClaudeApiKey";
        private const string SettingOpenAiApiKey = "OpenAiApiKey";
        private const string SettingGeminiApiKey = "GeminiApiKey";
        private const string SettingDeepSeekApiKey = "DeepSeekApiKey";
        private const string SettingPerplexityApiKey = "PerplexityApiKey";
        private const string SettingSlackWebhookUrl = "SlackWebhookUrl";
        private const string SettingDiscordWebhookUrl = "DiscordWebhookUrl";
        private const string SettingVoiceNoteSavePath = "VoiceNoteSavePath";
        private const string SettingVoiceNoteFilenamePattern = "VoiceNoteFilenamePattern";
        private const string SettingUseNoiseGate = "UseNoiseGate";
        private const string SettingUseAi = "UseAi";
        private const string SettingCustomPromptPrefix = "CustomPrompt_";
        private const string SettingTranslateTargetLanguage = "TranslateTargetLanguage";
        private const string SettingUseFillerWordCleaner = "UseFillerWordCleaner";
        private const string SettingCaseTransform = "CaseTransform";


        public AudioRecorderService AudioRecorder { get; private set; }
        public SpeechRecognitionService SpeechRecognition { get; private set; }
        public ClaudeApiService ClaudeApi { get; private set; }

        public VoxRingPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        public override void Load()
        {
            AudioRecorder = new AudioRecorderService();
            SpeechRecognition = new SpeechRecognitionService();
            ClaudeApi = new ClaudeApiService();
            Services.Ai.AiProviderRegistry.Initialize(ClaudeApi);

            // Load speech models from %LOCALAPPDATA%\VoxRing\models\
            // Auto-downloads on first run if models are missing.
            LoadModelsAsync();

            // Register destinations (Windows-only - this plugin only targets Windows)
            if (OperatingSystem.IsWindows())
            {
                DestinationRegistry.Register(new ClipboardDestination());
                DestinationRegistry.Register(new TypeOutDestination());
            }

            DestinationRegistry.Register(new EmailDestination());
            DestinationRegistry.Register(new SlackWebhookDestination());
            DestinationRegistry.Register(new DiscordWebhookDestination());
            DestinationRegistry.Register(new TeamsDestination());
            DestinationRegistry.Register(new CalendarDestination());
            DestinationRegistry.Register(new WhatsAppDestination());
            DestinationRegistry.Register(new NotionDestination());
            DestinationRegistry.Register(new TelegramDestination());

            // Restore persisted settings
            LoadSettings();

            // Start web settings service
            WebSettingsService.Instance.Initialize(this);
            WebSettingsService.Instance.Start();

            this.PluginEvents.AddEvent(HapticRecordStart, "Record Start", "Haptic buzz when recording starts");
            this.PluginEvents.AddEvent(HapticRecordPulse, "Record Pulse", "Haptic pulse while recording is active");
            this.PluginEvents.AddEvent(HapticRecordAutoStop, "Record Auto-Stop", "Haptic when recording hits time limit");
            this.PluginEvents.AddEvent(HapticTranscriptionComplete, "Transcription Complete", "Haptic pulse when transcript is ready");
            this.PluginEvents.AddEvent(HapticDialTick, "Dial Tick", "Haptic tick on each dial step");
            this.PluginEvents.AddEvent(HapticSendSuccess, "Send Success", "Haptic confirmation on successful send");
            this.PluginEvents.AddEvent(HapticSendFailure, "Send Failure", "Haptic alert on send failure");

            PluginLog.Info("VoxRing plugin loaded");
        }

        public override void Unload()
        {
            WebSettingsService.Instance?.Dispose();
            AudioRecorder?.Dispose();
            AudioRecorder = null;
            SpeechRecognition?.Dispose();
            SpeechRecognition = null;
            ClaudeApi?.Dispose();
            ClaudeApi = null;
            PluginLog.Info("VoxRing plugin unloaded");
        }

        private void LoadSettings()
        {
            if (this.TryGetPluginSetting(SettingSpeechEngine, out var engineValue)
                && Enum.TryParse<SpeechEngine>(engineValue, out var engine))
            {
                VoxRingState.SelectedEngine = engine;
                PluginLog.Info($"Loaded speech engine setting: {engine}");
            }

            if (this.TryGetPluginSetting(SettingLanguage, out var langValue)
                && !string.IsNullOrEmpty(langValue))
            {
                VoxRingState.SelectedLanguage = langValue;
                PluginLog.Info($"Loaded language setting: {langValue}");
            }

            if (this.TryGetPluginSetting(SettingMicrophoneDevice, out var micValue)
                && int.TryParse(micValue, out var micIndex))
            {
                VoxRingState.SelectedMicrophoneIndex = micIndex;
                PluginLog.Info($"Loaded microphone device setting: {micIndex}");
            }

            if (this.TryGetPluginSetting(SettingMaxRecordingSeconds, out var maxRecValue)
                && int.TryParse(maxRecValue, out var maxRec)
                && maxRec is >= 5 and <= 300)
            {
                VoxRingState.MaxRecordingSeconds = maxRec;
                PluginLog.Info($"Loaded max recording duration: {maxRec}s");
            }

            if (this.TryGetPluginSetting(SettingWhisperModelSize, out var modelSizeValue)
                && Enum.TryParse<WhisperModelSize>(modelSizeValue, out var modelSize))
            {
                VoxRingState.SelectedWhisperModel = modelSize;
                PluginLog.Info($"Loaded Whisper model size: {modelSize}");
            }

            if (this.TryGetPluginSetting(SettingAiProvider, out var providerStr)
                && Enum.TryParse<AiProvider>(providerStr, out var provider))
            {
                VoxRingState.SelectedAiProvider = provider;
                PluginLog.Info($"Loaded AI provider: {provider}");
            }

            LoadEncryptedSetting(SettingClaudeApiKey, "Claude API key",
                v => VoxRingState.ClaudeApiKey = v, SaveClaudeApiKey);
            LoadEncryptedSetting(SettingOpenAiApiKey, "OpenAI API key",
                v => VoxRingState.OpenAiApiKey = v, SaveOpenAiApiKey);
            LoadEncryptedSetting(SettingGeminiApiKey, "Gemini API key",
                v => VoxRingState.GeminiApiKey = v, SaveGeminiApiKey);
            LoadEncryptedSetting(SettingDeepSeekApiKey, "DeepSeek API key",
                v => VoxRingState.DeepSeekApiKey = v, SaveDeepSeekApiKey);
            LoadEncryptedSetting(SettingPerplexityApiKey, "Perplexity API key",
                v => VoxRingState.PerplexityApiKey = v, SavePerplexityApiKey);
            LoadEncryptedSetting(SettingSlackWebhookUrl, "Slack webhook URL",
                v => VoxRingState.SlackWebhookUrl = v, SaveSlackWebhookUrl);
            LoadEncryptedSetting(SettingDiscordWebhookUrl, "Discord webhook URL",
                v => VoxRingState.DiscordWebhookUrl = v, SaveDiscordWebhookUrl);

            if (this.TryGetPluginSetting(SettingVoiceNoteSavePath, out var vnPath)
                && !string.IsNullOrEmpty(vnPath))
            {
                VoxRingState.VoiceNoteSavePath = vnPath;
                PluginLog.Info($"Loaded Voice Note save path: {vnPath}");
            }

            if (this.TryGetPluginSetting(SettingVoiceNoteFilenamePattern, out var vnPattern)
                && !string.IsNullOrEmpty(vnPattern))
            {
                VoxRingState.VoiceNoteFilenamePattern = vnPattern;
                PluginLog.Info($"Loaded Voice Note filename pattern: {vnPattern}");
            }

            if (this.TryGetPluginSetting(SettingUseNoiseGate, out var useNgStr))
            {
                VoxRingState.UseNoiseGate = useNgStr != "false";
                PluginLog.Info($"Loaded UseNoiseGate: {VoxRingState.UseNoiseGate}");
            }

            if (this.TryGetPluginSetting(SettingUseAi, out var useAiStr))
            {
                VoxRingState.UseAi = useAiStr != "false";
                PluginLog.Info($"Loaded UseAi: {VoxRingState.UseAi}");
            }

            if (this.TryGetPluginSetting(SettingTranslateTargetLanguage, out var translateStr))
            {
                VoxRingState.TranslateTargetLanguage = translateStr ?? "";
                PluginLog.Info($"Loaded TranslateTargetLanguage: {VoxRingState.TranslateTargetLanguage}");
            }

            if (this.TryGetPluginSetting(SettingUseFillerWordCleaner, out var fillerStr))
            {
                VoxRingState.UseFillerWordCleaner = fillerStr == "true";
                PluginLog.Info($"Loaded UseFillerWordCleaner: {VoxRingState.UseFillerWordCleaner}");
            }

            if (this.TryGetPluginSetting(SettingCaseTransform, out var caseStr)
                && Enum.TryParse<CaseTransform>(caseStr, out var caseVal))
            {
                VoxRingState.SelectedCaseTransform = caseVal;
                PluginLog.Info($"Loaded CaseTransform: {caseVal}");
            }

            foreach (var dest in Destinations.DestinationRegistry.All)
            {
                var key = SettingCustomPromptPrefix + dest.Name;
                if (this.TryGetPluginSetting(key, out var prompt) && !string.IsNullOrEmpty(prompt))
                {
                    VoxRingState.CustomPrompts[dest.Name] = prompt;
                    PluginLog.Info($"Loaded custom prompt for {dest.Name}");
                }
            }
        }

        public void SaveSpeechEngine(SpeechEngine engine)
        {
            VoxRingState.SelectedEngine = engine;
            this.SetPluginSetting(SettingSpeechEngine, engine.ToString(), false);
            PluginLog.Info($"Saved speech engine setting: {engine}");
        }

        public void SaveLanguage(string language)
        {
            VoxRingState.SelectedLanguage = language;
            this.SetPluginSetting(SettingLanguage, language, false);
            PluginLog.Info($"Saved language setting: {language}");
        }

        public void SaveMicrophoneDevice(int deviceIndex)
        {
            VoxRingState.SelectedMicrophoneIndex = deviceIndex;
            this.SetPluginSetting(SettingMicrophoneDevice, deviceIndex.ToString(), false);
            PluginLog.Info($"Saved microphone device setting: {deviceIndex}");
        }

        public void SaveMaxRecordingSeconds(int seconds)
        {
            VoxRingState.MaxRecordingSeconds = seconds;
            this.SetPluginSetting(SettingMaxRecordingSeconds, seconds.ToString(), false);
            PluginLog.Info($"Saved max recording duration: {seconds}s");
        }

        public void SaveWhisperModelSize(WhisperModelSize size)
        {
            VoxRingState.SelectedWhisperModel = size;
            this.SetPluginSetting(SettingWhisperModelSize, size.ToString(), false);
            PluginLog.Info($"Saved Whisper model size: {size}");
        }

        public void SaveClaudeApiKey(string key)
        {
            VoxRingState.ClaudeApiKey = key;
            this.SetPluginSetting(SettingClaudeApiKey, SecureStore.Protect(key), false);
            PluginLog.Info("Saved Claude API key (encrypted)");
        }

        public void SaveOpenAiApiKey(string key)
        {
            VoxRingState.OpenAiApiKey = key;
            this.SetPluginSetting(SettingOpenAiApiKey, SecureStore.Protect(key), false);
            PluginLog.Info("Saved OpenAI API key (encrypted)");
        }

        public void SaveGeminiApiKey(string key)
        {
            VoxRingState.GeminiApiKey = key;
            this.SetPluginSetting(SettingGeminiApiKey, SecureStore.Protect(key), false);
            PluginLog.Info("Saved Gemini API key (encrypted)");
        }

        public void SaveDeepSeekApiKey(string key)
        {
            VoxRingState.DeepSeekApiKey = key;
            this.SetPluginSetting(SettingDeepSeekApiKey, SecureStore.Protect(key), false);
            PluginLog.Info("Saved DeepSeek API key (encrypted)");
        }

        public void SavePerplexityApiKey(string key)
        {
            VoxRingState.PerplexityApiKey = key;
            this.SetPluginSetting(SettingPerplexityApiKey, SecureStore.Protect(key), false);
            PluginLog.Info("Saved Perplexity API key (encrypted)");
        }

        public void SaveAiProvider(AiProvider provider)
        {
            VoxRingState.SelectedAiProvider = provider;
            this.SetPluginSetting(SettingAiProvider, provider.ToString(), false);
            PluginLog.Info($"Saved AI provider: {provider}");
        }

        public void SaveSlackWebhookUrl(string url)
        {
            VoxRingState.SlackWebhookUrl = url;
            this.SetPluginSetting(SettingSlackWebhookUrl, SecureStore.Protect(url), false);
            PluginLog.Info("Saved Slack webhook URL (encrypted)");
        }

        public void SaveDiscordWebhookUrl(string url)
        {
            VoxRingState.DiscordWebhookUrl = url;
            this.SetPluginSetting(SettingDiscordWebhookUrl, SecureStore.Protect(url), false);
            PluginLog.Info("Saved Discord webhook URL (encrypted)");
        }

        /// <summary>
        /// Loads a DPAPI-encrypted setting, decrypts, applies it to state, and if the stored
        /// value was legacy plaintext (from before the encryption upgrade), re-saves it
        /// encrypted via the provided save delegate — transparent one-time migration per key.
        /// </summary>
        private void LoadEncryptedSetting(string settingKey, string label, Action<string> apply, Action<string> resaveEncrypted)
        {
            if (!this.TryGetPluginSetting(settingKey, out var stored) || string.IsNullOrEmpty(stored))
                return;

            var wasLegacy = SecureStore.IsLegacyPlaintext(stored);
            var plaintext = SecureStore.Unprotect(stored);
            if (string.IsNullOrEmpty(plaintext))
            {
                PluginLog.Warning($"{label}: stored value could not be decrypted (different user, corrupt). User must re-enter.");
                return;
            }

            apply(plaintext);
            if (wasLegacy)
            {
                PluginLog.Info($"{label}: migrating legacy plaintext to encrypted form");
                resaveEncrypted(plaintext);
            }
            else
            {
                PluginLog.Info($"Loaded {label} (encrypted)");
            }
        }

        public void SaveVoiceNoteSavePath(string path)
        {
            VoxRingState.VoiceNoteSavePath = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
            this.SetPluginSetting(SettingVoiceNoteSavePath, VoxRingState.VoiceNoteSavePath ?? string.Empty, false);
            PluginLog.Info($"Saved Voice Note save path: {VoxRingState.VoiceNoteSavePath ?? "(default)"}");
        }

        public void SaveVoiceNoteFilenamePattern(string pattern)
        {
            VoxRingState.VoiceNoteFilenamePattern = string.IsNullOrWhiteSpace(pattern) ? null : pattern.Trim();
            this.SetPluginSetting(SettingVoiceNoteFilenamePattern, VoxRingState.VoiceNoteFilenamePattern ?? string.Empty, false);
            PluginLog.Info($"Saved Voice Note filename pattern: {VoxRingState.VoiceNoteFilenamePattern ?? "(default)"}");
        }

        public void SaveUseNoiseGate(bool value)
        {
            VoxRingState.UseNoiseGate = value;
            this.SetPluginSetting(SettingUseNoiseGate, value ? "true" : "false", false);
            PluginLog.Info($"Saved UseNoiseGate: {value}");
        }

        public void SaveUseAi(bool value)
        {
            VoxRingState.UseAi = value;
            this.SetPluginSetting(SettingUseAi, value ? "true" : "false", false);
            PluginLog.Info($"Saved UseAi: {value}");
        }

        public void SaveTranslateTargetLanguage(string langCode)
        {
            VoxRingState.TranslateTargetLanguage = langCode ?? "";
            this.SetPluginSetting(SettingTranslateTargetLanguage, VoxRingState.TranslateTargetLanguage, false);
            PluginLog.Info($"Saved TranslateTargetLanguage: {VoxRingState.TranslateTargetLanguage}");
        }

        public void SaveUseFillerWordCleaner(bool value)
        {
            VoxRingState.UseFillerWordCleaner = value;
            this.SetPluginSetting(SettingUseFillerWordCleaner, value ? "true" : "false", false);
            PluginLog.Info($"Saved UseFillerWordCleaner: {value}");
        }

        public void SaveCaseTransform(CaseTransform value)
        {
            VoxRingState.SelectedCaseTransform = value;
            this.SetPluginSetting(SettingCaseTransform, value.ToString(), false);
            PluginLog.Info($"Saved CaseTransform: {value}");
        }

        public void SaveCustomPrompt(string destinationName, string prompt)
        {
            var trimmed = string.IsNullOrWhiteSpace(prompt) ? string.Empty : prompt.Trim();
            if (string.IsNullOrEmpty(trimmed))
                VoxRingState.CustomPrompts.Remove(destinationName);
            else
                VoxRingState.CustomPrompts[destinationName] = trimmed;
            this.SetPluginSetting(SettingCustomPromptPrefix + destinationName, trimmed, false);
            PluginLog.Info($"Saved custom prompt for {destinationName}: {(string.IsNullOrEmpty(trimmed) ? "(cleared)" : "set")}");
        }

        private string GetPluginBinDir()
        {
            var pluginsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Logi", "LogiPluginService", "Plugins");
            var linkFile = Path.Combine(pluginsDir, "VoxRingPlugin.link");
            var pluginBase = File.ReadAllText(linkFile).Trim();
            return Path.Combine(pluginBase, "bin");
        }

        private async void LoadModelsAsync()
        {
            try
            {
                PluginLog.Info($"Models directory: {ModelManager.ModelsBaseDir}");
                PluginLog.Info($"Vosk present: {ModelManager.IsVoskPresent}, Whisper present: {ModelManager.IsWhisperPresent}");

                // Set Whisper native lib path early (must be before any WhisperFactory)
                var binDir = GetPluginBinDir();
                Whisper.net.LibraryLoader.RuntimeOptions.LibraryPath = Path.Combine(binDir, "whisper.dll");
                PluginLog.Info($"Whisper native lib: {Path.Combine(binDir, "whisper.dll")}");

                // Load already-present models immediately (so recording works while downloads run)
                if (ModelManager.IsVoskPresent)
                    SpeechRecognition.LoadVoskModel(ModelManager.VoskModelPath);
                if (ModelManager.IsWhisperPresent)
                    SpeechRecognition.LoadWhisperModel(ModelManager.WhisperModelPath);

                PluginLog.Info($"Initial load - Vosk: {SpeechRecognition.IsVoskLoaded}, Whisper: {SpeechRecognition.IsWhisperLoaded}");

                // Fire Piper TTS installer in parallel — doesn't block speech availability,
                // and TTS gracefully falls back to SAPI if Piper isn't ready yet.
                StartPiperInstallInBackground();

                // Download any missing speech models in the background
                var needsDownload = !ModelManager.IsVoskPresent || !ModelManager.IsWhisperPresent;
                if (!needsDownload)
                    return;

                VoxRingState.IsDownloadingModels = true;
                await ModelManager.EnsureModelsAsync(status =>
                {
                    VoxRingState.ModelDownloadStatus = status;
                    PluginLog.Info($"Model download: {status}");
                });
                VoxRingState.IsDownloadingModels = false;
                VoxRingState.ModelDownloadStatus = null;

                // Load newly downloaded models (guard against plugin being unloaded during download)
                if (SpeechRecognition == null)
                    return;

                if (ModelManager.IsVoskPresent && !SpeechRecognition.IsVoskLoaded)
                    SpeechRecognition.LoadVoskModel(ModelManager.VoskModelPath);
                if (ModelManager.IsWhisperPresent && !SpeechRecognition.IsWhisperLoaded)
                    SpeechRecognition.LoadWhisperModel(ModelManager.WhisperModelPath);

                PluginLog.Info($"Models loaded - Vosk: {SpeechRecognition.IsVoskLoaded}, Whisper: {SpeechRecognition.IsWhisperLoaded}");
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Model loading failed: {ex.Message}");
                VoxRingState.IsDownloadingModels = false;
                VoxRingState.ModelDownloadStatus = null;
            }
        }

        // Piper install runs fully in the background; if it fails we log a warning and TTS
        // keeps working via SAPI, so we don't want to await or block startup on it.
        internal static void StartPiperInstallInBackground()
        {
            if (PiperInstaller.IsFullyInstalled)
            {
                PluginLog.Info("Piper TTS already installed, skipping download");
                return;
            }

            _ = Task.Run(async () =>
            {
                VoxRingState.IsInstallingPiper = true;
                try
                {
                    await PiperInstaller.EnsureInstalledAsync(status =>
                    {
                        VoxRingState.PiperInstallStatus = status;
                        PluginLog.Info($"Piper install: {status}");
                    });
                }
                finally
                {
                    VoxRingState.IsInstallingPiper = false;
                    VoxRingState.PiperInstallStatus = null;
                }
            });
        }
    }
}
