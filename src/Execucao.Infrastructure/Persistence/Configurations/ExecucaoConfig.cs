using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Execucao.Infrastructure.Persistence.Configurations;

public class ExecucaoConfig : IEntityTypeConfiguration<Domain.Execucoes.Execucao>
{
    public void Configure(EntityTypeBuilder<Domain.Execucoes.Execucao> b)
    {
        b.ToTable("execucoes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.ObservacoesDiagnostico).HasMaxLength(2000);
        b.Property(x => x.MotivoFalha).HasMaxLength(500);
        // Uma execução por OS: duas entregas concorrentes de EnfileirarExecucao não criam duas linhas.
        b.HasIndex(x => x.OrdemServicoId).IsUnique();
        b.HasIndex(x => new { x.Status, x.Prioridade, x.EnfileiradaEm });
        b.OwnsMany(x => x.Servicos, s =>
        {
            s.ToTable("execucao_servicos");
            s.WithOwner().HasForeignKey("ExecucaoId");
            s.Property<int>("Id");
            s.HasKey("Id");
        });
        b.Ignore(x => x.DomainEvents);
    }
}
