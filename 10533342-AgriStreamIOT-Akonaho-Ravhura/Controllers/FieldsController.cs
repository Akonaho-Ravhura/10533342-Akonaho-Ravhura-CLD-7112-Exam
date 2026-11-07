using _10533342_AgriStreamIOT_Akonaho_Ravhura.Data;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models.ViewModels;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Services;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Controllers
{
    /// <summary>
    /// The field data page, shared by all three user types. What a user can see and do is
    /// decided by <see cref="FarmAccessService"/>: the owning farmer can add data, the assigned
    /// agronomist can add recommendations, and admins can view everything.
    /// </summary>
    [Authorize]
    public class FieldsController : Controller
    {
        private readonly AgriDataStore _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly FarmAccessService _access;

        public FieldsController(AgriDataStore db, UserManager<ApplicationUser> userManager, FarmAccessService access)
        {
            _db = db;
            _userManager = userManager;
            _access = access;
        }

        public async Task<IActionResult> Details(int id, int days = 7)
        {
            var field = (await _access.VisibleFieldsAsync(User)).FirstOrDefault(f => f.Id == id);
            if (field == null) return NotFound();

            days = Math.Clamp(days, 1, 365);
            var devices = (await _db.ListAsync<IoTDevice>()).ToDictionary(d => d.Id);
            var readings = (await _db.ReadingsForFieldAsync(id, DateTime.UtcNow.AddDays(-days)))
                .OrderByDescending(r => r.RecordedAt).ToList();
            foreach (var r in readings) r.Device = devices.GetValueOrDefault(r.DeviceId);

            var fieldFilter = TableClient.CreateQueryFilter($"FieldId eq {id}");
            var images = (await _db.ListAsync<DroneImage>(fieldFilter)).OrderByDescending(i => i.CapturedAt).ToList();
            foreach (var i in images) i.Device = i.DeviceId == null ? null : devices.GetValueOrDefault(i.DeviceId.Value);

            var users = (await _db.ListUsersAsync()).ToDictionary(u => u.Id);
            var recommendations = (await _db.ListAsync<Recommendation>(fieldFilter)).OrderByDescending(r => r.CreatedAt).ToList();
            foreach (var r in recommendations) r.Agronomist = users.GetValueOrDefault(r.AgronomistId);

            var userId = _userManager.GetUserId(User);
            var model = new FieldDetailsViewModel
            {
                Field = field,
                Devices = devices.Values.Where(d => d.FieldId == id).OrderBy(d => d.DeviceType).ThenBy(d => d.Name).ToList(),
                SoilReadings = readings.Where(r => r.SoilMoisture != null).ToList(),
                HumidityReadings = readings.Where(r => r.Humidity != null).ToList(),
                DroneImages = images,
                Recommendations = recommendations,
                CanEdit = User.IsInRole(AppRoles.Farmer) && field.Farm!.FarmerId == userId,
                CanAdvise = User.IsInRole(AppRoles.Agronomist) && field.Farm!.AgronomistId == userId
            };
            ViewBag.Days = days;
            return View(model);
        }

        /// <summary>
        /// Streams a drone image from blob storage. The container is private, so images are only
        /// reachable through here, and only by users allowed to see the field.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Image(int id, [FromServices] IImageStorage storage)
        {
            var image = await _db.GetAsync<DroneImage>(id);
            if (image?.BlobName == null) return NotFound();
            if (!(await _access.VisibleFieldsAsync(User)).Any(f => f.Id == image.FieldId)) return NotFound();

            var stored = await storage.OpenAsync(image.BlobName, HttpContext.RequestAborted);
            if (stored == null) return NotFound();

            // Images are user content: never let the browser sniff or run scripts in them.
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
            Response.Headers["Cache-Control"] = "private, max-age=3600";
            return File(stored.Content, image.ContentType ?? stored.ContentType);
        }
    }
}
