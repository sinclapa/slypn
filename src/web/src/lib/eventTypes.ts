// Canonical event types, sorted alphabetically. Used by the event form's type
// dropdown; the public filters derive their options from the events actually
// present (so legacy/differently-cased types still filter correctly).
export const EVENT_TYPES = [
  'Activity',
  'Carer session',
  'Coffee meet-up',
  'Drinks',
  'Fundraising',
  'Q&A',
  'Social',
] as const

export type EventType = typeof EVENT_TYPES[number]
