namespace Ch57x.Core;

/// <summary>
/// CH57x packet builder + response parser. Faithful port of web/js/protocol.js.
/// See docs/PROTOCOL.md. Report ID = 3, 64-byte packets.
/// </summary>
public static class Protocol
{
    public const ushort VendorId = 0x1189;
    public static readonly ushort[] ProductIds = { 0x8840, 0x8842, 0x8890 };
    public const byte ReportId = 0x03;
    public const int PacketSize = 64;

    private static byte[] Pad64(IEnumerable<byte> bytes)
    {
        var buf = new byte[PacketSize];
        int i = 0;
        foreach (var b in bytes) { if (i >= PacketSize) break; buf[i++] = b; }
        return buf;
    }

    private static byte ModByte(IEnumerable<string>? mods) =>
        mods is null ? (byte)0 : mods.Aggregate((byte)0, (a, m) => (byte)(a | (KeyCodes.Modifiers.TryGetValue(m, out var v) ? v : 0)));

    private static byte I8(int n) => (byte)(n & 0xff);

    // keyId mapping (PROTOCOL.md §4) for a 9-button + 3-knob style layout.
    public static byte ButtonId(int n) => (byte)(n + 1);                  // n: 0-based
    public static byte KnobId(int knob, int action) => (byte)(16 + 3 * knob + action); // action 0=ccw,1=press,2=cw

    /// <summary>상용구: expand text into key accords (max 18, firmware limit).</summary>
    public static List<Accord> TextToSteps(string text)
    {
        var steps = new List<Accord>();
        foreach (var ch in text)
        {
            if (CharMap.TryGetValue(ch, out var a))
                steps.Add(new Accord { Mods = a.Mods.ToList(), Code = a.Code });
            if (steps.Count >= 18) break;
        }
        return steps;
    }

    /// <summary>Build all 64-byte messages for one key binding (bind + optional delay + commit sequence).</summary>
    public static List<byte[]> BuildKeyMessages(byte keyId, int layer, Binding? binding)
    {
        if (binding is null || binding.Type == BindingType.None) return new();

        // text → key macro
        if (binding.Type == BindingType.Text)
        {
            var steps = TextToSteps(binding.Text ?? "");
            if (steps.Count == 0) return new();
            binding = new Binding { Type = BindingType.Key, Steps = steps, Delay = binding.Delay };
        }

        byte kind = binding.Type switch
        {
            BindingType.Key => 1, BindingType.Media => 2, BindingType.Mouse => 3,
            _ => throw new InvalidOperationException("unknown binding")
        };
        var msg = new List<byte> { 0x03, 0xfe, keyId, (byte)(layer + 1), kind, 0, 0, 0, 0, 0 };

        if (binding.Type == BindingType.Key)
        {
            var steps = binding.Steps ?? new();
            if (steps.Count == 0) return new();
            if (steps.Count > 18) throw new InvalidOperationException("매크로 시퀀스가 너무 깁니다 (최대 18)");
            if (steps.Count == 1 && string.IsNullOrEmpty(steps[0].Code)) msg.Add(0);
            else msg.Add((byte)steps.Count);
            foreach (var s in steps)
            {
                byte code = !string.IsNullOrEmpty(s.Code) && KeyCodes.Codes.TryGetValue(s.Code!, out var c) ? c : (byte)0;
                msg.Add(ModByte(s.Mods)); msg.Add(code);
            }
        }
        else if (binding.Type == BindingType.Media)
        {
            ushort code = binding.Media is not null && KeyCodes.MediaCodes.TryGetValue(binding.Media, out var m) ? m.Code : (ushort)0;
            msg.AddRange(new byte[] { 0, (byte)(code & 0xff), (byte)((code >> 8) & 0xff) });
        }
        else // mouse
        {
            var mods = binding.Mods ?? new();
            byte mod = mods.Aggregate((byte)0, (a, m) => (byte)(a | (KeyCodes.MouseModifiers.TryGetValue(m, out var v) ? v : 0)));
            byte btns = (binding.Buttons ?? new()).Aggregate((byte)0, (a, b) => (byte)(a | (KeyCodes.MouseButtons.TryGetValue(b, out var v) ? v : 0)));
            switch (binding.Action)
            {
                case "click": msg.AddRange(new byte[] { 0x01, mod, btns }); break;
                case "wheel": msg.AddRange(new byte[] { 0x03, mod, 0, 0, 0, I8(binding.Delta) }); break;
                case "move":  msg.AddRange(new byte[] { 0x05, mod, 0, I8(binding.Dx), I8(binding.Dy) }); break;
                case "drag":  msg.AddRange(new byte[] { 0x05, mod, btns, I8(binding.Dx), I8(binding.Dy) }); break;
                default: throw new InvalidOperationException("알 수 없는 마우스 동작: " + binding.Action);
            }
        }

        var messages = new List<byte[]> { Pad64(msg) };

        if (binding.Type == BindingType.Key && binding.Delay != 0)
        {
            int d = binding.Delay;
            if (d > 6000) throw new InvalidOperationException("지원하는 최대 지연은 6000ms 입니다");
            messages.Add(Pad64(new byte[] { 0x03, 0xfe, keyId, (byte)(layer + 1), 5, (byte)(d & 0xff), (byte)((d >> 8) & 0xff) }));
        }

        // commit / end-programming sequence (prevents partial-reset bug)
        messages.Add(Pad64(new byte[] { 0x03, 0xaa, 0xaa }));
        messages.Add(Pad64(new byte[] { 0x03, 0xfd, 0xfe, 0xff }));
        messages.Add(Pad64(new byte[] { 0x03, 0xaa, 0xaa }));
        return messages;
    }

