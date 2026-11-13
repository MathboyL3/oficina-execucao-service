using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Execucao.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Execucao.Tests.Integration;

public sealed class ExecucaoApiFactory : WebApplicationFactory<Program>
{
    public const string Secret = "segredo-de-teste-com-mais-de-32-caracteres!";
    private readonly string _db = $"execucao-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Secret", Secret);
        builder.ConfigureTestServices(s => s.AddDbContext<ExecucaoDbContext>(o => o.UseInMemoryDatabase(_db)));
    }

    public static string Token(string role)
    {
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken("Oficina.Api", "Oficina.Api",
            new[] { new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role) },
            DateTime.UtcNow, DateTime.UtcNow.AddMinutes(10), creds);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
