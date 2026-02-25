using System.Text.Json.Serialization;

namespace ServiceIntegrationDemo.Core.Contracts;

public sealed class CheckInPayload
{
    [JsonPropertyName("reservationNumber")]
    public string ReservationNumber { get; set; } = default!; // resno

    [JsonPropertyName("site")]
    public string Site { get; set; } = default!;

    [JsonPropertyName("room")]
    public string Room { get; set; } = default!;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("last")]
    public string? Last { get; set; }

    [JsonPropertyName("first")]
    public string? First { get; set; }

    [JsonPropertyName("guestId")]
    public int? GuestId { get; set; }

    [JsonPropertyName("lang")]
    public string? Lang { get; set; } // EA/GE/JP/IT/SP/FR

    [JsonPropertyName("group")]
    public string? Group { get; set; }

    [JsonPropertyName("vip")]
    public string? Vip { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("mobile")]
    public string? Mobile { get; set; }

    [JsonPropertyName("arrival")]
    public string? Arrival { get; set; }

    [JsonPropertyName("departure")]
    public string? Departure { get; set; }

    [JsonPropertyName("tv")]
    public string? Tv { get; set; }

    [JsonPropertyName("minibar")]
    public string? Minibar { get; set; }

    // JSON bạn gửi là "viewbill" (all lowercase)
    [JsonPropertyName("viewbill")]
    public bool? ViewBill { get; set; }

    // JSON bạn gửi là "expressco" (all lowercase)
    [JsonPropertyName("expressco")]
    public bool? ExpressCo { get; set; }
}