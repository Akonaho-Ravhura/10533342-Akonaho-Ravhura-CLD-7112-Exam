using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Services;
using Microsoft.AspNetCore.Identity;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Data
{
    /// <summary>
    /// Seeds demo data on startup: 5 farmers, each with 1 farm that has 5 fields, 5 IoT devices
    /// (2 soil sensors, 2 humidity sensors, 1 drone) and 15 drone images (3 per field) uploaded to Blob Storage.
    /// Runs after the tables exist (see <see cref="DatabaseInitializer"/>). It is safe to run on every start:
    /// a farmer who already has a farm is skipped, so nothing is duplicated.
    /// Turn it off with "DemoData:Enabled": false; farmers sign in with "DemoData:FarmerPassword".
    /// </summary>
    public class DemoDataSeeder
    {
        private const int ImagesPerField = 3;

        private sealed record DemoFarmer(string FullName, string Email, string FarmName, string Location, decimal Hectares, string[] Crops);

        private static readonly DemoFarmer[] Farmers =
        {
            new("Sipho Ndlovu", "sipho.ndlovu@agristream.demo", "Green Valley Farm", "Tzaneen, Limpopo", 210,
                new[] { "Maize", "Avocados", "Tomatoes", "Bananas", "Macadamias" }),
            new("Lerato Mokoena", "lerato.mokoena@agristream.demo", "Highveld Grain Co.", "Bethal, Mpumalanga", 540,
                new[] { "Maize", "Soybeans", "Sunflower", "Sorghum", "Dry beans" }),
            new("Johan van der Merwe", "johan.vandermerwe@agristream.demo", "Karoo Ridge Orchards", "Ceres, Western Cape", 160,
                new[] { "Apples", "Pears", "Cherries", "Plums", "Onions" }),
            new("Nomvula Dlamini", "nomvula.dlamini@agristream.demo", "Umgeni Cane Estate", "Howick, KwaZulu-Natal", 320,
                new[] { "Sugarcane", "Potatoes", "Cabbage", "Maize", "Pecans" }),
            new("Pieter Botha", "pieter.botha@agristream.demo", "Vaal River Farms", "Parys, Free State", 410,
                new[] { "Wheat", "Maize", "Lucerne", "Groundnuts", "Sunflower" }),
        };

        private static readonly string[] FieldNames = { "North Block", "South Block", "East Block", "West Block", "River Block" };

        private readonly AgriDataStore _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IImageStorage _images;
        private readonly IConfiguration _config;
        private readonly ILogger<DemoDataSeeder> _logger;

        public DemoDataSeeder(AgriDataStore db, UserManager<ApplicationUser> userManager, IImageStorage images,
            IConfiguration config, ILogger<DemoDataSeeder> logger)
        {
            _db = db;
            _userManager = userManager;
            _images = images;
            _config = config;
            _logger = logger;
        }

        public async Task SeedAsync(CancellationToken ct = default)
        {
            if (!_config.GetValue("DemoData:Enabled", true))
            {
                _logger.LogInformation("Demo data seeding is turned off (DemoData:Enabled = false).");
                return;
            }

            var password = _config["DemoData:FarmerPassword"];
            if (string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning("DemoData:FarmerPassword is not set; demo farmers not seeded.");
                return;
            }

            var farms = await _db.ListAsync<Farm>(ct: ct);
            var seeded = 0;
            for (var i = 0; i < Farmers.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var farmer = await EnsureFarmerAsync(Farmers[i], password);
                if (farmer == null || farms.Any(f => f.FarmerId == farmer.Id)) continue;

                await SeedFarmAsync(Farmers[i], farmer, i, ct);
                seeded++;
            }

            if (seeded > 0)
                _logger.LogInformation("Demo data: seeded {Count} farm(s) with fields, devices and drone images in {Storage}.",
                    seeded, _images.Description);
        }

        private async Task<ApplicationUser?> EnsureFarmerAsync(DemoFarmer demo, string password)
        {
            var user = await _userManager.FindByEmailAsync(demo.Email);
            if (user != null) return user;

            user = new ApplicationUser { UserName = demo.Email, Email = demo.Email, EmailConfirmed = true, FullName = demo.FullName };
            var result = await _userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                _logger.LogError("Could not create demo farmer {Email}: {Errors}", demo.Email,
                    string.Join("; ", result.Errors.Select(e => e.Description)));
                return null;
            }
            await _userManager.AddToRoleAsync(user, AppRoles.Farmer);
            return user;
        }

        private async Task SeedFarmAsync(DemoFarmer demo, ApplicationUser farmer, int index, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var rng = new Random(1000 + index);

            var farm = new Farm
            {
                Name = demo.FarmName, Location = demo.Location, SizeHectares = demo.Hectares,
                FarmerId = farmer.Id, CreatedAt = now
            };
            await _db.AddAsync(farm, ct);

            // 5 fields
            var fields = FieldNames.Select((name, f) => new Field
            {
                FarmId = farm.Id,
                Name = name,
                CropType = demo.Crops[f],
                AreaHectares = Math.Round(demo.Hectares / 6m + rng.Next(-10, 10), 1),
                PlantedOn = now.AddDays(-rng.Next(30, 120)).Date
            }).ToList();
            await _db.AddRangeAsync(fields, ct);

            // 5 devices: soil sensors on fields 1-2, humidity sensors on fields 3-4, a drone based on field 5
            // (drones fly over the whole farm).
            var code = $"F{index + 1}";
            var devices = new List<IoTDevice>
            {
                new() { FieldId = fields[0].Id, Name = $"Soil probe {code}-1", DeviceType = DeviceType.SoilSensor, SerialNumber = $"DEMO-{code}-SOIL-1" },
                new() { FieldId = fields[1].Id, Name = $"Soil probe {code}-2", DeviceType = DeviceType.SoilSensor, SerialNumber = $"DEMO-{code}-SOIL-2" },
                new() { FieldId = fields[2].Id, Name = $"Humidity {code}-1", DeviceType = DeviceType.HumiditySensor, SerialNumber = $"DEMO-{code}-HUM-1" },
                new() { FieldId = fields[3].Id, Name = $"Humidity {code}-2", DeviceType = DeviceType.HumiditySensor, SerialNumber = $"DEMO-{code}-HUM-2" },
                new() { FieldId = fields[4].Id, Name = $"Survey drone {code}", DeviceType = DeviceType.Drone, SerialNumber = $"DEMO-{code}-DRN-1" },
            };
            foreach (var d in devices) d.InstalledOn = now.AddDays(-rng.Next(60, 150)).Date;
            await _db.AddRangeAsync(devices, ct);
            var drone = devices[4];

            // 15 drone images: 3 per field, a week apart, uploaded to Blob Storage.
            var uploads = new List<Task<DroneImage>>();
            foreach (var (field, f) in fields.Select((x, n) => (x, n)))
            {
                var baseNdvi = 0.45 + rng.NextDouble() * 0.35;
                for (var shot = 0; shot < ImagesPerField; shot++)
                {
                    var daysAgo = 1 + (ImagesPerField - 1 - shot) * 7 + rng.Next(0, 3);
                    var ndvi = Math.Round(baseNdvi + shot * 0.02 + (rng.NextDouble() - 0.5) * 0.06, 2);
                    var hasStress = rng.NextDouble() < 0.4;
                    var patches = hasStress
                        ? new[] { new SampleDroneImages.Patch(rng.NextDouble() * 0.8 + 0.1, rng.NextDouble() * 0.8 + 0.1, 0.08 + rng.NextDouble() * 0.1, 0.08 + rng.NextDouble() * 0.1, 0.15 + rng.NextDouble() * 0.25) }
                        : Array.Empty<SampleDroneImages.Patch>();
                    var seed = (index + 1) * 1000 + f * 10 + shot;
                    var title = $"{field.Name} · {field.CropType} · {now.AddDays(-daysAgo):d MMM}";
                    var notes = hasStress
                        ? "Stress patch visible; check soil moisture and nutrients in this area."
                        : "Even canopy across the block.";
                    uploads.Add(UploadAsync(field, drone, title, ndvi, seed, patches, now.AddDays(-daysAgo), notes, ct));
                }
            }
            await _db.AddRangeAsync(await Task.WhenAll(uploads), ct);
        }

        private async Task<DroneImage> UploadAsync(Field field, IoTDevice drone, string title, double ndvi, int seed,
            SampleDroneImages.Patch[] patches, DateTime capturedAt, string notes, CancellationToken ct)
        {
            using var stream = new MemoryStream(SampleDroneImages.Generate(title, ndvi, seed, patches));
            var blobName = await _images.UploadAsync(stream, "image/svg+xml", $"{field.Name}.svg", ct);
            return new DroneImage
            {
                FieldId = field.Id, DeviceId = drone.Id, CapturedAt = capturedAt, Ndvi = (decimal)ndvi,
                BlobName = blobName, ContentType = "image/svg+xml", Notes = notes
            };
        }
    }
}
