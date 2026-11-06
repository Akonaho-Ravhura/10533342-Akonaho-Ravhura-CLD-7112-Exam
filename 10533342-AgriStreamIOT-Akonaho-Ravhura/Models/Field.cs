using System.ComponentModel.DataAnnotations;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Models
{
    /// <summary>A field (block / paddock) on a farm where a crop is grown and devices are installed.</summary>
    public class Field
    {
        public int Id { get; set; }

        public int FarmId { get; set; }
        public Farm? Farm { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Crop"), StringLength(100)]
        public string? CropType { get; set; }

        [Display(Name = "Area (ha)"), Range(0, 100_000)]
        public decimal? AreaHectares { get; set; }

        [Display(Name = "Planted on"), DataType(DataType.Date)]
        public DateTime? PlantedOn { get; set; }

        public ICollection<IoTDevice> Devices { get; set; } = new List<IoTDevice>();
        public ICollection<SensorReading> SensorReadings { get; set; } = new List<SensorReading>();
        public ICollection<DroneImage> DroneImages { get; set; } = new List<DroneImage>();
        public ICollection<Recommendation> Recommendations { get; set; } = new List<Recommendation>();
    }
}
