using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Controllers
{
    public class BedsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public BedsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Beds
        public async Task<IActionResult> Index()
        {
            var beds = await _context.Beds
                .Include(b => b.BedTransfers)
                .OrderBy(b => b.Category)
                .ThenBy(b => b.BedNumber)
                .ToListAsync();
            return View(beds);
        }

        // GET: Beds/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Beds/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,BedNumber,Category,DailyRate")] Bed bed)
        {
            if (ModelState.IsValid)
            {
                bed.CreatedAt = DateTime.UtcNow;
                bed.UpdatedAt = DateTime.UtcNow;
                _context.Add(bed);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Bed successfully added.";
                return RedirectToAction(nameof(Index));
            }
            return View(bed);
        }

        // GET: Beds/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var bed = await _context.Beds.FindAsync(id);
            if (bed == null) return NotFound();

            return View(bed);
        }

        // POST: Beds/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,BedNumber,Category,DailyRate,CreatedAt")] Bed bed)
        {
            if (id != bed.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    bed.UpdatedAt = DateTime.UtcNow;
                    _context.Update(bed);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Bed details updated.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!BedExists(bed.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(bed);
        }

        // POST: Beds/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var bed = await _context.Beds.FindAsync(id);
            if (bed != null)
            {
                _context.Beds.Remove(bed);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Bed removed from system.";
            }
            return RedirectToAction(nameof(Index));
        }

        private bool BedExists(int id)
        {
            return _context.Beds.Any(e => e.Id == id);
        }
    }
}
