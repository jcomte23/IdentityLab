using Microsoft.AspNetCore.Identity;

namespace IdentityLab.Mvc.Models;

public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }
}