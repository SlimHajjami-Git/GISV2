using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GisAPI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.Invoices;

/// <summary>
/// Scan de facture : choix du modèle vision chez Groq.
///
/// Incident du 11/09/2026 : Groq avait retiré meta-llama/llama-4-scout début août.
/// Chaque scan d'image répondait 404 « model_not_found », le client lisait « service
/// IA momentanément indisponible », et aucun scan n'a abouti pendant cinq semaines.
/// Ces tests rejouent les réponses réellement observées ce jour-là depuis la production.
/// </summary>
public class GroqVisionFallbackTests
{
    private const string Image = "data:image/jpeg;base64,/9j/4AAQ";
    private const string JsonOk = "{\"amountTTC\": 400.245}";

    /// <summary>Faux Groq : répond par modèle, et garde chaque requête reçue.</summary>
    private sealed class FakeGroq : HttpMessageHandler
    {
        private readonly Func<string, (HttpStatusCode Status, string Body)> _answer;
        public readonly List<JsonElement> Requests = new();
        public FakeGroq(Func<string, (HttpStatusCode, string)> answer) => _answer = answer;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();
            Requests.Add(body);
            var (status, text) = _answer(body.GetProperty("model").GetString()!);
            return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }

        public IEnumerable<string> Models => Requests.Select(r => r.GetProperty("model").GetString()!);
    }

    private static string Completion(string content) => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { role = "assistant", content } } },
        usage = new { prompt_tokens = 2805, completion_tokens = 375, total_tokens = 3180 },
    });

    private const string ModelNotFound = "{\"error\":{\"message\":\"The model `meta-llama/llama-4-scout-17b-16e-instruct` does not exist or you do not have access to it.\",\"code\":\"model_not_found\"}}";

    private static GroqLlmService Service(FakeGroq groq, Dictionary<string, string?>? settings = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Groq:ApiKey"] = "test", ["Groq:Model"] = "openai/gpt-oss-120b" }
                .Concat(settings ?? new()).ToDictionary(kv => kv.Key, kv => kv.Value))
            .Build();
        return new GroqLlmService(config, NullLogger<GroqLlmService>.Instance, groq);
    }

    [Fact]
    public async Task Un_modele_retire_par_Groq_ne_casse_plus_le_scan()
    {
        // Configuration d'avant l'incident : le modèle retiré figure encore dans appsettings.
        var groq = new FakeGroq(model => model.Contains("llama-4-scout")
            ? (HttpStatusCode.NotFound, ModelNotFound)
            : (HttpStatusCode.OK, Completion(JsonOk)));

        var reponse = await Service(groq, new() { ["Groq:VisionModel"] = "meta-llama/llama-4-scout-17b-16e-instruct" })
            .ExtractJsonAsync("prompt", "Analyse cette facture", Image, 2500);

        reponse.Content.Should().Be(JsonOk);
        reponse.TokensUsed.Should().Be(3180);
        groq.Models.Should().Equal("meta-llama/llama-4-scout-17b-16e-instruct", "qwen/qwen3.8-27b");
    }

    [Fact]
    public async Task Par_defaut_le_scan_part_sur_qwen3_8_puis_qwen3_6()
    {
        var groq = new FakeGroq(_ => (HttpStatusCode.OK, Completion(JsonOk)));

        await Service(groq).ExtractJsonAsync("prompt", "Analyse", Image, 2500);

        groq.Models.Should().Equal("qwen/qwen3.8-27b");
        GroqLlmService.DefaultVisionModels.Should().Equal("qwen/qwen3.8-27b", "qwen/qwen3.6-27b");
    }

    [Fact]
    public async Task Le_raisonnement_des_Qwen_est_coupe_et_le_mode_JSON_garde()
    {
        // qwen3.6 avec raisonnement : 400 « Failed to validate JSON » mesuré en production.
        var groq = new FakeGroq(_ => (HttpStatusCode.OK, Completion(JsonOk)));

        await Service(groq).ExtractJsonAsync("prompt", "Analyse", Image, 2500);

        var requete = groq.Requests.Single();
        requete.GetProperty("reasoning_effort").GetString().Should().Be("none");
        requete.GetProperty("response_format").GetProperty("type").GetString().Should().Be("json_object");
        requete.GetProperty("max_tokens").GetInt32().Should().Be(2500);
        var user = requete.GetProperty("messages")[1].GetProperty("content");
        user[1].GetProperty("image_url").GetProperty("url").GetString().Should().Be(Image);
    }

    [Fact]
    public async Task Un_modele_sature_passe_la_main_au_suivant()
    {
        // 429 réellement reçu le 11/09 : plafond de 7 000 jetons d'entrée par minute et par modèle.
        var groq = new FakeGroq(model => model == "qwen/qwen3.8-27b"
            ? (HttpStatusCode.TooManyRequests, "{\"error\":{\"message\":\"Rate limit reached for model `qwen/qwen3.8-27b`\"}}")
            : (HttpStatusCode.OK, Completion(JsonOk)));

        var reponse = await Service(groq).ExtractJsonAsync("prompt", "Analyse", Image, 2500);

        reponse.Content.Should().Be(JsonOk);
        groq.Models.Should().Equal("qwen/qwen3.8-27b", "qwen/qwen3.6-27b");
    }

    [Fact]
    public async Task Une_cle_invalide_n_essaie_pas_tous_les_modeles()
    {
        var groq = new FakeGroq(_ => (HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"Invalid API Key\"}}"));

        var act = () => Service(groq).ExtractJsonAsync("prompt", "Analyse", Image, 2500);

        await act.Should().ThrowAsync<GroqApiException>().WithMessage("*Clé API Groq invalide*");
        groq.Requests.Should().HaveCount(1, "une clé refusée le sera par tous les modèles");
    }

    [Fact]
    public async Task Si_tous_les_modeles_echouent_l_erreur_remonte()
    {
        var groq = new FakeGroq(_ => (HttpStatusCode.NotFound, ModelNotFound));

        var act = () => Service(groq).ExtractJsonAsync("prompt", "Analyse", Image, 2500);

        await act.Should().ThrowAsync<GroqApiException>();
        groq.Models.Should().Equal("qwen/qwen3.8-27b", "qwen/qwen3.6-27b");
    }

    [Fact]
    public async Task Un_PDF_texte_reste_sur_le_modele_texte_sans_repli_vision()
    {
        var groq = new FakeGroq(_ => (HttpStatusCode.OK, Completion(JsonOk)));

        await Service(groq).ExtractJsonAsync("prompt", "Voici le texte extrait d'une facture", null, 2500);

        var requete = groq.Requests.Single();
        requete.GetProperty("model").GetString().Should().Be("openai/gpt-oss-120b");
        requete.TryGetProperty("reasoning_effort", out _).Should().BeFalse("gpt-oss n'est pas un Qwen");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.Gone, true)]
    [InlineData(HttpStatusCode.BadRequest, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public void Quels_echecs_meritent_d_essayer_le_modele_suivant(HttpStatusCode status, bool suivant)
        => GroqLlmService.IsWorthNextModel(status).Should().Be(suivant);
}
