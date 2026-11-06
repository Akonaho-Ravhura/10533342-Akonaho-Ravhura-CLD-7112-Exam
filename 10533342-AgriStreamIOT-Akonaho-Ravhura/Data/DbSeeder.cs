using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Data
{
    /// <summary>
    /// Applies migrations, creates the Admin / Farmer / Agronomist roles, seeds one user per role
    /// (from the "SeedUsers" config section) and, if enabled, sample farm and IoT data.
    /// </summary>
    public static class DbSeeder
    {
        public static async Task SeedAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var config = sp.GetRequiredService<IConfiguration>();
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

            await db.Database.MigrateAsync();

            foreach (var role in AppRoles.All)
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));
            }

            var admin = await EnsureUserAsync(userManager, config, logger, AppRoles.Admin);
            var farmer = await EnsureUserAsync(userManager, config, logger, AppRoles.Farmer);
            var agronomist = await EnsureUserAsync(userManager, config, logger, AppRoles.Agronomist);

            if (config.GetValue<bool>("SeedUsers:SampleData") && farmer != null && !await db.Farms.AnyAsync())
            {
                var storage = sp.GetRequiredService<IImageStorage>();
                await SeedSampleDataAsync(db, storage, farmer, agronomist);
                logger.LogInformation("Sample farm, fields, devices, readings and recommendations seeded; drone images uploaded to {Storage}.",
                    storage.Description);
            }
        }

        /// <summary>Creates the user configured under SeedUsers:{role} if it does not exist yet.</summary>
        private static async Task<ApplicationUser?> EnsureUserAsync(
            UserManager<ApplicationUser> userManager, IConfiguration config, ILogger logger, string role)
        {
            var section = config.GetSection($"SeedUsers:{role}");
            var email = section["Email"];
            var password = section["Password"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning("SeedUsers:{Role} not configured; no {Role} user seeded.", role, role);
                return null;
            }

            var user = await userManager.FindByEmailAsync(email);
            if (user != null) return user;

            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = section["FullName"] ?? AppRoles.DisplayName(role)
            };
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                logger.LogError("Could not seed {Role} {Email}: {Errors}", role, email,
                    string.Join("; ", result.Errors.Select(e => e.Description)));
                return null;
            }

            await userManager.AddToRoleAsync(user, role);
            logger.LogInformation("Seeded {Role} user {Email}.", role, email);
            return user;
        }

        private static async Task SeedSampleDataAsync(ApplicationDbContext db, IImageStorage storage, ApplicationUser farmer, ApplicationUser? agronomist)
        {
            var now = DateTime.UtcNow;

            var farm = new Farm
            {
                Name = "Sunrise Valley Farm",
                Location = "Polokwane, Limpopo",
                SizeHectares = 120,
                FarmerId = farmer.Id,
                AgronomistId = agronomist?.Id
            };

            var maize = new Field { Name = "North Block", CropType = "Maize", AreaHectares = 45, PlantedOn = now.AddDays(-60).Date };
            var soy = new Field { Name = "River Block", CropType = "Soybeans", AreaHectares = 30, PlantedOn = now.AddDays(-45).Date };
            farm.Fields.Add(maize);
            farm.Fields.Add(soy);

            var devices = new[]
            {
                (maize, new IoTDevice { Name = "Soil probe N1", DeviceType = DeviceType.SoilSensor, SerialNumber = "SOIL-N1-0001", InstalledOn = now.AddDays(-70).Date }),
                (maize, new IoTDevice { Name = "Humidity N1", DeviceType = DeviceType.HumiditySensor, SerialNumber = "HUM-N1-0001", InstalledOn = now.AddDays(-70).Date }),
                (soy, new IoTDevice { Name = "Soil probe R1", DeviceType = DeviceType.SoilSensor, SerialNumber = "SOIL-R1-0001", InstalledOn = now.AddDays(-50).Date }),
                (soy, new IoTDevice { Name = "Humidity R1", DeviceType = DeviceType.HumiditySensor, SerialNumber = "HUM-R1-0001", InstalledOn = now.AddDays(-50).Date }),
            };
            foreach (var (field, device) in devices) field.Devices.Add(device);

            var drone = new IoTDevice { Name = "Survey drone D1", DeviceType = DeviceType.Drone, SerialNumber = "DRN-0001", InstalledOn = now.AddDays(-70).Date };
            maize.Devices.Add(drone);

            db.Farms.Add(farm);
            await db.SaveChangesAsync();

            // A week of readings, twice a day, from each soil and humidity sensor.
            var rng = new Random(42);
            decimal R(double min, double max) => Math.Round((decimal)(min + rng.NextDouble() * (max - min)), 2);
            foreach (var (field, device) in devices)
            {
                for (var i = 13; i >= 0; i--)
                {
                    var reading = new SensorReading { DeviceId = device.Id, FieldId = field.Id, RecordedAt = now.AddHours(-12 * i) };
                    if (device.DeviceType == DeviceType.SoilSensor)
                    {
                        reading.SoilDensity = R(1.2, 1.5);
                        reading.SoilMoisture = R(18, 35);
                        reading.SoilPh = R(5.6, 6.8);
                        reading.Nitrogen = field == maize ? R(12, 19) : R(25, 40); // maize block is low on N
                        reading.Phosphorus = R(15, 30);
                        reading.Potassium = R(120, 200);
                    }
                    else
                    {
                        reading.Humidity = R(45, 85);
                        reading.Temperature = R(16, 31);
                    }
                    db.SensorReadings.Add(reading);
                }
            }

            // Test drone images: generated, uploaded to blob storage, metadata saved in SQL.
            var droneImages = new[]
            {
                (maize, 10, 0.58m, "Early canopy, mostly even. Slight thinning in the north-east.",
                    SampleDroneImages.Generate("North Block · Maize · week 6", 0.62, 11,
                        new SampleDroneImages.Patch(0.78, 0.3, 0.12, 0.18, 0.18))),
                (maize, 3, 0.52m, "Yellowing visible in the north-east corner (likely nitrogen deficiency).",
                    SampleDroneImages.Generate("North Block · Maize · week 7", 0.6, 12,
                        new SampleDroneImages.Patch(0.78, 0.3, 0.2, 0.3, 0.38),
                        new SampleDroneImages.Patch(0.3, 0.75, 0.08, 0.1, 0.2))),
                (soy, 9, 0.66m, "Good emergence across the block.",
                    SampleDroneImages.Generate("River Block · Soybeans · week 5", 0.68, 21)),
                (soy, 2, 0.71m, "Even canopy across the block. Small wet patch near the river.",
                    SampleDroneImages.Generate("River Block · Soybeans · week 6", 0.74, 22,
                        new SampleDroneImages.Patch(0.15, 0.85, 0.07, 0.08, 0.25))),
            };
            foreach (var (field, daysAgo, ndvi, notes, svg) in droneImages)
            {
                using var stream = new MemoryStream(svg);
                var blobName = await storage.UploadAsync(stream, "image/svg+xml", $"{field.Name}.svg");
                db.DroneImages.Add(new DroneImage
                {
                    FieldId = field.Id, DeviceId = drone.Id, CapturedAt = now.AddDays(-daysAgo), Ndvi = ndvi,
                    BlobName = blobName, ContentType = "image/svg+xml", Notes = notes
                });
            }

            if (agronomist != null)
            {
                db.Recommendations.Add(new Recommendation
                {
                    FieldId = maize.Id,
                    AgronomistId = agronomist.Id,
                    CreatedAt = now.AddDays(-1),
                    Title = "Top-dress nitrogen on North Block",
                    Details = "Soil nitrogen has averaged below 20 mg/kg this week and the drone NDVI (0.52) shows yellowing " +
                              "in the north-east corner. Apply 40 kg N/ha (LAN) within the next 5 days, ideally before forecast rain.",
                    CropHealth = CropHealthStatus.AtRisk,
                    Priority = RecommendationPriority.High,
                    ExpectedYieldTonsPerHectare = 6.5m
                });
            }

            await db.SaveChangesAsync();
        }
    }
}
