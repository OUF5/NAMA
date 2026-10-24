import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { ActivatedRoute } from '@angular/router';
import { AuthService } from '../../core/auth';
import { AuthorizeRequest, CheckResult, ConnectApi, LEAVE_TO } from './connect-api';

type Step = 'loading' | 'sign-in' | 'retry' | 'ready' | 'invalid' | 'leaving';

// MCP-01: /connect, the page an app such as Claude Code opens to ask for read-only access (NAMA's
// OAuth authorize page). Public, like the invite page: it signs in itself, so the request in its
// address is kept. It checks the request first; Allow is its one filled button (UX-04). A request that
// fails a check is shown here and sends you nowhere (MCP-02).
@Component({
  selector: 'app-connect',
  imports: [MatButtonModule],
  template: `
    <section class="card" aria-labelledby="connect-title">
      <span class="logo" aria-hidden="true"></span>
      @switch (step()) {
        @case ('loading') {
          <h1 id="connect-title">Connect to NAMA</h1>
          <p class="caption" role="status">Checking the request…</p>
        }
        @case ('sign-in') {
          <h1 id="connect-title">Connect to NAMA</h1>
          <p class="caption">An app asks to read your NAMA data. Sign in with Google to see what it asks for.</p>
          <button class="google" type="button" [disabled]="busy()" (click)="signIn()">
            <img src="google-g.svg" alt="" width="18" height="18" />
            <span>Sign in with Google</span>
          </button>
        }
        @case ('retry') {
          <h1 id="connect-title">Connect to NAMA</h1>
          <button mat-stroked-button type="button" (click)="check()">Try again</button>
        }
        @case ('invalid') {
          <h1 id="connect-title">This request is not valid</h1>
          <p class="caption">{{ error() }}</p>
          <p class="note">Nothing was shared. You can close this tab.</p>
        }
        @case ('ready') {
          <h1 id="connect-title">Connect {{ client()!.clientName }}?</h1>
          <ul class="facts">
            <li><strong>Read-only:</strong> it can see your budget, but not change it.</li>
            @if (email()) {
              <li>Your data as {{ email() }}</li>
            }
            <li>Sends you back to <strong>{{ client()!.redirectHost }}</strong></li>
          </ul>
          @if (local()) {
            <p class="warn">Allow only if you started this yourself from Claude Code on this computer.</p>
          }
          <p class="note">You can disconnect it any time in Settings → Connected apps.</p>
          <div class="actions">
            <button mat-stroked-button type="button" [disabled]="busy()" (click)="answer(false)">Cancel</button>
            <button mat-flat-button type="button" [disabled]="busy()" (click)="answer(true)">Allow</button>
          </div>
        }
        @case ('leaving') {
          <h1 id="connect-title">Back to {{ client()?.clientName ?? 'the app' }}</h1>
          <p class="caption" role="status">You can close this tab once the app says it is connected.</p>
        }
      }
      @if (status()) {
        <p class="error" role="alert">{{ status() }}</p>
      }
    </section>
  `,
  styles: `
    :host {
      position: fixed;
      inset: 0;
      z-index: 10;
      display: flex;
      align-items: center;
      justify-content: center;
      overflow: auto;
      padding: 32px 16px calc(24px + env(safe-area-inset-bottom));
      box-sizing: border-box;
      background: var(--bg);
    }

    .card {
      box-sizing: border-box;
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 16px;
      width: 100%;
      max-width: 420px;
      padding: 32px 24px;
      text-align: center;
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: 12px;
    }

    // The NAMA mark, as on the sign-in page.
    .logo {
      box-sizing: border-box;
      width: 40px;
      height: 40px;
      background: var(--bg);
      border: 3px solid var(--primary);
    }

    h1 {
      margin: 0;
      overflow-wrap: anywhere;
    }

    .caption,
    .note,
    .warn,
    .error {
      margin: 0;
    }

    .note {
      font-size: 13px;
      color: var(--text-2);
    }

    .warn {
      color: var(--warn);
    }

    .error {
      color: var(--danger);
    }

    .facts {
      align-self: stretch;
      margin: 0;
      padding: 12px 16px;
      list-style: none;
      text-align: left;
      background: var(--surface-2);
      border-radius: 8px;
      overflow-wrap: anywhere;

      li + li {
        margin-top: 8px;
      }
    }

    .actions {
      display: flex;
      justify-content: flex-end;
      gap: 8px;
      align-self: stretch;
    }

    .google {
      display: inline-flex;
      align-items: center;
      gap: 12px;
      height: 40px;
      padding: 0 12px;
      border: 1px solid var(--google-border);
      border-radius: 4px;
      background: var(--google-bg);
      color: var(--google-text);
      font: 500 14px var(--font);
      cursor: pointer;

      &:hover:not(:disabled) {
        background: var(--google-bg-hover);
      }

      &:disabled {
        opacity: 0.6;
        cursor: default;
      }
    }

    @media (max-width: 767.98px) {
      .google,
      .actions button {
        height: 44px;
      }

      .actions button {
        flex: 1;
      }
    }
  `,
})
export class Connect {
  private readonly auth = inject(AuthService);
  private readonly api = inject(ConnectApi);
  private readonly leaveTo = inject(LEAVE_TO);

