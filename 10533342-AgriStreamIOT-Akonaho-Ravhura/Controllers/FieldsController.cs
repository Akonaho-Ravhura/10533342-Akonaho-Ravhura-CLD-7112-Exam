using _10533342_AgriStreamIOT_Akonaho_Ravhura.Data;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models.ViewModels;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly FarmAccessService _access;

        public FieldsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, FarmAccessService access)
        {
            _db = db;
            _userManager = userManager;
            _access = access;
        }

        public async Task<IActionResult> Details(int id, int days = 7)
        {
            var field = await _access.VisibleFields(User)
                .Include(f => f.Farm).ThenInclude(f => f!.Farmer)
                .Include(f => f.Farm).ThenInclude(f => f!.Agronomist)
                .FirstOrDefaultAsync(f => f.Id == id);
            if (field == null) return NotFound();

            days = Math.Clamp(days, 1, 365);
            var since = DateTime.UtcNow.AddDays(-days);
            var readings = await _db.SensorReadings
                .Where(r => r.FieldId == id && r.RecordedAt >= since)
                .Include(r => r.Device)
                .OrderByDescending(r => r.RecordedAt)
                .ToListAsync();

            var userId = _userManager.GetUserId(User);
            var model = new FieldDetailsViewModel
            {
                Field = field,
                Devices = await _db.Devices.Where(d => d.FieldId == id).OrderBy(d => d.DeviceType).ThenBy(d => d.Name).ToListAsync(),
                SoilReadings = readings.Where(r => r.SoilMoisture != null).ToList(),
                HumidityReadings = readings.Where(r => r.Humidity != null).ToList(),
                DroneImages = await _db.DroneImages.Where(i => i.FieldId == id).Include(i => i.Device)
                    .OrderByDescending(i => i.CapturedAt).ToListAsync(),
                Recommendations = await _db.Recommendations.Where(r => r.FieldId == id).Include(r => r.Agronomist)
                    .OrderByDescending(r => r.CreatedAt).ToListAsync(),
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
            var visibleFieldIds = _access.VisibleFields(User).Select(f => f.Id);
            var image = await _db.DroneImages
                .FirstOrDefaultAsync(i => i.Id == id && i.BlobName != null && visibleFieldIds.Contains(i.FieldId));
            if (image == null) return NotFound();

            var stored = await storage.OpenAsync(image.BlobName!, HttpContext.RequestAborted);
            if (stored == null) return NotFound();

            // Images are user content: never let the browser sniff or run scripts in them.
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
            Response.Headers["Cache-Control"] = "private, max-age=3600";
            return File(stored.Content, image.ContentType ?? stored.ContentType);
        }
    }
}
