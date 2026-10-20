using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace PhotoRental.Tests.Api;

public class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Firebase:ProjectId", "photorental-test");
        builder.UseSetting("Firebase:EmulatorHost", Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST") ?? "localhost:1");
        builder.UseSetting("Jwt:Key", "test-only-key-0123456789-0123456789-0123456789-0123456789");
    }
}
