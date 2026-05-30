import type { Resource } from '@/types/content'

export const mockResources: Resource[] = [
  {
    id: 'r1',
    title: 'Parkinson’s UK helpline',
    description:
      'Free, confidential support from trained advisers. Mon–Fri 9am–7pm, Sat 10am–2pm.',
    url: 'https://www.parkinsons.org.uk/information-and-support/helpline-and-local-advisers',
    category: "Parkinson's UK",
  },
  {
    id: 'r2',
    title: 'Parkinson’s UK — for the newly diagnosed',
    description:
      'The single best starting point if you’ve been diagnosed in the last few months.',
    url: 'https://www.parkinsons.org.uk/information-and-support/newly-diagnosed',
    category: "Parkinson's UK",
  },
  {
    id: 'r3',
    title: 'NHS — Parkinson’s disease overview',
    description:
      'Plain-English summary of symptoms, treatment, and what to expect from NHS care.',
    url: 'https://www.nhs.uk/conditions/parkinsons-disease/',
    category: 'NHS',
  },
  {
    id: 'r4',
    title: 'King’s College Hospital movement disorders clinic',
    description:
      'The specialist movement-disorders unit at King’s College Hospital, Denmark Hill. GP referral required.',
    url: 'https://www.kch.nhs.uk/services/neurology',
    category: 'Local',
  },
  {
    id: 'r5',
    title: 'Deep brain stimulation (DBS) — Parkinson’s UK',
    description:
      'What DBS is, who it might suit, and how to be referred for an assessment.',
    url: 'https://www.parkinsons.org.uk/information-and-support/deep-brain-stimulation',
    category: "Parkinson's UK",
  },
  {
    id: 'r6',
    title: 'Personal Independence Payment (PIP)',
    description:
      'The main UK benefit you may be entitled to as a working-age adult with Parkinson’s.',
    url: 'https://www.gov.uk/pip',
    category: 'Benefits',
  },
  {
    id: 'r7',
    title: 'Access to Work — disability employment support',
    description:
      'A government grant scheme to pay for workplace adjustments your employer cannot reasonably cover.',
    url: 'https://www.gov.uk/access-to-work',
    category: 'Benefits',
  },
  {
    id: 'r8',
    title: 'Carers UK',
    description:
      'Advice, peer support, and a helpline specifically for unpaid carers.',
    url: 'https://www.carersuk.org/',
    category: 'Carers',
  },
  {
    id: 'r9',
    title: 'Parkinson’s UK — current research',
    description:
      'The charity’s research overview, including ways to take part in clinical studies.',
    url: 'https://www.parkinsons.org.uk/research',
    category: 'Research',
  },
]
