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
            string username = role == "Doctor" ? "drmock" : 
                              role == "Assistant" ? "receptionistmock" : 
                              role == "Pharmacist" ? "pharmacistmock" : "admin";
            
            var user = _context.Users
                .Include(u => u.Role)
                .FirstOrDefault(u => u.Username == username);

            if (user == null)
            {
                return RedirectToAction("Login");
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
            else if (user.Role.RoleName == "Pharmacist")
            {
                return RedirectToAction("Index", "Prescriptions");
            }
            else
            {
                return RedirectToAction("Index", "Home");
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
