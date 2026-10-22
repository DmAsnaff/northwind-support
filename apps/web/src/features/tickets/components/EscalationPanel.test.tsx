import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import type { TicketDetail } from '../../../types/api';
import { EscalationPanel } from './EscalationPanel';

vi.mock('../../../api/tickets', () => ({
  ticketsApi: {
    escalateTicket: vi.fn(),
  },
}));

const escalateTicket = vi.mocked(ticketsApi.escalateTicket);

function sampleTicket(overrides: Partial<TicketDetail> = {}): TicketDetail {
  return {
    id: 12,
    reference: 'TCK-0012',
    title: 'Invoice total does not match the order',
    description: 'Invoice 5512 is EUR 240 higher than the confirmed order.',
    status: 'InProgress',
    priority: 'Medium',
    customer: {
      id: 2,
      name: 'Fabrikam Inc',
      email: 'helpdesk@fabrikam.example',
      phone: null,
      tier: 'Standard',
    },
    category: { id: 2, name: 'Billing', requiresSpecialist: true },
    assignedAgent: { id: 4, fullName: 'Sara Lindqvist' },
    createdAtUtc: '2026-09-24T08:12:00Z',
    updatedAtUtc: '2026-09-25T10:03:00Z',
    dueAtUtc: '2026-09-24T16:12:00Z',
    resolvedAtUtc: null,
    slaStatus: 'WithinSla',
    ...overrides,
  };
}

describe('EscalationPanel', () => {
  afterEach(() => {
    vi.clearAllMocks();
  });

  it('blocks submission when the reason is shorter than 5 characters and does not call the API', () => {
    const onEscalated = vi.fn();
    render(<EscalationPanel ticket={sampleTicket()} onEscalated={onEscalated} />);

    fireEvent.change(screen.getByLabelText(/escalation reason/i), {
      target: { value: 'abc' },
    });
    fireEvent.click(screen.getByRole('button', { name: /escalate ticket/i }));

    expect(screen.getByText(/at least 5 characters/i)).toBeInTheDocument();
    expect(escalateTicket).not.toHaveBeenCalled();
    expect(onEscalated).not.toHaveBeenCalled();
  });

  it('shows a 409 conflict message from the server and leaves the ticket unchanged', async () => {
    const onEscalated = vi.fn();
    escalateTicket.mockRejectedValueOnce(
      new ApiError(409, 'Ticket TCK-0012 is already at Critical priority and cannot be escalated further.'),
    );

    render(<EscalationPanel ticket={sampleTicket()} onEscalated={onEscalated} />);

    fireEvent.change(screen.getByLabelText(/escalation reason/i), {
      target: { value: 'Customer production environment is completely blocked.' },
    });
    fireEvent.click(screen.getByRole('button', { name: /escalate ticket/i }));

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(
        /already at Critical priority and cannot be escalated further/i,
      );
    });

    expect(escalateTicket).toHaveBeenCalledTimes(1);
    expect(onEscalated).not.toHaveBeenCalled();
  });

  it('disables the escalate controls when the ticket is already Critical or Resolved', () => {
    const onEscalated = vi.fn();
    render(
      <EscalationPanel
        ticket={sampleTicket({ priority: 'Critical' })}
        onEscalated={onEscalated}
      />,
    );

    expect(screen.getByRole('button', { name: /escalate ticket/i })).toBeDisabled();
    expect(screen.getByRole('note')).toHaveTextContent(/already at Critical priority/i);
  });
});

