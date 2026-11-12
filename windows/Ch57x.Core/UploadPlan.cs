namespace Ch57x.Core;

/// <summary>업로드 계획 결과 — 보낼 패킷과 사람에게 보일 숫자.</summary>
/// <param name="Mine">이 프로필이 보낸 키 수</param>
/// <param name="Kept">키보드에서 읽은 값을 그대로 되쓴 키 수 (프로필에 없는 키)</param>
/// <param name="Cleared">프로필에서 「비움」으로 둔 키 수 (보내지 않음 · 되쓰지도 않음)</param>
/// <param name="LayersUsed">이 프로필이 키를 보낸 레이어 수</param>
public sealed record UploadPlanResult(List<byte[]> Messages, int Mine, int Kept, int Cleared, int LayersUsed);

/// <summary>
/// 업로드 보존 (판정 S1) — 웹 설정기 <c>web/js/app.js upload()</c> · <c>protocol.js uploadProfile(… { preserve })</c> 와 같은 방식.
/// 펌웨어는 ★보내지 않은 키를 기본값으로 되돌린다(docs/PROTOCOL.md §2). 그래서 올리기 전에 키보드를 읽고,
/// 프로필이 건드리지 않는 키는 ★읽은 값 그대로 되쓴다.
///
/// 웹과 다른 점 하나: 프로필에서 「이 키 비우기」로 둔 키(<see cref="BindingType.None"/>)는 프로필이 그 키를 「가진」 것으로 본다 —
/// 읽은 값으로 되살리지 않는다(사용자가 비운 키가 업로드 뒤 다시 살아나지 않게). 보내지는 않으므로 펌웨어 기본값이 된다(0.2.1 과 같음).
/// </summary>
public static class UploadPlan
{
    public static UploadPlanResult Build(Profile p, IReadOnlyDictionary<int, Dictionary<int, Binding>>? deviceRead)
    {
        var msgs = new List<byte[]>();
        var owned = new HashSet<(int Layer, int Key)>();
        var layersUsed = new HashSet<int>();
        int mine = 0, cleared = 0, kept = 0;

        for (int layer = 0; layer < p.Layers.Count; layer++)
            foreach (var (keyId, b) in p.Layers[layer].OrderBy(kv => kv.Key))
            {
                if (b == null || b.Type == BindingType.None) { owned.Add((layer, keyId)); cleared++; continue; }
                var m = Protocol.BuildKeyMessages((byte)keyId, layer, b);
                if (m.Count == 0) continue;            // 보낼 것이 없는 값(빈 상용구 등) = 웹처럼 기기 값 보존 대상
                owned.Add((layer, keyId));
                msgs.AddRange(m);
                mine++;
                layersUsed.Add(layer);
            }

        if (deviceRead != null)
            foreach (var (layer, keys) in deviceRead.OrderBy(kv => kv.Key))
                foreach (var (keyId, b) in keys.OrderBy(kv => kv.Key))
                {
                    if (owned.Contains((layer, keyId)) || b == null || b.Type == BindingType.None) continue;
                    var m = Protocol.BuildKeyMessages((byte)keyId, layer, b);
                    if (m.Count == 0) continue;
                    msgs.AddRange(m);
                    kept++;
                }

        // LED 레이어마다 (끄기 · 흰색은 색 0) — PROTOCOL §6. LED 는 되읽기가 없어 늘 보낸다.
        for (int layer = 0; layer < p.Led.Count; layer++)
        {
            var led = p.Led[layer];
            int color = (led.Mode == 0 || led.Mode == 5) ? 0 : led.Color;
            msgs.AddRange(Protocol.BuildLedMessages(layer, led.Mode, color));
        }
        return new UploadPlanResult(msgs, mine, kept, cleared, layersUsed.Count);
    }
}
