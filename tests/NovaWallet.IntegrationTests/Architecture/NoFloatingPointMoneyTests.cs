using System.Reflection;

namespace NovaWallet.IntegrationTests.Architecture;

/// <summary>Amounts are integer kobo end to end; no floating-point or decimal type may appear in our types.</summary>
public class NoFloatingPointMoneyTests
{
    private static readonly Type[] Forbidden = [typeof(float), typeof(double), typeof(decimal)];

    public static TheoryData<string> Assemblies => new()
    {
        typeof(NovaWallet.Domain.AssemblyMarker).Assembly.GetName().Name!,
        typeof(NovaWallet.Application.AssemblyMarker).Assembly.GetName().Name!,
        typeof(Program).Assembly.GetName().Name!,
        typeof(NovaWallet.Infrastructure.Persistence.LedgerDbContext).Assembly.GetName().Name!,
    };

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void No_member_of_our_types_is_floating_point_or_decimal(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);
        var offenders = assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("NovaWallet", StringComparison.Ordinal) == true && !IsGenerated(t))
            .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => MemberType(m) is { } type && Forbidden.Contains(Nullable.GetUnderlyingType(type) ?? type))
                .Select(m => $"{t.FullName}.{m.Name}"))
            .ToList();

        Assert.Empty(offenders);
    }

    private static Type? MemberType(MemberInfo member) => member switch
    {
        PropertyInfo p => p.PropertyType,
        FieldInfo f => f.FieldType,
        MethodInfo m when !m.IsSpecialName => m.ReturnType,
        _ => null,
    };

    private static bool IsGenerated(Type type) =>
        type.Namespace!.Contains(".Migrations", StringComparison.Ordinal)
        || type.Name.Contains('<', StringComparison.Ordinal);
}
