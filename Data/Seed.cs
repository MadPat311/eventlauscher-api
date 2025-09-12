using EventLauscherApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EventLauscherApi.Data
{
    public static class Seed
    {
        public static async Task EnsureSeedAsync(IApplicationBuilder app)
        {
            using var scope = app.ApplicationServices.CreateScope();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var db    = scope.ServiceProvider.GetRequiredService<EventContext>();

            // 1) DB auf neuesten Stand
            await db.Database.MigrateAsync();

            // 2) Rollen anlegen
            foreach (var r in new[] { "User", "Reviewer", "Admin" })
                if (!await roles.RoleExistsAsync(r))
                    await roles.CreateAsync(new AppRole { Name = r });

            // 3) Admin-User anlegen (DEV-Creds — später sicher ändern!)
            var adminEmail = "admin@eventlauscher.de";
            var admin = await users.FindByEmailAsync(adminEmail);
            if (admin is null)
            {
                admin = new AppUser { Email = adminEmail, UserName = "admin", EmailConfirmed = true };
                var create = await users.CreateAsync(admin, "Admin!123"); // DEV ONLY
                if (create.Succeeded)
                    await users.AddToRolesAsync(admin, new[] { "Admin", "Reviewer", "User" });
            }
        }
    }
}
