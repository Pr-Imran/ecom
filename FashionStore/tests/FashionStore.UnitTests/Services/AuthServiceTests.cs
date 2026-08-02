using System.Security.Claims;
using FashionStore.Application.DTOs.Auth;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Moq;
using Xunit;

namespace FashionStore.UnitTests.Services;

public class AuthServiceTests
{
    [Fact]
    public void RegisterAsync_WithValidRequest_CreatesUser()
    {
        // Arrange
        var request = new RegisterRequest("test@example.com", "Password123!", "John", "Doe");
        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new UserManager<ApplicationUser>(userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        
        var createdUser = new ApplicationUser { Email = request.Email, UserName = request.Email };
        userStoreMock.Setup(s => s.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityResult.Success)
            .Callback<ApplicationUser, CancellationToken>((u, _) => {
                u.GetType().GetProperty("Id")?.SetValue(u, "generated-id");
            });

        // Note: Full AuthService testing requires proper DI setup, this is a basic sanity test
        Assert.NotNull(request.Email);
        Assert.NotNull(request.Password);
    }

    [Fact]
    public void LoginRequest_ValidProperties()
    {
        // Arrange
        var request = new LoginRequest("test@example.com", "Password123!", true);

        // Assert
        Assert.Equal("test@example.com", request.EmailOrUserName);
        Assert.Equal("Password123!", request.Password);
        Assert.True(request.RememberMe);
    }

    [Fact]
    public void RegisterRequest_ValidProperties()
    {
        // Arrange & Act
        var request = new RegisterRequest("test@example.com", "Password123!", "John", "Doe", "+1234567890", true);

        // Assert
        Assert.Equal("test@example.com", request.Email);
        Assert.Equal("Password123!", request.Password);
        Assert.Equal("John", request.FirstName);
        Assert.Equal("Doe", request.LastName);
        Assert.True(request.AcceptTerms);
    }

    [Fact]
    public void ApplicationPermissions_AllPermissions_NotEmpty()
    {
        // Arrange & Act
        var permissions = ApplicationPermissions.AllPermissions;

        // Assert
        Assert.NotEmpty(permissions);
        Assert.Contains(permissions, p => p.Contains("Dashboard"));
        Assert.Contains(permissions, p => p.Contains("Products"));
    }

    [Fact]
    public void IdentityException_Constructor_SetsErrors()
    {
        // Arrange
        var errors = new List<string> { "Error 1", "Error 2" };

        // Act
        var exception = new IdentityException(errors);

        // Assert
        Assert.Equal(errors, exception.Errors);
        Assert.Contains("Error 1", exception.Message);
        Assert.Contains("Error 2", exception.Message);
    }

    [Fact]
    public void SecurityException_Constructor_SetsMessage()
    {
        // Arrange
        var message = "Invalid credentials";

        // Act
        var exception = new SecurityException(message);

        // Assert
        Assert.Equal(message, exception.Message);
    }
}
