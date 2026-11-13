using Execucao.Domain.Estoque;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Execucao.Infrastructure.Persistence.Configurations;

public class EstoqueConfig : IEntityTypeConfiguration<EstoquePeca>
{
    public void Configure(EntityTypeBuilder<EstoquePeca> b)
    {
        b.ToTable("estoque_peca");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.PecaId).IsRequired();
        b.HasIndex(x => x.PecaId).IsUnique();
        b.Property(x => x.QuantidadeDisponivel).IsRequired();
        b.Property(x => x.QuantidadeReservada).IsRequired();
        b.Property(x => x.LimiteMinimo).IsRequired();

        // xmin: duas OSes disputando a última unidade — a segunda gravação falha com
        // DbUpdateConcurrencyException e o retry do MassTransit relê o saldo.
        b.Property<uint>("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        b.Ignore(x => x.DomainEvents);
        b.Ignore(x => x.Total);
        b.Ignore(x => x.EstoqueBaixo);

        b.HasMany(x => x.Movimentacoes)
            .WithOne()
            .HasForeignKey(m => m.EstoquePecaId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(x => x.Movimentacoes).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class MovimentacaoEstoqueConfig : IEntityTypeConfiguration<MovimentacaoEstoque>
{
    public void Configure(EntityTypeBuilder<MovimentacaoEstoque> b)
    {
        b.ToTable("estoque_movimentacoes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.EstoquePecaId).IsRequired();
        b.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.Quantidade).IsRequired();
        b.Property(x => x.OcorreuEm).IsRequired();
        b.Property(x => x.Referencia).HasMaxLength(200);
    }
}
