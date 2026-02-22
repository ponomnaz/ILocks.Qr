namespace Api.Contracts.Auth;

public sealed record ConfirmOtpRequest(string PhoneNumber, string Code);
