import { BreakpointObserver } from '@angular/cdk/layout';
import { DOCUMENT, NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ComponentType } from '@angular/cdk/portal';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom, map } from 'rxjs';
import { Me, MeService } from '../../core/me';
import { SummaryService } from '../../core/summary';
import { dialogSize } from '../../shared/dialog';
import { countsLabel, incomeMonths } from '../../shared/income-months';
import { formatMoney, toMinor } from '../../shared/money';
import { MONTH_NAMES, formatDate, monthKeyLabel, periodWithDates } from '../../shared/month-label';
import { CalendarDialog } from './calendar-dialog';
import { IncomeItemDialog, IncomeItemDialogData } from './income-item-dialog';
import { incomeItemContext } from './income-item-fields';
import { InviteLinkDialog, InviteLinkDialogData, RemovePersonDialog } from './people-dialogs';
import { ProfileDialog } from './profile-dialog';
import { SalaryDialog, SalaryDialogData } from './salary-dialog';
import { Calendar, Income, IncomeItem, Invite, Member, People, SettingsApi, SettingsResponse } from './settings-api';
import { monthGroups } from './settings-lines';
import { SETTINGS_SECTIONS, SettingsSectionId, visibleSections } from './settings-sections';

const PHONE = '(max-width: 767.98px)';

