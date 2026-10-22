import { formatDateTime } from '../../../lib/format';
import type { TicketEscalation } from '../../../types/api';
import { PriorityBadge } from './TicketBadges';

interface EscalationHistoryProps {
  escalations: TicketEscalation[];
  isLoading: boolean;
  error?: string;
}

/** Displays a ticket's escalation history, newest first. */
export function EscalationHistory({ escalations, isLoading, error }: EscalationHistoryProps) {
  return (
    <div className="card">
      <h2>Escalation history</h2>

      {isLoading && <p className="field__hint">Loading escalation history...</p>}

      {error && (
        <p className="field__error" role="alert">
          {error}
        </p>
      )}

      {!isLoading && !error && escalations.length === 0 && (
        <p className="field__hint">This ticket has not been escalated.</p>
      )}

      {escalations.length > 0 && (
        <ol className="escalation-history">
          {escalations.map((item) => (
            <li key={item.id} className="escalation-history__item">
              <div className="escalation-history__header">
                <strong>{item.escalatedBy}</strong>
                <span className="summary__note">{formatDateTime(item.escalatedAtUtc)}</span>
              </div>

              <div className="escalation-history__changes">
                <div>
                  <span className="field__label">Priority: </span>
                  <PriorityBadge priority={item.fromPriority} />
                  <span aria-label="to"> → </span>
                  <PriorityBadge priority={item.toPriority} />
                </div>

                <div>
                  <span className="field__label">Assigned agent: </span>
                  <span>{item.fromAgent?.fullName ?? 'Unassigned'}</span>
                  <span aria-label="to"> → </span>
                  <span>{item.toAgent?.fullName ?? 'Unassigned'}</span>
                </div>

                <div>
                  <span className="field__label">Due date: </span>
                  <span>{formatDateTime(item.fromDueAtUtc)}</span>
                  <span aria-label="to"> → </span>
                  <span>{formatDateTime(item.toDueAtUtc)}</span>
                </div>
              </div>

              <p className="escalation-history__reason">{item.reason}</p>
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}

