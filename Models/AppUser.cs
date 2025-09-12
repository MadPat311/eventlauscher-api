using Microsoft.AspNetCore.Identity;

namespace EventLauscherApi.Models;

public class AppUser : IdentityUser<Guid>
{
    // Platz für spätere Profileigenschaften (DisplayName, AvatarUrl, ...)
}

public class AppRole : IdentityRole<Guid> { }
