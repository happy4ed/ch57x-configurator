using System.Text.Json.Serialization;

namespace Ch57x.Core;

public enum BindingType { None, Key, Text, Media, Mouse }

/// <summary>One press in a keyboard macro sequence.</summary>
public sealed class Accord
{
    public List<string> Mods { get; set; } = new();
    public string? Code { get; set; }
}

/// <summary>A single key/knob-action binding. Mirrors the web JSON profile shape.</summary>
public sealed class Binding
{
    // serialized lowercase ("key"/"text"/"media"/"mouse"/"none") via global converter for web compat
    public BindingType Type { get; set; } = BindingType.None;

    /// <summary>User-defined short label shown in HUD/UI instead of the auto summary. Web-compatible.</summary>
    public string? Alias { get; set; }

    // key
    public List<Accord>? Steps { get; set; }
    public int Delay { get; set; }

    // text (상용구)
    public string? Text { get; set; }

    // media
    public string? Media { get; set; }

    // mouse
    public string? Action { get; set; }      // click | wheel | move | drag
    public List<string>? Mods { get; set; }  // mouse modifiers (multi)
    public List<string>? Buttons { get; set; }
    public int Dx { get; set; }
    public int Dy { get; set; }
    public int Delta { get; set; }

    public static Binding Key(string? code, IEnumerable<string>? mods = null, int delay = 0) =>
        new() { Type = BindingType.Key, Delay = delay,
                Steps = new() { new Accord { Code = code, Mods = mods?.ToList() ?? new() } } };
}

/// <summary>LED setting per layer (see PROTOCOL.md §6).</summary>
public sealed class LedSetting
{
    public int Mode { get; set; }   // 0 off,1 backlight,2 shock,3 shock2,4 press,5 white
    public int Color { get; set; } = 1; // 1..7
}

/// <summary>A full profile: 3 layers of keyId->Binding, plus per-layer LED.</summary>
public sealed class Profile
{
    public string Name { get; set; } = "내 프로필";
    // layers[layerIndex][keyId] = binding   (keyId per PROTOCOL.md §4: buttons 1.., knobs 16..)
    public List<Dictionary<int, Binding>> Layers { get; set; } =
        new() { new(), new(), new() };
    public List<LedSetting> Led { get; set; } =
        new() { new(), new(), new() };

    public int KeyCount { get; set; } = 9;
    public int KnobCount { get; set; } = 3;
}
