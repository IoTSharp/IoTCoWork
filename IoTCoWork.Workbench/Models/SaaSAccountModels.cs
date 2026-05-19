using System.Text.Json.Serialization;

namespace IoTCoWork.Workbench.Models;

public sealed class PlatformBearerTokenResponse
{
    [JsonPropertyName("tokenType")] public string TokenType { get; set; } = "Bearer";
    [JsonPropertyName("accessToken")] public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("expiresIn")] public int ExpiresIn { get; set; }
    [JsonPropertyName("refreshToken")] public string RefreshToken { get; set; } = string.Empty;
}

public sealed class PlatformRegisterResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
}

public sealed class SaaSAuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string TokenType { get; set; } = "Bearer";
    public SaaSAccountProfile? User { get; set; }
}

public sealed class SaaSAccountProfile
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("avatarUrl")] public string? AvatarUrl { get; set; }
    [JsonPropertyName("phoneNumber")] public string? PhoneNumber { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("balanceCredits")] public long BalanceCredits { get; set; }
    [JsonPropertyName("accountService")] public SaaSAccountServiceStatus? AccountService { get; set; }

    public decimal Balance => BalanceCredits / 100m;
}

public sealed class SaaSAccountServiceStatus
{
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("detail")] public string? Detail { get; set; }
    [JsonPropertyName("balanceCredits")] public long? BalanceCredits { get; set; }
    [JsonPropertyName("lastSyncedAt")] public DateTimeOffset? LastSyncedAt { get; set; }
    [JsonPropertyName("accountName")] public string? AccountName { get; set; }
}

public sealed class SaaSRechargeOrderCreateRequest
{
    [JsonPropertyName("amountFen")] public long AmountFen { get; set; }
    [JsonPropertyName("tradeType")] public string TradeType { get; set; } = "native";
}

public sealed class SaaSRechargeOrder
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("orderNo")] public string OrderNo { get; set; } = string.Empty;
    [JsonPropertyName("amountFen")] public long AmountFen { get; set; }
    [JsonPropertyName("credits")] public long Credits { get; set; }
    [JsonPropertyName("channel")] public string Channel { get; set; } = string.Empty;
    [JsonPropertyName("tradeType")] public string TradeType { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("codeUrl")] public string? CodeUrl { get; set; }
    [JsonPropertyName("paidAt")] public DateTimeOffset? PaidAt { get; set; }
    [JsonPropertyName("closedAt")] public DateTimeOffset? ClosedAt { get; set; }
    [JsonPropertyName("expiresAt")] public DateTimeOffset ExpiresAt { get; set; }
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; set; }

    public decimal Amount => AmountFen / 100m;
    public decimal PayAmount => Amount;
    public string? QrCode => CodeUrl;
    public string? PayUrl => CodeUrl;
}

public sealed class SaaSDeviceCodeCreateRequest
{
    [JsonPropertyName("clientName")] public string ClientName { get; set; } = "IoTCoWork";
    [JsonPropertyName("clientVersion")] public string? ClientVersion { get; set; }
    [JsonPropertyName("deviceName")] public string? DeviceName { get; set; }
    [JsonPropertyName("deviceLocalId")] public string? DeviceLocalId { get; set; }
    [JsonPropertyName("scopes")] public IReadOnlyList<string> Scopes { get; set; } = ["platform.cloud", "ai.invoke"];
}

public sealed class SaaSDeviceCodeResponse
{
    [JsonPropertyName("deviceCode")] public string DeviceCode { get; set; } = string.Empty;
    [JsonPropertyName("userCode")] public string UserCode { get; set; } = string.Empty;
    [JsonPropertyName("verificationUri")] public string VerificationUri { get; set; } = string.Empty;
    [JsonPropertyName("verificationUriComplete")] public string VerificationUriComplete { get; set; } = string.Empty;
    [JsonPropertyName("expiresIn")] public int ExpiresIn { get; set; }
    [JsonPropertyName("interval")] public int Interval { get; set; }
}

public sealed class SaaSDeviceCodeApproveRequest
{
    [JsonPropertyName("bindingDays")] public int BindingDays { get; set; } = 3650;
}

public sealed class SaaSDeviceTokenRequest
{
    [JsonPropertyName("deviceCode")] public string DeviceCode { get; set; } = string.Empty;
}

public sealed class SaaSDeviceTokenResponse
{
    [JsonPropertyName("accessToken")] public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("refreshToken")] public string RefreshToken { get; set; } = string.Empty;
    [JsonPropertyName("tokenType")] public string TokenType { get; set; } = "Bearer";
    [JsonPropertyName("expiresIn")] public int ExpiresIn { get; set; }
    [JsonPropertyName("scope")] public string Scope { get; set; } = string.Empty;
}
