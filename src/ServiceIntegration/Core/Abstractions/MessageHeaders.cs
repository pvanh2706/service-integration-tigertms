using System.Collections.Concurrent;

namespace ServiceIntegration.Core.Abstractions;

public sealed class MessageHeaders
{
    private readonly ConcurrentDictionary<string, object> _headers = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string key, object value) => _headers[key] = value;
    public bool TryGet(string key, out object? value) => _headers.TryGetValue(key, out value);
    public IReadOnlyDictionary<string, object> AsReadOnly() => _headers;

    public string GetString(string key, string defaultValue = "")
        => TryGet(key, out var v) ? v?.ToString() ?? defaultValue : defaultValue;

    public int GetInt(string key, int defaultValue = 0)
    {
        if (!TryGet(key, out var v) || v is null) return defaultValue;
        if (v is int i) return i;
        if (int.TryParse(v.ToString(), out var parsed)) return parsed;
        return defaultValue;
    }
}
