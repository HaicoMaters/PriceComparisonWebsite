using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace PriceComparisonWebsite.Models
{
    // <summary>
    // Represents a login activity of a user
    // </summary>
    public class LoginActivity
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; }

        [ValidateNever]
        [ForeignKey("UserId")]
        public ApplicationUser User { get; set; }

        [Required]
        [DataType(DataType.DateTime)]
        public DateTime LoginTime { get; set; } = DateTime.Now;

        [Required]
        [MaxLength(45)]
        public string IpAddress { get; set; }

        [Required]
        public bool IsSuccessful { get; set; }

        [MaxLength(500)]
        public string? ErrorMessage { get; set; } = null;
    }
}