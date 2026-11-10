// UX-09 (owner note 37): what NAMA's words mean, written once and shown by the ⓘ button
// (shared/info.ts), so a term reads the same on every page. The app's words, as docs/PRD.md
// section 3 and the rules define them. Each meaning follows "Term:" and ends with a period.
export const GLOSSARY = {
  // Tracker's Year view (MON-02, owner note 32).
  spent: { term: 'Spent', meaning: 'what you spent in the month.' },
  plan: { term: 'Plan', meaning: "the month's own plan." },
  // R-PLN-05, carried from the FY's first month in the Year view (owner, 8 Nov 2026).
  available: { term: 'Available', meaning: "the month's plan plus what was left (or overspent) in the FY's earlier months." },
  // R-BAL-03, R-PLN-07.
  balance: { term: 'Balance', meaning: 'the money in your accounts at the end of the month, projected from today on.' },

  // Tracker's Totals (owner note 18) and Dashboard's Plan vs actual (DSH-02).
  unspentThisMonth: { term: 'Unspent this month', meaning: "the month's income minus what you spent." },
  accumulatedSavings: { term: 'Accumulated savings', meaning: "each month's unspent added up from the FY's first month." },
  projected: {
    term: 'Projected',
    meaning: "the rest of this month's plan is spent, then each month to come adds its income minus its plan.",
  },

  // The month (MON-01, DSH-01, owner note 32, R-PLN-09).
  remainingThisMonth: { term: 'Remaining this month', meaning: "the month's plan minus what you spent." },
  overspentLastMonth: { term: 'Overspent last month', meaning: 'what went over plan last month and is not made up yet.' },
  afterCovering: { term: 'After covering', meaning: 'what is left this month once the overspend is made up.' },

  // Plan's Allocating (R-PLN-03, R-PLN-04).
  unallocatedThisMonth: { term: 'Unallocated this month', meaning: "the month's income that no plan uses yet." },
  accumulatedPlannedSavings: {
    term: 'Accumulated Planned Savings',
    meaning: "each month's unallocated added up from the FY's first month; its FY total is what your plan saves this FY.",
  },

  // Dashboard's Saving (DSH-06) and Total balance (DSH-04).
  plannedSaving: { term: 'Planned saving', meaning: "Plan's Accumulated Planned Savings at the end of the FY." },
  projectedSaving: {
    term: 'Projected saving',
    meaning: "Tracker's Accumulated savings at the end of the FY: what happened so far, then each month to come's income minus its plan.",
  },
  thisFyAdded: { term: 'This FY added', meaning: "income minus spent in the FY's closed months." },

  // Banks (R-BAL-01 to R-BAL-03, R-TRF-01).
  accountBalance: {
    term: 'Balance',
    meaning:
      'its opening balance, plus its share of income on each payday, plus transfers in, minus transfers out, minus the expenses paid from it.',
  },
  openingBalance: { term: 'Opening balance', meaning: 'what the account held at the start of a day; NAMA keeps it up to date from then on.' },
  incomeSplit: { term: 'Income split', meaning: "which share of each month's income goes into which account on payday." },
  transfer: { term: 'Transfer', meaning: 'money moved between two of your own accounts. It is never spending or income.' },

  // Loans (R-LON-01, R-LON-02).
  lent: { term: 'Lent', meaning: 'you gave the money: they owe you.' },
  borrowed: { term: 'Borrowed', meaning: 'you received the money: you owe it.' },

  // A project (R-PRJ-04 to R-PRJ-08).
  projectNow: { term: 'Now', meaning: "the money in the project's account today." },
  byTheDeadline: {
    term: 'By the deadline',
    meaning: "the money now, plus what is still to put in this month, plus each month's amount to come.",
  },
  putIn: {
    term: 'Put in',
    meaning: 'what reached its account in the month (your share of income, income items, transfers), less money taken out.',
  },
  milestone: { term: 'Milestone', meaning: 'money you need on a day, compared with the money in its account that day.' },
  behindOrAhead: { term: 'Behind or ahead', meaning: 'what was planned but not put in (or put in beyond it), carried into the next month.' },
} as const;

export type GlossaryKey = keyof typeof GLOSSARY;
