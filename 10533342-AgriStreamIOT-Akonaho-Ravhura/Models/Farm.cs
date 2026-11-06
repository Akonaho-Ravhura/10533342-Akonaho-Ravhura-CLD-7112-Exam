using System.ComponentModel.DataAnnotations;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Models
{
    /// <summary>A farm owned by a farmer and optionally advised by an agronomist.</summary>
    public class Farm
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(200)]
        public string? Location { get; set; }

        [Display(Name = "Size (ha)"), Range(0, 1_000_000)]
        public decimal? SizeHectares { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public string FarmerId { get; set; } = string.Empty;
        public ApplicationUser? Farmer { get; set; }

        /// <summary>The agronomist assigned (by an admin) to advise this farm.</summary>
        [Display(Name = "Agronomist")]
        public string? AgronomistId { get; set; }
        public ApplicationUser? Agronomist { get; set; }

        public ICollection<Field> Fields { get; set; } = new List<Field>();
    }
}
