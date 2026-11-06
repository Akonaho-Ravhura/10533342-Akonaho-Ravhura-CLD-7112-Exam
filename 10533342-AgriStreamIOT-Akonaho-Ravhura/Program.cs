using _10533342_AgriStreamIOT_Akonaho_Ravhura.Data;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Services;
using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Azure SQL Database (LocalDB in Development). EnableRetryOnFailure handles the
// transient faults Azure SQL can return during failovers / scaling.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

builder.Services.AddScoped<FarmAccessService>();

// Drone images: Azure Blob Storage when ConnectionStrings:BlobStorage is set, otherwise a local folder.
var blobConnection = builder.Configuration.GetConnectionString("BlobStorage");
if (!string.IsNullOrWhiteSpace(blobConnection))
{
    var container = builder.Configuration["BlobStorage:ContainerName"] ?? "drone-images";
    builder.Services.AddSingleton<IImageStorage>(new BlobImageStorage(blobConnection, container));
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
    .AddEntityFrameworkStores<ApplicationDbContext>()
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

// Apply migrations, create the Admin / Farmer / Agronomist roles and seed users (+ sample data).
await DbSeeder.SeedAsync(app.Services);

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

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
