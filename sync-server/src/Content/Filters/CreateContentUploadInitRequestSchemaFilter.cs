using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using SyncServer.Content.Dtos;

namespace SyncServer.Content.Filters;

public class CreateContentUploadInitRequestSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(ContentUploadInitRequest))
            return;

        if (schema is not OpenApiSchema openApiSchema)
            return;

        openApiSchema.Example = JsonNode.Parse(
            """
            {
              "item": {
                "title": "Araragi Koyomi. Mita Manma no Otoko sa",
                "description": "Araragi Koyomi. Mita Manma no Otoko sa",
                "format": "AudioFlac",
                "sizeInBytes": 22855771,
                "metadata": {
                  "artist": "Yume",
                  "album": "Nightfall",
                  "genre": "Ambient",
                  "releaseDate": "2025-01-01",
                  "duration": "3:30",
                  "bitrate": "320",
                  "musicFormat": "AudioFlac"
                }
              },
              "urlExpiresInMinutes": 15
            }
            """
        );
    }
}
