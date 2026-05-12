using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using SyncServer.Identity.Dtos;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SyncServer.Identity.Filters
{
    public class CreateUserRequestSchemaFilter : ISchemaFilter
    {
        public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
        {
            if (context.Type != typeof(CreateUserRequest))
                return;

            if (schema is not OpenApiSchema openApiSchema)
                return;

            openApiSchema.Example = JsonNode.Parse(
                """
                {
                  "email": "root",
                  "password": "Root123!",
                  "roles": [0, 1, 2]
                }
                """
            );
        }
    }
}
