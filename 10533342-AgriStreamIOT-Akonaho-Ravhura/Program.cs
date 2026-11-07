using _10533342_AgriStreamIOT_Akonaho_Ravhura.Data;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Services;
using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Azure.Data.Tables;

var builder = WebApplication.CreateBuilder(args);

// All data (users, roles, farms, fields, devices, readings, drone image details, recommendations)
// lives in Azure Table Storage; drone image files go to Blob Storage in the same storage account.
// Set ConnectionStrings:StorageAccount in appsettings.json. The client is created on first use, so a
// missing or placeholder value never stops the site from starting (DatabaseInitializer reports it).
var storageConnection = StorageAccountSettings.GetConnectionString(builder.Configuration);
builder.Services.AddSingleton(_ => new TableServiceClient(storageConnection));
builder.Services.AddSingleton<AgriDataStore>();

// Table creation + seeding run in the background after startup (see DatabaseInitializer), so an
// unreachable storage account can't push startup past Azure's limit (HTTP 500.37).
builder.Services.AddSingleton<DatabaseStatus>();
builder.Services.AddHostedService<DatabaseInitializer>();

builder.Services.AddScoped<FarmAccessService>();

// Drone images: Azure Blob Storage when the storage account is configured, otherwise a local folder.
if (StorageAccountSettings.IsConfigured(storageConnection))
{
    var container = builder.Configuration["BlobStorage:ContainerName"] ?? "drone-images";
    builder.Services.AddSingleton<IImageStorage>(_ => new BlobImageStorage(storageConnection!, container));
}
else
{
    var root = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "drone-images");
    builder.Services.AddSingleton<IImageStorage>(new LocalImageStorage(root));
}

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = false;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddUserStore<TableUserStore>()
    .AddRoleStore<TableRoleStore>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

builder.Services.AddControllersWithViews();

// Sensor values are decimals and HTML number inputs always submit "1.35". Pin the culture
// (South Africa, but with a "." decimal separator) so parsing doesn't depend on the server's locale.
var appCulture = (CultureInfo)CultureInfo.GetCultureInfo("en-ZA").Clone();
appCulture.NumberFormat.NumberDecimalSeparator = ".";
appCulture.NumberFormat.NumberGroupSeparator = ",";
CultureInfo.DefaultThreadCurrentCulture = appCulture;
CultureInfo.DefaultThreadCurrentUICulture = appCulture;
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture(appCulture);
    options.SupportedCultures = new[] { appCulture };
    options.SupportedUICultures = new[] { appCulture };
    options.RequestCultureProviders.Clear();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseRequestLocalization();
app.UseHttpsRedirection();
app.UseStaticFiles();

// While the database isn't ready, answer with a friendly 503 instead of letting the request reach
// cookie authentication / controllers, which would throw a SqlException on every page.
app.Use(async (context, next) =>
{
    var status = context.RequestServices.GetRequiredService<DatabaseStatus>();
    if (status.State == DatabaseState.Ready || context.Request.Path.StartsWithSegments("/health"))
    {
        await next();
        return;
    }

    var startingUp = status.State == DatabaseState.Pending;
    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
    context.Response.Headers.RetryAfter = startingUp ? "30" : "120";
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.WriteAsync($$"""
        <!DOCTYPE html>
        <html lang="en"><head><meta charset="utf-8" /><meta name="viewport" content="width=device-width, initial-scale=1" />
        <title>AgriStream IoT · Unavailable</title>
        <style>body{font-family:system-ui,sans-serif;background:#0a0a0d;color:#e8e8ea;display:grid;place-items:center;min-height:100vh;margin:0;padding:16px}
        main{max-width:440px;text-align:center}h1{font-size:1.4rem}p{color:#a0a0a8}</style></head>
        <body><main><h1>{{(startingUp ? "AgriStream IoT is starting up" : "AgriStream IoT is temporarily unavailable")}}</h1>
        <p>{{(startingUp ? "We're connecting to the database. Please refresh in a minute." : "We can't reach the database right now. Please try again later.")}}</p></main></body></html>
        """);
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Anonymous health check: reports Pending / Ready / Failed for the database initialisation.
app.MapGet("/health", (DatabaseStatus status) => Results.Json(status.ToHealthPayload()))
    .AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
