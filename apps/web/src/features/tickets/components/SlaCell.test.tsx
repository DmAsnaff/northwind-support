import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { SlaStatus } from '../../../types/api';
import { SlaIndicator } from './SlaCell';

describe('SlaIndicator', () => {
  const cases: { status: SlaStatus; expectedText: string; expectedSymbol: string; expectedTone: string }[] = [
    { status: 'WithinSla', expectedText: 'Within SLA', expectedSymbol: '●', expectedTone: 'info' },
    { status: 'AtRisk', expectedText: 'At risk', expectedSymbol: '⚠', expectedTone: 'warning' },
    { status: 'Breached', expectedText: 'Breached', expectedSymbol: '✕', expectedTone: 'danger' },
    { status: 'Met', expectedText: 'Met', expectedSymbol: '✓', expectedTone: 'success' },
    { status: 'NotApplicable', expectedText: 'No SLA', expectedSymbol: '—', expectedTone: 'neutral' },
  ];

  it.each(cases)(
    'renders $status distinguishably by text, symbol and tone (not colour alone)',
    ({ status, expectedText, expectedSymbol, expectedTone }) => {
      const { container } = render(<SlaIndicator slaStatus={status} />);

      expect(screen.getByText(expectedText)).toBeInTheDocument();
      expect(container.textContent).toContain(expectedSymbol);

      const wrapper = container.querySelector(`[data-sla-status="${status}"]`);
      expect(wrapper).not.toBeNull();
      expect(wrapper).toHaveAttribute('data-tone', expectedTone);
    },
  );
});

