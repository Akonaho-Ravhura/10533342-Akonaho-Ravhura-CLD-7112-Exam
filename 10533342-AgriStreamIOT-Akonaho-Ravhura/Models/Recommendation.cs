using System.ComponentModel.DataAnnotations;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Models
{
    /// <summary>An agronomist's advice for a field, based on the farmer's sensor and drone data.</summary>
    public class Recommendation
    {
        public int Id { get; set; }

        public int FieldId { get; set; }
        public Field? Field { get; set; }

        [Required]
        public string AgronomistId { get; set; } = string.Empty;
        public ApplicationUser? Agronomist { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required, StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [Required, StringLength(4000)]
        public string Details { get; set; } = string.Empty;

        [Display(Name = "Crop health")]
        public CropHealthStatus CropHealth { get; set; } = CropHealthStatus.Healthy;

        public RecommendationPriority Priority { get; set; } = RecommendationPriority.Medium;

        [Display(Name = "Expected yield (t/ha)"), Range(0, 1000)]
        public decimal? ExpectedYieldTonsPerHectare { get; set; }

        /// <summary>Set by the farmer once they have acted on the advice.</summary>
        [Display(Name = "Acknowledged")]
        public bool IsAcknowledged { get; set; }
    }
}
