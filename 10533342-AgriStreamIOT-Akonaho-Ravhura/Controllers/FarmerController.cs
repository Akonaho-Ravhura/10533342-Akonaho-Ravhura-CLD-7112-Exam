using _10533342_AgriStreamIOT_Akonaho_Ravhura.Data;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models.ViewModels;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Controllers
{
    /// <summary>
    /// Farmers manage their farms and fields and collect data from their IoT devices:
    /// soil sensors (density, moisture, pH, N/P/K), humidity sensors and drones (field images).
    /// </summary>
    [Authorize(Roles = AppRoles.Farmer)]
    public class FarmerController : Controller
    {
        private readonly AgriDataStore _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly FarmAccessService _access;

        public FarmerController(AgriDataStore db, UserManager<ApplicationUser> userManager, FarmAccessService access)
        {
            _db = db;
            _userManager = userManager;
            _access = access;
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.Farms = (await _access.VisibleFarmsAsync(User)).OrderBy(f => f.Name).ToList();
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

            farm.CreatedAt = DateTime.UtcNow;
            await _db.AddAsync(farm);
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
            await _db.AddAsync(field);
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
            if ((await _db.ListAsync<IoTDevice>()).Any(d => string.Equals(d.SerialNumber, device.SerialNumber, StringComparison.OrdinalIgnoreCase)))
                ModelState.AddModelError(nameof(device.SerialNumber), "A device with this serial number is already registered.");
            if (!ModelState.IsValid) { ViewBag.Field = field; return View(device); }

            await _db.AddAsync(device);
            TempData["Success"] = $"Device '{device.Name}' registered.";
            return RedirectToAction("Details", "Fields", new { id = field.Id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleDevice(int id)
        {
            var device = await _db.GetAsync<IoTDevice>(id);
            if (device == null || await _access.GetOwnedFieldAsync(User, device.FieldId) == null) return NotFound();
            device.IsActive = !device.IsActive;
            await _db.UpdateAsync(device);
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

            var device = await _db.GetAsync<IoTDevice>(reading.DeviceId);
            if (device == null || device.FieldId != field.Id || device.DeviceType == DeviceType.Drone)
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
            await _db.AddAsync(reading);
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
            if (image.DeviceId != null && !await IsDroneOnFarmAsync(image.DeviceId.Value, field.FarmId))
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
            await _db.AddAsync(image);
            TempData["Success"] = "Drone image added.";
            return RedirectToAction("Details", "Fields", new { id = field.Id });
        }

        private async Task<bool> IsDroneOnFarmAsync(int deviceId, int farmId)
        {
            var device = await _db.GetAsync<IoTDevice>(deviceId);
            if (device == null || device.DeviceType != DeviceType.Drone) return false;
            var deviceField = await _db.GetAsync<Field>(device.FieldId);
            return deviceField?.FarmId == farmId;
        }

        // ---------- Recommendations ----------

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AcknowledgeRecommendation(int id)
        {
            var rec = await _db.GetAsync<Recommendation>(id);
            if (rec == null || await _access.GetOwnedFieldAsync(User, rec.FieldId) == null) return NotFound();
            rec.IsAcknowledged = true;
            await _db.UpdateAsync(rec);
            TempData["Success"] = "Recommendation marked as actioned.";
            return RedirectToAction("Details", "Fields", new { id = rec.FieldId });
        }

        /// <summary>Lists the farmer's devices of the given types. Drones fly over the whole farm, so all the farm's drones are offered.</summary>
        private async Task PopulateDevicesAsync(Field field, params DeviceType[] types)
        {
            var devices = (await _db.ListAsync<IoTDevice>()).Where(d => d.IsActive && types.Contains(d.DeviceType));
            if (types.Contains(DeviceType.Drone))
            {
                var farmFieldIds = (await _db.ListAsync<Field>()).Where(f => f.FarmId == field.FarmId).Select(f => f.Id).ToHashSet();
                devices = devices.Where(d => farmFieldIds.Contains(d.FieldId));
            }
            else
            {
                devices = devices.Where(d => d.FieldId == field.Id);
            }
            ViewBag.Field = field;
            ViewBag.Devices = devices.OrderBy(d => d.Name)
                .Select(d => new SelectListItem($"{d.Name} ({d.DeviceType}, {d.SerialNumber})", d.Id.ToString())).ToList();
        }
    }
}
