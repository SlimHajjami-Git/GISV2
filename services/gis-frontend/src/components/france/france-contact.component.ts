import { Component } from '@angular/core';

/**
 * Page 5 — Contact.
 *
 * <p><b>Ce que cette page n'invente pas.</b> La maquette porte une adresse
 * (« 123 Avenue des Champs-Élysées »), un téléphone et un courriel qui sont
 * des remplissages de maquettiste. Publier des coordonnées inventées sur un
 * site commercial, c'est afficher un faux renseignement d'entreprise : les
 * champs restent donc explicitement à compléter tant que les vraies valeurs
 * n'ont pas été fournies.</p>
 *
 * <p>Le formulaire n'est pas non plus relié : aucun point d'entrée de contact
 * n'existe côté API. Un formulaire qui affiche « message envoyé » sans rien
 * envoyer est pire que pas de formulaire du tout — il fait perdre des
 * demandes clients en silence. L'envoi annonce donc franchement son état.</p>
 */
@Component({
  selector: 'app-france-contact',
  standalone: true,
  template: `
    <section class="band-glow tight">
      <div class="shell">
        <div class="sec-head rise">
          <h2>Contactez-nous</h2>
          <p>Une question, un besoin spécifique ? Notre équipe est là pour vous aider.</p>
        </div>

        <div class="contact-grid rise">
          <div class="contact-info">
            <div class="legal-warn">
              <strong>Coordonnées à compléter.</strong> Adresse, téléphone et
              courriel de la structure française doivent être fournis avant la
              mise en ligne publique de cette page. Ils ne sont pas inventés ici.
            </div>
            <div class="ci">
              <div class="ic"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2" aria-hidden="true"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/></svg></div>
              <div><h4>Courriel</h4><p>à communiquer</p></div>
            </div>
            <div class="ci">
              <div class="ic"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2" aria-hidden="true"><path d="M5 4h4l2 5-2.5 1.5a11 11 0 0 0 5 5L15 13l5 2v4a1 1 0 0 1-1 1A16 16 0 0 1 4 5a1 1 0 0 1 1-1z"/></svg></div>
              <div><h4>Téléphone</h4><p>à communiquer</p></div>
            </div>
            <div class="ci">
              <div class="ic"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2" aria-hidden="true"><path d="M12 21s7-6.3 7-11a7 7 0 1 0-14 0c0 4.7 7 11 7 11z"/><circle cx="12" cy="10" r="2.5"/></svg></div>
              <div><h4>Adresse</h4><p>à communiquer</p></div>
            </div>
            <div class="ci">
              <div class="ic"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg></div>
              <div><h4>Horaires</h4><p>à communiquer</p></div>
            </div>
          </div>

          <form class="contact-form" (ngSubmit)="submit($event)">
            <label for="c-nom">Nom</label>
            <input id="c-nom" name="nom" type="text" placeholder="Votre nom" autocomplete="name">

            <label for="c-mail">Courriel</label>
            <input id="c-mail" name="email" type="email" placeholder="votre@email.com" autocomplete="email">

            <label for="c-sujet">Sujet</label>
            <input id="c-sujet" name="sujet" type="text" placeholder="Sujet de votre message">

            <label for="c-msg">Message</label>
            <textarea id="c-msg" name="message" rows="6" placeholder="Votre message…"></textarea>

            <button class="btn btn-grad" type="submit" style="width:100%">Envoyer le message</button>

            @if (attempted) {
              <p class="legal-warn" style="margin-top:18px" role="status">
                L'envoi n'est pas encore raccordé : aucune boîte de réception n'a
                été définie pour ce site. Votre message n'a donc pas été transmis.
              </p>
            }
          </form>
        </div>
      </div>
    </section>
  `
})
export class FranceContactComponent {
  attempted = false;

  submit(event: Event): void {
    event.preventDefault();
    // Tant qu'aucune destination n'existe, on le dit plutôt que de simuler
    // un envoi réussi.
    this.attempted = true;
  }
}
