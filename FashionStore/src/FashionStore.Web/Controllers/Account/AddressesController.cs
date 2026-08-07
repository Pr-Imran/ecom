using System.Security.Claims;
using FashionStore.Application.DTOs.Account;
using FashionStore.Application.Interfaces;
using FashionStore.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers;

/// <summary>
/// Customer address book. All routes live under /account/addresses and require an
/// authenticated customer; ownership of every address is enforced by the address
/// service using the id resolved from the principal, so one customer can never
/// read or mutate another customer's addresses.
/// </summary>
[Authorize]
[Route("account/addresses")]
public class AddressesController : Controller
{
    private readonly IAddressService _addressService;
    private readonly INavigationService _navigationService;
    private readonly ILogger<AddressesController> _logger;

    public AddressesController(
        IAddressService addressService,
        INavigationService navigationService,
        ILogger<AddressesController> logger)
    {
        _addressService = addressService;
        _navigationService = navigationService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var userId = GetUserIdOrRedirect();
        if (userId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        var viewData = await _addressService.GetAddressBookAsync(userId, cancellationToken);
        await PopulateAccountNavAsync(userId, cancellationToken);

        ViewData["Title"] = "Addresses";
        return View(viewData);
    }

    [HttpGet("new")]
    public async Task<IActionResult> New(CancellationToken cancellationToken = default)
    {
        var userId = GetUserIdOrRedirect();
        if (userId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        var viewData = await _addressService.GetAddressBookAsync(userId, cancellationToken);
        await PopulateAccountNavAsync(userId, cancellationToken);

        ViewData["Title"] = "Add Address";
        return View(new AddressFormModel(viewData.Countries, null, null));
    }

    [HttpGet("edit/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = GetUserIdOrRedirect();
        if (userId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        var address = await _addressService.GetByIdAsync(userId, id, cancellationToken);
        if (address == null)
        {
            return NotFound();
        }

        var viewData = await _addressService.GetAddressBookAsync(userId, cancellationToken);
        await PopulateAccountNavAsync(userId, cancellationToken);

        var request = new SaveAddressRequest(
            address.Label,
            address.RecipientName,
            address.Phone,
            address.AddressLine1,
            address.AddressLine2,
            address.Area,
            address.City,
            address.Region,
            address.PostalCode,
            address.CountryCode,
            address.DeliveryInstructions,
            address.IsDefaultShipping,
            address.IsDefaultBilling);

        ViewData["Title"] = "Edit Address";
        return View("New", new AddressFormModel(viewData.Countries, request, address.Id));
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SaveAddressRequest model, CancellationToken cancellationToken = default)
    {
        var userId = GetUserIdOrRedirect();
        if (userId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        if (!ModelState.IsValid)
        {
            var viewData = await _addressService.GetAddressBookAsync(userId, cancellationToken);
            await PopulateAccountNavAsync(userId, cancellationToken);
            ViewData["Title"] = "Add Address";
            return View("New", new AddressFormModel(viewData.Countries, model, null));
        }

        var result = await _addressService.CreateAsync(userId, model, cancellationToken);

        if (result.Success)
        {
            TempData["SuccessMessage"] = "Address added.";
            return RedirectToAction(nameof(Index));
        }

        ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Unable to save the address.");
        var data = await _addressService.GetAddressBookAsync(userId, cancellationToken);
        await PopulateAccountNavAsync(userId, cancellationToken);
        ViewData["Title"] = "Add Address";
        return View("New", new AddressFormModel(data.Countries, model, null));
    }

    [HttpPost("update/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, SaveAddressRequest model, CancellationToken cancellationToken = default)
    {
        var userId = GetUserIdOrRedirect();
        if (userId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        if (!ModelState.IsValid)
        {
            var viewData = await _addressService.GetAddressBookAsync(userId, cancellationToken);
            await PopulateAccountNavAsync(userId, cancellationToken);
            ViewData["Title"] = "Edit Address";
            return View("New", new AddressFormModel(viewData.Countries, model, id));
        }

        var result = await _addressService.UpdateAsync(userId, id, model, cancellationToken);

        if (result.Success)
        {
            TempData["SuccessMessage"] = "Address updated.";
            return RedirectToAction(nameof(Index));
        }

        ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Unable to save the address.");
        var data = await _addressService.GetAddressBookAsync(userId, cancellationToken);
        await PopulateAccountNavAsync(userId, cancellationToken);
        ViewData["Title"] = "Edit Address";
        return View("New", new AddressFormModel(data.Countries, model, id));
    }

    [HttpPost("delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = GetUserIdOrRedirect();
        if (userId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        var result = await _addressService.DeleteAsync(userId, id, cancellationToken);

        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] =
            result.Success ? "Address removed." : (result.ErrorMessage ?? "Unable to remove the address.");

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("set-default/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefault(Guid id, [FromForm] bool asShipping, [FromForm] bool asBilling, CancellationToken cancellationToken = default)
    {
        var userId = GetUserIdOrRedirect();
        if (userId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        var result = await _addressService.SetDefaultAsync(userId, id, asShipping, asBilling, cancellationToken);

        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] =
            result.Success ? "Default address updated." : (result.ErrorMessage ?? "Unable to update the default address.");

        return RedirectToAction(nameof(Index));
    }

    private string? GetUserIdOrRedirect()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private async Task PopulateAccountNavAsync(string userId, CancellationToken cancellationToken)
    {
        ViewData["AccountNav"] = await _navigationService.GetAccountNavigationAsync(userId, cancellationToken);
    }
}

/// <summary>
/// View model for the address form page: the selectable countries plus the request
/// being edited (null for a new address) and the address id for updates.
/// </summary>
public sealed record AddressFormModel(
    IReadOnlyList<CountryOption> Countries,
    SaveAddressRequest? Request,
    Guid? AddressId);
