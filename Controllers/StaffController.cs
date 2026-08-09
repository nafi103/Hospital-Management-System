using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Controllers
{
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
                .OrderBy(u => u.Role.RoleName)
                .ThenBy(u => u.FullName)
                .ToListAsync();
            return View(staff);
        }

        // GET: Staff/Create
        public IActionResult Create()
        {
            ViewData["RoleId"] = new SelectList(_context.Roles, "Id", "RoleName");
            return View();
        }

        // POST: Staff/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("RoleId,Username,Password,FullName,Category")] User user)
        {
            ModelState.Remove("Role");
            if (string.IsNullOrEmpty(user.Category))
            {
                user.Category = "";
                ModelState.Remove("Category");
            }

            if (ModelState.IsValid)
            {
                user.CreatedAt = DateTime.UtcNow;
                user.UpdatedAt = DateTime.UtcNow;
                _context.Add(user);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Staff member {user.FullName} successfully added!";
                return RedirectToAction(nameof(Index));
            }
            ViewData["RoleId"] = new SelectList(_context.Roles, "Id", "RoleName", user.RoleId);
            return View(user);
        }

        // GET: Staff/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            ViewData["RoleId"] = new SelectList(_context.Roles, "Id", "RoleName", user.RoleId);
            return View(user);
        }

        // POST: Staff/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,RoleId,Username,Password,FullName,Category,CreatedAt")] User user)
        {
            if (id != user.Id) return NotFound();

            ModelState.Remove("Role");
            if (string.IsNullOrEmpty(user.Category))
            {
                user.Category = "";
                ModelState.Remove("Category");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // Npgsql requires UTC for timestamp with time zone
                    user.CreatedAt = DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc);
                    user.UpdatedAt = DateTime.UtcNow;
                    _context.Update(user);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Staff member {user.FullName} successfully updated!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!UserExists(user.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            ViewData["RoleId"] = new SelectList(_context.Roles, "Id", "RoleName", user.RoleId);
            return View(user);
        }

        // GET: Staff/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(m => m.Id == id);
            
            if (user == null) return NotFound();

            return View(user);
        }

        // POST: Staff/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user != null)
            {
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
