using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Farm> Farms => Set<Farm>();
        public DbSet<Field> Fields => Set<Field>();
        public DbSet<IoTDevice> Devices => Set<IoTDevice>();
        public DbSet<SensorReading> SensorReadings => Set<SensorReading>();
        public DbSet<DroneImage> DroneImages => Set<DroneImage>();
        public DbSet<Recommendation> Recommendations => Set<Recommendation>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ----- Farm: owned by a farmer, advised by an agronomist -----
            builder.Entity<Farm>(e =>
            {
                e.HasOne(f => f.Farmer).WithMany(u => u.OwnedFarms)
                    .HasForeignKey(f => f.FarmerId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(f => f.Agronomist).WithMany(u => u.AdvisedFarms)
                    .HasForeignKey(f => f.AgronomistId).OnDelete(DeleteBehavior.SetNull);
                e.Property(f => f.SizeHectares).HasPrecision(10, 2);
            });

            builder.Entity<Field>(e =>
            {
                e.HasOne(f => f.Farm).WithMany(f => f.Fields)
                    .HasForeignKey(f => f.FarmId).OnDelete(DeleteBehavior.Cascade);
                e.Property(f => f.AreaHectares).HasPrecision(10, 2);
            });

            builder.Entity<IoTDevice>(e =>
            {
                e.HasOne(d => d.Field).WithMany(f => f.Devices)
                    .HasForeignKey(d => d.FieldId).OnDelete(DeleteBehavior.Cascade);
                e.HasIndex(d => d.SerialNumber).IsUnique();
                e.Property(d => d.DeviceType).HasConversion<string>().HasMaxLength(30);
            });

            // SQL Server forbids multiple cascade paths, so readings/images cascade from
            // the field and only restrict on the device (devices are deactivated, not deleted).
            builder.Entity<SensorReading>(e =>
            {
                e.HasOne(r => r.Field).WithMany(f => f.SensorReadings)
                    .HasForeignKey(r => r.FieldId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(r => r.Device).WithMany(d => d.Readings)
                    .HasForeignKey(r => r.DeviceId).OnDelete(DeleteBehavior.NoAction);
                e.HasIndex(r => new { r.FieldId, r.RecordedAt });
                e.Property(r => r.SoilDensity).HasPrecision(5, 2);
                e.Property(r => r.SoilMoisture).HasPrecision(5, 2);
                e.Property(r => r.SoilPh).HasPrecision(4, 2);
                e.Property(r => r.Nitrogen).HasPrecision(8, 2);
                e.Property(r => r.Phosphorus).HasPrecision(8, 2);
                e.Property(r => r.Potassium).HasPrecision(8, 2);
                e.Property(r => r.Humidity).HasPrecision(5, 2);
                e.Property(r => r.Temperature).HasPrecision(5, 2);
            });

            builder.Entity<DroneImage>(e =>
            {
                e.HasOne(i => i.Field).WithMany(f => f.DroneImages)
                    .HasForeignKey(i => i.FieldId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(i => i.Device).WithMany(d => d.Images)
                    .HasForeignKey(i => i.DeviceId).OnDelete(DeleteBehavior.NoAction);
                e.Property(i => i.Ndvi).HasPrecision(4, 3);
            });

            builder.Entity<Recommendation>(e =>
            {
                e.HasOne(r => r.Field).WithMany(f => f.Recommendations)
                    .HasForeignKey(r => r.FieldId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(r => r.Agronomist).WithMany()
                    .HasForeignKey(r => r.AgronomistId).OnDelete(DeleteBehavior.Restrict);
                e.Property(r => r.CropHealth).HasConversion<string>().HasMaxLength(20);
                e.Property(r => r.Priority).HasConversion<string>().HasMaxLength(20);
                e.Property(r => r.ExpectedYieldTonsPerHectare).HasPrecision(8, 2);
            });
        }
    }
}
