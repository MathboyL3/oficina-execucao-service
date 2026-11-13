namespace Execucao.Domain.Estoque;

public enum TipoMovimentacao
{
    Entrada = 1,
    Saida = 2,
    Reserva = 3,
    EstornoReserva = 4,
    ConsumoReservado = 5,
    Perda = 99
}
