namespace Ch57x.Core;

/// <summary>실행 취소 (판정 S5) — 바꾸기 전의 프로필을 통째로 기억한다(작은 JSON · 최대 <see cref="Limit"/> 단계).</summary>
public sealed class ProfileHistory
{
    public const int Limit = 50;
    private readonly List<(string Json, string Label)> _stack = new();

    public bool CanUndo => _stack.Count > 0;
    public int Count => _stack.Count;
    /// <summary>되돌릴 일의 이름(단추 도움말 「실행 취소 — 키 3 바꿈」)</summary>
    public string? NextLabel => _stack.Count > 0 ? _stack[^1].Label : null;

    /// <summary>바꾸기 직전에 부른다.</summary>
    public void Push(Profile before, string label)
    {
        _stack.Add((ProfileStore.Serialize(before), label));
        if (_stack.Count > Limit) _stack.RemoveAt(0);
    }

    /// <summary>마지막 바꾸기 전 프로필. 없으면 null.</summary>
    public Profile? Undo()
    {
        if (_stack.Count == 0) return null;
        var (json, _) = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        return ProfileStore.Deserialize(json);
    }

    public void Clear() => _stack.Clear();
}
