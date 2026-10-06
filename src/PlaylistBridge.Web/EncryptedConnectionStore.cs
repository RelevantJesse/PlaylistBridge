using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace PlaylistBridge.Web;

public sealed class EncryptedConnectionStore
{
    private readonly object _gate = new();
    private readonly IDataProtector _protector;
    private readonly ILogger<EncryptedConnectionStore> _logger;
    private readonly string _filePath;
    private PersistentConnectionState _state;

    public EncryptedConnectionStore(
        IDataProtectionProvider protectionProvider,
        ILogger<EncryptedConnectionStore> logger)
    {
        _protector = protectionProvider.CreateProtector("PlaylistBridge.ConnectionCredentials.v1");
        _logger = logger;
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlaylistBridge");
        _filePath = Path.Combine(directory, "connections.dat");
        _state = Load();
    }

    public StoredServiceCredentials ServiceCredentials
    {
        get { lock (_gate) return _state.ServiceCredentials; }
    }

    public IReadOnlyDictionary<string, SpotifyOAuthSession> SpotifySessions
    {
        get { lock (_gate) return new Dictionary<string, SpotifyOAuthSession>(_state.SpotifySessions); }
    }

    internal void SaveSpotifyHandshake(string state, PendingSpotifyHandshake handshake) => Update(current =>
    {
        var handshakes = new Dictionary<string, PendingSpotifyHandshake>(current.SpotifyHandshakes)
        {
            [state] = handshake
        };
        return current with { SpotifyHandshakes = handshakes };
    });

    internal PendingSpotifyHandshake? TakeSpotifyHandshake(string state)
    {
        lock (_gate)
        {
            if (!_state.SpotifyHandshakes.TryGetValue(state, out var handshake)) return null;
            var handshakes = new Dictionary<string, PendingSpotifyHandshake>(_state.SpotifyHandshakes);
            handshakes.Remove(state);
            _state = _state with { SpotifyHandshakes = handshakes };
            PersistUnsafe();
            return handshake.ExpiresAt > DateTimeOffset.UtcNow ? handshake : null;
        }
    }

    public void SaveServiceCredentials(StoredServiceCredentials credentials) =>
        Update(state => state with { ServiceCredentials = credentials });

    public void SaveSpotifySession(string id, SpotifyOAuthSession session) => Update(state =>
    {
        var sessions = new Dictionary<string, SpotifyOAuthSession>(state.SpotifySessions) { [id] = session };
        return state with { SpotifySessions = sessions };
    });

    public void RemoveSpotifySession(string id) => Update(state =>
    {
        var sessions = new Dictionary<string, SpotifyOAuthSession>(state.SpotifySessions);
        sessions.Remove(id);
        return state with { SpotifySessions = sessions };
    });

    private PersistentConnectionState Load()
    {
        if (!File.Exists(_filePath)) return new();
        try
        {
            var protectedJson = File.ReadAllText(_filePath);
            var json = _protector.Unprotect(protectedJson);
            return JsonSerializer.Deserialize<PersistentConnectionState>(json) ?? new();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Saved connections could not be decrypted; starting with an empty credential store");
            return new();
        }
    }

    private void Update(Func<PersistentConnectionState, PersistentConnectionState> update)
    {
        lock (_gate)
        {
            _state = update(_state);
            PersistUnsafe();
        }
    }

    private void PersistUnsafe()
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var protectedJson = _protector.Protect(JsonSerializer.Serialize(_state));
        var temporaryPath = _filePath + ".tmp";
        File.WriteAllText(temporaryPath, protectedJson);
        File.Move(temporaryPath, _filePath, true);
    }
}

internal sealed record PersistentConnectionState
{
    public StoredServiceCredentials ServiceCredentials { get; init; } = new();
    public IReadOnlyDictionary<string, SpotifyOAuthSession> SpotifySessions { get; init; } =
        new Dictionary<string, SpotifyOAuthSession>();
    public IReadOnlyDictionary<string, PendingSpotifyHandshake> SpotifyHandshakes { get; init; } =
        new Dictionary<string, PendingSpotifyHandshake>();
}

public sealed record StoredServiceCredentials(
    string? AppleDeveloperToken = null,
    string? AppleUserToken = null,
    string Storefront = "us",
    string? YouTubeAccessToken = null,
    DateTimeOffset? YouTubeAccessTokenExpiresAt = null,
    string? YouTubeClientId = null,
    string? YouTubeClientSecret = null,
    string? YouTubeRefreshToken = null);
