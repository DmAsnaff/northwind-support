using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Infrastructure.Data.Configurations;

/// <summary>
/// Maps <see cref="TicketEscalation"/> to the <c>TicketEscalations</c> table.
/// </summary>
public sealed class TicketEscalationConfiguration : IEntityTypeConfiguration<TicketEscalation>
{
    public void Configure(EntityTypeBuilder<TicketEscalation> builder)
    {
        builder.ToTable("TicketEscalations", t =>
        {
            t.HasCheckConstraint("CK_TicketEscalations_FromPriority", "[FromPriority] BETWEEN 1 AND 4");
            t.HasCheckConstraint("CK_TicketEscalations_ToPriority", "[ToPriority] BETWEEN 1 AND 4");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.TicketId).IsRequired();

        builder.Property(e => e.FromPriority).HasConversion<int>().IsRequired();

        builder.Property(e => e.ToPriority).HasConversion<int>().IsRequired();

        builder.Property(e => e.FromDueAtUtc).HasColumnType("datetime2(3)");

        builder.Property(e => e.ToDueAtUtc).HasColumnType("datetime2(3)").IsRequired();

        builder.Property(e => e.Reason).HasMaxLength(500).IsRequired();

        builder.Property(e => e.EscalatedBy).HasMaxLength(100).IsRequired();

        builder.Property(e => e.EscalatedAtUtc).HasColumnType("datetime2(3)").IsRequired();

        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(e => e.FromAgentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(e => e.ToAgentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite index with TicketId as the leading key: supports both FK lookups on TicketId
        // and the GET /api/tickets/{id}/escalations query ordered by EscalatedAtUtc DESC.
        builder.HasIndex(e => new { e.TicketId, e.EscalatedAtUtc })
            .HasDatabaseName("IX_TicketEscalations_TicketId_EscalatedAtUtc");
    }
}

