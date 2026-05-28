namespace Ch57x.Core;

/// <summary>HID usage codes, modifiers, media codes. Ported from web/js/keycodes.js.</summary>
public static class KeyCodes
{
    // HID usage codes (Usage Page 0x07), declaration order starting at A=0x04.
    // name -> (code, label)
    public static readonly IReadOnlyList<(string Name, string Label)> Order = new (string, string)[]
    {
        ("A","A"),("B","B"),("C","C"),("D","D"),("E","E"),("F","F"),("G","G"),("H","H"),
        ("I","I"),("J","J"),("K","K"),("L","L"),("M","M"),("N","N"),("O","O"),("P","P"),
        ("Q","Q"),("R","R"),("S","S"),("T","T"),("U","U"),("V","V"),("W","W"),("X","X"),
        ("Y","Y"),("Z","Z"),
        ("1","1"),("2","2"),("3","3"),("4","4"),("5","5"),("6","6"),("7","7"),("8","8"),("9","9"),("0","0"),
        ("Enter","Enter"),("Escape","Esc"),("Backspace","Backspace"),("Tab","Tab"),("Space","Space"),
        ("Minus","- _"),("Equal","= +"),("LeftBracket","[ {"),("RightBracket","] }"),("Backslash","\\ |"),
        ("NonUSHash","# ~"),("Semicolon","; :"),("Quote","' \""),("Grave","` ~"),("Comma",", <"),
        ("Dot",". >"),("Slash","/ ?"),("CapsLock","CapsLock"),
        ("F1","F1"),("F2","F2"),("F3","F3"),("F4","F4"),("F5","F5"),("F6","F6"),
        ("F7","F7"),("F8","F8"),("F9","F9"),("F10","F10"),("F11","F11"),("F12","F12"),
        ("PrintScreen","PrtSc"),("ScrollLock","ScrLk"),("Pause","Pause"),("Insert","Insert"),
        ("Home","Home"),("PageUp","PgUp"),("Delete","Delete"),("End","End"),("PageDown","PgDn"),
        ("Right","Right"),("Left","Left"),("Down","Down"),("Up","Up"),("NumLock","NumLock"),
        ("NumPadSlash","NP /"),("NumPadAsterisk","NP *"),("NumPadMinus","NP -"),("NumPadPlus","NP +"),
        ("NumPadEnter","NP Enter"),("NumPad1","NP 1"),("NumPad2","NP 2"),("NumPad3","NP 3"),
        ("NumPad4","NP 4"),("NumPad5","NP 5"),("NumPad6","NP 6"),("NumPad7","NP 7"),("NumPad8","NP 8"),
        ("NumPad9","NP 9"),("NumPad0","NP 0"),("NumPadDot","NP ."),("NonUSBackslash","NonUS \\"),
        ("Application","Menu"),("Power","Power"),("NumPadEqual","NP ="),
        ("F13","F13"),("F14","F14"),("F15","F15"),("F16","F16"),("F17","F17"),("F18","F18"),
        ("F19","F19"),("F20","F20"),("F21","F21"),("F22","F22"),("F23","F23"),("F24","F24"),
    };

    /// <summary>name -> HID usage code (0x04 + index).</summary>
    public static readonly IReadOnlyDictionary<string, byte> Codes;
    /// <summary>HID usage code -> name (reverse, for read parsing).</summary>
    public static readonly IReadOnlyDictionary<byte, string> CodeToName;
    /// <summary>name -> label.</summary>
    public static readonly IReadOnlyDictionary<string, string> Labels;

    // HID modifier bits (standard).
    public static readonly IReadOnlyDictionary<string, byte> Modifiers = new Dictionary<string, byte>
    {
        ["Ctrl"] = 0x01, ["Shift"] = 0x02, ["Alt"] = 0x04, ["Win"] = 0x08,
        ["RightCtrl"] = 0x10, ["RightShift"] = 0x20, ["RightAlt"] = 0x40, ["RightWin"] = 0x80,
    };

    // 16-bit consumer (media) usages.  name -> (code, label)
    public static readonly IReadOnlyDictionary<string, (ushort Code, string Label)> MediaCodes =
        new Dictionary<string, (ushort, string)>
    {
        ["Next"] = (0xb5, "다음 곡"), ["Previous"] = (0xb6, "이전 곡"), ["Stop"] = (0xb7, "정지"),
        ["Play"] = (0xcd, "재생/일시정지"), ["Mute"] = (0xe2, "음소거"),
        ["VolumeUp"] = (0xe9, "볼륨 +"), ["VolumeDown"] = (0xea, "볼륨 -"),
        ["Favorites"] = (0x182, "즐겨찾기"), ["Calculator"] = (0x192, "계산기"), ["ScreenLock"] = (0x19e, "화면 잠금"),
    };
    public static readonly IReadOnlyDictionary<ushort, string> MediaCodeToName;

    public static readonly IReadOnlyDictionary<string, byte> MouseButtons = new Dictionary<string, byte>
    {
        ["Left"] = 0x01, ["Right"] = 0x02, ["Middle"] = 0x04,
    };
    public static readonly IReadOnlyDictionary<string, byte> MouseModifiers = new Dictionary<string, byte>
    {
        ["Ctrl"] = 0x01, ["Shift"] = 0x02, ["Alt"] = 0x04,
    };

    static KeyCodes()
    {
        var codes = new Dictionary<string, byte>();
        var codeToName = new Dictionary<byte, string>();
        var labels = new Dictionary<string, string>();
        for (int i = 0; i < Order.Count; i++)
        {
            byte code = (byte)(0x04 + i);
            var (name, label) = Order[i];
            codes[name] = code;
            codeToName[code] = name;
            labels[name] = label;
        }
        Codes = codes; CodeToName = codeToName; Labels = labels;

        var m2n = new Dictionary<ushort, string>();
        foreach (var kv in MediaCodes) m2n[kv.Value.Code] = kv.Key;
        MediaCodeToName = m2n;
    }
}
