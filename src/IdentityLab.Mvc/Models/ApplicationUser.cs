using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace IdentityLab.Mvc.Models;

public sealed class ApplicationUser : IdentityUser
{
    [MaxLength(100)]
    public string? FullName { get; set; }
    
    public ApplicationUser(string? fullName,string userName, string email ) : base(userName)
    {
        Email = email;
        FullName = fullName;
    }
}