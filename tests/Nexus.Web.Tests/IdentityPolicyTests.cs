using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nexus.Infrastructure.DependencyInjection;

namespace Nexus.Web.Tests;

/// <summary>
/// Regression tests for H4 (e-mail confirmation disabled) and M5 (weak password policy).
/// </summary>
public class IdentityPolicyTests
{
    private static IdentityOptions Resolve()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(
            "Host=localhost;Database=nexus_test;Username=u;Password=p",
            new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<IdentityOptions>>().Value;
    }

    /// <summary>
    /// H4: without this, anyone could register using an address they do not control and use the
    /// account immediately (account pre-hijacking), and Nexus would happily send mail on their
    /// behalf to arbitrary addresses.
    /// </summary>
    [Fact]
    public void SignIn_RequiresAConfirmedAccount()
    {
        Assert.True(Resolve().SignIn.RequireConfirmedAccount);
    }

    // M5: Identity's stock minimum is 6 characters.
    [Fact]
    public void Password_MinimumLengthIsRaisedAboveTheIdentityDefault()
    {
        var password = Resolve().Password;

        Assert.True(password.RequiredLength >= 10, $"RequiredLength was {password.RequiredLength}");
        Assert.True(password.RequireDigit);
        Assert.True(password.RequireLowercase);
        Assert.True(password.RequireUppercase);
        Assert.True(password.RequireNonAlphanumeric);
        Assert.True(password.RequiredUniqueChars >= 4);
    }

    [Fact]
    public void Lockout_IsEnabledForNewUsersWithAMeaningfulWindow()
    {
        var lockout = Resolve().Lockout;

        Assert.True(lockout.AllowedForNewUsers);
        Assert.Equal(5, lockout.MaxFailedAccessAttempts);
        Assert.True(lockout.DefaultLockoutTimeSpan >= TimeSpan.FromMinutes(15));
    }

    /// <summary>
    /// Proves the raised policy actually rejects the passwords the old 6-character default let
    /// through, using the real PasswordValidator rather than re-reading the options.
    /// </summary>
    [Theory]
    [InlineData("Ab1!de", false)]      // 6 chars — valid under the old default, rejected now
    [InlineData("Ab1!defgh", false)]   // 9 chars — still one short
    [InlineData("Ab1!defghi", true)]   // 10 chars, all four character classes
    public async Task PasswordValidator_EnforcesTheNewMinimumLength(string password, bool expectedValid)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(
            "Host=localhost;Database=nexus_test;Username=u;Password=p",
            new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Nexus.Domain.Entities.ApplicationUser>>();

        var validator = new PasswordValidator<Nexus.Domain.Entities.ApplicationUser>();
        var result = await validator.ValidateAsync(userManager, new Nexus.Domain.Entities.ApplicationUser(), password);

        Assert.Equal(expectedValid, result.Succeeded);
    }
}
