import { Badge, type BadgeTone } from '../../../components/Badge';
import { formatDateTime } from '../../../lib/format';
import type { SlaStatus } from '../../../types/api';

interface SlaDescriptor {
  text: string;
  symbol: string;
  tone: BadgeTone;
}

const descriptors: Record<SlaStatus, SlaDescriptor> = {
  WithinSla: { text: 'Within SLA', symbol: '●', tone: 'info' },
  AtRisk: { text: 'At risk', symbol: '⚠', tone: 'warning' },
  Breached: { text: 'Breached', symbol: '✕', tone: 'danger' },
  Met: { text: 'Met', symbol: '✓', tone: 'success' },
  NotApplicable: { text: 'No SLA', symbol: '—', tone: 'neutral' },
};

export interface SlaIndicatorProps {
  slaStatus: SlaStatus;
}

/**
 * Reusable SLA status badge used across the ticket list and detail views.
 * Conveys state via both a distinct symbol and explicit label, never by colour alone.
 */
export function SlaIndicator({ slaStatus }: SlaIndicatorProps) {
  const descriptor = descriptors[slaStatus];

  return (
    <span data-sla-status={slaStatus} data-tone={descriptor.tone}>
      <Badge tone={descriptor.tone}>
        <span aria-hidden="true" className="sla-indicator__symbol">
          {descriptor.symbol}{' '}
        </span>
        <span>{descriptor.text}</span>
      </Badge>
    </span>
  );
}

interface SlaCellProps {
  dueAtUtc: string | null;
  slaStatus: SlaStatus;
}

/** The due date and where the ticket stands against it, as shown in the ticket list and detail summary. */
export function SlaCell({ dueAtUtc, slaStatus }: SlaCellProps) {
  return (
    <div className="sla-cell">
      <SlaIndicator slaStatus={slaStatus} />
      <span className="sla-cell__due">{formatDateTime(dueAtUtc)}</span>
    </div>
  );
}