// PRD 5.7 Settings (STG-01 to STG-06). Owner's choice (M8): each section is its own page,
// /settings/<section>. Desktop picks it in the side bar (/settings opens Profile); phones open
// /settings as a list of the sections with a summary each, and a section page has a back link.
// No page has a filled button; every change opens one dialog whose Save is its one filled button
// (UX-04). Archive and restore are reversible, so they need no confirmation. Load = GET
// /api/settings and GET /api/health (NFR-01), once for every section; each change is one call.
// Long lists stay short: only the newest salary shows. Categories are on Plan's Categories tab
// (owner note 9). People (STG-07, AUTH-07) is for admins only: /api/settings sends it
// only to them.
@Component({
  selector: 'app-settings',
  imports: [NgTemplateOutlet, RouterLink, MatButtonModule, MatIconModule],
  templateUrl: './settings.html',
  styleUrl: './settings.scss',
})
export class Settings {
  private readonly api = inject(SettingsApi);
  private readonly meService = inject(MeService);
  private readonly summaryService = inject(SummaryService);
  private readonly dialog = inject(MatDialog);
  private readonly breakpoints = inject(BreakpointObserver);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);

  protected readonly monthNames = MONTH_NAMES;
  protected readonly formatDate = formatDate;
  protected readonly incomeMonths = incomeMonths;

  // "Sep 2026 · Actual · into SNB" (STG-03, R-INC-04, R-INC-05).
  protected incomeCaption(item: IncomeItem): string {
    const account = this.data()?.income.bankAccounts.find((option) => option.id === item.bankAccountId)?.name;
    return [incomeMonths(item), countsLabel(item.counts), account ? `into ${account}` : ''].filter(Boolean).join(' · ');
  }

  protected readonly phone = toSignal(this.breakpoints.observe(PHONE).pipe(map((state) => state.matches)), {
    initialValue: this.breakpoints.isMatched(PHONE),
  });
  private readonly sectionParam = toSignal(this.route.paramMap.pipe(map((params) => params.get('section'))), {
    initialValue: null,
  });
  // null is the list of sections (phones only).
  protected readonly section = computed(() => SETTINGS_SECTIONS.find((s) => s.id === this.sectionParam()) ?? null);

  protected readonly loadError = signal('');
  protected readonly data = signal<SettingsResponse | null>(null);
  protected readonly version = signal('');
  protected readonly status = signal('');
  protected readonly busy = signal(false);

  private readonly currency = computed(() => this.data()?.profile.currency ?? 'SAR');

  protected readonly sections = computed(() => visibleSections(!!this.data()?.people));

  // The folded parts that are open: "salary-history".
  private readonly opened = signal<ReadonlySet<string>>(new Set());

  // STG-03: "Sep 2026 · 27 Sep to 26 Oct", or null when no FY contains today.
  protected readonly currentMonth = computed(() => {
    const income = this.data()?.income;
    const key = income?.currentPeriodKey;
    if (!income || !key) {
      return null;
    }
    const period = income.fiscalYears.flatMap((fy) => fy.periods).find((p) => p.key === key);
    return period ? periodWithDates(period) : monthKeyLabel(key);
  });

  // STG-02: "FY 2027 · 27 Jan 2027 to 26 Jul 2027 · 6 months (transition)".
  protected readonly nextFiscalYear = computed(() => {
    const fy = this.data()?.calendar.nextFiscalYear;
    if (!fy) {
      return '';
    }
    const months = `${fy.periods.length} ${fy.periods.length === 1 ? 'month' : 'months'}`;
    return `${fy.label} · ${formatDate(fy.startDate)} to ${formatDate(fy.endDate)} · ${months}${fy.isTransition ? ' (transition)' : ''}`;
  });

  // The phone list: one line per section, so the values show without opening it.
  protected readonly summaries = computed<Partial<Record<SettingsSectionId, string>>>(() => {
    const d = this.data();
    if (!d) {
      return {};
    }
    return {
      profile: `${d.profile.projectName} · ${d.profile.currency}`,
      'fiscal-year': `Starts in ${MONTH_NAMES[d.calendar.fiscalStartMonth - 1]} · Payday ${d.calendar.paydayDay}`,
      income: d.income.currentTotal !== null ? `${this.money(d.income.currentTotal)} this month` : 'No current month',
      people: d.people ? peopleSummary(d.people) : undefined,
    };
  });

  constructor() {
    void this.load();
    // Desktop has no list page: /settings opens Profile. An unknown section goes back to /settings.
    effect(() => {
      const param = this.sectionParam();
      if (param && !this.section()) {
        void this.router.navigate(['/settings'], { replaceUrl: true });
      } else if (!param && !this.phone()) {
        void this.router.navigate(['/settings', 'profile'], { replaceUrl: true });
      } else if (param === 'people' && this.data() && !this.data()!.people) {
        // Not an admin: People is not one of your sections.
        void this.router.navigate(['/settings'], { replaceUrl: true });
      }
    });
    // A message belongs to the section it was shown on.
    effect(() => {
      this.section();
      this.status.set('');
    });
  }

  protected isOpen(key: string): boolean {
    return this.opened().has(key);
  }

  protected toggle(key: string): void {
    this.opened.update((keys) => {
      const next = new Set(keys);
      if (!next.delete(key)) {
        next.add(key);
      }
      return next;
    });
  }

  protected money(amount: number | null | undefined): string {
    return formatMoney(toMinor(amount ?? 0), this.currency());
  }

  // STG-01. The new /api/me updates the top bar, the currency and "today" everywhere.
  protected async editProfile(): Promise<void> {
    const me = await this.open<ProfileDialog, unknown, Me>(ProfileDialog, this.data()!.profile, '480px');
    if (me?.profile) {
      this.meService.set(me);
      this.patch({ profile: me.profile });
      this.status.set('Profile saved.');
    }
  }

  // STG-02, R-CAL-07.
  protected async editCalendar(): Promise<void> {
    const calendar = await this.open<CalendarDialog, Calendar, Calendar>(CalendarDialog, this.data()!.calendar, '480px');
    if (calendar) {
      this.meService.patchProfile({ fiscalStartMonth: calendar.fiscalStartMonth, paydayDay: calendar.paydayDay });
      this.patch({ calendar });
      this.status.set(`Saved. It takes effect from the fiscal year starting ${formatDate(calendar.takesEffectFrom)}.`);
    }
  }

  // R-INC-02
  protected async changeSalary(): Promise<void> {
    const income = this.data()!.income;
    const data: SalaryDialogData = {
      groups: monthGroups(income.fiscalYears),
      defaultKey: income.currentPeriodKey,
      currency: this.currency(),
    };
    this.incomeSaved(await this.open<SalaryDialog, SalaryDialogData, Income>(SalaryDialog, data, '480px'), 'Salary saved.');
  }

  // R-INC-03 to R-INC-05: add (item null), edit and delete. The preview uses the top bar's balances.
  protected async editIncome(item: IncomeItem | null): Promise<void> {
    const income = this.data()!.income;
    const me = this.meService.me();
    const active = income.bankAccounts.filter((account) => !account.archived);
    const data: IncomeItemDialogData = {
      item,
      context: incomeItemContext(income, this.summaryService.summary(), me?.today ?? null, item),
      defaultKey: income.currentPeriodKey,
      defaultAccountId: (active.find((account) => account.id === me?.profile?.lastBankAccountId) ?? active[0])?.id ?? null,
      currency: this.currency(),
    };
    this.incomeSaved(await this.open<IncomeItemDialog, IncomeItemDialogData, Income>(IncomeItemDialog, data), 'Income saved.');
  }

  // AUTH-06: the link is built here from the token, and shown once.
  protected async createInvite(): Promise<void> {
    this.busy.set(true);
    this.status.set('');
    try {
      const created = await this.api.createInvite();
      this.patch({ people: created.people });
      const link = `${this.document.location.origin}/invite/${created.token}`;
      await this.open<InviteLinkDialog, InviteLinkDialogData, unknown>(InviteLinkDialog, { link, expiresOn: created.expiresOn }, '520px');
      this.status.set('Invite link created.');
    } catch {
      this.status.set('The link could not be created. Please try again.');
    } finally {
      this.busy.set(false);
    }
  }

  // AUTH-07: an unused link can be made again in one tap, so revoking needs no confirmation.
  protected async revokeInvite(invite: Invite): Promise<void> {
    await this.peopleChange(() => this.api.revokeInvite(invite.id), 'Link revoked.');
  }

  // AUTH-07, UX-04: confirmed first. Their data is kept.
  protected async removePerson(member: Member): Promise<void> {
    if (await this.open<RemovePersonDialog, string, boolean>(RemovePersonDialog, member.email, '440px')) {
      await this.peopleChange(() => this.api.removePerson(member.id), `Removed ${member.email}.`);
    }
  }

  // A 404 means the link was used or revoked meanwhile, or the person already removed: the list is
  // read again so it shows what is there now.
  private async peopleChange(call: () => Promise<{ people: People }>, message: string): Promise<void> {
    this.busy.set(true);
    try {
      this.patch({ people: (await call()).people });
      this.status.set(message);
    } catch (error: unknown) {
      if (error instanceof HttpErrorResponse && error.status === 404) {
        this.patch({ people: (await this.api.settings().catch(() => this.data()!)).people });
        this.status.set('That changed in the meantime. The list is up to date now.');
      } else {
        this.status.set('It could not be saved. Please try again.');
      }
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      // The version is a nicety (STG-06); the page works without it.
      const [data, version] = await Promise.all([this.api.settings(), this.api.version().catch(() => '')]);
      this.data.set(data);
      this.version.set(version.slice(0, 7));
    } catch {
      this.loadError.set('Settings could not be loaded. Please reload the page.');
    }
  }

  private incomeSaved(income: Income | undefined, message: string): void {
    if (income) {
      this.patch({ income });
      this.status.set(message);
    }
  }

  private patch(changes: Partial<SettingsResponse>): void {
    this.data.update((data) => (data ? { ...data, ...changes } : data));
  }

  // One dialog at a time (UX-04); full screen on phones.
  private open<C, D, R>(component: ComponentType<C>, data: D, width?: string): Promise<R | undefined> {
    this.status.set('');
    const ref = this.dialog.open<C, D, R>(component, { ...dialogSize(this.breakpoints.isMatched(PHONE), width), data });
    return firstValueFrom(ref.afterClosed());
  }
}

// "2 people · 1 unused link" on the phone list.
function peopleSummary(people: People): string {
  const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;
  const unused = people.invites.filter((invite) => !invite.expired).length;
  return `${plural(people.members.length, 'person', 'people')} · ${plural(unused, 'unused link', 'unused links')}`;
}
