namespace Loupedeck.VoxRingPlugin.Services;

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Loupedeck.VoxRingPlugin.Models;
using NAudio.Wave;

public sealed class WebSettingsService : IDisposable
{
    private static WebSettingsService _instance;
    private static readonly object _lock = new();

    private HttpListener _listener;
    private CancellationTokenSource _cts;
    private Task _serverTask;
    private bool _disposed;

    private VoxRingPlugin _plugin;
    private string _settingsHtml;

    // Live microphone meter state
    private WaveInEvent _liveMic;
    private volatile int _liveMicPeak;
    private readonly object _liveMicLock = new();

    public int Port { get; private set; }
    public bool IsRunning { get; private set; }

    private WebSettingsService() { }

    public static WebSettingsService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new WebSettingsService();
                }
            }
            return _instance;
        }
    }

    public void Initialize(VoxRingPlugin plugin)
    {
        _plugin = plugin;
        _settingsHtml = LoadSettingsHtml();
    }

    public void Start()
    {
        if (IsRunning) return;

        Port = FindAvailablePort(8090);
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{Port}/");

        try
        {
            _listener.Start();
            IsRunning = true;
            _serverTask = Task.Run(() => ListenLoop(_cts.Token));
            PluginLog.Info($"WebSettingsService started on port {Port}");
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to start web server: {ex.Message}");
            _listener = null;
        }
    }

    public void OpenInBrowser()
    {
        if (!IsRunning) Start();
        if (!IsRunning) return;

        var url = $"http://localhost:{Port}/";
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to open browser: {ex.Message}");
        }
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleRequest(context), ct);
            }
            catch (ObjectDisposedException) { break; }
            catch (HttpListenerException) { break; }
            catch (Exception ex)
            {
                PluginLog.Error($"Web server error: {ex.Message}");
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext context)
    {
        var req = context.Request;
        var resp = context.Response;

        try
        {
            // Security: only serve localhost
            if (!req.IsLocal)
            {
                resp.StatusCode = 403;
                resp.Close();
                return;
            }

            var path = req.Url.AbsolutePath;
            var method = req.HttpMethod;

            if (path == "/" && method == "GET")
            {
                await ServeHtml(resp);
            }
            else if (path == "/api/settings" && method == "GET")
            {
                await ServeSettings(resp);
            }
            else if (path == "/api/settings" && method == "POST")
            {
                await SaveSettings(req, resp);
            }
            else if (path == "/api/microphones" && method == "GET")
            {
                await ServeMicrophones(resp);
            }
            else if (path == "/api/mic-test" && method == "POST")
            {
                await RunMicTest(resp);
            }
            else if (path == "/api/mic-test-start" && method == "POST")
            {
                await StartLiveMicTest(resp);
            }
            else if (path == "/api/mic-level" && method == "GET")
            {
                await GetLiveMicLevel(resp);
            }
            else if (path == "/api/mic-test-stop" && method == "POST")
            {
                await StopLiveMicTest(resp);
            }
            else if (path == "/api/download-models" && method == "POST")
            {
                await DownloadModels(resp);
            }
            else if (path == "/api/tts/install" && method == "POST")
            {
                await InstallPiper(resp);
            }
            else if (path == "/api/tts/test" && method == "POST")
            {
                await TestTts(req, resp);
            }
            else if (path == "/api/test-claude" && method == "POST")
            {
                await TestClaude(resp);
            }
            else if (path == "/api/test-ai" && method == "POST")
            {
                await TestAiProvider(req, resp);
            }
            else if (path == "/api/open-voice-notes" && method == "POST")
            {
                await OpenVoiceNotesFolder(resp);
            }
            else if (path == "/api/pick-voice-notes-folder" && method == "POST")
            {
                await PickVoiceNotesFolder(resp);
            }
            else if (path == "/api/preview-filename" && method == "POST")
            {
                await PreviewFilenameEndpoint(req, resp);
            }
            else
            {
                resp.StatusCode = 404;
                resp.Close();
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Request handler error: {ex.Message}");
            try
            {
                resp.StatusCode = 500;
                resp.Close();
            }
            catch { }
        }
    }

    private async Task ServeHtml(HttpListenerResponse resp)
    {
        var html = _settingsHtml ?? "<h1>Settings page not found</h1>";
        var buffer = Encoding.UTF8.GetBytes(html);
        resp.ContentType = "text/html; charset=utf-8";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private async Task ServeSettings(HttpListenerResponse resp)
    {
        // TTS info is Windows-only (SAPI/Piper); give null on non-Windows to keep the API shape.
        var ttsBackend = OperatingSystem.IsWindows() ? TtsService.Instance.ActiveBackend.ToString() : "None";

        var settings = new
        {
            speechEngine = VoxRingState.SelectedEngine.ToString(),
            language = VoxRingState.SelectedLanguage,
            microphoneDevice = VoxRingState.SelectedMicrophoneIndex,
            maxRecordingSeconds = VoxRingState.MaxRecordingSeconds,
            whisperModelSize = VoxRingState.SelectedWhisperModel.ToString(),
            voskLoaded = _plugin?.SpeechRecognition?.IsVoskLoaded ?? false,
            whisperLoaded = _plugin?.SpeechRecognition?.IsWhisperLoaded ?? false,
            voskPresent = ModelManager.IsVoskPresent,
            whisperPresent = ModelManager.IsWhisperPresent,
            modelsPath = ModelManager.ModelsBaseDir,
            claudeApiKey = MaskApiKey(VoxRingState.ClaudeApiKey),
            claudeConfigured = VoxRingState.IsClaudeAvailable,
            openAiApiKey = MaskApiKey(VoxRingState.OpenAiApiKey),
            openAiConfigured = !string.IsNullOrEmpty(VoxRingState.OpenAiApiKey),
            slackWebhookUrl = MaskWebhookUrl(VoxRingState.SlackWebhookUrl),
            slackConfigured = !string.IsNullOrEmpty(VoxRingState.SlackWebhookUrl),
            discordWebhookUrl = MaskWebhookUrl(VoxRingState.DiscordWebhookUrl),
            discordConfigured = !string.IsNullOrEmpty(VoxRingState.DiscordWebhookUrl),
            voiceNoteSavePath = VoxRingState.VoiceNoteSavePath ?? "",
            voiceNoteEffectivePath = VoxRingState.EffectiveVoiceNoteSavePath,
            voiceNoteFilenamePattern = VoxRingState.VoiceNoteFilenamePattern ?? "",
            voiceNoteDefaultPattern = VoxRingState.DefaultVoiceNoteFilenamePattern,
            voiceNoteFilenamePreview = PreviewFilename(VoxRingState.VoiceNoteFilenamePattern ?? VoxRingState.DefaultVoiceNoteFilenamePattern),

            // AI provider (new: multi-provider)
            aiProvider = VoxRingState.SelectedAiProvider.ToString(),
            geminiApiKey = MaskApiKey(VoxRingState.GeminiApiKey),
            geminiConfigured = !string.IsNullOrEmpty(VoxRingState.GeminiApiKey),
            deepSeekApiKey = MaskApiKey(VoxRingState.DeepSeekApiKey),
            deepSeekConfigured = !string.IsNullOrEmpty(VoxRingState.DeepSeekApiKey),
            perplexityApiKey = MaskApiKey(VoxRingState.PerplexityApiKey),
            perplexityConfigured = !string.IsNullOrEmpty(VoxRingState.PerplexityApiKey),

            // Noise gate + transcript post-processing
            useNoiseGate = VoxRingState.UseNoiseGate,
            translateTargetLanguage = VoxRingState.TranslateTargetLanguage ?? "",
            useFillerWordCleaner = VoxRingState.UseFillerWordCleaner,
            caseTransform = VoxRingState.SelectedCaseTransform.ToString(),

            // AI toggle + custom prompts
            useAi = VoxRingState.UseAi,
            customPromptEmail = VoxRingState.CustomPrompts.TryGetValue("Email", out var cpEmail) ? cpEmail : "",
            customPromptSlack = VoxRingState.CustomPrompts.TryGetValue("Slack", out var cpSlack) ? cpSlack : "",
            customPromptDiscord = VoxRingState.CustomPrompts.TryGetValue("Discord", out var cpDiscord) ? cpDiscord : "",
            customPromptTeams = VoxRingState.CustomPrompts.TryGetValue("Teams", out var cpTeams) ? cpTeams : "",
            customPromptCalendar = VoxRingState.CustomPrompts.TryGetValue("Calendar", out var cpCal) ? cpCal : "",

            // Text-to-Speech
            ttsActiveBackend = ttsBackend,
            ttsPath = PiperInstaller.ActivePiperDir ?? PiperInstaller.TtsRoot,
            ttsBundledPath = PiperInstaller.BundledTtsRoot,
            piperBinaryPresent = PiperInstaller.IsBinaryPresent,
            piperEnVoicePresent = PiperInstaller.IsEnVoicePresent,
            piperDeVoicePresent = PiperInstaller.IsDeVoicePresent,
            piperBinarySource = PiperInstaller.PiperBinarySource,       // "Bundled" | "Downloaded" | "None"
            piperEnVoiceSource = PiperInstaller.EnVoiceSource,
            piperDeVoiceSource = PiperInstaller.DeVoiceSource,
            piperFullyInstalled = PiperInstaller.IsFullyInstalled,
            isInstallingPiper = VoxRingState.IsInstallingPiper,
            piperInstallStatus = VoxRingState.PiperInstallStatus
        };

        var json = JsonSerializer.Serialize(settings);
        var buffer = Encoding.UTF8.GetBytes(json);
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private async Task SaveSettings(HttpListenerRequest req, HttpListenerResponse resp)
    {
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
        var body = await reader.ReadToEndAsync();

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("speechEngine", out var engineEl))
        {
            if (Enum.TryParse<SpeechEngine>(engineEl.GetString(), out var engine))
                _plugin?.SaveSpeechEngine(engine);
        }

        if (root.TryGetProperty("language", out var langEl))
        {
            var lang = langEl.GetString();
            if (!string.IsNullOrEmpty(lang))
                _plugin?.SaveLanguage(lang);
        }

        if (root.TryGetProperty("microphoneDevice", out var micEl))
        {
            var micIndex = micEl.GetInt32();
            _plugin?.SaveMicrophoneDevice(micIndex);
        }

        if (root.TryGetProperty("maxRecordingSeconds", out var maxRecEl))
        {
            var maxRec = maxRecEl.GetInt32();
            if (maxRec is >= 5 and <= 300)
                _plugin?.SaveMaxRecordingSeconds(maxRec);
        }

        if (root.TryGetProperty("whisperModelSize", out var modelSizeEl))
        {
            if (Enum.TryParse<WhisperModelSize>(modelSizeEl.GetString(), out var modelSize))
                _plugin?.SaveWhisperModelSize(modelSize);
        }

        if (root.TryGetProperty("aiProvider", out var providerEl))
        {
            if (Enum.TryParse<AiProvider>(providerEl.GetString(), out var provider))
                _plugin?.SaveAiProvider(provider);
        }

        if (root.TryGetProperty("claudeApiKey", out var claudeEl))
        {
            var key = claudeEl.GetString();
            if (!string.IsNullOrEmpty(key) && !key.StartsWith("••••"))
                _plugin?.SaveClaudeApiKey(key);
        }

        if (root.TryGetProperty("openAiApiKey", out var openAiEl))
        {
            var key = openAiEl.GetString();
            if (!string.IsNullOrEmpty(key) && !key.StartsWith("••••"))
                _plugin?.SaveOpenAiApiKey(key);
        }

        if (root.TryGetProperty("geminiApiKey", out var geminiEl))
        {
            var key = geminiEl.GetString();
            if (!string.IsNullOrEmpty(key) && !key.StartsWith("••••"))
                _plugin?.SaveGeminiApiKey(key);
        }

        if (root.TryGetProperty("deepSeekApiKey", out var dsEl))
        {
            var key = dsEl.GetString();
            if (!string.IsNullOrEmpty(key) && !key.StartsWith("••••"))
                _plugin?.SaveDeepSeekApiKey(key);
        }

        if (root.TryGetProperty("perplexityApiKey", out var pxEl))
        {
            var key = pxEl.GetString();
            if (!string.IsNullOrEmpty(key) && !key.StartsWith("••••"))
                _plugin?.SavePerplexityApiKey(key);
        }

        if (root.TryGetProperty("slackWebhookUrl", out var slackEl))
        {
            var url = slackEl.GetString();
            if (url != null && !url.StartsWith("••••"))
                _plugin?.SaveSlackWebhookUrl(url);
        }

        if (root.TryGetProperty("discordWebhookUrl", out var discordEl))
        {
            var url = discordEl.GetString();
            if (url != null && !url.StartsWith("••••"))
                _plugin?.SaveDiscordWebhookUrl(url);
        }

        if (root.TryGetProperty("voiceNoteSavePath", out var vnPathEl))
        {
            _plugin?.SaveVoiceNoteSavePath(vnPathEl.GetString() ?? "");
        }

        if (root.TryGetProperty("voiceNoteFilenamePattern", out var vnPatternEl))
        {
            _plugin?.SaveVoiceNoteFilenamePattern(vnPatternEl.GetString() ?? "");
        }

        if (root.TryGetProperty("useNoiseGate", out var useNgEl))
            _plugin?.SaveUseNoiseGate(useNgEl.GetBoolean());
        if (root.TryGetProperty("translateTargetLanguage", out var translateEl))
            _plugin?.SaveTranslateTargetLanguage(translateEl.GetString() ?? "");
        if (root.TryGetProperty("useFillerWordCleaner", out var fillerEl))
            _plugin?.SaveUseFillerWordCleaner(fillerEl.GetBoolean());
        if (root.TryGetProperty("caseTransform", out var caseEl)
            && Enum.TryParse<Models.CaseTransform>(caseEl.GetString(), out var caseVal))
            _plugin?.SaveCaseTransform(caseVal);

        if (root.TryGetProperty("useAi", out var useAiEl))
        {
            _plugin?.SaveUseAi(useAiEl.GetBoolean());
        }

        foreach (var destName in new[] { "Email", "Slack", "Discord", "Teams", "Calendar" })
        {
            var propKey = "customPrompt" + destName;
            if (root.TryGetProperty(propKey, out var promptEl))
                _plugin?.SaveCustomPrompt(destName, promptEl.GetString() ?? "");
        }

        var buffer = Encoding.UTF8.GetBytes("{\"ok\":true}");
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private async Task ServeMicrophones(HttpListenerResponse resp)
    {
        var devices = new List<object>();
        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            devices.Add(new { index = i, name = caps.ProductName, channels = caps.Channels });
        }

        var json = JsonSerializer.Serialize(new
        {
            devices,
            selected = VoxRingState.SelectedMicrophoneIndex
        });
        var buffer = Encoding.UTF8.GetBytes(json);
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private async Task RunMicTest(HttpListenerResponse resp)
    {
        string resultJson;
        try
        {
            var format = new WaveFormat(16000, 1);
            var deviceIndex = VoxRingState.SelectedMicrophoneIndex;
            var waveIn = new WaveInEvent
            {
                DeviceNumber = deviceIndex,
                WaveFormat = format,
                BufferMilliseconds = 50
            };

            var pcm = new MemoryStream();
            waveIn.DataAvailable += (s, e) => pcm.Write(e.Buffer, 0, e.BytesRecorded);

            waveIn.StartRecording();
            await Task.Delay(2000);
            waveIn.StopRecording();
            waveIn.Dispose();

            var pcmData = pcm.ToArray();
            pcm.Dispose();

            // Calculate peak amplitude from raw PCM (16-bit samples)
            var peak = 0;
            for (var i = 0; i < pcmData.Length - 1; i += 2)
            {
                var sample = Math.Abs((short)(pcmData[i] | (pcmData[i + 1] << 8)));
                if (sample > peak) peak = sample;
            }

            var peakDb = peak > 0 ? 20.0 * Math.Log10(peak / 32768.0) : -96.0;
            var byteCount = pcmData.Length;

            resultJson = JsonSerializer.Serialize(new
            {
                ok = true,
                durationMs = 2000,
                peakDb = Math.Round(peakDb, 1),
                bytesRecorded = byteCount,
                detected = peak > 500
            });
        }
        catch (Exception ex)
        {
            resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }

        var buffer = Encoding.UTF8.GetBytes(resultJson);
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private async Task InstallPiper(HttpListenerResponse resp)
    {
        string resultJson;
        try
        {
            // Kick off install on a background task; don't block the HTTP response waiting for
            // ~150 MB of downloads. UI polls /api/settings for status.
            if (!VoxRingState.IsInstallingPiper)
            {
                VoxRingPlugin.StartPiperInstallInBackground();
            }
            resultJson = JsonSerializer.Serialize(new { ok = true, installing = true });
        }
        catch (Exception ex)
        {
            resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }

        var buffer = Encoding.UTF8.GetBytes(resultJson);
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private async Task TestTts(HttpListenerRequest req, HttpListenerResponse resp)
    {
        string resultJson;
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                resultJson = JsonSerializer.Serialize(new { ok = false, error = "TTS is Windows-only" });
            }
            else
            {
                // Optional {"lang": "en" | "de"} body to force a specific voice. Omitted = use current language setting.
                string langOverride = null;
                try
                {
                    using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                    var body = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        using var doc = JsonDocument.Parse(body);
                        if (doc.RootElement.TryGetProperty("lang", out var langEl))
                            langOverride = langEl.GetString();
                    }
                }
                catch { /* empty body is fine */ }

                var effective = string.IsNullOrEmpty(langOverride) ? (VoxRingState.SelectedLanguage ?? "auto") : langOverride;
                var sample = effective == "de"
                    ? "Hallo, dies ist ein Test von VoxRing."
                    : "Hello, this is a VoxRing voice test.";
                TtsService.Instance.Speak(sample, langOverride);
                resultJson = JsonSerializer.Serialize(new
                {
                    ok = true,
                    backend = TtsService.Instance.ActiveBackend.ToString(),
                    language = effective
                });
            }
        }
        catch (Exception ex)
        {
            resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }

        var buffer = Encoding.UTF8.GetBytes(resultJson);
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private async Task DownloadModels(HttpListenerResponse resp)
    {
        string resultJson;
        try
        {
            await ModelManager.EnsureModelsAsync(status => PluginLog.Info($"Model download: {status}"));

            // Reload models into the speech service
            if (ModelManager.IsVoskPresent && !(_plugin?.SpeechRecognition?.IsVoskLoaded ?? false))
                _plugin?.SpeechRecognition?.LoadVoskModel(ModelManager.VoskModelPath);

            if (ModelManager.IsWhisperPresent && !(_plugin?.SpeechRecognition?.IsWhisperLoaded ?? false))
                _plugin?.SpeechRecognition?.LoadWhisperModel(ModelManager.WhisperModelPath);

            resultJson = JsonSerializer.Serialize(new
            {
                ok = true,
                voskPresent = ModelManager.IsVoskPresent,
                whisperPresent = ModelManager.IsWhisperPresent,
                voskLoaded = _plugin?.SpeechRecognition?.IsVoskLoaded ?? false,
                whisperLoaded = _plugin?.SpeechRecognition?.IsWhisperLoaded ?? false
            });
        }
        catch (Exception ex)
        {
            resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }

        var buffer = Encoding.UTF8.GetBytes(resultJson);
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private async Task TestClaude(HttpListenerResponse resp)
    {
        string resultJson;
        try
        {
            if (_plugin?.ClaudeApi == null || !VoxRingState.IsClaudeAvailable)
            {
                resultJson = JsonSerializer.Serialize(new { ok = false, error = "Claude API key not configured" });
            }
            else
            {
                var (success, message) = await _plugin.ClaudeApi.TestApiKeyAsync();
                resultJson = JsonSerializer.Serialize(new { ok = success, message });
            }
        }
        catch (Exception ex)
        {
            resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }

        var buffer = Encoding.UTF8.GetBytes(resultJson);
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    /// <summary>
    /// Generic test endpoint: accepts {"provider": "Claude" | "OpenAi" | ...} and runs that
    /// provider's smoke test. Returns {ok, message} matching what the Claude-specific endpoint does.
    /// </summary>
    private async Task TestAiProvider(HttpListenerRequest req, HttpListenerResponse resp)
    {
        string resultJson;
        try
        {
            string providerName = null;
            try
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(body))
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("provider", out var pEl))
                        providerName = pEl.GetString();
                }
            }
            catch { /* body is optional */ }

            if (string.IsNullOrEmpty(providerName) || !Enum.TryParse<AiProvider>(providerName, out var providerId))
            {
                resultJson = JsonSerializer.Serialize(new { ok = false, error = "Missing or invalid 'provider'" });
            }
            else
            {
                var provider = Services.Ai.AiProviderRegistry.Get(providerId);
                if (!provider.IsImplemented)
                {
                    resultJson = JsonSerializer.Serialize(new { ok = false, error = $"{provider.DisplayName}: coming soon" });
                }
                else if (!provider.IsAvailable)
                {
                    resultJson = JsonSerializer.Serialize(new { ok = false, error = $"{provider.DisplayName}: API key not configured" });
                }
                else
                {
                    var (success, message) = await provider.TestApiKeyAsync();
                    resultJson = JsonSerializer.Serialize(new { ok = success, message });
                }
            }
        }
        catch (Exception ex)
        {
            resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }

        var buffer = Encoding.UTF8.GetBytes(resultJson);
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private async Task StartLiveMicTest(HttpListenerResponse resp)
    {
        string resultJson;
        try
        {
            StopLiveMicInternal(); // idempotent: stop any existing session first

            var deviceIndex = VoxRingState.SelectedMicrophoneIndex >= 0 ? VoxRingState.SelectedMicrophoneIndex : 0;
            lock (_liveMicLock)
            {
                _liveMicPeak = 0;
                _liveMic = new WaveInEvent
                {
                    DeviceNumber = deviceIndex,
                    WaveFormat = new WaveFormat(16000, 1),
                    BufferMilliseconds = 30
                };
                _liveMic.DataAvailable += OnLiveMicData;
                _liveMic.StartRecording();
            }

            resultJson = JsonSerializer.Serialize(new { ok = true });
        }
        catch (Exception ex)
        {
            StopLiveMicInternal();
            resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
            PluginLog.Error($"Mic test start failed: {ex.Message}");
        }

        await WriteJson(resp, resultJson);
    }

    private void OnLiveMicData(object sender, WaveInEventArgs e)
    {
        var peak = 0;
        for (var i = 0; i < e.BytesRecorded - 1; i += 2)
        {
            var sample = Math.Abs((short)(e.Buffer[i] | (e.Buffer[i + 1] << 8)));
            if (sample > peak) peak = sample;
        }
        // Keep the highest peak observed since last poll (simple VU-meter behavior)
        if (peak > _liveMicPeak)
            _liveMicPeak = peak;
    }

    private async Task GetLiveMicLevel(HttpListenerResponse resp)
    {
        var active = _liveMic != null;
        // Read-and-reset so each poll reflects activity since last call (bouncy meter)
        var peak = System.Threading.Interlocked.Exchange(ref _liveMicPeak, 0);
        var peakDb = peak > 0 ? 20.0 * Math.Log10(peak / 32768.0) : -96.0;

        var json = JsonSerializer.Serialize(new
        {
            ok = active,
            peak,
            peakDb = Math.Round(peakDb, 1),
            detected = peak > 500
        });

        await WriteJson(resp, json);
    }

    private async Task StopLiveMicTest(HttpListenerResponse resp)
    {
        StopLiveMicInternal();
        await WriteJson(resp, "{\"ok\":true}");
    }

    private void StopLiveMicInternal()
    {
        lock (_liveMicLock)
        {
            if (_liveMic == null) return;
            try
            {
                _liveMic.DataAvailable -= OnLiveMicData;
                _liveMic.StopRecording();
                _liveMic.Dispose();
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Mic test stop: {ex.Message}");
            }
            finally
            {
                _liveMic = null;
                _liveMicPeak = 0;
            }
        }
    }

    private static async Task WriteJson(HttpListenerResponse resp, string json)
    {
        var buffer = Encoding.UTF8.GetBytes(json);
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private async Task PreviewFilenameEndpoint(HttpListenerRequest req, HttpListenerResponse resp)
    {
        string resultJson;
        try
        {
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
            var body = await reader.ReadToEndAsync();
            using var doc = JsonDocument.Parse(body);
            var pattern = doc.RootElement.TryGetProperty("pattern", out var p) ? p.GetString() : null;
            resultJson = JsonSerializer.Serialize(new { ok = true, preview = PreviewFilename(pattern) });
        }
        catch (Exception ex)
        {
            resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }

        var buffer = Encoding.UTF8.GetBytes(resultJson);
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private static string PreviewFilename(string pattern)
    {
        var effective = string.IsNullOrWhiteSpace(pattern) ? VoxRingState.DefaultVoiceNoteFilenamePattern : pattern;
        string raw;
        try { raw = DateTime.Now.ToString(effective); }
        catch { return "(invalid format)"; }

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(raw.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c));
        return (string.IsNullOrWhiteSpace(sanitized) ? DateTime.Now.ToString(VoxRingState.DefaultVoiceNoteFilenamePattern) : sanitized) + ".wav";
    }

    private async Task PickVoiceNotesFolder(HttpListenerResponse resp)
    {
        string resultJson;
        try
        {
            var currentPath = (VoxRingState.EffectiveVoiceNoteSavePath ?? "").Replace("'", "''");

            // Build the PowerShell script. Single-quoted PS strings: inner apostrophes are doubled above.
            var script =
                "Add-Type -AssemblyName System.Windows.Forms | Out-Null\n" +
                "$f = New-Object System.Windows.Forms.FolderBrowserDialog\n" +
                "$f.Description = 'Select Voice Notes save folder'\n" +
                "$f.UseDescriptionForTitle = $true\n" +
                $"$f.SelectedPath = '{currentPath}'\n" +
                "$f.ShowNewFolderButton = $true\n" +
                "if ($f.ShowDialog() -eq 'OK') { [Console]::Out.WriteLine($f.SelectedPath) }\n";

            // Base64 UTF-16LE encode the script to bypass all command-line quoting rules.
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            var psExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(psExe)) psExe = "powershell.exe";

            var psi = new ProcessStartInfo
            {
                FileName = psExe,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -STA -EncodedCommand {encoded}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            PluginLog.Info($"FolderPicker: launching {psExe}");
            using var proc = Process.Start(psi);
            if (proc == null) throw new InvalidOperationException("Failed to start folder picker");

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            await Task.Run(() => proc.WaitForExit(120000));
            var stdout = (await stdoutTask).Trim();
            var stderr = (await stderrTask).Trim();

            PluginLog.Info($"FolderPicker exit={proc.ExitCode} stdout='{stdout}' stderr='{stderr}'");

            if (!string.IsNullOrWhiteSpace(stdout) && Directory.Exists(stdout))
            {
                _plugin?.SaveVoiceNoteSavePath(stdout);
                resultJson = JsonSerializer.Serialize(new { ok = true, path = stdout });
            }
            else if (!string.IsNullOrWhiteSpace(stderr))
            {
                resultJson = JsonSerializer.Serialize(new { ok = false, error = stderr });
            }
            else
            {
                resultJson = JsonSerializer.Serialize(new { ok = false, cancelled = true });
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error($"PickVoiceNotesFolder failed: {ex.Message}");
            resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }

        await WriteJson(resp, resultJson);
    }

    private async Task OpenVoiceNotesFolder(HttpListenerResponse resp)
    {
        string resultJson;
        try
        {
            var folder = VoxRingState.EffectiveVoiceNoteSavePath;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            resultJson = JsonSerializer.Serialize(new { ok = true, path = folder });
        }
        catch (Exception ex)
        {
            resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }

        var buffer = Encoding.UTF8.GetBytes(resultJson);
        resp.ContentType = "application/json";
        resp.ContentLength64 = buffer.Length;
        await resp.OutputStream.WriteAsync(buffer);
        resp.Close();
    }

    private static string MaskApiKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (key.Length <= 8) return "••••";
        return "••••" + key[^4..];
    }

    private static string MaskWebhookUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        if (url.Length <= 20) return "••••";
        return url[..20] + "••••";
    }

    private string GetPluginBasePath()
    {
        try
        {
            var pluginsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Logi", "LogiPluginService", "Plugins");
            var linkFile = Path.Combine(pluginsDir, "VoxRingPlugin.link");
            return File.Exists(linkFile) ? File.ReadAllText(linkFile).Trim() : "(unknown)";
        }
        catch { return "(error reading path)"; }
    }

    private string LoadSettingsHtml()
    {
        try
        {
            return PluginResources.ReadTextFile("settings.html");
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to load settings.html: {ex.Message}");
            return null;
        }
    }

    private int FindAvailablePort(int startPort)
    {
        for (var port = startPort; port < startPort + 200; port++)
        {
            if (port < 1024) continue;
            try
            {
                using var probe = new HttpListener();
                probe.Prefixes.Add($"http://localhost:{port}/");
                probe.Start();
                probe.Stop();
                return port;
            }
            catch { }
        }

        PluginLog.Warning($"No free port found from {startPort}");
        return startPort;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopLiveMicInternal();
        _cts?.Cancel();
        _listener?.Stop();
        _listener?.Close();

        try { _serverTask?.Wait(TimeSpan.FromSeconds(3)); }
        catch { }

        _cts?.Dispose();
        IsRunning = false;

        PluginLog.Info("WebSettingsService stopped");
    }
}
