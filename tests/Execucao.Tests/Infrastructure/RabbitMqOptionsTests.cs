using Execucao.Infrastructure.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Execucao.Tests.Infrastructure;

/// <summary>
/// A factory do RabbitMQ é montada campo a campo (e não por URI): senha e vhost com
/// / # ? @ não podem derrubar a inicialização, e porta/vhost precisam ser respeitados.
/// </summary>
public class RabbitMqOptionsTests
{
    private static IConfiguration Config(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Factory_respeita_porta_vhost_e_senha_com_caracteres_de_uri()
    {
        var cfg = Config(new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = "rabbitmq",
            ["RabbitMq:Port"] = "5673",
            ["RabbitMq:VirtualHost"] = "oficina/execucao",
            ["RabbitMq:Username"] = "oficina",
            ["RabbitMq:Password"] = "senha/#?@perigosa"
        });

        var act = () => RabbitMqOptions.FromConfiguration(cfg).CreateConnectionFactory();

        var factory = act.Should().NotThrow().Subject;
        factory.HostName.Should().Be("rabbitmq");
        factory.Port.Should().Be(5673);
        factory.VirtualHost.Should().Be("oficina/execucao");
        factory.UserName.Should().Be("oficina");
        factory.Password.Should().Be("senha/#?@perigosa");
    }

    [Fact]
    public void Factory_usa_os_defaults_quando_as_chaves_estao_ausentes()
    {
        var factory = RabbitMqOptions.FromConfiguration(Config(new Dictionary<string, string?>())).CreateConnectionFactory();

        factory.HostName.Should().Be("localhost");
        factory.Port.Should().Be(5672);
        factory.VirtualHost.Should().Be("/");
        factory.UserName.Should().Be("guest");
        factory.Password.Should().Be("guest");
    }
}
