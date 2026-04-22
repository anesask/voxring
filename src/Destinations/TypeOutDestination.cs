namespace Loupedeck.VoxRingPlugin.Destinations;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Loupedeck.VoxRingPlugin.Models;

[SupportedOSPlatform("windows")]
public class TypeOutDestination : IDestination
{
    public string Name => "Type Out";
    public string Description => "Type voice directly into the active window";
    public bool IsAvailable => true;
    public DestinationCategory Category => DestinationCategory.Raw;
    public string AiPrompt => null;

    // Delay before typing starts (ms) - gives user time to focus target window
    private const int PreTypeDelayMs = 300;
    // Delay between chunks to avoid dropped characters
    private const int ChunkDelayMs = 10;
    // Characters per chunk
    private const int ChunkSize = 50;

    public async Task<bool> SendAsync(string text)
    {
        try
        {
            // Small delay so the user can switch focus to target window
            await Task.Delay(PreTypeDelayMs);

            // Send in chunks to avoid dropped characters on slow apps
            for (var i = 0; i < text.Length; i += ChunkSize)
            {
                var chunk = text.Substring(i, Math.Min(ChunkSize, text.Length - i));
                SendChunk(chunk);

                if (i + ChunkSize < text.Length)
                    await Task.Delay(ChunkDelayMs);
            }

            PluginLog.Info($"Typed {text.Length} chars into active window");
            return true;
        }
        catch (Exception ex)
        {
            PluginLog.Error($"TypeOut failed: {ex.Message}");
            return false;
        }
    }

    private static void SendChunk(string text)
    {
        // Build INPUT array: each char needs keydown + keyup = 2 entries
        var inputs = new INPUT[text.Length * 2];

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            // Key down
            inputs[i * 2] = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE,
                        time = 0,
                        dwExtraInfo = GetMessageExtraInfo()
                    }
                }
            };

            // Key up
            inputs[i * 2 + 1] = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = GetMessageExtraInfo()
                    }
                }
            };
        }

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            PluginLog.Warning($"TypeOut: sent {sent}/{inputs.Length} input events");
    }

    // --- Win32 P/Invoke ---

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetMessageExtraInfo();
}
