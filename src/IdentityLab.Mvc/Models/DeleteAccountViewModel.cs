using System.ComponentModel.DataAnnotations;

namespace IdentityLab.Mvc.Models;

public class DeleteAccountViewModel
{
    // True for accounts with a password; accounts created with Google confirm by typing their username.
    public bool RequirePassword { get; set; }

    [DataType(DataType.Password)]
    public string? Password { get; set; }

    [Display(Name = "Type your username to confirm")]
    public string? Confirmation { get; set; }
}
