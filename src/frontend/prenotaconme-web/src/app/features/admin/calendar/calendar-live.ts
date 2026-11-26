import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { environment } from '../../../../environments/environment';
import { AuthService } from '../../../core/auth/auth.service';

/**
 * F2-10: listens on the calendar hub and calls `changed` (debounced) when an appointment of the business changes,
 * and once more after a reconnect, in case something changed meanwhile. Browsers cannot send headers on WebSockets,
 * so the token and the business go in the query string.
 */
export function connectCalendarLive(auth: AuthService, slug: string, changed: () => void): () => void {
  let timer: ReturnType<typeof setTimeout> | undefined;
  const refetch = () => {
    clearTimeout(timer);
    timer = setTimeout(changed, 300);
  };

  const connection: HubConnection = new HubConnectionBuilder()
    .withUrl(`${environment.apiUrl}/api/scheduling/calendar/hub?tenant=${encodeURIComponent(slug)}`, {
      accessTokenFactory: async () => (await auth.idToken()) ?? '',
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();
  connection.on('calendarChanged', refetch);
  connection.onreconnected(refetch);

  let stopped = false;
  connection.start().catch(() => {
    // Live updates are a convenience: the calendar still works (and refetches after every own change) without them.
  });

  return () => {
    if (stopped) return;
    stopped = true;
    clearTimeout(timer);
    void connection.stop();
  };
}
