using System.Text.Json;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.S3;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using SyncServer.Configuration;
using SyncServer.Content.Contracts;
using SyncServer.Content.Filters;
using SyncServer.Content.Plugins;
using SyncServer.Content.Registries;
using SyncServer.Content.Repositories;
using SyncServer.Identity.Filters;
using SyncServer.Identity.Middlewares;
using SyncServer.Identity.Repositories;
using SyncServer.Identity.Services;
using SyncServer.Infrastructure;


var builder = WebApplication.CreateBuilder(args);

//Logger
builder.Logging
        .ClearProviders()
        .AddJsonConsole();

// Add services to the container.

// ── Cloud Storage Settings (S3 / R2 / MinIO / etc.) ──
builder.Services.Configure<CloudStorageSettings>(opts =>
{
    builder.Configuration.GetSection("CloudStorage").Bind(opts);

    var envBucket = Environment.GetEnvironmentVariable("BUCKET");
    if (!string.IsNullOrEmpty(envBucket)) opts.BucketName = envBucket;

    var envRegion = Environment.GetEnvironmentVariable("REGION");
    if (!string.IsNullOrEmpty(envRegion)) opts.Region = envRegion;

    var envAccess = Environment.GetEnvironmentVariable("ACCESS_KEY");
    if (!string.IsNullOrEmpty(envAccess)) opts.AccessKey = envAccess;

    var envSecret = Environment.GetEnvironmentVariable("SECRET_KEY");
    if (!string.IsNullOrEmpty(envSecret)) opts.SecretKey = envSecret;

    var envServiceUrl = Environment.GetEnvironmentVariable("SERVICE_URL");
    if (!string.IsNullOrEmpty(envServiceUrl)) opts.ServiceUrl = envServiceUrl;

    var envForcePathStyle = Environment.GetEnvironmentVariable("FORCE_PATH_STYLE");
    if (bool.TryParse(envForcePathStyle, out var fps)) opts.ForcePathStyle = fps;
});


builder.Services.Configure<JwtSettings>(opts =>
{
    builder.Configuration.GetSection("JwtConstants").Bind(opts);

    var key = Environment.GetEnvironmentVariable("JWT_KEY");
    if (!string.IsNullOrEmpty(key)) opts.Key = key;

    var issuer = Environment.GetEnvironmentVariable("JWT_ISSUER");
    if (!string.IsNullOrEmpty(issuer)) opts.Issuer = issuer;

    var audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE");
    if (!string.IsNullOrEmpty(audience)) opts.Audience = audience;

    var dur = Environment.GetEnvironmentVariable("JWT_DURATION_HOURS");
    if (int.TryParse(dur, out var hours)) opts.DurationInHours = hours;
});


builder.Services
        .AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });

var storageSettings = builder.Configuration.GetSection("CloudStorage").Get<CloudStorageSettings>() ?? new CloudStorageSettings();

// Apply the same env overrides used in Configure<CloudStorageSettings> for early use
var envBucketEarly = Environment.GetEnvironmentVariable("BUCKET");
if (!string.IsNullOrEmpty(envBucketEarly)) storageSettings.BucketName = envBucketEarly;
var envRegionEarly = Environment.GetEnvironmentVariable("REGION");
if (!string.IsNullOrEmpty(envRegionEarly)) storageSettings.Region = envRegionEarly;
var envAccessEarly = Environment.GetEnvironmentVariable("ACCESS_KEY");
if (!string.IsNullOrEmpty(envAccessEarly)) storageSettings.AccessKey = envAccessEarly;
var envSecretEarly = Environment.GetEnvironmentVariable("SECRET_KEY");
if (!string.IsNullOrEmpty(envSecretEarly)) storageSettings.SecretKey = envSecretEarly;
var envServiceUrlEarly = Environment.GetEnvironmentVariable("SERVICE_URL");
if (!string.IsNullOrEmpty(envServiceUrlEarly)) storageSettings.ServiceUrl = envServiceUrlEarly;
var envForcePathStyleEarly = Environment.GetEnvironmentVariable("FORCE_PATH_STYLE");
if (bool.TryParse(envForcePathStyleEarly, out var fpsEarly)) storageSettings.ForcePathStyle = fpsEarly;

// Build shared credentials from config
var awsCredentials = (!string.IsNullOrEmpty(storageSettings.AccessKey) && !string.IsNullOrEmpty(storageSettings.SecretKey))
    ? new Amazon.Runtime.BasicAWSCredentials(storageSettings.AccessKey, storageSettings.SecretKey)
    : null;

