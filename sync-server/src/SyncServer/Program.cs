using System.Text.Json;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using SyncServer.Repositories;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using SyncServer.Middleware;
using SyncServer.Services;
using Microsoft.AspNetCore.Authorization;
using SyncServer.Abstraction;


var builder = WebApplication.CreateBuilder(args);

//Logger
builder.Logging
        .ClearProviders()
        .AddJsonConsole();

// Add services to the container.
builder.Services.Configure<AmazonWebServicesConstants>(opts =>
{
    // Bind from configuration section first
    builder.Configuration.GetSection("AmazonWebServiceConstants").Bind(opts);

    var envBucket = Environment.GetEnvironmentVariable("AWS_BUCKET");
    if (!string.IsNullOrEmpty(envBucket)) opts.BucketName = envBucket;

    var envSongTable = Environment.GetEnvironmentVariable("AWS_SONG_TABLE_NAME");
    if (!string.IsNullOrEmpty(envSongTable)) opts.SongTableName = envSongTable;

    var envUserTable = Environment.GetEnvironmentVariable("AWS_USER_TABLE_NAME");
    if (!string.IsNullOrEmpty(envUserTable)) opts.UserTableName = envUserTable;

    var envRegion = Environment.GetEnvironmentVariable("AWS_REGION");
    if (!string.IsNullOrEmpty(envRegion)) opts.AwsRegion = envRegion;

    var envAccess = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
    if (!string.IsNullOrEmpty(envAccess)) opts.AccessKey = envAccess;

    var envSecret = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
    if (!string.IsNullOrEmpty(envSecret)) opts.SecretKey = envSecret;
});


builder.Services.Configure<JwtConstants>(opts =>
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

var awsConstants = builder.Configuration.GetSection("AmazonWebServiceConstants").Get<AmazonWebServicesConstants>() ?? new AmazonWebServicesConstants();
var envRegion = Environment.GetEnvironmentVariable("AWS_REGION");
if (!string.IsNullOrEmpty(envRegion)) awsConstants.AwsRegion = envRegion;

builder.Services
        .AddSingleton<IAmazonDynamoDB>(new AmazonDynamoDBClient(RegionEndpoint.GetBySystemName(awsConstants.AwsRegion)))
        .AddScoped<IDynamoDBContext, DynamoDBContext>()
        .AddScoped<IMusicRepository, MusicRepository>()
        .AddScoped<IUserRepository, UserRepository>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Keep AWS SDK configuration and services for S3/DynamoDB
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<Amazon.S3.IAmazonS3>();

// JWT Authentication
var jwtConstants = builder.Configuration.GetSection("JwtConstants").Get<JwtConstants>() ?? new JwtConstants(); 
var envKey = Environment.GetEnvironmentVariable("JWT_KEY");
if (!string.IsNullOrEmpty(envKey)) jwtConstants.Key = envKey;
var envIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER");
if (!string.IsNullOrEmpty(envIssuer)) jwtConstants.Issuer = envIssuer;
var envAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE");
if (!string.IsNullOrEmpty(envAudience)) jwtConstants.Audience = envAudience;

var keyBytes = Encoding.UTF8.GetBytes(jwtConstants.Key);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
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
// Enable Swagger middleware in development or when running inside a container (Docker)
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

app.MapGet("/", () => "Welcome to running ASP.NET Core on Kestrel");

app.Run();