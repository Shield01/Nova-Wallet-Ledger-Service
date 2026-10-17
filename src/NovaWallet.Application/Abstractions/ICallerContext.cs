namespace NovaWallet.Application.Abstractions;

/// <summary>Who is making the current request, as established by authentication.</summary>
public interface ICallerContext
{
    /// <summary>The verified token subject (<c>sub</c>). For customers this is the customer id.</summary>
    string Subject { get; }
}
