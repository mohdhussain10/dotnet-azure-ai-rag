using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RAG.API.Controllers;
using RAG.API.Models;
using RAG.API.Services;
using RAG.Infrastructure.Data;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAppConfigurationOptions(builder.Configuration);
builder.Services.AddScoped<AzureAIService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<ITextExtractorService, TextExtractorService>();
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<IchatService, ChatService>();
builder.Services.AddScoped<IGeminiChatService, GeminiChatService>();





builder.Services.AddSingleton<IChunkingService, ChunkingService>();
builder.Services.AddHttpClient<IEmbeddingService, GeminiEmbeddingService>();
builder.Services.AddScoped<IAzureSearchService, AzureSearchService>();



// Register Supabase PostgreSQL DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("SupabasePostgres")));

builder.Services.AddSingleton(sp =>
{
    var connectionString = builder.Configuration["AzureStorage:BlobStorageConnectionString"];
    var containerName = builder.Configuration["AzureStorage:BlobContainer"];
    return new BlobContainerClient(connectionString, containerName);
});

// Configure JWT Authentication for Supabase (Automatic JWKS validation for ES256/RS256)
var supabaseUrl = builder.Configuration["Supabase:Url"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"{supabaseUrl.TrimEnd('/')}/auth/v1";
        options.MetadataAddress = $"{supabaseUrl.TrimEnd('/')}/auth/v1/.well-known/openid-configuration";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"{supabaseUrl.TrimEnd('/')}/auth/v1",
            ValidateAudience = true,
            ValidAudience = "authenticated",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();


// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin", policy =>
    {
        policy.WithOrigins("http://localhost:4200") // Allow only the 4200 port
              .AllowAnyHeader()
              .AllowAnyMethod();
    });

    options.AddPolicy("AllowAllOrigins", policy =>
    {
        policy.AllowAnyOrigin() // Allow all origins
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowSpecificOrigin");

app.UseAuthentication(); 
app.UseAuthorization();

app.MapControllers();

app.Run();

