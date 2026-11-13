using Execucao.Domain.Catalogo;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Execucao.Infrastructure.Persistence.Configurations;

public class ServicoConfig : IEntityTypeConfiguration<Servico>
{
    public void Configure(EntityTypeBuilder<Servico> b)
    {
        b.ToTable("servicos");
        b.HasKey(x => x.Id);
        b.Property(x => x.Nome).HasMaxLength(200).IsRequired();
        b.Property(x => x.Descricao).HasMaxLength(1000);
        b.Property(x => x.TempoEstimado).IsRequired();
        b.Property(x => x.Ativo).IsRequired();
        b.OwnsOne(x => x.ValorBase, m =>
        {
            m.Property(p => p.Amount).HasColumnName("valor_base_amount").HasColumnType("numeric(18,2)").IsRequired();
            m.Property(p => p.Currency).HasColumnName("valor_base_currency").HasMaxLength(3).IsRequired();
        });
    }
}

public class PecaConfig : IEntityTypeConfiguration<Peca>
{
    public void Configure(EntityTypeBuilder<Peca> b)
    {
        b.ToTable("pecas");
        b.HasKey(x => x.Id);
        b.Property(x => x.Nome).HasMaxLength(200).IsRequired();
        b.Property(x => x.Codigo).HasMaxLength(50).IsRequired();
        b.HasIndex(x => x.Codigo).IsUnique();
        b.Property(x => x.Unidade).HasMaxLength(20).IsRequired();
        b.Property(x => x.Ativo).IsRequired();
        b.OwnsOne(x => x.Valor, m =>
        {
            m.Property(p => p.Amount).HasColumnName("valor_amount").HasColumnType("numeric(18,2)").IsRequired();
            m.Property(p => p.Currency).HasColumnName("valor_currency").HasMaxLength(3).IsRequired();
        });
    }
}
