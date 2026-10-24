import { HttpClient } from '@angular/common/http';
import { DOCUMENT } from '@angular/common';
import { Injectable, InjectionToken, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// MCP-01: the OAuth authorization request as the app sent it in Connect's address (OAuth's names).
export interface AuthorizeRequest {
  responseType: string | null;
  clientId: string | null;
  redirectUri: string | null;
  codeChallenge: string | null;
  codeChallengeMethod: string | null;
  state: string | null;
  scope: string | null;
  resource: string | null;
}

// What Connect shows before Allow: redirectHost is "localhost:33418" or "claude.ai".
export interface CheckResult {
  clientName: string;
  redirectHost: string;
  scope: string;
}

@Injectable({ providedIn: 'root' })
export class ConnectApi {
  private readonly http = inject(HttpClient);

  // 400 with a ProblemDetails `detail` when the request fails a check (MCP-02).
  check(request: AuthorizeRequest): Promise<CheckResult> {
    return firstValueFrom(this.http.post<CheckResult>('/api/oauth/check', request));
  }

  // Allow or Cancel; the answer is where to send the browser (the app's callback).
  authorize(request: AuthorizeRequest, allow: boolean): Promise<{ redirectTo: string }> {
    return firstValueFrom(this.http.post<{ redirectTo: string }>('/api/oauth/authorize', { ...request, allow }));
  }
}

// Leaving NAMA for the app's callback. A token, so tests can see where it goes.
export const LEAVE_TO = new InjectionToken<(url: string) => void>('LEAVE_TO', {
  providedIn: 'root',
  factory: () => {
    const document = inject(DOCUMENT);
    return (url: string) => document.location.assign(url);
  },
});
