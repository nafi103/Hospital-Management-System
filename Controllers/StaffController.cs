using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Controllers
{
    [Authorize(Roles = "Admin")]
    public class StaffController : Controller
    {
        private readonly ApplicationDbContext _context;

        public StaffController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Staff
        public async Task<IActionResult> Index()
        {
            var staff = await _context.Users
                .Include(u => u.Role)
                .Include(u => u.AssignedDoctor)
                .OrderBy(u => u.Role.RoleName)
                .ThenBy(u => u.FullName)
                .ToListAsync();
            return View(staff);
        }

        // GET: Staff/Create
        public async Task<IActionResult> Create()
        {
            await PopulateStaffDropDownsAsync();
            return View();
        }

        // POST: Staff/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("RoleId,Username,Password,FullName,Category,AssignedDoctorId")] User user)
        {
            ModelState.Remove("Role");
            ModelState.Remove("AssignedDoctor");
            if (string.IsNullOrEmpty(user.Category))
            {
                user.Category = "";
                ModelState.Remove("Category");
            }

            var usernameTrimmed = user.Username?.Trim() ?? string.Empty;
            user.Username = usernameTrimmed;
            if (string.IsNullOrWhiteSpace(usernameTrimmed))
            {
                ModelState.AddModelError("Username", "Username is required.");
            }
            else if (await _context.Users.AnyAsync(u => u.Username.ToLower() == usernameTrimmed.ToLower()))
            {
                ModelState.AddModelError("Username", "Username is already in use by another staff member.");
            }

            var role = await _context.Roles.FindAsync(user.RoleId);
            if (role != null && role.RoleName != "Assistant")
            {
                user.AssignedDoctorId = null;
            }

            if (ModelState.IsValid)
            {
                try
                {
                    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(user.Password);
                    user.CreatedAt = DateTime.UtcNow;
                    user.UpdatedAt = DateTime.UtcNow;
                    _context.Add(user);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Staff member {user.FullName} successfully added!";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateException)
                {
                    if (await _context.Users.AnyAsync(u => u.Username.ToLower() == usernameTrimmed.ToLower()))
                    {
                        ModelState.AddModelError("Username", "Username is already in use by another staff member.");
                    }
                    else
                    {
                        ModelState.AddModelError(string.Empty, "Unable to save staff member due to a database conflict.");
                    }
                }
            }
            await PopulateStaffDropDownsAsync(user.RoleId, user.AssignedDoctorId);
            return View(user);
        }

        // GET: Staff/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            await PopulateStaffDropDownsAsync(user.RoleId, user.AssignedDoctorId);
            return View(user);
        }

        // POST: Staff/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,RoleId,Username,Password,FullName,Category,AssignedDoctorId,CreatedAt")] User user)
        {
            if (id != user.Id) return NotFound();

            var originalUser = await _context.Users.AsNoTracking().Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id);
            if (originalUser == null) return NotFound();

            ModelState.Remove("Role");
            ModelState.Remove("AssignedDoctor");
            if (string.IsNullOrEmpty(user.Category))
            {
                user.Category = "";
                ModelState.Remove("Category");
            }
            // Password is a non-nullable string, so ASP.NET Core's implicit-required
            // validation rejects the submission outright when this field is left blank -
            // the "blank means keep the current password" handling further down never
            // even runs unless this is removed from ModelState first.
            if (string.IsNullOrWhiteSpace(user.Password))
            {
                ModelState.Remove("Password");
            }

            var usernameTrimmed = user.Username?.Trim() ?? string.Empty;
            user.Username = usernameTrimmed;
            if (string.IsNullOrWhiteSpace(usernameTrimmed))
            {
                ModelState.AddModelError("Username", "Username is required.");
            }
            else if (await _context.Users.AnyAsync(u => u.Id != user.Id && u.Username.ToLower() == usernameTrimmed.ToLower()))
            {
                ModelState.AddModelError("Username", "Username is already in use by another staff member.");
            }

            var role = await _context.Roles.FindAsync(user.RoleId);
            if (role != null && role.RoleName != "Assistant")
            {
                user.AssignedDoctorId = null;
            }

            // Prevent sole admin demotion or self-demotion
            if (originalUser.Role?.RoleName == "Admin" && role?.RoleName != "Admin")
            {
                var adminCount = await _context.Users.CountAsync(u => u.Role.RoleName == "Admin");
                if (adminCount <= 1)
                {
                    ModelState.AddModelError("RoleId", "Cannot change role: this user is the only Administrator in the system.");
                }
                else
                {
                    var currentUserIdStr = User.FindFirst("UserId")?.Value;
                    if (int.TryParse(currentUserIdStr, out int currentUserId) && currentUserId == id)
                    {
                        ModelState.AddModelError("RoleId", "You cannot demote your own administrator account.");
                    }
                }
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // A blank password field means "keep the current password" - the
                    // previous version re-hashed unconditionally, so saving the edit form
                    // with the password field left empty silently replaced every account's
                    // password with the hash of an empty string.
                    if (!string.IsNullOrWhiteSpace(user.Password))
                    {
                        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(user.Password);
                    }
                    else
                    {
                        user.PasswordHash = originalUser.PasswordHash ?? string.Empty;
                    }
                    // Npgsql requires UTC for timestamp with time zone
                    user.CreatedAt = DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc);
                    user.UpdatedAt = DateTime.UtcNow;
                    _context.Update(user);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Staff member {user.FullName} successfully updated!";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!UserExists(user.Id)) return NotFound();
                    else throw;
                }
                catch (DbUpdateException)
                {
                    if (await _context.Users.AnyAsync(u => u.Id != user.Id && u.Username.ToLower() == usernameTrimmed.ToLower()))
                    {
                        ModelState.AddModelError("Username", "Username is already in use by another staff member.");
                    }
                    else
                    {
                        ModelState.AddModelError(string.Empty, "Unable to update staff member due to a database conflict.");
                    }
                }
            }
            await PopulateStaffDropDownsAsync(user.RoleId, user.AssignedDoctorId);
            return View(user);
        }

        private async Task PopulateStaffDropDownsAsync(int? selectedRoleId = null, int? selectedDoctorId = null)
        {
            ViewData["RoleId"] = new SelectList(await _context.Roles.OrderBy(r => r.RoleName).ToListAsync(), "Id", "RoleName", selectedRoleId);

            var doctors = await _context.Users
                .Include(u => u.Role)
                .Where(u => u.Role.RoleName == "Doctor")
                .OrderBy(u => u.FullName)
                .Select(u => new
                {
                    u.Id,
                    DisplayName = "Dr. " + u.FullName + (!string.IsNullOrEmpty(u.Category) ? " (" + u.Category + ")" : "")
                })
                .ToListAsync();

            ViewData["AssignedDoctorId"] = new SelectList(doctors, "Id", "DisplayName", selectedDoctorId);
        }

        // GET: Staff/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(m => m.Id == id);
            
            if (user == null) return NotFound();

            var currentUserIdStr = User.FindFirst("UserId")?.Value;
            int.TryParse(currentUserIdStr, out int currentUserId);

            ViewBag.CanDelete = true;
            if (currentUserId == id)
            {
                ViewBag.CanDelete = false;
                ViewBag.DeleteWarning = "You cannot delete your own administrator account.";
            }
            else if (user.Role?.RoleName == "Admin")
            {
                var adminCount = await _context.Users.CountAsync(u => u.Role.RoleName == "Admin");
                if (adminCount <= 1)
                {
                    ViewBag.CanDelete = false;
                    ViewBag.DeleteWarning = "Cannot delete the only Administrator in the system.";
                }
            }

            return View(user);
        }

        // POST: Staff/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var currentUserIdStr = User.FindFirst("UserId")?.Value;
            int.TryParse(currentUserIdStr, out int currentUserId);

            if (currentUserId == id)
            {
                TempData["ErrorMessage"] = "You cannot delete your own administrator account.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user != null)
            {
                if (user.Role?.RoleName == "Admin")
                {
                    var adminCount = await _context.Users.CountAsync(u => u.Role.RoleName == "Admin");
                    if (adminCount <= 1)
                    {
                        TempData["ErrorMessage"] = "Cannot delete the only Administrator in the system.";
                        return RedirectToAction(nameof(Index));
                    }
                }

                try
                {
                    _context.Users.Remove(user);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Staff member {user.FullName} was deleted.";
                }
                catch (DbUpdateException)
                {
                    TempData["ErrorMessage"] = $"Cannot delete {user.FullName} because they are linked to existing hospital records (e.g. Admissions, Appointments).";
                    return RedirectToAction(nameof(Delete), new { id = id });
                }
            }
            
            return RedirectToAction(nameof(Index));
        }

        private bool UserExists(int id)
        {
            return _context.Users.Any(e => e.Id == id);
        }
    }
}
