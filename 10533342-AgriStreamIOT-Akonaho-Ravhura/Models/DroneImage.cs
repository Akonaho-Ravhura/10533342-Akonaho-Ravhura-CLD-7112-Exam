using System.ComponentModel.DataAnnotations;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Models
{
    /// <summary>
    /// A picture of a field taken by a drone. The image file lives in Azure Blob Storage
    /// (<see cref="BlobName"/>); SQL only stores its metadata. <see cref="ImageUrl"/> is kept
    /// for images hosted elsewhere.
    /// </summary>
    public class DroneImage
    {
        public int Id { get; set; }

        public int FieldId { get; set; }
        public Field? Field { get; set; }

        [Display(Name = "Drone")]
        public int? DeviceId { get; set; }
        public IoTDevice? Device { get; set; }

        /// <summary>Name of the blob in the drone-images container (e.g. 2026/11/06/abc.png).</summary>
        [StringLength(300)]
        public string? BlobName { get; set; }

        [StringLength(100)]
        public string? ContentType { get; set; }

        [StringLength(500), Url, Display(Name = "Image URL")]
        public string? ImageUrl { get; set; }

        [Display(Name = "Captured at")]
        public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Normalised Difference Vegetation Index (-1..1), a common drone-derived crop health indicator.</summary>
        [Display(Name = "NDVI"), Range(-1, 1)]
        public decimal? Ndvi { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }
    }
}
