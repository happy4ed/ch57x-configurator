using HidSharp;

namespace Ch57x.Core;

/// <summary>
/// HID transport for a CH57x macro keyboard via HidSharp. Cross-platform; on Windows
/// uses the OS HID stack (no USBDK driver needed). Mirrors the WebHID layer.
/// </summary>
public sealed class Ch57xDevice : IDisposable
{
    private readonly HidDevice _device;
    private HidStream? _stream;

    public string Name => _device.GetFriendlyName() ?? _device.GetProductName() ?? "CH57x";
    public bool IsOpen => _stream != null;

    private Ch57xDevice(HidDevice device) => _device = device;

    /// <summary>Find the programming interface (vendor-defined, max output report length).</summary>
    public static Ch57xDevice? Find()
    {
        var list = DeviceList.Local;
        HidDevice? best = null;
        foreach (var pid in Protocol.ProductIds)
        {
            // pick the interface that accepts our 64-byte output reports
            foreach (var d in list.GetHidDevices(Protocol.VendorId, pid))
            {
                int outLen = d.GetMaxOutputReportLength();
                if (outLen >= 33) // report-id + payload
                {
                    if (best == null || outLen > best.GetMaxOutputReportLength()) best = d;
                }
            }
            if (best != null) break;
        }
        return best == null ? null : new Ch57xDevice(best);
    }

    public void Open()
    {
        if (_stream != null) return;
        var cfg = new OpenConfiguration();
        cfg.SetOption(OpenOption.Exclusive, false);
        _stream = _device.Open(cfg);
        _stream.ReadTimeout = 1000;
    }

    private void Send(byte[] packet64)
    {
        if (_stream == null) throw new InvalidOperationException("장치가 열려있지 않습니다");
        // HidSharp expects report-id as byte 0; packet64 already starts with 0x03.
        _stream.Write(packet64);
    }

    /// <summary>Upload the whole profile: every bound key across all layers + LED, with commit sequences.</summary>
    public int UploadProfile(Profile p, Action<int, int>? onProgress = null)
    {
        var all = new List<byte[]>();
        for (int layer = 0; layer < p.Layers.Count; layer++)
            foreach (var kv in p.Layers[layer])
                all.AddRange(Protocol.BuildKeyMessages((byte)kv.Key, layer, kv.Value));

        for (int layer = 0; layer < p.Led.Count; layer++)
        {
            var led = p.Led[layer];
            int color = (led.Mode == 0 || led.Mode == 5) ? 0 : led.Color;
            all.AddRange(Protocol.BuildLedMessages(layer, led.Mode, color));
        }

        for (int i = 0; i < all.Count; i++) { Send(all[i]); onProgress?.Invoke(i + 1, all.Count); }
        return all.Count;
    }

    /// <summary>Send a single key binding immediately (used by host-remapping live updates).</summary>
    public void WriteBinding(byte keyId, int layer, Binding binding)
    {
        foreach (var m in Protocol.BuildKeyMessages(keyId, layer, binding)) Send(m);
    }

    /// <summary>Read all 3 layers. Returns dict[layer (0-based)] -> dict[keyId -> Binding].</summary>
    public Dictionary<int, Dictionary<int, Binding>> ReadProfile(Action<int, int>? onProgress = null)
    {
        if (_stream == null) throw new InvalidOperationException("장치가 열려있지 않습니다");
        var result = new Dictionary<int, Dictionary<int, Binding>>();
        int inLen = Math.Max(_device.GetMaxInputReportLength(), 33);

        for (int layer = 1; layer <= 3; layer++)
        {
            Send(Protocol.ReadRequest(layer));
            var layerMap = result[layer - 1] = new();
            var deadline = DateTime.UtcNow.AddMilliseconds(500);
            while (DateTime.UtcNow < deadline)
            {
                var buf = new byte[inLen];
                int n;
                try { n = _stream.Read(buf); } catch (TimeoutException) { break; }
                if (n <= 0) break;
                // HidSharp returns report-id at byte 0 (=3); response payload begins with 0xFA at byte 1
                var span = buf[0] == Protocol.ReportId ? buf.AsSpan(1, n - 1) : buf.AsSpan(0, n);
                var parsed = Protocol.ParseReadResponse(span);
                if (parsed is { } r && r.Binding != null) layerMap[r.KeyId] = r.Binding;
            }
            onProgress?.Invoke(layer, 3);
        }
        return result;
    }

    /// <summary>Query physical key/knob count via 0xFB. Null if device doesn't answer.</summary>
    public (int KeyCount, int KnobCount)? ReadDeviceInfo()
    {
        if (_stream == null) return null;
        Send(Protocol.DeviceInfoRequest());
        int inLen = Math.Max(_device.GetMaxInputReportLength(), 33);
        var deadline = DateTime.UtcNow.AddMilliseconds(700);
        while (DateTime.UtcNow < deadline)
        {
            var buf = new byte[inLen];
            int n;
            try { n = _stream.Read(buf); } catch (TimeoutException) { break; }
            if (n <= 0) break;
            var span = buf[0] == Protocol.ReportId ? buf.AsSpan(1) : buf.AsSpan();
            if (span.Length >= 3 && span[0] == 0xfb) return (span[1], span[2]);
        }
        return null;
    }

    public void Dispose() { _stream?.Dispose(); _stream = null; }
}
