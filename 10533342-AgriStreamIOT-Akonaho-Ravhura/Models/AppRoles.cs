namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Models
{
    /// <summary>The three user types supported by AgriStream IoT.</summary>
    public static class AppRoles
    {
        /// <summary>Administers the system: users, roles and farm assignments.</summary>
        public const string Admin = "Admin";

        /// <summary>Owns farms and collects data from soil sensors, humidity sensors and drones.</summary>
        public const string Farmer = "Farmer";

        /// <summary>Analyses farmers' data and makes crop health and yield recommendations.</summary>
        public const string Agronomist = "Agronomist";

        public static readonly string[] All = { Admin, Farmer, Agronomist };

        public static string DisplayName(string role) => role switch
        {
            Admin => "Administrator",
            Farmer => "Farmer",
            Agronomist => "Agronomist",
            _ => role
        };
    }
}
