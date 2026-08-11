using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Controllers
{
    public class MedicinesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public MedicinesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Medicines
        public async Task<IActionResult> Index()
        {
            var medicines = await _context.Medicines.OrderBy(m => m.Name).ToListAsync();
            return View(medicines);
        }

        // GET: Medicines/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Medicines/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Name,UnitPrice,StockQuantity")] Medicine medicine)
        {


            if (ModelState.IsValid)
            {
                medicine.CreatedAt = DateTime.UtcNow;
                medicine.UpdatedAt = DateTime.UtcNow;
                _context.Add(medicine);
                await _context.SaveChangesAsync();
                
                TempData["SuccessMessage"] = "Medicine added successfully.";
                return RedirectToAction(nameof(Index));
            }
            return View(medicine);
        }

        // GET: Medicines/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var medicine = await _context.Medicines.FindAsync(id);
            if (medicine == null) return NotFound();
            
            return View(medicine);
        }

        // POST: Medicines/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,UnitPrice,StockQuantity,CreatedAt")] Medicine medicine)
        {
            if (id != medicine.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    medicine.UpdatedAt = DateTime.UtcNow;
                    _context.Update(medicine);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Medicine updated successfully.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!MedicineExists(medicine.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(medicine);
        }

        // GET: Medicines/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var medicine = await _context.Medicines.FirstOrDefaultAsync(m => m.Id == id);
            if (medicine == null) return NotFound();

            return View(medicine);
        }

        // POST: Medicines/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var medicine = await _context.Medicines.FindAsync(id);
            if (medicine != null)
            {
                _context.Medicines.Remove(medicine);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Medicine deleted successfully.";
            }
            return RedirectToAction(nameof(Index));
        }

        private bool MedicineExists(int id)
        {
            return _context.Medicines.Any(e => e.Id == id);
        }
    }
}
