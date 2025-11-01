using System.Collections.Concurrent;
using System.Diagnostics;

namespace Mideej.Helpers;

/// <summary>
/// Caches process names to avoid repeated expensive lookups and exceptions
/// Similar to DeejNG's AudioUtilities.GetProcessNameSafely
/// </summary>
public static class ProcessNameCache
{
    private static readonly ConcurrentDictionary<int, string> _cache = new();
    private static DateTime _lastCleanup = DateTime.UtcNow;
    private const int CleanupIntervalSeconds = 60;
    private const int MaxCacheSize = 100;

    /// <summary>
    /// Gets process name safely with caching. Returns empty string on failure.
    /// </summary>
    public static string GetProcessName(int processId)
    {
        // Clean cache periodically
        if ((DateTime.UtcNow - _lastCleanup).TotalSeconds > CleanupIntervalSeconds)
        {
            CleanCache();
        }

        // Return from cache if available
        if (_cache.TryGetValue(processId, out var cachedName))
        {
            return cachedName;
        }

        // Skip system processes
        if (processId <= 0 || processId < 100)
        {
            _cache[processId] = "";
            return "";
        }

        string processName = "";
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process != null && !process.HasExited)
            {
                processName = process.ProcessName?.ToLowerInvariant() ?? "";
            }
        }
        catch
        {
            // Process may have exited or access denied - cache empty string
            processName = "";
        }

        // Cache the result (even empty to avoid repeated failures)
        _cache[processId] = processName;
        return processName;
    }

    private static void CleanCache()
    {
        _lastCleanup = DateTime.UtcNow;

        // If cache is too large, clear old entries
        if (_cache.Count > MaxCacheSize)
        {
            var toRemove = _cache.Keys.Take(_cache.Count - MaxCacheSize / 2).ToList();
            foreach (var key in toRemove)
            {
                _cache.TryRemove(key, out _);
            }
        }
    }
}
