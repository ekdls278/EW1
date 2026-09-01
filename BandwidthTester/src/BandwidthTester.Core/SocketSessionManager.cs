using System.Collections.Concurrent;

namespace BandwidthTester.Core;

/// <summary>
/// Owns an open-ended set of <see cref="SocketWorker"/> instances (one per configured
/// socket) so the user can add/remove/start/stop any number of independently-configured
/// sockets at runtime.
/// </summary>
public sealed class SocketSessionManager
{
    private readonly ConcurrentDictionary<Guid, SocketWorker> _workers = new();

    public event Action<SocketWorker, string>? LogEmitted;
    public event Action<SocketWorker, SocketStats>? StatsUpdated;
    public event Action<SocketWorker, IReadOnlyDictionary<string, object>>? HeaderReceived;

    public IReadOnlyCollection<SocketWorker> Workers => _workers.Values.ToList();

    /// <summary>Adds a new socket profile (or replaces one with the same Id) without starting it.</summary>
    public SocketWorker Add(SocketProfile profile)
    {
        var worker = new SocketWorker(profile);
        worker.LogEmitted += (w, m) => LogEmitted?.Invoke(w, m);
        worker.StatsUpdated += (w, s) => StatsUpdated?.Invoke(w, s);
        worker.HeaderReceived += (w, h) => HeaderReceived?.Invoke(w, h);
        _workers[profile.Id] = worker;
        return worker;
    }

    public async Task RemoveAsync(Guid profileId)
    {
        if (_workers.TryRemove(profileId, out var worker))
            await worker.StopAsync().ConfigureAwait(false);
    }

    public SocketWorker? Get(Guid profileId) => _workers.GetValueOrDefault(profileId);

    public void Start(Guid profileId) => Get(profileId)?.Start();

    public Task StopAsync(Guid profileId) => Get(profileId)?.StopAsync() ?? Task.CompletedTask;

    public void StartAll()
    {
        foreach (var worker in _workers.Values)
            worker.Start();
    }

    public async Task StopAllAsync()
    {
        await Task.WhenAll(_workers.Values.Select(w => w.StopAsync())).ConfigureAwait(false);
    }

    /// <summary>Replaces the whole set of profiles with the given config, stopping anything removed.</summary>
    public async Task LoadAsync(AppConfig config)
    {
        await StopAllAsync().ConfigureAwait(false);
        _workers.Clear();
        foreach (var profile in config.Sockets)
            Add(profile);
    }

    public AppConfig ToConfig() => new()
    {
        Sockets = _workers.Values.Select(w => w.Profile).ToList()
    };
}
