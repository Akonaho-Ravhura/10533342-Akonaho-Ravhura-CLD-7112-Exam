using System.ComponentModel.DataAnnotations;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Models
{
    /// <summary>A soil sensor, humidity sensor or drone installed on / assigned to a field.</summary>
    public class IoTDevice
    {
        public int Id { get; set; }

        public int FieldId { get; set; }
        public Field? Field { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Device type")]
        public DeviceType DeviceType { get; set; }

        [Required, StringLength(50), Display(Name = "Serial number")]
        public string SerialNumber { get; set; } = string.Empty;

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Installed on"), DataType(DataType.Date)]
        public DateTime InstalledOn { get; set; } = DateTime.UtcNow.Date;

        public ICollection<SensorReading> Readings { get; set; } = new List<SensorReading>();
        public ICollection<DroneImage> Images { get; set; } = new List<DroneImage>();
    }
}
