using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;
using HospitalManagementSystem.Services.Providers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.AccessDeniedPath = "/Auth/AccessDenied";
    });

// Authorization is opt-in by default in ASP.NET Core: a controller with no [Authorize]
// attribute is anonymous. That silently left several controllers unprotected (Staff,
// Medicines, most of MedicalRecords). A fallback policy flips the default to opt-out
// instead - every endpoint requires an authenticated user unless explicitly marked
// [AllowAnonymous].
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddSignalR();
builder.Services.AddMemoryCache();

// ডাটাবেস কানেকশন সেটআপ (অবশ্যই builder.Build() এর আগে থাকতে হবে)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// AI spine: a scrubber, an admin-managed provider chain (see AiProviderSetting /
// AiProviderResolver), and one service every AI feature calls through. Provider API
// keys are no longer read from config at request time - they live encrypted in the
// database and are resolved fresh on every call, so an admin updating a key from
// /AiProviderSettings takes effect immediately, no restart required. GeminiOptions/
// GroqOptions are only read once below, to seed the database from whatever was in
// user-secrets before this feature existed.
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("Gemini"));
builder.Services.Configure<GroqOptions>(builder.Configuration.GetSection("Groq"));
builder.Services.AddDataProtection();
builder.Services.AddSingleton<ApiKeyProtector>();
builder.Services.AddScoped<PhiScrubber>();
builder.Services.AddScoped<AiProviderResolver>();
builder.Services.AddScoped<IClinicalAiService, ClinicalAiService>();
builder.Services.AddScoped<DemoDataSeeder>();

builder.Services.AddHttpClient<GroqClient>();
builder.Services.AddScoped<IAiTextProvider, GeminiTextProvider>();
builder.Services.AddScoped<IAiTextProvider, GroqTextProvider>();
builder.Services.AddScoped<IAiTextProvider, AnthropicTextProvider>();

var app = builder.Build();

// One-time seed: if no provider has ever been configured through the admin UI, copy
// whatever Gemini/Groq keys already exist in config (user-secrets) into the database
// so upgrading to this feature doesn't silently break AI generation for keys the user
// already provided. Runs once per empty table, not on every startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (!db.AiProviderSettings.Any())
    {
        var protector = scope.ServiceProvider.GetRequiredService<ApiKeyProtector>();
        var geminiOptions = scope.ServiceProvider.GetRequiredService<IOptions<GeminiOptions>>().Value;
        var groqOptions = scope.ServiceProvider.GetRequiredService<IOptions<GroqOptions>>().Value;
        var now = DateTime.UtcNow;
        var priority = 0;

        if (!string.IsNullOrWhiteSpace(geminiOptions.ApiKey))
        {
            db.AiProviderSettings.Add(new AiProviderSetting
            {
                Provider = AiProviderType.Gemini,
                EncryptedApiKey = protector.Protect(geminiOptions.ApiKey),
                ModelId = geminiOptions.ReasoningModel,
                IsEnabled = true,
                Priority = priority++,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        if (!string.IsNullOrWhiteSpace(groqOptions.ApiKey))
        {
            db.AiProviderSettings.Add(new AiProviderSetting
            {
                Provider = AiProviderType.Groq,
                EncryptedApiKey = protector.Protect(groqOptions.ApiKey),
                ModelId = groqOptions.Model,
                IsEnabled = true,
                Priority = priority++,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        db.SaveChanges();
    }
}

// Demo data reset, run only via `dotnet run -- --seed-demo` - a CLI flag, not an HTTP
// endpoint, so it can never be triggered by a stray click during a live demo. Wipes and
// rebuilds patients/beds/admissions/appointments/prescriptions/bills/medical records into
// a known-good presentable state, then exits without starting the web server.
if (args.Contains("--seed-demo"))
{
    using var seedScope = app.Services.CreateScope();
    var seeder = seedScope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
    await seeder.SeedAsync();
    return;
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStaticFiles();

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<HospitalManagementSystem.Hubs.NotificationHub>("/notificationHub").RequireAuthorization();
app.MapHub<HospitalManagementSystem.Hubs.AiStreamHub>("/aiStreamHub").RequireAuthorization();

app.Run();