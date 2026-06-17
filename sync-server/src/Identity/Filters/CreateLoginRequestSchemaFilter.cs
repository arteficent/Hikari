using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using SyncServer.Identity.Dtos;
using System.Text.Json.Nodes;

namespace SyncServer.Identity.Filters
{
    public class CreateLoginRequestSchemaFilter: ISchemaFilter
    {
        public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
        {
            if (context.Type != typeof(LoginRequest))
                return;

            if (schema is not OpenApiSchema openApiSchema)
                return;

            openApiSchema.Example = JsonNode.Parse(
                """
                {
                  "username": "user",
                  "password": "pass"
                }
                """
            );
        }
    }
}
