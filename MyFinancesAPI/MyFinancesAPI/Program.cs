using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MyFinancesAPI.Data;
using MyFinancesAPI.Models;
using MyFinancesAPI.Models.Identity;
using MyFinancesAPI.Services;
using MyFinancesAPI.Services.Abstractions;
using MyFinancesAPI.Services.EmailServices;
using MyFinancesAPI.Services.EmailServices.Abstractions;
using MyFinancesAPI.Services.MailClient;
using System.Text;
using AutoMapper;
using MyFinancesAPI.Maps;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<IJwtProvider, JwtProvider>();

builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

builder.Services.AddIdentity<User, IdentityRole>(options =>
{
    options.Password.RequiredLength = 8;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireDigit = true;
    options.Password.RequireNonAlphanumeric = true;

    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = 401;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = 403;
        return Task.CompletedTask;
    };
});

builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
var jwtSecret = builder.Configuration["Jwt:Secret"];
var jwtKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwtSecret ?? string.Empty));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddGoogle(options =>
{
    options.ClientId = builder.Configuration["Google:ClientId"] ?? string.Empty;
    options.ClientSecret = builder.Configuration["Google:ClientSecret"] ?? string.Empty;
    options.CallbackPath = "/google-response";
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = jwtKey,
        ValidateLifetime = true,
        ValidateIssuer = false,
        ValidateAudience = false,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy =>
        {
            policy.WithOrigins("http://localhost:5173")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
        });
});

builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IEmailClient, EmailClient>();
builder.Services.AddAutoMapper(cfg => cfg.AddProfile<RegisterDtoToUser>());
builder.Services.AddAutoMapper(cfg => cfg.AddProfile<RegisterDtoToSendValidationEmailDto>());


var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();