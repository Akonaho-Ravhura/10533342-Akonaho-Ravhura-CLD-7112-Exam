using _10533342_AgriStreamIOT_Akonaho_Ravhura.Data;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models.ViewModels;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Controllers
{
    /// <summary>
    /// Farmers manage their farms and fields and collect data from their IoT devices:
    /// soil sensors (density, moisture, pH, N/P/K), humidity sensors and drones (field images).
    /// </summary>
    [Authorize(Roles = AppRoles.Farmer)]
    public class FarmerController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly FarmAccessService _access;

        public FarmerController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, FarmAccessService access)
        {
            _db = db;
            _userManager = userManager;
            _access = access;
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.Farms = await _access.VisibleFarms(User)
                .Include(f => f.Agronomist).OrderBy(f => f.Name).ToListAsync();
            return View(await _access.GetFieldSummariesAsync(User));
        }

        // ---------- Farms & fields ----------

        [HttpGet]
        public IActionResult CreateFarm() => View(new Farm());

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateFarm([Bind("Name,Location,SizeHectares")] Farm farm)
        {
            farm.FarmerId = _userManager.GetUserId(User)!;
            ModelState.Remove(nameof(Farm.FarmerId));
            if (!ModelState.IsValid) return View(farm);

            _db.Farms.Add(farm);
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Farm '{farm.Name}' created. Add a field to start recording data.";
            return RedirectToAction(nameof(CreateField), new { farmId = farm.Id });
        }

        [HttpGet]
        public async Task<IActionResult> CreateField(int farmId)
        {
            var farm = await _access.GetOwnedFarmAsync(User, farmId);
            if (farm == null) return NotFound();
            return View(new CreateFieldViewModel { FarmId = farm.Id, FarmName = farm.Name });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateField(CreateFieldViewModel model)
        {
            var farm = await _access.GetOwnedFarmAsync(User, model.FarmId);
            if (farm == null) return NotFound();
            if (!ModelState.IsValid) { model.FarmName = farm.Name; return View(model); }

            var field = new Field
            {
                FarmId = farm.Id, Name = model.Name, CropType = model.CropType,
                AreaHectares = model.AreaHectares, PlantedOn = model.PlantedOn
            };
            _db.Fields.Add(field);
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Field '{field.Name}' added.";
            return RedirectToAction("Details", "Fields", new { id = field.Id });
        }

        // ---------- IoT devices ----------

        [HttpGet]
        public async Task<IActionResult> AddDevice(int fieldId)
        {
            var field = await _access.GetOwnedFieldAsync(User, fieldId);
            if (field == null) return NotFound();
            ViewBag.Field = field;
            return View(new IoTDevice { FieldId = field.Id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddDevice([Bind("FieldId,Name,DeviceType,SerialNumber,InstalledOn")] IoTDevice device)
        {
            var field = await _access.GetOwnedFieldAsync(User, device.FieldId);
            if (field == null) return NotFound();
            if (await _db.Devices.AnyAsync(d => d.SerialNumber == device.SerialNumber))
                ModelState.AddModelError(nameof(device.SerialNumber), "A device with this serial number is already registered.");
            if (!ModelState.IsValid) { ViewBag.Field = field; return View(device); }

            _db.Devices.Add(device);
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Device '{device.Name}' registered.";
            return RedirectToAction("Details", "Fields", new { id = field.Id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleDevice(int id)
        {
            var device = await _db.Devices.Include(d => d.Field).FirstOrDefaultAsync(d => d.Id == id);
            if (device == null || await _access.GetOwnedFieldAsync(User, device.FieldId) == null) return NotFound();
            device.IsActive = !device.IsActive;
            await _db.SaveChangesAsync();
            return RedirectToAction("Details", "Fields", new { id = device.FieldId });
        }

        // ---------- Sensor readings (soil + humidity) ----------

        [HttpGet]
        public async Task<IActionResult> RecordReading(int fieldId)
        {
            var field = await _access.GetOwnedFieldAsync(User, fieldId);
            if (field == null) return NotFound();
            await PopulateDevicesAsync(field, DeviceType.SoilSensor, DeviceType.HumiditySensor);
            return View(new SensorReading { FieldId = field.Id, RecordedAt = DateTime.Now });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordReading(
            [Bind("FieldId,DeviceId,RecordedAt,SoilDensity,SoilMoisture,SoilPh,Nitrogen,Phosphorus,Potassium,Humidity,Temperature")] SensorReading reading)
        {
            var field = await _access.GetOwnedFieldAsync(User, reading.FieldId);
            if (field == null) return NotFound();

            var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == reading.DeviceId && d.FieldId == field.Id);
            if (device == null || device.DeviceType == DeviceType.Drone)
                ModelState.AddModelError(nameof(reading.DeviceId), "Choose a soil or humidity sensor on this field.");
            else if (device.DeviceType == DeviceType.SoilSensor)
            {
                reading.Humidity = reading.Temperature = null;
                if (reading.SoilMoisture == null) ModelState.AddModelError(nameof(reading.SoilMoisture), "Soil sensors must report soil moisture.");
            }
            else
            {
                reading.SoilDensity = reading.SoilMoisture = reading.SoilPh = reading.Nitrogen = reading.Phosphorus = reading.Potassium = null;
                if (reading.Humidity == null) ModelState.AddModelError(nameof(reading.Humidity), "Humidity sensors must report humidity.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateDevicesAsync(field, DeviceType.SoilSensor, DeviceType.HumiditySensor);
                return View(reading);
            }

            reading.RecordedAt = reading.RecordedAt.ToUniversalTime();
            _db.SensorReadings.Add(reading);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Reading recorded.";
            return RedirectToAction("Details", "Fields", new { id = field.Id });
        }

        // ---------- Drone images ----------

        [HttpGet]
        public async Task<IActionResult> AddDroneImage(int fieldId)
        {
            var field = await _access.GetOwnedFieldAsync(User, fieldId);
            if (field == null) return NotFound();
            await PopulateDevicesAsync(field, DeviceType.Drone);
            return View(new DroneImage { FieldId = field.Id, CapturedAt = DateTime.Now });
        }

        private const long MaxImageBytes = 10 * 1024 * 1024;

        /// <summary>Saves a drone image: an uploaded photo (stored in blob storage) or a link to an image hosted elsewhere.</summary>
        [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(MaxImageBytes + 1024 * 1024)]
        public async Task<IActionResult> AddDroneImage(
            [Bind("FieldId,DeviceId,ImageUrl,CapturedAt,Ndvi,Notes")] DroneImage image,
            IFormFile? photo, [FromServices] IImageStorage storage)
        {
            var field = await _access.GetOwnedFieldAsync(User, image.FieldId);
            if (field == null) return NotFound();
            if (image.DeviceId != null && !await _db.Devices.AnyAsync(d =>
                    d.Id == image.DeviceId && d.DeviceType == DeviceType.Drone && d.Field!.FarmId == field.FarmId))
                ModelState.AddModelError(nameof(image.DeviceId), "Choose one of your drones.");

            string? contentType = null;
            if (photo is { Length: > 0 })
            {
                var ext = Path.GetExtension(photo.FileName);
                if (!ImageNames.AllowedUploadTypes.TryGetValue(ext, out contentType))
                    ModelState.AddModelError("photo", "Upload a JPG, PNG, WebP or GIF image.");
                else if (photo.Length > MaxImageBytes)
                    ModelState.AddModelError("photo", "Images must be 10 MB or smaller.");
            }
            else if (string.IsNullOrWhiteSpace(image.ImageUrl))
            {
                ModelState.AddModelError("photo", "Upload a photo or enter an image URL.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateDevicesAsync(field, DeviceType.Drone);
                return View(image);
            }

            if (photo is { Length: > 0 })
            {
                await using var stream = photo.OpenReadStream();
                image.BlobName = await storage.UploadAsync(stream, contentType!, photo.FileName, HttpContext.RequestAborted);
                image.ContentType = contentType;
                image.ImageUrl = null;
            }

            image.CapturedAt = image.CapturedAt.ToUniversalTime();
            _db.DroneImages.Add(image);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Drone image added.";
            return RedirectToAction("Details", "Fields", new { id = field.Id });
        }

        // ---------- Recommendations ----------

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AcknowledgeRecommendation(int id)
        {
            var rec = await _db.Recommendations.FindAsync(id);
            if (rec == null || await _access.GetOwnedFieldAsync(User, rec.FieldId) == null) return NotFound();
            rec.IsAcknowledged = true;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Recommendation marked as actioned.";
            return RedirectToAction("Details", "Fields", new { id = rec.FieldId });
        }

        /// <summary>Lists the farmer's devices of the given types. Drones fly over the whole farm, so all the farm's drones are offered.</summary>
        private async Task PopulateDevicesAsync(Field field, params DeviceType[] types)
        {
            var query = _db.Devices.Where(d => d.IsActive && types.Contains(d.DeviceType));
            query = types.Contains(DeviceType.Drone)
                ? query.Where(d => d.Field!.FarmId == field.FarmId)
                : query.Where(d => d.FieldId == field.Id);
            var devices = await query.OrderBy(d => d.Name).ToListAsync();
            ViewBag.Field = field;
            ViewBag.Devices = devices.Select(d => new SelectListItem($"{d.Name} ({d.DeviceType}, {d.SerialNumber})", d.Id.ToString())).ToList();
        }
    }
}
