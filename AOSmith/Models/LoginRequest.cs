using System.ComponentModel.DataAnnotations;

namespace AOSmith.Models
{
    /// <summary>
    /// Login request model
    /// </summary>
    public class LoginRequest
    {
        [Required(ErrorMessage = "Company is required")]
        [Display(Name = "Company")]
        public string CompanyName { get; set; }

        [Required(ErrorMessage = "Username is required")]
        [Display(Name = "Username")]
        public string Username { get; set; }

        [Required(ErrorMessage = "Password is required")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; }

        [Display(Name = "Remember Me")]
        public bool RememberMe { get; set; }
    }
}
