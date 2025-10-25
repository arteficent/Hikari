using System.Text.Json;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Lambda.Abstraction;
using ServerlessAPI.Repositories;


var builder = WebApplication.CreateBuilder(args);

//Logger
builder.Logging
        .ClearProviders()
        .AddJsonConsole();

// Add services to the container.
builder.Services.Configure<AmazonWebServicesConstants>(builder.Configuration.GetSection("AmazonWebServiceConstants"));
builder.Services
        .AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });

string region = Environment.GetEnvironmentVariable("AWS_REGION") ?? RegionEndpoint.APSouth1.SystemName;
builder.Services
        .AddSingleton<IAmazonDynamoDB>(new AmazonDynamoDBClient(RegionEndpoint.GetBySystemName(region)))
        .AddScoped<IDynamoDBContext, DynamoDBContext>()
        .AddScoped<IMusicRepository, MusicRepository>();

// Remove Lambda hosting registration. Keep AWS SDK configuration and services for S3/DynamoDB
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<Amazon.S3.IAmazonS3>();
// Add Swagger services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


var app = builder.Build();

// Enable Swagger middleware in development
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/", () => "Welcome to running ASP.NET Core on Kestrel");

app.Run();