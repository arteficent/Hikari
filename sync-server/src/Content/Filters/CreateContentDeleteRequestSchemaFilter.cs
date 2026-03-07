using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using SyncServer.Content.Dtos;

namespace SyncServer.Content.Filters;

public class CreateContentDeleteRequestSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(ContentDeleteRequest))
            return;

        if (schema is not OpenApiSchema openApiSchema)
            return;

        openApiSchema.Example = JsonNode.Parse(
            """
            {
              "items": [
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "contentType": "music",
                  "title": "Dream Song",
                  "format": "audio/mpeg",
                  "storagePath": "music/song/Yume/Nightfall/Dream-Song.mp3",
                  "metadata": {
                    "artist": "Yume",
                    "album": "Nightfall",
                    "genre": "Ambient",
                    "musicFormat": "AudioMpeg"
                  }
                }
              ]
            }
            """
        );
    }
}
