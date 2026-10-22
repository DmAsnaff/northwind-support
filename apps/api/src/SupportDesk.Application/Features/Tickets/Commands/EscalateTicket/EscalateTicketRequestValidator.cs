using FluentValidation;
using SupportDesk.Application.Contracts.Tickets;

namespace SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

public sealed class EscalateTicketRequestValidator : AbstractValidator<EscalateTicketRequest>
{
    public EscalateTicketRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty()
            .Must(reason => !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length >= 5)
            .WithMessage("Escalation reason must be at least 5 characters.")
            .MaximumLength(500);

        RuleFor(x => x.EscalatedBy)
            .MaximumLength(100)
            .When(x => x.EscalatedBy is not null);
    }
}

