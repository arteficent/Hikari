using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using SyncServer.Content.Dtos;

namespace SyncServer.Content.Filters;

public class CreateContentUploadCompleteRequestSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(ContentUploadCompleteRequest))
            return;

        if (schema is not OpenApiSchema openApiSchema)
            return;

        openApiSchema.Example = JsonNode.Parse(
            """
            {
              "item": {
                "title": "Dream Song",
                "contentType": "music",
                "format": "audio/mpeg",
                "storagePath": "music/song/Yume/Nightfall/Dream-Song.mp3",
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
            }
            """
        );
    }
}
