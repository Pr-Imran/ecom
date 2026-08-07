using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Account;
using FashionStore.Application.DTOs.Images;
using FashionStore.Application.Interfaces;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FashionStore.UnitTests.Services;

public class ProfileServiceTests
{
    private const string UserId = "user-1";

    private static UserManager<ApplicationUser> CreateUserManager(Mock<IUserStore<ApplicationUser>> storeMock)
    {
        return new UserManager<ApplicationUser>(
            storeMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static Mock<IUserStore<ApplicationUser>> CreateStoreMock()
    {
        return new Mock<IUserStore<ApplicationUser>>();
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"profile-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static ProfileService CreateService(
        UserManager<ApplicationUser> userManager,
        AppDbContext context,
        IFileStorageService? storage = null,
        IImageValidationService? imageValidation = null)
    {
        var settings = new FileStorageSettings
        {
            BasePath = "uploads",
            PublicUrlBase = "/uploads"
        };

        return new ProfileService(
            userManager,
            context,
            storage ?? Mock.Of<IFileStorageService>(),
            imageValidation ?? Mock.Of<IImageValidationService>(),
            settings,
            NullLogger<ProfileService>.Instance);
    }

    private static Mock<IUserStore<ApplicationUser>> SetupStoreWithUser(ApplicationUser user)
    {
        var storeMock = CreateStoreMock();
        storeMock.Setup(s => s.FindByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        storeMock.Setup(s => s.UpdateAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityResult.Success);
        return storeMock;
    }

    private static ApplicationUser CreateUser() => new()
    {
        Id = UserId,
        UserName = "jane@example.com",
        Email = "jane@example.com",
        FirstName = "Jane",
        LastName = "Doe",
        DisplayName = "JaneD",
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow
    };

    [Fact]
    public async Task GetProfileAsync_WithExistingUser_ReturnsProfile()
    {
        var user = CreateUser();
        var storeMock = SetupStoreWithUser(user);
        var userManager = CreateUserManager(storeMock);
        var context = CreateContext();

        var service = CreateService(userManager, context);
        var profile = await service.GetProfileAsync(UserId, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal(user.Id, profile!.UserId);
        Assert.Equal("jane@example.com", profile.Email);
        Assert.Equal("JaneD", profile.DisplayName);
        Assert.True(profile.IsActive);
    }

    [Fact]
    public async Task GetProfileAsync_WithMissingUser_ReturnsNull()
    {
        var storeMock = CreateStoreMock();
        storeMock.Setup(s => s.FindByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationUser?)null);
        var userManager = CreateUserManager(storeMock);

        var service = CreateService(userManager, CreateContext());
        var profile = await service.GetProfileAsync(UserId, CancellationToken.None);

        Assert.Null(profile);
    }

    [Fact]
    public async Task UpdateProfileAsync_UpdatesEditableFields()
    {
        var user = CreateUser();
        var storeMock = SetupStoreWithUser(user);
        var userManager = CreateUserManager(storeMock);

        var service = CreateService(userManager, CreateContext());
        var result = await service.UpdateProfileAsync(
            UserId,
            new UpdateProfileRequest("Janet", "Smith", "JanetS", "555-1234", new DateTime(1990, 5, 20)),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Janet", result.Profile!.FirstName);
        Assert.Equal("Smith", result.Profile.LastName);
        Assert.Equal("JanetS", result.Profile.DisplayName);
        Assert.Equal("555-1234", result.Profile.PhoneNumber);
        Assert.Equal(new DateTime(1990, 5, 20), result.Profile.DateOfBirth);
    }

    [Fact]
    public async Task UpdateProfileAsync_FutureDateOfBirth_ReturnsError()
    {
        var user = CreateUser();
        var storeMock = SetupStoreWithUser(user);
        var userManager = CreateUserManager(storeMock);

        var service = CreateService(userManager, CreateContext());
        var result = await service.UpdateProfileAsync(
            UserId,
            new UpdateProfileRequest("Janet", "Smith", null, null, DateTime.UtcNow.AddDays(30)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("future", result.ErrorMessage!);
    }

    [Fact]
    public async Task UpdateProfileAsync_MissingUser_ReturnsError()
    {
        var storeMock = CreateStoreMock();
        storeMock.Setup(s => s.FindByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationUser?)null);
        var userManager = CreateUserManager(storeMock);

        var service = CreateService(userManager, CreateContext());
        var result = await service.UpdateProfileAsync(
            UserId,
            new UpdateProfileRequest(null, null, null, null, null),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage!);
    }

    [Fact]
    public async Task UpdatePreferencesAsync_IgnoresUnknownNotificationCodes()
    {
        var user = CreateUser();
        var storeMock = SetupStoreWithUser(user);
        var userManager = CreateUserManager(storeMock);

        var service = CreateService(userManager, CreateContext());
        var result = await service.UpdatePreferencesAsync(
            UserId,
            new UpdatePreferencesRequest(true, new[] { "order_updates", "offers", "spam_channel", "ORDER_UPDATES" }),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Profile!.MarketingOptIn);
        Assert.Contains("order_updates", result.Profile.NotificationPreferences);
        Assert.Contains("offers", result.Profile.NotificationPreferences);
        Assert.DoesNotContain("spam_channel", result.Profile.NotificationPreferences);
        Assert.Single(result.Profile.NotificationPreferences, c => c == "order_updates");
    }

    [Fact]
    public async Task UpdatePreferencesAsync_WithNoCodes_SetsNull()
    {
        var user = CreateUser();
        var storeMock = SetupStoreWithUser(user);
        var userManager = CreateUserManager(storeMock);

        var service = CreateService(userManager, CreateContext());
        var result = await service.UpdatePreferencesAsync(
            UserId,
            new UpdatePreferencesRequest(false, Array.Empty<string>()),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.Profile!.MarketingOptIn);
        Assert.Empty(result.Profile.NotificationPreferences);
        Assert.Null(user.NotificationPreferences);
    }

    [Fact]
    public async Task RequestDeactivationAsync_RecordsRequestAndReason()
    {
        var user = CreateUser();
        var storeMock = SetupStoreWithUser(user);
        var userManager = CreateUserManager(storeMock);

        var service = CreateService(userManager, CreateContext());
        var result = await service.RequestDeactivationAsync(
            UserId,
            new DeactivationRequest("Too many emails"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Profile!.DeactivationRequestedAtUtc);
        Assert.Equal("Too many emails", result.Profile.DeactivationReason);
        Assert.NotNull(user.DeactivationRequestedAtUtc);
    }

    [Fact]
    public async Task RequestDeactivationAsync_WithNullReason_StillRecordsRequest()
    {
        var user = CreateUser();
        var storeMock = SetupStoreWithUser(user);
        var userManager = CreateUserManager(storeMock);

        var service = CreateService(userManager, CreateContext());
        var result = await service.RequestDeactivationAsync(
            UserId,
            new DeactivationRequest(null),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Profile!.DeactivationRequestedAtUtc);
        Assert.Null(result.Profile.DeactivationReason);
    }

    [Fact]
    public async Task UploadProfileImageAsync_WithNoFile_ReturnsError()
    {
        var storeMock = CreateStoreMock();
        var userManager = CreateUserManager(storeMock);

        var service = CreateService(userManager, CreateContext());
        var result = await service.UploadProfileImageAsync(UserId, null!, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("No image", result.ErrorMessage!);
    }

    [Fact]
    public async Task UploadProfileImageAsync_WithInvalidImage_ReturnsValidationError()
    {
        var user = CreateUser();
        var storeMock = SetupStoreWithUser(user);
        var userManager = CreateUserManager(storeMock);

        var validationMock = new Mock<IImageValidationService>();
        validationMock.Setup(v => v.ValidateAsync(It.IsAny<UploadedFileInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageValidationResult.Invalid("File is not a valid image."));

        var service = CreateService(userManager, CreateContext(), imageValidation: validationMock.Object);
        var result = await service.UploadProfileImageAsync(
            UserId,
            new UploadedFileInput(new MemoryStream(new byte[] { 1, 2, 3 }), "a.png", "image/png", 3),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not a valid image", result.ErrorMessage!);
    }

    [Fact]
    public async Task UploadProfileImageAsync_WithValidImage_StoresAndUpdatesProfile()
    {
        var user = CreateUser();
        var storeMock = SetupStoreWithUser(user);
        var userManager = CreateUserManager(storeMock);

        var storageMock = new Mock<IFileStorageService>();
        storageMock.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredFileResult("profiles/user-1/avatar.png", "/uploads/profiles/user-1/avatar.png", 123));

        var validationMock = new Mock<IImageValidationService>();
        validationMock.Setup(v => v.ValidateAsync(It.IsAny<UploadedFileInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageValidationResult.Valid("png", "image/png", 400, 400));

        var service = CreateService(userManager, CreateContext(), storage: storageMock.Object, imageValidation: validationMock.Object);
        var result = await service.UploadProfileImageAsync(
            UserId,
            new UploadedFileInput(new MemoryStream(new byte[] { 1, 2, 3 }), "avatar.png", "image/png", 3),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("/uploads/profiles/user-1/avatar.png", result.Profile!.ProfileImageUrl);
    }

    [Fact]
    public async Task UploadProfileImageAsync_ReplacesOldImage()
    {
        var user = CreateUser();
        user.ProfileImageUrl = "/uploads/profiles/user-1/old.png";
        var storeMock = SetupStoreWithUser(user);
        var userManager = CreateUserManager(storeMock);

        var deletedPaths = new List<string>();

        var storageMock = new Mock<IFileStorageService>();
        storageMock.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredFileResult("profiles/user-1/avatar.png", "/uploads/profiles/user-1/avatar.png", 123));
        storageMock.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .Callback<string, CancellationToken>((path, _) => deletedPaths.Add(path));

        var validationMock = new Mock<IImageValidationService>();
        validationMock.Setup(v => v.ValidateAsync(It.IsAny<UploadedFileInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageValidationResult.Valid("png", "image/png", 400, 400));

        var service = CreateService(userManager, CreateContext(), storage: storageMock.Object, imageValidation: validationMock.Object);
        var result = await service.UploadProfileImageAsync(
            UserId,
            new UploadedFileInput(new MemoryStream(new byte[] { 1, 2, 3 }), "avatar.png", "image/png", 3),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("/uploads/profiles/user-1/avatar.png", result.Profile!.ProfileImageUrl);
        Assert.Contains("profiles/user-1/old.png", deletedPaths);
    }
}
