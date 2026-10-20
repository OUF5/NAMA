import { BreakpointObserver } from '@angular/cdk/layout';
import { ComponentType } from '@angular/cdk/portal';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { MeService } from '../../core/me';
import { dialogSize } from '../../shared/dialog';
import { formatMoney, formatPercent, toBasisPoints, toMinor } from '../../shared/money';
import { formatDate, formatDay, monthKeyLabel } from '../../shared/month-label';
import { monthGroups } from '../settings/settings-lines';
import { AccountDialog, AccountDialogData } from './account-dialog';
import { Account, BanksApi, BanksResponse, Split, Transfer } from './banks-api';
import { SplitDialog, SplitDialogData } from './split-dialog';
import { TransferDialog, TransferDialogData, TransferDialogResult } from './transfer-dialog';

const PHONE = '(max-width: 767.98px)';

// The newest transfers show at first; the rest fold away.
const TRANSFER_LIMIT = 10;

// PRD 5.11 Banks (BNK-01 to BNK-05): every account with its balance today and how it adds up
// (R-BAL-03), the income split (R-BAL-02) and the transfers (R-TRF-01), in one call (NFR-01). Nothing
// is hidden: each account is a card, and the page says what is still missing. The page has no filled
// button; every change opens one dialog whose Save is its one filled button (UX-04). Archive and
// restore are reversible, so they need no confirmation. The API makes every sum; this only formats.
@Component({
  selector: 'app-banks',
  imports: [RouterLink, MatButtonModule, MatIconModule],
  templateUrl: './banks.html',
  styleUrl: './banks.scss',
})
export class Banks {
  private readonly api = inject(BanksApi);
  private readonly meService = inject(MeService);
  private readonly dialog = inject(MatDialog);
  private readonly breakpoints = inject(BreakpointObserver);

  protected readonly formatDate = formatDate;

  protected readonly loadError = signal('');
  protected readonly data = signal<BanksResponse | null>(null);
  protected readonly status = signal('');
  protected readonly busy = signal(false);
  protected readonly archivedOpen = signal(false);
  protected readonly historyOpen = signal(false);
  protected readonly allTransfers = signal(false);

  protected readonly currency = computed(() => this.meService.me()?.profile?.currency ?? 'SAR');

  protected readonly active = computed(() => (this.data()?.accounts ?? []).filter((account) => !account.archived));
  protected readonly archived = computed(() => (this.data()?.accounts ?? []).filter((account) => account.archived));

  // BNK-05: what is still missing before the total is complete.
  protected readonly missingOpening = computed(() => this.active().filter((account) => account.openingDate === null));

  protected readonly monthLabel = computed(() => {
    const data = this.data();
    const key = data?.currentPeriodKey ?? data?.split?.fromPeriodKey;
    return key ? monthKeyLabel(key) : '';
  });

  // Each active account's card. The breakdown reads top to bottom, like Plan's totals.
  protected readonly cards = computed(() =>
    this.active().map((account) => ({
      account,
      balance: account.balance ? this.money(account.balance.balance) : null,
      negative: (account.balance?.balance ?? 0) < 0,
      shareText: this.shareText(account),
      lines: account.balance
        ? [
            { op: '', label: `Opening on ${formatDate(account.openingDate!)}`, amount: this.money(account.balance.opening) },
            { op: '+', label: 'Income since', amount: this.money(account.balance.income) },
            { op: '+', label: 'Transfers in', amount: this.money(account.balance.transfersIn) },
            { op: '−', label: 'Transfers out', amount: this.money(account.balance.transfersOut) },
            { op: '−', label: 'Expenses paid', amount: this.money(account.balance.spent) },
          ]
        : [],
    })),
  );

  // BNK-03: the split of the shown month, with a bar per account.
  protected readonly splitRows = computed(() => {
    const data = this.data();
    if (!data?.split) {
      return [];
    }
    return data.accounts
      .filter((account) => (data.split!.shares[account.id] ?? 0) > 0 || !account.archived)
      .map((account) => {
        const basisPoints = toBasisPoints(data.split!.shares[account.id] ?? 0);
        return {
          id: account.id,
          name: account.archived ? `${account.name} (archived)` : account.name,
          percent: formatPercent(basisPoints),
          width: basisPoints / 100,
          amount: account.monthIncome === null ? '' : this.money(account.monthIncome),
        };
      });
  });

  protected readonly monthIncome = computed(() => {
    const data = this.data();
    const key = data?.currentPeriodKey;
    return data && key ? this.money(data.income[key] ?? 0) : null;
  });

  // "From Jan 2026 · SNB 80%, Savings 20%", newest first.
  protected readonly history = computed(() => {
    const data = this.data();
    if (!data) {
      return [];
    }
    const names = new Map(data.accounts.map((account) => [account.id, account.name]));
    return data.splitHistory.map((split) => this.splitLine(split, names));
  });

  protected readonly transfers = computed(() => {
    const data = this.data();
    if (!data) {
      return [];
    }
    const names = new Map(data.accounts.map((account) => [account.id, account.name]));
    return data.transfers.map((transfer) => {
      const from = names.get(transfer.fromBankAccountId) ?? '';
      const to = names.get(transfer.toBankAccountId) ?? '';
      const amount = this.money(transfer.amount);
      return {
        transfer,
        day: formatDay(transfer.date),
        year: transfer.date.slice(0, 4),
        from,
        to,
        amount,
        editLabel: `Edit transfer ${amount} from ${from} to ${to}, ${formatDate(transfer.date)}`,
      };
    });
  });
  protected readonly shownTransfers = computed(() =>
    this.allTransfers() ? this.transfers() : this.transfers().slice(0, TRANSFER_LIMIT),
  );
  protected readonly hiddenTransfers = computed(() => Math.max(0, this.transfers().length - TRANSFER_LIMIT));

