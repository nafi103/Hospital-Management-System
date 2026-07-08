using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Models
{
    [Index(nameof(BedNumber), IsUnique = true)]
    [Index(nameof(CreatedAt))]
    public class Bed
    {
        [Key]
        public int Id { get; set; }

        public string BedNumber { get; set; }
        public BedCategory Category { get; set; }
        public decimal DailyRate { get; set; }

        [NotMapped]
        public string Status 
        {
            get 
            {
                bool isOccupied = BedTransfers != null && BedTransfers.Any(bt => bt.EndDate == null);
                return isOccupied ? "Occupied" : "Available";
            }
        }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public List<BedTransfer> BedTransfers { get; set; } = new List<BedTransfer>();
    }
}