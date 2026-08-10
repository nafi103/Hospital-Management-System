using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

using HospitalManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Controllers
{
    public class AuthController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AuthController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Login()
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(string username, string password)
        {
            var user = await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Username == username && u.Password == password);
            if (user == null)
            {
                ViewBag.Error = "Invalid username or password";
                return View();
            }

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

            if (user.Role.RoleName == "Doctor")
            {
                return RedirectToAction("Index", "DoctorDashboard");
            }
            else if (user.Role.RoleName == "Assistant")
            {
                return RedirectToAction("Index", "Appointments");
            }
            else
            {
                return RedirectToAction("Index", "Home");
            }
        }

        [HttpPost]
        public async Task<IActionResult> MockLogin(string role)
        {
            string userName = role == "Doctor" ? "Dr. Mock" : role == "Assistant" ? "Receptionist Mock" : "Admin Mock";
            string roleClaim = role; // "Doctor", "Assistant", or "Admin"

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, userName),
                new Claim(ClaimTypes.Role, roleClaim),
                // We'll hardcode an ID for the doctor if needed, let's say ID 1
                new Claim("UserId", "1")
            };

            if (role == "Assistant")
            {
                // Assign Assistant to Mock Doctor (ID 1)
                claims.Add(new Claim("AssignedDoctorId", "1"));
            }

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                new AuthenticationProperties { IsPersistent = true });

            if (role == "Doctor")
            {
                return RedirectToAction("Index", "DoctorDashboard");
            }
            else if (role == "Assistant")
            {
                return RedirectToAction("Index", "Appointments"); // Queue
            }
            else
            {
                return RedirectToAction("Index", "Home"); // Admin Dashboard
            }
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}
