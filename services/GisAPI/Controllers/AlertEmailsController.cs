using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.AlertEmails.Commands;
using GisAPI.Application.Features.AlertEmails.Queries;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GisAPI.Controllers;

/// <summary>
/// Liste de diffusion des échéances (assurance, taxe de circulation, visite technique,
/// entretien, permis, accident) : les adresses e-mail que les services d'alerte préviennent.
///
/// <para><b>Aucune permission n'était exigée et aucune portée n'était appliquée.</b> Le
/// préfixe « /api/alertemails » ne figure dans aucune des deux tables de
/// <c>PermissionMiddleware</c> : tout compte connecté — y compris un locataire restreint à
/// deux véhicules chez un LOUEUR comme HERTZ — pouvait inscrire l'adresse de son choix et
/// recevoir ensuite, par e-mail, les échéances de TOUT le parc : plaque, dates d'assurance
/// et de visite technique, entretiens dus. Une exfiltration continue, qui survit à la
/// fermeture du compte puisque l'abonnement, lui, reste.</para>
///
/// <para><b>Fermeture :</b> la table <c>alert_emails</c> ne porte AUCUNE colonne véhicule —
/// un abonnement est, par construction, de portée FLOTTE ENTIÈRE. Seul un appelant dont le
/// périmètre EST la flotte entière peut donc en créer, en lire, en modifier ou en
/// supprimer : c'est exactement <see cref="VehicleScope.SeesWholeFleet"/>, l'administrateur
/// de société ou l'administrateur système. Un compte à périmètre partiel ne peut pas
/// s'abonner à ce qu'il n'a pas le droit de voir.</para>
///
/// <para>Borner un abonnement à quelques véhicules demanderait une colonne de plus et une
/// reprise du dispatcher : à trancher par Slim, hors de cette passe (aucune migration ici).</para>
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AlertEmailsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentTenantService _tenantService;

    public AlertEmailsController(IMediator mediator, ICurrentTenantService tenantService)
    {
        _mediator = mediator;
        _tenantService = tenantService;
    }

    /// <summary>
    /// Refus 403 pour un appelant qui ne voit pas tout le parc. Un 404 serait ici un
    /// mensonge inutile : l'écran existe et l'appelant sait qu'il existe, c'est le DROIT
    /// qui lui manque.
    /// </summary>
    private ActionResult? RefusSiPerimetrePartiel() =>
        VehicleScope.SeesWholeFleet(_tenantService)
            ? null
            : new ObjectResult(new
            {
                message = "La liste de diffusion des alertes porte sur toute la flotte : "
                          + "elle est réservée aux administrateurs de la société."
            })
            { StatusCode = 403 };

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? alertType)
    {
        if (RefusSiPerimetrePartiel() is { } refus) return refus;

        var result = await _mediator.Send(new GetAlertEmailsQuery(alertType));
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAlertEmailRequest request)
    {
        if (RefusSiPerimetrePartiel() is { } refus) return refus;

        var id = await _mediator.Send(new CreateAlertEmailCommand(request.Email, request.AlertType));
        return Ok(new { id });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateAlertEmailRequest request)
    {
        if (RefusSiPerimetrePartiel() is { } refus) return refus;

        await _mediator.Send(new UpdateAlertEmailCommand(id, request.Email, request.AlertType));
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (RefusSiPerimetrePartiel() is { } refus) return refus;

        await _mediator.Send(new DeleteAlertEmailCommand(id));
        return NoContent();
    }

    /// <summary>
    /// Sends a one-off test email to the configured recipient so the user
    /// can verify delivery directly from the UI.
    /// </summary>
    [HttpPost("{id}/test")]
    public async Task<IActionResult> SendTest(int id)
    {
        if (RefusSiPerimetrePartiel() is { } refus) return refus;

        await _mediator.Send(new TestAlertEmailCommand(id));
        return Ok(new { sent = true });
    }
}

public record CreateAlertEmailRequest(string Email, string AlertType);
public record UpdateAlertEmailRequest(string Email, string AlertType);
