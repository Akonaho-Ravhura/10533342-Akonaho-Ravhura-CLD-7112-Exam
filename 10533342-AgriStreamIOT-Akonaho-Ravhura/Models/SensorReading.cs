using System.ComponentModel.DataAnnotations;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Models
{
    /// <summary>
    /// One measurement taken by a soil or humidity sensor. Each sensor fills in the values it measures;
    /// the rest stay null (a humidity sensor records humidity/temperature, a soil sensor records the soil values).
    /// </summary>
    public class SensorReading
    {
        public long Id { get; set; }

        public int DeviceId { get; set; }
        public IoTDevice? Device { get; set; }

        /// <summary>Denormalised from the device so field-level queries do not need a join.</summary>
        public int FieldId { get; set; }
        public Field? Field { get; set; }

        [Display(Name = "Recorded at")]
        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

        // ----- Soil sensor values -----

        [Display(Name = "Soil density (g/cm³)"), Range(0, 3)]
        public decimal? SoilDensity { get; set; }

        [Display(Name = "Soil moisture (%)"), Range(0, 100)]
        public decimal? SoilMoisture { get; set; }

        [Display(Name = "Soil pH"), Range(0, 14)]
        public decimal? SoilPh { get; set; }

        [Display(Name = "Nitrogen (mg/kg)"), Range(0, 10_000)]
        public decimal? Nitrogen { get; set; }

        [Display(Name = "Phosphorus (mg/kg)"), Range(0, 10_000)]
        public decimal? Phosphorus { get; set; }

        [Display(Name = "Potassium (mg/kg)"), Range(0, 10_000)]
        public decimal? Potassium { get; set; }

        // ----- Humidity sensor values -----

        [Display(Name = "Humidity (%)"), Range(0, 100)]
        public decimal? Humidity { get; set; }

        [Display(Name = "Temperature (°C)"), Range(-50, 70)]
        public decimal? Temperature { get; set; }
    }
}
