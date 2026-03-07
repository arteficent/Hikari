using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using SyncServer.Content.Models;

namespace SyncServer.Content.Filters;

public class CreateContentItemSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(ContentItem))
            return;

        if (schema is not OpenApiSchema openApiSchema)
            return;

        openApiSchema.Example = JsonNode.Parse(
            """
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "contentType": "music",
              "title": "Dream Song",
              "description": "A dreamy ambient track.",
              "format": "audio/mpeg",
              "sizeInBytes": 123456,
              "storagePath": "music/song/Yume/Nightfall/Dream-Song.mp3",
              "tags": ["chill", "sleep"],
              "metadata": {
                "artist": "Yume",
                "album": "Nightfall",
                "genre": "Ambient",
                "releaseDate": "2025-01-01",
                "duration": "3:30",
                "bitrate": "320",
                "musicFormat": "AudioMpeg"
              }
            }
            """
        );
    }
}
