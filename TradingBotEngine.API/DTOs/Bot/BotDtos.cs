using System.ComponentModel.DataAnnotations;

namespace TradingBotEngine.API.DTOs.Bot;

public sealed class CreateTradingBotRequestDto
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(50, MinimumLength = 2)]
    [RegularExpression("^[A-Za-z0-9 _.-]+$", ErrorMessage = "Strategy contains invalid characters.")]
    public string Strategy { get; set; } = string.Empty;

    [Required, StringLength(20, MinimumLength = 1)]
    [RegularExpression("^(1m|3m|5m|15m|30m|1h|2h|4h|6h|8h|12h|1d|3d|1w)$", ErrorMessage = "Timeframe is invalid.")]
    public string Timeframe { get; set; } = "1h";

    public int? BrokerConnectionId { get; set; }
    public bool UseFutures { get; set; }

    [Required, MinLength(1), MaxLength(50)]
    public List<int> TrackedSymbolIds { get; set; } = new();
}

public sealed class UpdateTradingBotRequestDto
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(50, MinimumLength = 2)]
    [RegularExpression("^[A-Za-z0-9 _.-]+$", ErrorMessage = "Strategy contains invalid characters.")]
    public string Strategy { get; set; } = string.Empty;

    [Required, StringLength(20, MinimumLength = 1)]
    [RegularExpression("^(1m|3m|5m|15m|30m|1h|2h|4h|6h|8h|12h|1d|3d|1w)$", ErrorMessage = "Timeframe is invalid.")]
    public string Timeframe { get; set; } = "1h";

    public int? BrokerConnectionId { get; set; }
    public bool UseFutures { get; set; }

    [Required, MinLength(1), MaxLength(50)]
    public List<int> TrackedSymbolIds { get; set; } = new();
}

public sealed class TradingBotResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public int? BrokerConnectionId { get; set; }
    public string? BrokerName { get; set; }
    public bool UseFutures { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsRunning { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastStartedAt { get; set; }
    public DateTime? LastStoppedAt { get; set; }
    public List<TrackedBotSymbolResponseDto> TrackedSymbols { get; set; } = new();
}

public sealed class TrackedBotSymbolResponseDto
{
    public int Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}
