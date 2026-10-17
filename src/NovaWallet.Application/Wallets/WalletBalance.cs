namespace NovaWallet.Application.Wallets;

public sealed record WalletBalance(Guid WalletId, long BalanceKobo, string Currency, DateTimeOffset AsOf);
