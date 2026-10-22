import { useCallback } from 'react';
import { ticketsApi } from '../../../api/tickets';
import { useAsyncData } from '../../../hooks/useAsyncData';

/** Loads escalation history for one ticket, newest first. */
export function useTicketEscalations(ticketId: number) {
  const load = useCallback(() => ticketsApi.getEscalations(ticketId), [ticketId]);

  return useAsyncData(load, 'Unable to load escalation history.');
}

