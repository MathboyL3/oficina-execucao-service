using Execucao.Domain.Common;
using Execucao.Domain.Reservas;
using FluentAssertions;

namespace Execucao.Tests.Domain;

public class ReservaPecaTests
{
    [Fact]
    public void Criar_valida_quantidade_e_nasce_ativa()
    {
        var r = ReservaPeca.Criar(Guid.NewGuid(), Guid.NewGuid(), 2);
        r.Status.Should().Be(StatusReserva.Ativa);
        FluentActions.Invoking(() => ReservaPeca.Criar(Guid.NewGuid(), Guid.NewGuid(), 0))
            .Should().Throw<DomainException>();
        FluentActions.Invoking(() => ReservaPeca.Criar(Guid.Empty, Guid.NewGuid(), 1))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void Consumir_e_Liberar_so_a_partir_de_ativa()
    {
        var r = ReservaPeca.Criar(Guid.NewGuid(), Guid.NewGuid(), 1);
        r.Consumir();
        r.Status.Should().Be(StatusReserva.Consumida);
        FluentActions.Invoking(r.Liberar).Should().Throw<DomainException>();

        var r2 = ReservaPeca.Criar(Guid.NewGuid(), Guid.NewGuid(), 1);
        r2.Liberar();
        r2.Status.Should().Be(StatusReserva.Liberada);
        FluentActions.Invoking(r2.Consumir).Should().Throw<DomainException>();
    }
}