    /// <summary>LED packet (PROTOCOL.md §6). mode 0..5, color 0..7.</summary>
    public static List<byte[]> BuildLedMessages(int layer, int mode, int color)
    {
        byte code = (byte)(((color & 0x0f) << 4) | (mode & 0x0f));
        return new()
        {
            Pad64(new byte[] { 0x03, 0xfe, 0xb0, (byte)(layer + 1), 0x08, 0, 0, 0, 0, 0, 0x01, 0, code }),
            Pad64(new byte[] { 0x03, 0xfd, 0xfe, 0xff }),
        };
    }

    // read request for one layer (1-based): [03 FA numKeys numKnobs layer]
    public static byte[] ReadRequest(int layer, int numKeys = 0x0f, int numKnobs = 0x03) =>
        Pad64(new byte[] { 0x03, 0xfa, (byte)numKeys, (byte)numKnobs, (byte)layer });

    // device key/knob count query: [03 FB FB FB]
    public static byte[] DeviceInfoRequest() => Pad64(new byte[] { 0x03, 0xfb, 0xfb, 0xfb });

    /// <summary>Decode one 0xFA response (full 64-byte buffer incl. leading report-id-less data starting with 0xFA).</summary>
    public static (int KeyId, int Layer, Binding? Binding)? ParseReadResponse(ReadOnlySpan<byte> d)
    {
        if (d.Length < 12 || d[0] != 0xfa) return null;
        int keyId = d[1], layer = d[2], kind = d[3], count = d[9];
        Binding? binding = null;
        if (kind is 0 or 1) // keyboard
        {
            var steps = new List<Accord>();
            for (int i = 0; i < Math.Max(count, 1); i++)
            {
                if (10 + i * 2 + 1 >= d.Length) break;
                byte mod = d[10 + i * 2], code = d[11 + i * 2];
                if (mod == 0 && code == 0) continue;
                steps.Add(new Accord
                {
                    Mods = ModsFromByte(mod),
                    Code = code != 0 && KeyCodes.CodeToName.TryGetValue(code, out var n) ? n : null,
                });
            }
            if (steps.Count > 0) binding = new Binding { Type = BindingType.Key, Steps = steps };
        }
        else if (kind == 2) // media
        {
            ushort code = (ushort)(d[10] | (d[11] << 8));
            if (code != 0)
                binding = new Binding { Type = BindingType.Media,
                    Media = KeyCodes.MediaCodeToName.TryGetValue(code, out var n) ? n : $"0x{code:x}" };
        }
        else if (kind == 3) // mouse (best-effort)
        {
            sbyte delta = unchecked((sbyte)d[14]);
            binding = delta != 0
                ? new Binding { Type = BindingType.Mouse, Action = "wheel", Delta = delta, Buttons = new(), Mods = new() }
                : new Binding { Type = BindingType.Mouse, Action = "click", Buttons = new(), Mods = new() };
        }
        return (keyId, layer, binding);
    }

    private static List<string> ModsFromByte(byte b) =>
        KeyCodes.Modifiers.Where(kv => (b & kv.Value) != 0).Select(kv => kv.Key).ToList();

    // char -> accord for 상용구 (US layout)
    private static readonly Dictionary<char, (string[] Mods, string Code)> CharMap = BuildCharMap();
    private static Dictionary<char, (string[], string)> BuildCharMap()
    {
        var m = new Dictionary<char, (string[], string)>();
        for (char c = 'a'; c <= 'z'; c++) { string up = char.ToUpper(c).ToString(); m[c] = (Array.Empty<string>(), up); m[char.ToUpper(c)] = (new[] { "Shift" }, up); }
        const string digits = "1234567890", shiftDigits = "!@#$%^&*()";
        for (int i = 0; i < 10; i++) { m[digits[i]] = (Array.Empty<string>(), digits[i].ToString()); m[shiftDigits[i]] = (new[] { "Shift" }, digits[i].ToString()); }
        void Sym(char ch, string code, bool sh) => m[ch] = (sh ? new[] { "Shift" } : Array.Empty<string>(), code);
        Sym(' ', "Space", false); Sym('\n', "Enter", false); Sym('\t', "Tab", false);
        Sym('-', "Minus", false); Sym('_', "Minus", true); Sym('=', "Equal", false); Sym('+', "Equal", true);
        Sym('[', "LeftBracket", false); Sym('{', "LeftBracket", true); Sym(']', "RightBracket", false); Sym('}', "RightBracket", true);
        Sym('\\', "Backslash", false); Sym('|', "Backslash", true); Sym(';', "Semicolon", false); Sym(':', "Semicolon", true);
        Sym('\'', "Quote", false); Sym('"', "Quote", true); Sym('`', "Grave", false); Sym('~', "Grave", true);
        Sym(',', "Comma", false); Sym('<', "Comma", true); Sym('.', "Dot", false); Sym('>', "Dot", true);
        Sym('/', "Slash", false); Sym('?', "Slash", true);
        return m;
    }
}
