import { useState } from 'react';
import { ApiError, toErrorMessage } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import { Field } from '../../../components/Field';
import type { EscalateTicketResponse, TicketDetail } from '../../../types/api';

interface EscalationPanelProps {
  ticket: TicketDetail;
  actorName?: string;
  onEscalated: (response: EscalateTicketResponse) => void;
}

/**
 * Escalates a ticket one priority level with a required reason (5–500 characters).
 * Validates client-side before calling the API, disables controls when the ticket
 * cannot be escalated, and surfaces server rejections (including 409 Conflict).
 */
export function EscalationPanel({ ticket, actorName, onEscalated }: EscalationPanelProps) {
  const [reason, setReason] = useState('');
  const [escalatedBy, setEscalatedBy] = useState(
    actorName ?? ticket.assignedAgent?.fullName ?? 'Alex Turner',
  );
  const [reasonError, setReasonError] = useState<string>();
  const [actorError, setActorError] = useState<string>();
  const [submitError, setSubmitError] = useState<string>();
  const [isPending, setIsPending] = useState(false);

  const isTerminalStatus = ticket.status === 'Resolved' || ticket.status === 'Closed';
  const isAlreadyCritical = ticket.priority === 'Critical';
  const canEscalate = !isTerminalStatus && !isAlreadyCritical;

  const disabledReason = isTerminalStatus
    ? `This ticket is ${ticket.status.toLowerCase()} and cannot be escalated.`
    : isAlreadyCritical
      ? 'This ticket is already at Critical priority and cannot be escalated further.'
      : undefined;

  const submit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    setReasonError(undefined);
    setActorError(undefined);
    setSubmitError(undefined);

    const trimmedReason = reason.trim();
    const effectiveActor = (actorName ?? escalatedBy).trim();

    let hasValidationError = false;
    if (trimmedReason.length < 5) {
      setReasonError('Provide an escalation reason of at least 5 characters.');
      hasValidationError = true;
    } else if (trimmedReason.length > 500) {
      setReasonError('Escalation reason cannot exceed 500 characters.');
      hasValidationError = true;
    }

    if (effectiveActor.length === 0) {
      setActorError('Specify who is escalating this ticket.');
      hasValidationError = true;
    } else if (effectiveActor.length > 100) {
      setActorError('Escalated by cannot exceed 100 characters.');
      hasValidationError = true;
    }

    if (hasValidationError) {
      return;
    }

    setIsPending(true);

    try {
      const response = await ticketsApi.escalateTicket(ticket.id, {
        reason: trimmedReason,
        escalatedBy: effectiveActor,
      });
      setReason('');
      onEscalated(response);
    } catch (caught) {
      if (caught instanceof ApiError && Object.keys(caught.fieldErrors).length > 0) {
        const reasonMessages = caught.fieldErrors.Reason ?? caught.fieldErrors.reason;
        if (reasonMessages?.[0]) {
          setReasonError(reasonMessages[0]);
        }
        const actorMessages = caught.fieldErrors.EscalatedBy ?? caught.fieldErrors.escalatedBy;
        if (actorMessages?.[0]) {
          setActorError(actorMessages[0]);
        }
      }

      setSubmitError(toErrorMessage(caught, 'Could not escalate the ticket.'));
    } finally {
      setIsPending(false);
    }
  };

  return (
    <div className="card">
      <h2>Escalate ticket</h2>

      {disabledReason && (
        <p className="field__hint" role="note">
          {disabledReason}
        </p>
      )}

      <form onSubmit={submit} noValidate>
        <Field
          id="escalation-reason"
          label="Escalation reason"
          error={reasonError}
          hint="Between 5 and 500 characters."
        >
          {(fieldProps) => (
            <textarea
              {...fieldProps}
              rows={3}
              value={reason}
              maxLength={500}
              disabled={!canEscalate || isPending}
              onChange={(event) => setReason(event.target.value)}
            />
          )}
        </Field>

        {!actorName && (
          <Field id="escalation-actor" label="Escalated by" error={actorError}>
            {(fieldProps) => (
              <input
                {...fieldProps}
                type="text"
                value={escalatedBy}
                maxLength={100}
                disabled={!canEscalate || isPending}
                onChange={(event) => setEscalatedBy(event.target.value)}
              />
            )}
          </Field>
        )}

        {submitError && (
          <p className="field__error" role="alert">
            {submitError}
          </p>
        )}

        <div className="button-row">
          <button
            type="submit"
            className="button button--primary"
            disabled={!canEscalate || isPending}
          >
            {isPending ? 'Escalating...' : 'Escalate ticket'}
          </button>
        </div>
      </form>
    </div>
  );
}

