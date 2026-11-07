using _10533342_AgriStreamIOT_Akonaho_Ravhura.Data;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Controllers
{
    /// <summary>
    /// Agronomists review the sensor and drone data from the farms they are assigned to
    /// and record crop health / yield recommendations for each field.
    /// </summary>
    [Authorize(Roles = AppRoles.Agronomist)]
    public class AgronomistController : Controller
    {
        private readonly AgriDataStore _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly FarmAccessService _access;

        public AgronomistController(AgriDataStore db, UserManager<ApplicationUser> userManager, FarmAccessService access)
        {
            _db = db;
            _userManager = userManager;
            _access = access;
        }

        /// <summary>All fields on assigned farms, with the latest readings and automatic alerts.</summary>
        public async Task<IActionResult> Index()
        {
            var summaries = await _access.GetFieldSummariesAsync(User);
            // Fields that need attention first.
            return View(summaries.OrderByDescending(s => s.Alerts.Count).ThenBy(s => s.FarmName).ToList());
        }

        [HttpGet]
        public async Task<IActionResult> CreateRecommendation(int fieldId)
        {
            var field = await _access.GetAdvisedFieldAsync(User, fieldId);
            if (field == null) return NotFound();
            ViewBag.Field = field;
            return View(new Recommendation { FieldId = field.Id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateRecommendation(
            [Bind("FieldId,Title,Details,CropHealth,Priority,ExpectedYieldTonsPerHectare")] Recommendation rec)
        {
            var field = await _access.GetAdvisedFieldAsync(User, rec.FieldId);
            if (field == null) return NotFound();

            rec.AgronomistId = _userManager.GetUserId(User)!;
            ModelState.Remove(nameof(Recommendation.AgronomistId));
            if (!ModelState.IsValid) { ViewBag.Field = field; return View(rec); }

            rec.CreatedAt = DateTime.UtcNow;
            await _db.AddAsync(rec);
            TempData["Success"] = $"Recommendation sent to the farmer of '{field.Farm!.Name}'.";
            return RedirectToAction("Details", "Fields", new { id = field.Id });
        }
    }
}