  // OAuth's own names in the address (RFC 6749 section 4.1.1, RFC 7636, RFC 8707).
  private readonly request: AuthorizeRequest = (() => {
    const query = inject(ActivatedRoute).snapshot.queryParamMap;
    return {
      responseType: query.get('response_type'),
      clientId: query.get('client_id'),
      redirectUri: query.get('redirect_uri'),
      codeChallenge: query.get('code_challenge'),
      codeChallengeMethod: query.get('code_challenge_method'),
      state: query.get('state'),
      scope: query.get('scope'),
      resource: query.get('resource'),
    };
  })();

  protected readonly step = signal<Step>('loading');
  protected readonly client = signal<CheckResult | null>(null);
  protected readonly error = signal('');
  protected readonly status = signal('');
  protected readonly busy = signal(false);
  protected readonly email = computed(() => this.auth.user()?.email ?? '');
  // The MCP spec asks for a warning when the app comes back on this computer (a local port).
  protected readonly local = computed(() => /^(localhost|127\.0\.0\.1)(:|$)/.test(this.client()?.redirectHost ?? ''));

  constructor() {
    void this.start();
  }

  protected async signIn(): Promise<void> {
    this.busy.set(true);
    this.status.set('');
    try {
      await this.auth.signIn();
    } catch {
      this.status.set('Sign-in did not finish. Please try again.');
      return;
    } finally {
      this.busy.set(false);
    }
    await this.check();
  }

  protected async answer(allow: boolean): Promise<void> {
    this.busy.set(true);
    this.status.set('');
    try {
      const { redirectTo } = await this.api.authorize(this.request, allow);
      this.step.set('leaving');
      this.leaveTo(redirectTo);
    } catch (error: unknown) {
      this.failed(error);
    } finally {
      this.busy.set(false);
    }
  }

  private async start(): Promise<void> {
    await this.auth.ready();
    if (this.auth.user()) {
      await this.check();
    } else {
      this.step.set('sign-in');
    }
  }

  protected async check(): Promise<void> {
    this.step.set('loading');
    this.status.set('');
    try {
      this.client.set(await this.api.check(this.request));
      this.step.set('ready');
    } catch (error: unknown) {
      this.failed(error);
    }
  }

  // 400: the request failed a check (its reason is shown). A 403 goes to no-access (auth interceptor).
  private failed(error: unknown): void {
    if (error instanceof HttpErrorResponse && error.status === 400) {
      this.error.set(error.error?.detail ?? 'The app sent a request NAMA cannot answer.');
      this.step.set('invalid');
    } else {
      this.status.set('NAMA could not be reached. Please try again.');
      if (this.step() === 'loading') {
        this.step.set('retry');
      }
    }
  }
}
