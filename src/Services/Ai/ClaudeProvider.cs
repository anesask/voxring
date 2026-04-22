namespace Loupedeck.VoxRingPlugin.Services.Ai;

using System.Threading.Tasks;
using Loupedeck.VoxRingPlugin.Models;

/// <summary>
/// Claude provider. Thin wrapper around the existing <see cref="ClaudeApiService"/> so the
/// service class can stay untouched while we introduce the multi-provider abstraction.
/// </summary>
internal sealed class ClaudeProvider : IAiProvider
{
    private readonly ClaudeApiService _service;

    public ClaudeProvider(ClaudeApiService service)
    {
        _service = service;
    }

    public AiProvider Id => AiProvider.Claude;
    public string DisplayName => "Claude";
    public bool IsImplemented => true;
    public bool IsAvailable => !string.IsNullOrEmpty(VoxRingState.ClaudeApiKey);

    public Task<string> ReformatAsync(string rawText, string destinationPrompt, string language)
        => _service.ReformatAsync(rawText, destinationPrompt, language);

    public Task<(bool success, string message)> TestApiKeyAsync()
        => _service.TestApiKeyAsync();
}
