using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;

namespace Mideej.Benchmarks
{
    [MemoryDiagnoser]
    public class SessionRefreshBenchmarks
    {
        private ConcurrentDictionary<string, MockWrapper> _sessionsLinear = default!;
        private ConcurrentDictionary<string, MockWrapper> _sessionsIndexed = default!;
        private Dictionary<int, string> _processIndex = default!; // processId -> sessionId
        private List<(string sessionId, int processId, string displayName)> _incoming = default!;

        [Params(100, 500, 2000)]
        public int SessionCount;

        [Params(0.1, 0.25)]
        public double ChurnRatio; // portion of sessions that change sessionId (force migration)

        [GlobalSetup]
        public void Setup()
        {
            _sessionsLinear = new ConcurrentDictionary<string, MockWrapper>();
            _sessionsIndexed = new ConcurrentDictionary<string, MockWrapper>();
            _processIndex = new Dictionary<int, string>(SessionCount);

            // Seed existing sessions
            for (int i = 0; i < SessionCount; i++)
            {
                var pid = 1000 + i;
                var sid = $"{pid}_sess_{i}";
                var wrapper = new MockWrapper
                {
                    Info = new MockInfo { ProcessId = pid, DisplayName = $"App{i}" },
                    LastSeen = DateTime.UtcNow
                };
                _sessionsLinear.TryAdd(sid, wrapper);
                _sessionsIndexed.TryAdd(sid, new MockWrapper
                {
                    Info = new MockInfo { ProcessId = pid, DisplayName = $"App{i}" },
                    LastSeen = wrapper.LastSeen
                });
                _processIndex[pid] = sid;
            }

            // Create incoming snapshot with churn (some sessionIds change but keep same processId)
            var churnCount = (int)(SessionCount * ChurnRatio);
            _incoming = new List<(string sessionId, int processId, string displayName)>(SessionCount);

            for (int i = 0; i < SessionCount; i++)
            {
                var pid = 1000 + i;
                var changed = i < churnCount; // first N churn
                var sid = changed ? $"{pid}_sess_new_{i}" : $"{pid}_sess_{i}";
                _incoming.Add((sid, pid, $"App{i}"));
            }
        }

        [Benchmark(Baseline = true)]
        public (int updated, int migrated, int added) Refresh_WithLinearSearch()
        {
            int updated = 0, migrated = 0, added = 0;
            var now = DateTime.UtcNow;

            // Simulate the algorithm currently used: FirstOrDefault over _sessions when key missing
            foreach (var (sessionId, processId, displayName) in _incoming)
            {
                if (_sessionsLinear.ContainsKey(sessionId))
                {
                    var w = _sessionsLinear[sessionId];
                    w.LastSeen = now;
                    updated++;
                }
                else
                {
                    var migrate = _sessionsLinear.FirstOrDefault(kvp => kvp.Value.Info.ProcessId == processId);
                    if (!string.IsNullOrEmpty(migrate.Key))
                    {
                        var w = migrate.Value;
                        w.LastSeen = now;
                        // move key
                        _sessionsLinear.TryRemove(migrate.Key, out _);
                        _sessionsLinear[sessionId] = w;
                        migrated++;
                    }
                    else
                    {
                        _sessionsLinear[sessionId] = new MockWrapper
                        {
                            Info = new MockInfo { ProcessId = processId, DisplayName = displayName },
                            LastSeen = now
                        };
                        added++;
                    }
                }
            }
            return (updated, migrated, added);
        }

        [Benchmark]
        public (int updated, int migrated, int added) Refresh_WithProcessIndex()
        {
            int updated = 0, migrated = 0, added = 0;
            var now = DateTime.UtcNow;

            foreach (var (sessionId, processId, displayName) in _incoming)
            {
                if (_sessionsIndexed.ContainsKey(sessionId))
                {
                    var w = _sessionsIndexed[sessionId];
                    w.LastSeen = now;
                    updated++;
                }
                else if (_processIndex.TryGetValue(processId, out var oldKey))
                {
                    var w = _sessionsIndexed[oldKey];
                    w.LastSeen = now;
                    _sessionsIndexed.TryRemove(oldKey, out _);
                    _sessionsIndexed[sessionId] = w;
                    _processIndex[processId] = sessionId;
                    migrated++;
                }
                else
                {
                    _sessionsIndexed[sessionId] = new MockWrapper
                    {
                        Info = new MockInfo { ProcessId = processId, DisplayName = displayName },
                        LastSeen = now
                    };
                    _processIndex[processId] = sessionId;
                    added++;
                }
            }
            return (updated, migrated, added);
        }

        private sealed class MockWrapper
        {
            public MockInfo Info { get; set; } = null!;
            public DateTime LastSeen { get; set; }
        }

        private sealed class MockInfo
        {
            public int ProcessId { get; set; }
            public string DisplayName { get; set; } = string.Empty;
        }
    }
}