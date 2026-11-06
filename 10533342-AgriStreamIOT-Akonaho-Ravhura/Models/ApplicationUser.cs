using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Models
{
    public class ApplicationUser : IdentityUser
    {
        [Required, StringLength(100), Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Farms this user owns (farmers only).</summary>
        public ICollection<Farm> OwnedFarms { get; set; } = new List<Farm>();

        /// <summary>Farms this user advises (agronomists only).</summary>
        public ICollection<Farm> AdvisedFarms { get; set; } = new List<Farm>();
    }
}