// ── Core Infrastructure: DynamoDB ──
var dynamoConfig = new AmazonDynamoDBConfig
{
    RegionEndpoint = RegionEndpoint.GetBySystemName(storageSettings.Region)
};
builder.Services
        .AddSingleton<IAmazonDynamoDB>(awsCredentials != null
            ? new AmazonDynamoDBClient(awsCredentials, dynamoConfig)
            : new AmazonDynamoDBClient(dynamoConfig))
        .AddScoped<IDynamoDBContext, DynamoDBContext>();

// ── Identity ──
builder.Services
        .AddScoped<IUserRepository, UserRepository>()
        .AddHttpContextAccessor()
        .AddScoped<ICurrentUserService, CurrentUserService>();

// ── Content Plugin Infrastructure ──
// Register individual plugins (add new plugins here)
builder.Services.AddSingleton<IContentPlugin, AudioPlugin>();
builder.Services.AddSingleton<IContentPlugin, VideoPlugin>();
builder.Services.AddSingleton<IContentPlugin, BookPlugin>();
builder.Services.AddSingleton<IContentPlugin, MangaPlugin>();
builder.Services.AddSingleton<IContentPlugin, ImagePlugin>();

// Build the plugin registry from all registered IContentPlugin instances
builder.Services.BuildContentPluginRegistry();

// Generic content repository used by all plugins
builder.Services.AddScoped<IContentRepository, ContentRepository>();

// ── Blob Storage (S3-compatible) ──
builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var s3Config = new AmazonS3Config();
    if (!string.IsNullOrEmpty(storageSettings.ServiceUrl))
    {
        s3Config.ServiceURL = storageSettings.ServiceUrl;
        s3Config.ForcePathStyle = storageSettings.ForcePathStyle;
    }
    else if (!string.IsNullOrEmpty(storageSettings.Region))
    {
        s3Config.RegionEndpoint = RegionEndpoint.GetBySystemName(storageSettings.Region);
    }
    return awsCredentials != null
        ? new AmazonS3Client(awsCredentials, s3Config)
        : new AmazonS3Client(s3Config);
});
builder.Services.AddSingleton<IBlobStorageProvider, S3BlobStorageProvider>();

// JWT Authentication
var jwtConstants = builder.Configuration.GetSection("JwtConstants").Get<JwtSettings>() ?? new JwtSettings();
var envKey = Environment.GetEnvironmentVariable("JWT_KEY");
if (!string.IsNullOrEmpty(envKey)) jwtConstants.Key = envKey;
var envIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER");
if (!string.IsNullOrEmpty(envIssuer)) jwtConstants.Issuer = envIssuer;
var envAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE");
if (!string.IsNullOrEmpty(envAudience)) jwtConstants.Audience = envAudience;

// Fail fast if JWT key is missing or too short
if (string.IsNullOrWhiteSpace(jwtConstants.Key) || Encoding.UTF8.GetByteCount(jwtConstants.Key) < 32)
{
    throw new InvalidOperationException(
        "JWT signing key must be at least 32 bytes. Set the JWT_KEY environment variable or configure JwtConstants:Key in appsettings.");
}

var keyBytes = Encoding.UTF8.GetBytes(jwtConstants.Key);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtConstants.Issuer,
        ValidAudience = jwtConstants.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes)
    };
});

// Require authentication by default (all endpoints) unless [AllowAnonymous]
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// Add Swagger services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SyncServer", Version = "v1" });
    c.SchemaFilter<CreateUserRequestSchemaFilter>();
    c.SchemaFilter<CreateLoginRequestSchemaFilter>();
    c.SchemaFilter<CreateContentUploadInitRequestSchemaFilter>();
    c.SchemaFilter<CreateContentUploadCompleteRequestSchemaFilter>();
    c.SchemaFilter<CreateContentItemSchemaFilter>();
    c.SchemaFilter<CreateContentDeleteRequestSchemaFilter>();
    // Add JWT auth to swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: 'Bearer {token}'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });
    var schemeRef = new OpenApiSecuritySchemeReference("Bearer", null);
    c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", doc), new List<string>() }
    });
});


var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
// Only enable Swagger in development
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "SyncServer V1");
        c.RoutePrefix = "swagger"; // serve at /swagger
    });
}

app.UseAuthentication();
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
// Attach current user to context
app.UseMiddleware<CurrentUserMiddleware>();

app.UseAuthorization();
app.MapControllers();

app.MapGet("/", () => "Welcome to running ASP.NET Core on Kestrel").AllowAnonymous();

app.Run();