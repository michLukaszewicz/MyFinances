using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyFinances.Api;
using MyFinances.Api.Auth;

var builder = WebApplication.CreateBuilder(args);

// Render (and similar PaaS platforms) inject the port to bind via $PORT at runtime;
// ASP.NET Core has no built-in convention for it, so we wire it up explicitly.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<MyFinances.Api.AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

const string FrontendDevCorsPolicy = "FrontendDev";
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendDevCorsPolicy, policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddIdentityCore<AppUser>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 12;
}).AddSignInManager().AddEntityFrameworkStores<AppDbContext>();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddCookie(IdentityConstants.ApplicationScheme, options =>
{
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Events.OnRedirectToLogin = ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization();

builder.Services.AddAntiforgery(options => { options.HeaderName = "X-XSRF-TOKEN"; });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors(FrontendDevCorsPolicy);
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api").RequireAuthorization();

api.MapAuthEndpoints();

// The React SPA is built (see MyFinances/frontend, `npm run build`) and its static
// output copied into wwwroot at publish time (see the csproj's Publish target below).
// Serving it from this same app means the browser only ever talks to one origin —
// no CORS needed in production. In dev, the frontend runs its own Vite server instead
// and proxies /api/* here (see MyFinances/frontend/vite.config.ts); the CORS policy
// above covers that case.
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

// Required so WebApplicationFactory<Program> (used by the integration test project) can
// resolve this top-level-statements entry point as a type.
public partial class Program { }
