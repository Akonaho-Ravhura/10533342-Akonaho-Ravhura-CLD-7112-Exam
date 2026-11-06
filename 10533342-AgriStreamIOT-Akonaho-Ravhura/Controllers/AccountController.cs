using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models;
using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models.ViewModels;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IConfiguration _config;

        public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IConfiguration config)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _config = config;
        }

        [HttpGet, AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true) return RedirectToAction(nameof(Dashboard));
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                    return LocalRedirect(model.ReturnUrl);
                return RedirectToAction(nameof(Dashboard));
            }

            ModelState.AddModelError(string.Empty, result.IsLockedOut
                ? "This account is locked. Try again later."
                : "Invalid email or password.");
            return View(model);
        }

        /// <summary>
        /// One registration page for every user type. Farmers and agronomists can sign up freely;
        /// registering as an Admin needs the secret code from configuration (Registration:AdminCode).
        /// </summary>
        [HttpGet, AllowAnonymous]
        public IActionResult Register(string? role = null)
        {
            if (User.Identity?.IsAuthenticated == true) return RedirectToAction(nameof(Dashboard));
            ViewBag.AdminRegistrationEnabled = AdminCodeConfigured;
            return View(new RegisterViewModel { Role = AppRoles.All.Contains(role) ? role! : AppRoles.Farmer });
        }

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            ViewBag.AdminRegistrationEnabled = AdminCodeConfigured;

            if (!AppRoles.All.Contains(model.Role))
                ModelState.AddModelError(nameof(model.Role), "Choose Farmer, Agronomist or Admin.");
            else if (model.Role == AppRoles.Admin && !AdminCodeMatches(model.AdminCode))
                ModelState.AddModelError(nameof(model.AdminCode), AdminCodeConfigured
                    ? "The admin registration code is not correct."
                    : "Admin registration is disabled. Ask an existing administrator to create your account.");

            if (!ModelState.IsValid) return View(model);

            var user = new ApplicationUser { UserName = model.Email, Email = model.Email, FullName = model.FullName };
            var result = await _userManager.CreateAsync(user, model.Password);
            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(user, model.Role);
                TempData["Success"] = $"Welcome, {user.FullName}! Your {AppRoles.DisplayName(model.Role).ToLower()} account is ready." +
                    (model.Role == AppRoles.Agronomist ? " An administrator will assign farms to you." : "");
                await _signInManager.SignInAsync(user, isPersistent: false);
                return RedirectToAction(nameof(Dashboard));
            }

            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        [HttpPost, Authorize, ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }

        /// <summary>Sends each user type to its own dashboard.</summary>
        [Authorize]
        public IActionResult Dashboard()
        {
            if (User.IsInRole(AppRoles.Admin)) return RedirectToAction("Index", "Admin");
            if (User.IsInRole(AppRoles.Agronomist)) return RedirectToAction("Index", "Agronomist");
            return RedirectToAction("Index", "Farmer");
        }

        [AllowAnonymous]
        public IActionResult AccessDenied() => View();

        private string? ConfiguredAdminCode => _config["Registration:AdminCode"];
        private bool AdminCodeConfigured => !string.IsNullOrWhiteSpace(ConfiguredAdminCode);

        /// <summary>Constant-time comparison so the code can't be guessed by timing responses.</summary>
        private bool AdminCodeMatches(string? supplied)
        {
            if (!AdminCodeConfigured || string.IsNullOrEmpty(supplied)) return false;
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(ConfiguredAdminCode!));
        }
    }
}
