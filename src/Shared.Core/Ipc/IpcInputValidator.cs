namespace TheEasyWayForDrivers.Core.Ipc;

public static class IpcInputValidator
{
    public const int MaximumUpdateIds = 128;

    public static string[] ValidateUpdateIds(IEnumerable<string>? updateIds)
    {
        if (updateIds is null)
        {
            return [];
        }

        var values = updateIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (values.Length > MaximumUpdateIds)
        {
            throw new ArgumentException(
                $"A request cannot contain more than {MaximumUpdateIds} update IDs.",
                nameof(updateIds));
        }

        foreach (var value in values)
        {
            if (!Guid.TryParse(value, out _))
            {
                throw new ArgumentException(
                    $"Invalid Windows Update identifier '{value}'.",
                    nameof(updateIds));
            }
        }

        return values;
    }
}
