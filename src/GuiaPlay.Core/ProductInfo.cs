using System.Reflection;

namespace GuiaPlay.Core;

public static class ProductInfo
{
    private static readonly Assembly Assembly = typeof(ProductInfo).Assembly;

    public static string Version { get; } =
        Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    public static DateOnly ReleaseDate { get; } = DateOnly.ParseExact(
        Metadata("ReleaseDate") ?? throw new InvalidOperationException("ReleaseDate ausente nos metadados do produto."),
        "yyyy-MM-dd",
        System.Globalization.CultureInfo.InvariantCulture);

    public static UpdateChannel Channel { get; } = Enum.Parse<UpdateChannel>(Metadata("UpdateChannel") ?? "Prototype");
    public static string RepositoryOwner { get; } = Metadata("RepositoryOwner") ?? "guiasysstudio";
    public static string RepositoryName { get; } = Metadata("RepositoryName") ?? "GuiaPlay";
    public static Uri ProjectPageUri { get; } = new($"https://github.com/{RepositoryOwner}/{RepositoryName}");

    private static string? Metadata(string key) => Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))?.Value;
}
