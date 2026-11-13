using System.Diagnostics.CodeAnalysis;
using Execucao.Application.Consumers;
using Execucao.Infrastructure.Persistence;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Execucao.Infrastructure.Messaging;

[ExcludeFromCodeCoverage]
public static class MessagingExtensions
{
    /// <summary>
    /// RabbitMQ + outbox transacional EF (publicações só saem após o SaveChanges)
    /// + inbox (idempotência por MessageId) + retry com intervalos.
    /// O retry também cobre o DbUpdateConcurrencyException do xmin do estoque: a nova
    /// tentativa abre escopo novo e relê o saldo.
    /// Em "Testing" usa o transporte em memória.
    /// </summary>
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration cfg, bool testing)
    {
        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            x.AddConsumers(typeof(ReservarPecasConsumer).Assembly);

            if (!testing)
            {
                x.AddEntityFrameworkOutbox<ExecucaoDbContext>(o =>
                {
                    o.UsePostgres();
                    o.UseBusOutbox();
                });
            }

            x.AddConfigureEndpointsCallback((ctx, _, ep) =>
            {
                ep.UseMessageRetry(r => r.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
                if (!testing) ep.UseEntityFrameworkOutbox<ExecucaoDbContext>(ctx);
            });

            if (testing)
            {
                x.UsingInMemory((ctx, c) => c.ConfigureEndpoints(ctx));
                return;
            }

            x.UsingRabbitMq((ctx, c) =>
            {
                var rabbit = RabbitMqOptions.FromConfiguration(cfg);
                c.Host(rabbit.Host, (ushort)rabbit.Port, rabbit.VirtualHost, h =>
                {
                    h.Username(rabbit.Username);
                    h.Password(rabbit.Password);
                });
                c.ConfigureEndpoints(ctx);
            });
        });
        return services;
    }
}
