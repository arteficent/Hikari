using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using SyncServer.Content.Models;
using SyncServer.Identity.Models;

namespace SyncServer.Infrastructure.Mongo;

/// <summary>
/// Centralised MongoDB serialization setup. The domain models keep their DynamoDB
/// attributes (which Mongo ignores) and stay free of Mongo attributes; their BSON
/// mapping is configured here instead, so the two persistence backends remain decoupled.
///
/// <see cref="Register"/> is idempotent and must be invoked once during startup before any
/// collection is used — only when the MongoDB provider is selected.
/// </summary>
public static class MongoMappings
{
    private static readonly object Gate = new();
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        lock (Gate)
        {
            if (_registered) return;

            // GUIDs (ContentItem.Id, User.Playlist) — pin a representation explicitly because
            // the v3 driver no longer assumes one and would otherwise throw at runtime.
            BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

            RegisterClassMap<User>(cm =>
            {
                cm.AutoMap();
                cm.MapIdMember(u => u.Id);
                cm.SetIgnoreExtraElements(true);
            });

            RegisterClassMap<RefreshTokenRecord>(cm =>
            {
                cm.AutoMap();
                // The SHA-256 token hash is the natural key — use it as _id.
                cm.MapIdMember(r => r.TokenHash);
                cm.SetIgnoreExtraElements(true);
            });

            RegisterClassMap<ContentItem>(cm =>
            {
                cm.AutoMap();
                cm.MapIdMember(c => c.Id);
                cm.SetIgnoreExtraElements(true);
            });

            _registered = true;
        }
    }

    private static void RegisterClassMap<T>(Action<BsonClassMap<T>> configure)
    {
        if (!BsonClassMap.IsClassMapRegistered(typeof(T)))
            BsonClassMap.RegisterClassMap(configure);
    }
}
