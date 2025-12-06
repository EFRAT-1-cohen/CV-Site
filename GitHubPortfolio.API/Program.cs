using GitHubPortfolio.Service;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();

// הגדרת GitHub Options מ-User Secrets / appsettings
builder.Services.Configure<GitHubOptions>(
    builder.Configuration.GetSection(GitHubOptions.SectionName));

// הוספת Memory Cache
builder.Services.AddMemoryCache();

// רישום GitHubService עם Caching באמצעות Scrutor (Decorator Pattern)
builder.Services.AddScoped<IGitHubService, GitHubService>();
builder.Services.Decorate<IGitHubService, GitHubCachingService>();

// הסבר: 
// 1. נוצר GitHubService רגיל
// 2. GitHubCachingService "עוטף" אותו (decorator)
// 3. כל קריאה עוברת דרך GitHubCachingService שמחליט אם להשתמש ב-cache

// Add Swagger/OpenAPI support
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add CORS policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

app.Run();