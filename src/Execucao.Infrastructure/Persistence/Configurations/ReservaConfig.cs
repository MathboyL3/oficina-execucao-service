using Execucao.Domain.Reservas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Execucao.Infrastructure.Persistence.Configurations;

public class ReservaConfig : IEntityTypeConfiguration<ReservaPeca>
{
    public void Configure(EntityTypeBuilder<ReservaPeca> b)
    {
        b.ToTable("reservas_peca");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.HasIndex(x => x.OrdemServicoId);
        b.Ignore(x => x.DomainEvents);
    }
}

public class LiberacaoOsConfig : IEntityTypeConfiguration<LiberacaoOs>
{
    public void Configure(EntityTypeBuilder<LiberacaoOs> b)
    {
        b.ToTable("liberacoes_os");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.OrdemServicoId).IsRequired();
        b.Property(x => x.LiberadaEm).IsRequired();
        // Uma liberação por OS: mensagem duplicada não cria duas linhas.
        b.HasIndex(x => x.OrdemServicoId).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}
