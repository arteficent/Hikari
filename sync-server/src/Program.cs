using System.Text.Json;
using System.Text.Json.Serialization;
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

// ── Object Storage Settings (S3 / Cloudflare R2 / MinIO / etc.) ──
builder.Services.Configure<ObjectStorageSettings>(opts =>
{
    builder.Configuration.GetSection("ObjectStorage").Bind(opts);

    var envBucket = Environment.GetEnvironmentVariable("OBJECT_STORAGE_BUCKET");
    if (!string.IsNullOrEmpty(envBucket)) opts.BucketName = envBucket;

    var envRegion = Environment.GetEnvironmentVariable("OBJECT_STORAGE_REGION");
    if (!string.IsNullOrEmpty(envRegion)) opts.Region = envRegion;

    var envAccess = Environment.GetEnvironmentVariable("OBJECT_STORAGE_ACCESS_KEY");
    if (!string.IsNullOrEmpty(envAccess)) opts.AccessKey = envAccess;

    var envSecret = Environment.GetEnvironmentVariable("OBJECT_STORAGE_SECRET_KEY");
    if (!string.IsNullOrEmpty(envSecret)) opts.SecretKey = envSecret;

    var envServiceUrl = Environment.GetEnvironmentVariable("OBJECT_STORAGE_SERVICE_URL");
    if (!string.IsNullOrEmpty(envServiceUrl)) opts.ServiceUrl = envServiceUrl;

    var envForcePathStyle = Environment.GetEnvironmentVariable("OBJECT_STORAGE_FORCE_PATH_STYLE");
    if (bool.TryParse(envForcePathStyle, out var fps)) opts.ForcePathStyle = fps;
});

// ── DynamoDB Settings (AWS) ──
builder.Services.Configure<DynamoDbSettings>(opts =>
{
    builder.Configuration.GetSection("DynamoDb").Bind(opts);

    var envRegion = Environment.GetEnvironmentVariable("DYNAMODB_REGION");
    if (!string.IsNullOrEmpty(envRegion)) opts.Region = envRegion;

    var envAccess = Environment.GetEnvironmentVariable("DYNAMODB_ACCESS_KEY");
    if (!string.IsNullOrEmpty(envAccess)) opts.AccessKey = envAccess;

    var envSecret = Environment.GetEnvironmentVariable("DYNAMODB_SECRET_KEY");
    if (!string.IsNullOrEmpty(envSecret)) opts.SecretKey = envSecret;
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

// ── Bootstrap Admin (env-only seed credentials, see BootstrapAdminSettings) ──
builder.Services.Configure<BootstrapAdminSettings>(opts =>
{
    builder.Configuration.GetSection("BootstrapAdmin").Bind(opts);

    var username = Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_USERNAME");
    if (!string.IsNullOrEmpty(username)) opts.Username = username;

    var password = Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_PASSWORD");
    if (!string.IsNullOrEmpty(password)) opts.Password = password;
});


builder.Services
        .AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            // Serialise enums (notably Role) by name so the wire format matches the
            // [Authorize(Roles = "Admin")] string and clients can deal in human-
            // readable values like "User" / "Admin" instead of magic ints.
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

// ── Helper: load both settings with env overrides applied for early use ──
var objectStorage = builder.Configuration.GetSection("ObjectStorage").Get<ObjectStorageSettings>() ?? new ObjectStorageSettings();
ApplyEnv("OBJECT_STORAGE_BUCKET", v => objectStorage.BucketName = v);
ApplyEnv("OBJECT_STORAGE_REGION", v => objectStorage.Region = v);
ApplyEnv("OBJECT_STORAGE_ACCESS_KEY", v => objectStorage.AccessKey = v);
ApplyEnv("OBJECT_STORAGE_SECRET_KEY", v => objectStorage.SecretKey = v);
ApplyEnv("OBJECT_STORAGE_SERVICE_URL", v => objectStorage.ServiceUrl = v);
if (bool.TryParse(Environment.GetEnvironmentVariable("OBJECT_STORAGE_FORCE_PATH_STYLE"), out var earlyFps))
    objectStorage.ForcePathStyle = earlyFps;

var dynamoDb = builder.Configuration.GetSection("DynamoDb").Get<DynamoDbSettings>() ?? new DynamoDbSettings();
ApplyEnv("DYNAMODB_REGION", v => dynamoDb.Region = v);
ApplyEnv("DYNAMODB_ACCESS_KEY", v => dynamoDb.AccessKey = v);
ApplyEnv("DYNAMODB_SECRET_KEY", v => dynamoDb.SecretKey = v);

static void ApplyEnv(string name, Action<string> setter)
{
    var value = Environment.GetEnvironmentVariable(name);
    if (!string.IsNullOrEmpty(value)) setter(value);
}

// ── Core Infrastructure: DynamoDB (AWS) ──
var dynamoCredentials = (!string.IsNullOrEmpty(dynamoDb.AccessKey) && !string.IsNullOrEmpty(dynamoDb.SecretKey))
    ? new Amazon.Runtime.BasicAWSCredentials(dynamoDb.AccessKey, dynamoDb.SecretKey)
    : null;
var dynamoConfig = new AmazonDynamoDBConfig
{
    RegionEndpoint = RegionEndpoint.GetBySystemName(dynamoDb.Region)
};
builder.Services
        .AddSingleton<IAmazonDynamoDB>(dynamoCredentials != null
            ? new AmazonDynamoDBClient(dynamoCredentials, dynamoConfig)
            : new AmazonDynamoDBClient(dynamoConfig))
        .AddScoped<IDynamoDBContext, DynamoDBContext>();

// ── Identity ──
builder.Services
        .AddScoped<IUserRepository, UserRepository>()
        .AddScoped<IRefreshTokenRepository, RefreshTokenRepository>()
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

// ── Object Storage (S3-compatible: AWS S3 / Cloudflare R2 / MinIO / etc.) ──
var objectStorageCredentials = (!string.IsNullOrEmpty(objectStorage.AccessKey) && !string.IsNullOrEmpty(objectStorage.SecretKey))
    ? new Amazon.Runtime.BasicAWSCredentials(objectStorage.AccessKey, objectStorage.SecretKey)
    : null;
builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var s3Config = new AmazonS3Config();
    if (!string.IsNullOrEmpty(objectStorage.ServiceUrl))
    {
        s3Config.ServiceURL = objectStorage.ServiceUrl;
        s3Config.ForcePathStyle = objectStorage.ForcePathStyle;
    }
    else if (!string.IsNullOrEmpty(objectStorage.Region))
    {
        s3Config.RegionEndpoint = RegionEndpoint.GetBySystemName(objectStorage.Region);
    }
    return objectStorageCredentials != null
        ? new AmazonS3Client(objectStorageCredentials, s3Config)
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

// Swagger UI is served in every environment so operators can always exercise
// the API surface against a deployed instance.
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SyncServer V1");
    c.RoutePrefix = "swagger"; // serve at /swagger
});

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