import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * F5-04: privacy notice (GDPR art. 13). Italian first: the first businesses and their clients are in Italy.
 * The bracketed parts must be filled in (and the whole text reviewed by a lawyer) before going live.
 */
@Component({
  selector: 'app-privacy-page',
  imports: [RouterLink, TranslocoPipe],
  template: `
    <article class="privacy">
      <h1>Informativa sulla privacy</h1>
      <p class="muted">{{ 'privacy.languageNote' | transloco }}</p>
      <p class="muted">Ultimo aggiornamento: [data]</p>

      <h2>1. Chi tratta i tuoi dati</h2>
      <p>
        Quando prenoti presso un'attività (barbiere, parrucchiere, centro estetico), il <strong>titolare</strong> dei dati
        della prenotazione è quell'attività. PrenotaConMe, servizio di [ragione sociale], RUC [numero], [indirizzo], Perù,
        tratta questi dati per suo conto come <strong>responsabile del trattamento</strong>, ed è titolare dei dati del tuo
        account. Contatto privacy: [email]. Rappresentante nell'UE (art. 27 GDPR): [nome e indirizzo].
      </p>

      <h2>2. Quali dati</h2>
      <ul>
        <li>Account: nome, email e foto del tuo account Google o Microsoft; il numero di telefono che inserisci.</li>
        <li>Prenotazioni: servizio, data, ora, operatore, prezzo e stato (confermata, completata, annullata).</li>
        <li>Messaggi: le conversazioni con l'assistente dell'attività su Telegram o WhatsApp, comprese le note vocali trascritte.</li>
        <li>Notifiche: i dispositivi su cui hai attivato le notifiche e le tue preferenze sui promemoria.</li>
      </ul>

      <h2>3. Perché e su quale base</h2>
      <ul>
        <li>Gestire prenotazioni, modifiche, cancellazioni e promemoria: esecuzione del servizio che richiedi (art. 6.1.b GDPR).</li>
        <li>Rispondere tramite l'assistente automatico e, se lo chiedi, passarti a una persona: esecuzione del servizio.</li>
        <li>Avvisi dell'attività (per esempio chiusure o orari speciali): legittimo interesse dell'attività; puoi chiedere di non riceverli.</li>
        <li>Sicurezza e prevenzione degli abusi (limiti di utilizzo, registri tecnici): legittimo interesse.</li>
      </ul>

      <h2>4. A chi li comunichiamo</h2>
      <p>
        All'attività presso cui prenoti e ai fornitori che ci aiutano a erogare il servizio: Google (accesso e Google Calendar,
        se l'attività lo usa), Microsoft (accesso), OpenAI (assistente e trascrizione delle note vocali), Telegram e Meta/WhatsApp
        (messaggi), il fornitore della posta elettronica [nome] e il fornitore del server [nome].
      </p>

      <h2>5. Trasferimenti fuori dall'UE</h2>
      <p>
        I dati sono conservati su server in Perù e alcuni fornitori si trovano negli Stati Uniti. Questi trasferimenti avvengono
        con le clausole contrattuali tipo della Commissione europea o, per i fornitori certificati, con il Data Privacy Framework
        UE-USA. Puoi chiederne copia a [email].
      </p>

      <h2>6. Per quanto tempo</h2>
      <ul>
        <li>Conversazioni con l'assistente: 30 giorni dall'ultimo messaggio.</li>
        <li>Prenotazioni: finché l'attività ne ha bisogno per la sua gestione e per obblighi di legge; se elimini l'account restano senza il tuo nome.</li>
        <li>Account: finché non lo elimini.</li>
      </ul>

      <h2>7. I tuoi diritti</h2>
      <p>
        Puoi accedere ai tuoi dati, scaricarli, correggerli, chiederne la cancellazione o la limitazione e opporti al trattamento.
        Dalla pagina <em>Le mie prenotazioni</em> di un'attività puoi scaricare tutti i tuoi dati ed eliminare l'account; per i dati
        tenuti da una sola attività puoi rivolgerti direttamente a lei. Per qualsiasi richiesta: [email]. Hai diritto di presentare
        reclamo al Garante per la protezione dei dati personali (garanteprivacy.it).
      </p>

      <p><a routerLink="/">PrenotaConMe</a></p>
    </article>
  `,
  styles: `
    .privacy { max-width: 720px; margin: 0 auto var(--space-7); }
    h2 { margin: var(--space-5) 0 var(--space-2); font: var(--mat-sys-title-medium); font-weight: 600; }
    p, li { line-height: 1.6; }
  `,
})
export class PrivacyPage {}
