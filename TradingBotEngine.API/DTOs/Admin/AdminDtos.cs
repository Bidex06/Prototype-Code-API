namespace TradingBotEngine.API.DTOs.Admin;

public sealed class AdminStatsResponseDto
{
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int ActiveSubscriptions { get; set; }
    public int ActiveTrials { get; set; }
    public int TotalBots { get; set; }
    public int RunningBots { get; set; }
    public int ConnectedBrokers { get; set; }
}

public sealed class AdminUserResponseDto
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsEmailVerified { get; set; }
    public bool IsAutoTradeEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime? SubscriptionEnd { get; set; }
    public bool IsTrial { get; set; }
    public bool IsSubscriptionActive { get; set; }
    public int BotCount { get; set; }
    public int RunningBotCount { get; set; }
    public int BrokerConnectionCount { get; set; }
    public int ConnectedBrokerCount { get; set; }
}

public sealed class AdminSubscriptionResponseDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Plan { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; }
    public bool IsTrial { get; set; }
    public DateTime? TrialEndDate { get; set; }
    public string? PaymentReference { get; set; }
}

public sealed class AdminUserDetailResponseDto
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsEmailVerified { get; set; }
    public bool IsAutoTradeEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public AdminSubscriptionResponseDto? Subscription { get; set; }
}

public sealed class AdminUserStatusResponseDto
{
    public bool Success { get; set; }
    public int UserId { get; set; }
    public bool IsActive { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class AdminBotResponseDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public int? BrokerConnectionId { get; set; }
    public string? BrokerName { get; set; }
    public bool IsTestnet { get; set; }
    public bool IsLiveTradingEnabled { get; set; }
    public bool UseFutures { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsRunning { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastStartedAt { get; set; }
    public DateTime? LastStoppedAt { get; set; }
    public int TrackedSymbolCount { get; set; }
}

public sealed class AdminBrokerConnectionResponseDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string BrokerName { get; set; } = string.Empty;
    public string? AccountId { get; set; }
    public bool IsActive { get; set; }
    public bool IsConnected { get; set; }
    public bool IsTestnet { get; set; }
    public bool IsLiveTradingEnabled { get; set; }
    public DateTime? LiveTradingOptInAt { get; set; }
    public DateTime? LastConnectedAt { get; set; }
    public DateTime? LastDisconnectedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class AdminAuditLogResponseDto
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string? UserEmail { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }
}
