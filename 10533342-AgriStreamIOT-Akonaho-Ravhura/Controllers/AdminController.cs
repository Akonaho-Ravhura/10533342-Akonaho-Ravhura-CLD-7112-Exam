using _10533342_AgriStreamIOT_Akonaho_Ravhura.Data;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Controllers
{
    /// <summary>System administration: users and their types, and assigning agronomists to farms.</summary>
    [Authorize(Roles = AppRoles.Admin)]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public AdminController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index([FromServices] _10533342_AgriStreamIOT_Akonaho_Ravhura.Services.IImageStorage storage)
        {
            ViewBag.Storage = storage.Description;
            ViewBag.Database = _db.Database.GetDbConnection() is { } c ? $"{c.DataSource} / {c.Database}" : "";
            var since = DateTime.UtcNow.AddHours(-24);
            var model = new AdminDashboardViewModel
            {
                Admins = (await _userManager.GetUsersInRoleAsync(AppRoles.Admin)).Count,
                Farmers = (await _userManager.GetUsersInRoleAsync(AppRoles.Farmer)).Count,
                Agronomists = (await _userManager.GetUsersInRoleAsync(AppRoles.Agronomist)).Count,
                Farms = await _db.Farms.CountAsync(),
                UnassignedFarms = await _db.Farms.CountAsync(f => f.AgronomistId == null),
                Fields = await _db.Fields.CountAsync(),
                Devices = await _db.Devices.CountAsync(),
                ReadingsLast24h = await _db.SensorReadings.CountAsync(r => r.RecordedAt >= since),
                Recommendations = await _db.Recommendations.CountAsync()
            };
            return View(model);
        }

        // ---------- Users ----------

        public async Task<IActionResult> Users(string? role = null)
        {
            var users = await _userManager.Users
                .OrderBy(u => u.FullName)
                .Select(u => new { User = u, FarmCount = u.OwnedFarms.Count + u.AdvisedFarms.Count })
                .ToListAsync();

            var list = new List<UserListItemViewModel>();
            foreach (var x in users)
            {
                var userRole = (await _userManager.GetRolesAsync(x.User)).FirstOrDefault() ?? "";
                if (role != null && userRole != role) continue;
                list.Add(new UserListItemViewModel
                {
                    Id = x.User.Id, FullName = x.User.FullName, Email = x.User.Email ?? "",
                    Role = userRole, FarmCount = x.FarmCount, CreatedAt = x.User.CreatedAt
                });
            }
            ViewBag.RoleFilter = role;
            return View(list);
        }

        [HttpGet]
        public IActionResult CreateUser()
        {
            PopulateRoles();
            return View(new CreateUserViewModel());
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateUser(CreateUserViewModel model)
        {
            if (!AppRoles.All.Contains(model.Role)) ModelState.AddModelError(nameof(model.Role), "Choose a valid user type.");
            if (ModelState.IsValid)
            {
                var user = new ApplicationUser
                {
                    UserName = model.Email, Email = model.Email, EmailConfirmed = true, FullName = model.FullName
                };
                var result = await _userManager.CreateAsync(user, model.Password);
                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(user, model.Role);
                    TempData["Success"] = $"{AppRoles.DisplayName(model.Role)} '{model.FullName}' created.";
                    return RedirectToAction(nameof(Users));
                }
                foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            }
            PopulateRoles();
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> EditUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();
            PopulateRoles();
            return View(new EditUserViewModel
            {
                Id = user.Id, FullName = user.FullName, Email = user.Email ?? "",
                Role = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? AppRoles.Farmer
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUser(EditUserViewModel model)
        {
            var user = await _userManager.FindByIdAsync(model.Id);
            if (user == null) return NotFound();

            var currentRoles = await _userManager.GetRolesAsync(user);
            if (!AppRoles.All.Contains(model.Role))
                ModelState.AddModelError(nameof(model.Role), "Choose a valid user type.");
            if (user.Id == _userManager.GetUserId(User) && model.Role != AppRoles.Admin)
                ModelState.AddModelError(nameof(model.Role), "You cannot remove your own Admin role.");
            if (currentRoles.Contains(AppRoles.Farmer) && model.Role != AppRoles.Farmer
                && await _db.Farms.AnyAsync(f => f.FarmerId == user.Id))
                ModelState.AddModelError(nameof(model.Role), "This farmer still owns farms; they must stay a Farmer.");

            if (ModelState.IsValid)
            {
                user.FullName = model.FullName;
                user.Email = model.Email;
                user.UserName = model.Email;
                var result = await _userManager.UpdateAsync(user);
                if (result.Succeeded)
                {
                    if (!currentRoles.Contains(model.Role))
                    {
                        await _userManager.RemoveFromRolesAsync(user, currentRoles);
                        await _userManager.AddToRoleAsync(user, model.Role);
                        // An agronomist who changes type no longer advises any farm.
                        if (currentRoles.Contains(AppRoles.Agronomist))
                            await _db.Farms.Where(f => f.AgronomistId == user.Id)
                                .ExecuteUpdateAsync(s => s.SetProperty(f => f.AgronomistId, (string?)null));
                    }
                    TempData["Success"] = $"'{user.FullName}' updated.";
                    return RedirectToAction(nameof(Users));
                }
                foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            }
            PopulateRoles();
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            string? blocker = null;
            if (user.Id == _userManager.GetUserId(User)) blocker = "You cannot delete your own account.";
            else if (await _db.Farms.AnyAsync(f => f.FarmerId == user.Id)) blocker = "This farmer still owns farms.";
            else if (await _db.Recommendations.AnyAsync(r => r.AgronomistId == user.Id))
                blocker = "This agronomist has recommendations on record; change their user type instead of deleting.";
            if (blocker != null)
            {
                TempData["Error"] = blocker;
                return RedirectToAction(nameof(Users));
            }

            await _userManager.DeleteAsync(user); // advised farms are unassigned via SET NULL
            TempData["Success"] = $"'{user.FullName}' deleted.";
            return RedirectToAction(nameof(Users));
        }

        // ---------- Farms: assign agronomists ----------

        public async Task<IActionResult> Farms()
        {
            var farms = await _db.Farms
                .OrderBy(f => f.Name)
                .Select(f => new FarmListItemViewModel
                {
                    Id = f.Id, Name = f.Name, Location = f.Location, SizeHectares = f.SizeHectares,
                    FarmerName = f.Farmer!.FullName,
                    AgronomistId = f.AgronomistId, AgronomistName = f.Agronomist!.FullName,
                    FieldCount = f.Fields.Count
                })
                .ToListAsync();

            var agronomists = await _userManager.GetUsersInRoleAsync(AppRoles.Agronomist);
            ViewBag.Agronomists = agronomists.OrderBy(a => a.FullName)
                .Select(a => new SelectListItem(a.FullName, a.Id)).ToList();
            return View(farms);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignAgronomist(int farmId, string? agronomistId)
        {
            var farm = await _db.Farms.FindAsync(farmId);
            if (farm == null) return NotFound();

            if (!string.IsNullOrEmpty(agronomistId))
            {
                var agronomist = await _userManager.FindByIdAsync(agronomistId);
                if (agronomist == null || !await _userManager.IsInRoleAsync(agronomist, AppRoles.Agronomist))
                {
                    TempData["Error"] = "Choose a valid agronomist.";
                    return RedirectToAction(nameof(Farms));
                }
            }

            farm.AgronomistId = string.IsNullOrEmpty(agronomistId) ? null : agronomistId;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Agronomist for '{farm.Name}' updated.";
            return RedirectToAction(nameof(Farms));
        }

        private void PopulateRoles() =>
            ViewBag.Roles = AppRoles.All.Select(r => new SelectListItem(AppRoles.DisplayName(r), r)).ToList();
    }
}