  constructor() {
    void this.load();
  }

  protected monthLabelOf(key: string): string {
    return monthKeyLabel(key);
  }

  protected money(amount: number): string {
    return formatMoney(toMinor(amount), this.currency());
  }

  // BNK-02: add an account with its opening balance (R-BAL-01).
  protected async addAccount(): Promise<void> {
    const banks = await this.accountDialog(null);
    if (banks) {
      const added = banks.accounts.filter((account) => !this.data()!.accounts.some((known) => known.id === account.id));
      this.data.set(banks);
      this.status.set(added.length ? `Added ${added[0].name}.` : 'Account added.');
    }
  }

  // Rename and set the opening balance.
  protected async editAccount(account: Account): Promise<void> {
    const banks = await this.accountDialog(account);
    if (banks) {
      this.data.set(banks);
      this.status.set(`Saved ${banks.accounts.find((item) => item.id === account.id)?.name ?? account.name}.`);
    }
  }

  // Archived accounts are hidden from the pickers; their entries and transfers keep them (PRD 4.6).
  // The API refuses the last active account and one that gets a share of income (R-BAL-02).
  protected async toggleArchive(account: Account): Promise<void> {
    this.busy.set(true);
    this.status.set('');
    try {
      this.data.set(
        await this.api.updateAccount(account.id, {
          name: account.name,
          archived: !account.archived,
          openingBalance: account.openingBalance,
          openingDate: account.openingDate,
        }),
      );
      this.status.set(`${account.name} ${account.archived ? 'restored' : 'archived'}.`);
    } catch (error: unknown) {
      const message =
        error instanceof HttpErrorResponse && error.status === 400
          ? ((error.error?.errors?.['Archived'] as string[] | undefined)?.[0] ?? 'It could not be saved.')
          : 'It could not be saved. Please try again.';
      this.status.set(message);
    } finally {
      this.busy.set(false);
    }
  }

  // BNK-03, R-BAL-02.
  protected async changeSplit(): Promise<void> {
    const data = this.data()!;
    const dialogData: SplitDialogData = {
      accounts: this.active(),
      split: data.split,
      groups: monthGroups(data.fiscalYears),
      // Without a split yet, it covers every stored month; afterwards a change starts this month.
      defaultKey: data.split ? (data.currentPeriodKey ?? data.fiscalYears.at(-1)!.periods[0].key) : data.fiscalYears[0].periods[0].key,
      income: data.income,
      currency: this.currency(),
    };
    const banks = await this.open<SplitDialog, SplitDialogData, BanksResponse>(SplitDialog, dialogData, '640px');
    if (banks) {
      this.data.set(banks);
      this.status.set('Income split saved.');
    }
  }

  // BNK-04: edit or delete.
  protected async editTransfer(transfer: Transfer): Promise<void> {
    const me = this.meService.me();
    const result = await this.open<TransferDialog, TransferDialogData, TransferDialogResult>(TransferDialog, {
      transfer,
      accounts: this.data()!.accounts,
      currency: this.currency(),
      firstDate: me?.firstDate ?? null,
      lastDate: me?.lastDate ?? null,
    });
    if (result) {
      this.status.set(result === 'saved' ? 'Transfer updated.' : 'Transfer deleted.');
      await this.load();
    }
  }

  private shareText(account: Account): string {
    if (account.share === null) {
      return 'No income split yet';
    }
    if (account.share === 0) {
      return 'Gets no share of income';
    }
    const month = this.monthLabel();
    const amount = account.monthIncome === null ? '' : ` · ${this.money(account.monthIncome)}${month ? ` in ${month}` : ''}`;
    return `Gets ${formatPercent(toBasisPoints(account.share))} of income${amount}`;
  }

  private splitLine(split: Split, names: Map<string, string>): string {
    const shares = Object.entries(split.shares)
      .filter(([, percent]) => percent > 0)
      .map(([id, percent]) => `${names.get(id) ?? ''} ${formatPercent(toBasisPoints(percent))}`)
      .join(', ');
    return `From ${monthKeyLabel(split.fromPeriodKey)} · ${shares}`;
  }

  private accountDialog(account: Account | null): Promise<BanksResponse | undefined> {
    const data = this.data()!;
    const me = this.meService.me();
    return this.open<AccountDialog, AccountDialogData, BanksResponse>(
      AccountDialog,
      {
        account,
        taken: data.accounts.filter((item) => item.id !== account?.id).map((item) => item.name),
        currency: this.currency(),
        today: data.today,
        firstDate: me?.firstDate ?? data.fiscalYears[0].startDate,
        lastDate: me?.lastDate ?? data.fiscalYears.at(-1)!.endDate,
      },
      '520px',
    );
  }

  private async load(): Promise<void> {
    try {
      await this.meService.load();
      this.data.set(await this.api.banks());
      this.loadError.set('');
    } catch {
      this.loadError.set('Banks could not be loaded. Please reload the page.');
    }
  }

  // One dialog at a time (UX-04); full screen on phones.
  private open<C, D, R>(component: ComponentType<C>, data: D, width?: string): Promise<R | undefined> {
    this.status.set('');
    const ref = this.dialog.open<C, D, R>(component, { ...dialogSize(this.breakpoints.isMatched(PHONE), width), data });
    return firstValueFrom(ref.afterClosed());
  }
}
