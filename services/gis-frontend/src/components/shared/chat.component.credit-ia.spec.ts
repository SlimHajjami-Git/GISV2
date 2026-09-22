import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ChatComponent } from './chat.component';
import { ApiService } from '../../services/api.service';
import { SignalRService } from '../../services/signalr.service';

/**
 * Assistant IA (onglet « IA » de la bulle de discussion) et crédit IA du mois (22/09/2026,
 * « le quota inclut l'utilisation de l'IA ») : la barre « Crédit IA » s'affiche dans
 * l'assistant, suit chaque réponse, grise l'envoi à 100 % avec un message clair, et un
 * refus 403/429 AI_CREDIT_* affiche le message du serveur.
 */
describe('ChatComponent — crédit IA de l’assistant', () => {
  const credit = (patch: Record<string, unknown> = {}) => ({
    enabled: true, budgetTokens: 60000, usedTokens: 25200, remainingTokens: 34800,
    percentUsed: 42, scansThisMonth: 3, estimatedScansLeft: 11, resetsAt: '2026-10-01T00:00:00Z',
    byFeature: { invoice_scan: 9000, assistant_chat: 16200 },
    ...patch
  });
  const epuise = () => credit({ usedTokens: 61200, remainingTokens: 0, percentUsed: 100, estimatedScansLeft: 0 });
  const vehicule = { id: 1, name: 'Service 01', plate: 'GA-214-RK', brand: 'Renault', model: 'Kangoo', type: 'van', mileage: 84000, status: 'available' };
  const MESSAGE_429 = "Crédit IA du mois épuisé (100 %). Il se recharge le 01/10/2026 ; votre administrateur peut l'augmenter.";

  let api: Record<string, jest.Mock>;
  let fixture: any;
  let horloge: jest.SpyInstance<number, []>;

  function ouvrirAssistant(creditInitial: unknown = credit()) {
    api = {
      getChatUsers: jest.fn(() => of([])),
      getChatUnreadCount: jest.fn(() => of({ count: 0 })),
      getAiChatVehicles: jest.fn(() => of([vehicule])),
      getAllHealthScores: jest.fn(() => of([])),
      getAiChatHistory: jest.fn(() => of([])),
      getAiCredit: jest.fn(() => of(creditInitial)),
      sendAiChatMessage: jest.fn(() => of({ message: 'Diagnostic : RAS.', tokensUsed: 2400, messageId: 9, credit: credit({ usedTokens: 28200, remainingTokens: 31800, percentUsed: 47 }) })),
      generateAiReport: jest.fn(() => of({ report: 'Rapport', tokensUsed: 3100, credit: credit({ usedTokens: 28300, remainingTokens: 31700, percentUsed: 47 }) }))
    };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [ChatComponent],
      providers: [
        { provide: ApiService, useValue: api },
        { provide: SignalRService, useValue: { isConnected: () => false, connectionState$: of('Disconnected') } }
      ]
    });
    fixture = TestBed.createComponent(ChatComponent);
    fixture.detectChanges();
    const c = fixture.componentInstance as ChatComponent;
    c.isOpen = true;
    c.switchToAi();
    c.openAiChat(vehicule as any);
    fixture.detectChanges();
    return c;
  }

  const el = (): HTMLElement => fixture.nativeElement;
  const barre = () => el().querySelector('.chat-conversation app-credit-ia-bar [role="progressbar"]') as HTMLElement | null;
  const envoyer = () => el().querySelector('.send-btn.ai-send') as HTMLButtonElement;
  const saisie = () => el().querySelector('.chat-conversation .message-input input') as HTMLInputElement;
  const rapport = () => el().querySelector('.report-btn') as HTMLButtonElement;

  beforeEach(() => { horloge = jest.spyOn(Date, 'now').mockReturnValue(Date.parse('2026-09-22T10:00:00Z')); });
  afterEach(() => { fixture?.destroy(); horloge.mockRestore(); });

  it('la barre « Crédit IA » du mois s’affiche dans l’assistant, lue sur /api/ai-credit', () => {
    ouvrirAssistant();

    expect(api['getAiCredit']).toHaveBeenCalled();
    expect(barre()!.getAttribute('aria-valuenow')).toBe('42');
    expect((el().querySelector('.chat-conversation .credit-ia') as HTMLElement).title)
      .toBe('42 % du crédit IA gratuit du mois utilisé (25 200 / 60 000 jetons). Se recharge le 1er octobre.');
  });

  it('crédit épuisé : envoi, saisie, suggestions et rapport grisés, avec le motif et la date de recharge', async () => {
    const c = ouvrirAssistant(epuise());
    c.aiNewMessage = 'Diagnostic ?';
    fixture.detectChanges();
    // ngModel applique [disabled] au champ de saisie dans une micro-tâche (whenStable
    // attendrait aussi la minuterie de 30 s de la liste des utilisateurs).
    await Promise.resolve();

    expect(envoyer().disabled).toBe(true);
    expect(saisie().disabled).toBe(true);
    expect(rapport().disabled).toBe(true);
    expect(Array.from(el().querySelectorAll('.ai-suggestion')).every(b => (b as HTMLButtonElement).disabled)).toBe(true);
    expect(el().querySelector('.ai-credit-blocked')!.textContent!.trim())
      .toBe("Crédit IA du mois épuisé (100 %) — il se recharge le 1er octobre. Votre administrateur peut l'augmenter.");

    c.sendAiMessage();
    expect(api['sendAiChatMessage']).not.toHaveBeenCalled();
  });

  it('IA coupée pour la société : envoi grisé, « IA désactivée »', () => {
    ouvrirAssistant(credit({ enabled: false, budgetTokens: 0, usedTokens: 0, remainingTokens: 0, percentUsed: 100 }));

    expect(envoyer().disabled).toBe(true);
    expect(el().querySelector('.ai-credit-blocked')!.textContent!.trim())
      .toBe("Les fonctions d'IA ne sont pas activées pour votre société.");
    expect(el().querySelector('.chat-conversation .credit-ia-off')!.textContent!.trim()).toBe('IA désactivée');
  });

  it('chaque réponse rafraîchit la barre avec le crédit joint par le serveur', () => {
    const c = ouvrirAssistant();
    c.aiNewMessage = 'Diagnostic ?';
    c.sendAiMessage();
    fixture.detectChanges();

    expect(barre()!.getAttribute('aria-valuenow')).toBe('47');
    expect(c.aiMessages.map(m => m.role)).toEqual(['user', 'assistant']);

    c.generateReport();
    fixture.detectChanges();
    expect(c.aiCredit!.usedTokens).toBe(28300);
  });

  it('refus 429 AI_CREDIT_EXHAUSTED : message du serveur, barre à 100 %, envoi grisé, question rendue au champ', () => {
    const c = ouvrirAssistant();
    api['sendAiChatMessage'].mockReturnValue(throwError(() => ({
      status: 429, error: { code: 'AI_CREDIT_EXHAUSTED', message: MESSAGE_429, credit: epuise() }
    })));
    c.aiNewMessage = 'Quels entretiens prévoir ?';
    c.sendAiMessage();
    fixture.detectChanges();

    expect(el().querySelector('.ai-error')!.textContent!.trim()).toBe(MESSAGE_429);
    expect(barre()!.getAttribute('aria-valuenow')).toBe('100');
    expect(envoyer().disabled).toBe(true);
    expect(c.aiMessages).toHaveLength(0);          // le serveur n'a pas enregistré la question
    expect(c.aiNewMessage).toBe('Quels entretiens prévoir ?');
  });

  it('refus 403 AI_CREDIT_DISABLED sur le rapport : message du serveur affiché', () => {
    const c = ouvrirAssistant();
    const message = "Les fonctions d'IA ne sont pas activées pour votre société.";
    api['generateAiReport'].mockReturnValue(throwError(() => ({
      status: 403, error: { code: 'AI_CREDIT_DISABLED', message, credit: credit({ enabled: false, budgetTokens: 0, usedTokens: 0, remainingTokens: 0, percentUsed: 100 }) }
    })));
    c.generateReport();
    fixture.detectChanges();

    expect(el().querySelector('.ai-error')!.textContent!.trim()).toBe(message);
    expect(rapport().disabled).toBe(true);
    expect(c.aiMessages).toHaveLength(0);
  });

  it('autre panne de l’IA : message habituel, la barre ne bouge pas', () => {
    const c = ouvrirAssistant();
    api['sendAiChatMessage'].mockReturnValue(throwError(() => ({ status: 503, error: { message: 'Service IA indisponible' } })));
    c.aiNewMessage = 'Diagnostic ?';
    c.sendAiMessage();
    fixture.detectChanges();

    expect(el().querySelector('.ai-error')!.textContent!.trim()).toBe('Service IA indisponible');
    expect(barre()!.getAttribute('aria-valuenow')).toBe('42');
    expect(envoyer().disabled).toBe(true);        // champ vidé : rien à envoyer, pas un blocage de crédit
    expect(el().querySelector('.ai-credit-blocked')).toBeNull();
  });
});
