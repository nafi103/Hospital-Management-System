using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using HospitalManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Controllers
{
    public class AuthController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public AuthController(ApplicationDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login()
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Login(string username, string password)
        {
            var user = await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Username == username);
            if (user == null || string.IsNullOrEmpty(password) || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                ViewBag.Error = "Invalid username or password";
                return View();
            }

            await SignInUserAsync(user);
            return RedirectAfterLogin(user.Role.RoleName);
        }

        // Demo-only shortcut so a presenter can switch roles without remembering
        // passwords. Restricted to Development so it can never be reached once the
        // app is actually deployed, and the role string is matched against an
        // explicit whitelist - an unrecognized value used to fall through to the
        // "admin" account, which would have made this an anonymous privilege
        // escalation to Admin in production.
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MockLogin(string role)
        {
            if (!_environment.IsDevelopment())
            {
                return NotFound();
            }

            string? username = role switch
            {
                "Doctor" => "drmock",
                "Assistant" => "mock-assistant",
                "Pharmacist" => "pharmacistmock",
                "Admin" => "admin",
                "Receptionist" => "reception1",
                _ => null
            };

            if (username == null)
            {
                return RedirectToAction("Login");
            }

            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username == username);

            if (user == null)
            {
                return RedirectToAction("Login");
            }

            await SignInUserAsync(user);
            return RedirectAfterLogin(user.Role.RoleName);
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            return View();
        }

        private IActionResult RedirectAfterLogin(string roleName)
        {
            return roleName switch
            {
                "Doctor" => RedirectToAction("Index", "DoctorDashboard"),
                "Assistant" => RedirectToAction("Index", "Appointments"),
                "Pharmacist" => RedirectToAction("Index", "Prescriptions"),
                "Receptionist" => RedirectToAction("Index", "Reception"),
                "Patient" => RedirectToAction("Index", "Portal"),
                _ => RedirectToAction("Index", "Home")
            };
        }

        private async Task SignInUserAsync(User user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Role, user.Role.RoleName),
                new Claim("UserId", user.Id.ToString())
            };

            if (user.AssignedDoctorId.HasValue)
            {
                claims.Add(new Claim("AssignedDoctorId", user.AssignedDoctorId.Value.ToString()));
            }

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                new AuthenticationProperties { IsPersistent = true });
        }
    }
}
