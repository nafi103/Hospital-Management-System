using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Hospital_Management_System.Models;

namespace Hospital_Management_System.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    [HttpGet("/ClearAdmissions")]
    public async Task<IActionResult> ClearAdmissions([FromServices] HospitalManagementSystem.Models.ApplicationDbContext context)
    {
        await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync(context.Database, "TRUNCATE TABLE \"BedTransfers\", \"Admissions\" CASCADE;");
        return Content("Cleared.");
    }
}
