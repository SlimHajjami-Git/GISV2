using System.Globalization;
using System.Text.Json;
using GisAPI.Application.Features.Payments;

namespace GisAPI.Services;

/// <summary>
/// Appels REST de ClicToPay (manuel « Basic 1.0 ») : register.do et getOrderStatusExtended.do,
/// en POST application/x-www-form-urlencoded. Les identifiants marchand partent dans le corps,
/// jamais dans l'adresse ni dans les journaux.
///
/// <para>La lecture du JSON est tolérante : le manuel montre <c>errorCode</c> tantôt en nombre
/// (register.do : 4), tantôt en texte (getOrderStatusExtended.do : "0"), et <c>currency</c> en
/// texte ("788").</para>
/// </summary>
public class ClicToPayClient : IClicToPayClient
{
    private readonly HttpClient _http;
    private readonly ClicToPayOptions _options;

    public ClicToPayClient(HttpClient http, ClicToPayOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<ClicToPayRegisterResult> RegisterAsync(ClicToPayRegisterRequest request, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["userName"] = _options.UserName,
            ["password"] = _options.Password,
            ["orderNumber"] = request.OrderNumber,
            ["amount"] = request.AmountMinor.ToString(CultureInfo.InvariantCulture),
            ["currency"] = request.CurrencyCode.ToString(CultureInfo.InvariantCulture),
            ["returnUrl"] = request.ReturnUrl,
            ["failUrl"] = request.FailUrl,
            ["description"] = request.Description,
            ["language"] = request.Language,
            ["pageView"] = request.PageView
        };
        using var json = await PostAsync("register.do", form, ct);
        var root = json.RootElement;
        return new ClicToPayRegisterResult(
            Int(root, "errorCode") ?? 0,
            Str(root, "errorMessage"),
            Str(root, "orderId"),
            Str(root, "formUrl"));
    }

    public async Task<ClicToPayOrderStatus> GetOrderStatusAsync(string providerOrderId, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["userName"] = _options.UserName,
            ["password"] = _options.Password,
            ["orderId"] = providerOrderId,
            ["language"] = string.IsNullOrWhiteSpace(_options.Language) ? "fr" : _options.Language
        };
        using var json = await PostAsync("getOrderStatusExtended.do", form, ct);
        return ParseStatus(json.RootElement);
    }

    /// <summary>Réponse de getOrderStatusExtended.do réduite à ce que Calypso vérifie (testée seule).</summary>
    public static ClicToPayOrderStatus ParseStatus(JsonElement root)
    {
        string? pan = null, approval = null;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("cardAuthInfo", out var card) && card.ValueKind == JsonValueKind.Object)
        {
            pan = Str(card, "pan");
            // Le manuel dit approvalCode ; l'environnement de test réel (06/11/2026) renvoie
            // authorizationResponseId à la place. On lit les deux.
            approval = Str(card, "approvalCode") ?? Str(card, "authorizationResponseId");
        }
        return new ClicToPayOrderStatus(
            Int(root, "errorCode") ?? 0,
            Str(root, "errorMessage"),
            Str(root, "orderNumber"),
            Int(root, "orderStatus"),
            Int(root, "actionCode"),
            Str(root, "actionCodeDescription"),
            Long(root, "amount"),
            Int(root, "currency"),
            pan,
            approval);
    }

    private async Task<JsonDocument> PostAsync(string endpoint, Dictionary<string, string> form, CancellationToken ct)
    {
        var url = _options.BaseUrl.TrimEnd('/') + "/" + endpoint;
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(url, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"ClicToPay {endpoint} : HTTP {(int)response.StatusCode}");
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException($"ClicToPay {endpoint} : réponse illisible", ex);
        }
    }

    private static string? Str(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null
            }
            : null;

    private static long? Long(JsonElement obj, string name)
    {
        var raw = Str(obj, name);
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static int? Int(JsonElement obj, string name)
    {
        var raw = Str(obj, name);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
