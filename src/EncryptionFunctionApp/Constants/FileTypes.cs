namespace EncryptionFunctionApp.Constants;

public static class FileTypes
{
    public const string Members = "members";
    public const string Addresses = "addresses";

    // To add a new file type:
    //   1. Add a constant here and include it in All (2 lines)
    //   2. Add the type to fileTypes array in infrastructure/bicep/main.bicepparam
    //   Bicep will create the encrypted-{type} blob container automatically.
    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Members,
            Addresses,
        };
}
