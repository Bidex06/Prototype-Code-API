using System.ComponentModel.DataAnnotations;

namespace TradingBotEngine.API.DTOs.Trading;

public sealed class TrackedSymbolRequestDto
{
    [Required]
    [StringLength(30, MinimumLength = 1)]
    [RegularExpression("^[A-Za-z0-9._-]+$", ErrorMessage = "Symbol contains invalid characters.")]
    public string Symbol { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(?i:binance|bybit)$", ErrorMessage = "Exchange must be Binance or Bybit.")]
    public string Exchange { get; set; } = "Binance";

    public bool IsEnabled { get; set; } = true;
}

public sealed class TrackedSymbolResponseDto
{
    public int Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
