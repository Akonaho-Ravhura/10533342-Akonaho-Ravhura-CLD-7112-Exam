using System.ComponentModel.DataAnnotations;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Models.ViewModels
{
    public class UserListItemViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int FarmCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>Used by an admin to create a user of any type.</summary>
    public class CreateUserViewModel
    {
        [Required, StringLength(100), Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 6), DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required, Display(Name = "User type")]
        public string Role { get; set; } = AppRoles.Farmer;
    }

    /// <summary>Used by an admin to change a user's details or type.</summary>
    public class EditUserViewModel
    {
        public string Id { get; set; } = string.Empty;

        [Required, StringLength(100), Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, Display(Name = "User type")]
        public string Role { get; set; } = AppRoles.Farmer;
    }

    public class AdminDashboardViewModel
    {
        public int Admins { get; set; }
        public int Farmers { get; set; }
        public int Agronomists { get; set; }
        public int Farms { get; set; }
        public int UnassignedFarms { get; set; }
        public int Fields { get; set; }
        public int Devices { get; set; }
        public int ReadingsLast24h { get; set; }
        public int Recommendations { get; set; }
    }
}
