using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.ServiceApp.Services;

/// <summary>
/// Read-only COM metadata isolation for WUA service discovery and Type=Software.
/// A malformed unrelated registered service must not hide Microsoft Update,
/// while unknown services must never be called "not registered".
/// The software title is optional, unlike UpdateID and collection structure.
/// </summary>
public static class WuaSourceMetadataReader
{
    public const int MaximumRegisteredServices = 128;

    public sealed record SoftwareTitle(string? Text, bool Unreadable);

    public static bool IsMicrosoftUpdateRegistered(
        object registeredServices, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registeredServices);
        cancellationToken.ThrowIfCancellationRequested();

        dynamic collection = registeredServices;
        // Collection count is mandatory structural evidence. An unreadable
        // Count or an oversized inventory fails source registration discovery.
        int count = (int)collection.Count;
        if (count < 0 || count > MaximumRegisteredServices)
            throw new InvalidDataException(
                "Numero di servizi WUA non verificabile o oltre limite.");

        var unknown = false;
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? candidateId;
            try
            {
                // BOTH COM getters are within the catch boundary. Continue
                // even if a non-MU provider's service metadata is corrupt.
                dynamic service = collection.Item(i);
                candidateId = service.ServiceID as string;
            }
            catch (Exception error) when (
                error is not OperationCanceledException &&
                error is not OutOfMemoryException)
            {
                unknown = true;
                continue;
            }

            if (!Guid.TryParse(candidateId, out var parsed))
            {
                unknown = true;
                continue;
            }

            if (parsed == Guid.Parse(WindowsDriverSearchPlan.MicrosoftUpdateServiceId))
                return true;
        }

        if (unknown)
            throw new InvalidDataException(
                "Inventario servizi Windows Update incompleto: " +
                "disponibilità Microsoft Update non verificata.");

        return false;
    }

    public static SoftwareTitle ReadOptionalSoftwareTitle(object update)
    {
        ArgumentNullException.ThrowIfNull(update);
        try
        {
            // Passing update.Title into SafeString(value) DOES NOT protect
            // the COM getter: read it inside this try/catch instead.
            object? value = ((dynamic)update).Title;
            if (value is string title)
                return new SoftwareTitle(title, false);
            return new SoftwareTitle(null, true);
        }
        catch (Exception error) when (
            error is not OperationCanceledException &&
            error is not OutOfMemoryException)
        {
            return new SoftwareTitle(null, true);
        }
    }
}
